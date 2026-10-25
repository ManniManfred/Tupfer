using Tupfer.Designer.Model;
using Tupfer.Designer.Oid;

namespace Tupfer.Designer.Services;

/// <summary>
/// Erzeugt eine Testseite, mit der sich herausfinden lässt, welche Druckeinstellung
/// (Auflösung und Punktgröße) der eigene Drucker so druckt, dass der Stift sie erkennt.
/// Jede Zeile enthält ein Anmeldefeld und ein Testfeld in einer anderen Variante;
/// beim Antippen des Testfelds sagt der Stift die erkannte Variante an.
/// </summary>
public static class PrinterTest
{
    public const uint ProductId = 950;
    public const string BaseName = "druckertest";

    public record Variant(string Letter, int Dpi, int PixelSize)
    {
        public string Script => $"variante_{Letter.ToLowerInvariant()}";
        public string Description => $"{Dpi} dpi, Punktgröße {PixelSize}";
    }

    public static IReadOnlyList<Variant> Variants { get; } = new[]
    {
        new Variant("A", 1200, 1),
        new Variant("B", 1200, 2),
        new Variant("C", 1200, 3),
        new Variant("D", 1200, 4),
        new Variant("E", 600, 1),
        new Variant("F", 600, 2),
    };

    public static ProjectFile CreateProject()
    {
        var project = new ProjectFile
        {
            ProductId = ProductId,
            Comment = "Druckertest fuer den tiptoi-Stift",
            Language = "de",
            Welcome = "willkommen",
        };
        project.Speak.Add(new SpeakEntry { Name = "willkommen", Text = "Druckertest bereit. Tippe auf ein Testfeld." });
        foreach (var v in Variants)
        {
            project.Scripts.Add(new ScriptEntry { Name = v.Script, Lines = { $"P({v.Script})" } });
            project.Speak.Add(new SpeakEntry { Name = v.Script, Text = $"Variante {v.Letter}. {v.Dpi} d p i, Punktgröße {v.PixelSize}." });
        }
        CodeAssigner.Assign(project);
        return project;
    }

    public static PdfPageSpec CreatePage(ProjectFile project)
    {
        var page = new PdfPageSpec();
        page.Texts.Add(new PdfText(15, 12, 18, "Druckertest für den tiptoi-Stift"));
        string[] help =
        {
            $"1. {BaseName}.gme auf den Stift kopieren.",
            "2. Diese Seite in Originalgröße drucken: keine Skalierung („Tatsächliche Größe“ / 100 %),",
            "    höchste Druckqualität, keine Tonersparfunktion.",
            "3. In einer Zeile zuerst „Anmelden“, dann „Test“ antippen. Der Stift nennt die erkannte Variante.",
            "4. Die zuverlässigste Variante im Designer unter Projekt → Druckauflösung / Punktgröße eintragen.",
        };
        for (int i = 0; i < help.Length; i++)
            page.Texts.Add(new PdfText(15, 24 + i * 5.5, 10, help[i]));

        const double top = 58, rowHeight = 36, field = 26;
        for (int i = 0; i < Variants.Count; i++)
        {
            var v = Variants[i];
            double y = top + i * rowHeight;
            int code = project.Scripts.First(s => s.Name == v.Script).Code!.Value;

            page.Texts.Add(new PdfText(15, y + 7, 16, $"Variante {v.Letter}"));
            page.Texts.Add(new PdfText(15, y + 15, 10, v.Description));

            page.Frames.Add((69, y - 1, field + 2, field + 2));
            page.Frames.Add((104, y - 1, 92, field + 2));
            page.Areas.Add(new OidArea(70, y, field, field, (int)ProductId, v.Dpi, v.PixelSize));
            page.Texts.Add(new PdfText(70, y + field + 2.5, 8, "Anmelden"));

            page.Areas.Add(new OidArea(105, y, 90, field, code, v.Dpi, v.PixelSize));
            page.Texts.Add(new PdfText(105, y + field + 2.5, 8, $"Test {v.Letter} (OID {code})"));
        }
        page.Texts.Add(new PdfText(15, 282, 8, $"Produkt-ID {ProductId} · erzeugt mit Tupfer Designer"));
        return page;
    }

    /// <summary>Schreibt PDF und tttool-YAML in das Verzeichnis des PDF-Pfads.</summary>
    public static string Write(string pdfPath)
    {
        var project = CreateProject();
        PdfWriter.Write(pdfPath, new[] { CreatePage(project) });
        var yaml = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pdfPath))!, BaseName + ".yaml");
        TttoolYaml.Export(project, yaml);
        return yaml;
    }
}
