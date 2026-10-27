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
    public void FarbflaecheSpeichernUndRueckgaengig()
    {
        var (project, page) = NewProject();
        var shape = page.AddShape();
        Assert.True(shape.IsShape && shape.HasColor);
        shape.Color = "#90CAF9";
        shape.ShapeOption = AreaViewModel.ShapeOptions.First(o => o.Value == AreaShape.Ellipse);

        // Speichern und Laden
        var json = System.Text.Json.JsonSerializer.Serialize(page.ToEntry(null),
            new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        Assert.Contains("\"Shape\"", json);
        var copy = ElementViewModel.FromEntry(project, page.Elements[0].ToEntry(null), null);
        Assert.Equal(ElementKind.Shape, copy.Kind);
        Assert.Equal("#90CAF9", copy.Color);
        Assert.Equal(AreaShape.Ellipse, copy.Shape);
        Assert.Equal("Farbfläche: Kreis / Ellipse", copy.Caption);

        project.History.Undo();
        Assert.Equal(AreaShape.Rectangle, page.Elements[0].Shape);

        // Projekte aus 1.18 kannten nur „Ellipse“
        var old = ElementViewModel.FromEntry(project, new ElementEntry { Kind = ElementKind.Shape, Ellipse = true, Width = 10, Height = 10 }, null);
        Assert.Equal(AreaShape.Ellipse, old.Shape);
    }

    [Fact]
    public void FarbflaecheMitRand()
    {
        var (project, page) = NewProject();
        var shape = page.AddShape();
        Assert.Null(shape.StrokePen(1));
        shape.StrokeWidth = 1.5;
        shape.StrokeColor = "#1976D2";
        Assert.Equal(1.5 * 4, shape.StrokePen(4)!.Thickness);

        var copy = ElementViewModel.FromEntry(project, shape.ToEntry(null), null);
        Assert.Equal(1.5, copy.StrokeWidth);
        Assert.Equal("#1976D2", copy.StrokeColor);

        shape.ClearFillCommand.Execute(null);
        Assert.Equal(0, Avalonia.Media.Color.Parse(shape.Color).A);

        // Rückgängig stellt die Seite neu her (neue Objekte)
        project.History.Undo();
        Assert.NotEqual(0, Avalonia.Media.Color.Parse(page.Elements[0].Color).A);
        Assert.Equal(1.5, page.Elements[0].StrokeWidth);

        // Negative Breite gibt es nicht
        page.Elements[0].StrokeWidth = -1;
        Assert.Equal(0, page.Elements[0].StrokeWidth);
    }

    [Fact]
    public void GedrehtesFarbpolygon()
    {
        var (_, page) = NewProject();
        var shape = page.AddShape();
        shape.X = 10; shape.Y = 20; shape.Width = 40; shape.Height = 20; shape.Rotation = 90;
        shape.ChangeShape(AreaShape.Polygon);
        Assert.Equal(4, shape.Points.Count);

        // Gedreht um die Mitte (30, 30): die linke obere Ecke (10, 20) liegt jetzt bei (40, 10)
        var corners = shape.PagePoints.ToList();
        Assert.Equal(40, corners[0].X, 6);
        Assert.Equal(10, corners[0].Y, 6);

        // Eine Ecke auf der Seite verschieben: die übrigen bleiben stehen, Drehung bleibt
        corners[2] = corners[2] with { X = corners[2].X - 10 };
        shape.SetPolygon(corners);
        var moved = shape.PagePoints.ToList();
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(corners[i].X, moved[i].X, 1);
            Assert.Equal(corners[i].Y, moved[i].Y, 1);
        }
        Assert.Equal(90, shape.Rotation);

        // Treffer nur innerhalb des Umrisses
        Assert.True(shape.Contains(new Avalonia.Point(30, 30)));
        Assert.False(shape.Contains(new Avalonia.Point(corners[2].X + 9, corners[2].Y)));
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

    static AreaViewModel AddArea(PageViewModel page, double x, double y, double w, double h)
    {
        var a = new AreaViewModel(page.Project) { X = x, Y = y, Width = w, Height = h };
        page.Areas.Add(a);
        return a;
    }

    [Fact]
    public void MehrfachauswahlUndEinzelauswahl()
    {
        var (_, page) = NewProject();
        var text = AddText(page);
        var area = AddArea(page, 10, 10, 20, 20);

        page.SelectedElement = text;
        Assert.Equal([text], page.Selection);
        Assert.True(text.IsSelected);

        // Hinzunehmen: keine Einzelauswahl mehr
        page.ToggleSelection(area);
        Assert.True(page.IsMultiSelection);
        Assert.Null(page.SelectedElement);
        Assert.Null(page.SelectedArea);
        Assert.True(text.IsSelected);

        // Herausnehmen: das übrige ist wieder einzeln gewählt
        page.ToggleSelection(text);
        Assert.Same(area, page.SelectedArea);
        Assert.False(text.IsSelected);

        // Eine normale Auswahl ersetzt die Mehrfachauswahl
        page.ToggleSelection(text);
        page.SelectedElement = text;
        Assert.Equal([text], page.Selection);

        // Entfernte Objekte fallen aus der Auswahl
        page.SelectAll(true);
        Assert.Equal(2, page.Selection.Count);
        page.Elements.Remove(text);
        Assert.Same(area, page.SelectedArea);

        page.SelectAll(true);
        page.DeleteSelection();
        Assert.Empty(page.Areas);
        Assert.False(page.HasSelection);
    }

    [Fact]
    public void AusrichtenAneinanderUndAnDerSeite()
    {
        var (project, page) = NewProject();
        var a = AddArea(page, 30, 10, 20, 10);
        var b = AddArea(page, 50, 40, 40, 20);
        var t = AddText(page);
        t.X = 70;
        t.Y = 100;
        page.Select([a, b, t]);

        project.History.Change(() => page.Align(page.Selection, AlignMode.Left));
        Assert.Equal([30.0, 30, 30], [a.X, b.X, t.X]);
        project.History.Change(() => page.Align(page.Selection, AlignMode.Right));
        Assert.Equal([50.0, 30, 30], [a.X, b.X, t.X]);
        project.History.Change(() => page.Align(page.Selection, AlignMode.CenterY));
        // Umschließend 10 … 110, Mitte 60
        Assert.Equal([55.0, 50, 55], [a.Y, b.Y, t.Y]);

        // Rückgängig stellt Lage und Auswahl wieder her (mit neu aufgebauten Objekten)
        project.History.Undo();
        (a, b, t) = (page.Areas[0], page.Areas[1], page.Elements[0]);
        Assert.Equal([10.0, 40, 100], [a.Y, b.Y, t.Y]);
        Assert.Equal([a, b, t], page.Selection.OrderBy(i => i is ElementViewModel));
        project.History.Undo();
        (a, b, t) = (page.Areas[0], page.Areas[1], page.Elements[0]);
        Assert.Equal([30.0, 30, 30], [a.X, b.X, t.X]);

        // Ein einzelnes Objekt richtet sich an der Seite aus (A4)
        page.SelectedArea = b;
        page.Align(page.Selection, AlignMode.CenterX);
        page.Align(page.Selection, AlignMode.Bottom);
        Assert.Equal(85, b.X);
        Assert.Equal(277, b.Y);
    }

    [Fact]
    public void AusrichtenNutztDenUmrissGedrehterElemente()
    {
        var (_, page) = NewProject();
        var area = AddArea(page, 10, 10, 20, 20);
        var t = AddText(page);
        t.X = 50;
        t.Y = 50;
        t.Rotation = 90;
        // Gedreht ist der Text 10 breit und 40 hoch, mit der Mitte bei (70, 55): links bei 65
        page.Select([area, t]);
        page.Align(page.Selection, AlignMode.Left);
        Assert.Equal(10, t.Bounds().Left, 6);
        Assert.Equal(-5, t.X, 6);
    }

    [Fact]
    public void VerteilenMitGleichenAbstaenden()
    {
        var (_, page) = NewProject();
        var a = AddArea(page, 10, 0, 10, 10);
        var b = AddArea(page, 25, 0, 30, 10);
        var c = AddArea(page, 100, 0, 20, 10);
        var d = AddArea(page, 60, 0, 10, 10);
        page.Select([c, a, d, b]);
        page.Distribute(page.Selection, horizontal: true);
        // Spanne 10 … 120, Breiten zusammen 70: drei Lücken zu je 40/3
        Assert.Equal(10, a.X);
        Assert.Equal(Math.Round(20 + 40.0 / 3, 2), b.X);
        Assert.Equal(Math.Round(50 + 80.0 / 3, 2), d.X);
        Assert.Equal(100, c.X);

        // Zwei Objekte bleiben stehen
        page.Select([a, b]);
        page.Distribute(page.Selection, horizontal: false);
        Assert.Equal(0, b.Y);
    }
}
