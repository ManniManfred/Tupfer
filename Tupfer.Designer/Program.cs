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
        PersonDetector.FaceDetector = PersonSegmenter.DetectFacesAsync;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

#pragma warning disable AVALONIA_X11_CSD // noch als experimentell markiert
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // Linux: Kopfzeile und Titelleiste vereint, Rahmen zeichnet Avalonia selbst
            .With(new X11PlatformOptions { EnableDrawnDecorations = true })
            .LogToTrace();
#pragma warning restore AVALONIA_X11_CSD
}
