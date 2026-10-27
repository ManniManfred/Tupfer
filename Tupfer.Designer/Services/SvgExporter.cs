using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Tupfer.Designer.Model;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer.Services;

/// <summary>
/// Schreibt eine Seite als SVG (Maße in mm): Bilder (eingebettet, mit Zuschnitt), Texte (umbrochen wie
/// in der Ansicht), Farbflächen mit Rand (Polygonbögen als echte Bögen). Die OID-Flächen liegen auf einer
/// eigenen, ausgeblendeten Ebene und werden von <see cref="SvgImporter"/> wieder als OID-Flächen gelesen.
/// </summary>
public static class SvgExporter
{
    static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    static readonly XNamespace Inkscape = "http://www.inkscape.org/namespaces/inkscape";

    /// <param name="warnings">Nimmt Hinweise auf (z. B. fehlende Bilddateien).</param>
    public static string Export(PageViewModel page, List<string>? warnings = null)
    {
        var root = new XElement(Svg + "svg",
            new XAttribute(XNamespace.Xmlns + "xlink", XLink),
            new XAttribute(XNamespace.Xmlns + "inkscape", Inkscape),
            new XAttribute("version", "1.1"),
            new XAttribute("width", N(page.WidthMm) + "mm"),
            new XAttribute("height", N(page.HeightMm) + "mm"),
            new XAttribute("viewBox", $"0 0 {N(page.WidthMm)} {N(page.HeightMm)}"),
            new XElement(Svg + "title", page.Name));

        var elements = Layer("elemente", "Bilder, Texte und Farbflächen");
        foreach (var e in page.Elements)
            if (Element(e, warnings) is { } x)
                elements.Add(x);
        root.Add(elements);

        // Ausgeblendet: sonst würden die Flächen beim Weiterverwenden der Grafik mitgedruckt
        var areas = Layer("oid-flaechen", "OID-Flächen");
        areas.Add(new XAttribute("data-tupfer", "oid-areas"), new XAttribute("style", "display:none"));
        foreach (var a in page.Areas)
            areas.Add(Area(a));
        root.Add(areas);

        var sb = new StringBuilder();
        using (var writer = new Utf8StringWriter(sb))
            new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(writer);
        return sb.ToString();
    }

    sealed class Utf8StringWriter(StringBuilder sb) : StringWriter(sb, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    static XElement Layer(string id, string label) => new(Svg + "g",
        new XAttribute("id", id),
        new XAttribute(Inkscape + "groupmode", "layer"),
        new XAttribute(Inkscape + "label", label));

    static string N(double v) => Math.Round(v, 4).ToString(CultureInfo.InvariantCulture);

    static XElement? Element(ElementViewModel e, List<string>? warnings)
    {
        XElement? x = e.Kind switch
        {
            ElementKind.Image => Image(e, warnings),
            ElementKind.Text => Text(e),
            ElementKind.Shape => Shape(e),
            _ => null,
        };
        // Drehung über eine Gruppe: transform an verschachteltem svg gibt es erst ab SVG 2
        if (x != null && Math.Abs(e.Rotation) > 1e-9)
            return new XElement(Svg + "g", new XAttribute("transform", $"rotate({N(e.Rotation)} {N(e.Center.X)} {N(e.Center.Y)})"), x);
        return x;
    }

    static XElement? Image(ElementViewModel e, List<string>? warnings)
    {
        if (!File.Exists(e.ImagePath))
        {
            warnings?.Add($"Bild „{e.FileName}“ nicht gefunden.");
            return null;
        }
        var mime = Path.GetExtension(e.ImagePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png",
        };
        var href = $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(e.ImagePath))}";
        // Zuschnitt: verschachteltes svg zeigt nur den Ausschnitt (Bild in Einheiten seiner Größe)
        return new XElement(Svg + "svg",
            new XAttribute("x", N(e.X)), new XAttribute("y", N(e.Y)),
            new XAttribute("width", N(e.Width)), new XAttribute("height", N(e.Height)),
            new XAttribute("viewBox", $"{N(e.CropX)} {N(e.CropY)} {N(e.CropWidth)} {N(e.CropHeight)}"),
            new XAttribute("preserveAspectRatio", "none"),
            new XElement(Svg + "image",
                new XAttribute("width", "1"), new XAttribute("height", "1"),
                new XAttribute("preserveAspectRatio", "none"),
                new XAttribute(XLink + "href", href)));
    }

    /// <summary>Farbe als #RRGGBB und Deckkraft (SVG kennt kein #AARRGGBB).</summary>
    static (string Color, double Opacity) Paint(string value)
    {
        var c = Color.TryParse(value, out var parsed) ? parsed : Colors.Black;
        return ($"#{c.R:X2}{c.G:X2}{c.B:X2}", Math.Round(c.A / 255.0, 3));
    }

    static void AddFill(XElement x, string color, string attribute = "fill")
    {
        var (c, opacity) = Paint(color);
        if (opacity <= 0)
        {
            x.Add(new XAttribute(attribute, "none"));
            return;
        }
        x.Add(new XAttribute(attribute, c));
        if (opacity < 1)
            x.Add(new XAttribute(attribute + "-opacity", N(opacity)));
    }

    static XElement Shape(ElementViewModel e)
    {
        XElement x = e.Shape switch
        {
            AreaShape.Ellipse => new XElement(Svg + "ellipse",
                new XAttribute("cx", N(e.Center.X)), new XAttribute("cy", N(e.Center.Y)),
                new XAttribute("rx", N(e.Width / 2)), new XAttribute("ry", N(e.Height / 2))),
            AreaShape.Polygon when e.Points.Count >= 3 => new XElement(Svg + "path",
                new XAttribute("d", PathData(e.Points.Select(p => p with { X = e.X + p.X, Y = e.Y + p.Y }).ToList()))),
            _ => new XElement(Svg + "rect",
                new XAttribute("x", N(e.X)), new XAttribute("y", N(e.Y)),
                new XAttribute("width", N(e.Width)), new XAttribute("height", N(e.Height))),
        };
        AddFill(x, e.Color);
        if (e.StrokeWidth > 0)
        {
            AddFill(x, e.StrokeColor, "stroke");
            x.Add(new XAttribute("stroke-width", N(e.StrokeWidth)));
            x.Add(new XAttribute("stroke-linejoin", e.Shape == AreaShape.Rectangle ? "miter" : "round"));
        }
        return x;
    }

    /// <summary>Geschlossener Pfad; Kanten mit Bulge werden Kreisbögen (A).</summary>
    public static string PathData(IReadOnlyList<AreaPoint> points)
    {
        var sb = new StringBuilder();
        sb.Append($"M{N(points[0].X)},{N(points[0].Y)}");
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var q = points[(i + 1) % points.Count];
            if (Math.Abs(p.Bulge) < 1e-9)
            {
                if (i + 1 < points.Count)
                    sb.Append($" L{N(q.X)},{N(q.Y)}");
                continue;
            }
            double chord = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
            double angle = 4 * Math.Atan(p.Bulge);
            double radius = chord / (2 * Math.Sin(Math.Abs(angle) / 2));
            int large = Math.Abs(angle) > Math.PI ? 1 : 0, sweep = angle > 0 ? 1 : 0;
            sb.Append($" A{N(radius)},{N(radius)} 0 {large} {sweep} {N(q.X)},{N(q.Y)}");
        }
        sb.Append(" Z");
        return sb.ToString();
    }

    static XElement Text(ElementViewModel e)
    {
        double sizeMm = e.FontSize * 25.4 / 72;
        var (anchor, x) = e.Align switch
        {
            TextAlign.Center => ("middle", e.X + e.Width / 2),
            TextAlign.Right => ("end", e.X + e.Width),
            _ => ("start", e.X),
        };
        var text = new XElement(Svg + "text",
            new XAttribute("font-family", string.IsNullOrWhiteSpace(e.FontFamily) ? "sans-serif" : e.FontFamily),
            new XAttribute("font-size", N(sizeMm)),
            new XAttribute("text-anchor", anchor));
        if (e.Bold)
            text.Add(new XAttribute("font-weight", "bold"));
        if (e.Italic)
            text.Add(new XAttribute("font-style", "italic"));
        AddFill(text, e.Color);
        foreach (var (line, baseline) in Lines(e))
            text.Add(new XElement(Svg + "tspan", new XAttribute(XNamespace.Xml + "space", "preserve"),
                new XAttribute("x", N(x)), new XAttribute("y", N(e.Y + baseline)), line));
        return text;
    }

    /// <summary>Zeilen wie in der Ansicht umbrochen, mit Lage der Grundlinie unter dem oberen Rand (mm).</summary>
    static List<(string Line, double Baseline)> Lines(ElementViewModel e)
    {
        const double scale = 10; // Pixel pro mm zum Messen
        double sizeMm = e.FontSize * 25.4 / 72;
        try
        {
            var family = string.IsNullOrWhiteSpace(e.FontFamily) ? FontFamily.Default : new FontFamily(e.FontFamily);
            var typeface = new Typeface(family, e.Italic ? FontStyle.Italic : FontStyle.Normal, e.Bold ? FontWeight.Bold : FontWeight.Normal);
            using var layout = new TextLayout(e.Text, typeface, Math.Max(0.1, sizeMm * scale), Brushes.Black,
                TextAlignment.Left, TextWrapping.Wrap, maxWidth: Math.Max(1, e.Width * scale));
            var result = new List<(string, double)>();
            double top = 0;
            foreach (var line in layout.TextLines)
            {
                int start = Math.Min(line.FirstTextSourceIndex, e.Text.Length);
                int length = Math.Min(line.Length, e.Text.Length - start);
                result.Add((e.Text.Substring(start, length).TrimEnd('\r', '\n'), (top + line.Baseline) / scale));
                top += line.Height;
            }
            return result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or ArgumentException)
        {
            // Ohne Schriftsystem (z. B. in Tests): nur an Zeilenumbrüchen trennen
            return e.Text.Split('\n')
                .Select((l, i) => (l.TrimEnd('\r'), (SvgImporter.AscentFactor + i * SvgImporter.LineHeightFactor) * sizeMm))
                .ToList();
        }
    }

    static XElement Area(AreaViewModel a)
    {
        XElement x = a.Shape switch
        {
            AreaShape.Ellipse => new XElement(Svg + "ellipse",
                new XAttribute("cx", N(a.X + a.Width / 2)), new XAttribute("cy", N(a.Y + a.Height / 2)),
                new XAttribute("rx", N(a.Width / 2)), new XAttribute("ry", N(a.Height / 2))),
            AreaShape.Polygon when a.Points.Count >= 3 => new XElement(Svg + "path",
                new XAttribute("d", PathData(a.PagePoints.ToList()))),
            _ => new XElement(Svg + "rect",
                new XAttribute("x", N(a.X)), new XAttribute("y", N(a.Y)),
                new XAttribute("width", N(a.Width)), new XAttribute("height", N(a.Height))),
        };
        x.Add(new XAttribute("fill", "#E8590C"), new XAttribute("fill-opacity", "0.35"),
            new XAttribute("stroke", "#E8590C"), new XAttribute("stroke-width", "0.3"),
            new XAttribute("data-target", a.Target.ToString()));
        if (a.Script is { Length: > 0 } script)
            x.Add(new XAttribute("data-script", script));
        if (a.Label.Length > 0)
            x.Add(new XAttribute("data-label", a.Label));
        x.Add(new XElement(Svg + "title", a.Caption));
        return x;
    }
}
