using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using Tupfer.Designer.Gme;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;

[assembly: SupportedOSPlatform("browser")]

namespace Tupfer.Designer;

static class Program
{
    static Task Main(string[] args)
    {
        BrowserFileDialogs.NewWorkDir();
        App.CreateDialogs = topLevel => new BrowserFileDialogs(topLevel);
        TextToSpeech.LoadAsync = BrowserInterop.LoadSpeechAsync;
        TextToSpeech.NaturalSynthesizer = async (voice, text) => Convert.FromBase64String(await BrowserInterop.SpeakNaturalAsync(voice, text));
        TextToSpeech.Synthesizer = BrowserInterop.Speak;
        Audio.Device = new BrowserAudio();
        MainWindowViewModel.AutoSaveStore = new BrowserAutoSaveStore();
        BrowserInterop.SetTitle(MainWindowViewModel.AppName);
        MainWindowViewModel.CanSaveToPen = BrowserInterop.CanPickSaveFile();

        // main.js übergibt die Adresse der Seite
        var pageUri = new Uri(args[0]);
        MainWindowViewModel.Examples = Examples.Create(pageUri);
        var query = System.Web.HttpUtility.ParseQueryString(pageUri.Query);
        App.StartExample = MainWindowViewModel.Examples.FirstOrDefault(e => e.Id == query["beispiel"]);
        // In der Nextcloud-App: Dateien in Nextcloud öffnen und speichern, ?datei=… gleich öffnen
        if (NextcloudInterop.Available())
        {
            NextcloudFileDialogs.Init();
            App.CreateDialogs = topLevel => new NextcloudFileDialogs(new BrowserFileDialogs(topLevel));
            var startFile = NextcloudInterop.StartFile();
            if (startFile.Length > 0)
                App.StartExample = new ExampleProject("nextcloud", Path.GetFileName(startFile) + " aus Nextcloud",
                    () => NextcloudFileDialogs.OpenProjectAsync(startFile));
        }
        // Download-Seite mit den Setups für Windows, Linux und macOS
        MainWindowViewModel.OpenDownloads = () => BrowserInterop.OpenUrl(new Uri(pageUri, "download.html").ToString());

        return AppBuilder.Configure<App>()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }
}
