namespace Tupfer.Designer.Gme;

/// <summary>Im Browser gibt es keine Windows-Sprachausgabe (stattdessen <see cref="TextToSpeech.Synthesizer"/>).</summary>
static class WindowsSpeech
{
    public static bool TrySpeak(string lang, string text, string wavPath) => false;
}
