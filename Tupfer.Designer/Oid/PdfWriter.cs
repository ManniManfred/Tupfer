using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Tupfer.Designer.Oid;

/// <summary>RGB-Bild (3 Bytes pro Pixel, zeilenweise von oben).</summary>
public record RgbImage(int Width, int Height, byte[] Rgb);

/// <summary>
/// Eine mit einem OID-Muster gefüllte Fläche, Angaben in mm ab oben links.
/// Dpi und PixelSize überschreiben optional die Einstellungen des Dokuments.
/// Ist <paramref name="Outline"/> gesetzt (Polygonzug in mm), wird nur dieser Umriss gefüllt,
/// sonst das Rechteck.
/// </summary>
public record OidArea(double X, double Y, double Width, double Height, int Oid, int? Dpi = null, int? PixelSize = null,
    IReadOnlyList<(double X, double Y)>? Outline = null);

/// <summary>Ein Text auf der Seite, Angaben in mm ab oben links.</summary>
public record PdfText(double X, double Y, double SizePt, string Text);

public class PdfPageSpec
{
    public double WidthMm { get; init; } = 210;
    public double HeightMm { get; init; } = 297;
    public RgbImage? Background { get; init; }
    public List<OidArea> Areas { get; } = new();
    public List<PdfText> Texts { get; } = new();

    /// <summary>Dünne graue Rahmen (mm ab oben links), z. B. um Tippfelder sichtbar zu machen.</summary>
    public List<(double X, double Y, double Width, double Height)> Frames { get; } = new();
}

/// <summary>
/// Kleiner PDF-Schreiber für Druckvorlagen mit OID-Codes.
/// Die Codes werden als Kachelmuster (Tiling Pattern) erzeugt, damit sie unabhängig von der
/// Flächengröße pixelgenau gedruckt werden.
/// </summary>
public class PdfWriter
{
    const double PtPerMm = 72 / 25.4;

    readonly List<byte[]> _objects = new();
    readonly int _dpi;
    readonly int _pixelSize;
    readonly Dictionary<(int Oid, int Dpi, int PixelSize, double PageHeight), int> _patterns = new();

    public PdfWriter(int dpi = 1200, int pixelSize = 2)
    {
        _dpi = dpi;
        _pixelSize = pixelSize;
    }

    public static void Write(string path, IEnumerable<PdfPageSpec> pages, int dpi = 1200, int pixelSize = 2)
    {
        var w = new PdfWriter(dpi, pixelSize);
        File.WriteAllBytes(path, w.Build(pages.ToList()));
    }

    /// <summary>Erzeugt eine Tabelle aller angegebenen Codes (ähnlich "tttool oid-table").</summary>
    public static void WriteOidTable(string path, string title, IReadOnlyList<(string Label, int Oid)> entries, int dpi = 1200, int pixelSize = 2)
    {
        const double size = 25, gapX = 10, gapY = 14, margin = 15, top = 25;
        int cols = (int)((210 - 2 * margin + gapX) / (size + gapX));
        int rows = (int)((297 - top - margin + gapY) / (size + gapY));
        var pages = new List<PdfPageSpec>();
        for (int i = 0; i < Math.Max(1, entries.Count); i += cols * rows)
        {
            var page = new PdfPageSpec();
            page.Texts.Add(new PdfText(margin, 10, 14, title));
            foreach (var (e, idx) in entries.Skip(i).Take(cols * rows).Select((e, idx) => (e, idx)))
            {
                double x = margin + idx % cols * (size + gapX);
                double y = top + idx / cols * (size + gapY);
                page.Areas.Add(new OidArea(x, y, size, size, e.Oid));
                page.Texts.Add(new PdfText(x, y + size + 1.5, 8, $"{e.Label} ({e.Oid})"));
            }
            pages.Add(page);
        }
        Write(path, pages, dpi, pixelSize);
    }

    int Add(byte[] content)
    {
        _objects.Add(content);
        return _objects.Count;
    }

    int Reserve() => Add(Array.Empty<byte>());

    int AddStream(string dict, byte[] data, bool compress)
    {
        if (compress)
        {
            var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(data);
            data = ms.ToArray();
            dict += " /Filter /FlateDecode";
        }
        var head = Encoding.ASCII.GetBytes($"<< {dict} /Length {data.Length} >>\nstream\n");
        var tail = Encoding.ASCII.GetBytes("\nendstream");
        return Add(head.Concat(data).Concat(tail).ToArray());
    }

    static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Liefert das Kachelmuster eines Codes. Eine Kachel ist ein 1-Bit-Bild in Druckerpixeln,
    /// dessen weiße Pixel per Color-Key-Maske transparent sind, damit der Hintergrund sichtbar
    /// bleibt. Der Ursprung des Musters liegt an der oberen linken Seitenecke, sodass die Punkte
    /// auch dann auf ganzen Druckerpixeln liegen, wenn die Seitenhöhe kein Vielfaches eines Pixels
    /// ist (z. B. A4 bei 600 dpi).
    /// </summary>
    int PatternFor(int oid, int dpi, int pixelSize, double pageHeightPt)
    {
        if (_patterns.TryGetValue((oid, dpi, pixelSize, pageHeightPt), out var id))
            return id;

        var raw = OidCode.ToRawCode(oid) ?? throw new ArgumentException($"Für OID {oid} ist kein Raw-Code bekannt.");
        int tile = OidCode.TilePixelSize(dpi);
        var mask = OidCode.TileMask(raw, dpi, pixelSize);
        int stride = (tile + 7) / 8;
        var bits = new byte[stride * tile];
        for (int y = 0; y < tile; y++)
            for (int x = 0; x < tile; x++)
                if (!mask[y * tile + x])
                    bits[y * stride + x / 8] |= (byte)(0x80 >> (x % 8)); // 1 = weiß (transparent)

        int image = AddStream($"/Type /XObject /Subtype /Image /Width {tile} /Height {tile} /ColorSpace /DeviceGray /BitsPerComponent 1 /Mask [1 1] /Interpolate false", bits, true);

        // Musterraum in Druckerpixeln, y nach unten, Ursprung oben links auf der Seite
        double unit = 72.0 / dpi;
        var matrix = string.Join(" ", new[] { unit, 0, 0, -unit, 0, pageHeightPt }.Select(v => v.ToString("0.########", CultureInfo.InvariantCulture)));
        var content = Encoding.ASCII.GetBytes($"q {tile} 0 0 -{tile} 0 {tile} cm /Im Do Q");
        id = AddStream($"/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /Matrix [{matrix}] /BBox [0 0 {tile} {tile}] /XStep {tile} /YStep {tile} /Resources << /XObject << /Im {image} 0 R >> >>", content, false);
        _patterns[(oid, dpi, pixelSize, pageHeightPt)] = id;
        return id;
    }

    byte[] Build(List<PdfPageSpec> pages)
    {
        int catalog = Reserve();
        int pagesObj = Reserve();
        int font = Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        var pageIds = new List<int>();

        foreach (var spec in pages)
        {
            double pw = spec.WidthMm * PtPerMm, ph = spec.HeightMm * PtPerMm;
            var content = new StringBuilder();
            var xobjects = new StringBuilder();
            var patterns = new StringBuilder();
            var usedPatterns = new HashSet<int>();

            if (spec.Background is { } bg)
            {
                int img = AddStream($"/Type /XObject /Subtype /Image /Width {bg.Width} /Height {bg.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8", bg.Rgb, true);
                xobjects.Append($" /Bg {img} 0 R");
                content.AppendLine($"q {F(pw)} 0 0 {F(ph)} 0 0 cm /Bg Do Q");
            }

            foreach (var a in spec.Areas)
            {
                int p = PatternFor(a.Oid, a.Dpi ?? _dpi, a.PixelSize ?? _pixelSize, ph);
                if (usedPatterns.Add(p))
                    patterns.Append($" /P{p} {p} 0 R");
                if (a.Outline is { Count: >= 3 } outline)
                {
                    content.Append($"/Pattern cs /P{p} scn");
                    for (int i = 0; i < outline.Count; i++)
                        content.Append($" {F(outline[i].X * PtPerMm)} {F(ph - outline[i].Y * PtPerMm)} {(i == 0 ? "m" : "l")}");
                    content.AppendLine(" h f");
                }
                else
                {
                    content.AppendLine($"/Pattern cs /P{p} scn {F(a.X * PtPerMm)} {F(ph - (a.Y + a.Height) * PtPerMm)} {F(a.Width * PtPerMm)} {F(a.Height * PtPerMm)} re f");
                }
            }

            foreach (var (x, y, w, h) in spec.Frames)
                content.AppendLine($"q 0.6 G 0.5 w {F(x * PtPerMm)} {F(ph - (y + h) * PtPerMm)} {F(w * PtPerMm)} {F(h * PtPerMm)} re S Q");

            foreach (var t in spec.Texts)
                content.AppendLine($"BT 0 g /F1 {F(t.SizePt)} Tf {F(t.X * PtPerMm)} {F(ph - t.Y * PtPerMm - t.SizePt)} Td ({EscapeText(t.Text)}) Tj ET");

            int stream = AddStream("", Encoding.Latin1.GetBytes(content.ToString()), true);
            var resources = $"<< /Font << /F1 {font} 0 R >> /XObject <<{xobjects} >> /Pattern <<{patterns} >> >>";
            pageIds.Add(Add(Encoding.ASCII.GetBytes(
                $"<< /Type /Page /Parent {pagesObj} 0 R /MediaBox [0 0 {F(pw)} {F(ph)}] /Resources {resources} /Contents {stream} 0 R >>")));
        }

        _objects[catalog - 1] = Encoding.ASCII.GetBytes($"<< /Type /Catalog /Pages {pagesObj} 0 R >>");
        _objects[pagesObj - 1] = Encoding.ASCII.GetBytes(
            $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(i => $"{i} 0 R"))}] /Count {pageIds.Count} >>");

        var output = new MemoryStream();
        void W(string s) => output.Write(Encoding.Latin1.GetBytes(s));
        W("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new List<long>();
        for (int i = 0; i < _objects.Count; i++)
        {
            offsets.Add(output.Position);
            W($"{i + 1} 0 obj\n");
            output.Write(_objects[i]);
            W("\nendobj\n");
        }
        long xref = output.Position;
        W($"xref\n0 {_objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets)
            W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {_objects.Count + 1} /Root {catalog} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    // Zeichen außerhalb von Latin-1, die in WinAnsiEncoding vorhanden sind
    static readonly Dictionary<char, int> WinAnsiExtra = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['„'] = 0x84, ['…'] = 0x85, ['‘'] = 0x91, ['’'] = 0x92,
        ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97,
    };

    static string EscapeText(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c is '(' or ')' or '\\')
                sb.Append('\\').Append(c);
            else if (WinAnsiExtra.TryGetValue(c, out var code))
                sb.Append('\\').Append(Convert.ToString(code, 8));
            else if (c == '→')
                sb.Append("->");
            else if (c < 32 || c > 255)
                sb.Append('?');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
