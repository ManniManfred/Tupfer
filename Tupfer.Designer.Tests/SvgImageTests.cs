using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Tupfer.Designer.Model;
using Tupfer.Designer.Oid;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Avalonia ohne Fenster, aber mit Skia, damit Seiten wirklich gezeichnet werden.</summary>
public static class HeadlessSkia
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessSkia)));

    /// <summary>Führt <paramref name="action"/> auf dem UI-Thread der Sitzung aus.</summary>
    public static T Run<T>(Func<T> action) => Session.Value.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
}

/// <summary>SVG-Grafiken als Bilder: laden, einfügen und als Vektorgrafik zeichnen.</summary>
public class SvgImageTests : IDisposable
{
    // Linke Hälfte rot, rechte Hälfte blau; 100 × 50 Einheiten, 40 × 20 mm groß
    const string Drawing = """
        <svg xmlns="http://www.w3.org/2000/svg" width="40mm" height="20mm" viewBox="0 0 100 50">
          <rect x="0" y="0" width="50" height="50" fill="#ff0000"/>
          <rect x="50" y="0" width="50" height="50" fill="#0000ff"/>
        </svg>
        """;

    readonly string _dir = Path.Combine(Path.GetTempPath(), "tupfer-svgbild-" + Guid.NewGuid());
    readonly string _file;

    public SvgImageTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "zeichnung.svg");
        File.WriteAllText(_file, Drawing);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    static (byte R, byte G, byte B) Pixel(RgbImage img, int x, int y)
    {
        int i = (y * img.Width + x) * 3;
        return (img.Rgb[i], img.Rgb[i + 1], img.Rgb[i + 2]);
    }

    [Fact]
    public void WirdAlsVektorgrafikGeladenUndInEigenerGroesseEingefuegt()
    {
        var (isSvg, w, h) = HeadlessSkia.Run(() =>
        {
            var project = new ProjectViewModel(_ => { });
            var page = new PageViewModel(project) { Name = "Seite", WidthMm = 148.5, HeightMm = 210 };
            project.Pages.Add(page);
            var e = page.AddImage(_file)!;
            return (e.Image is SvgImage, e.Width, e.Height);
        });
        Assert.True(isSvg);
        // Svg.Skia rundet die Größe auf ganze Pixel (96 dpi): auf etwa 0,3 mm genau
        Assert.InRange(w, 39.7, 40.3);
        Assert.InRange(h, 19.7, 20.3);
    }

    [Fact]
    public void WirdBeimDruckenScharfGezeichnet()
    {
        var img = HeadlessSkia.Run(() =>
        {
            var project = new ProjectViewModel(_ => { });
            var page = new PageViewModel(project) { Name = "Seite", WidthMm = 50, HeightMm = 30 };
            project.Pages.Add(page);
            page.Elements.Add(new ElementViewModel(project, ElementKind.Image) { ImagePath = _file, X = 5, Y = 5, Width = 40, Height = 20 });
            return ImageConverter.RenderPage(page, 1200)!;
        });
        double px = 1200 / 25.4;
        int y = (int)(15 * px);
        Assert.Equal((255, 255, 255), Pixel(img, (int)(2 * px), y));          // Rand der Seite
        Assert.Equal((255, 0, 0), Pixel(img, (int)(15 * px), y));            // rote Hälfte
        Assert.Equal((0, 0, 255), Pixel(img, (int)(35 * px), y));            // blaue Hälfte
        // Kante zwischen Rot und Blau bei 25 mm: höchstens ein Mischpixel, obwohl die Zeichnung nur
        // 100 Einheiten breit ist (als Rasterbild hochskaliert wäre sie über viele Pixel verwaschen)
        int edge = (int)Math.Round(25 * px);
        Assert.Equal((255, 0, 0), Pixel(img, edge - 2, y));
        Assert.Equal((0, 0, 255), Pixel(img, edge + 2, y));
    }

    [Fact]
    public void ZuschnittZeigtNurDenAusschnitt()
    {
        var img = HeadlessSkia.Run(() =>
        {
            var project = new ProjectViewModel(_ => { });
            var page = new PageViewModel(project) { Name = "Seite", WidthMm = 30, HeightMm = 30 };
            project.Pages.Add(page);
            // Nur die rechte Hälfte (blau), gestreckt auf 20 × 20 mm
            page.Elements.Add(new ElementViewModel(project, ElementKind.Image)
                { ImagePath = _file, X = 5, Y = 5, Width = 20, Height = 20, CropX = 0.5, CropWidth = 0.5 });
            return ImageConverter.RenderPage(page, 300)!;
        });
        double px = 300 / 25.4;
        Assert.Equal((0, 0, 255), Pixel(img, (int)(6 * px), (int)(15 * px)));
        Assert.Equal((0, 0, 255), Pixel(img, (int)(24 * px), (int)(15 * px)));
        Assert.Equal((255, 255, 255), Pixel(img, (int)(27 * px), (int)(15 * px)));
    }

    [Fact]
    public void UngueltigeDateiWirdNichtGeladen()
    {
        var bad = Path.Combine(_dir, "kaputt.svg");
        File.WriteAllText(bad, "kein svg");
        var loaded = HeadlessSkia.Run(() => new ProjectViewModel(_ => { }).LoadImage(bad) != null);
        Assert.False(loaded);
    }

    [Fact]
    public void BeispielWeihnachtenZeigtDieSeitenAlsSvg()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Programme", "weihnachten");
        var file = ProjectStore.Load(Path.Combine(dir, "weihnachten.tupfer.json"));
        var result = HeadlessSkia.Run(() =>
        {
            var project = ProjectViewModel.FromFile(file, dir, _ => { });
            var images = project.Pages.Select(p => p.Elements.First().Image).ToList();
            var cover = ImageConverter.RenderPage(project.Pages[0], 100)!;
            return (images, cover);
        });
        Assert.Equal(4, result.images.Count);
        Assert.All(result.images, i => Assert.IsType<SvgImage>(i));
        // Titelseite: dunkelblauer Grund (#13234A) am Rand, kein leeres Weiß
        double px = 100 / 25.4;
        Assert.Equal((0x13, 0x23, 0x4A), Pixel(result.cover, (int)(2 * px), (int)(100 * px)));
    }
}
