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
    public async Task NatuerlicheStimme_KommtAlsOggInDieGme()
    {
        var dir = Directory.CreateTempSubdirectory("tupfer-tts").FullName;
        try
        {
            // Piper liefert WAV (hier 16000 Hz wie bei x_low-Stimmen); in der GME soll Ogg Vorbis 22050 Hz landen
            var samples = Enumerable.Range(0, 16000).Select(i => (float)Math.Sin(i * 0.1) * 0.5f).ToArray();
            TextToSpeech.NaturalSynthesizer = (_, _) => Task.FromResult(PiperSpeech.WavFromSamples(samples, 16000));
            var tts = new TextToSpeech(dir, _ => { });
            await tts.PrepareAsync([("de", "Hallo")]);

            var data = tts.Speak("de", "Hallo");
            Assert.Equal(".ogg", Services.Audio.ExtensionOf(data));
            Assert.True(data.Length < 32000 / 4, $"Ogg ist {data.Length} Bytes groß");

            // Beim nächsten Mal aus dem Cache, nicht neu kodiert
            TextToSpeech.NaturalSynthesizer = (_, _) => throw new InvalidOperationException("darf nicht sprechen");
            Assert.Equal(data, new TextToSpeech(dir, _ => { }).Speak("de", "Hallo"));
        }
        finally
        {
            TextToSpeech.NaturalSynthesizer = null;
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

    [Fact]
    public void Piper_Pfade_und_Wav()
    {
        Assert.Equal("de/de_DE/kerstin/low/de_DE-kerstin-low", PiperSpeech.VoicePath("de_DE-kerstin-low"));
        Assert.Equal("de/de_DE/thorsten_emotional/medium/de_DE-thorsten_emotional-medium", PiperSpeech.VoicePath("de_DE-thorsten_emotional-medium"));
        Assert.All(Voices.All, v => PiperSpeech.VoicePath(v.Id.Split('#')[0]));
        Assert.Throws<ArgumentException>(() => PiperSpeech.VoicePath("kaputt"));

        var wav = PiperSpeech.WavFromSamples([0f, 1f, -2f], 22050);
        Assert.Equal(44 + 6, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(22050, BitConverter.ToInt32(wav, 24));
        Assert.Equal(6, BitConverter.ToInt32(wav, 40));
        Assert.Equal([0, 0x7fff, -0x7fff], new[] { BitConverter.ToInt16(wav, 44), BitConverter.ToInt16(wav, 46), BitConverter.ToInt16(wav, 48) }.Select(v => (int)v));
    }
}
