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
