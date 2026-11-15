using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using Tupfer.Designer.Gme;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;

[assembly: SupportedOSPlatform("browser")]

namespace Tupfer.Designer;

/// <summary>Einstellungen im Browser: localStorage der Seite (in Nextcloud im Ursprung von Nextcloud).</summary>
class BrowserSettingsStore : ISettingsStore
{
    public string? Read() => BrowserInterop.SettingsLoad();
    public void Write(string json) => BrowserInterop.SettingsSave(json);
}

static class Program
{
    static Task Main(string[] args)
    {
        BrowserFileDialogs.NewWorkDir();
        App.CreateDialogs = topLevel => new BrowserFileDialogs(topLevel);
        // Projekte (*.tupfer) liegen schon in einem eigenen Arbeitsverzeichnis: dort entpacken
        ProjectStore.ArchiveWorkDir = path => Path.GetDirectoryName(path)!;
        TextToSpeech.LoadAsync = BrowserInterop.LoadSpeechAsync;
        TextToSpeech.NaturalSynthesizer = async (voice, text) => Convert.FromBase64String(await BrowserInterop.SpeakNaturalAsync(voice, text));
        TextToSpeech.Synthesizer = BrowserInterop.Speak;
        PersonDetector.Segmenter = async rgb =>
        {
            BrowserInterop.SetModelInput(rgb);
            var data = Convert.FromBase64String(await BrowserInterop.SegmentPersonsAsync());
            int scores = PersonDetector.Candidates * sizeof(float);
            return (MemoryMarshal.Cast<byte, float>(data.AsSpan(0, scores)).ToArray(), data[scores..]);
        };
        PersonDetector.FaceDetector = async (rgb, width, height) =>
        {
            BrowserInterop.SetModelInput(rgb);
            return MemoryMarshal.Cast<byte, float>(Convert.FromBase64String(await BrowserInterop.DetectFacesAsync(width, height))).ToArray();
        };
        Audio.Device = new BrowserAudio();
        MainWindowViewModel.AutoSaveStore = new BrowserAutoSaveStore();
        Settings.Store = new BrowserSettingsStore();
        BrowserInterop.SetTitle(MainWindowViewModel.AppName);
        MainWindowViewModel.CanSaveToPen = BrowserInterop.CanPickSaveFile();

        // main.js übergibt die Adresse der Seite
        var pageUri = new Uri(args[0]);
        ImmichClient.PageIsSecure = pageUri.Scheme == Uri.UriSchemeHttps;
        MainWindowViewModel.Examples = Examples.Create(pageUri);
        var query = System.Web.HttpUtility.ParseQueryString(pageUri.Query);
        App.StartExample = MainWindowViewModel.Examples.FirstOrDefault(e => e.Id == query["beispiel"]);
        // In der Nextcloud-App: Dateien in Nextcloud öffnen und speichern, ?datei=… gleich öffnen,
        // ?neu=… (Menü „Neu“ der Dateien-App) gleich anlegen
        if (NextcloudInterop.Available())
        {
            NextcloudFileDialogs.Init();
            // Immich über die Weiterleitung der App: kein CORS nötig, nur der vom Admin eingetragene Server
            ImmichClient.Proxy = new ImmichProxy(NextcloudInterop.ImmichProxy(), NextcloudInterop.ImmichUrl(),
                () => NextcloudInterop.RequestToken());
            App.CreateDialogs = topLevel => new NextcloudFileDialogs(new BrowserFileDialogs(topLevel));
            var startFile = NextcloudInterop.StartFile();
            if (startFile.Length > 0)
                App.StartExample = new ExampleProject("nextcloud", Path.GetFileName(startFile) + " aus Nextcloud",
                    () => NextcloudFileDialogs.OpenProjectAsync(startFile));
            var startNew = NextcloudInterop.StartNew();
            if (startNew.Length > 0)
                App.StartExample = new ExampleProject("nextcloud-neu", Path.GetFileName(startNew) + " (neu in Nextcloud)",
                    () => NextcloudFileDialogs.CreateProjectAsync(startNew));
        }
        // Download-Seite mit den Setups für Windows, Linux und macOS
        MainWindowViewModel.OpenDownloads = () => BrowserInterop.OpenUrl(new Uri(pageUri, "download.html").ToString());

        return AppBuilder.Configure<App>()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }
}
