using TipToy.Designer.Gme;
using TipToy.Designer.Model;
using TipToy.Designer.Services;
using TipToy.Designer.ViewModels;
using Xunit;

namespace TipToy.Designer.Tests;

/// <summary>Eigene Aufnahmen statt Sprachausgabe.</summary>
public class RecordingTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"aufnahmen-{Guid.NewGuid():N}");

    public RecordingTests() => Directory.CreateDirectory(Path.Combine(_dir, "aufnahmen"));

    public void Dispose() => Directory.Delete(_dir, true);

    static ProjectFile Project(params SpeakEntry[] speak)
    {
        var p = new ProjectFile { ProductId = 950 };
        p.Scripts.Add(new ScriptEntry { Name = "feld", Lines = ["P(hallo)"] });
        p.Speak.AddRange(speak);
        return p;
    }

    static byte[] Tone(short level) => Audio.Wav(Enumerable.Repeat(level, 2000).ToArray());

    [Fact]
    public void AufnahmeErsetztSprachausgabe()
    {
        File.WriteAllBytes(Path.Combine(_dir, "aufnahmen", "hallo.wav"), Tone(1000));
        File.WriteAllBytes(Path.Combine(_dir, "hallo.wav"), Tone(1000));
        var date = new DateTime(2026, 1, 1);
        byte[] NoSpeech(string lang, string text) => throw new InvalidOperationException("Sprachausgabe darf nicht laufen");

        var recorded = GmeBuilder.Build(Project(new SpeakEntry { Name = "hallo", Text = "Hallo", Recording = "aufnahmen/hallo.wav" }),
            _dir, NoSpeech, date);
        // Gleiches Ergebnis wie mit einer Audiodatei im Medienverzeichnis ohne Text
        var mediaFile = GmeBuilder.Build(Project(), _dir, NoSpeech, date);
        Assert.Equal(mediaFile, recorded);
    }

    [Fact]
    public void FehlendeAufnahme()
    {
        var project = Project(new SpeakEntry { Name = "hallo", Text = "Hallo", Recording = "aufnahmen/fehlt.wav" });
        var ex = Assert.Throws<GmeBuildException>(() => GmeBuilder.Build(project, _dir, (_, _) => Tone(1)));
        Assert.Contains("fehlt", ex.Message);
    }

    [Fact]
    public void YamlOhneAufgenommeneTexte()
    {
        var yaml = Path.Combine(_dir, "test.yaml");
        TttoolYaml.Export(Project(
            new SpeakEntry { Name = "hallo", Text = "Hallo", Recording = "aufnahmen/hallo.wav" },
            new SpeakEntry { Name = "tschuess", Text = "Tschüss" }), yaml);
        var text = File.ReadAllText(yaml);
        Assert.Contains("'tschuess': 'Tschüss'", text);
        Assert.DoesNotContain("'hallo': 'Hallo'", text);
    }

    [Fact]
    public void AufnahmeRelativSpeichern()
    {
        var vm = new ProjectViewModel(_ => { });
        vm.Speak.Add(new SpeakViewModel { Name = "hallo", Text = "Hallo", Recording = Path.Combine(_dir, "aufnahmen", "hallo.wav") });
        var file = vm.ToFile(_dir);
        Assert.Equal("aufnahmen/hallo.wav", file.Speak[0].Recording);

        var loaded = ProjectViewModel.FromFile(file, _dir, _ => { });
        Assert.Equal(Path.Combine(_dir, "aufnahmen", "hallo.wav"), loaded.Speak[0].Recording);
        Assert.True(loaded.Speak[0].HasRecording);
        Assert.Equal("hallo.wav", loaded.Speak[0].RecordingName);
    }

    [Fact]
    public void WavKopfReparieren()
    {
        var wav = Tone(500);
        // Wie nach einem abgebrochenen Aufnahmeprogramm: Längen ungültig
        BitConverter.TryWriteBytes(wav.AsSpan(4), int.MaxValue);
        BitConverter.TryWriteBytes(wav.AsSpan(40), 0);
        Audio.FixWavLengths(wav);
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(4000, BitConverter.ToInt32(wav, 40));
        Assert.Equal(2000.0 / Audio.SampleRate, Audio.WavSeconds(wav), 6);
        Assert.Equal(".wav", Audio.ExtensionOf(wav));
        Assert.Equal(".ogg", Audio.ExtensionOf("OggS...."u8.ToArray()));
    }
}
