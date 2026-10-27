using CommunityToolkit.Mvvm.ComponentModel;
using Tupfer.Designer.Model;

namespace Tupfer.Designer.ViewModels;

public record TargetOption(AreaTarget Value, string Name);

public record ShapeOption(AreaShape Value, string Name);

/// <summary>
/// Eine Fläche auf einer Seite, die mit einem OID-Code bedruckt wird. Maße in mm.
/// X/Y/Width/Height sind der umschließende Rahmen; Polygonpunkte liegen relativ zu X/Y,
/// sodass Verschieben nur X/Y ändert und eine neue Breite/Höhe die Punkte mitskaliert.
/// </summary>
public partial class AreaViewModel : ObservableObject, IPolygonShape
{
    public static IReadOnlyList<TargetOption> TargetOptions { get; } = new[]
    {
        new TargetOption(AreaTarget.Script, "Skript"),
        new TargetOption(AreaTarget.Product, "Produkt (Anmelden)"),
        new TargetOption(AreaTarget.Replay, "Wiederholen"),
        new TargetOption(AreaTarget.Stop, "Stopp"),
    };

    public static IReadOnlyList<ShapeOption> ShapeOptions { get; } = new[]
    {
        new ShapeOption(AreaShape.Rectangle, "Rechteck"),
        new ShapeOption(AreaShape.Ellipse, "Kreis / Ellipse"),
        new ShapeOption(AreaShape.Polygon, "Polygon"),
    };

    public AreaViewModel(ProjectViewModel project)
    {
        Project = project;
    }

    public ProjectViewModel Project { get; }

    [ObservableProperty] string _label = "";
    [ObservableProperty] double _x;
    [ObservableProperty] double _y;
    [ObservableProperty] double _width;
    [ObservableProperty] double _height;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPolygon), nameof(IsRectangle), nameof(ShapeOption))]
    AreaShape _shape;

    /// <summary>Eckpunkte des Polygons relativ zu X/Y (wird immer als Ganzes ersetzt).</summary>
    [ObservableProperty] IReadOnlyList<AreaPoint> _points = [];

    public bool IsPolygon => Shape == AreaShape.Polygon;
    public bool IsRectangle => Shape == AreaShape.Rectangle;

    public ShapeOption ShapeOption
    {
        get => ShapeOptions.First(o => o.Value == Shape);
        set
        {
            if (value != null)
                ChangeShape(value.Value);
        }
    }

    /// <summary>Wechselt die Form im selben Rahmen; ein neues Polygon beginnt als Rechteck.</summary>
    public void ChangeShape(AreaShape shape)
    {
        if (shape == Shape)
            return;
        if (shape == AreaShape.Polygon)
            Points = [new(0, 0), new(Width, 0), new(Width, Height), new(0, Height)];
        Shape = shape;
        if (shape != AreaShape.Polygon)
            Points = [];
    }

    bool _settingFrame;

    // Neue Breite/Höhe (Eingabe oder Anfasser) skaliert die Polygonpunkte mit
    partial void OnWidthChanged(double oldValue, double newValue)
    {
        if (!_settingFrame && IsPolygon && oldValue > 0 && newValue > 0)
            Points = Points.Select(p => p with { X = p.X * newValue / oldValue }).ToList();
    }

    partial void OnHeightChanged(double oldValue, double newValue)
    {
        if (!_settingFrame && IsPolygon && oldValue > 0 && newValue > 0)
            Points = Points.Select(p => p with { Y = p.Y * newValue / oldValue }).ToList();
    }

    /// <summary>Polygonpunkte in Seitenkoordinaten.</summary>
    public IEnumerable<AreaPoint> PagePoints => Points.Select(p => p with { X = X + p.X, Y = Y + p.Y });

    /// <summary>
    /// Setzt das Polygon aus Punkten in Seitenkoordinaten; der Rahmen wird an den Umriss
    /// (einschließlich der Bögen) angepasst.
    /// </summary>
    public void SetPolygon(IReadOnlyList<AreaPoint> pagePoints)
    {
        var outline = AreaGeometry.Outline(AreaShape.Polygon, 0, 0, 0, 0, pagePoints);
        var (bx, by, bw, bh) = AreaGeometry.Bounds(outline);
        _settingFrame = true;
        try
        {
            Shape = AreaShape.Polygon;
            X = Math.Round(bx, 2);
            Y = Math.Round(by, 2);
            Width = Math.Round(bw, 2);
            Height = Math.Round(bh, 2);
            Points = pagePoints.Select(p => p with { X = Math.Round(p.X - X, 3), Y = Math.Round(p.Y - Y, 3) }).ToList();
        }
        finally
        {
            _settingFrame = false;
        }
    }

    /// <summary>Umriss in Seitenkoordinaten (mm).</summary>
    public List<(double X, double Y)> Outline() => AreaGeometry.Outline(Shape, X, Y, Width, Height, Points);

    public bool Contains(double x, double y) =>
        IsRectangle ? x >= X && x <= X + Width && y >= Y && y <= Y + Height : AreaGeometry.Contains(Outline(), x, y);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetOption), nameof(IsScript), nameof(Oid), nameof(Caption))]
    AreaTarget _target;

    string _script = "";

    /// <summary>Name des Skripts. null wird ignoriert (ComboBox setzt kurzzeitig null beim Aktualisieren der Liste).</summary>
    public string? Script
    {
        get => _script;
        set
        {
            if (value != null && SetProperty(ref _script, value))
                RefreshOid();
        }
    }

    public bool IsScript => Target == AreaTarget.Script;

    public TargetOption TargetOption
    {
        get => TargetOptions.First(o => o.Value == Target);
        set
        {
            if (value != null)
                Target = value.Value;
        }
    }

    public int? Oid => Target switch
    {
        AreaTarget.Product => (int)Project.ProductId,
        AreaTarget.Replay => Project.Replay,
        AreaTarget.Stop => Project.Stop,
        _ => Project.Scripts.FirstOrDefault(s => s.Name == _script)?.Code,
    };

    public string Caption
    {
        get
        {
            var name = Target == AreaTarget.Script ? _script : TargetOption.Name;
            if (!string.IsNullOrWhiteSpace(Label))
                name = Label;
            return Oid is { } oid ? $"{name} ({oid})" : $"{name} (kein Code)";
        }
    }

    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(Caption));

    public void RefreshOid()
    {
        OnPropertyChanged(nameof(Oid));
        OnPropertyChanged(nameof(Caption));
    }

    public AreaEntry ToEntry() => new()
    {
        Label = Label, X = X, Y = Y, Width = Width, Height = Height, Target = Target, Script = _script,
        Shape = Shape, Points = IsPolygon ? Points.ToList() : null,
    };

    public static AreaViewModel FromEntry(ProjectViewModel project, AreaEntry e)
    {
        var area = new AreaViewModel(project)
        {
            Label = e.Label, X = e.X, Y = e.Y, Width = e.Width, Height = e.Height, Target = e.Target, Script = e.Script,
        };
        // Polygon ohne gültige Punkte wird zum Rechteck
        if (e.Shape == AreaShape.Polygon && e.Points is { Count: >= 3 } points)
        {
            area.Points = points.ToList();
            area.Shape = AreaShape.Polygon;
        }
        else if (e.Shape == AreaShape.Ellipse)
        {
            area.Shape = AreaShape.Ellipse;
        }
        return area;
    }
}
