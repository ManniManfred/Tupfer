using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Windows.Input;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer.Views;

/// <summary>
/// Zeichenfläche einer Seite: zeigt Bilder, Texte und OID-Flächen.
/// Ziehen auf freier Fläche legt eine neue OID-Fläche an (Rechteck oder Ellipse, je nach
/// <see cref="AreaTool"/>; ohne Werkzeug hebt es nur die Auswahl auf); Polygone werden Punkt für Punkt geklickt. Ziehen eines Objekts verschiebt es.
/// Bei einem gewählten Polygon lassen sich Ecken verschieben und Kanten zu Kreisbögen ziehen.
/// Bilder und Texte haben Anfasser zum Skalieren (bzw. Zuschneiden) und einen Drehgriff.
/// Im Testmodus wird nicht bearbeitet: Antippen einer Fläche führt <see cref="TestTapCommand"/> aus.
/// Im Modus „Nur OID-Flächen“ sind Bilder und Texte gesperrt.
/// Strg- bzw. Umschalt-Klick (oder Klick bei <see cref="MultiSelect"/>) nimmt Objekte in die Auswahl auf
/// oder entfernt sie; mehrere gewählte Objekte werden gemeinsam verschoben.
/// </summary>
public class PageCanvas : Control
{
    public static readonly StyledProperty<PageViewModel?> PageProperty =
        AvaloniaProperty.Register<PageCanvas, PageViewModel?>(nameof(Page));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(Zoom), 1.0);

    /// <summary>Nur OID-Flächen bearbeiten, Bilder und Texte sind gesperrt.</summary>
    public static readonly StyledProperty<bool> AreasOnlyProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(AreasOnly), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Die Anfasser eines Bilds schneiden zu statt zu skalieren.</summary>
    public static readonly StyledProperty<bool> CropModeProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(CropMode));

    /// <summary>Form neuer OID-Flächen.</summary>
    public static readonly StyledProperty<AreaShape?> AreaToolProperty =
        AvaloniaProperty.Register<PageCanvas, AreaShape?>(nameof(AreaTool));

    /// <summary>Testmodus: Antippen führt die Fläche aus, statt zu bearbeiten.</summary>
    public static readonly StyledProperty<bool> TestModeProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(TestMode));

    /// <summary>Klicken nimmt Objekte in die Auswahl auf bzw. entfernt sie (wie Strg-Klick, für Touch).</summary>
    public static readonly StyledProperty<bool> MultiSelectProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(MultiSelect));

    /// <summary>Wird im Testmodus mit der angetippten Fläche ausgeführt.</summary>
    public static readonly StyledProperty<ICommand?> TestTapCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(TestTapCommand));

    const double PxPerMmAt100 = 96 / 25.4;
    const double HandleSize = 10;
    const double RotateHandleDistance = 24;
    const double Grid = 0.5;
    const double MinSizeMm = 3;

    enum DragMode { None, Create, Move, Resize, MoveElement, ScaleElement, RotateElement, MoveVertex, BendEdge, MoveSelection }

    /// <summary>Unter dieser Stichhöhe (mm) wird eine gebogene Kante wieder gerade.</summary>
    const double StraightSnapMm = 0.3;

    DragMode _mode;

    /// <summary>
    /// Maus bzw. Finger, mit dem gerade gezogen wird. Weitere Finger werden solange ignoriert –
    /// sonst begänne jeder einen eigenen Rückgängig-Schritt, und der Verlauf bliebe offen hängen.
    /// </summary>
    IPointer? _pointer;

    /// <summary>Gezogene Ecke bzw. Kante (von dieser Ecke zur nächsten) eines Polygons.</summary>
    int _vertex;

    /// <summary>Ecken des Polygons, das gerade geklickt wird (mm), null = es wird keins gezeichnet.</summary>
    List<Point>? _drawing;
    Point _hoverMm;
    Point _startMm;
    AreaEntry? _startRect;
    ElementEntry? _startElement;

    /// <summary>Lage der gewählten Objekte zu Beginn des gemeinsamen Verschiebens.</summary>
    readonly Dictionary<object, Point> _startPositions = new();

    /// <summary>Beim gemeinsamen Verschieben angeklicktes Objekt und ob es tatsächlich bewegt wurde.</summary>
    object? _clicked;
    bool _toggleClicked;
    bool _moved;

    static readonly IPen GroupPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 110, 0)), 1, new DashStyle(new double[] { 4, 3 }, 0));

    /// <summary>Gezogener Anfasser: -1/0/1 je Richtung, (0, -2) = Drehgriff.</summary>
    (int X, int Y) _handle;

    PageViewModel? _subscribed;

    static readonly (int X, int Y)[] Handles =
        { (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) };
    static readonly (int X, int Y) RotateHandle = (0, -2);

    static readonly IBrush AreaFill = new SolidColorBrush(Color.FromArgb(70, 30, 120, 255));
    static readonly IBrush AreaFillInvalid = new SolidColorBrush(Color.FromArgb(90, 230, 40, 40));
    static readonly IBrush TappedFill = new SolidColorBrush(Color.FromArgb(140, 40, 200, 80));
    static readonly IBrush SelectedFill = new SolidColorBrush(Color.FromArgb(110, 255, 160, 0));
    static readonly IPen AreaPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 90, 200)), 1);
    static readonly IPen SelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 110, 0)), 2);
    static readonly IPen ElementPen = new Pen(new SolidColorBrush(Color.FromRgb(232, 89, 12)), 1.5);
    static readonly IPen CropPen = new Pen(Brushes.Black, 1.5, new DashStyle(new double[] { 3, 2 }, 0));
    static readonly IPen EmptyTextPen = new Pen(Brushes.Gray, 1, new DashStyle(new double[] { 3, 3 }, 0));
    static readonly IPen HandlePen = new Pen(new SolidColorBrush(Color.FromRgb(232, 89, 12)), 1.5);
    static readonly IPen PageBorder = new Pen(Brushes.Gray, 1);
    static readonly IBrush LabelBackground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));

    static readonly Cursor[] ResizeCursors =
    {
        new(StandardCursorType.SizeWestEast), new(StandardCursorType.BottomRightCorner),
        new(StandardCursorType.SizeNorthSouth), new(StandardCursorType.BottomLeftCorner),
    };
    static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
    static readonly Cursor CornerCursor = new(StandardCursorType.BottomRightCorner);
    static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
    static readonly IPen DrawingPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 110, 0)), 1.5, new DashStyle(new double[] { 4, 2 }, 0));

    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    static PageCanvas()
    {
        AffectsMeasure<PageCanvas>(PageProperty, ZoomProperty);
        AffectsRender<PageCanvas>(PageProperty, ZoomProperty, AreasOnlyProperty, CropModeProperty, AreaToolProperty, TestModeProperty);
        FocusableProperty.OverrideDefaultValue<PageCanvas>(true);
        DragDrop.AllowDropProperty.OverrideDefaultValue<PageCanvas>(true);
    }

    public PageCanvas()
    {
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        // Beim Fokussieren (Antippen) nicht zur Ecke der Seite springen
        AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);
    }

    public bool TestMode
    {
        get => GetValue(TestModeProperty);
        set => SetValue(TestModeProperty, value);
    }

    public bool MultiSelect
    {
        get => GetValue(MultiSelectProperty);
        set => SetValue(MultiSelectProperty, value);
    }

    public ICommand? TestTapCommand
    {
        get => GetValue(TestTapCommandProperty);
        set => SetValue(TestTapCommandProperty, value);
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

    public bool AreasOnly
    {
        get => GetValue(AreasOnlyProperty);
        set => SetValue(AreasOnlyProperty, value);
    }

    public bool CropMode
    {
        get => GetValue(CropModeProperty);
        set => SetValue(CropModeProperty, value);
    }

    public AreaShape? AreaTool
    {
        get => GetValue(AreaToolProperty);
        set => SetValue(AreaToolProperty, value);
    }

    double Scale => PxPerMmAt100 * Zoom;

    PageHistory? History => Page?.Project.History;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
            Subscribe(change.GetNewValue<PageViewModel?>());
        if (change.Property == PageProperty || change.Property == AreaToolProperty)
            _drawing = null;
    }

    void Subscribe(PageViewModel? page)
    {
        if (_subscribed != null)
            _subscribed.Changed -= OnPageChanged;
        _subscribed = page;
        if (page != null)
            page.Changed += OnPageChanged;
        InvalidateMeasure();
        InvalidateVisual();
    }

    void OnPageChanged(object sender, string? property)
    {
        if (sender == _subscribed && property is nameof(PageViewModel.WidthMm) or nameof(PageViewModel.HeightMm))
            InvalidateMeasure();
        // Neu gewähltes Bild oder neuen Text ins Bild holen (z. B. nach dem Einfügen)
        if (sender == _subscribed && property == nameof(PageViewModel.SelectedElement)
            && _mode == DragMode.None && _subscribed.SelectedElement is { } e)
            this.BringIntoView(LocalRectPx(e).Inflate(RotateHandleDistance + HandleSize));
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Page is { } p ? new Size(p.WidthMm * Scale + 1, p.HeightMm * Scale + 1) : default;

    Rect ToPx(AreaViewModel a) => new(a.X * Scale, a.Y * Scale, a.Width * Scale, a.Height * Scale);

    bool ElementsLocked => AreasOnly;

    bool Cropping(ElementViewModel e) => CropMode && e.IsImage;

    public override void Render(DrawingContext ctx)
    {
        if (Page is not { } page)
            return;

        var pageRect = new Rect(0, 0, page.WidthMm * Scale, page.HeightMm * Scale);
        ctx.FillRectangle(Brushes.White, pageRect);
        using (ctx.PushClip(pageRect))
        {
            foreach (var element in page.Elements)
            {
                ImageConverter.DrawElement(ctx, element, Scale);
                if (element.IsText && string.IsNullOrWhiteSpace(element.Text))
                    using (ctx.PushTransform(ImageConverter.RotationAround(ToPx(element.Center), element.Rotation)))
                        ctx.DrawRectangle(EmptyTextPen, LocalRectPx(element));
            }
        }
        ctx.DrawRectangle(PageBorder, pageRect);

        // Beim Zuschneiden das ganze Bild blass zeigen, damit man sieht, was sich aufdecken lässt
        if (!ElementsLocked && page.SelectedElement is { } crop && Cropping(crop) && crop.Image is { } bmp)
        {
            using (ctx.PushTransform(ImageConverter.RotationAround(ToPx(crop.Center), crop.Rotation)))
            using (ctx.PushOpacity(0.35))
            {
                var r = LocalRectPx(crop);
                double fw = r.Width / crop.CropWidth, fh = r.Height / crop.CropHeight;
                ctx.DrawImage(bmp, new Rect(r.X - crop.CropX * fw, r.Y - crop.CropY * fh, fw, fh));
            }
        }

        foreach (var area in page.Areas)
            DrawArea(ctx, area, !TestMode && page.Selection.Contains(area), area == page.SelectedArea && !TestMode);

        if (_drawing != null)
            DrawPolygonInProgress(ctx, _drawing);

        if (!ElementsLocked && page.SelectedElement is { } sel)
            DrawSelection(ctx, sel);

        if (page.IsMultiSelection && !TestMode)
            DrawMultiSelection(ctx, page);
    }

    /// <summary>Mehrere gewählte Objekte: Rahmen der Bilder und Texte und gestrichelt das Rechteck um alle.</summary>
    void DrawMultiSelection(DrawingContext ctx, PageViewModel page)
    {
        var items = page.Selection.Where(i => !ElementsLocked || i is AreaViewModel).ToList();
        foreach (var e in items.OfType<ElementViewModel>())
            using (ctx.PushTransform(ImageConverter.RotationAround(ToPx(e.Center), e.Rotation)))
                ctx.DrawRectangle(ElementPen, LocalRectPx(e));
        if (items.Count < 2)
            return;
        var b = items.Select(PageViewModel.BoundsOf).Aggregate((x, y) => x.Union(y));
        ctx.DrawRectangle(GroupPen, new Rect(b.X * Scale, b.Y * Scale, b.Width * Scale, b.Height * Scale).Inflate(3));
    }

    void DrawArea(DrawingContext ctx, AreaViewModel area, bool selected, bool handles)
    {
        var r = ToPx(area);
        var fill = area == _tapped ? TappedFill : selected ? SelectedFill : area.Oid == null ? AreaFillInvalid : AreaFill;
        var pen = selected ? SelectedPen : AreaPen;
        if (area.IsRectangle)
            ctx.DrawRectangle(fill, pen, r);
        else
            ctx.DrawGeometry(fill, pen, new PolylineGeometry(area.Outline().Select(p => new Point(p.X * Scale, p.Y * Scale)), true));

        var text = new FormattedText(area.Caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, Math.Clamp(11 * Zoom, 9, 16), Brushes.Black)
        {
            MaxTextWidth = Math.Max(10, r.Width - 4),
            MaxTextHeight = Math.Max(10, r.Height - 4),
            Trimming = TextTrimming.CharacterEllipsis,
        };
        // Bei Rechtecken oben links, sonst in der Mitte (die Ecken des Rahmens liegen außerhalb)
        var origin = area.IsRectangle
            ? new Point(r.X + 3, r.Y + 2)
            : new Point(r.Center.X - text.Width / 2, r.Center.Y - text.Height / 2);
        ctx.FillRectangle(LabelBackground, new Rect(origin.X - 2, origin.Y - 1, Math.Min(text.Width + 4, r.Width - 2), Math.Min(text.Height + 2, r.Height - 2)));
        ctx.DrawText(text, origin);

        if (!handles)
            return;
        ctx.FillRectangle(Brushes.DarkOrange, AreaHandleRect(r));
        if (area.IsPolygon)
        {
            var points = area.PagePoints.ToList();
            for (int i = 0; i < points.Count; i++)
            {
                ctx.DrawEllipse(Brushes.White, HandlePen, EdgeHandlePx(points, i), HandleSize / 2 - 1, HandleSize / 2 - 1);
                var v = ToPx(new Point(points[i].X, points[i].Y));
                ctx.DrawRectangle(Brushes.White, HandlePen, new Rect(v.X - HandleSize / 2, v.Y - HandleSize / 2, HandleSize, HandleSize));
            }
        }
    }

    void DrawPolygonInProgress(DrawingContext ctx, List<Point> points)
    {
        var px = points.Select(ToPx).Append(ToPx(_hoverMm)).ToList();
        ctx.DrawGeometry(AreaFill, DrawingPen, new PolylineGeometry(px, false));
        for (int i = 0; i < points.Count; i++)
        {
            var p = px[i];
            var size = i == 0 && points.Count >= 3 ? HandleSize + 4 : HandleSize;
            ctx.DrawRectangle(i == 0 ? Brushes.DarkOrange : Brushes.White, HandlePen, new Rect(p.X - size / 2, p.Y - size / 2, size, size));
        }
    }

    /// <summary>Anfasser einer Polygonkante (von Ecke i zur nächsten): der Scheitel des Bogens bzw. die Mitte.</summary>
    Point EdgeHandlePx(IReadOnlyList<AreaPoint> points, int i)
    {
        var p = points[i];
        var q = points[(i + 1) % points.Count];
        var (ax, ay) = AreaGeometry.Apex(p.X, p.Y, q.X, q.Y, p.Bulge);
        return ToPx(new Point(ax, ay));
    }

    /// <summary>Zusätzlicher Spielraum (Pixel) beim Treffen der Anfasser; mit dem Finger größer.</summary>
    double _slop = 2;

    bool NearPx(Point a, Point b) => Math.Abs(a.X - b.X) <= HandleSize / 2 + _slop && Math.Abs(a.Y - b.Y) <= HandleSize / 2 + _slop;

    /// <summary>Getroffene Ecke (Vertex = true) oder Kante eines Polygons.</summary>
    (int Index, bool Vertex)? HitPolygonHandle(AreaViewModel area, Point px)
    {
        var points = area.PagePoints.ToList();
        for (int i = 0; i < points.Count; i++)
            if (NearPx(ToPx(new Point(points[i].X, points[i].Y)), px))
                return (i, true);
        for (int i = 0; i < points.Count; i++)
            if (NearPx(EdgeHandlePx(points, i), px))
                return (i, false);
        return null;
    }

    void DrawSelection(DrawingContext ctx, ElementViewModel e)
    {
        bool crop = Cropping(e);
        using (ctx.PushTransform(ImageConverter.RotationAround(ToPx(e.Center), e.Rotation)))
        {
            var r = LocalRectPx(e);
            ctx.DrawRectangle(crop ? CropPen : ElementPen, r);
            var top = new Point(r.Center.X, r.Y);
            ctx.DrawLine(HandlePen, top, top - new Vector(0, RotateHandleDistance - HandleSize / 2));
            ctx.DrawEllipse(Brushes.White, HandlePen, top - new Vector(0, RotateHandleDistance), HandleSize / 2, HandleSize / 2);
            foreach (var h in Handles)
            {
                var c = new Point(r.X + (h.X + 1) / 2.0 * r.Width, r.Y + (h.Y + 1) / 2.0 * r.Height);
                var hr = new Rect(c.X - HandleSize / 2, c.Y - HandleSize / 2, HandleSize, HandleSize);
                if (crop)
                    ctx.FillRectangle(Brushes.Black, hr);
                else
                    ctx.DrawRectangle(Brushes.White, HandlePen, hr);
            }
        }
    }

    Point ToPx(Point mm) => new(mm.X * Scale, mm.Y * Scale);

    Rect LocalRectPx(ElementViewModel e) => new(e.X * Scale, e.Y * Scale, e.Width * Scale, e.Height * Scale);

    /// <summary>Position eines Anfassers in Bildschirmpixeln (gedreht).</summary>
    Point HandlePx(ElementViewModel e, (int X, int Y) h)
    {
        double ly = h == RotateHandle ? -RotateHandleDistance / Scale : (h.Y + 1) / 2.0 * e.Height;
        return ToPx(ElementViewModel.ToPage(e.ToEntry(null), new Point((h.X + 1) / 2.0 * e.Width, ly)));
    }

    (int X, int Y)? HitHandle(ElementViewModel e, Point px)
    {
        foreach (var h in Handles.Append(RotateHandle))
        {
            var d = HandlePx(e, h) - px;
            if (Math.Abs(d.X) <= HandleSize / 2 + _slop && Math.Abs(d.Y) <= HandleSize / 2 + _slop)
                return h;
        }
        return null;
    }

    static Rect AreaHandleRect(Rect r) => new(r.Right - HandleSize / 2, r.Bottom - HandleSize / 2, HandleSize, HandleSize);

    static double Snap(double v) => Math.Round(v / Grid) * Grid;

    Point ToMm(Point px) => new(px.X / Scale, px.Y / Scale);

    static bool IsImageFile(IStorageItem item) => item is IStorageFile && ImageExtensions.Contains(Path.GetExtension(item.Name));

    void OnDragOver(object? sender, DragEventArgs e)
    {
        bool images = Page != null && e.DataTransfer.TryGetFiles() is { } files && files.Any(IsImageFile);
        e.DragEffects = images ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Aus dem Explorer gezogene Bilder an der Mausposition einfügen (mehrere leicht versetzt).</summary>
    async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Page is not { } page || e.DataTransfer.TryGetFiles() is not { } files)
            return;
        var pos = ToMm(e.GetPosition(this));
        var paths = new List<string>();
        // Ohne lokalen Pfad (Browser) wird das Bild ins Arbeitsverzeichnis kopiert
        foreach (var file in files.Where(IsImageFile).Cast<IStorageFile>())
        {
            try
            {
                paths.Add(file.TryGetLocalPath() ?? await WorkFiles.CopyAsync(file, Environment.CurrentDirectory));
            }
            catch (IOException)
            {
                // Nicht lesbare Datei überspringen
            }
        }
        if (paths.Count == 0)
            return;
        double offset = 0;
        History?.Change(() =>
        {
            foreach (var path in paths)
                if (page.AddImage(path, pos + new Point(offset, offset)) != null)
                    offset += 5;
        });
        if (offset > 0)
        {
            AreasOnly = false;
            Focus();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Page is not { } page || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (TestMode)
        {
            TestTap(page, ToMm(e.GetPosition(this)));
            e.Handled = true;
            return;
        }

        if (_pointer != null)
        {
            // Zweiter Finger beim Aufziehen einer neuen Fläche: war nicht als Zeichnen gemeint
            if (_mode == DragMode.Create && page.SelectedArea is { } created)
            {
                page.Areas.Remove(created);
                page.SelectedArea = null;
                _mode = DragMode.None;
            }
            e.Handled = true;
            return;
        }

        Focus();
        _slop = e.Pointer.Type == PointerType.Touch ? 12 : 2;
        var pos = e.GetPosition(this);
        _startMm = ToMm(pos);

        if (_drawing != null)
        {
            ContinuePolygon(page, e.ClickCount);
            e.Handled = true;
            return;
        }

        History?.BeginChange();
        _pointer = e.Pointer;
        var mods = e.KeyModifiers;
        bool toggle = MultiSelect || mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Shift) || mods.HasFlag(KeyModifiers.Meta);
        var hitItem = HitItem(page, _startMm);

        if (hitItem != null && page.IsMultiSelection && page.Selection.Contains(hitItem))
        {
            // Mehrere gewählte Objekte gemeinsam verschieben; ein Klick ohne Ziehen wählt (bzw. entfernt) nur dieses
            _mode = DragMode.MoveSelection;
            _clicked = hitItem;
            _toggleClicked = toggle;
            _moved = false;
            _startPositions.Clear();
            foreach (var item in page.Selection.Where(i => !ElementsLocked || i is AreaViewModel))
                _startPositions[item] = PageViewModel.BoundsOf(item).TopLeft;
        }
        else if (page.SelectedArea is { IsPolygon: true } poly && HitPolygonHandle(poly, pos) is { } ph)
        {
            _vertex = ph.Index;
            if (e.ClickCount == 2)
            {
                // Doppelklick: Ecke löschen bzw. Kante in der Mitte teilen (ein dritter Klick zieht wieder)
                if (ph.Vertex)
                    RemoveVertex(poly, ph.Index);
                else
                    SplitEdge(poly, ph.Index);
                EndDrag();
                e.Handled = true;
                return;
            }
            _mode = ph.Vertex ? DragMode.MoveVertex : DragMode.BendEdge;
        }
        else if (!ElementsLocked && page.SelectedElement is { } selElement && HitHandle(selElement, pos) is { } handle)
        {
            _handle = handle;
            _mode = handle == RotateHandle ? DragMode.RotateElement : DragMode.ScaleElement;
            _startElement = selElement.ToEntry(null);
        }
        else if (page.SelectedArea is { } sel && AreaHandleRect(ToPx(sel)).Inflate(_slop + 1).Contains(pos))
        {
            _mode = DragMode.Resize;
            _startRect = sel.ToEntry();
        }
        else if (toggle && hitItem != null)
        {
            page.ToggleSelection(hitItem);
            EndDrag();
            e.Handled = true;
            return;
        }
        else if (MultiSelect)
        {
            // Im Auswahlmodus legt Ziehen auf freier Stelle keine Fläche an
            EndDrag();
            e.Handled = true;
            return;
        }
        else if (page.Areas.LastOrDefault(a => a.Contains(_startMm.X, _startMm.Y)) is { } hit)
        {
            page.SelectedArea = hit;
            _mode = DragMode.Move;
            _startRect = hit.ToEntry();
        }
        else if (!ElementsLocked && page.Elements.LastOrDefault(el => el.Contains(_startMm, 1 / Scale)) is { } hitElement)
        {
            page.SelectedElement = hitElement;
            _mode = DragMode.MoveElement;
            _startElement = hitElement.ToEntry(null);
        }
        else if (AreaTool is not { } tool)
        {
            // Kein Flächenwerkzeug: Klick auf freie Stelle hebt nur die Auswahl auf
            EndDrag();
            page.SelectedArea = null;
            page.SelectedElement = null;
            e.Handled = true;
            return;
        }
        else if (tool == AreaShape.Polygon)
        {
            // Erste Ecke eines neuen Polygons; die Fläche entsteht erst beim Abschließen
            EndDrag();
            page.SelectedArea = null;
            page.SelectedElement = null;
            _drawing = [SnapPoint(_startMm)];
            _hoverMm = _drawing[0];
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        else
        {
            var area = NewArea(page.Project);
            area.Shape = tool;
            area.X = Snap(_startMm.X);
            area.Y = Snap(_startMm.Y);
            page.Areas.Add(area);
            page.SelectedArea = area;
            _mode = DragMode.Create;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>Oberstes Objekt an der Stelle: Flächen liegen über Bildern und Texten.</summary>
    object? HitItem(PageViewModel page, Point mm) =>
        (object?)page.Areas.LastOrDefault(a => a.Contains(mm.X, mm.Y))
        ?? (ElementsLocked ? null : page.Elements.LastOrDefault(el => el.Contains(mm, 1 / Scale)));

    /// <summary>Im Testmodus angetippte Fläche (wird kurz hervorgehoben).</summary>
    AreaViewModel? _tapped;

    void TestTap(PageViewModel page, Point mm)
    {
        _drawing = null;
        // Wie beim Stift zählt die oberste Fläche an der Stelle
        if (page.Areas.LastOrDefault(a => a.Contains(mm.X, mm.Y)) is not { } area)
            return;
        _tapped = area;
        InvalidateVisual();
        DispatcherTimer.RunOnce(() =>
        {
            if (_tapped == area)
            {
                _tapped = null;
                InvalidateVisual();
            }
        }, TimeSpan.FromMilliseconds(300));
        if (TestTapCommand is { } command && command.CanExecute(area))
            command.Execute(area);
    }

    static AreaViewModel NewArea(ProjectViewModel project) => new(project)
    {
        Target = AreaTarget.Script,
        Script = project.SelectedScript?.Name ?? project.Scripts.FirstOrDefault()?.Name ?? "",
    };

    static Point SnapPoint(Point mm) => new(Snap(mm.X), Snap(mm.Y));

    /// <summary>Klick beim Zeichnen eines Polygons: Ecke hinzufügen oder abschließen.</summary>
    void ContinuePolygon(PageViewModel page, int clickCount)
    {
        var points = _drawing!;
        var p = SnapPoint(ClampToPage(page, _startMm));
        // Doppelklick oder Klick auf die erste Ecke schließt ab
        if (clickCount >= 2 || (points.Count >= 3 && NearPx(ToPx(points[0]), ToPx(_startMm))))
        {
            FinishPolygon(page);
            return;
        }
        if (p != points[^1])
            points.Add(p);
        InvalidateVisual();
    }

    void FinishPolygon(PageViewModel page)
    {
        var points = _drawing!.Distinct().ToList();
        _drawing = null;
        InvalidateVisual();
        if (points.Count < 3)
            return;
        var area = NewArea(page.Project);
        area.SetPolygon(points.Select(p => new AreaPoint(p.X, p.Y)).ToList());
        page.Areas.Add(area);
        page.SelectedArea = area;
    }

    static Point ClampToPage(PageViewModel page, Point mm) =>
        new(Math.Clamp(mm.X, 0, page.WidthMm), Math.Clamp(mm.Y, 0, page.HeightMm));

    /// <summary>Ersetzt eine Ecke des Polygons (Seitenkoordinaten).</summary>
    static void UpdatePoints(AreaViewModel area, Func<List<AreaPoint>, List<AreaPoint>> change) =>
        area.SetPolygon(change(area.PagePoints.ToList()));

    static void RemoveVertex(AreaViewModel area, int index)
    {
        if (area.Points.Count <= 3)
            return;
        UpdatePoints(area, points =>
        {
            points.RemoveAt(index);
            // Die Kante davor führt jetzt zu einer anderen Ecke: gerade machen
            int prev = (index - 1 + points.Count) % points.Count;
            points[prev] = points[prev] with { Bulge = 0 };
            return points;
        });
    }

    static void SplitEdge(AreaViewModel area, int index)
    {
        UpdatePoints(area, points =>
        {
            var p = points[index];
            var q = points[(index + 1) % points.Count];
            var (ax, ay) = AreaGeometry.Apex(p.X, p.Y, q.X, q.Y, p.Bulge);
            double half = AreaGeometry.HalfBulge(p.Bulge);
            points[index] = p with { Bulge = half };
            points.Insert(index + 1, new AreaPoint(Snap(ax), Snap(ay), half));
            return points;
        });
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Page is not { } page || (_pointer != null && e.Pointer != _pointer))
            return;

        var px = e.GetPosition(this);
        var raw = ToMm(px);
        double maxX = page.WidthMm, maxY = page.HeightMm;
        var pos = new Point(Math.Clamp(raw.X, 0, maxX), Math.Clamp(raw.Y, 0, maxY));
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (TestMode)
        {
            Cursor = page.Areas.Any(a => a.Contains(raw.X, raw.Y)) ? HandCursor : null;
            return;
        }

        if (_drawing != null)
        {
            _hoverMm = SnapPoint(pos);
            Cursor = CrossCursor;
            InvalidateVisual();
            return;
        }

        switch (_mode)
        {
            case DragMode.Create when page.SelectedArea is { } area:
                double w = Math.Abs(pos.X - _startMm.X), h = Math.Abs(pos.Y - _startMm.Y);
                // Umschalt: Kreis bzw. Quadrat
                if (shift)
                    w = h = Math.Max(w, h);
                area.X = Snap(pos.X < _startMm.X ? _startMm.X - w : _startMm.X);
                area.Y = Snap(pos.Y < _startMm.Y ? _startMm.Y - h : _startMm.Y);
                area.Width = Snap(w);
                area.Height = Snap(h);
                break;
            case DragMode.MoveVertex when page.SelectedArea is { IsPolygon: true } area:
                var v = SnapPoint(pos);
                UpdatePoints(area, points =>
                {
                    points[_vertex] = points[_vertex] with { X = v.X, Y = v.Y };
                    return points;
                });
                break;
            case DragMode.BendEdge when page.SelectedArea is { IsPolygon: true } area:
                UpdatePoints(area, points =>
                {
                    var p = points[_vertex];
                    var q = points[(_vertex + 1) % points.Count];
                    double bulge = AreaGeometry.BulgeThrough(p.X, p.Y, q.X, q.Y, raw.X, raw.Y);
                    double chord = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                    if (Math.Abs(bulge * chord / 2) < StraightSnapMm)
                        bulge = 0;
                    points[_vertex] = p with { Bulge = Math.Round(bulge, 4) };
                    return points;
                });
                break;
            case DragMode.Move when _startRect != null && page.SelectedArea is { } area:
                area.X = Snap(Math.Clamp(_startRect.X + pos.X - _startMm.X, 0, maxX - area.Width));
                area.Y = Snap(Math.Clamp(_startRect.Y + pos.Y - _startMm.Y, 0, maxY - area.Height));
                break;
            case DragMode.Resize when _startRect != null && page.SelectedArea is { } area:
                area.Width = Snap(Math.Max(MinSizeMm, _startRect.Width + pos.X - _startMm.X));
                area.Height = Snap(Math.Max(MinSizeMm, _startRect.Height + pos.Y - _startMm.Y));
                break;
            case DragMode.MoveSelection:
                var delta = page.ClampMove(_startPositions.Keys,
                    new Vector(Snap(raw.X - _startMm.X), Snap(raw.Y - _startMm.Y)));
                if (delta != default)
                    _moved = true;
                foreach (var (item, start) in _startPositions)
                {
                    var b = PageViewModel.BoundsOf(item);
                    PageViewModel.MoveBy(item, start.X + delta.X - b.X, start.Y + delta.Y - b.Y);
                }
                break;
            case DragMode.MoveElement when _startElement != null && page.SelectedElement is { } el:
                // Bilder dürfen über den Seitenrand hinausragen
                el.X = Snap(_startElement.X + raw.X - _startMm.X);
                el.Y = Snap(_startElement.Y + raw.Y - _startMm.Y);
                break;
            case DragMode.ScaleElement when _startElement != null && page.SelectedElement is { } el:
                // Bilder an den Ecken proportional skalieren, Texte frei; Umschalt kehrt das um
                ResizeElement(el, raw, Cropping(el), keepAspect: el.IsImage != shift);
                break;
            case DragMode.RotateElement when _startElement != null && page.SelectedElement is { } el:
                var c = el.Center;
                double angle = Math.Atan2(raw.Y - c.Y, raw.X - c.X) * 180 / Math.PI + 90;
                el.Rotation = shift ? Math.Round(angle / 15) * 15 : Math.Round(angle);
                break;
            case DragMode.None:
                _slop = e.Pointer.Type == PointerType.Touch ? 12 : 2;
                Cursor = HoverCursor(page, px);
                break;
        }
    }

    Cursor? HoverCursor(PageViewModel page, Point px)
    {
        if (page.SelectedArea is { IsPolygon: true } poly && HitPolygonHandle(poly, px) is { } ph)
            return ph.Vertex ? MoveCursor : HandCursor;
        if (!ElementsLocked && page.SelectedElement is { } el && HitHandle(el, px) is { } h)
        {
            if (h == RotateHandle)
                return HandCursor;
            // Richtung des Anfassers einschließlich Drehung, auf 45° gerundet
            double angle = Math.Atan2(h.Y, h.X) * 180 / Math.PI + el.Rotation;
            int sector = ((int)Math.Round(angle / 45) % 4 + 4) % 4;
            return ResizeCursors[sector];
        }
        if (page.SelectedArea is { } area && AreaHandleRect(ToPx(area)).Inflate(_slop + 1).Contains(px))
            return CornerCursor;
        if (!ElementsLocked && page.SelectedElement is { } sel && sel.Contains(ToMm(px)))
            return MoveCursor;
        if (page.IsMultiSelection && HitItem(page, ToMm(px)) is { } item && page.Selection.Contains(item))
            return MoveCursor;
        return Cursor.Default;
    }

    /// <summary>
    /// Ändert den Rahmen beim Ziehen eines Anfassers. Gerechnet wird im ungedrehten Rahmen vom
    /// Beginn des Ziehens, die gegenüberliegende Seite bleibt fest. Beim Zuschneiden wandert der
    /// Ausschnitt mit, das Bild selbst bleibt an seiner Stelle.
    /// </summary>
    void ResizeElement(ElementViewModel el, Point posMm, bool crop, bool keepAspect)
    {
        var s = _startElement!;
        var p = ElementViewModel.ToLocal(s, posMm);
        double l = 0, t = 0, r = s.Width, b = s.Height;
        if (_handle.X < 0) l = p.X; else if (_handle.X > 0) r = p.X;
        if (_handle.Y < 0) t = p.Y; else if (_handle.Y > 0) b = p.Y;

        if (crop)
        {
            // Nicht über den Bildrand hinaus aufdecken
            double fw = s.Width / s.CropWidth, fh = s.Height / s.CropHeight;
            l = Math.Max(l, -s.CropX * fw);
            t = Math.Max(t, -s.CropY * fh);
            r = Math.Min(r, s.Width + (1 - s.CropX - s.CropWidth) * fw);
            b = Math.Min(b, s.Height + (1 - s.CropY - s.CropHeight) * fh);
        }

        if (_handle.X < 0) l = Math.Min(l, r - MinSizeMm); else if (_handle.X > 0) r = Math.Max(r, l + MinSizeMm);
        if (_handle.Y < 0) t = Math.Min(t, b - MinSizeMm); else if (_handle.Y > 0) b = Math.Max(b, t + MinSizeMm);

        if (!crop && keepAspect && _handle.X != 0 && _handle.Y != 0)
        {
            double f = Math.Max((r - l) / s.Width, (b - t) / s.Height);
            double w = s.Width * f, h = s.Height * f;
            if (_handle.X < 0) l = r - w; else r = l + w;
            if (_handle.Y < 0) t = b - h; else b = t + h;
        }

        double nw = r - l, nh = b - t;
        var center = ElementViewModel.ToPage(s, new Point((l + r) / 2, (t + b) / 2));
        if (crop)
        {
            el.CropX = s.CropX + l / s.Width * s.CropWidth;
            el.CropY = s.CropY + t / s.Height * s.CropHeight;
            el.CropWidth = nw / s.Width * s.CropWidth;
            el.CropHeight = nh / s.Height * s.CropHeight;
        }
        el.Width = Math.Round(nw, 2);
        el.Height = Math.Round(nh, 2);
        el.X = Math.Round(center.X - nw / 2, 2);
        el.Y = Math.Round(center.Y - nh / 2, 2);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer != _pointer)
            return;
        if (_mode == DragMode.Create && Page is { SelectedArea: { } area } page
            && (area.Width < MinSizeMm || area.Height < MinSizeMm))
        {
            // Nur geklickt: keine Fläche anlegen, Auswahl aufheben
            page.Areas.Remove(area);
            page.SelectedArea = null;
        }
        // Nach dem Skalieren den Rahmen eines Polygons wieder eng an den Umriss legen (Bögen)
        if (_mode == DragMode.Resize && Page?.SelectedArea is { IsPolygon: true } scaled)
            UpdatePoints(scaled, points => points);
        if (_mode == DragMode.MoveSelection && !_moved && _clicked != null && Page is { } p)
        {
            if (_toggleClicked)
                p.ToggleSelection(_clicked);
            else
                p.Select([_clicked]);
        }
        _startPositions.Clear();
        _clicked = null;
        _startRect = null;
        _startElement = null;
        EndDrag();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (e.Pointer == _pointer)
            EndDrag();
    }

    /// <summary>Beendet das Ziehen und schließt den Rückgängig-Schritt (genau einmal je Drücken).</summary>
    void EndDrag()
    {
        if (_pointer == null)
            return;
        _pointer = null;
        _mode = DragMode.None;
        History?.EndChange();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Page is not { } page || TestMode)
            return;

        if (_drawing != null)
        {
            switch (e.Key)
            {
                case Key.Enter: FinishPolygon(page); break;
                case Key.Escape: _drawing = null; InvalidateVisual(); break;
                case Key.Back when _drawing.Count > 1: _drawing.RemoveAt(_drawing.Count - 1); InvalidateVisual(); break;
                case Key.Back: _drawing = null; InvalidateVisual(); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            page.SelectAll(!ElementsLocked);
            e.Handled = true;
            return;
        }

        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 0.5;
        if (page.IsMultiSelection)
        {
            var items = page.Selection.Where(i => !ElementsLocked || i is AreaViewModel).ToList();
            Vector? move = e.Key switch
            {
                Key.Left => new Vector(-step, 0),
                Key.Right => new Vector(step, 0),
                Key.Up => new Vector(0, -step),
                Key.Down => new Vector(0, step),
                _ => null,
            };
            switch (e.Key)
            {
                case Key.Delete:
                case Key.Back:
                    History?.Change(page.DeleteSelection);
                    break;
                case Key.Escape: page.Select([]); break;
                case var _ when move is { } m:
                    var d = page.ClampMove(items, m);
                    History?.Change(() => items.ForEach(i => PageViewModel.MoveBy(i, d.X, d.Y)));
                    break;
                default: return;
            }
        }
        else if (page.SelectedArea is { } area)
        {
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
        }
        else if (!ElementsLocked && page.SelectedElement is { } el)
        {
            switch (e.Key)
            {
                case Key.Delete:
                case Key.Back:
                    page.Elements.Remove(el);
                    page.SelectedElement = null;
                    break;
                case Key.Left: el.X -= step; break;
                case Key.Right: el.X += step; break;
                case Key.Up: el.Y -= step; break;
                case Key.Down: el.Y += step; break;
                case Key.Escape: page.SelectedElement = null; break;
                default: return;
            }
        }
        else
        {
            return;
        }
        e.Handled = true;
    }
}
