using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Tupfer.Designer.Model;
using AvColor = Avalonia.Media.Color;

namespace Tupfer.Designer.Services;

/// <summary>Ergebnis eines SVG-Imports; Angaben in mm.</summary>
public class SvgImportResult
{
    /// <summary>Größe der Zeichnung, falls im SVG angegeben.</summary>
    public double? WidthMm { get; set; }
    public double? HeightMm { get; set; }

    /// <summary>Bilder, Texte und Farbflächen in Zeichenreihenfolge (unterstes zuerst).</summary>
    public List<ElementEntry> Elements { get; } = new();

    /// <summary>OID-Flächen aus der Ebene „OID-Flächen“ (siehe <see cref="SvgExporter"/>).</summary>
    public List<AreaEntry> Areas { get; } = new();

    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Liest eine SVG-Datei als Bilder, Texte und Farbflächen. Unterstützt werden rect (auch abgerundet),
/// circle, ellipse, line, polyline, polygon, path (Kurven werden fein unterteilt, Kreisbögen bleiben Bögen),
/// text/tspan, image (eingebettet oder als Datei), g/a/use/verschachtelte svg mit Transformationen,
/// Stile aus Attributen, style und einfachen CSS-Klassen, Deckkraft und Verläufe (als erste Farbe).
/// Offene Linien werden zu Flächen in Strichbreite. Eine Ebene mit data-tupfer="oid-areas" (oder
/// einer Beschriftung mit „OID“) wird zu OID-Flächen.
/// </summary>
public static class SvgImporter
{
    /// <summary>Geschätzte Oberlänge (Anteil der Schriftgröße) für die Lage des Texts über der Grundlinie.</summary>
    public const double AscentFactor = 0.9;

    /// <summary>Geschätzte Zeilenhöhe (Anteil der Schriftgröße).</summary>
    public const double LineHeightFactor = 1.2;

    const double MmPerPx = 25.4 / 96;

    /// <summary>Größte Abweichung der unterteilten Kurven (mm).</summary>
    const double CurveTolerance = 0.05;

    static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    static readonly XNamespace Inkscape = "http://www.inkscape.org/namespaces/inkscape";

    /// <param name="svg">Inhalt der SVG-Datei.</param>
    /// <param name="svgDir">Verzeichnis der SVG-Datei, für verlinkte Bilder (null = keine).</param>
    /// <param name="imageDir">Hierhin werden eingebettete Bilder als Dateien geschrieben.</param>
    public static SvgImportResult Import(string svg, string? svgDir, string imageDir)
    {
        var doc = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new FormatException("Leere SVG-Datei.");
        if (root.Name.LocalName != "svg")
            throw new FormatException("Keine SVG-Datei.");
        var reader = new Reader(root, svgDir, imageDir);
        reader.Run();
        return reader.Result;
    }

    // ---------- Geometrie ----------

    /// <summary>Affine Abbildung (x', y') = (a x + c y + e, b x + d y + f) wie in SVG.</summary>
    readonly record struct Affine(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

        public static Affine Translate(double x, double y) => new(1, 0, 0, 1, x, y);
        public static Affine Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

        public static Affine Rotate(double degrees)
        {
            double r = degrees * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
            return new(cos, sin, -sin, cos, 0, 0);
        }

        /// <summary>Erst <paramref name="inner"/>, dann diese Abbildung.</summary>
        public Affine Then(Affine inner) => new(
            A * inner.A + C * inner.B, B * inner.A + D * inner.B,
            A * inner.C + C * inner.D, B * inner.C + D * inner.D,
            A * inner.E + C * inner.F + E, B * inner.E + D * inner.F + F);

        public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);

        public double Det => A * D - B * C;

        /// <summary>Mittlerer Maßstab (für Strichbreiten und Toleranzen).</summary>
        public double MeanScale => Math.Sqrt(Math.Abs(Det));

        public double ScaleX => Math.Sqrt(A * A + B * B);

        /// <summary>Maßstab in y-Richtung, negativ bei Spiegelung.</summary>
        public double ScaleY => Det / ScaleX;

        public double AngleDegrees => Math.Atan2(B, A) * 180 / Math.PI;

        /// <summary>Ohne Scherung (Rechtecke bleiben Rechtecke).</summary>
        public bool NoSkew => Math.Abs(A * C + B * D) < 1e-9 * Math.Max(1, A * A + B * B + C * C + D * D);

        /// <summary>Gleichmäßig skaliert (Kreise bleiben Kreise).</summary>
        public bool IsSimilarity => NoSkew && Math.Abs(ScaleX - Math.Abs(ScaleY)) < 1e-6 * Math.Max(1, ScaleX);
    }

    /// <summary>Eckpunkt eines Pfads (Seitenkoordinaten, mm); Bulge gilt für die Kante zum nächsten Punkt.</summary>
    record struct Vertex(double X, double Y, double Bulge = 0);

    record Subpath(List<Vertex> Points, bool Closed);

    // ---------- Stil ----------

    /// <summary>Farbe einer Füllung oder Linie; null = keine.</summary>
    record Paint(AvColor? Color);

    class Style
    {
        public Paint Fill = new(Avalonia.Media.Colors.Black);
        public Paint Stroke = new(null);
        public double StrokeWidth = 1;
        public double FillOpacity = 1, StrokeOpacity = 1;
        public string FontFamily = "";
        public double FontSize = 16;
        public bool Bold, Italic;
        public string TextAnchor = "start";
        public bool Hidden;

        public Style Clone() => (Style)MemberwiseClone();
    }

    sealed class Reader(XElement root, string? svgDir, string imageDir)
    {
        public SvgImportResult Result { get; } = new();

        readonly Dictionary<string, XElement> _ids = new();
        readonly Dictionary<string, Dictionary<string, string>> _classes = new();
        readonly HashSet<XElement> _using = new();
        double _viewportWidth = 100, _viewportHeight = 100;
        int _images;

        public void Run()
        {
            foreach (var e in root.DescendantsAndSelf())
                if ((string?)e.Attribute("id") is { Length: > 0 } id)
                    _ids.TryAdd(id, e);
            foreach (var style in root.Descendants().Where(e => e.Name.LocalName == "style"))
                ParseCss(style.Value);

            var ctm = RootTransform();
            var style0 = new Style();
            Walk(root.Elements(), ctm, ApplyStyle(root, style0), 1, areas: false, clip: null);
        }

        /// <summary>Abbildung der Benutzereinheiten auf mm (width/height und viewBox).</summary>
        Affine RootTransform()
        {
            var viewBox = ParseNumbers((string?)root.Attribute("viewBox"));
            double? w = LengthToMm((string?)root.Attribute("width")), h = LengthToMm((string?)root.Attribute("height"));
            if (viewBox.Count == 4 && viewBox[2] > 0 && viewBox[3] > 0)
            {
                _viewportWidth = viewBox[2];
                _viewportHeight = viewBox[3];
                // Ohne Größenangabe: eine Benutzereinheit = 1 px
                w ??= h != null ? h * viewBox[2] / viewBox[3] : viewBox[2] * MmPerPx;
                h ??= w * viewBox[3] / viewBox[2];
                Result.WidthMm = w;
                Result.HeightMm = h;
                return Affine.Scale(w.Value / viewBox[2], h.Value / viewBox[3]).Then(Affine.Translate(-viewBox[0], -viewBox[1]));
            }
            Result.WidthMm = w;
            Result.HeightMm = h;
            if (w != null)
                _viewportWidth = w.Value / MmPerPx;
            if (h != null)
                _viewportHeight = h.Value / MmPerPx;
            return Affine.Scale(MmPerPx, MmPerPx);
        }

        /// <summary>Länge der Wurzel (width/height) in mm; ohne Einheit px.</summary>
        static double? LengthToMm(string? s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Trim().EndsWith('%'))
                return null;
            var m = Regex.Match(s.Trim(), @"^([-+]?[\d.]+(?:[eE][-+]?\d+)?)\s*([a-z]*)$");
            if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return null;
            return m.Groups[2].Value switch
            {
                "mm" => v,
                "cm" => v * 10,
                "in" => v * 25.4,
                "pt" => v * 25.4 / 72,
                "pc" => v * 25.4 / 6,
                _ => v * MmPerPx,
            };
        }

        /// <summary>Länge in Benutzereinheiten (px); Prozent bezogen auf <paramref name="percentOf"/>.</summary>
        double Length(string? s, double fallback = 0, double? percentOf = null, double fontSize = 16)
        {
            if (string.IsNullOrWhiteSpace(s))
                return fallback;
            var m = Regex.Match(s.Trim(), @"^([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*([a-z%]*)");
            if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return fallback;
            return m.Groups[2].Value switch
            {
                "mm" => v / MmPerPx,
                "cm" => v * 10 / MmPerPx,
                "in" => v * 96,
                "pt" => v * 96 / 72,
                "pc" => v * 16,
                "em" => v * fontSize,
                "ex" => v * fontSize / 2,
                "%" => v / 100 * (percentOf ?? Math.Sqrt((_viewportWidth * _viewportWidth + _viewportHeight * _viewportHeight) / 2)),
                _ => v,
            };
        }

        void ParseCss(string css)
        {
            css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
            foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^}]*)\}"))
            {
                var declarations = ParseDeclarations(rule.Groups[2].Value);
                // Nur einfache Klassen-Selektoren („.a, .b“)
                foreach (var selector in rule.Groups[1].Value.Split(','))
                {
                    var m = Regex.Match(selector.Trim(), @"^\.([\w-]+)$");
                    if (!m.Success)
                        continue;
                    if (!_classes.TryGetValue(m.Groups[1].Value, out var target))
                        _classes[m.Groups[1].Value] = target = new();
                    foreach (var (k, v) in declarations)
                        target[k] = v;
                }
            }
        }

        static Dictionary<string, string> ParseDeclarations(string text)
        {
            var result = new Dictionary<string, string>();
            foreach (var part in text.Split(';'))
            {
                int colon = part.IndexOf(':');
                if (colon > 0)
                    result[part[..colon].Trim().ToLowerInvariant()] = part[(colon + 1)..].Trim().Replace("!important", "").Trim();
            }
            return result;
        }

        static readonly string[] StyleProperties =
        [
            "fill", "stroke", "stroke-width", "fill-opacity", "stroke-opacity", "opacity", "font-family", "font-size",
            "font-weight", "font-style", "text-anchor", "display", "visibility", "font",
        ];

        /// <summary>Stil-Angaben des Elements: Attribute, dann CSS-Klassen, dann style-Attribut.</summary>
        Dictionary<string, string> Declarations(XElement e)
        {
            var result = new Dictionary<string, string>();
            foreach (var name in StyleProperties)
                if ((string?)e.Attribute(name) is { } v)
                    result[name] = v.Trim();
            foreach (var cls in ((string?)e.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (_classes.TryGetValue(cls, out var rules))
                    foreach (var (k, v) in rules)
                        result[k] = v;
            if ((string?)e.Attribute("style") is { } style)
                foreach (var (k, v) in ParseDeclarations(style))
                    result[k] = v;
            return result;
        }

        Style ApplyStyle(XElement e, Style parent) => ApplyStyle(e, parent, out _);

        Style ApplyStyle(XElement e, Style parent, out double opacity)
        {
            var s = parent.Clone();
            var d = Declarations(e);
            opacity = 1;
            if (d.TryGetValue("font-size", out var fs))
                s.FontSize = FontSize(fs, parent.FontSize);
            if (d.TryGetValue("fill", out var fill))
                s.Fill = ParsePaint(fill, parent.Fill);
            if (d.TryGetValue("stroke", out var stroke))
                s.Stroke = ParsePaint(stroke, parent.Stroke);
            if (d.TryGetValue("stroke-width", out var sw))
                s.StrokeWidth = Length(sw, s.StrokeWidth, fontSize: s.FontSize);
            if (d.TryGetValue("fill-opacity", out var fo))
                s.FillOpacity = Opacity(fo);
            if (d.TryGetValue("stroke-opacity", out var so))
                s.StrokeOpacity = Opacity(so);
            if (d.TryGetValue("opacity", out var o))
                opacity = Opacity(o);
            if (d.TryGetValue("font-family", out var ff))
                s.FontFamily = FontFamily(ff);
            if (d.TryGetValue("font-weight", out var fw))
                s.Bold = fw is "bold" or "bolder" || (int.TryParse(fw, out var w) && w >= 600);
            if (d.TryGetValue("font-style", out var fst))
                s.Italic = fst is "italic" or "oblique";
            if (d.TryGetValue("text-anchor", out var ta))
                s.TextAnchor = ta;
            if (d.TryGetValue("display", out var display) && display == "none")
                s.Hidden = true;
            if (d.TryGetValue("visibility", out var visibility))
                s.Hidden = s.Hidden || visibility is "hidden" or "collapse";
            return s;
        }

        double FontSize(string value, double parent) => value switch
        {
            "smaller" => parent / 1.2,
            "larger" => parent * 1.2,
            _ => Length(value, parent, parent, parent),
        };

        static double Opacity(string value)
        {
            value = value.Trim();
            bool percent = value.EndsWith('%');
            if (!double.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return 1;
            return Math.Clamp(percent ? v / 100 : v, 0, 1);
        }

        static string FontFamily(string value)
        {
            var first = value.Split(',')[0].Trim().Trim('\'', '"');
            // Allgemeine Familien: Standardschrift
            return first is "sans-serif" or "serif" or "monospace" or "cursive" or "fantasy" or "system-ui" ? "" : first;
        }

        Paint ParsePaint(string value, Paint parent)
        {
            value = value.Trim();
            if (value is "none" or "transparent")
                return new Paint(null);
            if (value is "inherit")
                return parent;
            if (value is "currentColor")
                return new Paint(Avalonia.Media.Colors.Black);
            var url = Regex.Match(value, @"url\(\s*['""]?#([^)'""]+)['""]?\s*\)\s*(.*)");
            if (url.Success)
            {
                if (GradientColor(url.Groups[1].Value) is { } c)
                    return new Paint(c);
                // Ersatzfarbe nach der URL
                return url.Groups[2].Value.Length > 0 ? ParsePaint(url.Groups[2].Value, parent) : new Paint(null);
            }
            return ParseColor(value) is { } color ? new Paint(color) : parent;
        }

        /// <summary>Verläufe und Muster werden durch die Farbe ihres ersten Haltepunkts ersetzt.</summary>
        AvColor? GradientColor(string id)
        {
            for (int depth = 0; depth < 10 && _ids.TryGetValue(id, out var g); depth++)
            {
                var stop = g.Elements().FirstOrDefault(e => e.Name.LocalName == "stop");
                if (stop != null)
                {
                    var d = Declarations(stop);
                    var color = ParseColor(d.GetValueOrDefault("stop-color") ?? (string?)stop.Attribute("stop-color") ?? "black")
                        ?? Avalonia.Media.Colors.Black;
                    double opacity = Opacity(d.GetValueOrDefault("stop-opacity") ?? (string?)stop.Attribute("stop-opacity") ?? "1");
                    return AvColor.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
                }
                if (Href(g) is not { } next)
                    break;
                id = next;
            }
            return null;
        }

        static AvColor? ParseColor(string value)
        {
            value = value.Trim();
            var rgb = Regex.Match(value, @"^rgba?\(\s*([^,\s]+)[\s,]+([^,\s]+)[\s,]+([^,\s/)]+)(?:[\s,/]+([^)\s]+))?\s*\)$");
            if (rgb.Success)
            {
                byte Channel(string s) => s.EndsWith('%')
                    ? (byte)Math.Clamp(Math.Round(double.Parse(s.TrimEnd('%'), CultureInfo.InvariantCulture) * 2.55), 0, 255)
                    : (byte)Math.Clamp(Math.Round(double.Parse(s, CultureInfo.InvariantCulture)), 0, 255);
                try
                {
                    byte a = rgb.Groups[4].Success ? (byte)Math.Round(Opacity(rgb.Groups[4].Value) * 255) : (byte)255;
                    return AvColor.FromArgb(a, Channel(rgb.Groups[1].Value), Channel(rgb.Groups[2].Value), Channel(rgb.Groups[3].Value));
                }
                catch (FormatException)
                {
                    return null;
                }
            }
            // #RRGGBBAA (CSS) statt #AARRGGBB (Avalonia)
            if (Regex.IsMatch(value, "^#[0-9a-fA-F]{8}$"))
                value = "#" + value[7..9] + value[1..7];
            return AvColor.TryParse(value, out var c) ? c : null;
        }

        static string? Href(XElement e) =>
            ((string?)e.Attribute(XLink + "href") ?? (string?)e.Attribute("href")) is { } h && h.StartsWith('#') ? h[1..] : null;

        // ---------- Durchlauf ----------

        void Walk(IEnumerable<XElement> elements, Affine ctm, Style style, double opacity, bool areas, (double X, double Y, double W, double H)? clip)
        {
            foreach (var e in elements)
            {
                if (e.Name.Namespace != Svg && e.Name.Namespace != XNamespace.None)
                    continue;
                bool areaLayer = !areas && IsAreaLayer(e);
                var s = ApplyStyle(e, style, out double o);
                // Die OID-Ebene ist beim Export ausgeblendet
                if (s.Hidden && !areaLayer)
                    continue;
                s.Hidden = false;
                var m = ctm.Then(ParseTransform((string?)e.Attribute("transform")));
                double op = opacity * o;
                _current = e;
                switch (e.Name.LocalName)
                {
                    case "g" or "a" or "switch":
                        Walk(e.Elements(), m, s, op, areas || areaLayer, null);
                        break;
                    case "svg":
                        NestedSvg(e, m, s, op, areas);
                        break;
                    case "use":
                        if (Href(e) is { } id && _ids.TryGetValue(id, out var target) && _using.Add(target))
                        {
                            var t = m.Then(Affine.Translate(Length((string?)e.Attribute("x")), Length((string?)e.Attribute("y"))));
                            if (target.Name.LocalName == "symbol")
                                Walk(target.Elements(), t, ApplyStyle(target, s), op, areas, null);
                            else
                                Walk([target], t, s, op, areas, null);
                            _using.Remove(target);
                        }
                        break;
                    case "rect":
                        Rect(e, m, s, op, areas);
                        break;
                    case "circle" or "ellipse":
                        Ellipse(e, m, s, op, areas);
                        break;
                    case "line":
                        Shape([new Subpath([P(m, e, "x1", "y1"), P(m, e, "x2", "y2")], false)], m, s, op, areas);
                        break;
                    case "polyline" or "polygon":
                        var numbers = ParseNumbers((string?)e.Attribute("points"));
                        var pts = new List<Vertex>();
                        for (int i = 0; i + 1 < numbers.Count; i += 2)
                        {
                            var (x, y) = m.Apply(numbers[i], numbers[i + 1]);
                            pts.Add(new Vertex(x, y));
                        }
                        Shape([new Subpath(pts, e.Name.LocalName == "polygon")], m, s, op, areas);
                        break;
                    case "path":
                        try
                        {
                            Shape(PathParser.Parse((string?)e.Attribute("d") ?? "", m, CurveTolerance), m, s, op, areas);
                        }
                        catch (FormatException ex)
                        {
                            Result.Warnings.Add($"Pfad übersprungen: {ex.Message}");
                        }
                        break;
                    case "text" when !areas:
                        Text(e, m, s, op);
                        break;
                    case "image" when !areas:
                        Image(e, m, op, clip);
                        break;
                }
            }
        }

        static bool IsAreaLayer(XElement e) =>
            (string?)e.Attribute("data-tupfer") == "oid-areas"
            || ((string?)e.Attribute(Inkscape + "label") is { } label && label.Contains("OID", StringComparison.OrdinalIgnoreCase));

        Vertex P(Affine m, XElement e, string xName, string yName)
        {
            var (x, y) = m.Apply(Length((string?)e.Attribute(xName), 0, _viewportWidth), Length((string?)e.Attribute(yName), 0, _viewportHeight));
            return new Vertex(x, y);
        }

        void NestedSvg(XElement e, Affine m, Style s, double op, bool areas)
        {
            double x = Length((string?)e.Attribute("x"), 0, _viewportWidth), y = Length((string?)e.Attribute("y"), 0, _viewportHeight);
            double w = Length((string?)e.Attribute("width"), _viewportWidth, _viewportWidth);
            double h = Length((string?)e.Attribute("height"), _viewportHeight, _viewportHeight);
            var viewBox = ParseNumbers((string?)e.Attribute("viewBox"));
            var t = m.Then(Affine.Translate(x, y));
            (double, double, double, double) clip = (0, 0, w, h);
            if (viewBox.Count == 4 && viewBox[2] > 0 && viewBox[3] > 0)
            {
                // preserveAspectRatio wird wie „none“ behandelt
                t = t.Then(Affine.Scale(w / viewBox[2], h / viewBox[3])).Then(Affine.Translate(-viewBox[0], -viewBox[1]));
                clip = (viewBox[0], viewBox[1], viewBox[2], viewBox[3]);
            }
            Walk(e.Elements(), t, s, op, areas, clip);
        }

        void Rect(XElement e, Affine m, Style s, double op, bool areas)
        {
            double x = Length((string?)e.Attribute("x"), 0, _viewportWidth), y = Length((string?)e.Attribute("y"), 0, _viewportHeight);
            double w = Length((string?)e.Attribute("width"), 0, _viewportWidth), h = Length((string?)e.Attribute("height"), 0, _viewportHeight);
            if (w <= 0 || h <= 0)
                return;
            string? rxs = (string?)e.Attribute("rx"), rys = (string?)e.Attribute("ry");
            double rx = Length(rxs ?? rys, 0, _viewportWidth), ry = Length(rys ?? rxs, 0, _viewportHeight);
            double r = Math.Min(Math.Min(rx, ry), Math.Min(w, h) / 2);
            if (r > 0)
            {
                // Abgerundete Ecken als Viertelkreise (tan(90° / 4))
                const double quarter = 0.41421356;
                var corners = new (double X, double Y, double B)[]
                {
                    (x + r, y, 0), (x + w - r, y, quarter), (x + w, y + r, 0), (x + w, y + h - r, quarter),
                    (x + w - r, y + h, 0), (x + r, y + h, quarter), (x, y + h - r, 0), (x, y + r, quarter),
                };
                Shape([new Subpath(Transform(m, corners), true)], m, s, op, areas);
                return;
            }
            if (m.NoSkew)
                Frame(AreaShape.Rectangle, x, y, w, h, m, s, op, areas);
            else
                Shape([new Subpath(Transform(m, [(x, y, 0), (x + w, y, 0), (x + w, y + h, 0), (x, y + h, 0)]), true)], m, s, op, areas);
        }

        void Ellipse(XElement e, Affine m, Style s, double op, bool areas)
        {
            double cx = Length((string?)e.Attribute("cx"), 0, _viewportWidth), cy = Length((string?)e.Attribute("cy"), 0, _viewportHeight);
            double rx, ry;
            if (e.Name.LocalName == "circle")
                rx = ry = Length((string?)e.Attribute("r"));
            else
            {
                string? rxs = (string?)e.Attribute("rx"), rys = (string?)e.Attribute("ry");
                rx = Length(rxs ?? rys, 0, _viewportWidth);
                ry = Length(rys ?? rxs, 0, _viewportHeight);
            }
            if (rx <= 0 || ry <= 0)
                return;
            if (m.NoSkew)
            {
                Frame(AreaShape.Ellipse, cx - rx, cy - ry, 2 * rx, 2 * ry, m, s, op, areas);
                return;
            }
            var d = string.Create(CultureInfo.InvariantCulture,
                $"M{cx - rx},{cy} A{rx},{ry} 0 0 1 {cx + rx},{cy} A{rx},{ry} 0 0 1 {cx - rx},{cy} Z");
            Shape(PathParser.Parse(d, m, CurveTolerance), m, s, op, areas);
        }

        static List<Vertex> Transform(Affine m, IEnumerable<(double X, double Y, double B)> points)
        {
            double sign = m.Det < 0 ? -1 : 1;
            return points.Select(p =>
            {
                var (x, y) = m.Apply(p.X, p.Y);
                return new Vertex(x, y, p.B * sign);
            }).ToList();
        }

        /// <summary>Rechteck oder Ellipse (ungedreht im Benutzerraum) als gedrehter Rahmen auf der Seite.</summary>
        void Frame(AreaShape shape, double x, double y, double w, double h, Affine m, Style s, double op, bool areas)
        {
            var (cx, cy) = m.Apply(x + w / 2, y + h / 2);
            double width = w * m.ScaleX, height = h * Math.Abs(m.ScaleY);
            double rotation = Normalize(m.AngleDegrees);
            if (areas)
            {
                if (Math.Abs(rotation) > 0.01 && !(shape == AreaShape.Ellipse && Math.Abs(width - height) < 1e-6))
                {
                    // OID-Flächen lassen sich nicht drehen: Umriss als Polygon
                    var outline = AreaGeometry.Outline(shape, x, y, w, h, null, CurveTolerance / Math.Max(m.MeanScale, 1e-9));
                    AddArea(outline.Select(p => { var (px, py) = m.Apply(p.X, p.Y); return new Vertex(px, py); }).ToList(), m);
                    return;
                }
                var area = NewArea();
                Result.Areas.Add(area);
                area.Shape = shape;
                area.X = Round(cx - width / 2);
                area.Y = Round(cy - height / 2);
                area.Width = Round(width);
                area.Height = Round(height);
                return;
            }
            if (ShapeEntry(s, op, m) is not { } entry)
                return;
            entry.Shape = shape;
            entry.X = Round(cx - width / 2);
            entry.Y = Round(cy - height / 2);
            entry.Width = Round(width);
            entry.Height = Round(height);
            entry.Rotation = Math.Round(rotation, 2);
            Result.Elements.Add(entry);
        }

        static double Normalize(double degrees) => degrees - 360 * Math.Round(degrees / 360);

        static double Round(double v) => Math.Round(v, 3);

        /// <summary>Farbfläche mit Füllung und Rand aus dem Stil, null wenn weder noch.</summary>
        ElementEntry? ShapeEntry(Style s, double op, Affine m, bool stroke = true)
        {
            var fill = s.Fill.Color;
            var line = stroke ? s.Stroke.Color : null;
            if (fill == null && (line == null || s.StrokeWidth <= 0))
                return null;
            var entry = new ElementEntry
            {
                Kind = ElementKind.Shape,
                Color = fill is { } f ? ColorString(f, s.FillOpacity * op) : "#00FFFFFF",
            };
            if (line is { } l && s.StrokeWidth > 0)
            {
                entry.StrokeWidth = Round(s.StrokeWidth * m.MeanScale);
                entry.StrokeColor = ColorString(l, s.StrokeOpacity * op);
            }
            return entry;
        }

        static string ColorString(AvColor c, double opacity)
        {
            byte a = (byte)Math.Round(c.A * Math.Clamp(opacity, 0, 1));
            return a == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{a:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        /// <summary>Gerade gelesenes Element (für die Angaben der OID-Flächen).</summary>
        XElement? _current;

        /// <summary>OID-Fläche mit Ziel, Skript und Beschriftung aus data-target, data-script und data-label.</summary>
        AreaEntry NewArea() => new()
        {
            Target = Enum.TryParse<AreaTarget>((string?)_current?.Attribute("data-target"), out var target) ? target : AreaTarget.Script,
            Script = (string?)_current?.Attribute("data-script") ?? "",
            Label = (string?)_current?.Attribute("data-label") ?? "",
        };

        /// <summary>Linienzüge und Flächen eines Pfads.</summary>
        void Shape(List<Subpath> subpaths, Affine m, Style s, double op, bool areas)
        {
            foreach (var sub in subpaths)
            {
                var points = Clean(sub.Points, sub.Closed);
                // Zwei Ecken mit Bogen (z. B. Halbkreis): Bögen teilen, ein Polygon braucht drei Ecken
                if (points.Count == 2 && points.Any(p => p.Bulge != 0))
                    points = SplitArcs(points);
                if (areas)
                {
                    if (points.Count >= 3)
                        AddArea(points, m);
                    continue;
                }
                bool filled = s.Fill.Color != null && points.Count >= 3;
                bool stroked = s.Stroke.Color != null && s.StrokeWidth > 0 && points.Count >= 2;
                if (sub.Closed || !stroked)
                {
                    if (points.Count >= 3 && ShapeEntry(s, op, m) is { } entry)
                        AddPolygon(entry, points);
                    continue;
                }
                // Offener Linienzug: Füllung (geschlossen gedacht) und Linie als Fläche in Strichbreite
                if (filled && ShapeEntry(s, op, m, stroke: false) is { } fill)
                    AddPolygon(fill, points);
                var outline = StrokeOutline(Flatten(points), s.StrokeWidth * m.MeanScale);
                if (outline.Count >= 3)
                    AddPolygon(new ElementEntry { Kind = ElementKind.Shape, Color = ColorString(s.Stroke.Color!.Value, s.StrokeOpacity * op) }, outline);
            }
        }

        static List<Vertex> Clean(List<Vertex> points, bool closed)
        {
            var result = new List<Vertex>();
            foreach (var p in points)
            {
                if (result.Count > 0 && Near(result[^1], p))
                {
                    // Doppelter Punkt: Bogen der weggefallenen Kante übernehmen
                    if (p.Bulge != 0)
                        result[^1] = result[^1] with { Bulge = p.Bulge };
                    continue;
                }
                result.Add(p);
            }
            if (closed && result.Count > 1 && Near(result[0], result[^1]))
            {
                var last = result[^1];
                result.RemoveAt(result.Count - 1);
                if (last.Bulge != 0)
                    result[^1] = result[^1] with { Bulge = last.Bulge };
            }
            return result;
        }

        static List<Vertex> SplitArcs(List<Vertex> points)
        {
            var result = new List<Vertex>();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var q = points[(i + 1) % points.Count];
                if (p.Bulge == 0)
                {
                    result.Add(p);
                    continue;
                }
                double half = AreaGeometry.HalfBulge(p.Bulge);
                var (ax, ay) = AreaGeometry.Apex(p.X, p.Y, q.X, q.Y, p.Bulge);
                result.Add(p with { Bulge = half });
                result.Add(new Vertex(ax, ay, half));
            }
            return result;
        }

        static bool Near(Vertex a, Vertex b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

        /// <summary>Bögen in Punkte auflösen.</summary>
        static List<Vertex> Flatten(List<Vertex> points)
        {
            var result = new List<Vertex>();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                result.Add(p with { Bulge = 0 });
                if (i + 1 < points.Count)
                    foreach (var (x, y) in AreaGeometry.ArcPoints(p.X, p.Y, points[i + 1].X, points[i + 1].Y, p.Bulge, CurveTolerance))
                        result.Add(new Vertex(x, y));
            }
            return result;
        }

        /// <summary>Umriss einer offenen Linie der Breite <paramref name="width"/> (Ecken auf Gehrung, begrenzt).</summary>
        static List<Vertex> StrokeOutline(List<Vertex> line, double width)
        {
            double half = width / 2;
            var left = new List<Vertex>();
            var right = new List<Vertex>();
            for (int i = 0; i < line.Count; i++)
            {
                var (nx, ny) = (0.0, 0.0);
                if (i > 0)
                    (nx, ny) = Add((nx, ny), Normal(line[i - 1], line[i]));
                if (i + 1 < line.Count)
                    (nx, ny) = Add((nx, ny), Normal(line[i], line[i + 1]));
                double len = Math.Sqrt(nx * nx + ny * ny);
                if (len < 1e-9)
                    continue;
                (nx, ny) = (nx / len, ny / len);
                // Gehrung: Abstand so verlängern, dass die Kanten parallel bleiben (höchstens 4-fach)
                double factor = 1.0;
                if (i > 0 && i + 1 < line.Count)
                {
                    var (ax, ay) = Normal(line[i - 1], line[i]);
                    double cos = nx * ax + ny * ay;
                    factor = Math.Min(4, 1 / Math.Max(cos, 0.25));
                }
                left.Add(new Vertex(line[i].X + nx * half * factor, line[i].Y + ny * half * factor));
                right.Add(new Vertex(line[i].X - nx * half * factor, line[i].Y - ny * half * factor));
            }
            right.Reverse();
            return [.. left, .. right];

            static (double, double) Add((double X, double Y) a, (double X, double Y) b) => (a.X + b.X, a.Y + b.Y);
        }

        static (double X, double Y) Normal(Vertex a, Vertex b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            return len < 1e-12 ? (0, 0) : (-dy / len, dx / len);
        }

        void AddPolygon(ElementEntry entry, List<Vertex> points)
        {
            var (x, y, w, h, relative) = Polygon(points);
            entry.Shape = AreaShape.Polygon;
            entry.X = x;
            entry.Y = y;
            entry.Width = w;
            entry.Height = h;
            entry.Points = relative;
            Result.Elements.Add(entry);
        }

        void AddArea(List<Vertex> points, Affine m)
        {
            var (x, y, w, h, relative) = Polygon(points);
            var area = NewArea();
            area.Shape = AreaShape.Polygon;
            area.X = x;
            area.Y = y;
            area.Width = w;
            area.Height = h;
            area.Points = relative;
            Result.Areas.Add(area);
        }

        /// <summary>Rahmen (um den Umriss mit Bögen) und Punkte relativ dazu.</summary>
        static (double X, double Y, double W, double H, List<AreaPoint> Points) Polygon(List<Vertex> points)
        {
            var absolute = points.Select(p => new AreaPoint(p.X, p.Y, Math.Clamp(p.Bulge, -AreaGeometry.MaxBulge, AreaGeometry.MaxBulge))).ToList();
            var (bx, by, bw, bh) = AreaGeometry.Bounds(AreaGeometry.Outline(AreaShape.Polygon, 0, 0, 0, 0, absolute));
            double x = Round(bx), y = Round(by);
            return (x, y, Round(bw), Round(bh),
                absolute.Select(p => p with { X = Math.Round(p.X - x, 3), Y = Math.Round(p.Y - y, 3), Bulge = Math.Round(p.Bulge, 4) }).ToList());
        }

        // ---------- Text ----------

        void Text(XElement e, Affine m, Style s, double op)
        {
            var lines = new List<string> { "" };
            bool preserve = Preserve(e, false);
            double x = FirstLength((string?)e.Attribute("x")), y = FirstLength((string?)e.Attribute("y"));
            var style = s;
            bool first = true;

            static bool Preserve(XElement e, bool inherited) =>
                (string?)e.Attribute(XNamespace.Xml + "space") is { } space ? space == "preserve" : inherited;

            void Collect(XElement node, Style nodeStyle, bool keepSpaces)
            {
                foreach (var child in node.Nodes())
                {
                    if (child is XText t)
                        lines[^1] += keepSpaces ? t.Value.Replace('\n', ' ') : Regex.Replace(t.Value, @"\s+", " ");
                    else if (child is XElement span && span.Name.LocalName is "tspan" or "textPath" && !ApplyStyle(span, nodeStyle).Hidden)
                    {
                        // Ein tspan mit eigener Lage beginnt eine neue Zeile
                        bool positioned = span.Attribute("x") != null || span.Attribute("y") != null || span.Attribute("dy") != null;
                        if (positioned && first && lines[^1].Trim().Length == 0)
                        {
                            // Erste Zeile: Lage und Stil vom ersten tspan
                            if (span.Attribute("x") != null)
                                x = FirstLength((string?)span.Attribute("x"));
                            if (span.Attribute("y") != null)
                                y = FirstLength((string?)span.Attribute("y"));
                            style = ApplyStyle(span, nodeStyle);
                            lines[^1] = "";
                        }
                        else if (positioned)
                            lines.Add("");
                        first = false;
                        // Leerraum vor einer neuen Zeile gehört zur Einrückung der Datei
                        if (positioned && !keepSpaces)
                            lines[^1] = lines[^1].TrimStart();
                        Collect(span, ApplyStyle(span, nodeStyle), Preserve(span, keepSpaces));
                    }
                }
            }

            Collect(e, s, preserve);
            // Einrückung zwischen den Zeilen (außerhalb von xml:space="preserve") entfernen
            lines = lines.Select(l => preserve ? l : l.Trim()).ToList();
            while (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            if (lines.Count == 0 || style.Fill.Color is not { } color)
                return;

            double scale = m.MeanScale;
            double sizeMm = style.FontSize * scale;
            if (sizeMm <= 0)
                return;
            // Breite grob schätzen; die Oberfläche misst beim Einfügen nach
            double width = Math.Max(5, lines.Max(l => l.Length) * sizeMm * 0.6 + 1);
            double height = lines.Count * sizeMm * LineHeightFactor;
            var align = style.TextAnchor switch { "middle" => TextAlign.Center, "end" => TextAlign.Right, _ => TextAlign.Left };
            double f = align switch { TextAlign.Center => 0.5, TextAlign.Right => 1, _ => 0 };
            // Rahmen im ungedrehten Textsystem (mm), Mitte auf die Seite abbilden
            double ux = x * scale, uy = y * scale;
            double left = ux - f * width, top = uy - AscentFactor * sizeMm;
            var rot = new Affine(m.A / scale, m.B / scale, m.C / scale, m.D / scale, m.E, m.F);
            var (cx, cy) = rot.Apply(left + width / 2, top + height / 2);
            Result.Elements.Add(new ElementEntry
            {
                Kind = ElementKind.Text,
                Text = string.Join("\n", lines),
                FontFamily = style.FontFamily,
                FontSize = Math.Round(sizeMm * 72 / 25.4, 2),
                Bold = style.Bold,
                Italic = style.Italic,
                Align = align,
                Color = ColorString(color, style.FillOpacity * op),
                X = Round(cx - width / 2),
                Y = Round(cy - height / 2),
                Width = Round(width),
                Height = Round(height),
                Rotation = Math.Round(Normalize(m.AngleDegrees), 2),
            });
        }

        double FirstLength(string? list) =>
            list == null ? 0 : Length(list.Split([' ', ',', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());

        // ---------- Bilder ----------

        void Image(XElement e, Affine m, double op, (double X, double Y, double W, double H)? clip)
        {
            double x = Length((string?)e.Attribute("x"), 0, _viewportWidth), y = Length((string?)e.Attribute("y"), 0, _viewportHeight);
            double w = Length((string?)e.Attribute("width"), 0, _viewportWidth), h = Length((string?)e.Attribute("height"), 0, _viewportHeight);
            if (w <= 0 || h <= 0)
                return;
            var href = (string?)e.Attribute(XLink + "href") ?? (string?)e.Attribute("href");
            if (string.IsNullOrWhiteSpace(href) || ImageFile(href) is not { } path)
                return;
            // Sichtbarer Ausschnitt (verschachteltes svg schneidet zu)
            double vx = x, vy = y, vw = w, vh = h;
            if (clip is { } c)
            {
                double l = Math.Max(x, c.X), t = Math.Max(y, c.Y), r = Math.Min(x + w, c.X + c.W), b = Math.Min(y + h, c.Y + c.H);
                if (r <= l || b <= t)
                    return;
                (vx, vy, vw, vh) = (l, t, r - l, b - t);
            }
            if (!m.NoSkew)
                Result.Warnings.Add("Ein verzerrtes Bild wurde ohne Scherung übernommen.");
            var (cx, cy) = m.Apply(vx + vw / 2, vy + vh / 2);
            double width = vw * m.ScaleX, height = vh * Math.Abs(m.ScaleY);
            Result.Elements.Add(new ElementEntry
            {
                Kind = ElementKind.Image,
                ImagePath = path,
                X = Round(cx - width / 2),
                Y = Round(cy - height / 2),
                Width = Round(width),
                Height = Round(height),
                Rotation = Math.Round(Normalize(m.AngleDegrees), 2),
                CropX = Math.Round((vx - x) / w, 5),
                CropY = Math.Round((vy - y) / h, 5),
                CropWidth = Math.Round(vw / w, 5),
                CropHeight = Math.Round(vh / h, 5),
            });
            if (op < 1)
                Result.Warnings.Add("Die Deckkraft von Bildern wird nicht übernommen.");
        }

        /// <summary>Eingebettetes Bild als Datei speichern bzw. verlinkte Datei finden; null = nicht verfügbar.</summary>
        string? ImageFile(string href)
        {
            var data = Regex.Match(href, @"^data:image/([\w.+-]+)(;[^,]*)?,(.*)$", RegexOptions.Singleline);
            if (data.Success)
            {
                var ext = data.Groups[1].Value.ToLowerInvariant() switch
                {
                    "png" => ".png",
                    "jpeg" or "jpg" => ".jpg",
                    "gif" => ".gif",
                    "webp" => ".webp",
                    "bmp" => ".bmp",
                    _ => null,
                };
                if (ext == null)
                {
                    Result.Warnings.Add($"Eingebettetes Bild im Format „{data.Groups[1].Value}“ wird nicht unterstützt.");
                    return null;
                }
                byte[] bytes;
                try
                {
                    var payload = data.Groups[3].Value;
                    bytes = data.Groups[2].Value.Contains("base64")
                        ? Convert.FromBase64String(Regex.Replace(payload, @"\s+", ""))
                        : System.Text.Encoding.Latin1.GetBytes(Uri.UnescapeDataString(payload));
                }
                catch (FormatException)
                {
                    Result.Warnings.Add("Ein eingebettetes Bild ist beschädigt.");
                    return null;
                }
                Directory.CreateDirectory(imageDir);
                string path;
                do
                    path = Path.Combine(imageDir, $"svg-bild-{++_images}{ext}");
                while (File.Exists(path));
                File.WriteAllBytes(path, bytes);
                return path;
            }
            var file = href.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? new Uri(href).LocalPath : Uri.UnescapeDataString(href);
            if (!Path.IsPathRooted(file) && svgDir != null)
                file = Path.GetFullPath(file, svgDir);
            if (Path.IsPathRooted(file) && File.Exists(file))
                return file;
            Result.Warnings.Add($"Bild „{href}“ nicht gefunden.");
            return null;
        }
    }

    // ---------- Transformationen und Zahlen ----------

    static Affine ParseTransform(string? s)
    {
        var result = Affine.Identity;
        if (string.IsNullOrWhiteSpace(s))
            return result;
        foreach (Match m in Regex.Matches(s, @"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)"))
        {
            var v = ParseNumbers(m.Groups[2].Value);
            double V(int i, double fallback = 0) => i < v.Count ? v[i] : fallback;
            var t = m.Groups[1].Value switch
            {
                "matrix" when v.Count == 6 => new Affine(v[0], v[1], v[2], v[3], v[4], v[5]),
                "translate" => Affine.Translate(V(0), V(1)),
                "scale" => Affine.Scale(V(0, 1), V(1, V(0, 1))),
                "rotate" when v.Count >= 3 => Affine.Translate(v[1], v[2]).Then(Affine.Rotate(v[0])).Then(Affine.Translate(-v[1], -v[2])),
                "rotate" => Affine.Rotate(V(0)),
                "skewX" => new Affine(1, 0, Math.Tan(V(0) * Math.PI / 180), 1, 0, 0),
                "skewY" => new Affine(1, Math.Tan(V(0) * Math.PI / 180), 0, 1, 0, 0),
                _ => Affine.Identity,
            };
            result = result.Then(t);
        }
        return result;
    }

    static List<double> ParseNumbers(string? s)
    {
        var result = new List<double>();
        if (s == null)
            return result;
        foreach (Match m in Regex.Matches(s, @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?"))
            result.Add(double.Parse(m.Value, CultureInfo.InvariantCulture));
        return result;
    }

    /// <summary>SVG-Pfaddaten (alle Befehle, auch relativ) als Linienzüge in Seitenkoordinaten.</summary>
    static class PathParser
    {
        public static List<Subpath> Parse(string d, Affine m, double toleranceMm)
        {
            var tokens = new Tokenizer(d);
            var result = new List<Subpath>();
            List<Vertex>? current = null;
            double x = 0, y = 0, startX = 0, startY = 0;
            // Letzter Kontrollpunkt für S/T
            double lastCx = 0, lastCy = 0;
            char lastCommand = ' ';
            char command = ' ';
            double tol = toleranceMm / Math.Max(m.MeanScale, 1e-12);
            double bulgeSign = m.Det < 0 ? -1 : 1;

            void LineTo(double nx, double ny)
            {
                current ??= Start(x, y);
                var (px, py) = m.Apply(nx, ny);
                current.Add(new Vertex(px, py));
            }

            List<Vertex> Start(double sx, double sy)
            {
                var (px, py) = m.Apply(sx, sy);
                var list = new List<Vertex> { new(px, py) };
                result.Add(new Subpath(list, false));
                return list;
            }

            while (tokens.More)
            {
                if (tokens.NextIsCommand)
                    command = tokens.Command();
                else if (command == ' ')
                    throw new FormatException("Pfad beginnt ohne Befehl.");
                bool rel = char.IsLower(command);
                double ox = rel ? x : 0, oy = rel ? y : 0;
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        x = ox + tokens.Number();
                        y = oy + tokens.Number();
                        startX = x;
                        startY = y;
                        current = Start(x, y);
                        // Weitere Koordinaten nach M sind Linien
                        command = rel ? 'l' : 'L';
                        lastCommand = 'M';
                        continue;
                    case 'L':
                        x = ox + tokens.Number();
                        y = oy + tokens.Number();
                        LineTo(x, y);
                        break;
                    case 'H':
                        x = ox + tokens.Number();
                        LineTo(x, y);
                        break;
                    case 'V':
                        y = oy + tokens.Number();
                        LineTo(x, y);
                        break;
                    case 'C' or 'S':
                    {
                        double c1x, c1y;
                        if (char.ToUpperInvariant(command) == 'C')
                        {
                            c1x = ox + tokens.Number();
                            c1y = oy + tokens.Number();
                        }
                        else
                        {
                            bool smooth = char.ToUpperInvariant(lastCommand) is 'C' or 'S';
                            c1x = smooth ? 2 * x - lastCx : x;
                            c1y = smooth ? 2 * y - lastCy : y;
                        }
                        double c2x = ox + tokens.Number(), c2y = oy + tokens.Number();
                        double ex = ox + tokens.Number(), ey = oy + tokens.Number();
                        Cubic(x, y, c1x, c1y, c2x, c2y, ex, ey);
                        (lastCx, lastCy) = (c2x, c2y);
                        (x, y) = (ex, ey);
                        break;
                    }
                    case 'Q' or 'T':
                    {
                        double qx, qy;
                        if (char.ToUpperInvariant(command) == 'Q')
                        {
                            qx = ox + tokens.Number();
                            qy = oy + tokens.Number();
                        }
                        else
                        {
                            bool smooth = char.ToUpperInvariant(lastCommand) is 'Q' or 'T';
                            qx = smooth ? 2 * x - lastCx : x;
                            qy = smooth ? 2 * y - lastCy : y;
                        }
                        double ex = ox + tokens.Number(), ey = oy + tokens.Number();
                        // Quadratisch als kubisch
                        Cubic(x, y, x + 2.0 / 3 * (qx - x), y + 2.0 / 3 * (qy - y), ex + 2.0 / 3 * (qx - ex), ey + 2.0 / 3 * (qy - ey), ex, ey);
                        (lastCx, lastCy) = (qx, qy);
                        (x, y) = (ex, ey);
                        break;
                    }
                    case 'A':
                    {
                        double rx = Math.Abs(tokens.Number()), ry = Math.Abs(tokens.Number()), phi = tokens.Number();
                        bool large = tokens.Flag(), sweep = tokens.Flag();
                        double ex = ox + tokens.Number(), ey = oy + tokens.Number();
                        Arc(x, y, rx, ry, phi, large, sweep, ex, ey);
                        (x, y) = (ex, ey);
                        break;
                    }
                    case 'Z':
                        if (current != null)
                        {
                            result[^1] = result[^1] with { Closed = true };
                            x = startX;
                            y = startY;
                            current = null;
                        }
                        lastCommand = 'Z';
                        continue;
                    default:
                        throw new FormatException($"Unbekannter Pfadbefehl „{command}“.");
                }
                lastCommand = command;
            }
            return result;

            void Cubic(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
            {
                double length = Dist(x0, y0, x1, y1) + Dist(x1, y1, x2, y2) + Dist(x2, y2, x3, y3);
                int n = Math.Clamp((int)Math.Ceiling(Math.Sqrt(length / (8 * tol)) * 2), 1, 200);
                for (int i = 1; i <= n; i++)
                {
                    double t = (double)i / n, u = 1 - t;
                    LineTo(u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3,
                        u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3);
                }
            }

            void Arc(double x0, double y0, double rx, double ry, double phiDeg, bool large, bool sweep, double x1, double y1)
            {
                if (rx < 1e-12 || ry < 1e-12)
                {
                    LineTo(x1, y1);
                    return;
                }
                // Mittelpunktdarstellung nach SVG 1.1, Anhang F.6.5
                double phi = phiDeg * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
                double dx = (x0 - x1) / 2, dy = (y0 - y1) / 2;
                double x1p = cos * dx + sin * dy, y1p = -sin * dx + cos * dy;
                double lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
                if (lambda > 1)
                {
                    rx *= Math.Sqrt(lambda);
                    ry *= Math.Sqrt(lambda);
                }
                double num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
                double den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
                double coef = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, num / den));
                double cxp = coef * rx * y1p / ry, cyp = -coef * ry * x1p / rx;
                double cx = cos * cxp - sin * cyp + (x0 + x1) / 2, cy = sin * cxp + cos * cyp + (y0 + y1) / 2;
                double theta1 = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
                double delta = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx) - theta1;
                if (sweep && delta < 0)
                    delta += 2 * Math.PI;
                else if (!sweep && delta > 0)
                    delta -= 2 * Math.PI;

                // Kreisbogen bei Ähnlichkeitsabbildung: als Bogen der Kante behalten
                if (m.IsSimilarity && Math.Abs(rx - ry) < 1e-9 * Math.Max(rx, 1) && Math.Abs(delta) <= 1.5 * Math.PI + 1e-9)
                {
                    current ??= Start(x0, y0);
                    current[^1] = current[^1] with { Bulge = Math.Tan(delta / 4) * bulgeSign };
                    LineTo(x1, y1);
                    return;
                }
                double r = Math.Max(rx, ry);
                double step = 2 * Math.Acos(Math.Max(-1, 1 - tol / r));
                int n = Math.Clamp((int)Math.Ceiling(Math.Abs(delta) / Math.Max(step, 1e-3)), 1, 400);
                for (int i = 1; i <= n; i++)
                {
                    double t = theta1 + delta * i / n;
                    double ex = rx * Math.Cos(t), ey = ry * Math.Sin(t);
                    if (i == n)
                        LineTo(x1, y1);
                    else
                        LineTo(cos * ex - sin * ey + cx, sin * ex + cos * ey + cy);
                }
            }
        }

        static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

        sealed class Tokenizer(string s)
        {
            int _pos;

            void SkipSeparators()
            {
                while (_pos < s.Length && (char.IsWhiteSpace(s[_pos]) || s[_pos] == ','))
                    _pos++;
            }

            public bool More
            {
                get
                {
                    SkipSeparators();
                    return _pos < s.Length;
                }
            }

            public bool NextIsCommand
            {
                get
                {
                    SkipSeparators();
                    return _pos < s.Length && char.IsLetter(s[_pos]) && s[_pos] is not ('e' or 'E');
                }
            }

            public char Command() => s[_pos++];

            public bool Flag()
            {
                SkipSeparators();
                if (_pos < s.Length && s[_pos] is '0' or '1')
                    return s[_pos++] == '1';
                throw new FormatException("Bogen-Kennzeichen erwartet.");
            }

            static readonly Regex NumberPattern = new(@"\G[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

            public double Number()
            {
                SkipSeparators();
                var m = NumberPattern.Match(s, _pos);
                if (!m.Success)
                    throw new FormatException($"Zahl erwartet bei „{s[_pos..Math.Min(s.Length, _pos + 10)]}“.");
                _pos += m.Length;
                return double.Parse(m.Value, CultureInfo.InvariantCulture);
            }
        }
    }
}
