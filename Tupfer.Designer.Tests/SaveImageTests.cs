using Tupfer.Designer.Model;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>„Bild speichern …“: die Datei des gewählten Bildes unverändert auf die Festplatte schreiben.</summary>
public class SaveImageTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "tupfer-bildspeichern-" + Guid.NewGuid());
    static readonly byte[] Bytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    public SaveImageTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>Wie im Browser: Nach dem Speichern wird die geschriebene Datei weggeräumt.</summary>
    class Dialogs(string? target, bool deleteAfterSave = false) : IFileDialogs
    {
        public string? Suggested, Extension;
        public byte[]? Saved;

        public Task<string?> OpenAsync(string title, string filterName, params string[] patterns) => Task.FromResult<string?>(null);

        public Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension)
        {
            Suggested = suggestedName;
            Extension = extension;
            return Task.FromResult(target);
        }

        public Task SavedAsync(params string[] paths)
        {
            Saved = File.ReadAllBytes(paths.Single());
            if (deleteAfterSave)
                File.Delete(paths.Single());
            return Task.CompletedTask;
        }

        public Task<string> PrintAsync(string pdfPath) => Task.FromResult("");
        public Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create) => Task.FromResult<string?>(null);
    }

    (MainWindowViewModel Vm, string Source) Create(IFileDialogs dialogs)
    {
        var source = Path.Combine(_dir, "foto.jpg");
        File.WriteAllBytes(source, Bytes);
        var vm = new MainWindowViewModel { Dialogs = dialogs };
        var page = vm.Project.SelectedPage!;
        var image = new ElementViewModel(vm.Project, ElementKind.Image) { ImagePath = source, Width = 30, Height = 20 };
        page.Elements.Add(image);
        page.SelectedElement = image;
        return (vm, source);
    }

    [Fact]
    public async Task SpeichertDieDateiUnveraendert()
    {
        var target = Path.Combine(_dir, "ziel", "kopie.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var dialogs = new Dialogs(target);
        var (vm, _) = Create(dialogs);

        await vm.SaveImageCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Fehler", vm.LogText);
        Assert.Equal("foto.jpg", dialogs.Suggested);
        Assert.Equal(".jpg", dialogs.Extension);
        Assert.Equal(Bytes, File.ReadAllBytes(target));
        Assert.Contains("Bild gespeichert", vm.LogText);
    }

    [Fact]
    public async Task AbbrechenSchreibtNichts()
    {
        var (vm, _) = Create(new Dialogs(null));
        await vm.SaveImageCommand.ExecuteAsync(null);
        Assert.Single(Directory.GetFiles(_dir));
        Assert.DoesNotContain("Bild gespeichert", vm.LogText);
    }

    [Fact]
    public async Task BildBleibtErhaltenWennDasZielDieBilddateiSelbstIst()
    {
        // Im Browser liegt das Bild im Arbeitsverzeichnis, und dort landet auch der Download (danach gelöscht)
        var source = Path.Combine(_dir, "foto.jpg");
        var dialogs = new Dialogs(source, deleteAfterSave: true);
        var (vm, _) = Create(dialogs);

        await vm.SaveImageCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Fehler", vm.LogText);
        Assert.Equal(Bytes, dialogs.Saved);
        Assert.Equal(Bytes, File.ReadAllBytes(source));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task FehlendeDateiWirdGemeldet()
    {
        var (vm, source) = Create(new Dialogs(Path.Combine(_dir, "x.jpg")));
        File.Delete(source);
        await vm.SaveImageCommand.ExecuteAsync(null);
        Assert.Contains("fehlt", vm.LogText);
    }
}
