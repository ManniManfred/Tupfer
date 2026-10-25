using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TipToy.Designer.Model;

namespace TipToy.Designer.ViewModels;

public record AlignOption(TextAlign Value, string Name);

public record ColorPreset(string Value, IBrush Brush);

/// <summary>Ein Bild oder Text auf einer Seite. Maße in mm, gedreht wird um die Mitte des Rahmens.</summary>
public partial class ElementViewModel : ObservableObject
{
    public static IReadOnlyList<AlignOption> AlignOptions { get; } = new[]
    {
        new AlignOption(TextAlign.Left, "Links"),
        new AlignOption(TextAlign.Center, "Mitte"),
        new AlignOption(TextAlign.Right, "Rechts"),
    };

    /// <summary>Farben zur schnellen Auswahl für Texte.</summary>
    public static IReadOnlyList<ColorPreset> ColorPresets { get; } = new[]
        {
            "#000000", "#FFFFFF", "#D32F2F", "#F57C00", "#FBC02D", "#388E3C", "#1976D2", "#7B1FA2",
        }
        .Select(c => new ColorPreset(c, new SolidColorBrush(Avalonia.Media.Color.Parse(c))))
        .ToList();

    static IReadOnlyList<string>? _fontNames;

    /// <summary>Auf dem System installierte Schriftarten.</summary>
    public static IReadOnlyList<string> FontNames => _fontNames ??= LoadFontNames();

    static IReadOnlyList<string> LoadFontNames()
    {
        try
        {
            return FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().Order().ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Schriftart für neue Texte.</summary>
    public static string DefaultFont => FontNames.Contains("Arial") ? "Arial" : "";

    public ElementViewModel(ProjectViewModel project, ElementKind kind)
    {
        Project = project;
        Kind = kind;
    }

    public ProjectViewModel Project { get; }
    public ElementKind Kind { get; }
    public bool IsImage => Kind == ElementKind.Image;
    public bool IsText => Kind == ElementKind.Text;

    [ObservableProperty] double _x;
    [ObservableProperty] double _y;
    [ObservableProperty] double _width;
    [ObservableProperty] double _height;
    [ObservableProperty] double _rotation;

    /// <summary>Absoluter Pfad des Bilds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Image), nameof(Caption), nameof(FileName))]
    string _imagePath = "";

    public string FileName => Path.GetFileName(ImagePath);

    [ObservableProperty] double _cropX;
    [ObservableProperty] double _cropY;
    [ObservableProperty] double _cropWidth = 1;
    [ObservableProperty] double _cropHeight = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caption))]
    string _text = "";

    [ObservableProperty] double _fontSize = 24;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontChoice))]
    string _fontFamily = "";

    /// <summary>Für die Auswahlliste: null wird ignoriert (Schriftart nicht in der Liste).</summary>
    public string? FontChoice
    {
        get => FontFamily;
        set
        {
            if (value != null)
                FontFamily = value;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorBrush))]
    string _color = "#000000";

    [ObservableProperty] bool _bold;
    [ObservableProperty] bool _italic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlignOption))]
    TextAlign _align;

    public Bitmap? Image => IsImage ? Project.LoadImage(ImagePath) : null;

    public IBrush ColorBrush => Avalonia.Media.Color.TryParse(Color, out var c) ? new SolidColorBrush(c) : Brushes.Black;

    public string Caption => IsImage
        ? $"Bild: {Path.GetFileName(ImagePath)}"
        : $"Text: {Text.Split('\n')[0].Trim()}";

    public AlignOption AlignOption
    {
        get => AlignOptions.First(o => o.Value == Align);
        set
        {
            if (value != null)
                Align = value.Value;
        }
    }

    public Point Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>Gewählt (für die Ebenen-Liste); gehört nicht zum Projekt.</summary>
    [ObservableProperty] bool _isSelected;

    /// <summary>Umschließendes Rechteck auf der Seite (mm), auch bei gedrehten Elementen.</summary>
    public Rect Bounds()
    {
        var e = ToEntry(null);
        var corners = new[] { new Point(0, 0), new Point(Width, 0), new Point(0, Height), new Point(Width, Height) }
            .Select(p => ToPage(e, p)).ToList();
        double left = corners.Min(p => p.X), top = corners.Min(p => p.Y);
        return new Rect(left, top, corners.Max(p => p.X) - left, corners.Max(p => p.Y) - top);
    }

    partial void OnRotationChanged(double value)
    {
        // Auf -180 … 180 Grad halten
        var normalized = value - 360 * Math.Round(value / 360);
        if (normalized != value)
            Rotation = normalized;
    }

    /// <summary>Rechnet einen Punkt der Seite (mm) in den ungedrehten Rahmen um (0,0 = linke obere Ecke).</summary>
    public static Point ToLocal(ElementEntry e, Point page)
    {
        double a = -e.Rotation * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double dx = page.X - (e.X + e.Width / 2), dy = page.Y - (e.Y + e.Height / 2);
        return new Point(dx * cos - dy * sin + e.Width / 2, dx * sin + dy * cos + e.Height / 2);
    }

    /// <summary>Gegenstück zu <see cref="ToLocal"/>.</summary>
    public static Point ToPage(ElementEntry e, Point local)
    {
        double a = e.Rotation * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double dx = local.X - e.Width / 2, dy = local.Y - e.Height / 2;
        return new Point(dx * cos - dy * sin + e.X + e.Width / 2, dx * sin + dy * cos + e.Y + e.Height / 2);
    }

    public bool Contains(Point page, double toleranceMm = 0)
    {
        var p = ToLocal(ToEntry(null), page);
        return p.X >= -toleranceMm && p.Y >= -toleranceMm && p.X <= Width + toleranceMm && p.Y <= Height + toleranceMm;
    }

    /// <summary>Erzeugt den Text zum Zeichnen; <paramref name="scale"/> = Pixel pro mm.</summary>
    public FormattedText CreateText(double scale)
    {
        var family = string.IsNullOrWhiteSpace(FontFamily) ? Avalonia.Media.FontFamily.Default : new FontFamily(FontFamily);
        var typeface = new Typeface(family, Italic ? FontStyle.Italic : FontStyle.Normal, Bold ? FontWeight.Bold : FontWeight.Normal);
        return new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface,
            Math.Max(0.1, FontSize * 25.4 / 72 * scale), ColorBrush)
        {
            MaxTextWidth = Math.Max(1, Width * scale),
            TextAlignment = Align switch
            {
                TextAlign.Center => TextAlignment.Center,
                TextAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
        };
    }

    [RelayCommand]
    void SetColor(string color) => Color = color;

    [RelayCommand]
    void Rotate(string degrees) => Rotation = Math.Round(Rotation + double.Parse(degrees, CultureInfo.InvariantCulture), 1);

    /// <summary>Zeigt wieder das ganze Bild im gleichen Maßstab, die Mitte bleibt erhalten.</summary>
    [RelayCommand]
    void ResetCrop()
    {
        if (!IsImage || (CropX == 0 && CropY == 0 && CropWidth == 1 && CropHeight == 1))
            return;
        var start = ToEntry(null);
        double w = Width / CropWidth, h = Height / CropHeight;
        // Mitte des ganzen Bilds im ungedrehten Rahmen
        var center = ToPage(start, new Point(w * (0.5 - CropX), h * (0.5 - CropY)));
        Project.History.Change(() =>
        {
            CropX = CropY = 0;
            CropWidth = CropHeight = 1;
            Width = Math.Round(w, 1);
            Height = Math.Round(h, 1);
            X = Math.Round(center.X - Width / 2, 1);
            Y = Math.Round(center.Y - Height / 2, 1);
        });
    }

    /// <param name="baseDir">Pfade relativ hierzu speichern, null = absolut lassen.</param>
    public ElementEntry ToEntry(string? baseDir) => new()
    {
        Kind = Kind, X = X, Y = Y, Width = Width, Height = Height, Rotation = Rotation,
        ImagePath = baseDir == null || string.IsNullOrEmpty(ImagePath) ? ImagePath : Path.GetRelativePath(baseDir, ImagePath),
        CropX = CropX, CropY = CropY, CropWidth = CropWidth, CropHeight = CropHeight,
        Text = Text, FontSize = FontSize, FontFamily = FontFamily, Color = Color, Bold = Bold, Italic = Italic, Align = Align,
    };

    public static ElementViewModel FromEntry(ProjectViewModel project, ElementEntry e, string? baseDir) => new(project, e.Kind)
    {
        X = e.X, Y = e.Y, Width = e.Width, Height = e.Height, Rotation = e.Rotation,
        ImagePath = baseDir == null || string.IsNullOrEmpty(e.ImagePath) ? e.ImagePath : Path.GetFullPath(e.ImagePath, baseDir),
        CropX = e.CropX, CropY = e.CropY, CropWidth = e.CropWidth, CropHeight = e.CropHeight,
        Text = e.Text, FontSize = e.FontSize, FontFamily = e.FontFamily, Color = e.Color, Bold = e.Bold, Italic = e.Italic, Align = e.Align,
    };
}
