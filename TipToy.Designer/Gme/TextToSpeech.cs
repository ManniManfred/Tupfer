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

    /// <summary>Lädt die eingebaute Sprachausgabe für die Sprache (vor <see cref="Synthesizer"/>).</summary>
    public static Func<string, Task>? LoadAsync { get; set; }

    /// <summary>
    /// Natürlich klingende Sprachausgabe (Stimme aus <see cref="Voices"/>, Text → WAV-Daten), asynchron.
    /// Wird in <see cref="PrepareAsync"/> vorab für alle Texte benutzt; wirft sie (z. B. kein Netz),
    /// spricht <see cref="Synthesizer"/>.
    /// </summary>
    public static Func<string, string, Task<byte[]>>? NaturalSynthesizer { get; set; }

    /// <summary>Stimmen, die in dieser Sitzung nicht gingen, und schon geladene Stimmen.</summary>
    static readonly HashSet<string> NaturalFailed = new();
    static readonly HashSet<string> NaturalLoaded = new();

    readonly string _cacheDir;
    readonly Action<string> _log;
    readonly Action<string> _notice;
    bool _warnedNoOgg;

    /// <param name="notice">Für Meldungen, die sichtbar sein sollen (z. B. langes Laden); Standard: <paramref name="log"/>.</param>
    public TextToSpeech(string cacheDir, Action<string> log, Action<string>? notice = null)
    {
        _cacheDir = cacheDir;
        _log = log;
        _notice = notice ?? log;
    }

    /// <summary>Datei im Cache; <paramref name="speechKey"/> wie <see cref="Voices.SpeechKey(string?, string?, string?)"/>.</summary>
    string CachePath(string speechKey, string text, string suffix)
    {
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(speechKey + "\n" + text)))[..16].ToLowerInvariant();
        return Path.Combine(_cacheDir, $"tts-{key}-{Voices.Parse(speechKey).Language}{suffix}");
    }

    /// <summary>
    /// Vor <see cref="Speak"/> aufrufen: lädt die eingebaute Sprachausgabe und spricht die Texte, wenn
    /// möglich, mit der natürlichen Stimme vor (Ergebnisse landen im Cache). Einträge: Schlüssel wie
    /// <see cref="Voices.SpeechKey(string?, string?, string?)"/> und Text.
    /// </summary>
    public async Task PrepareAsync(IEnumerable<(string SpeechKey, string Text)> items)
    {
        var todo = items.Where(t => !string.IsNullOrWhiteSpace(t.Text)).Distinct().ToList();
        if (LoadAsync is { } load)
            foreach (var language in todo.Select(t => Voices.Parse(t.SpeechKey).Language).Distinct())
                await load(language);
        if (NaturalSynthesizer is not { } natural)
            return;
        Directory.CreateDirectory(_cacheDir);
        foreach (var (speechKey, text) in todo)
        {
            var (language, chosen) = Voices.Parse(speechKey);
            if ((chosen ?? Voices.Default(language)?.Id) is not { } voice || NaturalFailed.Contains(voice))
                continue;
            var path = CachePath(speechKey, text, "-natural.wav");
            if (File.Exists(path))
                continue;
            if (NaturalLoaded.Add(voice))
                _notice($"Lade die Stimme „{Voices.All.FirstOrDefault(v => v.Id == voice)?.Name ?? voice}“ – beim ersten Mal 20–110 MB, das kann etwas dauern …");
            try
            {
                _log($"Spreche „{text}“");
                await File.WriteAllBytesAsync(path, await natural(voice, text));
            }
            catch (Exception ex)
            {
                NaturalLoaded.Remove(voice);
                NaturalFailed.Add(voice);
                _notice($"Natürliche Stimme nicht verfügbar ({ex.Message}) – es spricht die einfache Stimme.");
            }
        }
    }

    /// <param name="speechKey">Sprache, ggf. mit Stimme (<see cref="Voices.SpeechKey(string?, string?, string?)"/>).</param>
    public byte[] Speak(string speechKey, string text)
    {
        var natural = CachePath(speechKey, text, "-natural.wav");
        if (File.Exists(natural))
            return File.ReadAllBytes(natural);
        // Ohne natürliche Stimme zählt nur die Sprache
        var language = Voices.Parse(speechKey).Language;
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
