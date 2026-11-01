using System.IO.Compression;
using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Projektdatei *.tupfer (ZIP mit Bildern und Aufnahmen) und Öffnen alter *.tupfer.json.</summary>
public class ProjectArchiveTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"tupfer-archiv-{Guid.NewGuid():N}");
    readonly Func<string, string> _previousWorkDir = ProjectStore.ArchiveWorkDir;
    int _workDirs;

    public ProjectArchiveTests()
    {
        Directory.CreateDirectory(_root);
        // Arbeitsverzeichnisse nicht im Benutzerprofil anlegen
        ProjectStore.ArchiveWorkDir = _ => Directory.CreateDirectory(Path.Combine(_root, "arbeit", $"{++_workDirs}")).FullName;
    }

    public void Dispose()
    {
        ProjectStore.ArchiveWorkDir = _previousWorkDir;
        Directory.Delete(_root, true);
    }

    class SaveDialogs(string target) : IFileDialogs
    {
        public Task<string?> OpenAsync(string title, string filterName, params string[] patterns) => Task.FromResult<string?>(null);
        public Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension) => Task.FromResult<string?>(target);
        public Task<string> PrintAsync(string pdfPath) => Task.FromResult("");
        public Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create) => Task.FromResult<string?>(null);
    }

    static byte[] Tone(short level) => Audio.Wav(Enumerable.Repeat(level, 2000).ToArray());

    static byte[] NoSpeech(string lang, string text) => throw new InvalidOperationException("Sprachausgabe darf nicht laufen");

    static readonly DateTime Date = new(2026, 1, 1);

    /// <summary>
    /// Altes Projekt: Bild im Projektordner und eins daneben, Aufnahme unter aufnahmen/,
    /// Audiodatei im Medienverzeichnis außerhalb des Projektordners. Liefert den Pfad der .tupfer.json.
    /// </summary>
    string CreateLegacyProject()
    {
        var dir = Path.Combine(_root, "alt", "buch");
        Directory.CreateDirectory(Path.Combine(dir, "bilder"));
        Directory.CreateDirectory(Path.Combine(dir, "aufnahmen"));
        Directory.CreateDirectory(Path.Combine(_root, "alt", "extern"));
        Directory.CreateDirectory(Path.Combine(_root, "alt", "audio"));
        File.WriteAllBytes(Path.Combine(dir, "bilder", "katze.png"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_root, "alt", "extern", "hund.png"), [4, 5, 6]);
        File.WriteAllBytes(Path.Combine(dir, "aufnahmen", "hallo.wav"), Tone(1000));
        File.WriteAllBytes(Path.Combine(_root, "alt", "audio", "muh.wav"), Tone(2000));

        var project = new ProjectFile { ProductId = 951, MediaPath = "../audio/%s" };
        project.Scripts.Add(new ScriptEntry { Name = "katze", Lines = ["P(hallo)"] });
        project.Scripts.Add(new ScriptEntry { Name = "kuh", Lines = ["P(muh)"] });
        project.Speak.Add(new SpeakEntry { Name = "hallo", Text = "Hallo", Recording = "aufnahmen/hallo.wav" });
        project.Pages.Add(new PageEntry
        {
            Name = "Seite 1",
            Elements =
            [
                new ElementEntry { Kind = ElementKind.Image, ImagePath = "bilder/katze.png", Width = 50, Height = 50 },
                new ElementEntry { Kind = ElementKind.Image, ImagePath = "../extern/hund.png", Width = 50, Height = 50 },
            ],
        });
        var path = Path.Combine(dir, "buch" + ProjectStore.LegacyExtension);
        ProjectStore.Save(project, path);
        return path;
    }

    [Fact]
    public void SpeichernUndAnAnderemOrtOeffnen()
    {
        var legacy = CreateLegacyProject();
        var expectedGme = GmeBuilder.Build(ProjectStore.Load(legacy), Path.GetDirectoryName(legacy)!, NoSpeech, Date);

        var saved = Path.Combine(_root, "gespeichert", "buch.tupfer");
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        var vm = new MainWindowViewModel { Dialogs = new SaveDialogs(saved) };
        vm.Load(legacy);
        vm.SaveProjectAsCommand.Execute(null);
        Assert.DoesNotContain("Fehler", vm.LogText);
        Assert.True(ProjectStore.IsArchive(saved));
        Assert.Equal(saved, vm.FilePath);

        using (var zip = ZipFile.OpenRead(saved))
            Assert.Equal(
                ["aufnahmen/hallo.wav", "bilder/katze.png", "dateien/hund.png", "medien/muh.wav", ProjectStore.ArchiveEntry],
                zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));

        // Nach dem Speichern wird im Arbeitsverzeichnis weitergearbeitet, nicht mehr mit den alten Dateien
        Assert.All(vm.Project.Pages[0].Elements, e => Assert.StartsWith(Path.Combine(_root, "arbeit"), e.ImagePath));

        // An anderen Ort kopieren, alles andere löschen
        var moved = Path.Combine(_root, "woanders", "kopie.tupfer");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Copy(saved, moved);
        Directory.Delete(Path.Combine(_root, "alt"), true);
        Directory.Delete(Path.Combine(_root, "gespeichert"), true);
        Directory.Delete(Path.Combine(_root, "arbeit"), true);

        var reopened = new MainWindowViewModel();
        reopened.Load(moved);
        Assert.DoesNotContain("Fehler", reopened.LogText);
        Assert.Equal(moved, reopened.FilePath);
        var images = reopened.Project.Pages[0].Elements.Select(e => e.ImagePath).ToList();
        Assert.All(images, p => Assert.True(File.Exists(p), p));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(images[0]));
        Assert.Equal([4, 5, 6], File.ReadAllBytes(images[1]));
        Assert.True(File.Exists(reopened.Project.Speak.Single(s => s.Name == "hallo").Recording));

        // Daraus lässt sich dieselbe GME bauen
        var (file, baseDir) = ProjectStore.LoadArchive(moved, Path.Combine(_root, "gme"));
        Assert.Equal(expectedGme, GmeBuilder.Build(file, baseDir, NoSpeech, Date));
    }

    [Fact]
    public void AltesProjektWirdBeimSpeichernZuTupfer()
    {
        var legacy = CreateLegacyProject();
        var vm = new MainWindowViewModel { Dialogs = new SaveDialogs(Path.Combine(_root, "nicht-gefragt.tupfer")) };
        vm.Load(legacy);
        Assert.DoesNotContain("Fehler", vm.LogText);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(legacy)!, "bilder", "katze.png"), vm.Project.Pages[0].Elements[0].ImagePath);

        vm.SaveProjectCommand.Execute(null);
        var archive = Path.Combine(Path.GetDirectoryName(legacy)!, "buch.tupfer");
        Assert.Equal(archive, vm.FilePath);
        Assert.True(ProjectStore.IsArchive(archive));
        Assert.False(ProjectStore.IsArchive(legacy));
        Assert.False(File.Exists(Path.Combine(_root, "nicht-gefragt.tupfer")));
    }

    [Fact]
    public void AltesZipMitTupferJsonLaesstSichOeffnen()
    {
        var legacy = CreateLegacyProject();
        var dir = Path.GetDirectoryName(legacy)!;
        var zipPath = Path.Combine(_root, "download.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            foreach (var f in new[] { legacy, Path.Combine(dir, "bilder", "katze.png") })
                zip.CreateEntryFromFile(f, "buch/" + Path.GetRelativePath(dir, f).Replace('\\', '/'));

        var (file, baseDir) = ProjectStore.LoadArchive(zipPath, Path.Combine(_root, "entpackt"));
        Assert.Equal(Path.Combine(_root, "entpackt", "buch"), baseDir);
        Assert.True(File.Exists(Path.Combine(baseDir, file.Pages[0].Elements[0].ImagePath)));
    }
}
