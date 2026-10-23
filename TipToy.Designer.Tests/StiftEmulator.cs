using TipToy.Designer.Gme;

namespace TipToy.Designer.Tests;

/// <summary>
/// Einfacher Emulator für Stift-Skripte: Beim Antippen wird die erste Zeile ausgeführt, deren
/// Bedingungen alle zutreffen; ein Sprung J(…) folgt nach der Zeile.
/// </summary>
public class StiftEmulator
{
    readonly Dictionary<string, List<ScriptLine>> _scripts = new();
    ushort _zaehler = 7;

    public Dictionary<string, ushort> Register { get; } = new();
    public List<string> Abgespielt { get; } = new();

    public StiftEmulator(IEnumerable<(string Name, IEnumerable<string> Zeilen)> skripte, string init)
    {
        foreach (var (name, zeilen) in skripte)
            _scripts[name] = zeilen.Select(ScriptParser.ParseLine).ToList();
        foreach (var (reg, wert) in ScriptParser.ParseInit(init))
            Register[reg.ToString()] = wert;
    }

    ushort Wert(Value v) => v.Reg == null ? v.Constant : Register.GetValueOrDefault(v.Reg.ToString());

    public void Tippe(string skript)
    {
        string? naechstes = skript;
        for (int schritte = 0; naechstes != null; schritte++)
        {
            if (schritte > 200)
                throw new InvalidOperationException($"Endlosschleife ab {skript}");
            var zeilen = _scripts[naechstes];
            naechstes = null;
            var zeile = zeilen.FirstOrDefault(l => l.Conditions.All(c => Vergleiche(Wert(c.Left), c.Op[0], Wert(c.Right))));
            if (zeile == null)
                continue;
            foreach (var c in zeile.Commands)
            {
                var reg = c.Reg?.ToString();
                ushort a = reg == null ? (ushort)0 : Register.GetValueOrDefault(reg);
                ushort v = Wert(c.Arg);
                switch ((c.Op[0], c.Op[1]))
                {
                    case (0xF9, 0xFF): Register[reg!] = v; break;
                    case (0xF0, 0xFF): Register[reg!] = (ushort)(a + v); break;
                    case (0xF1, 0xFF): Register[reg!] = (ushort)(a - v); break;
                    case (0xF2, 0xFF): Register[reg!] = (ushort)(a * v); break;
                    case (0xF3, 0xFF): Register[reg!] = (ushort)(a / v); break;
                    case (0xF4, 0xFF): Register[reg!] = (ushort)(a % v); break;
                    case (0xF5, 0xFF): Register[reg!] = (ushort)(a & v); break;
                    case (0xF6, 0xFF): Register[reg!] = (ushort)(a | v); break;
                    case (0xF7, 0xFF): Register[reg!] = (ushort)(a ^ v); break;
                    case (0xE8, 0xFF): Abgespielt.Add(zeile.Media[v]); break;
                    case (0x00, 0xFC): Abgespielt.Add(zeile.Media[v >> 8]); break; // Zufall: erste Datei
                    case (0x00, 0xFB):
                        for (int i = v >> 8; i <= (v & 0xFF); i++)
                            Abgespielt.Add(zeile.Media[i]);
                        break;
                    case (0xFF, 0xF8): naechstes = c.JumpTarget ?? v.ToString(); break;
                    case (0x00, 0xFF): Register[reg!] = (ushort)(_zaehler++ % Math.Max((ushort)1, v)); break;
                    case (0xFF, 0xFA): break;
                    default: throw new NotSupportedException($"Befehl {c.Op[0]:X2}{c.Op[1]:X2}");
                }
            }
        }
    }

    static bool Vergleiche(ushort a, byte op, ushort b) => op switch
    {
        0xF9 => a == b,
        0xFA => a > b,
        0xFB => a < b,
        0xFD => a >= b,
        0xFE => a <= b,
        _ => a != b,
    };
}
