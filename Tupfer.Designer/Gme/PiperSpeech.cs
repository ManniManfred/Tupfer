using System.Buffers.Binary;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Tupfer.Designer.Gme;

/// <summary>
/// Natürliche Sprachausgabe mit Piper für die Desktop-Version, genauso wie im Browser
/// (wwwroot/tupfer.js): Lautschrift mit piper_phonemize (espeak-ng), Stimme mit ONNX Runtime,
/// Stimmen aus piper-voices auf Hugging Face. piper_phonemize und die Stimmen werden beim ersten
/// Gebrauch geladen und im Benutzerverzeichnis abgelegt (…/Tupfer/piper).
/// </summary>
public static class PiperSpeech
{
    const string PhonemizeRelease = "2023.11.14-4";
    const string PhonemizeUrl = "https://github.com/rhasspy/piper-phonemize/releases/download/" + PhonemizeRelease + "/";
    const string VoicesUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/main/";
    const int MaxVoices = 3;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    static readonly SemaphoreSlim DownloadLock = new(1, 1);

    /// <summary>Geladene Stimmen (Modell → Konfiguration und Sitzung), zuletzt benutzte am Ende.</summary>
    static readonly List<(string Model, JsonNode Config, InferenceSession Session)> Loaded = new();
    static readonly object LoadedLock = new();

    /// <summary>Ablage für piper_phonemize und die Stimmen.</summary>
    public static string DataDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "Tupfer", "piper");

    /// <summary>Spricht den Text (Stimme wie in <see cref="Voices"/>, bei mehreren Sprechern „…#sprecher“), liefert WAV (mono, 16 Bit).</summary>
    public static async Task<byte[]> SpeakAsync(string voiceId, string text)
    {
        var parts = voiceId.Split('#', 2);
        var (config, session) = await UseVoiceAsync(parts[0]);
        var ids = await PhonemizeAsync(config["espeak"]?["voice"]?.GetValue<string>() ?? parts[0].Split('_')[0], text.Trim());
        var inference = config["inference"];
        float Scale(string name, float standard) => inference?[name]?.GetValue<float>() ?? standard;
        long speaker = 0;
        if ((config["num_speakers"]?.GetValue<int>() ?? 1) > 1 && parts.Length > 1)
            speaker = config["speaker_id_map"]?[parts[1]]?.GetValue<long>() ?? 0;
        var sampleRate = config["audio"]?["sample_rate"]?.GetValue<int>() ?? 22050;
        var samples = await Task.Run(() => Infer(session, ids, speaker, Scale("noise_scale", 0.667f), Scale("length_scale", 1f), Scale("noise_w", 0.8f)));
        return WavFromSamples(samples, sampleRate);
    }

    static float[] Infer(InferenceSession session, long[] ids, long speaker, float noiseScale, float lengthScale, float noiseW)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<long>(ids, [1, ids.Length])),
            NamedOnnxValue.CreateFromTensor("input_lengths", new DenseTensor<long>(new[] { (long)ids.Length }, [1])),
            NamedOnnxValue.CreateFromTensor("scales", new DenseTensor<float>(new[] { noiseScale, lengthScale, noiseW }, [3])),
        };
        if (session.InputMetadata.ContainsKey("sid"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("sid", new DenseTensor<long>(new[] { speaker }, [1])));
        using var results = session.Run(inputs);
        return results.First(r => r.Name == "output").AsEnumerable<float>().ToArray();
    }

    /// <summary>Pfad in piper-voices ohne Endung, z. B. de_DE-kerstin-low → de/de_DE/kerstin/low/de_DE-kerstin-low.</summary>
    public static string VoicePath(string model)
    {
        var parts = model.Split('-');
        if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty))
            throw new ArgumentException($"Unbekannte Stimme „{model}“.");
        return $"{parts[0].Split('_')[0]}/{parts[0]}/{parts[1]}/{parts[2]}/{model}";
    }

    /// <summary>Lädt eine Stimme (beim ersten Mal aus dem Netz); behält nur die zuletzt benutzten im Speicher.</summary>
    static async Task<(JsonNode Config, InferenceSession Session)> UseVoiceAsync(string model)
    {
        lock (LoadedLock)
        {
            int i = Loaded.FindIndex(v => v.Model == model);
            if (i >= 0)
            {
                var found = Loaded[i];
                Loaded.RemoveAt(i);
                Loaded.Add(found);
                return (found.Config, found.Session);
            }
        }
        var path = VoicePath(model);
        var dir = Path.Combine(DataDir, "voices");
        var onnx = Path.Combine(dir, model + ".onnx");
        var json = onnx + ".json";
        await DownloadAsync(VoicesUrl + path + ".onnx.json", json);
        await DownloadAsync(VoicesUrl + path + ".onnx", onnx);
        var config = JsonNode.Parse(await File.ReadAllTextAsync(json)) ?? throw new InvalidDataException($"{json} ist leer.");
        var session = await Task.Run(() => new InferenceSession(onnx));
        lock (LoadedLock)
        {
            Loaded.Add((model, config, session));
            while (Loaded.Count > MaxVoices)
            {
                // Gesprochen wird nacheinander, die älteste Sitzung ist also gerade nicht in Gebrauch
                Loaded[0].Session.Dispose();
                Loaded.RemoveAt(0);
            }
        }
        return (config, session);
    }

    /// <summary>Lädt eine Datei, wenn sie noch nicht da ist (erst in eine temporäre Datei, dann umbenannt).</summary>
    static async Task DownloadAsync(string url, string target)
    {
        if (File.Exists(target))
            return;
        await DownloadLock.WaitAsync();
        try
        {
            if (File.Exists(target))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var tmp = target + ".part";
            try
            {
                using (var response = await SendAsync(url))
                await using (var file = File.Create(tmp))
                    await response.Content.CopyToAsync(file);
                File.Move(tmp, target, true);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
        }
        finally
        {
            DownloadLock.Release();
        }
    }

    static async Task<HttpResponseMessage> SendAsync(string url)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException($"{new Uri(url).Host} ist nicht erreichbar");
        }
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new InvalidOperationException($"{url}: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        return response;
    }

    // ---------- Lautschrift mit piper_phonemize ----------

    /// <summary>Paket von piper-phonemize für dieses System.</summary>
    static string PhonemizePackage()
    {
        var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsWindows())
            return "piper-phonemize_windows_amd64.zip"; // läuft auf Windows für ARM emuliert
        if (OperatingSystem.IsMacOS())
            return arm ? "piper-phonemize_macos_aarch64.tar.gz" : "piper-phonemize_macos_x64.tar.gz";
        if (OperatingSystem.IsLinux())
            return arm ? "piper-phonemize_linux_aarch64.tar.gz" : "piper-phonemize_linux_x86_64.tar.gz";
        throw new PlatformNotSupportedException("Für dieses System gibt es keine natürliche Sprachausgabe.");
    }

    /// <summary>Lädt und entpackt piper_phonemize (einmalig); liefert Programm und Verzeichnis mit espeak-ng-data.</summary>
    static async Task<(string Program, string EspeakData, string[] LibraryDirs)> PhonemizerAsync()
    {
        var dir = Path.Combine(DataDir, "phonemize-" + PhonemizeRelease);
        if (!Directory.Exists(dir))
        {
            await DownloadLock.WaitAsync();
            try
            {
                if (!Directory.Exists(dir))
                {
                    var package = PhonemizePackage();
                    var tmp = dir + ".part";
                    if (Directory.Exists(tmp))
                        Directory.Delete(tmp, true);
                    Directory.CreateDirectory(tmp);
                    try
                    {
                        using (var response = await SendAsync(PhonemizeUrl + package))
                        await using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            if (package.EndsWith(".zip"))
                            {
                                // ZipFile braucht einen durchsuchbaren Datenstrom
                                var buffer = new MemoryStream();
                                await stream.CopyToAsync(buffer);
                                buffer.Position = 0;
                                ZipFile.ExtractToDirectory(buffer, tmp, true);
                            }
                            else
                            {
                                await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
                                await TarFile.ExtractToDirectoryAsync(gzip, tmp, true);
                            }
                        }
                        Directory.Move(tmp, dir);
                    }
                    finally
                    {
                        if (Directory.Exists(tmp))
                            Directory.Delete(tmp, true);
                    }
                }
            }
            finally
            {
                DownloadLock.Release();
            }
        }

        var name = OperatingSystem.IsWindows() ? "piper_phonemize_exe.exe" : "piper_phonemize";
        var program = Directory.EnumerateFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException($"{name} fehlt in {dir}.");
        var data = Directory.EnumerateDirectories(dir, "espeak-ng-data", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new DirectoryNotFoundException($"espeak-ng-data fehlt in {dir}.");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(program, File.GetUnixFileMode(program) | UnixFileMode.UserExecute);
        var root = Path.GetDirectoryName(Path.GetDirectoryName(program)!)!;
        return (program, data, [Path.GetDirectoryName(program)!, Path.Combine(root, "lib")]);
    }

    /// <summary>Wandelt Text in Lautschrift-IDs (espeak-ng), wie piper_phonemize im Browser.</summary>
    static async Task<long[]> PhonemizeAsync(string espeakVoice, string text)
    {
        var (program, data, libraryDirs) = await PhonemizerAsync();
        var psi = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-l", espeakVoice, "--espeak_data", data })
            psi.ArgumentList.Add(arg);
        // Die Bibliotheken liegen neben dem Programm bzw. in …/lib
        foreach (var variable in new[] { "PATH", "LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH" })
        {
            var old = psi.Environment.TryGetValue(variable, out var value) ? value : null;
            psi.Environment[variable] = string.Join(Path.PathSeparator, libraryDirs.Append(old).Where(d => !string.IsNullOrEmpty(d)));
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"{program} startet nicht.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        // Eine Zeile je Text; Zeilenumbrüche im Text würden ihn teilen
        await process.StandardInput.WriteLineAsync(text.ReplaceLineEndings(" "));
        process.StandardInput.Close();
        await process.WaitForExitAsync();
        var line = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (process.ExitCode != 0 || line == null)
            throw new InvalidOperationException("Lautschrift fehlgeschlagen: " + ((await error).Trim() is { Length: > 0 } e ? e : $"Code {process.ExitCode}"));
        return JsonNode.Parse(line)?["phoneme_ids"]?.AsArray().Select(id => id!.GetValue<long>()).ToArray()
            ?? throw new InvalidDataException("Lautschrift ohne phoneme_ids.");
    }

    /// <summary>WAV-Datei (mono, 16 Bit) aus Abtastwerten zwischen -1 und 1.</summary>
    public static byte[] WavFromSamples(float[] samples, int sampleRate)
    {
        var wav = new byte[44 + samples.Length * 2];
        void Text(int pos, string s) => Encoding.ASCII.GetBytes(s).CopyTo(wav, pos);
        void U16(int pos, int v) => BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(pos), (ushort)v);
        void U32(int pos, int v) => BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(pos), (uint)v);
        Text(0, "RIFF"); U32(4, 36 + samples.Length * 2); Text(8, "WAVEfmt ");
        U32(16, 16); U16(20, 1); U16(22, 1);
        U32(24, sampleRate); U32(28, sampleRate * 2);
        U16(32, 2); U16(34, 16);
        Text(36, "data"); U32(40, samples.Length * 2);
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + i * 2), (short)(Math.Clamp(samples[i], -1f, 1f) * 0x7fff));
        return wav;
    }
}
