using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TipToy.Designer.ViewModels;

namespace TipToy.Designer.Views;

public partial class MainWindow : Window, IFileDialogs
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Dialogs = this;
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.LogText))
                        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
                };
            }
        };
        // Vor dem ScrollViewer abfangen, damit Strg+Mausrad nicht zusätzlich scrollt
        PageScroll.AddHandler(PointerWheelChangedEvent, OnPageWheel, RoutingStrategies.Tunnel);
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

    public async Task<string?> OpenAsync(string title, string filterName, params string[] patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(filterName) { Patterns = patterns } },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType(filterName) { Patterns = new[] { "*" + extension } } },
        });
        return file?.TryGetLocalPath();
    }
}
