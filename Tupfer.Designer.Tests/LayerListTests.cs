using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Ebenen-Liste im Tab „Seiten“: OID-Flächen als eigene Gruppe.</summary>
public class LayerListTests
{
    static (ProjectViewModel Project, PageViewModel Page) NewProject()
    {
        var project = new ProjectViewModel(_ => { });
        var page = new PageViewModel(project) { Name = "Seite 1" };
        project.Pages.Add(page);
        project.SelectedPage = page;
        return (project, page);
    }

    static AreaViewModel AddArea(PageViewModel page, string label)
    {
        var a = new AreaViewModel(page.Project) { Label = label, Width = 10, Height = 10 };
        page.Areas.Add(a);
        return a;
    }

    [Fact]
    public void FlaechenStehenObersteZuerstInDerListe()
    {
        var (_, page) = NewProject();
        Assert.False(page.HasAnyLayers);
        var a = AddArea(page, "A");
        var b = AddArea(page, "B");
        Assert.True(page.HasAreaLayers);
        Assert.True(page.HasAnyLayers);
        Assert.False(page.HasLayers);
        Assert.Equal([b, a], page.AreaLayers);

        page.Areas.Remove(b);
        Assert.Equal([a], page.AreaLayers);
    }

    [Fact]
    public void FlaecheUmsortierenLaesstSichRueckgaengigMachen()
    {
        var (project, page) = NewProject();
        var a = AddArea(page, "A");
        var b = AddArea(page, "B");
        var c = AddArea(page, "C");

        project.History.BeginChange();
        page.MoveLayer(a, 0);
        project.History.EndChange();
        Assert.Equal([b, c, a], page.Areas);
        Assert.Equal([a, c, b], page.AreaLayers);

        project.History.Undo();
        Assert.Equal(["A", "B", "C"], page.Areas.Select(x => x.Label));
    }

    [Fact]
    public void AuswahlMarkiertFlaechenInDerListe()
    {
        var (_, page) = NewProject();
        var a = AddArea(page, "A");
        var b = AddArea(page, "B");

        page.SelectedArea = a;
        Assert.True(a.IsSelected);
        Assert.False(b.IsSelected);

        page.ToggleSelection(b);
        Assert.True(a.IsSelected);
        Assert.True(b.IsSelected);
        Assert.Null(page.SelectedArea);
    }
}
