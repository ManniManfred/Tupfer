using TipToy.Designer.Gme;
using Xunit;

namespace TipToy.Designer.Tests;

/// <summary>Natürliche Stimme (im Browser Piper) mit Rückfall auf die einfache Sprachausgabe.</summary>
public class TextToSpeechTests
{
    [Fact]
    public async Task NatuerlicheStimme_MitRueckfall()
    {
        var dir = Directory.CreateTempSubdirectory("tiptoy-tts").FullName;
        int natural = 0;
        try
        {
            TextToSpeech.NaturalSynthesizer = (language, text) =>
            {
                natural++;
                return language == "de" ? Task.FromResult(new byte[] { 1, 2, 3 }) : throw new InvalidOperationException("keine Stimme");
            };
            TextToSpeech.Synthesizer = (_, _) => [9];
            var notices = new List<string>();
            var tts = new TextToSpeech(dir, _ => { }, notices.Add);

            await tts.PrepareAsync("de", ["Hallo", "Hallo", "Welt", ""]);
            Assert.Equal(2, natural);
            Assert.Equal([1, 2, 3], tts.Speak("de", "Hallo"));

            // Schon im Cache: nicht noch einmal sprechen
            await tts.PrepareAsync("de", ["Hallo"]);
            Assert.Equal(2, natural);

            // Keine natürliche Stimme: Meldung, danach spricht die einfache Stimme
            await tts.PrepareAsync("xx", ["Hallo"]);
            Assert.Contains(notices, n => n.Contains("keine Stimme"));
            Assert.Equal([9], tts.Speak("xx", "Hallo"));
        }
        finally
        {
            TextToSpeech.NaturalSynthesizer = null;
            TextToSpeech.Synthesizer = null;
            Directory.Delete(dir, true);
        }
    }
}
