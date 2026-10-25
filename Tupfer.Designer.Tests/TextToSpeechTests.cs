using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Natürliche Stimmen (im Browser Piper) mit Rückfall auf die einfache Sprachausgabe.</summary>
public class TextToSpeechTests
{
    [Fact]
    public async Task NatuerlicheStimme_MitRueckfall()
    {
        var dir = Directory.CreateTempSubdirectory("tupfer-tts").FullName;
        var calls = new List<string>();
        try
        {
            TextToSpeech.NaturalSynthesizer = (voice, text) =>
            {
                calls.Add(voice);
                return voice == "de_DE-ramona-low" ? throw new InvalidOperationException("nicht erreichbar") : Task.FromResult(new byte[] { 1, (byte)calls.Count });
            };
            TextToSpeech.Synthesizer = (_, _) => [9];
            var notices = new List<string>();
            var tts = new TextToSpeech(dir, _ => { }, notices.Add);

            await tts.PrepareAsync([("de", "Hallo"), ("de", "Hallo"), ("de:de_DE-kerstin-low", "Hallo"), ("de", "")]);
            Assert.Equal(["de_DE-thorsten-medium", "de_DE-kerstin-low"], calls);
            Assert.Equal([1, 1], tts.Speak("de", "Hallo"));
            Assert.Equal([1, 2], tts.Speak("de:de_DE-kerstin-low", "Hallo"));

            // Schon im Cache: nicht noch einmal sprechen
            await tts.PrepareAsync([("de", "Hallo")]);
            Assert.Equal(2, calls.Count);

            // Stimme lässt sich nicht laden: Meldung, die einfache Stimme spricht
            await tts.PrepareAsync([("de:de_DE-ramona-low", "Hallo")]);
            Assert.Contains(notices, n => n.Contains("nicht erreichbar"));
            Assert.Equal([9], tts.Speak("de:de_DE-ramona-low", "Hallo"));

            // Sprache ohne natürliche Stimme: einfache Stimme, ohne Versuch
            await tts.PrepareAsync([("xx", "Hallo")]);
            Assert.Equal(3, calls.Count);
            Assert.Equal([9], tts.Speak("xx", "Hallo"));
        }
        finally
        {
            TextToSpeech.NaturalSynthesizer = null;
            TextToSpeech.Synthesizer = null;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Stimme_TextVorProjekt_NurPassendeSprache()
    {
        Assert.Equal("de", Voices.SpeechKey("de", null, null));
        Assert.Equal("de:de_DE-kerstin-low", Voices.SpeechKey("de", "de_DE-kerstin-low", ""));
        Assert.Equal("de:de_DE-thorsten_emotional-medium#whisper", Voices.SpeechKey("de", "de_DE-kerstin-low", "de_DE-thorsten_emotional-medium#whisper"));
        // Nach dem Wechsel der Sprache passen die Stimmen nicht mehr
        Assert.Equal("en", Voices.SpeechKey("en", "de_DE-kerstin-low", "de_DE-ramona-low"));
        Assert.Equal("en", Voices.SpeechKey("", null, null));
        Assert.Equal(("de", "de_DE-thorsten_emotional-medium#whisper"), Voices.Parse("de:de_DE-thorsten_emotional-medium#whisper"));
        Assert.All(Voices.All, v => Assert.Matches(@"^[a-z]{2}_[A-Z]{2}-[a-z_]+-(x_low|low|medium|high)(#[a-z]+)?$", v.Id));
        Assert.Equal("de_DE-thorsten-medium", Voices.Default("de")?.Id);
    }

    [Fact]
    public void Stimmen_werden_gespeichert()
    {
        var project = new ProjectFile { Voice = "de_DE-kerstin-low" };
        project.Speak.Add(new SpeakEntry { Name = "a", Text = "A", Voice = "de_DE-karlsson-low" });
        project.Speak.Add(new SpeakEntry { Name = "b", Text = "B" });
        var vm = ViewModels.ProjectViewModel.FromFile(project, Path.GetTempPath(), _ => { });
        var back = vm.ToFile(Path.GetTempPath());
        Assert.Equal("de_DE-kerstin-low", back.Voice);
        Assert.Equal("de_DE-karlsson-low", back.Speak[0].Voice);
        Assert.Null(back.Speak[1].Voice);
        Assert.DoesNotContain("\"Voice\": null", Services.ProjectStore.ToJson(back));
        Assert.Equal("de:de_DE-karlsson-low", Voices.SpeechKey(back, back.Speak[0]));
        Assert.Equal("de:de_DE-kerstin-low", Voices.SpeechKey(back, back.Speak[1]));
    }
}
