using Avalonia;
using Tupfer.Designer.Gme;
using Tupfer.Designer.Services;

namespace Tupfer.Designer;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Natürliche Stimmen wie im Browser (Piper); ohne Netz spricht die Sprachausgabe des Systems
        TextToSpeech.NaturalSynthesizer = PiperSpeech.SpeakAsync;
        PersonDetector.Segmenter = PersonSegmenter.SegmentAsync;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
