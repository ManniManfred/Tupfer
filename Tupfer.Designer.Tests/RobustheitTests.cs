using System.Buffers.Binary;
using System.IO.Compression;
using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Fehlerfälle: ungewöhnliche Eingaben, Datenverlust beim Speichern und Aufnehmen, ungespeicherte Änderungen.</summary>
// Ändert Audio.Device und Arbeitsverzeichnisse (statisch): nicht parallel zu anderen Tests derselben Sammlung
[Collection("Statisch")]
public class RobustheitTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tupfer-robust").FullName;

    public void Dispose()
    {
        // Die Sperrdatei eines belegten Arbeitsverzeichnisses ist offen und verhindert unter Windows das Löschen
        ProjectStore.ReleaseWorkDir();
        Directory.Delete(_dir, true);
    }

    // ---------- Skripte ----------

    [Theory]
    [InlineData("$x := 99999999999999999999", 65535)]
    [InlineData("$x := 0x1234567890ABCDEF12345", 0x2345)]
    [InlineData("$x := 65537", 1)]
    public void SehrLangeZahlenWerdenWieBeiTttoolModulo65536(string line, int expected)
    {
        var command = Assert.Single(ScriptParser.ParseLine(line).Commands);
        Assert.Equal((ushort)expected, command.Arg.Constant);
    }

    [Fact]
    public void LangeHexBytesUeberlaufenNicht()
    {
        var command = Assert.Single(ScriptParser.ParseLine("?($1,2) (FFFFFFFFFFFFFFFFFFFF12 F9)").Commands);
        Assert.Equal(new byte[] { 0x12, 0xF9 }, command.Op);
    }

    [Fact]
    public void LangeZahlVerhindertNichtDasSpeichern()
    {
        var project = new ProjectFile { ProductId = 950 };
        project.Scripts.Add(new ScriptEntry { Name = "a", Lines = ["$x := 123456789012345678901234567890 P(hallo)"] });
        File.WriteAllBytes(Path.Combine(_dir, "hallo.wav"), Audio.Wav(new short[100]));
        Assert.Single(GmeBuilder.MediaFiles(project, _dir));
        ProjectStore.SaveArchive(project, _dir, Path.Combine(_dir, "p.tupfer"));
    }

    [Fact]
    public void SkriptnameAlsZuGrosserCodeIstEinFehler()
    {
        var project = new ProjectFile { ProductId = 950 };
        project.Scripts.Add(new ScriptEntry { Name = "70000", Lines = ["$x := 1"] });
        var ex = Assert.Throws<GmeBuildException>(() => GmeBuilder.Build(project, _dir, (_, _) => []));
        Assert.Contains("70000", ex.Message);
    }

    // ---------- tttool-YAML ----------

    [Fact]
    public void AuslassungspunkteInWeiterenEintraegenBleibenErhalten()
    {
        var yaml = Path.Combine(_dir, "buch.yaml");
        File.WriteAllText(yaml, """
            product-id: 950
            scripts:
              a:
              - P(hallo)
            unbekannt:
              text: 'Na... und?'
            """);
        var project = TttoolYaml.Import(yaml);
        Assert.Contains("Na... und?", project.ExtraYaml);
        Assert.DoesNotMatch(@"(?m)^\.\.\.\s*$", project.ExtraYaml!);
    }

    // ---------- WAV ----------

    [Fact]
    public void KaputteWavLaengenWerfenKeineUnerwartetenFehler()
    {
        var wav = Audio.Wav(Enumerable.Repeat((short)1000, 100).ToArray());
        var broken = new byte[wav.Length + 8];
        wav.AsSpan(0, 12).CopyTo(broken);
        // Zusätzlicher Abschnitt mit riesiger Länge vor „fmt “
        "LIST"u8.CopyTo(broken.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(16), int.MaxValue - 4);
        wav.AsSpan(12).CopyTo(broken.AsSpan(20));

        Assert.Equal(0, Audio.ClippedFraction(broken));
        Audio.FixWavLengths(broken);
        Assert.Throws<InvalidDataException>(() => Audio.WavToOgg(broken));
    }

    // ---------- Projektdatei ----------

    [Fact]
    public void FehlendeDateienWerdenGemeldet()
    {
        var project = new ProjectFile { ProductId = 950 };
        project.Speak.Add(new SpeakEntry { Name = "hallo", Text = "Hallo", Recording = "aufnahmen/weg.ogg" });
        var (_, _, missing) = ProjectStore.SaveArchive(project, _dir, Path.Combine(_dir, "p.tupfer"));
        Assert.Equal([Path.Combine(_dir, "aufnahmen", "weg.ogg")], missing);
    }

    [Fact]
    public void BenutztesArbeitsverzeichnisWirdNichtGeleert()
    {
        var work = Path.Combine(_dir, "arbeit");
        Assert.True(ProjectStore.ClaimWorkDir(work, clear: true));
        File.WriteAllText(Path.Combine(work, "aufnahme.ogg"), "x");

        // Dieselbe Instanz darf es wieder belegen (und leeren)
        Assert.True(ProjectStore.ClaimWorkDir(work, clear: true));
        Assert.False(File.Exists(Path.Combine(work, "aufnahme.ogg")));
        File.WriteAllText(Path.Combine(work, "aufnahme.ogg"), "x");

        // Ein anderes Verzeichnis belegen gibt das erste frei; hält eine andere Instanz es, bleibt es unberührt
        Assert.True(ProjectStore.ClaimWorkDir(Path.Combine(_dir, "anderes"), clear: true));
        using (new FileStream(Path.Combine(work, ProjectStore.LockFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.False(ProjectStore.ClaimWorkDir(work, clear: true));
        Assert.True(File.Exists(Path.Combine(work, "aufnahme.ogg")));
        Assert.True(ProjectStore.ClaimWorkDir(work, clear: false));
        Assert.True(File.Exists(Path.Combine(work, "aufnahme.ogg")));
    }

    // ---------- Begrüßung ----------

    class YamlDialogs(string path) : IFileDialogs
    {
        public Task<string?> OpenAsync(string title, string filterName, params string[] patterns) => Task.FromResult<string?>(null);
        public Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension) => Task.FromResult<string?>(path);
        public Task<string> PrintAsync(string pdfPath) => Task.FromResult("");
        public Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create) => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task BegruessungAlsSatzWirdBeimBauenVorgelesen()
    {
        var yaml = Path.Combine(_dir, "familie.yaml");
        var vm = new MainWindowViewModel { Dialogs = new YamlDialogs(yaml) };
        vm.Project.Welcome = "Willkommen bei Familie Roßkamp";

        await vm.ExportYamlCommand.ExecuteAsync(null);

        Assert.Equal("begruessung", vm.Project.Welcome);
        Assert.Equal("Willkommen bei Familie Roßkamp", vm.Project.Speak.Single(s => s.Name == "begruessung").Text);
        var text = File.ReadAllText(yaml);
        Assert.Contains("welcome: 'begruessung'", text);
        Assert.Contains("Willkommen bei Familie Roßkamp", text);
    }

    // ---------- Aufnahmen ----------

    class FakeAudio : IAudioDevice
    {
        public short Level = 1000;
        public Task PlayAsync(byte[] data, string extension) => Task.CompletedTask;
        public void StopPlayback() { }
        public void SetPlaybackPaused(bool paused) { }
        public Task StartRecordingAsync() => Task.CompletedTask;
        public Task<byte[]> StopRecordingAsync() => Task.FromResult(Audio.Wav(Enumerable.Repeat(Level, 4000).ToArray()));
    }

    class Dialogs(bool discard) : IFileDialogs
    {
        public int Questions;
        public Task<string?> OpenAsync(string title, string filterName, params string[] patterns) => Task.FromResult<string?>(null);
        public Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension) => Task.FromResult<string?>(null);
        public Task<string> PrintAsync(string pdfPath) => Task.FromResult("");
        public Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string message)
        {
            Questions++;
            return Task.FromResult(discard);
        }
    }

    MainWindowViewModel OpenProject(Dialogs dialogs)
    {
        var project = new ProjectFile { ProductId = 950 };
        project.Scripts.Add(new ScriptEntry { Name = "a", Lines = ["P(hallo)"] });
        project.Speak.Add(new SpeakEntry { Name = "hallo", Text = "Hallo" });
        project.Pages.Add(new PageEntry { Name = "Seite 1" });
        var path = Path.Combine(_dir, "buch" + ProjectStore.LegacyExtension);
        ProjectStore.Save(project, path);
        var vm = new MainWindowViewModel { Dialogs = dialogs };
        vm.Load(path);
        Assert.DoesNotContain("Fehler", vm.LogText);
        return vm;
    }

    [Fact]
    public async Task AufnahmeUeberschreibtNichtDieEinesUmbenanntenTextes()
    {
        var previous = Audio.Device;
        var audio = new FakeAudio();
        Audio.Device = audio;
        try
        {
            var vm = OpenProject(new Dialogs(discard: true));
            var first = vm.Project.Speak.Single();
            await vm.RecordSpeakCommand.ExecuteAsync(first);
            await vm.RecordSpeakCommand.ExecuteAsync(first);
            var firstRecording = first.Recording!;
            var firstData = File.ReadAllBytes(firstRecording);

            // Umbenennen, dann einen neuen Text mit dem alten Namen aufnehmen
            first.Name = "gruss";
            var second = new SpeakViewModel { Name = "hallo", Text = "Hallo" };
            vm.Project.Speak.Add(second);
            audio.Level = 3000;
            await vm.RecordSpeakCommand.ExecuteAsync(second);
            await vm.RecordSpeakCommand.ExecuteAsync(second);

            Assert.NotEqual(firstRecording, second.Recording);
            Assert.Equal(firstData, File.ReadAllBytes(firstRecording));

            // Neu aufnehmen ersetzt die eigene Datei
            var secondRecording = second.Recording;
            await vm.RecordSpeakCommand.ExecuteAsync(second);
            await vm.RecordSpeakCommand.ExecuteAsync(second);
            Assert.Equal(secondRecording, second.Recording);
        }
        finally
        {
            Audio.Device = previous;
        }
    }

    [Fact]
    public async Task AufnahmeSchneidenUndLauterMachen()
    {
        var previous = Audio.Device;
        Audio.Device = new FakeAudio();
        try
        {
            var vm = OpenProject(new Dialogs(discard: true));
            var speak = vm.Project.Speak.Single();
            // Ton mit 1/4 Aussteuerung, 4000 Samples
            var original = Path.Combine(_dir, "ton.wav");
            File.WriteAllBytes(original, Audio.Wav(Enumerable.Range(0, 4000)
                .Select(i => (short)(8192 * Math.Sin(2 * Math.PI * 441 * i / Audio.SampleRate))).ToArray()));
            speak.Recording = original;
            var originalData = File.ReadAllBytes(original);

            await vm.EditRecordingCommand.ExecuteAsync(speak);
            var editor = vm.RecordingEditor!;
            Assert.Equal(4000, editor.Samples.Length);
            // Ende vor den Anfang ziehen geht nicht
            editor.Start = 0.25;
            editor.End = 0.1;
            Assert.True(editor.End > editor.Start);
            editor.End = 0.75;
            editor.NormalizeCommand.Execute(null);
            // Sinus mit 0,25 Aussteuerung hat den Sprachpegel 0,177 → auf 0,08 leiser
            Assert.InRange(editor.Volume, 44, 46);
            Assert.False(editor.Clips);
            editor.Volume = 5000;
            Assert.Equal(RecordingEditorViewModel.MaxVolume, editor.Volume);
            Assert.True(editor.Clips);
            editor.Volume = 300;

            await vm.ApplyRecordingEditCommand.ExecuteAsync(null);
            Assert.Null(vm.RecordingEditor);
            // Neue Datei (so gilt das Projekt als geändert), die alte Aufnahme bleibt unverändert
            Assert.NotEqual(original, speak.Recording);
            Assert.Equal(originalData, File.ReadAllBytes(original));
            var edited = Audio.DecodeMono(File.ReadAllBytes(speak.Recording!));
            Assert.Equal(2000, edited.Length);
            Assert.InRange(Audio.Peak(edited), 0.7f, 0.8f);

            // Abbrechen ändert nichts
            var recording = speak.Recording;
            await vm.EditRecordingCommand.ExecuteAsync(speak);
            vm.RecordingEditor!.Start = 0.5;
            vm.CancelRecordingEditCommand.Execute(null);
            Assert.Null(vm.RecordingEditor);
            Assert.Equal(recording, speak.Recording);
        }
        finally
        {
            Audio.Device = previous;
        }
    }

    [Fact]
    public async Task AlleAufnahmenAngleichen()
    {
        var vm = OpenProject(new Dialogs(discard: true));
        short[] Ton(double level) => Enumerable.Range(0, 8000)
            .Select(i => (short)(level * 32767 * Math.Sin(2 * Math.PI * 441 * i / Audio.SampleRate))).ToArray();
        // Zu leise, zu laut, passend (Sprachpegel eines Sinus = Aussteuerung / √2) und eine MP3-Datei
        var leise = new SpeakViewModel { Name = "leise" };
        var laut = new SpeakViewModel { Name = "laut" };
        var passt = new SpeakViewModel { Name = "passt" };
        var mp3 = new SpeakViewModel { Name = "mp3" };
        File.WriteAllBytes(Path.Combine(_dir, "leise.wav"), Audio.Wav(Ton(0.02)));
        File.WriteAllBytes(Path.Combine(_dir, "laut.wav"), Audio.Wav(Ton(0.9)));
        File.WriteAllBytes(Path.Combine(_dir, "passt.wav"), Audio.Wav(Ton(Audio.TargetSpeechLevel * Math.Sqrt(2))));
        File.WriteAllBytes(Path.Combine(_dir, "x.mp3"), "ID3 keine Aufnahme"u8.ToArray());
        leise.Recording = Path.Combine(_dir, "leise.wav");
        laut.Recording = Path.Combine(_dir, "laut.wav");
        passt.Recording = Path.Combine(_dir, "passt.wav");
        mp3.Recording = Path.Combine(_dir, "x.mp3");
        // Dieselbe Datei für zwei Texte
        var auchLaut = new SpeakViewModel { Name = "auchlaut", Recording = laut.Recording };
        foreach (var s in new[] { leise, laut, passt, mp3, auchLaut })
            vm.Project.Speak.Add(s);

        await vm.LevelRecordingsCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(_dir, "passt.wav"), passt.Recording);
        Assert.Equal(Path.Combine(_dir, "x.mp3"), mp3.Recording);
        Assert.NotEqual(Path.Combine(_dir, "laut.wav"), laut.Recording);
        Assert.Equal(laut.Recording, auchLaut.Recording);
        foreach (var s in new[] { leise, laut })
            Assert.Equal(Audio.TargetSpeechLevel, Audio.SpeechLevel(Audio.DecodeMono(File.ReadAllBytes(s.Recording!))), 0.005);
        Assert.Contains("2 von 4 Aufnahmen angeglichen", vm.LogText);
    }

    // ---------- Ungespeicherte Änderungen ----------

    [Fact]
    public async Task UngespeicherteAenderungenWerdenNichtStillVerworfen()
    {
        var dialogs = new Dialogs(discard: false);
        var vm = OpenProject(dialogs);
        Assert.False(vm.IsModified);

        // Ohne Änderungen wird nicht gefragt
        await vm.NewProjectCommand.ExecuteAsync(null);
        Assert.Equal(0, dialogs.Questions);
        Assert.False(vm.IsModified);

        vm.Project.Comment = "Geändert";
        Assert.True(vm.IsModified);
        await vm.NewProjectCommand.ExecuteAsync(null);
        Assert.Equal(1, dialogs.Questions);
        Assert.Equal("Geändert", vm.Project.Comment);
    }

    [Fact]
    public void NachDemSpeichernGiltDasProjektAlsUnveraendert()
    {
        var vm = OpenProject(new Dialogs(discard: false));
        var previous = ProjectStore.ArchiveWorkDir;
        ProjectStore.ArchiveWorkDir = _ => Directory.CreateDirectory(Path.Combine(_dir, "arbeit-speichern")).FullName;
        try
        {
            vm.Project.Comment = "Geändert";
            Assert.True(vm.IsModified);
            vm.SaveProjectCommand.Execute(null);
            Assert.DoesNotContain("Fehler", vm.LogText);
            Assert.False(vm.IsModified);
        }
        finally
        {
            ProjectStore.ArchiveWorkDir = previous;
        }
    }

    // ---------- SVG ----------

    [Fact]
    public void SvgUebernimmtNurBilder()
    {
        var secret = Path.Combine(_dir, "geheim.txt");
        File.WriteAllText(secret, "geheim");
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="100mm" height="100mm">
              <image x="0" y="0" width="10" height="10" xlink:href="{new Uri(secret)}" />
            </svg>
            """;
        var result = SvgImporter.Import(svg, Path.Combine(_dir, "svg"), _dir);
        Assert.Empty(result.Elements);
        Assert.Contains(result.Warnings, w => w.Contains("kein unterstütztes Bild"));
    }

    // ---------- Piper ----------

    [Fact]
    public void PruefsummeWirdGeprueft()
    {
        PiperSpeech.VerifySha256("abc"u8, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "test");
        Assert.Throws<InvalidDataException>(() => PiperSpeech.VerifySha256("abd"u8, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "test"));
    }
}
