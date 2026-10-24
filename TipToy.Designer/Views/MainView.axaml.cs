using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TipToy.Designer.ViewModels;

namespace TipToy.Designer.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.LogText))
                        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
                };
            }
        };
        // Vor dem ScrollViewer abfangen, damit Strg+Mausrad nicht zusätzlich scrollt
        PageScroll.AddHandler(PointerWheelChangedEvent, OnPageWheel, RoutingStrategies.Tunnel);
        PageScroll.AddHandler(PointerPressedEvent, OnPanStart, RoutingStrategies.Tunnel);
        PageScroll.AddHandler(PointerMovedEvent, OnPanMove, RoutingStrategies.Tunnel);
        PageScroll.AddHandler(PointerReleasedEvent, OnPanEnd, RoutingStrategies.Tunnel);
        PageScroll.AddHandler(PointerCaptureLostEvent, (_, _) => EndPan());
    }

    static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);

    /// <summary>Mauszeiger und Bildlaufposition beim Beginn des Panens, null = es wird nicht gepant.</summary>
    (Point Pointer, Vector Offset)? _panStart;

    /// <summary>Mittlere Maustaste gedrückt halten verschiebt die Ansicht.</summary>
    void OnPanStart(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PageScroll).Properties.IsMiddleButtonPressed)
            return;
        _panStart = (e.GetPosition(PageScroll), PageScroll.Offset);
        PageScroll.Cursor = PanCursor;
        e.Pointer.Capture(PageScroll);
        e.Handled = true;
    }

    void OnPanMove(object? sender, PointerEventArgs e)
    {
        if (_panStart is not { } start)
            return;
        PageScroll.Offset = start.Offset - (e.GetPosition(PageScroll) - start.Pointer);
        e.Handled = true;
    }

    void OnPanEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (_panStart == null || e.InitialPressMouseButton != MouseButton.Middle)
            return;
        e.Pointer.Capture(null);
        EndPan();
        e.Handled = true;
    }

    void EndPan()
    {
        _panStart = null;
        PageScroll.Cursor = null;
    }

    /// <summary>Strg+Mausrad zoomt; der Punkt unter dem Mauszeiger bleibt dabei stehen.</summary>
    void OnPageWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || DataContext is not MainWindowViewModel vm || e.Delta.Y == 0)
            return;
        e.Handled = true;

        double oldZoom = vm.Zoom;
        double zoom = Math.Clamp(oldZoom * Math.Pow(1.25, e.Delta.Y), 0.2, 4);
        if (zoom == oldZoom)
            return;
        var onPage = e.GetPosition(PageView);
        var inView = e.GetPosition(PageScroll);
        vm.Zoom = zoom;

        PageScroll.UpdateLayout();
        if (PageScroll.Content is Visual content && PageView.TranslatePoint(default, content) is { } origin)
            PageScroll.Offset = new Vector(
                origin.X + onPage.X * zoom / oldZoom - inView.X,
                origin.Y + onPage.Y * zoom / oldZoom - inView.Y);
    }
}
