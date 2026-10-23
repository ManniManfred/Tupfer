using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using TipToy.Designer.Model;

namespace TipToy.Designer.ViewModels;

public partial class PageViewModel : ObservableObject
{
    public PageViewModel(ProjectViewModel project)
    {
        Project = project;
        PropertyChanged += (_, e) => Changed?.Invoke(this, e.PropertyName);
        Areas.CollectionChanged += OnCollectionChanged;
        Elements.CollectionChanged += OnCollectionChanged;
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

    /// <summary>
    /// Wird bei jeder Änderung der Seite, ihrer Flächen oder Elemente ausgelöst:
    /// Absender und Name der Eigenschaft, null bei Änderungen der Listen.
    /// </summary>
    public event Action<object, string?>? Changed;

    // Es kann immer nur eine Fläche oder ein Element gewählt sein
    partial void OnSelectedAreaChanged(AreaViewModel? value)
    {
        if (value != null)
            SelectedElement = null;
    }

    partial void OnSelectedElementChanged(ElementViewModel? value)
    {
        if (value != null)
            SelectedArea = null;
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (INotifyPropertyChanged item in e.OldItems ?? Array.Empty<object>())
            item.PropertyChanged -= OnItemChanged;
        foreach (INotifyPropertyChanged item in e.NewItems ?? Array.Empty<object>())
            item.PropertyChanged += OnItemChanged;
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
    public void Restore(PageEntry e, string? baseDir, int selectedArea = -1, int selectedElement = -1)
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
        SelectedArea = selectedArea >= 0 && selectedArea < Areas.Count ? Areas[selectedArea] : null;
        SelectedElement = selectedElement >= 0 && selectedElement < Elements.Count ? Elements[selectedElement] : null;
    }
}
