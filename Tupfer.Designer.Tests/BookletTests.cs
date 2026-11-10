using Tupfer.Designer.Oid;
using Tupfer.Designer.Services;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Broschürendruck: Seitenreihenfolge und Anordnung auf den Bögen.</summary>
public class BookletTests
{
    [Fact]
    public void AchtSeitenInHeftreihenfolge()
    {
        Assert.Equal(new (int?, int?)[] { (7, 0), (1, 6), (5, 2), (3, 4) }, Booklet.Order(8));
    }

    [Fact]
    public void FehlendeSeitenBleibenLeer()
    {
        // 5 Seiten → 8: Rückseite und die beiden Seiten davor bleiben leer
        Assert.Equal(new (int?, int?)[] { (null, 0), (1, null), (null, 2), (3, 4) }, Booklet.Order(5));
        Assert.Equal(new (int?, int?)[] { (null, 0), (null, null) }, Booklet.Order(1));
    }

    [Fact]
    public void FlaechenWerdenVerschobenUndAmSeitenrandAbgeschnitten()
    {
        var pages = Enumerable.Range(0, 4).Select(i =>
        {
            var p = new PdfPageSpec { WidthMm = 148, HeightMm = 210 };
            p.Areas.Add(new OidArea(140, 10, 20, 20, 1000 + i));
            p.Texts.Add(new PdfText(5, 5, 10, $"Seite {i + 1}"));
            return p;
        }).ToList();

        var sheets = Booklet.Impose(pages);
        Assert.Equal(2, sheets.Count);
        Assert.All(sheets, s => Assert.Equal((296.0, 210.0), (s.WidthMm, s.HeightMm)));

        // Vorderseite: links Seite 4, rechts Seite 1
        var front = sheets[0];
        Assert.Equal(new[] { 1003, 1000 }, front.Areas.Select(a => a.Oid));
        Assert.Equal((140.0, 8.0), (front.Areas[0].X, front.Areas[0].Width));
        Assert.Equal((288.0, 8.0), (front.Areas[1].X, front.Areas[1].Width));
        Assert.Equal(new[] { "Seite 4", "Seite 1" }, front.Texts.Select(t => t.Text));
        Assert.Equal(153.0, front.Texts[1].X);
        Assert.Null(front.Background);
    }

    [Fact]
    public void HintergruendeWerdenZusammengesetzt()
    {
        var red = new RgbImage(2, 2, [255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0]);
        var pages = new List<PdfPageSpec>
        {
            new() { WidthMm = 10, HeightMm = 10, Background = red },
            new() { WidthMm = 10, HeightMm = 10 },
        };
        var bg = Booklet.Impose(pages)[0].Background!;
        Assert.Equal((4, 2), (bg.Width, bg.Height));
        // Links leer (weiß), rechts Seite 1 (rot)
        Assert.Equal(new byte[] { 255, 255, 255, 255, 255, 255, 255, 0, 0, 255, 0, 0 }, bg.Rgb[..12]);
    }
}
