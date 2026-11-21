using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tupfer.Designer.ViewModels;

/// <summary>
/// Suchspiel: Der Stift fragt „Wo ist …?“ und man tippt die richtige Fläche an. Mitspielen können Flächen,
/// deren Skript nur etwas abspielt (z. B. nach „Personen erkennen“: <c>P(anni)</c>). Außerhalb des Spiels
/// bleibt alles wie vorher.
/// <para>
/// Register: <c>$ziel</c> ist die Nummer der gesuchten Fläche (0 = kein Spiel), <c>$frage</c> die laufende
/// Frage, <c>$runde</c> wechselt zwischen zwei Reihenfolgen. Skripte: <c>suchspiel</c> (Lupe, startet),
/// <c>nochmal</c> (wiederholt die Frage), <c>frage</c> und <c>weiter</c>. Jede mitspielende Fläche bekommt
/// <code>
/// $ziel == 0? P(anni)                                       – außerhalb des Spiels wie bisher
/// $ziel == 2? $frage += 1 P(richtig1, richtig2, richtig3) J(weiter)
/// P(anni) J(frage)                                          – falsch: eigenen Namen sagen, nochmal fragen
/// </code>
/// </para>
/// </summary>
public static partial class SearchGame
{
    public const string StartScript = "suchspiel", AskScript = "nochmal", QuestionScript = "frage", NextScript = "weiter";

    static readonly string[] GameScripts = [StartScript, AskScript, QuestionScript, NextScript];

    /// <summary>Texte des Spiels mit Vorgabe; vorhandene Texte (und ihre Aufnahmen) bleiben, wie sie sind.</summary>
    public static readonly IReadOnlyList<(string Name, string Text)> Texts =
    [
        ("suchspiel_los", "Super, wir spielen Suchen! Finde die Richtigen."),
        ("suchspiel_lupe", "Tipp auf die Lupe, dann spielen wir Suchen!"),
        ("richtig1", "Richtig!"),
        ("richtig2", "Super, gefunden!"),
        ("richtig3", "Toll gemacht!"),
        ("suchspiel_geschafft", "Hurra! Du hast alle gefunden. Tipp auf die Lupe, dann spielen wir nochmal!"),
    ];

    const string Correct = "P(richtig1, richtig2, richtig3)";

    /// <summary>Eine Fläche im Spiel: ihr Skript und der Name in der Frage „Wo ist …?“.</summary>
    public record Target(ScriptViewModel Script, string Name);

    /// <summary>Text der Frage nach dem Skript.</summary>
    public static string QuestionSpeak(ScriptViewModel script) => "wo_" + script.Name;

    /// <summary>Ein Spiel ist angelegt.</summary>
    public static bool Exists(ProjectViewModel project) => project.Scripts.Any(s => s.Name == QuestionScript);

    /// <summary>
    /// Flächen, die mitspielen können: Ihr Skript (ohne ein schon eingebautes Spiel) ist eine Zeile ohne
    /// Bedingung und ohne Sprung. Je Skript die erste Fläche, in der Reihenfolge der Seiten.
    /// </summary>
    public static List<(AreaViewModel Area, ScriptViewModel Script, bool InGame)> Candidates(ProjectViewModel project)
    {
        var result = new List<(AreaViewModel, ScriptViewModel, bool)>();
        var seen = new HashSet<ScriptViewModel>();
        foreach (var area in project.Pages.SelectMany(p => p.Areas).Where(a => a.IsScript))
        {
            if (project.Scripts.FirstOrDefault(s => s.Name == area.Script) is not { } script
                || GameScripts.Contains(script.Name) || !seen.Add(script))
                continue;
            var original = Original(script.Text, out bool inGame);
            if (original != null && IsPlain(original))
                result.Add((area, script, inGame));
        }
        return result;
    }

    static bool IsPlain(string line) => !line.Contains('?') && !Regex.IsMatch(line, @"(?<![\w$])J\(");

    static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    [GeneratedRegex(@"^\$ziel == \d+\? \$frage \+= 1 P\(richtig1, richtig2, richtig3\) J\(weiter\)$")]
    private static partial Regex CorrectLine();

    /// <summary>
    /// Die eine Zeile des Skripts ohne das Spiel; null, wenn es nicht genau eine Zeile ist.
    /// <paramref name="inGame"/>: Das Skript spielt schon mit.
    /// </summary>
    static string? Original(string text, out bool inGame)
    {
        var lines = Lines(text);
        inGame = false;
        if (lines.Count == 3 && lines[0].StartsWith("$ziel == 0? ") && CorrectLine().IsMatch(lines[1]))
        {
            var original = lines[0]["$ziel == 0? ".Length..];
            if (lines[2] == original + " J(frage)")
            {
                inGame = true;
                return original;
            }
        }
        return lines.Count == 1 ? lines[0] : null;
    }

    /// <summary>
    /// Legt das Spiel an bzw. erneuert es mit diesen Flächen (Flächen, die nicht mehr dabei sind, werden wie
    /// vorher). Zwei Runden in verschiedener zufälliger Reihenfolge. Liefert die neu angelegten Texte.
    /// </summary>
    public static List<SpeakViewModel> Apply(ProjectViewModel project, IReadOnlyList<Target> targets, Random random)
    {
        if (targets.Count < 2)
            throw new ArgumentException("Ein Suchspiel braucht mindestens zwei Flächen.", nameof(targets));
        if (targets.Count > 250)
            throw new ArgumentException("Ein Suchspiel kann höchstens 250 Flächen haben.", nameof(targets));

        // Vorhandenes Spiel aus allen Skripten herausnehmen
        foreach (var script in project.Scripts.Where(s => !GameScripts.Contains(s.Name)))
            if (Original(script.Text, out bool inGame) is { } original && inGame)
                script.Text = original;

        var added = new List<SpeakViewModel>();
        void EnsureSpeak(string name, string text)
        {
            if (project.Speak.Any(s => s.Name == name))
                return;
            var speak = new SpeakViewModel { Name = name, Text = text };
            project.Speak.Add(speak);
            added.Add(speak);
        }
        foreach (var (name, text) in Texts)
            EnsureSpeak(name, text);

        for (int i = 0; i < targets.Count; i++)
        {
            var (script, name) = targets[i];
            var original = Original(script.Text, out _)!;
            script.Text = $"$ziel == 0? {original}\n$ziel == {i + 1}? $frage += 1 {Correct} J({NextScript})\n{original} J({QuestionScript})";
            var question = $"Wo ist {name}?";
            // Einen geänderten Namen übernehmen, solange keine eigene Aufnahme die Frage spricht
            if (project.Speak.FirstOrDefault(s => s.Name == QuestionSpeak(script)) is { HasRecording: false } existing)
                existing.Text = question;
            else
                EnsureSpeak(QuestionSpeak(script), question);
        }

        // Zwei Runden, die zweite in anderer Reihenfolge
        var first = Shuffle(targets.Count, random);
        var second = Shuffle(targets.Count, random);
        while (second.SequenceEqual(first))
            second = Shuffle(targets.Count, random);
        var questions = new List<string>();
        foreach (var (round, order) in new[] { (0, first), (1, second) })
            for (int q = 0; q < order.Length; q++)
                questions.Add($"$runde == {round}? $frage == {q + 1}? $ziel := {order[q] + 1} P({QuestionSpeak(targets[order[q]].Script)})");
        int done = targets.Count + 1;

        SetScript(project, StartScript, $"$frage := 1 P(suchspiel_los) J({QuestionScript})");
        SetScript(project, AskScript, $"$ziel == 0? P(suchspiel_lupe)\nJ({QuestionScript})");
        SetScript(project, QuestionScript, string.Join("\n", questions));
        SetScript(project, NextScript,
            $"$frage == {done}? $runde == 0? $ziel := 0 $runde := 1 P(suchspiel_geschafft)\n" +
            $"$frage == {done}? $ziel := 0 $runde := 0 P(suchspiel_geschafft)\n" +
            $"J({QuestionScript})");
        project.AssignCodes();
        project.RefreshAreas();
        return added;
    }

    static int[] Shuffle(int count, Random random)
    {
        var order = Enumerable.Range(0, count).ToArray();
        random.Shuffle(order);
        return order;
    }

    static void SetScript(ProjectViewModel project, string name, string text)
    {
        if (project.Scripts.FirstOrDefault(s => s.Name == name) is { } script)
            script.Text = text;
        else
            project.AddScript(name, text);
    }
}

/// <summary>Eine Fläche in der Auswahl fürs Suchspiel: mitspielen ja/nein und der Name in der Frage.</summary>
public partial class SearchGameEntry(ScriptViewModel script, string name, bool include) : ObservableObject
{
    public ScriptViewModel Script { get; } = script;

    [ObservableProperty] bool _include = include;
    [ObservableProperty] string _name = name;
}
