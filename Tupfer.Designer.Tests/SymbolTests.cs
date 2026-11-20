using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Symbole für Start, Wiederholen und Stopp: Grafik plus OID-Fläche mit dem Sondercode.</summary>
public class SymbolTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "tupfer-symbole-" + Guid.NewGuid());

    public SymbolTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void GrafikWirdEinmalAngelegtUndWiederbenutzt()
    {
        var first = Symbols.WriteFile(Symbols.Stop, _dir);
        var second = Symbols.WriteFile(Symbols.Stop, _dir);
        Assert.Equal(first, second);
        Assert.Equal("symbol-stopp.svg", Path.GetFileName(first));
        Assert.Contains("<svg", File.ReadAllText(first));

        // Eine fremde Datei gleichen Namens wird nicht überschrieben
        File.WriteAllText(Path.Combine(_dir, "symbol-start.svg"), "<svg/>");
        var start = Symbols.WriteFile(Symbols.Start, _dir);
        Assert.Equal("symbol-start-2.svg", Path.GetFileName(start));
        Assert.Equal("<svg/>", File.ReadAllText(Path.Combine(_dir, "symbol-start.svg")));
    }

    [Fact]
    public void SymboleSindOhneSchwarz()
    {
        // Schwarz (Kohlenstoff) verdeckt die OID-Punkte für den Stift
        foreach (var symbol in Symbols.All)
            Assert.DoesNotContain("#000", symbol.Svg);
    }

    [Theory]
    [InlineData(AreaTarget.Product, "Start", 0)]
    [InlineData(AreaTarget.Replay, "Wiederholen", 1)]
    [InlineData(AreaTarget.Stop, "Stopp", 2)]
    public void SymbolMitFlaecheWirdEingefuegt(AreaTarget target, string label, int index)
    {
        var (image, area, areas, elements, selection, areasOnly, product) = HeadlessSkia.Run(() =>
        {
            var vm = new MainWindowViewModel();
            var page = vm.Project.SelectedPage!;
            vm.AreasOnly = true;
            var result = vm.InsertSymbolOn(page, target)!.Value;
            return (result.Image, result.Area, page.Areas.Count, page.Elements.Count, page.Selection.ToList(), vm.AreasOnly,
                (int)vm.Project.ProductId);
        });
        // Bild und Fläche gewählt, damit sie sich zusammen verschieben lassen
        Assert.Equal([image, area], selection);
        Assert.False(areasOnly);
        Assert.Equal(1, areas);
        Assert.Equal(1, elements);
        Assert.Equal(label, area.Label);
        Assert.Equal(target, area.Target);
        Assert.Equal(AreaShape.Ellipse, area.Shape);
        Assert.Equal(Symbols.SizeMm, image.Width);
        Assert.Equal(Symbols.SizeMm, image.Height);
        Assert.Equal((image.X, image.Y, image.Width, image.Height), (area.X, area.Y, area.Width, area.Height));
        // Nebeneinander am unteren Rand
        Assert.Equal(10 + index * (Symbols.SizeMm + 5), image.X, 2);
        Assert.Equal(297 - 10 - Symbols.SizeMm, image.Y, 2);
        if (target == AreaTarget.Product)
            Assert.Equal(product, area.Oid);
    }
}
