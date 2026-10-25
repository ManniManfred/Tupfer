using Avalonia;
using Tupfer.Designer.Model;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Bearbeiten der Seiten: Rückgängig/Wiederholen, Geometrie der Elemente, alte Projekte.</summary>
public class PageEditTests
{
    static (ProjectViewModel Project, PageViewModel Page) NewProject()
    {
        var project = new ProjectViewModel(_ => { });
        var page = new PageViewModel(project) { Name = "Seite 1" };
        project.Pages.Add(page);
        project.SelectedPage = page;
        return (project, page);
    }

    static ElementViewModel AddText(PageViewModel page, string text = "Hallo")
    {
        var e = new ElementViewModel(page.Project, ElementKind.Text) { Text = text, Width = 40, Height = 10 };
        page.Elements.Add(e);
        return e;
    }

    [Fact]
    public void RueckgaengigUndWiederholen()
    {
        var (project, page) = NewProject();
        var history = project.History;
        Assert.False(history.CanUndo);

        AddText(page);
        page.Elements[0].X = 20;
        Assert.True(history.CanUndo);

        history.Undo();
        Assert.Single(page.Elements);
        Assert.Equal(0, page.Elements[0].X);

        history.Undo();
        Assert.Empty(page.Elements);
        Assert.False(history.CanUndo);

        history.Redo();
        history.Redo();
        Assert.Equal(20, page.Elements[0].X);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void TippenErgibtEinenSchritt()
    {
        var (project, page) = NewProject();
        var e = AddText(page, "");
        foreach (var text in new[] { "H", "Ha", "Hal", "Hall", "Hallo" })
            e.Text = text;

        project.History.Undo();
        Assert.Equal("", page.Elements[0].Text);
    }

    [Fact]
    public void ZusammengefassteAenderung()
    {
        var (project, page) = NewProject();
        AddText(page);
        project.History.Change(() =>
        {
            var e = page.Elements[0];
            e.X = 10;
            e.Y = 20;
            e.Rotation = 45;
        });

        project.History.Undo();
        var u = page.Elements[0];
        Assert.Equal((0.0, 0.0, 0.0), (u.X, u.Y, u.Rotation));
        Assert.True(project.History.CanUndo);
    }

    [Fact]
    public void NeueAenderungLoeschtWiederholen()
    {
        var (project, page) = NewProject();
        AddText(page);
        project.History.Undo();
        Assert.True(project.History.CanRedo);
        AddText(page, "Neu");
        Assert.False(project.History.CanRedo);
    }

    [Fact]
    public void RueckgaengigWechseltZurSeite()
    {
        var (project, page1) = NewProject();
        var page2 = new PageViewModel(project) { Name = "Seite 2" };
        project.Pages.Add(page2);
        AddText(page1);
        project.SelectedPage = page2;

        project.History.Undo();
        Assert.Same(page1, project.SelectedPage);
        Assert.Empty(page1.Elements);
    }

    [Fact]
    public void DrehungUmDieMitte()
    {
        var e = new ElementEntry { X = 10, Y = 20, Width = 40, Height = 20, Rotation = 90 };
        // Die rechte obere Ecke liegt nach 90° rechts unten von der Mitte (30, 30)
        var corner = ElementViewModel.ToPage(e, new Point(40, 0));
        Assert.Equal(40, corner.X, 6);
        Assert.Equal(50, corner.Y, 6);

        var back = ElementViewModel.ToLocal(e, corner);
        Assert.Equal(40, back.X, 6);
        Assert.Equal(0, back.Y, 6);
    }

    [Fact]
    public void HintergrundbildWirdZuBildelement()
    {
        var file = new ProjectFile();
        file.Pages.Add(new PageEntry { Name = "Alt", WidthMm = 100, HeightMm = 150, BackgroundImage = "bild.png" });
        var dir = Path.GetTempPath();
        var project = ProjectViewModel.FromFile(file, dir, _ => { });

        var e = Assert.Single(project.Pages[0].Elements);
        Assert.True(e.IsImage);
        Assert.Equal(Path.GetFullPath("bild.png", dir), e.ImagePath);
        Assert.Equal((0.0, 0.0, 100.0, 150.0), (e.X, e.Y, e.Width, e.Height));

        var saved = project.ToFile(dir).Pages[0];
        Assert.Null(saved.BackgroundImage);
        Assert.Equal("bild.png", Assert.Single(saved.Elements).ImagePath);
    }

    [Fact]
    public void Reihenfolge_EineEbeneUeberspringtNichtUeberlappende()
    {
        var (project, page) = NewProject();
        var a = AddText(page, "A");                 // 0–40 × 0–10
        var b = AddText(page, "B"); b.X = 100;      // liegt woanders
        var c = AddText(page, "C"); c.X = 20;       // überlappt A

        // A eine Ebene nach vorne: über C, B wird übersprungen
        Assert.True(page.MoveElement(a, ElementOrder.Forward));
        Assert.Equal(new[] { b, c, a }, page.Elements);
        Assert.False(page.MoveElement(a, ElementOrder.Forward));

        // Wieder zurück: unter C
        Assert.True(page.MoveElement(a, ElementOrder.Backward));
        Assert.Equal(new[] { b, a, c }, page.Elements);

        // Ohne Überlappung nach hinten: ganz nach unten
        Assert.False(page.MoveElement(c, ElementOrder.Front));
        Assert.True(page.MoveElement(b, ElementOrder.Front));
        Assert.Equal(new[] { a, c, b }, page.Elements);
        Assert.True(page.MoveElement(b, ElementOrder.Backward));
        Assert.Equal(new[] { b, a, c }, page.Elements);

        // Ebenen-Liste: oberstes zuerst; Rückgängig stellt die Reihenfolge wieder her
        Assert.Equal(new[] { c, a, b }, page.Layers);
        project.History.Undo();
        Assert.Equal(new[] { "A", "C", "B" }, page.Elements.Select(e => e.Text));
        Assert.Equal(new[] { "B", "C", "A" }, page.Layers.Select(e => e.Text));
    }

    [Fact]
    public void Reihenfolge_GanzNachVorneUndHinten()
    {
        var (_, page) = NewProject();
        var a = AddText(page, "A");
        var b = AddText(page, "B");
        var c = AddText(page, "C");
        Assert.True(page.MoveElement(a, ElementOrder.Front));
        Assert.Equal(new[] { b, c, a }, page.Elements);
        Assert.True(page.MoveElement(a, ElementOrder.Back));
        Assert.Equal(new[] { a, b, c }, page.Elements);
        Assert.False(page.MoveElement(a, ElementOrder.Back));
    }

    [Fact]
    public void Ebenenliste_ZeigtAuswahl()
    {
        var (_, page) = NewProject();
        var a = AddText(page, "A");
        var b = AddText(page, "B");
        page.SelectedElement = a;
        Assert.True(a.IsSelected);
        page.SelectedElement = b;
        Assert.False(a.IsSelected);
        Assert.True(b.IsSelected);
        page.SelectedArea = new AreaViewModel(page.Project);
        Assert.False(b.IsSelected);
    }
}
