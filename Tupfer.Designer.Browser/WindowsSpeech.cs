namespace Tupfer.Designer.Gme;

/// <summary>Im Browser gibt es keine Windows-Sprachausgabe (dort spricht nur die natürliche Stimme).</summary>
static class WindowsSpeech
{
    public static bool TrySpeak(string lang, string text, string wavPath) => false;
}
