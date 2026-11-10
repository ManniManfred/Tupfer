using Tupfer.Designer.Oid;

namespace Tupfer.Designer.Services;

/// <summary>
/// Broschürendruck (Rückstichheftung): Je zwei Seiten liegen nebeneinander auf einem Bogen, der beidseitig
/// gedruckt (an der kurzen Kante gewendet), in der Mitte gefalzt und ineinandergelegt wird. Die Seitenzahl wird
/// mit leeren Seiten auf ein Vielfaches von vier aufgefüllt. Die Seiten werden nicht skaliert, damit die
/// OID-Punkte ihre Größe behalten: Aus A5-Seiten wird ein A4-Bogen quer, aus A4-Seiten ein A3-Bogen.
/// </summary>
public static class Booklet
{
    /// <summary>
    /// Reihenfolge der Seiten auf den Bogenseiten: je Bogenseite (links, rechts) als Index in die Seitenliste,
    /// null = leere Seite. Bogen 1 vorne: letzte und erste Seite, hinten: zweite und vorletzte usw.
    /// </summary>
    public static List<(int? Left, int? Right)> Order(int pageCount)
    {
        int total = (pageCount + 3) / 4 * 4;
        int? Page(int i) => i < pageCount ? i : null;
        var sides = new List<(int?, int?)>();
        for (int i = 0; i < total / 2; i += 2)
        {
            sides.Add((Page(total - 1 - i), Page(i)));
            sides.Add((Page(i + 1), Page(total - 2 - i)));
        }
        return sides;
    }

    /// <summary>
    /// Ordnet die Seiten zu Bogenseiten an. Jede Hälfte des Bogens ist so groß wie die größte Seite; kleinere
    /// Seiten liegen am Falz und senkrecht mittig.
    /// </summary>
    public static List<PdfPageSpec> Impose(IReadOnlyList<PdfPageSpec> pages)
    {
        if (pages.Count == 0)
            return [];
        double halfW = pages.Max(p => p.WidthMm), height = pages.Max(p => p.HeightMm);
        // Bilder in der feinsten vorkommenden Auflösung zusammensetzen
        double pxPerMm = pages.Where(p => p.Background != null).Select(p => p.Background!.Width / p.WidthMm).DefaultIfEmpty(0).Max();

        var sheets = new List<PdfPageSpec>();
        foreach (var (left, right) in Order(pages.Count))
        {
            var placed = new List<(PdfPageSpec Page, double X, double Y)>();
            if (left is { } l)
                placed.Add((pages[l], halfW - pages[l].WidthMm, (height - pages[l].HeightMm) / 2));
            if (right is { } r)
                placed.Add((pages[r], halfW, (height - pages[r].HeightMm) / 2));

            var sheet = new PdfPageSpec
            {
                WidthMm = 2 * halfW,
                HeightMm = height,
                Background = placed.Any(p => p.Page.Background != null) ? Compose(placed, 2 * halfW, height, pxPerMm) : null,
            };
            foreach (var (page, x, y) in placed)
            {
                foreach (var a in page.Areas)
                {
                    // Am Rand der eigenen Seite abschneiden, sonst ragt die Fläche auf die Nachbarseite
                    double x0 = Math.Max(0, a.X), y0 = Math.Max(0, a.Y);
                    double x1 = Math.Min(page.WidthMm, a.X + a.Width), y1 = Math.Min(page.HeightMm, a.Y + a.Height);
                    if (x1 <= x0 || y1 <= y0)
                        continue;
                    var outline = a.Outline?.Select(p => (Math.Clamp(p.X, 0, page.WidthMm) + x, Math.Clamp(p.Y, 0, page.HeightMm) + y)).ToList();
                    sheet.Areas.Add(a with { X = x0 + x, Y = y0 + y, Width = x1 - x0, Height = y1 - y0, Outline = outline });
                }
                sheet.Texts.AddRange(page.Texts.Select(t => t with { X = t.X + x, Y = t.Y + y }));
                sheet.Frames.AddRange(page.Frames.Select(f => (f.X + x, f.Y + y, f.Width, f.Height)));
            }
            sheets.Add(sheet);
        }
        return sheets;
    }

    /// <summary>Setzt die Hintergründe der Seiten auf weißem Grund zu einem Bild des Bogens zusammen.</summary>
    static RgbImage Compose(List<(PdfPageSpec Page, double X, double Y)> placed, double widthMm, double heightMm, double pxPerMm)
    {
        int w = Math.Max(1, (int)Math.Round(widthMm * pxPerMm)), h = Math.Max(1, (int)Math.Round(heightMm * pxPerMm));
        var rgb = new byte[w * h * 3];
        Array.Fill(rgb, (byte)255);
        foreach (var (page, xMm, yMm) in placed)
        {
            if (page.Background is not { } bg)
                continue;
            int x0 = (int)Math.Round(xMm * pxPerMm), y0 = (int)Math.Round(yMm * pxPerMm);
            int pw = Math.Min(w - x0, (int)Math.Round(page.WidthMm * pxPerMm)), ph = Math.Min(h - y0, (int)Math.Round(page.HeightMm * pxPerMm));
            for (int y = 0; y < ph; y++)
            {
                int sy = Math.Min(bg.Height - 1, (int)((long)y * bg.Height / Math.Max(1, ph)));
                for (int x = 0; x < pw; x++)
                {
                    int sx = Math.Min(bg.Width - 1, (int)((long)x * bg.Width / Math.Max(1, pw)));
                    Array.Copy(bg.Rgb, (sy * bg.Width + sx) * 3, rgb, ((y0 + y) * w + x0 + x) * 3, 3);
                }
            }
        }
        return new RgbImage(w, h, rgb);
    }
}
