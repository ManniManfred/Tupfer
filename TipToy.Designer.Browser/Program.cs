using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using TipToy.Designer.Gme;

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
        return AppBuilder.Configure<App>()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }
}
