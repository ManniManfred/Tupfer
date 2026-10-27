using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>SVG-Import und -Export.</summary>
public class SvgTests
{
    static string Svg(string body, string size = "width=\"100mm\" height=\"50mm\" viewBox=\"0 0 100 50\"") =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" {size}>{body}</svg>";

    static SvgImportResult Import(string svg) => SvgImporter.Import(svg, null, Path.GetTempPath());

    [Fact]
    public void GroesseUndEinheiten()
    {
        var r = Import(Svg("", "width=\"200\" height=\"100\" viewBox=\"0 0 400 200\""));
        // Ohne Einheit: px (96 dpi)
        Assert.Equal(200 * 25.4 / 96, r.WidthMm!.Value, 6);
        Assert.Equal(100 * 25.4 / 96, r.HeightMm!.Value, 6);

        r = Import(Svg("<rect x=\"10\" y=\"10\" width=\"20\" height=\"10\" fill=\"red\"/>", "width=\"10cm\" height=\"5cm\" viewBox=\"0 0 200 100\""));
        var rect = Assert.Single(r.Elements);
        Assert.Equal(5, rect.X, 6);   // 10 Einheiten = 5 mm
        Assert.Equal(10, rect.Width, 6);
        Assert.Equal("#FF0000", rect.Color);
    }

    [Fact]
    public void RechteckKreisUndStil()
    {
        var r = Import(Svg("""
            <style>.blau { fill: #1976D2; stroke: black; stroke-width: 2 }</style>
            <g transform="translate(10,5)" opacity="0.5">
              <rect x="0" y="0" width="20" height="10" class="blau"/>
              <circle cx="50" cy="20" r="5" style="fill:rgb(255,128,0);stroke:none"/>
            </g>
            <ellipse cx="50" cy="25" rx="10" ry="5" fill="none" stroke="#00ff00" transform="rotate(30 50 25)"/>
            <rect x="0" y="0" width="5" height="5" fill="none"/>
            <rect x="0" y="0" width="5" height="5" style="display:none"/>
            """));
        Assert.Equal(3, r.Elements.Count);

        var rect = r.Elements[0];
        Assert.Equal(AreaShape.Rectangle, rect.Shape);
        Assert.Equal(10, rect.X, 6);
        Assert.Equal("#801976D2", rect.Color);         // Deckkraft 0.5 der Gruppe
        Assert.Equal(2, rect.StrokeWidth, 6);
        Assert.Equal("#80000000", rect.StrokeColor);

        var circle = r.Elements[1];
        Assert.Equal(AreaShape.Ellipse, circle.Shape);
        Assert.Equal(55, circle.X, 6);
        Assert.Equal(20, circle.Y, 6);
        Assert.Equal(10, circle.Width, 6);
        Assert.Equal("#80FF8000", circle.Color);

        var ellipse = r.Elements[2];
        Assert.Equal(AreaShape.Ellipse, ellipse.Shape);
        Assert.Equal(30, ellipse.Rotation, 6);
        Assert.Equal(20, ellipse.Width, 6);
        Assert.Equal(40, ellipse.X, 6);  // Mitte bleibt (50, 25)
        Assert.Equal(0, Avalonia.Media.Color.Parse(ellipse.Color).A); // ohne Füllung
        Assert.Equal("#00FF00", ellipse.StrokeColor);
    }

    [Fact]
    public void PfadMitKreisbogenUndKurve()
    {
        // Halbkreis oben (Bogen) und unten eine Bézierkurve
        var r = Import(Svg("<path d=\"M10,20 A10,10 0 0 1 30,20 C30,30 10,30 10,20 Z\" fill=\"#123456\"/>"));
        var shape = Assert.Single(r.Elements);
        Assert.Equal(AreaShape.Polygon, shape.Shape);
        var points = shape.Points!;
        // Der Bogen bleibt ein Bogen: Halbkreis = Bulge ±1
        Assert.Equal(1, Math.Abs(points[0].Bulge), 6);
        Assert.True(points.Count > 5);
        // Rahmen um den Umriss: oben bis zum Scheitel des Halbkreises (y = 10)
        Assert.Equal(10, shape.Y, 1);
        Assert.Equal(10, shape.X, 3);
        Assert.Equal(20, shape.Width, 3);

        // Gespiegelt bleibt der Bogen auf der richtigen Seite
        var mirrored = Import(Svg("<path transform=\"scale(1,-1) translate(0,-40)\" d=\"M10,20 A10,10 0 0 1 30,20 Z\"/>"));
        var m = Assert.Single(mirrored.Elements);
        Assert.Equal(20, m.Y, 3);   // nach dem Spiegeln liegt der Halbkreis unten (y 20 … 30)
        Assert.Equal(10, m.Height, 3);
    }

    [Fact]
    public void RelativeBefehleUndKurzschreibweise()
    {
        var r = Import(Svg("<path d=\"m10 10h20v10h-20z M50 10l10 0 0 10-10 0z\"/>"));
        Assert.Equal(2, r.Elements.Count);
        Assert.Equal(4, r.Elements[0].Points!.Count);
        Assert.Equal(20, r.Elements[0].Width, 6);
        Assert.Equal(50, r.Elements[1].X, 6);
        Assert.Equal("#000000", r.Elements[0].Color);   // Standardfüllung Schwarz
    }

    [Fact]
    public void OffeneLinieWirdFlaecheInStrichbreite()
    {
        var r = Import(Svg("<line x1=\"10\" y1=\"10\" x2=\"50\" y2=\"10\" stroke=\"blue\" stroke-width=\"2\"/>"));
        var line = Assert.Single(r.Elements);
        Assert.Equal(AreaShape.Polygon, line.Shape);
        Assert.Equal("#0000FF", line.Color);
        Assert.Equal(40, line.Width, 6);
        Assert.Equal(2, line.Height, 6);
        Assert.Equal(9, line.Y, 6);
    }

    [Fact]
    public void TextMitZeilen()
    {
        var r = Import(Svg("""
            <text x="50" y="20" font-size="5" font-family="'Comic Sans MS', sans-serif" font-weight="bold" text-anchor="middle" fill="#D32F2F">
              <tspan x="50" y="20">Hallo</tspan><tspan x="50" dy="6">Welt</tspan>
            </text>
            """));
        var text = Assert.Single(r.Elements);
        Assert.Equal(ElementKind.Text, text.Kind);
        Assert.Equal("Hallo\nWelt", text.Text);
        Assert.Equal("Comic Sans MS", text.FontFamily);
        Assert.True(text.Bold);
        Assert.Equal(TextAlign.Center, text.Align);
        Assert.Equal(5 * 72 / 25.4, text.FontSize, 1);
        // Mittig um x = 50, Oberkante etwa eine Oberlänge über der Grundlinie
        Assert.Equal(50, text.X + text.Width / 2, 6);
        Assert.Equal(20 - SvgImporter.AscentFactor * 5, text.Y, 6);
    }

    [Fact]
    public void EingebettetesBildMitZuschnitt()
    {
        var png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var dir = Path.Combine(Path.GetTempPath(), "tupfer-svg-" + Guid.NewGuid());
        try
        {
            var r = SvgImporter.Import(Svg($"""
                <svg x="10" y="10" width="20" height="10" viewBox="0.5 0 0.5 0.5" preserveAspectRatio="none">
                  <image width="1" height="1" preserveAspectRatio="none" xlink:href="data:image/png;base64,{png}"/>
                </svg>
                """), null, dir);
            var image = Assert.Single(r.Elements);
            Assert.Equal(ElementKind.Image, image.Kind);
            Assert.True(File.Exists(image.ImagePath));
            Assert.Equal(10, image.X, 6);
            Assert.Equal(20, image.Width, 6);
            Assert.Equal(0.5, image.CropX, 6);
            Assert.Equal(0.5, image.CropWidth, 6);
            Assert.Equal(0.5, image.CropHeight, 6);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void VerlaufWirdErsteFarbe()
    {
        var r = Import(Svg("""
            <defs><linearGradient id="a"><stop offset="0" stop-color="#388E3C"/><stop offset="1" stop-color="white"/></linearGradient>
            <linearGradient id="b" xlink:href="#a"/></defs>
            <rect width="10" height="10" fill="url(#b)"/>
            """));
        Assert.Equal("#388E3C", Assert.Single(r.Elements).Color);
    }

    [Fact]
    public void ExportUndWiederImport()
    {
        var project = new ProjectViewModel(_ => { });
        var page = new PageViewModel(project) { Name = "Seite", WidthMm = 120, HeightMm = 80 };
        project.Pages.Add(page);
        project.AddScript("tippen", "P(hallo)");

        var rect = page.AddShape();
        rect.X = 10; rect.Y = 10; rect.Width = 30; rect.Height = 20; rect.Rotation = 20;
        rect.Color = "#801976D2"; rect.StrokeWidth = 1; rect.StrokeColor = "#D32F2F";
        var poly = page.AddShape();
        poly.X = 50; poly.Y = 10; poly.Width = 40; poly.Height = 30;
        poly.ChangeShape(AreaShape.Polygon);
        poly.SetPolygon(poly.PagePoints.Select((p, i) => i == 0 ? p with { Bulge = 0.5 } : p).ToList());
        var text = page.AddText();
        text.Text = "Hallo Welt";

        var area = new AreaViewModel(project) { X = 5, Y = 50, Width = 20, Height = 10, Label = "Knopf", Script = "tippen" };
        page.Areas.Add(area);
        var round = new AreaViewModel(project) { X = 40, Y = 50, Width = 20, Height = 20, Script = "tippen" };
        round.ChangeShape(AreaShape.Ellipse);
        page.Areas.Add(round);

        var svg = SvgExporter.Export(page);
        Assert.Contains("data-tupfer=\"oid-areas\"", svg);
        Assert.Contains(" A", svg);   // Bogen des Polygons

        var r = Import(svg);
        Assert.Equal(120, r.WidthMm!.Value, 6);
        Assert.Equal(80, r.HeightMm!.Value, 6);
        Assert.Equal(3, r.Elements.Count);

        var r0 = r.Elements[0];
        Assert.Equal(AreaShape.Rectangle, r0.Shape);
        Assert.Equal(20, r0.Rotation, 4);
        Assert.Equal(10, r0.X, 3);
        Assert.Equal(30, r0.Width, 3);
        Assert.Equal("#801976D2", r0.Color);
        Assert.Equal(1, r0.StrokeWidth, 6);
        Assert.Equal("#D32F2F", r0.StrokeColor);

        var p1 = r.Elements[1];
        Assert.Equal(AreaShape.Polygon, p1.Shape);
        Assert.Equal(4, p1.Points!.Count);
        Assert.Equal(0.5, p1.Points[0].Bulge, 4);
        Assert.Equal(poly.X, p1.X, 2);
        Assert.Equal(poly.Y, p1.Y, 2);

        Assert.Equal("Hallo Welt", r.Elements[2].Text);

        Assert.Equal(2, r.Areas.Count);
        Assert.Equal(AreaShape.Rectangle, r.Areas[0].Shape);
        Assert.Equal("Knopf", r.Areas[0].Label);
        Assert.Equal("tippen", r.Areas[0].Script);
        Assert.Equal(5, r.Areas[0].X, 6);
        Assert.Equal(AreaShape.Ellipse, r.Areas[1].Shape);
        Assert.Equal(20, r.Areas[1].Width, 6);
    }

    [Fact]
    public void PfadDatenMitBogen()
    {
        var d = SvgExporter.PathData([new AreaPoint(0, 0, 1), new AreaPoint(10, 0), new AreaPoint(10, 10)]);
        Assert.Equal("M0,0 A5,5 0 0 1 10,0 L10,10 Z", d);
    }
}
