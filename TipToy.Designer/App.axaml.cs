using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TipToy.Designer.ViewModels;
using TipToy.Designer.Views;

namespace TipToy.Designer;

public partial class App : Application
{
    /// <summary>Erzeugt die Dateidialoge für die Oberfläche; Standard sind die des Betriebssystems.</summary>
    public static Func<TopLevel, IFileDialogs> CreateDialogs { get; set; } = topLevel => new StorageFileDialogs(topLevel);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var vm = new MainWindowViewModel();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Args is { Length: > 0 } args)
                vm.Load(args[0]);
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
        }
        base.OnFrameworkInitializationCompleted();
    }
}
