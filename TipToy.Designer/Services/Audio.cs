using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TipToy.Designer.Services;

/// <summary>Abspielen und Aufnehmen von Audio.</summary>
public interface IAudioDevice
{
    /// <summary>
    /// Spielt Audiodaten ab (Dateiendung wie ".wav"); eine laufende Wiedergabe wird beendet.
    /// Ist fertig, wenn die Wiedergabe zu Ende ist oder abgebrochen wurde.
    /// </summary>
    Task PlayAsync(byte[] data, string extension);

    /// <summary>Beendet eine laufende Wiedergabe.</summary>
    void StopPlayback();

    /// <summary>Startet eine Aufnahme vom Mikrofon.</summary>
    Task StartRecordingAsync();

    /// <summary>Beendet die Aufnahme und liefert sie als WAV (mono, 16 Bit).</summary>
    Task<byte[]> StopRecordingAsync();
}

public static class Audio
{
    /// <summary>Wird im Browser ersetzt.</summary>
    public static IAudioDevice Device { get; set; } = new DesktopAudio();

    public const int SampleRate = 22050;

    /// <summary>Dateiendung anhand des Dateianfangs (OGG, FLAC, MP3 oder sonst WAV).</summary>
    public static string ExtensionOf(byte[] data)
    {
        if (data.Length < 4)
            return ".wav";
        var magic = Encoding.ASCII.GetString(data, 0, 4);
        if (magic == "OggS")
            return ".ogg";
        if (magic == "fLaC")
            return ".flac";
        if (magic.StartsWith("ID3") || (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0))
            return ".mp3";
        return ".wav";
    }

    /// <summary>Erzeugt eine WAV-Datei aus 16-Bit-Samples (mono).</summary>
    public static byte[] Wav(ReadOnlySpan<short> samples, int sampleRate = SampleRate)
    {
        var data = new byte[44 + samples.Length * 2];
        WriteHeader(data, sampleRate, samples.Length * 2);
        MemoryMarshal.AsBytes(samples).CopyTo(data.AsSpan(44));
        return data;
    }

    static void WriteHeader(byte[] d, int sampleRate, int dataLength)
    {
        Encoding.ASCII.GetBytes("RIFF").CopyTo(d, 0);
        BitConverter.TryWriteBytes(d.AsSpan(4), 36 + dataLength);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(d, 8);
        BitConverter.TryWriteBytes(d.AsSpan(16), 16);
        BitConverter.TryWriteBytes(d.AsSpan(20), (short)1);          // PCM
        BitConverter.TryWriteBytes(d.AsSpan(22), (short)1);          // mono
        BitConverter.TryWriteBytes(d.AsSpan(24), sampleRate);
        BitConverter.TryWriteBytes(d.AsSpan(28), sampleRate * 2);
        BitConverter.TryWriteBytes(d.AsSpan(32), (short)2);
        BitConverter.TryWriteBytes(d.AsSpan(34), (short)16);
        Encoding.ASCII.GetBytes("data").CopyTo(d, 36);
        BitConverter.TryWriteBytes(d.AsSpan(40), dataLength);
    }

    /// <summary>
    /// Korrigiert die Längenangaben einer WAV-Datei, deren Aufnahmeprogramm abgebrochen wurde
    /// (der Kopf enthält dann oft 0 oder die Maximallänge).
    /// </summary>
    public static byte[] FixWavLengths(byte[] wav)
    {
        if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            return wav;
        BitConverter.TryWriteBytes(wav.AsSpan(4), wav.Length - 8);
        for (int pos = 12; pos + 8 <= wav.Length;)
        {
            var id = Encoding.ASCII.GetString(wav, pos, 4);
            int size = BitConverter.ToInt32(wav, pos + 4);
            if (id == "data")
            {
                BitConverter.TryWriteBytes(wav.AsSpan(pos + 4), wav.Length - pos - 8);
                break;
            }
            if (size < 0 || pos + 8 + size > wav.Length)
                break;
            pos += 8 + size + (size & 1);
        }
        return wav;
    }

    /// <summary>Dauer einer WAV-Datei in Sekunden (ungefähr, aus der Dateigröße).</summary>
    public static double WavSeconds(byte[] wav) =>
        wav.Length > 44 ? (wav.Length - 44) / (double)Math.Max(1, BitConverter.ToInt32(wav, 28)) : 0;
}

/// <summary>
/// Desktop: Wiedergabe und Aufnahme unter Windows über MCI (winmm), sonst über externe Programme
/// (paplay/aplay/ffplay/afplay bzw. arecord/sox/ffmpeg).
/// </summary>
public class DesktopAudio : IAudioDevice
{
    Process? _player;
    Process? _recorder;
    string? _recordFile;

    static string TempFile(string name) => Path.Combine(Path.GetTempPath(), $"tiptoy-{Environment.ProcessId}-{name}");

    /// <summary>Zählt die Wiedergaben, damit eine beendete nicht mehr auf ihr Ende wartet.</summary>
    int _playId;

    public async Task PlayAsync(byte[] data, string extension)
    {
        StopPlayback();
        int id = _playId;
        var file = TempFile("play" + extension.ToLowerInvariant());
        File.WriteAllBytes(file, data);
        if (OperatingSystem.IsWindows())
        {
            Mci($"open \"{file}\" alias tiptoyplay");
            Mci("play tiptoyplay");
            while (id == _playId && MciStatus("status tiptoyplay mode") == "playing")
                await Task.Delay(100);
            return;
        }
        bool wav = extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);
        var players = new List<string[]>();
        if (OperatingSystem.IsMacOS())
            players.Add(["afplay", file]);
        if (wav)
            players.AddRange([["paplay", file], ["aplay", "-q", file]]);
        players.Add(["ffplay", "-nodisp", "-autoexit", "-loglevel", "quiet", file]);
        if (!wav)
            players.Add(["paplay", file]);
        var player = _player = StartFirst(players)
            ?? throw new InvalidOperationException("Kein Programm zum Abspielen gefunden (paplay, aplay, ffplay oder afplay).");
        await player.WaitForExitAsync();
    }

    public void StopPlayback()
    {
        _playId++;
        if (OperatingSystem.IsWindows())
        {
            Mci("close tiptoyplay", throwOnError: false);
            return;
        }
        try
        {
            if (_player is { HasExited: false })
                _player.Kill();
        }
        catch (InvalidOperationException)
        {
        }
        _player = null;
    }

    public Task StartRecordingAsync()
    {
        _recordFile = TempFile("aufnahme.wav");
        if (File.Exists(_recordFile))
            File.Delete(_recordFile);
        if (OperatingSystem.IsWindows())
        {
            Mci("open new type waveaudio alias tiptoyrec");
            Mci($"set tiptoyrec bitspersample 16 channels 1 samplespersec {Audio.SampleRate} alignment 2 bytespersec {Audio.SampleRate * 2}");
            Mci("record tiptoyrec");
            return Task.CompletedTask;
        }
        string rate = Audio.SampleRate.ToString();
        var recorders = new List<string[]>
        {
            new[] { "arecord", "-q", "-f", "S16_LE", "-r", rate, "-c", "1", _recordFile },
            new[] { "rec", "-q", "-c", "1", "-r", rate, "-b", "16", _recordFile },
        };
        if (OperatingSystem.IsMacOS())
            recorders.Add(["ffmpeg", "-loglevel", "quiet", "-f", "avfoundation", "-i", ":0", "-ac", "1", "-ar", rate, _recordFile]);
        else
            recorders.Add(["ffmpeg", "-loglevel", "quiet", "-f", "pulse", "-i", "default", "-ac", "1", "-ar", rate, _recordFile]);
        _recorder = StartFirst(recorders)
            ?? throw new InvalidOperationException("Kein Aufnahmeprogramm gefunden (arecord, sox oder ffmpeg).");
        return Task.CompletedTask;
    }

    public async Task<byte[]> StopRecordingAsync()
    {
        var file = _recordFile ?? throw new InvalidOperationException("Es läuft keine Aufnahme.");
        _recordFile = null;
        if (OperatingSystem.IsWindows())
        {
            Mci("stop tiptoyrec");
            Mci($"save tiptoyrec \"{file}\"");
            Mci("close tiptoyrec");
        }
        else if (_recorder is { } recorder)
        {
            _recorder = null;
            // Mit SIGINT beenden die Programme die Datei sauber; notfalls hart abbrechen
            try
            {
                using (var kill = Process.Start("kill", ["-INT", recorder.Id.ToString()]))
                    await kill.WaitForExitAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await recorder.WaitForExitAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is Win32Exception or OperationCanceledException or InvalidOperationException)
            {
                if (!recorder.HasExited)
                    recorder.Kill();
            }
        }
        if (!File.Exists(file))
            throw new InvalidOperationException("Die Aufnahme ist leer – ist ein Mikrofon angeschlossen?");
        var wav = Audio.FixWavLengths(await File.ReadAllBytesAsync(file));
        File.Delete(file);
        return wav;
    }

    /// <summary>Startet das erste vorhandene Programm (Name und Argumente).</summary>
    static Process? StartFirst(IEnumerable<string[]> commands)
    {
        foreach (var command in commands)
        {
            var psi = new ProcessStartInfo(command[0])
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in command.Skip(1))
                psi.ArgumentList.Add(arg);
            try
            {
                var p = Process.Start(psi);
                if (p == null)
                    continue;
                p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEndAsync();
                return p;
            }
            catch (Win32Exception)
            {
                // Programm nicht installiert: nächstes versuchen
            }
        }
        return null;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern bool mciGetErrorString(int error, StringBuilder text, int length);

    /// <summary>Abfrage über MCI, z. B. „status … mode“; bei Fehlern leer.</summary>
    static string MciStatus(string command)
    {
        var result = new StringBuilder(64);
        return mciSendString(command, result, result.Capacity, IntPtr.Zero) == 0 ? result.ToString() : "";
    }

    static void Mci(string command, bool throwOnError = true)
    {
        int error = mciSendString(command, null, 0, IntPtr.Zero);
        if (error == 0 || !throwOnError)
            return;
        var text = new StringBuilder(256);
        mciGetErrorString(error, text, text.Capacity);
        throw new InvalidOperationException($"Audio (MCI): {text}");
    }
}
