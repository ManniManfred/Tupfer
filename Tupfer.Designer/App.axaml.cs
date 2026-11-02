using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Tupfer.Designer.Views;

namespace Tupfer.Designer;

public partial class App : Application
{
    /// <summary>Erzeugt die Dateidialoge für die Oberfläche; Standard sind die des Betriebssystems.</summary>
    public static Func<TopLevel, IFileDialogs> CreateDialogs { get; set; } = topLevel => new StorageFileDialogs(topLevel);

    /// <summary>Beispielprojekt, das im Browser gleich beim Start geöffnet wird.</summary>
    public static ExampleProject? StartExample { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var vm = new MainWindowViewModel();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindowViewModel.AutoSaveStore ??= new FileAutoSaveStore();
            var window = new MainWindow { DataContext = vm };
            vm.Dialogs = CreateDialogs(window);
            desktop.MainWindow = window;
            // Mit Datei (z. B. Doppelklick auf ein Projekt): sie öffnen, sonst den zuletzt gesicherten Stand
            var path = desktop.Args is { Length: > 0 } args ? args[0] : null;
            _ = vm.StartAsync(null, restore: path == null, path);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Browser: eine einzige Ansicht statt eines Fensters
            var view = new MainView { DataContext = vm };
            bool started = false;
            view.AttachedToVisualTree += (_, _) =>
            {
                vm.Dialogs ??= CreateDialogs(TopLevel.GetTopLevel(view)!);
                // Erst mit Dialogen starten: Beim Öffnen wird evtl. nach ungespeicherten Änderungen gefragt.
                // z. B. index.html?beispiel=tic-tac-toi, sonst der zuletzt gesicherte Stand
                if (!started)
                {
                    started = true;
                    _ = vm.StartAsync(StartExample, restore: true);
                }
            };
            singleView.MainView = view;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
