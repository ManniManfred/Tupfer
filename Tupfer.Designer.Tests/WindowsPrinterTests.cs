using System.Drawing;
using System.Drawing.Imaging;
using Tupfer.Designer.Oid;
using Tupfer.Designer.Services;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Direktes Drucken unter Windows: Die OID-Punkte müssen pixelgenau wie im PDF auf dem Seitenraster liegen.</summary>
public class WindowsPrinterTests
{
    const int Oid = 2666;

    /// <summary>Zeichnet eine 20 × 20 mm große Seite (hellblauer Hintergrund) mit einer Fläche in eine Bitmap.</summary>
    static Bitmap Render(OidArea area, int deviceDpi, int dpi = 1200, int pixelSize = 2, double offset = 0)
    {
        var spec = new PdfPageSpec { WidthMm = 20, HeightMm = 20, Background = new RgbImage(1, 1, [200, 220, 255]) };
        spec.Areas.Add(area);
        int size = (int)Math.Round(20 / 25.4 * deviceDpi);
        var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Pixel;
        // Grafikursprung um offset Pixel ins Blatt verschoben (wie der nicht bedruckbare Rand)
        g.TranslateTransform((float)-offset, (float)-offset);
        WindowsPrinter.DrawPage(g, spec, dpi, pixelSize, deviceDpi, deviceDpi, -offset, -offset);
        return bmp;
    }

    static bool IsBlack(Color c) => c.R < 60 && c.G < 60 && c.B < 60;

    /// <summary>Vergleicht jeden Pixel der Fläche mit der Kachel (Pixel des Musters = Gerätepixel / Faktor).</summary>
    static void AssertPattern(Bitmap bmp, int x0, int y0, int x1, int y1, int patternDpi, int pixelSize, int factor)
    {
        int tile = OidCode.TilePixelSize(patternDpi);
        var mask = OidCode.TileMask(OidCode.ToRawCode(Oid)!.Value, patternDpi, pixelSize);
        int wrong = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                bool expected = mask[(y / factor % tile) * tile + x / factor % tile];
                var c = bmp.GetPixel(x, y);
                if (expected != IsBlack(c))
                    wrong++;
                // Zwischen den Punkten bleibt der Hintergrund sichtbar (weiß ist durchsichtig)
                if (!expected && c != Color.FromArgb(200, 220, 255))
                    wrong++;
            }
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void PunkteLiegenPixelgenauAufDemSeitenraster()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // 5 mm bei 1200 dpi = 236,2 px: Flächengrenzen auf ganze Pixel gerundet
        using var bmp = Render(new OidArea(5, 5, 10, 10, Oid), 1200);
        AssertPattern(bmp, 237, 237, 708, 708, 1200, 2, 1);
        Assert.False(IsBlack(bmp.GetPixel(100, 100)));
    }

    [Fact]
    public void MusterMitNiedrigererAuflösungWirdVergrößert()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // 600-dpi-Fläche auf einem 1200-dpi-Drucker: jeder Musterpixel sind 2 × 2 Gerätepixel
        using var bmp = Render(new OidArea(5, 5, 10, 10, Oid, Dpi: 600, PixelSize: 2), 1200);
        AssertPattern(bmp, 238, 238, 708, 708, 600, 2, 2);
    }

    [Fact]
    public void DruckerMitNiedrigererAuflösungBekommtGleichGroßePunkte()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // Projekt 1200 dpi / Punkt 2, Drucker nur 600 dpi: 600 dpi mit Punktgröße 1 (gleiche Punktgröße in mm)
        using var bmp = Render(new OidArea(5, 5, 10, 10, Oid), 600);
        AssertPattern(bmp, 119, 119, 354, 354, 600, 1, 1);
    }

    [Fact]
    public void MeldungNenntAuflösungUndPunktgrößen()
    {
        // Testseite auf einem 600-dpi-Drucker: Varianten A–D (1200 dpi) und E/F (600 dpi)
        var text = WindowsPrinter.Describe("Laser", 1, [(1200, 600, 1), (1200, 600, 2), (600, 600, 1), (600, 600, 2)]);
        Assert.Equal("An „Laser“ gesendet: 1 Seite in Originalgröße, Codes mit 600 dpi (Punktgröße 1 und 2). " +
                     "Der Drucker kann höchstens 600 dpi; Codes für 1200 dpi wurden mit gleich großen Punkten in dieser Auflösung gedruckt.", text);
        Assert.Equal("An „Laser“ gesendet: 2 Seiten in Originalgröße, Codes mit 1200 dpi (Punktgröße 2).",
            WindowsPrinter.Describe("Laser", 2, [(1200, 1200, 2)]));
    }

    [Fact]
    public void NichtBedruckbarerRandVerschiebtDasMusterNicht()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // Ursprung 50 px im Blatt: Das Muster bleibt an der Blattecke ausgerichtet
        using var bmp = Render(new OidArea(5, 5, 10, 10, Oid), 1200, offset: 50);
        AssertPattern(bmp, 237, 237, 708, 708, 1200, 2, 1);
    }
}
