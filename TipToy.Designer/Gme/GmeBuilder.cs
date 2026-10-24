using System.Text;
using TipToy.Designer.Model;
using TipToy.Designer.Services;

namespace TipToy.Designer.Gme;

public class GmeBuildException : Exception
{
    public GmeBuildException(string message) : base(message) { }
}

/// <summary>
/// Baut eine GME-Datei aus einem Projekt, ohne tttool. Entspricht "tttool assemble"
/// (ttYaml2tt in TipToiYaml.hs) für Projekte ohne Spiele und Timer.
/// </summary>
public static class GmeBuilder
{
    public const int MaxCommentLength = 49;

    public static readonly string[] MediaExtensions = { ".wav", ".ogg", ".flac", ".mp3" };

    /// <param name="project">Projekt; fehlende Skriptcodes werden dabei vergeben.</param>
    /// <param name="baseDir">Verzeichnis, relativ zu dem das Medienverzeichnis gesucht wird.</param>
    /// <param name="speak">Liefert die Audiodaten für einen Text (Sprache, Text).</param>
    /// <param name="date">Datum für den Dateikopf; null = heute.</param>
    /// <param name="warn">Erhält Warnungen (z. B. zu lange Skriptzeilen).</param>
    public static byte[] Build(ProjectFile project, string baseDir, Func<string, string, byte[]> speak, DateTime? date = null, Action<string>? warn = null)
    {
        if (!string.IsNullOrWhiteSpace(project.ExtraYaml))
            throw new GmeBuildException(
                "Das Projekt enthält weitere YAML-Einträge (z. B. Spiele oder Timer), die der eingebaute GME-Builder " +
                "nicht unterstützt. Bitte unter Projekt „GME mit externem tttool bauen“ aktivieren.");

        var duplicate = project.Scripts.GroupBy(s => s.Name).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new GmeBuildException($"Der Skriptname „{duplicate.Key}“ ist mehrfach vergeben.");
        if (project.Scripts.Count == 0)
            throw new GmeBuildException("Das Projekt enthält keine Skripte.");

        int numeric = project.Scripts.Count(s => int.TryParse(s.Name, out _));
        if (numeric > 0 && numeric < project.Scripts.Count)
            throw new GmeBuildException("Skriptnamen dürfen nicht teils Zahlen, teils Namen sein (wie bei tttool).");

        CodeAssigner.Assign(project);
        var codeOf = project.Scripts.ToDictionary(s => s.Name, s => (ushort)s.Code!.Value);

        // Skripte parsen
        var parsed = new SortedDictionary<ushort, List<ScriptLine>>();
        foreach (var s in project.Scripts)
        {
            var lines = new List<ScriptLine>();
            int n = 0;
            foreach (var raw in s.Lines)
            {
                n++;
                try
                {
                    lines.Add(ScriptParser.ParseLine(raw));
                }
                catch (ScriptParseException ex)
                {
                    throw new GmeBuildException($"Skript „{s.Name}“, Zeile {n}: {ex.Message}");
                }
                if (lines[^1].Commands.Count > 8)
                    warn?.Invoke($"Skript „{s.Name}“, Zeile {n}: Der Stift führt nur die ersten 8 Befehle einer Zeile aus, " +
                                 $"die übrigen {lines[^1].Commands.Count - 8} werden ignoriert.");
            }
            if (!parsed.TryAdd(codeOf[s.Name], lines))
                throw new GmeBuildException($"Der Code {codeOf[s.Name]} ist mehreren Skripten zugeordnet.");
        }

        List<List<string>> welcome;
        List<(Register Reg, ushort Value)> init;
        try
        {
            welcome = string.IsNullOrWhiteSpace(project.Welcome)
                ? new List<List<string>>()
                : new List<List<string>> { ScriptParser.ParsePlayList(project.Welcome) };
            init = ScriptParser.ParseInit(project.Init ?? "");
        }
        catch (ScriptParseException ex)
        {
            throw new GmeBuildException($"Begrüßung/Initialisierung: {ex.Message}");
        }

        // Audiodateien: sortiert und eindeutig, wie Data.Set in tttool
        var mediaNames = parsed.Values.SelectMany(ls => ls.SelectMany(l => l.Media))
            .Concat(welcome.SelectMany(w => w))
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var mediaIndex = mediaNames.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => (ushort)x.i);

        // Register: Nummern bleiben, Namen bekommen (alphabetisch) die freien Nummern
        var allRegs = init.Select(i => i.Reg)
            .Concat(parsed.Values.SelectMany(ls => ls.SelectMany(RegistersOf)))
            .Distinct()
            .ToList();
        var usedNumbers = allRegs.Where(r => r.Number.HasValue).Select(r => r.Number!.Value).ToHashSet();
        var names = allRegs.Where(r => r.Name != null).Select(r => r.Name!).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        var regNumber = new Dictionary<string, ushort>();
        ushort next = 0;
        foreach (var name in names)
        {
            while (usedNumbers.Contains(next))
                next++;
            regNumber[name] = next++;
        }
        ushort Resolve(Register r) => r.Number ?? regNumber[r.Name!];
        ResValue Val(Value v) => v.Reg != null ? new ResValue(true, Resolve(v.Reg)) : new ResValue(false, v.Constant);

        var scripts = new SortedDictionary<ushort, List<ResLine>>();
        foreach (var (code, lines) in parsed)
        {
            scripts[code] = lines.Select(l => new ResLine(
                l.Conditions.Select(c => new ResCondition(Val(c.Left), c.Op, Val(c.Right))).ToList(),
                l.Commands.Select(c =>
                {
                    var arg = Val(c.Arg);
                    if (c.JumpTarget != null)
                    {
                        if (!codeOf.TryGetValue(c.JumpTarget, out var target))
                            throw new GmeBuildException($"Sprung zu unbekanntem Skript „{c.JumpTarget}“.");
                        arg = new ResValue(false, target);
                    }
                    return new ResCommand(c.Reg != null ? Resolve(c.Reg) : (ushort)0, c.Op, arg);
                }).ToList(),
                l.Media.Select(m => mediaIndex[m]).ToList())).ToList();
        }

        // Startwerte für alle Register bis zum höchsten in Skripten verwendeten
        int maxReg = parsed.Values.SelectMany(ls => ls.SelectMany(RegistersOf)).Select(r => (int)Resolve(r)).DefaultIfEmpty(0).Max();
        var initValues = init.GroupBy(i => Resolve(i.Reg)).ToDictionary(g => g.Key, g => g.Last().Value);
        var initialRegisters = Enumerable.Range(0, maxReg + 1)
            .Select(r => initValues.TryGetValue((ushort)r, out var v) ? v : (ushort)0)
            .ToList();

        var audio = mediaNames.Select(name => LoadAudio(project, baseDir, name, speak)).ToList();

        var comment = string.IsNullOrEmpty(project.Comment) ? "created with TipToy Designer" : project.Comment;
        if (comment.Length > MaxCommentLength)
            throw new GmeBuildException($"Der Kommentar ist {comment.Length - MaxCommentLength} Zeichen zu lang (maximal {MaxCommentLength}).");

        var file = new GmeFile
        {
            ProductId = project.ProductId,
            Comment = Encoding.Latin1.GetBytes(comment),
            Date = Encoding.ASCII.GetBytes((date ?? DateTime.UtcNow).ToString("yyyyMMdd")),
            Language = Encoding.Latin1.GetBytes(project.GmeLang ?? ""),
            Welcome = welcome.Select(w => w.Select(m => mediaIndex[m]).ToList()).ToList(),
            InitialRegisters = initialRegisters,
            Scripts = scripts,
            AudioFiles = audio,
            Replay = (ushort)(project.Replay ?? 0),
            Stop = (ushort)(project.Stop ?? 0),
        };
        try
        {
            return GmeWriter.Write(file);
        }
        catch (InvalidOperationException ex)
        {
            throw new GmeBuildException(ex.Message);
        }
    }

    static IEnumerable<Register> RegistersOf(ScriptLine line)
    {
        foreach (var c in line.Conditions)
        {
            if (c.Left.Reg != null) yield return c.Left.Reg;
            if (c.Right.Reg != null) yield return c.Right.Reg;
        }
        foreach (var c in line.Commands)
        {
            if (c.Reg != null) yield return c.Reg;
            if (c.Arg.Reg != null) yield return c.Arg.Reg;
        }
    }

    static byte[] LoadAudio(ProjectFile project, string baseDir, string name, Func<string, string, byte[]> speak)
    {
        var text = project.Speak.FirstOrDefault(s => s.Name == name);
        if (text != null)
            return speak(string.IsNullOrWhiteSpace(project.Language) ? "en" : project.Language, text.Text);

        var pattern = string.IsNullOrWhiteSpace(project.MediaPath) ? "%s" : project.MediaPath;
        var basePath = Path.Combine(baseDir, pattern.Replace("%s", name));
        var candidates = new[] { basePath }.Concat(MediaExtensions.Select(e => basePath + e)).ToList();
        var existing = candidates.Where(File.Exists).ToList();
        return existing.Count switch
        {
            1 => File.ReadAllBytes(existing[0]),
            0 => throw new GmeBuildException(
                $"Für „{name}“ gibt es weder einen Text in der Sprachausgabe noch eine Audiodatei. Gesucht wurde:\n  " +
                string.Join("\n  ", candidates)),
            _ => throw new GmeBuildException($"Für „{name}“ wurden mehrere Audiodateien gefunden:\n  " + string.Join("\n  ", existing)),
        };
    }
}
