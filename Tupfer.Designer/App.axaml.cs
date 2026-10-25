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
            bool hasArgs = desktop.Args is { Length: > 0 };
            if (hasArgs)
                vm.Load(desktop.Args![0]);
            MainWindowViewModel.AutoSaveStore ??= new FileAutoSaveStore();
            _ = vm.StartAsync(null, restore: !hasArgs);
            var window = new MainWindow { DataContext = vm };
            vm.Dialogs = CreateDialogs(window);
            desktop.MainWindow = window;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Browser: eine einzige Ansicht statt eines Fensters
            var view = new MainView { DataContext = vm };
            view.AttachedToVisualTree += (_, _) => vm.Dialogs ??= CreateDialogs(TopLevel.GetTopLevel(view)!);
            singleView.MainView = view;
            // z. B. index.html?beispiel=tic-tac-toi, sonst der zuletzt gesicherte Stand
            _ = vm.StartAsync(StartExample, restore: true);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
