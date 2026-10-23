using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TipToy.Designer.Model;
using TipToy.Designer.ViewModels;

namespace TipToy.Designer.Views;

/// <summary>
/// Zeichenfläche einer Seite: zeigt Hintergrundbild und OID-Flächen.
/// Ziehen auf freier Fläche legt eine neue Fläche an, Ziehen einer Fläche verschiebt sie,
/// der Anfasser unten rechts ändert die Größe, Entf löscht die Auswahl.
/// </summary>
public class PageCanvas : Control
{
    public static readonly StyledProperty<PageViewModel?> PageProperty =
        AvaloniaProperty.Register<PageCanvas, PageViewModel?>(nameof(Page));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(Zoom), 1.0);

    const double PxPerMmAt100 = 96 / 25.4;
    const double HandleSize = 10;
    const double Grid = 0.5;
    const double MinSizeMm = 3;

    enum DragMode { None, Create, Move, Resize }

    DragMode _mode;
    Point _startMm;
    AreaEntry? _startRect;
    PageViewModel? _subscribed;

    static readonly IBrush AreaFill = new SolidColorBrush(Color.FromArgb(70, 30, 120, 255));
    static readonly IBrush AreaFillInvalid = new SolidColorBrush(Color.FromArgb(90, 230, 40, 40));
    static readonly IBrush SelectedFill = new SolidColorBrush(Color.FromArgb(110, 255, 160, 0));
    static readonly IPen AreaPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 90, 200)), 1);
    static readonly IPen SelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 110, 0)), 2);
    static readonly IPen PageBorder = new Pen(Brushes.Gray, 1);
    static readonly IBrush LabelBackground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));

    static PageCanvas()
    {
        AffectsMeasure<PageCanvas>(PageProperty, ZoomProperty);
        AffectsRender<PageCanvas>(PageProperty, ZoomProperty);
        FocusableProperty.OverrideDefaultValue<PageCanvas>(true);
    }

    public PageViewModel? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    double Scale => PxPerMmAt100 * Zoom;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
            Subscribe(change.GetNewValue<PageViewModel?>());
    }

    void Subscribe(PageViewModel? page)
    {
        if (_subscribed != null)
        {
            _subscribed.PropertyChanged -= OnPageChanged;
            _subscribed.Areas.CollectionChanged -= OnAreasChanged;
            foreach (var a in _subscribed.Areas)
                a.PropertyChanged -= OnAreaChanged;
        }
        _subscribed = page;
        if (page != null)
        {
            page.PropertyChanged += OnPageChanged;
            page.Areas.CollectionChanged += OnAreasChanged;
            foreach (var a in page.Areas)
                a.PropertyChanged += OnAreaChanged;
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PageViewModel.WidthMm) or nameof(PageViewModel.HeightMm))
            InvalidateMeasure();
        InvalidateVisual();
    }

    void OnAreasChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (AreaViewModel a in e.OldItems ?? Array.Empty<AreaViewModel>())
            a.PropertyChanged -= OnAreaChanged;
        foreach (AreaViewModel a in e.NewItems ?? Array.Empty<AreaViewModel>())
            a.PropertyChanged += OnAreaChanged;
        InvalidateVisual();
    }

    void OnAreaChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize) =>
        Page is { } p ? new Size(p.WidthMm * Scale + 1, p.HeightMm * Scale + 1) : default;

    Rect ToPx(AreaViewModel a) => new(a.X * Scale, a.Y * Scale, a.Width * Scale, a.Height * Scale);

    public override void Render(DrawingContext ctx)
    {
        if (Page is not { } page)
            return;

        var pageRect = new Rect(0, 0, page.WidthMm * Scale, page.HeightMm * Scale);
        ctx.FillRectangle(Brushes.White, pageRect);
        if (page.Background is { } bg)
            ctx.DrawImage(bg, pageRect);
        ctx.DrawRectangle(PageBorder, pageRect);

        foreach (var area in page.Areas)
        {
            var r = ToPx(area);
            bool selected = area == page.SelectedArea;
            var fill = selected ? SelectedFill : area.Oid == null ? AreaFillInvalid : AreaFill;
            ctx.DrawRectangle(fill, selected ? SelectedPen : AreaPen, r);

            var text = new FormattedText(area.Caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, Math.Clamp(11 * Zoom, 9, 16), Brushes.Black)
            {
                MaxTextWidth = Math.Max(10, r.Width - 4),
                MaxTextHeight = Math.Max(10, r.Height - 4),
                Trimming = TextTrimming.CharacterEllipsis,
            };
            ctx.FillRectangle(LabelBackground, new Rect(r.X + 1, r.Y + 1, Math.Min(text.Width + 4, r.Width - 2), Math.Min(text.Height + 2, r.Height - 2)));
            ctx.DrawText(text, new Point(r.X + 3, r.Y + 2));

            if (selected)
                ctx.FillRectangle(Brushes.DarkOrange, HandleRect(r));
        }
    }

    static Rect HandleRect(Rect r) => new(r.Right - HandleSize / 2, r.Bottom - HandleSize / 2, HandleSize, HandleSize);

    static double Snap(double v) => Math.Round(v / Grid) * Grid;

    Point ToMm(Point px) => new(px.X / Scale, px.Y / Scale);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Page is not { } page || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Focus();
        var pos = e.GetPosition(this);
        _startMm = ToMm(pos);

        if (page.SelectedArea is { } sel && HandleRect(ToPx(sel)).Inflate(3).Contains(pos))
        {
            _mode = DragMode.Resize;
            _startRect = sel.ToEntry();
        }
        else if (page.Areas.LastOrDefault(a => ToPx(a).Contains(pos)) is { } hit)
        {
            page.SelectedArea = hit;
            _mode = DragMode.Move;
            _startRect = hit.ToEntry();
        }
        else
        {
            var project = page.Project;
            var area = new AreaViewModel(project)
            {
                X = Snap(_startMm.X),
                Y = Snap(_startMm.Y),
                Target = AreaTarget.Script,
                Script = project.SelectedScript?.Name ?? project.Scripts.FirstOrDefault()?.Name ?? "",
            };
            page.Areas.Add(area);
            page.SelectedArea = area;
            _mode = DragMode.Create;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Page is not { SelectedArea: { } area } page)
            return;

        var pos = ToMm(e.GetPosition(this));
        double maxX = page.WidthMm, maxY = page.HeightMm;
        pos = new Point(Math.Clamp(pos.X, 0, maxX), Math.Clamp(pos.Y, 0, maxY));

        switch (_mode)
        {
            case DragMode.Create:
                area.X = Snap(Math.Min(_startMm.X, pos.X));
                area.Y = Snap(Math.Min(_startMm.Y, pos.Y));
                area.Width = Snap(Math.Abs(pos.X - _startMm.X));
                area.Height = Snap(Math.Abs(pos.Y - _startMm.Y));
                break;
            case DragMode.Move when _startRect != null:
                area.X = Snap(Math.Clamp(_startRect.X + pos.X - _startMm.X, 0, maxX - area.Width));
                area.Y = Snap(Math.Clamp(_startRect.Y + pos.Y - _startMm.Y, 0, maxY - area.Height));
                break;
            case DragMode.Resize when _startRect != null:
                area.Width = Snap(Math.Max(MinSizeMm, _startRect.Width + pos.X - _startMm.X));
                area.Height = Snap(Math.Max(MinSizeMm, _startRect.Height + pos.Y - _startMm.Y));
                break;
            default:
                var over = page.SelectedArea != null && HandleRect(ToPx(area)).Inflate(3).Contains(e.GetPosition(this));
                Cursor = over ? new Cursor(StandardCursorType.BottomRightCorner) : Cursor.Default;
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == DragMode.Create && Page is { SelectedArea: { } area } page
            && (area.Width < MinSizeMm || area.Height < MinSizeMm))
        {
            // Nur geklickt: keine Fläche anlegen, Auswahl aufheben
            page.Areas.Remove(area);
            page.SelectedArea = null;
        }
        _mode = DragMode.None;
        _startRect = null;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Page is not { SelectedArea: { } area } page)
            return;

        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 0.5;
        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                page.Areas.Remove(area);
                page.SelectedArea = null;
                break;
            case Key.Left: area.X = Math.Max(0, area.X - step); break;
            case Key.Right: area.X = Math.Min(page.WidthMm - area.Width, area.X + step); break;
            case Key.Up: area.Y = Math.Max(0, area.Y - step); break;
            case Key.Down: area.Y = Math.Min(page.HeightMm - area.Height, area.Y + step); break;
            case Key.Escape: page.SelectedArea = null; break;
            default: return;
        }
        e.Handled = true;
    }
}
