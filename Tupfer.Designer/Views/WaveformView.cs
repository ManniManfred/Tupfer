using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Tupfer.Designer.Views;

/// <summary>
/// Zeigt eine Aufnahme als Wellenform (in der eingestellten Lautstärke). Anfang und Ende des Ausschnitts
/// (Anteile 0–1) lassen sich mit der Maus bzw. dem Finger ziehen: Es bewegt sich die Kante, die dem
/// Druckpunkt am nächsten ist. Was außerhalb liegt, ist blass, übersteuerte Stellen sind rot;
/// beim Anhören zeigt eine orange Linie die Stelle, die gerade erklingt.
/// </summary>
public class WaveformView : Control
{
    public static readonly StyledProperty<float[]?> SamplesProperty =
        AvaloniaProperty.Register<WaveformView, float[]?>(nameof(Samples));

    public static readonly StyledProperty<double> StartProperty =
        AvaloniaProperty.Register<WaveformView, double>(nameof(Start), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> EndProperty =
        AvaloniaProperty.Register<WaveformView, double>(nameof(End), 1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> GainProperty =
        AvaloniaProperty.Register<WaveformView, double>(nameof(Gain), 1);

    /// <summary>Stelle der laufenden Wiedergabe (Anteil); negativ = keine.</summary>
    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<WaveformView, double>(nameof(Position), -1);

    static WaveformView()
    {
        AffectsRender<WaveformView>(SamplesProperty, StartProperty, EndProperty, GainProperty, PositionProperty);
    }

    public WaveformView()
    {
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        ClipToBounds = true;
        // Farben kommen aus dem Theme (App.axaml): bei Hell/Dunkel-Wechsel neu zeichnen
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public float[]? Samples { get => GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public double Start { get => GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public double End { get => GetValue(EndProperty); set => SetValue(EndProperty, value); }
    public double Gain { get => GetValue(GainProperty); set => SetValue(GainProperty, value); }
    public double Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    /// <summary>Lautester Wert je Bildspalte, für die aktuelle Breite berechnet.</summary>
    float[]? _columns;
    float[]? _columnsOf;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SamplesProperty || change.Property == BoundsProperty)
            _columns = null;
    }

    float[] Columns(int width)
    {
        if (_columns is { } c && c.Length == width && _columnsOf == Samples)
            return c;
        var samples = Samples ?? [];
        c = new float[width];
        for (int x = 0; x < width; x++)
        {
            int from = (int)((long)samples.Length * x / width), to = (int)((long)samples.Length * (x + 1) / width);
            float peak = 0;
            for (int i = from; i < to; i++)
                peak = Math.Max(peak, Math.Abs(samples[i]));
            c[x] = peak;
        }
        _columnsOf = Samples;
        return _columns = c;
    }

    /// <summary>Farbe aus den Theme-Ressourcen (App.axaml, Hell bzw. Dunkel).</summary>
    IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        IBrush background = Brush("WaveformBackground"), selected = Brush("WaveformSelected"),
            outside = Brush("WaveformOutside"), clipped = Brush("WaveformClipped"), handle = Brush("WaveformEdge"),
            playheadBrush = Brush("WaveformPlayhead");
        IPen axis = new Pen(Brush("WaveformAxis"), 1), edge = new Pen(handle, 2), playhead = new Pen(playheadBrush, 2);
        context.FillRectangle(background, bounds);
        int width = (int)bounds.Width;
        if (width <= 0 || Samples is not { Length: > 0 })
            return;
        double mid = bounds.Height / 2, half = mid - 4;
        context.DrawLine(axis, new Point(0, mid), new Point(bounds.Width, mid));
        var columns = Columns(width);
        double start = Start * width, end = End * width;
        for (int x = 0; x < width; x++)
        {
            double value = columns[x] * Gain;
            bool inside = x + 0.5 >= start && x + 0.5 <= end;
            var brush = !inside ? outside : value > 1.001 ? clipped : selected;
            double h = Math.Max(0.5, Math.Min(value, 1) * half);
            context.FillRectangle(brush, new Rect(x, mid - h, 1, 2 * h));
        }
        foreach (var x in new[] { start, end })
        {
            context.DrawLine(edge, new Point(x, 0), new Point(x, bounds.Height));
            context.FillRectangle(handle, new Rect(x - 5, 0, 10, 10));
            context.FillRectangle(handle, new Rect(x - 5, bounds.Height - 10, 10, 10));
        }
        if (Position >= 0)
        {
            double x = Position * width;
            context.DrawLine(playhead, new Point(x, 0), new Point(x, bounds.Height));
            var arrow = new StreamGeometry();
            using (var g = arrow.Open())
            {
                g.BeginFigure(new Point(x - 6, 0), true);
                g.LineTo(new Point(x + 6, 0));
                g.LineTo(new Point(x, 8));
                g.EndFigure(true);
            }
            context.DrawGeometry(playheadBrush, null, arrow);
        }
    }

    /// <summary>Gezogene Kante: true = Ende, false = Anfang, null = keine.</summary>
    bool? _dragEnd;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Bounds.Width <= 0)
            return;
        double x = e.GetPosition(this).X / Bounds.Width;
        _dragEnd = x > End || (x >= Start && Math.Abs(x - End) < Math.Abs(x - Start));
        Move(x);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragEnd != null && Bounds.Width > 0)
            Move(e.GetPosition(this).X / Bounds.Width);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragEnd = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragEnd = null;
    }

    void Move(double x)
    {
        x = Math.Clamp(x, 0, 1);
        if (_dragEnd == true)
            End = x;
        else
            Start = x;
    }
}
