using System.Text;

namespace Tupfer.Designer.Services;

/// <summary>Name einer erkannten Person: <paramref name="Name"/> für Skript und Text, <paramref name="Label"/> für Fläche und Sprachtext.</summary>
public readonly record struct PersonName(string Name, string Label);

/// <summary>
/// Namen für die bei „Personen erkennen“ angelegten Flächen, Skripte und Texte: aus dem eingegebenen
/// Namen („Änni“) wird ein gültiger, freier Skriptname („aenni“); ohne Namen gilt der Platzhalter „person1“ ….
/// </summary>
public static class PersonNames
{
    /// <summary>
    /// Skriptname zu einem eingegebenen Namen: klein geschrieben, Umlaute und ß ersetzt, andere Akzente
    /// entfernt, Leerzeichen und Bindestriche als „_“, sonstige Zeichen weggelassen. Leer, wenn nichts übrig bleibt.
    /// </summary>
    public static string ToScriptName(string? name)
    {
        var sb = new StringBuilder();
        foreach (char c in (name ?? "").Trim().ToLowerInvariant())
        {
            switch (c)
            {
                case 'ä': sb.Append("ae"); continue;
                case 'ö': sb.Append("oe"); continue;
                case 'ü': sb.Append("ue"); continue;
                case 'ß': sb.Append("ss"); continue;
                case ' ' or '-' or '_':
                    if (sb.Length > 0 && sb[^1] != '_')
                        sb.Append('_');
                    continue;
            }
            // Akzente abtrennen (é → e) und nur Buchstaben a–z und Ziffern behalten
            foreach (char d in c.ToString().Normalize(NormalizationForm.FormD))
                if (d is >= 'a' and <= 'z' or >= '0' and <= '9')
                    sb.Append(d);
        }
        var result = sb.ToString().Trim('_');
        // Skriptnamen beginnen nicht mit einer Ziffer
        return result.Length > 0 && char.IsDigit(result[0]) ? "p" + result : result;
    }

    /// <summary>
    /// Namen für die Personen in der Reihenfolge von <paramref name="names"/> (leer: Platzhalter
    /// „person1“, „Person 1“ mit der ersten freien Nummer). <paramref name="taken"/> meldet, ob ein Name
    /// schon als Skript oder Text vergeben ist; belegte Namen bekommen eine Nummer („anni2“).
    /// </summary>
    public static List<PersonName> Assign(IReadOnlyList<string?> names, Func<string, bool> taken)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        bool Free(string name) => !used.Contains(name) && !taken(name);
        var result = new List<PersonName>();
        foreach (var entered in names)
        {
            var baseName = ToScriptName(entered);
            string name;
            string label;
            if (baseName.Length == 0)
            {
                int n = 1;
                while (!Free($"person{n}"))
                    n++;
                name = $"person{n}";
                label = $"Person {n}";
            }
            else
            {
                name = baseName;
                for (int i = 2; !Free(name); i++)
                    name = baseName + i;
                label = entered!.Trim();
            }
            used.Add(name);
            result.Add(new PersonName(name, label));
        }
        return result;
    }
}
