using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TipToy.Designer.Model;
using TipToy.Designer.Services;
using TipToy.Designer.ViewModels;

namespace TipToy.Designer.Views;

/// <summary>
/// Zeichenfläche einer Seite: zeigt Bilder, Texte und OID-Flächen.
/// Ziehen auf freier Fläche legt eine neue OID-Fläche an, Ziehen eines Objekts verschiebt es.
/// Bilder und Texte haben Anfasser zum Skalieren (bzw. Zuschneiden) und einen Drehgriff.
/// Im Modus „Nur OID-Flächen“ sind Bilder und Texte gesperrt.
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

    const double PxPerMmAt100 = 96 / 25.4;
    const double HandleSize = 10;
    const double RotateHandleDistance = 24;
    const double Grid = 0.5;
    const double MinSizeMm = 3;

    enum DragMode { None, Create, Move, Resize, MoveElement, ScaleElement, RotateElement }

    DragMode _mode;
    Point _startMm;
    AreaEntry? _startRect;
    ElementEntry? _startElement;

    /// <summary>Gezogener Anfasser: -1/0/1 je Richtung, (0, -2) = Drehgriff.</summary>
    (int X, int Y) _handle;

    PageViewModel? _subscribed;

    static readonly (int X, int Y)[] Handles =
        { (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) };
    static readonly (int X, int Y) RotateHandle = (0, -2);

    static readonly IBrush AreaFill = new SolidColorBrush(Color.FromArgb(70, 30, 120, 255));
    static readonly IBrush AreaFillInvalid = new SolidColorBrush(Color.FromArgb(90, 230, 40, 40));
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

    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    static PageCanvas()
    {
        AffectsMeasure<PageCanvas>(PageProperty, ZoomProperty);
        AffectsRender<PageCanvas>(PageProperty, ZoomProperty, AreasOnlyProperty, CropModeProperty);
        FocusableProperty.OverrideDefaultValue<PageCanvas>(true);
        DragDrop.AllowDropProperty.OverrideDefaultValue<PageCanvas>(true);
    }

    public PageCanvas()
    {
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
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

    double Scale => PxPerMmAt100 * Zoom;

    PageHistory? History => Page?.Project.History;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
            Subscribe(change.GetNewValue<PageViewModel?>());
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
                ctx.FillRectangle(Brushes.DarkOrange, AreaHandleRect(r));
        }

        if (!ElementsLocked && page.SelectedElement is { } sel)
            DrawSelection(ctx, sel);
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
            if (Math.Abs(d.X) <= HandleSize / 2 + 2 && Math.Abs(d.Y) <= HandleSize / 2 + 2)
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

        Focus();
        var pos = e.GetPosition(this);
        _startMm = ToMm(pos);
        History?.BeginChange();

        if (!ElementsLocked && page.SelectedElement is { } selElement && HitHandle(selElement, pos) is { } handle)
        {
            _handle = handle;
            _mode = handle == RotateHandle ? DragMode.RotateElement : DragMode.ScaleElement;
            _startElement = selElement.ToEntry(null);
        }
        else if (page.SelectedArea is { } sel && AreaHandleRect(ToPx(sel)).Inflate(3).Contains(pos))
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
        else if (!ElementsLocked && page.Elements.LastOrDefault(el => el.Contains(_startMm, 1 / Scale)) is { } hitElement)
        {
            page.SelectedElement = hitElement;
            _mode = DragMode.MoveElement;
            _startElement = hitElement.ToEntry(null);
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
        if (Page is not { } page)
            return;

        var px = e.GetPosition(this);
        var raw = ToMm(px);
        double maxX = page.WidthMm, maxY = page.HeightMm;
        var pos = new Point(Math.Clamp(raw.X, 0, maxX), Math.Clamp(raw.Y, 0, maxY));
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (_mode)
        {
            case DragMode.Create when page.SelectedArea is { } area:
                area.X = Snap(Math.Min(_startMm.X, pos.X));
                area.Y = Snap(Math.Min(_startMm.Y, pos.Y));
                area.Width = Snap(Math.Abs(pos.X - _startMm.X));
                area.Height = Snap(Math.Abs(pos.Y - _startMm.Y));
                break;
            case DragMode.Move when _startRect != null && page.SelectedArea is { } area:
                area.X = Snap(Math.Clamp(_startRect.X + pos.X - _startMm.X, 0, maxX - area.Width));
                area.Y = Snap(Math.Clamp(_startRect.Y + pos.Y - _startMm.Y, 0, maxY - area.Height));
                break;
            case DragMode.Resize when _startRect != null && page.SelectedArea is { } area:
                area.Width = Snap(Math.Max(MinSizeMm, _startRect.Width + pos.X - _startMm.X));
                area.Height = Snap(Math.Max(MinSizeMm, _startRect.Height + pos.Y - _startMm.Y));
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
                Cursor = HoverCursor(page, px);
                break;
        }
    }

    Cursor? HoverCursor(PageViewModel page, Point px)
    {
        if (!ElementsLocked && page.SelectedElement is { } el && HitHandle(el, px) is { } h)
        {
            if (h == RotateHandle)
                return HandCursor;
            // Richtung des Anfassers einschließlich Drehung, auf 45° gerundet
            double angle = Math.Atan2(h.Y, h.X) * 180 / Math.PI + el.Rotation;
            int sector = ((int)Math.Round(angle / 45) % 4 + 4) % 4;
            return ResizeCursors[sector];
        }
        if (page.SelectedArea is { } area && AreaHandleRect(ToPx(area)).Inflate(3).Contains(px))
            return CornerCursor;
        if (!ElementsLocked && page.SelectedElement is { } sel && sel.Contains(ToMm(px)))
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
        if (_mode == DragMode.None)
            return;
        if (_mode == DragMode.Create && Page is { SelectedArea: { } area } page
            && (area.Width < MinSizeMm || area.Height < MinSizeMm))
        {
            // Nur geklickt: keine Fläche anlegen, Auswahl aufheben
            page.Areas.Remove(area);
            page.SelectedArea = null;
        }
        _mode = DragMode.None;
        _startRect = null;
        _startElement = null;
        e.Pointer.Capture(null);
        History?.EndChange();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_mode == DragMode.None)
            return;
        _mode = DragMode.None;
        History?.EndChange();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Page is not { } page)
            return;

        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 0.5;
        if (page.SelectedArea is { } area)
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
