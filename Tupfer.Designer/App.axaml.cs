using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Desktop: immer mit einer Projektdatei arbeiten, mit Liste der zuletzt geöffneten
            var vm = new MainWindowViewModel { RequireProjectFile = true, Recent = new RecentFiles() };
            MainWindowViewModel.AutoSaveStore ??= new FileAutoSaveStore();
            var window = new MainWindow { DataContext = vm };
            vm.Dialogs = CreateDialogs(window);
            desktop.MainWindow = window;
            // Mit Datei (z. B. Doppelklick auf ein Projekt): sie öffnen, sonst den zuletzt gesicherten Stand
            var path = desktop.Args is { Length: > 0 } args ? args[0] : null;
            _ = vm.StartAsync(null, restore: path == null, path);
            // macOS übergibt per Doppelklick geöffnete Dateien nicht als Argument, sondern als Ereignis
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                activatable.Activated += async (_, e) =>
                {
                    if (e is FileActivatedEventArgs { Files: [var file, ..] } && file.TryGetLocalPath() is { } local)
                        await vm.OpenFromSystemAsync(local);
                };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Browser: eine einzige Ansicht statt eines Fensters
            var vm = new MainWindowViewModel();
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
