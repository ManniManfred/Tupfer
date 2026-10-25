using System.Runtime.Versioning;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;

namespace Tupfer.Designer.Gme;

/// <summary>Sprachausgabe über die in Windows eingebauten Stimmen.</summary>
[SupportedOSPlatform("windows")]
static class WindowsSpeech
{
    public static bool TrySpeak(string lang, string text, string wavPath)
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            var voice = synth.GetInstalledVoices()
                .Where(v => v.Enabled)
                .FirstOrDefault(v => v.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals(lang, StringComparison.OrdinalIgnoreCase));
            if (voice != null)
                synth.SelectVoice(voice.VoiceInfo.Name);
            synth.Rate = -1;
            synth.SetOutputToWaveFile(wavPath, new SpeechAudioFormatInfo(22050, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            synth.Speak(new Prompt(text));
            synth.SetOutputToNull();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
