using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Tupfer.Designer.Model;
using Tupfer.Designer.Oid;

namespace Tupfer.Designer.Services;

/// <summary>
/// Druckt Seiten unter Windows direkt (GDI+): Hintergrund und Texte wie im PDF, die OID-Muster pixelgenau in
/// Druckerpixeln, ohne Skalierung. Nicht im Browser (System.Drawing).
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsPrinter : IDirectPrinter
{
    public IReadOnlyList<string> Printers() => PrinterSettings.InstalledPrinters.Cast<string>().ToList();

    public string? DefaultPrinter()
    {
        var settings = new PrinterSettings();
        return settings.IsValid ? settings.PrinterName : null;
    }

    public string Print(IReadOnlyList<PdfPageSpec> pages, int dpi, int pixelSize, string printer)
    {
        using var doc = new PrintDocument { PrintController = new StandardPrintController(), DocumentName = "Tupfer Designer" };
        doc.PrinterSettings.PrinterName = printer;
        if (!doc.PrinterSettings.IsValid)
            throw new InvalidOperationException($"Der Drucker „{printer}“ wurde nicht gefunden.");
        var resolution = ChooseResolution(doc.PrinterSettings, dpi);
        doc.OriginAtMargins = false;

        int index = 0;
        var used = new HashSet<string>();
        doc.QueryPageSettings += (_, e) =>
        {
            var spec = pages[index];
            e.PageSettings.Landscape = spec.WidthMm > spec.HeightMm;
            if (ChoosePaper(doc.PrinterSettings, spec) is { } paper)
                e.PageSettings.PaperSize = paper;
            if (resolution != null)
                e.PageSettings.PrinterResolution = resolution;
            e.PageSettings.Margins = new Margins(0, 0, 0, 0);
        };
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Pixel;
            // Der Ursprung liegt am bedruckbaren Bereich, nicht an der Blattkante
            double offsetX = e.PageSettings.HardMarginX / 100.0 * g.DpiX;
            double offsetY = e.PageSettings.HardMarginY / 100.0 * g.DpiY;
            used.Add(DrawPage(g, pages[index], dpi, pixelSize, g.DpiX, g.DpiY, offsetX, offsetY));
            e.HasMorePages = ++index < pages.Count;
        };
        doc.Print();
        return $"An „{printer}“ gesendet ({pages.Count} Seite(n), Originalgröße, {string.Join("; ", used)}).";
    }

    /// <summary>Wunschauflösung, sonst die höchste angebotene; null = Einstellung des Druckers lassen.</summary>
    static PrinterResolution? ChooseResolution(PrinterSettings settings, int dpi)
    {
        var custom = settings.PrinterResolutions.Cast<PrinterResolution>().Where(r => r.Kind == PrinterResolutionKind.Custom && r.X > 0).ToList();
        return custom.FirstOrDefault(r => r.X == dpi && r.Y == dpi)
               ?? custom.OrderByDescending(r => Math.Min(r.X, r.Y)).FirstOrDefault();
    }

    /// <summary>Papier in Seitengröße (± 2 mm, auch quer), z. B. A4.</summary>
    static PaperSize? ChoosePaper(PrinterSettings settings, PdfPageSpec spec)
    {
        double w = Math.Min(spec.WidthMm, spec.HeightMm), h = Math.Max(spec.WidthMm, spec.HeightMm);
        return settings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p =>
        {
            // PaperSize in 1/100 Zoll
            double pw = Math.Min(p.Width, p.Height) * 0.254, ph = Math.Max(p.Width, p.Height) * 0.254;
            return Math.Abs(pw - w) < 2 && Math.Abs(ph - h) < 2;
        });
    }

    /// <summary>
    /// Zeichnet eine Seite in Gerätepixeln (<see cref="GraphicsUnit.Pixel"/>); <paramref name="offsetX"/>/<paramref name="offsetY"/>
    /// ist die Lage des Grafikursprungs auf dem Blatt in Pixeln. Die Muster liegen wie im PDF auf dem Pixelraster der
    /// Seite, gemessen ab der oberen linken Blattecke. Liefert, mit welcher Auflösung und Punktgröße gedruckt wurde.
    /// </summary>
    public static string DrawPage(Graphics g, PdfPageSpec spec, int dpi, int pixelSize, double deviceDpiX, double deviceDpiY,
        double offsetX = 0, double offsetY = 0)
    {
        double pxX = deviceDpiX / 25.4, pxY = deviceDpiY / 25.4;
        int ox = (int)Math.Round(offsetX), oy = (int)Math.Round(offsetY);
        int pageW = (int)Math.Round(spec.WidthMm * pxX), pageH = (int)Math.Round(spec.HeightMm * pxY);
        g.SetClip(new Rectangle(-ox, -oy, pageW, pageH));

        if (spec.Background is { } bg)
        {
            using var image = ToBitmap(bg);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Gespiegelte Ränder: sonst blendet GDI+ beim Vergrößern die Bildkanten mit Weiß aus
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(image, new Rectangle(-ox, -oy, pageW, pageH), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var used = new SortedSet<string>();
        foreach (var a in spec.Areas)
        {
            int areaDpi = a.Dpi ?? dpi, areaPixel = a.PixelSize ?? pixelSize;
            // Kann der Drucker die Auflösung nicht, gleich große Punkte in seiner Auflösung
            int effective = (int)Math.Min(areaDpi, Math.Min(Math.Round(deviceDpiX), Math.Round(deviceDpiY)));
            int effectivePixel = Math.Max(1, (int)Math.Round(areaPixel * (double)effective / areaDpi));
            used.Add($"{effective} dpi, Punktgröße {effectivePixel}");
            using var dots = AreaBitmap(a, effective, effectivePixel, pxX, pxY, pageW, pageH, out int x0, out int y0);
            if (dots != null)
                g.DrawImage(dots, new Rectangle(x0 - ox, y0 - oy, dots.Width, dots.Height));
        }

        foreach (var (x, y, w, h) in spec.Frames)
        {
            using var pen = new Pen(Color.FromArgb(153, 153, 153), (float)(0.5 / 72 * deviceDpiX));
            g.DrawRectangle(pen, (float)(x * pxX - ox), (float)(y * pxY - oy), (float)(w * pxX), (float)(h * pxY));
        }

        foreach (var t in spec.Texts)
        {
            using var font = new Font("Arial", (float)t.SizePt, GraphicsUnit.Point);
            g.DrawString(t.Text, font, Brushes.Black, (float)(t.X * pxX - ox), (float)(t.Y * pxY - oy));
        }
        g.ResetClip();
        return string.Join(", ", used);
    }

    /// <summary>
    /// Die Punkte einer Fläche als 1-Bit-Bild in Gerätepixeln (weiß durchsichtig), am Seitenrand abgeschnitten;
    /// null, wenn die Fläche ganz außerhalb liegt.
    /// </summary>
    static Bitmap? AreaBitmap(OidArea a, int dpi, int pixelSize, double pxX, double pxY, int pageW, int pageH, out int x0, out int y0)
    {
        x0 = Math.Max(0, (int)Math.Round(a.X * pxX));
        y0 = Math.Max(0, (int)Math.Round(a.Y * pxY));
        int x1 = Math.Min(pageW, (int)Math.Round((a.X + a.Width) * pxX));
        int y1 = Math.Min(pageH, (int)Math.Round((a.Y + a.Height) * pxY));
        if (x1 <= x0 || y1 <= y0)
            return null;

        var raw = OidCode.ToRawCode(a.Oid) ?? throw new ArgumentException($"Für OID {a.Oid} ist kein Raw-Code bekannt.");
        int tile = OidCode.TilePixelSize(dpi);
        var mask = OidCode.TileMask(raw, dpi, pixelSize);
        // Gerätepixel → Pixel des Musters (bei niedrigerer Musterauflösung mehrere Gerätepixel je Musterpixel)
        double scaleX = dpi / (pxX * 25.4), scaleY = dpi / (pxY * 25.4);
        var outline = a.Outline is { Count: >= 3 } o ? o.Select(p => (p.X * pxX, p.Y * pxY)).ToList() : null;

        int w = x1 - x0, h = y1 - y0;
        var bmp = new Bitmap(w, h, PixelFormat.Format1bppIndexed);
        var palette = bmp.Palette;
        palette.Entries[0] = Color.FromArgb(0, 255, 255, 255);
        palette.Entries[1] = Color.Black;
        bmp.Palette = palette;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format1bppIndexed);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < h; y++)
            {
                Array.Clear(row);
                int py = y0 + y;
                int my = (int)(py * scaleY) % tile;
                var spans = outline != null ? AreaGeometry.Spans(outline, py + 0.5) : null;
                for (int x = 0; x < w; x++)
                {
                    int px = x0 + x;
                    if (!mask[my * tile + (int)(px * scaleX) % tile])
                        continue;
                    if (spans != null && !spans.Any(s => px + 0.5 >= s.From && px + 0.5 < s.To))
                        continue;
                    row[x / 8] |= (byte)(0x80 >> (x % 8));
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    static Bitmap ToBitmap(RgbImage image)
    {
        var bmp = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < image.Height; y++)
            {
                // GDI+ erwartet BGR
                for (int x = 0; x < image.Width; x++)
                {
                    int i = (y * image.Width + x) * 3;
                    row[x * 3] = image.Rgb[i + 2];
                    row[x * 3 + 1] = image.Rgb[i + 1];
                    row[x * 3 + 2] = image.Rgb[i];
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}
