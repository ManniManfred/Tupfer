using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

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

    /// <summary>Sinuston (Sprachbereich), damit der Encoder etwas zu tun hat.</summary>
    static short[] Sine(int count, int rate, double hz = 440) =>
        Enumerable.Range(0, count).Select(i => (short)(8000 * Math.Sin(2 * Math.PI * hz * i / rate))).ToArray();

    [Fact]
    public void AufnahmeAlsOggVorbis()
    {
        var wav = Audio.Wav(Sine(Audio.SampleRate * 2, Audio.SampleRate));
        var ogg = Audio.WavToOgg(wav);

        Assert.Equal(".ogg", Audio.ExtensionOf(ogg));
        Assert.Equal("OggS", System.Text.Encoding.ASCII.GetString(ogg, 0, 4));
        // Erstes Paket: Vorbis-Kennung, mono, 22050 Hz
        int header = 27 + ogg[26];
        Assert.Equal(1, ogg[header]);
        Assert.Equal("vorbis", System.Text.Encoding.ASCII.GetString(ogg, header + 1, 6));
        Assert.Equal(1, ogg[header + 11]);
        Assert.Equal(Audio.SampleRate, BitConverter.ToInt32(ogg, header + 12));
        Assert.Equal(2.0, Audio.OggSeconds(ogg), 1);
        Assert.True(ogg.Length < wav.Length / 5, $"OGG {ogg.Length} Bytes, WAV {wav.Length} Bytes");
        // Gleiche Eingabe, gleiche Datei (GME bleibt reproduzierbar)
        Assert.Equal(ogg, Audio.WavToOgg(wav));

        // Zurückgewandelt: gleiche Länge, Ton noch hörbar
        var back = Audio.OggToWav(ogg);
        Assert.Equal(2.0, Audio.WavSeconds(back), 1);
        var samples = Enumerable.Range(0, (back.Length - 44) / 2).Select(i => BitConverter.ToInt16(back, 44 + i * 2)).ToArray();
        Assert.InRange(samples.Max(s => Math.Abs((int)s)), 6000, 10000);
    }

    [Fact]
    public void AndereAbtastrateUndStereo()
    {
        // 44,1 kHz Stereo, 1,5 s → mono 22050 Hz
        var mono = Sine(66150, 44100);
        var stereo = Audio.Wav(mono.SelectMany(s => new[] { s, s }).ToArray(), 44100);
        BitConverter.TryWriteBytes(stereo.AsSpan(22), (short)2);
        BitConverter.TryWriteBytes(stereo.AsSpan(28), 44100 * 4);
        BitConverter.TryWriteBytes(stereo.AsSpan(32), (short)4);
        var ogg = Audio.WavToOgg(stereo);
        int header = 27 + ogg[26];
        Assert.Equal(1, ogg[header + 11]);
        Assert.Equal(Audio.SampleRate, BitConverter.ToInt32(ogg, header + 12));
        Assert.Equal(1.5, Audio.OggSeconds(ogg), 1);

        Assert.Throws<InvalidDataException>(() => Audio.WavToOgg("keine WAV-Datei"u8.ToArray()));
    }

    [Fact]
    public void OggAufnahmeInGme()
    {
        var ogg = Audio.WavToOgg(Audio.Wav(Sine(Audio.SampleRate, Audio.SampleRate)));
        File.WriteAllBytes(Path.Combine(_dir, "aufnahmen", "hallo.ogg"), ogg);
        byte[] NoSpeech(string lang, string text) => throw new InvalidOperationException("Sprachausgabe darf nicht laufen");
        var gme = GmeBuilder.Build(Project(new SpeakEntry { Name = "hallo", Text = "Hallo", Recording = "aufnahmen/hallo.ogg" }),
            _dir, NoSpeech, new DateTime(2026, 1, 1));
        // Unverändert eingebettet (GME-Medien sind mit einer festen Maske verschlüsselt, daher nur die Länge)
        Assert.True(gme.Length > ogg.Length);
    }
}
