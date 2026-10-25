using Tupfer.Designer.Services;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Automatisches Sichern des Bearbeitungsstands (Desktop-Ablage).</summary>
public class AutoSaveTests
{
    [Fact]
    public async Task FileStore_SichertUndStelltWiederHer()
    {
        var dir = Directory.CreateTempSubdirectory("tupfer-autosave").FullName;
        try
        {
            var path = Path.Combine(dir, "unter", "autosave.json");
            Assert.Null(await new FileAutoSaveStore(path).LoadAsync());

            var project = ProjectStore.ToJson(new Model.ProjectFile { ProductId = 951, Comment = "Test" });
            var snapshot = new AutoSaveSnapshot("/projekte/a.tupfer.json", "/projekte", "/projekte", project);
            await new FileAutoSaveStore(path).SaveAsync(snapshot);

            var loaded = await new FileAutoSaveStore(path).LoadAsync();
            Assert.Equal(snapshot, loaded);
            Assert.Equal(951, (int)ProjectStore.FromJson(loaded!.ProjectJson).ProductId);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task FileStore_SchreibtNurBeiAenderungen()
    {
        var dir = Directory.CreateTempSubdirectory("tupfer-autosave").FullName;
        try
        {
            var path = Path.Combine(dir, "autosave.json");
            var store = new FileAutoSaveStore(path);
            var snapshot = new AutoSaveSnapshot(null, dir, dir, "{}");
            await store.SaveAsync(snapshot);
            File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await store.SaveAsync(snapshot);
            Assert.Equal(2000, File.GetLastWriteTimeUtc(path).Year);
            await store.SaveAsync(snapshot with { ProjectJson = "{ }" });
            Assert.NotEqual(2000, File.GetLastWriteTimeUtc(path).Year);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
