using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Desktop: immer mit einer *.tupfer-Datei arbeiten, Startbildschirm und zuletzt geöffnete Dateien.</summary>
// Ändert ProjectStore.ArchiveWorkDir (statisch)
[Collection("Statisch")]
public class ProjectFileModeTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"tupfer-datei-{Guid.NewGuid():N}");
    readonly Func<string, string> _previousWorkDir = ProjectStore.ArchiveWorkDir;
    int _workDirs;

    public ProjectFileModeTests()
    {
        Directory.CreateDirectory(_root);
        ProjectStore.ArchiveWorkDir = _ => Directory.CreateDirectory(Path.Combine(_root, "arbeit", $"{++_workDirs}")).FullName;
    }

    public void Dispose()
    {
        ProjectStore.ArchiveWorkDir = _previousWorkDir;
        Directory.Delete(_root, true);
    }

    class SaveDialogs(string? target) : IFileDialogs
    {
        public Task<string?> OpenAsync(string title, string filterName, params string[] patterns) => Task.FromResult<string?>(null);
        public Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension) => Task.FromResult(target);
        public Task<string> PrintAsync(string pdfPath) => Task.FromResult("");
        public Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create) => Task.FromResult<string?>(null);
    }

    string RecentPath => Path.Combine(_root, "zuletzt.json");

    MainWindowViewModel CreateVm(string? target) => new()
    {
        RequireProjectFile = true,
        Recent = new RecentFiles(RecentPath),
        Dialogs = new SaveDialogs(target),
    };

    [Fact]
    public void ListeHältDieNeuestenZuerstOhneDoppelte()
    {
        var recent = new RecentFiles(RecentPath);
        for (int i = 0; i < RecentFiles.MaxCount + 2; i++)
            recent.Add(Path.Combine(_root, $"p{i}.tupfer"));
        recent.Add(Path.Combine(_root, "p5.tupfer"));

        var loaded = new RecentFiles(RecentPath).Files;
        Assert.Equal(RecentFiles.MaxCount, loaded.Count);
        Assert.Equal(Path.Combine(_root, "p5.tupfer"), loaded[0]);
        Assert.Equal(Path.Combine(_root, "p11.tupfer"), loaded[1]);
        Assert.Single(loaded, f => f.EndsWith("p5.tupfer"));
        Assert.DoesNotContain(Path.Combine(_root, "p0.tupfer"), loaded);

        recent.Remove(Path.Combine(_root, "p5.tupfer"));
        Assert.DoesNotContain(Path.Combine(_root, "p5.tupfer"), new RecentFiles(RecentPath).Files);
    }

    [Fact]
    public async Task NeuesProjektWirdGleichAlsDateiAngelegt()
    {
        var target = Path.Combine(_root, "buch.tupfer");
        var vm = CreateVm(target);
        await vm.NewProjectCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Fehler", vm.LogText);
        Assert.True(ProjectStore.IsArchive(target));
        Assert.Equal(target, vm.FilePath);
        Assert.False(vm.IsModified);
        Assert.Equal(target, Assert.Single(vm.RecentProjects).FilePath);
    }

    [Fact]
    public async Task NeuesProjektOhneSpeicherortWirdNichtAngelegt()
    {
        var vm = CreateVm(null);
        await vm.StartAsync(null, restore: true);
        Assert.True(vm.ShowStartScreen);

        await vm.NewProjectCommand.ExecuteAsync(null);
        Assert.Null(vm.FilePath);
        Assert.True(vm.ShowStartScreen);
    }

    [Fact]
    public async Task StartbildschirmEndetMitDemÖffnenEinerDatei()
    {
        var target = Path.Combine(_root, "buch.tupfer");
        await CreateVm(target).NewProjectCommand.ExecuteAsync(null);

        var vm = CreateVm(null);
        await vm.StartAsync(null, restore: true);
        Assert.True(vm.ShowStartScreen);

        await vm.OpenRecentCommand.ExecuteAsync(Assert.Single(vm.RecentProjects));
        Assert.False(vm.ShowStartScreen);
        Assert.Equal(target, vm.FilePath);
    }

    [Fact]
    public async Task FehlendeDateiVerschwindetAusDerListe()
    {
        var target = Path.Combine(_root, "buch.tupfer");
        await CreateVm(target).NewProjectCommand.ExecuteAsync(null);
        File.Delete(target);

        var vm = CreateVm(null);
        await vm.OpenRecentCommand.ExecuteAsync(Assert.Single(vm.RecentProjects));
        Assert.Empty(vm.RecentProjects);
        Assert.Null(vm.FilePath);
        Assert.Contains("gibt es nicht mehr", vm.LogText);
    }

    [Fact]
    public async Task OhneProjektdateiPflichtKeinStartbildschirm()
    {
        var vm = new MainWindowViewModel { Dialogs = new SaveDialogs(null) };
        await vm.StartAsync(null, restore: true);
        Assert.False(vm.ShowStartScreen);
    }
}
