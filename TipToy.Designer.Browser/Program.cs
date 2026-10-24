using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using TipToy.Designer.Gme;
using TipToy.Designer.Services;
using TipToy.Designer.ViewModels;

[assembly: SupportedOSPlatform("browser")]

namespace TipToy.Designer;

static class Program
{
    static Task Main(string[] args)
    {
        BrowserFileDialogs.NewWorkDir();
        App.CreateDialogs = topLevel => new BrowserFileDialogs(topLevel);
        TextToSpeech.PrepareAsync = BrowserInterop.LoadSpeechAsync;
        TextToSpeech.Synthesizer = BrowserInterop.Speak;
        Audio.Device = new BrowserAudio();
        BrowserInterop.SetTitle(MainWindowViewModel.AppName);
        MainWindowViewModel.CanSaveToPen = BrowserInterop.CanPickSaveFile();

        // main.js übergibt die Adresse der Seite
        var pageUri = new Uri(args[0]);
        MainWindowViewModel.Examples = Examples.Create(pageUri);
        var query = System.Web.HttpUtility.ParseQueryString(pageUri.Query);
        App.StartExample = MainWindowViewModel.Examples.FirstOrDefault(e => e.Id == query["beispiel"]);

        return AppBuilder.Configure<App>()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }
}
