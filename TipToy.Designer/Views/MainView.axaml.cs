using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TipToy.Designer.ViewModels;

namespace TipToy.Designer.Views;

public partial class MainView : UserControl
{
    /// <summary>Unter dieser Breite (Handy) wird das kompakte Layout verwendet.</summary>
    const double CompactWidth = 760;

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
        PageScroll.AddHandler(PointerCaptureLostEvent, (_, e) => RemoveTouch(e.Pointer));

        FitButton.Click += (_, _) => FitPage();
        ShowCanvas.Click += (_, _) => ShowPagesPanel(CanvasPanel);
        ShowPages.Click += (_, _) => ShowPagesPanel(PagesPanel);
        ShowProperties.Click += (_, _) => ShowPagesPanel(PropertiesPanel);
    }

    // ---------- Kompaktes Layout ----------

    bool? _compact;

    /// <summary>Im kompakten Layout sichtbarer Bereich des Tabs „Seiten“.</summary>
    Control? _pagesPanel;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        SetCompact(e.NewSize.Width < CompactWidth);
    }

    /// <summary>
    /// Schmale Bildschirme: Kopfzeile nur mit Menü und GME-Knopf, im Tab „Seiten“ ist immer nur
    /// ein Bereich zu sehen (Umschalter darüber), die Spalten der übrigen Tabs stehen untereinander.
    /// </summary>
    void SetCompact(bool compact)
    {
        if (_compact == compact)
            return;
        bool first = _compact == null;
        _compact = compact;
        Classes.Set("compact", compact);

        AppTitle.IsVisible = !compact;
        CompactVersion.IsVisible = compact;
        HeaderFileButtons.IsVisible = !compact;
        GmeLabel.IsVisible = !compact;
        ZoomSlider.IsVisible = !compact;
        CompactBar.IsVisible = compact;
        RootGrid.RowDefinitions[3].Height = new GridLength(compact ? 80 : 140);

        StackColumns(ScriptsGrid, compact, "250,12,*,12,340", "180,8,*,0,0", hideLast: true);
        StackColumns(ProjectGrid, compact, "*,12,*", "Auto,12,Auto");
        ShowPagesPanel(_pagesPanel ?? CanvasPanel);

        // Auf dem Handy passt eine ganze Seite bei 100 % nicht auf den Bildschirm
        if (compact && first)
            Dispatcher.UIThread.Post(FitPage, DispatcherPriority.Background);
    }

    /// <summary>Spalten 0, 2, 4 … eines Grids im kompakten Layout als Zeilen untereinander.</summary>
    static void StackColumns(Grid grid, bool compact, string columns, string rows, bool hideLast = false)
    {
        grid.ColumnDefinitions = ColumnDefinitions.Parse(compact ? "*" : columns);
        grid.RowDefinitions = compact ? RowDefinitions.Parse(rows) : new RowDefinitions();
        var panels = grid.Children.Where(c => c.Classes.Contains("card")).ToList();
        for (int i = 0; i < panels.Count; i++)
        {
            Grid.SetColumn(panels[i], compact ? 0 : 2 * i);
            Grid.SetRow(panels[i], compact ? 2 * i : 0);
            panels[i].IsVisible = !(compact && hideLast && i == panels.Count - 1);
        }
    }

    void ShowPagesPanel(Control panel)
    {
        _pagesPanel = panel;
        bool compact = _compact == true;
        PagesGrid.ColumnDefinitions = ColumnDefinitions.Parse(compact ? "*" : "265,12,*,12,290");
        Control[] panels = [PagesPanel, CanvasPanel, PropertiesPanel];
        ToggleButton[] buttons = [ShowPages, ShowCanvas, ShowProperties];
        for (int i = 0; i < panels.Length; i++)
        {
            Grid.SetColumn(panels[i], compact ? 0 : 2 * i);
            panels[i].IsVisible = !compact || panels[i] == panel;
            buttons[i].IsChecked = panels[i] == panel;
        }
    }

    /// <summary>Zoomt so, dass die Seite in die Breite der Ansicht passt.</summary>
    void FitPage()
    {
        if (DataContext is not MainWindowViewModel { Project.SelectedPage: { } page } vm)
            return;
        double available = PageScroll.Bounds.Width - 48 - 8; // Rand um die Seite und Bildlaufleiste
        if (available > 0)
            vm.Zoom = Math.Clamp(available / (page.WidthMm * 96 / 25.4), 0.2, 4);
    }

    // ---------- Ansicht verschieben und zoomen ----------

    static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);

    /// <summary>Mauszeiger und Bildlaufposition beim Beginn des Panens, null = es wird nicht gepant.</summary>
    (Point Pointer, Vector Offset)? _panStart;

    /// <summary>Beim Verschieben aufliegende Finger (Position in PageScroll).</summary>
    readonly Dictionary<IPointer, Point> _touches = new();

    /// <summary>Beginn des Zoomens mit zwei Fingern: Abstand, Zoom und Seitenpunkt (bei Zoom 1) unter der Mitte.</summary>
    (double Distance, double Zoom, Point PageAt1)? _pinch;

    bool PanMode => PanToggle.IsChecked == true;

    /// <summary>
    /// Mittlere Maustaste gedrückt halten verschiebt die Ansicht, ebenso Ziehen mit Maus oder Finger,
    /// wenn „Ansicht verschieben“ an ist. Dann zoomen zwei Finger.
    /// </summary>
    void OnPanStart(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(PageScroll).Properties;
        if (!props.IsMiddleButtonPressed && !(PanMode && props.IsLeftButtonPressed))
            return;
        var pos = e.GetPosition(PageScroll);
        _touches[e.Pointer] = pos;
        e.Pointer.Capture(PageScroll);
        e.Handled = true;
        if (_touches.Count == 2)
            StartPinch();
        else if (_touches.Count == 1)
        {
            _panStart = (pos, PageScroll.Offset);
            PageScroll.Cursor = PanCursor;
        }
    }

    void OnPanMove(object? sender, PointerEventArgs e)
    {
        if (!_touches.ContainsKey(e.Pointer))
            return;
        var pos = e.GetPosition(PageScroll);
        _touches[e.Pointer] = pos;
        e.Handled = true;
        if (_pinch != null)
            UpdatePinch();
        else if (_panStart is { } start)
            PageScroll.Offset = start.Offset - (pos - start.Pointer);
    }

    void OnPanEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (!_touches.ContainsKey(e.Pointer))
            return;
        e.Pointer.Capture(null);
        RemoveTouch(e.Pointer);
        e.Handled = true;
    }

    void RemoveTouch(IPointer pointer)
    {
        if (!_touches.Remove(pointer))
            return;
        _pinch = null;
        if (_touches.Count == 1)
        {
            // Mit dem verbliebenen Finger weiterschieben
            _panStart = (_touches.Values.First(), PageScroll.Offset);
            return;
        }
        _touches.Clear();
        _panStart = null;
        PageScroll.Cursor = null;
    }

    (Point A, Point B) TwoTouches()
    {
        var points = _touches.Values.Take(2).ToArray();
        return (points[0], points[1]);
    }

    void StartPinch()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        var (a, b) = TwoTouches();
        var mid = (a + b) / 2;
        if (PageScroll.TranslatePoint(mid, PageView) is not { } onPage)
            return;
        _panStart = null;
        _pinch = (Math.Max(1, Distance(a, b)), vm.Zoom, onPage / vm.Zoom);
    }

    void UpdatePinch()
    {
        if (_pinch is not { } pinch || DataContext is not MainWindowViewModel vm)
            return;
        var (a, b) = TwoTouches();
        var mid = (a + b) / 2;
        vm.Zoom = Math.Clamp(pinch.Zoom * Distance(a, b) / pinch.Distance, 0.2, 4);
        // Der Seitenpunkt unter der Fingermitte bleibt unter der Fingermitte
        PageScroll.UpdateLayout();
        if (PageScroll.Content is Visual content && PageView.TranslatePoint(default, content) is { } origin)
            PageScroll.Offset = new Vector(origin.X + pinch.PageAt1.X * vm.Zoom - mid.X, origin.Y + pinch.PageAt1.Y * vm.Zoom - mid.Y);
    }

    static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

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
