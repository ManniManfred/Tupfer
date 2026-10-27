using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Tupfer.Designer.Model;

namespace Tupfer.Designer.ViewModels;

/// <summary>Änderung der Zeichenreihenfolge eines Bildes oder Textes.</summary>
public enum ElementOrder { Back, Backward, Forward, Front }

public partial class PageViewModel : ObservableObject
{
    public PageViewModel(ProjectViewModel project)
    {
        Project = project;
        PropertyChanged += (_, e) => Changed?.Invoke(this, e.PropertyName);
        Areas.CollectionChanged += OnCollectionChanged;
        Elements.CollectionChanged += OnCollectionChanged;
        Elements.CollectionChanged += (_, _) => SyncLayers();
    }

    public ProjectViewModel Project { get; }

    [ObservableProperty] string _name = "";
    [ObservableProperty] double _widthMm = 210;
    [ObservableProperty] double _heightMm = 297;

    [ObservableProperty] AreaViewModel? _selectedArea;
    [ObservableProperty] ElementViewModel? _selectedElement;

    public ObservableCollection<AreaViewModel> Areas { get; } = new();

    /// <summary>Bilder und Texte in Zeichenreihenfolge (das letzte liegt oben).</summary>
    public ObservableCollection<ElementViewModel> Elements { get; } = new();

    /// <summary>Bilder und Texte für die Ebenen-Liste: das oberste zuerst.</summary>
    public ObservableCollection<ElementViewModel> Layers { get; } = new();

    /// <summary>
    /// Wird bei jeder Änderung der Seite, ihrer Flächen oder Elemente ausgelöst:
    /// Absender und Name der Eigenschaft, null bei Änderungen der Listen.
    /// </summary>
    public event Action<object, string?>? Changed;

    // Als Einzelauswahl kann immer nur eine Fläche oder ein Element gewählt sein (mehrere: siehe Selection)
    partial void OnSelectedAreaChanged(AreaViewModel? value)
    {
        if (value != null)
            SelectedElement = null;
        OnSingleSelectionChanged(value);
    }

    partial void OnSelectedElementChanged(ElementViewModel? value)
    {
        if (value != null)
            SelectedArea = null;
        OnSingleSelectionChanged(value);
    }

    /// <summary>Gleicht die Ebenen-Liste ab, ohne sie neu aufzubauen.</summary>
    void SyncLayers()
    {
        var target = Elements.Reverse().ToList();
        for (int i = 0; i < target.Count; i++)
        {
            int index = Layers.IndexOf(target[i]);
            if (index < 0)
                Layers.Insert(i, target[i]);
            else if (index != i)
                Layers.Move(index, i);
        }
        while (Layers.Count > target.Count)
            Layers.RemoveAt(Layers.Count - 1);
        OnPropertyChanged(nameof(HasLayers));
    }

    public bool HasLayers => Layers.Count > 0;

    /// <summary>
    /// Ändert die Zeichenreihenfolge. Eine Ebene vor bzw. zurück heißt: über bzw. unter das nächste
    /// Element, das sich mit diesem überschneidet – sonst wäre oft keine Änderung zu sehen.
    /// false, wenn es schon ganz oben bzw. unten liegt.
    /// </summary>
    public bool MoveElement(ElementViewModel element, ElementOrder order)
    {
        int from = Elements.IndexOf(element);
        if (from < 0)
            return false;
        int last = Elements.Count - 1;
        var bounds = element.Bounds();
        int to = order switch
        {
            ElementOrder.Back => 0,
            ElementOrder.Front => last,
            ElementOrder.Forward => Enumerable.Range(from + 1, last - from)
                .FirstOrDefault(i => Elements[i].Bounds().Intersects(bounds), last),
            _ => Enumerable.Range(0, from).Reverse()
                .Select(i => (int?)i).FirstOrDefault(i => Elements[i!.Value].Bounds().Intersects(bounds)) ?? 0,
        };
        if (to == from)
            return false;
        Elements.Move(from, to);
        return true;
    }

    /// <summary>Mindestens eine Fläche oder ein Element ist gewählt (kann gelöscht werden).</summary>
    public bool HasSelection => _selection.Count > 0;

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (INotifyPropertyChanged item in e.OldItems ?? Array.Empty<object>())
            item.PropertyChanged -= OnItemChanged;
        foreach (INotifyPropertyChanged item in e.NewItems ?? Array.Empty<object>())
            item.PropertyChanged += OnItemChanged;
        // Entfernte Objekte aus der Auswahl nehmen
        if (_selection.Any(i => !Areas.Contains(i) && !Elements.Contains(i)))
            SetSelection(_selection.Where(i => Areas.Contains(i) || Elements.Contains(i)).ToList());
        Changed?.Invoke(this, null);
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(sender!, e.PropertyName);

    /// <summary>
    /// Fügt ein Bild ein (bei 150 dpi, höchstens 80 % der Seite), zentriert um <paramref name="centerMm"/>
    /// oder in der Mitte der Seite.
    /// </summary>
    public ElementViewModel? AddImage(string path, Point? centerMm = null)
    {
        if (Project.LoadImage(path) is not { } bmp)
            return null;
        double w = bmp.PixelSize.Width / 150.0 * 25.4, h = bmp.PixelSize.Height / 150.0 * 25.4;
        double f = Math.Min(1, Math.Min(WidthMm * 0.8 / w, HeightMm * 0.8 / h));
        w = Math.Round(w * f, 1);
        h = Math.Round(h * f, 1);
        var center = centerMm ?? new Point(WidthMm / 2, HeightMm / 2);
        var element = new ElementViewModel(Project, ElementKind.Image)
        {
            ImagePath = path,
            Width = w,
            Height = h,
            X = Math.Round(center.X - w / 2, 1),
            Y = Math.Round(center.Y - h / 2, 1),
        };
        Elements.Add(element);
        SelectedElement = element;
        return element;
    }

    public ElementViewModel AddText()
    {
        var element = new ElementViewModel(Project, ElementKind.Text)
        {
            Text = "Text",
            FontFamily = ElementViewModel.DefaultFont,
            Width = 60,
            Height = 15,
        };
        element.X = Math.Round((WidthMm - element.Width) / 2, 1);
        element.Y = Math.Round((HeightMm - element.Height) / 2, 1);
        Elements.Add(element);
        SelectedElement = element;
        return element;
    }

    /// <summary>Neue Farbfläche in der Mitte der Seite.</summary>
    public ElementViewModel AddShape()
    {
        var element = ElementViewModel.NewShape(Project, AreaShape.Rectangle);
        element.Width = 40;
        element.Height = 30;
        element.X = Math.Round((WidthMm - element.Width) / 2, 1);
        element.Y = Math.Round((HeightMm - element.Height) / 2, 1);
        Elements.Add(element);
        SelectedElement = element;
        return element;
    }

    /// <param name="baseDir">Pfade relativ hierzu speichern, null = absolut lassen.</param>
    public PageEntry ToEntry(string? baseDir) => new()
    {
        Name = Name,
        WidthMm = WidthMm,
        HeightMm = HeightMm,
        Elements = Elements.Select(e => e.ToEntry(baseDir)).ToList(),
        Areas = Areas.Select(a => a.ToEntry()).ToList(),
    };

    public static PageViewModel FromEntry(ProjectViewModel project, PageEntry e, string baseDir)
    {
        var page = new PageViewModel(project);
        page.Restore(e, baseDir);
        // Hintergrundbild alter Projekte: als seitenfüllendes Bild ganz unten
        if (!string.IsNullOrEmpty(e.BackgroundImage))
            page.Elements.Insert(0, new ElementViewModel(project, ElementKind.Image)
            {
                ImagePath = Path.GetFullPath(e.BackgroundImage, baseDir),
                Width = e.WidthMm,
                Height = e.HeightMm,
            });
        return page;
    }

    /// <summary>Übernimmt den Inhalt eines gespeicherten Zustands (für Rückgängig/Wiederholen).</summary>
    public void Restore(PageEntry e, string? baseDir, IReadOnlyList<int>? selectedAreas = null, IReadOnlyList<int>? selectedElements = null)
    {
        Name = e.Name;
        WidthMm = e.WidthMm;
        HeightMm = e.HeightMm;
        // Einzeln entfernen statt Clear(), damit die Abmeldung der Ereignisse greift
        while (Areas.Count > 0)
            Areas.RemoveAt(Areas.Count - 1);
        while (Elements.Count > 0)
            Elements.RemoveAt(Elements.Count - 1);
        foreach (var a in e.Areas)
            Areas.Add(AreaViewModel.FromEntry(Project, a));
        foreach (var el in e.Elements)
            Elements.Add(ElementViewModel.FromEntry(Project, el, baseDir));
        Select((selectedAreas ?? []).Where(i => i >= 0 && i < Areas.Count).Select(i => (object)Areas[i])
            .Concat((selectedElements ?? []).Where(i => i >= 0 && i < Elements.Count).Select(i => Elements[i])));
    }
}
