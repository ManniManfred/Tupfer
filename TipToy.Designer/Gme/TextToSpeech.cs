using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace TipToy.Designer.Gme;

/// <summary>
/// Erzeugt Sprachausgaben für die GME-Datei. Wie tttool werden pico2wave, espeak oder (macOS)
/// say genutzt; unter Windows die eingebaute Sprachausgabe. Ist oggenc vorhanden, wird nach
/// Ogg/Vorbis umgewandelt, sonst wird die WAV-Datei direkt eingebettet (der Stift spielt beides).
/// Ergebnisse werden in einem Cache-Verzeichnis abgelegt.
/// </summary>
public class TextToSpeech
{
    /// <summary>
    /// Eingebaute Sprachausgabe (Sprache, Text → WAV-Daten), ersetzt die externen Programme.
    /// Wird im Browser gesetzt, wo keine Programme gestartet werden können.
    /// </summary>
    public static Func<string, string, byte[]>? Synthesizer { get; set; }

    /// <summary>Wird mit der Sprache vor dem Bauen einer GME-Datei mit Texten aufgerufen, z. B. um die Sprachausgabe zu laden.</summary>
    public static Func<string, Task>? PrepareAsync { get; set; }

    readonly string _cacheDir;
    readonly Action<string> _log;
    bool _warnedNoOgg;

    public TextToSpeech(string cacheDir, Action<string> log)
    {
        _cacheDir = cacheDir;
        _log = log;
    }

    public byte[] Speak(string language, string text)
    {
        Directory.CreateDirectory(_cacheDir);
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(language + "\n" + text)))[..16].ToLowerInvariant();
        var ogg = Path.Combine(_cacheDir, $"tts-{key}-{language}.ogg");
        var wav = Path.Combine(_cacheDir, $"tts-{key}-{language}.wav");
        if (Synthesizer is { } synthesize)
        {
            if (File.Exists(wav))
                return File.ReadAllBytes(wav);
            _log($"Spreche „{text}“");
            var data = synthesize(language, text);
            File.WriteAllBytes(wav, data);
            return data;
        }
        if (File.Exists(ogg))
            return File.ReadAllBytes(ogg);
        if (File.Exists(wav) && !CanRun("oggenc"))
            return File.ReadAllBytes(wav);

        _log($"Spreche „{text}“");
        var tmp = Path.Combine(_cacheDir, $"tmp-{key}.wav");
        try
        {
            if (!Synthesize(language, text, tmp) || !File.Exists(tmp) || new FileInfo(tmp).Length <= 44)
                throw new InvalidOperationException(
                    "Keine Sprachausgabe gefunden. Bitte pico2wave (libttspico-utils) oder espeak installieren " +
                    "oder Audiodateien im Medienverzeichnis ablegen.");

            if (Run("oggenc", "-Q", "-o", ogg, tmp))
                return File.ReadAllBytes(ogg);

            if (!_warnedNoOgg)
            {
                _log("Hinweis: oggenc (vorbis-tools) nicht gefunden – Sprachausgaben werden als WAV eingebettet (größere GME-Datei).");
                _warnedNoOgg = true;
            }
            File.Copy(tmp, wav, true);
            return File.ReadAllBytes(wav);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    bool Synthesize(string lang, string text, string wavPath)
    {
        if (OperatingSystem.IsWindows() && WindowsSpeech.TrySpeak(lang, text, wavPath))
            return true;

        var picoLang = lang switch { "en" => "en-GB", "de" => "de-DE", "fr" => "fr-FR", "es" => "es-ES", "it" => "it-IT", _ => lang };
        if (Run("pico2wave", "--wave", wavPath, "--lang", picoLang, text))
            return true;
        if (Run("espeak", "-v", lang, "-w", wavPath, "-s", "120", text))
            return true;
        if (Run("espeak-ng", "-v", lang, "-w", wavPath, "-s", "120", text))
            return true;
        if (OperatingSystem.IsMacOS())
        {
            var voice = lang switch { "en" => "Alex", "de" => "Anna", "fr" => "Thomas", _ => null };
            var args = new List<string> { "-o", wavPath, "--data-format=LEI16@22050" };
            if (voice != null)
                args.AddRange(new[] { "-v", voice });
            args.Add(text);
            if (Run("say", args.ToArray()))
                return true;
        }
        return false;
    }

    static bool CanRun(string program)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(program, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit();
            return p != null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Startet ein Programm; false, wenn es nicht existiert oder fehlschlägt.</summary>
    static bool Run(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p == null)
                return false;
            p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
