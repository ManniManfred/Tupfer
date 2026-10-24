using TipToy.Designer.Model;
using TipToy.Designer.Services;

namespace TipToy.Designer.Gme;

/// <summary>Ergebnis eines Antippens: abzuspielende Audio-Namen und eine Beschreibung des Ablaufs.</summary>
public record PenResult(List<string> Media, List<string> Trace, bool Stop);

/// <summary>
/// Simuliert den Stift für den Testmodus: Beim Antippen wird die erste Zeile des Skripts ausgeführt,
/// deren Bedingungen alle zutreffen (höchstens 8 Befehle wie auf dem Stift); ein Sprung J(…) folgt
/// nach der Zeile. Spiele, Timer und Soundprofile werden nicht simuliert.
/// </summary>
public class PenSimulator
{
    const int MaxCommands = 8;
    const int MaxJumps = 100;

    Dictionary<string, List<ScriptLine>> _scripts = new();
    Dictionary<int, string> _byCode = new();
    List<string> _welcome = new();
    List<(Register Reg, ushort Value)> _init = new();
    readonly Random _random = new();

    /// <summary>Registerwerte (Schlüssel wie im Skript, z. B. „$punkte“).</summary>
    public SortedDictionary<string, ushort> Registers { get; } = new(StringComparer.Ordinal);

    /// <summary>Zuletzt abgespielte Audio-Namen (für „Wiederholen“).</summary>
    public List<string> LastMedia { get; private set; } = new();

    /// <summary>
    /// Übernimmt Skripte, Begrüßung und Initialisierung des Projekts (die Registerwerte bleiben).
    /// Wirft <see cref="ScriptParseException"/> mit Skriptname und Zeile bei Fehlern.
    /// </summary>
    public void Load(ProjectFile project)
    {
        CodeAssigner.Assign(project);
        var scripts = new Dictionary<string, List<ScriptLine>>();
        foreach (var s in project.Scripts)
        {
            var lines = new List<ScriptLine>();
            for (int i = 0; i < s.Lines.Count; i++)
            {
                try
                {
                    lines.Add(ScriptParser.ParseLine(s.Lines[i]));
                }
                catch (ScriptParseException ex)
                {
                    throw new ScriptParseException($"Skript „{s.Name}“, Zeile {i + 1}: {ex.Message}");
                }
            }
            scripts[s.Name] = lines;
        }
        try
        {
            _welcome = string.IsNullOrWhiteSpace(project.Welcome) ? new() : ScriptParser.ParsePlayList(project.Welcome);
            _init = ScriptParser.ParseInit(project.Init ?? "");
        }
        catch (ScriptParseException ex)
        {
            throw new ScriptParseException($"Begrüßung/Initialisierung: {ex.Message}");
        }
        _scripts = scripts;
        _byCode = project.Scripts.Where(s => s.Code != null).GroupBy(s => s.Code!.Value).ToDictionary(g => g.Key, g => g.First().Name);
    }

    /// <summary>Wie nach dem Antippen des Produkt-Codes: Register auf die Startwerte, Begrüßung abspielen.</summary>
    public PenResult Start()
    {
        Registers.Clear();
        foreach (var (reg, value) in _init)
            Registers[reg.ToString()] = value;
        var trace = new List<string> { "Start: Register zurückgesetzt" };
        if (_welcome.Count > 0)
            trace.Add("Begrüßung: " + string.Join(", ", _welcome));
        LastMedia = _welcome.ToList();
        return new PenResult(_welcome.ToList(), trace, false);
    }

    ushort Get(Register r) => Registers.GetValueOrDefault(r.ToString());

    ushort Get(Value v) => v.Reg == null ? v.Constant : Get(v.Reg);

    /// <summary>Führt das Skript aus, als würde eine Fläche mit seinem Code angetippt.</summary>
    public PenResult Tap(string script)
    {
        var media = new List<string>();
        var trace = new List<string>();
        bool stop = false;
        string? next = script;
        for (int jumps = 0; next != null; jumps++)
        {
            if (jumps > MaxJumps)
            {
                trace.Add("Abbruch: Endlosschleife durch Sprünge");
                break;
            }
            if (!_scripts.TryGetValue(next, out var lines))
            {
                trace.Add($"Skript „{next}“ gibt es nicht");
                break;
            }
            var current = next;
            next = null;
            int index = lines.FindIndex(l => l.Conditions.All(Holds));
            if (index < 0)
            {
                trace.Add($"{current}: keine Zeile trifft zu");
                continue;
            }
            var line = lines[index];
            trace.Add($"{current}, Zeile {index + 1}");
            foreach (var c in line.Commands.Take(MaxCommands))
            {
                if (Execute(c, line, media, trace, ref next))
                {
                    stop = true;
                    break;
                }
            }
            if (stop)
                break;
        }
        if (media.Count > 0)
            LastMedia = media;
        return new PenResult(media, trace, stop);
    }

    bool Holds(Condition c)
    {
        ushort a = Get(c.Left), b = Get(c.Right);
        return c.Op[0] switch
        {
            0xF9 => a == b,
            0xFA => a > b,
            0xFB => a < b,
            0xFD => a >= b,
            0xFE => a <= b,
            _ => a != b,
        };
    }

    /// <summary>Führt einen Befehl aus; true = Abbruch (C).</summary>
    bool Execute(Command c, ScriptLine line, List<string> media, List<string> trace, ref string? next)
    {
        ushort v = Get(c.Arg);
        if (c.Reg is { } reg)
        {
            ushort a = Get(reg);
            ushort? result = (c.Op[0], c.Op[1]) switch
            {
                (0xF9, 0xFF) => v,
                (0xF0, 0xFF) => (ushort)(a + v),
                (0xF1, 0xFF) => (ushort)(a - v),
                (0xF2, 0xFF) => (ushort)(a * v),
                (0xF3, 0xFF) => v == 0 ? a : (ushort)(a / v),
                (0xF4, 0xFF) => v == 0 ? a : (ushort)(a % v),
                (0xF5, 0xFF) => (ushort)(a & v),
                (0xF6, 0xFF) => (ushort)(a | v),
                (0xF7, 0xFF) => (ushort)(a ^ v),
                (0xF8, 0xFF) => (ushort)-a,
                (0x00, 0xFF) => (ushort)_random.Next(Math.Max(1, (int)v)), // T($r, n): Zufallswert 0 … n-1
                _ => null,
            };
            if (result == null)
            {
                trace.Add($"  Befehl {c.Op[0]:X2}{c.Op[1]:X2} wird im Test nicht simuliert");
                return false;
            }
            Registers[reg.ToString()] = result.Value;
            trace.Add($"  {reg} = {result.Value}");
            return false;
        }

        void Play(IEnumerable<string> names)
        {
            var list = names.ToList();
            media.AddRange(list);
            trace.Add("  spielt " + string.Join(", ", list));
        }

        IEnumerable<string> Range(ushort packed)
        {
            int first = packed >> 8, last = packed & 0xFF;
            for (int i = first; i <= last && i < line.Media.Count; i++)
                yield return line.Media[i];
        }

        switch ((c.Op[0], c.Op[1]))
        {
            case (0xE8, 0xFF) or (0xA1, 0xFF):
                Play([line.Media[v]]);
                break;
            case (0x00, 0xFC):
                var choice = Range(v).ToList();
                Play([choice[_random.Next(choice.Count)]]);
                break;
            case (0x00, 0xFB):
                Play(Range(v));
                break;
            case (0xE0, 0xFF) when line.Media.Count > 0:
                Play([line.Media[_random.Next(line.Media.Count)]]);
                break;
            case (0xE1, 0xFF):
                Play(line.Media);
                break;
            case (0xFF, 0xF8):
                next = c.JumpTarget ?? (_byCode.TryGetValue(v, out var name) ? name : v.ToString());
                trace.Add($"  springt zu {next}");
                break;
            case (0xFF, 0xFA):
                trace.Add("  C: Abbruch");
                return true;
            case (0xFF, 0xFE):
                break;
            case (0x00, 0xFD):
                trace.Add($"  Spiel {v} wird im Test nicht simuliert");
                break;
            default:
                trace.Add($"  Befehl {c.Op[0]:X2}{c.Op[1]:X2} wird im Test nicht simuliert");
                break;
        }
        return false;
    }
}
