using System.IO.Compression;
using System.Text.Json;
using Tupfer.Designer.Services;

namespace Tupfer.Designer;

/// <summary>
/// Sichert den Bearbeitungsstand im Browser (IndexedDB). Das Arbeitsverzeichnis liegt nur im Speicher,
/// deshalb werden Bilder und Audiodateien mitgesichert (als ZIP, nur wenn sich daran etwas geändert hat)
/// und beim Wiederherstellen an dieselbe Stelle zurückgeschrieben.
/// </summary>
class BrowserAutoSaveStore : IAutoSaveStore
{
    const string Root = "/tupfer";
    string? _json;
    string? _mediaKey;

    public async Task SaveAsync(AutoSaveSnapshot snapshot)
    {
        // Erst die Dateien, dann das Projekt, das auf sie verweist
        var dir = BrowserFileDialogs.WorkDirOf(Path.Combine(snapshot.BaseDir, "x"));
        var media = BrowserFileDialogs.MediaFiles(dir);
        var key = dir + "\n" + string.Join("\n", media.Select(f =>
        {
            var info = new FileInfo(f);
            return $"{f}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }));
        if (key != _mediaKey)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var file in media)
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(Root, file), CompressionLevel.NoCompression);
            BrowserInterop.AutoSaveSetMedia(media.Count > 0 ? ms.ToArray() : []);
            await BrowserInterop.AutoSaveSaveMediaAsync();
            _mediaKey = key;
        }

        var json = JsonSerializer.Serialize(snapshot);
        if (json != _json)
        {
            await BrowserInterop.AutoSaveSaveAsync(json);
            _json = json;
        }
    }

    public async Task<AutoSaveSnapshot?> LoadAsync()
    {
        var json = await BrowserInterop.AutoSaveLoadAsync();
        if (string.IsNullOrEmpty(json))
            return null;
        var snapshot = JsonSerializer.Deserialize<AutoSaveSnapshot>(json);
        if (snapshot == null)
            return null;

        var media = Convert.FromBase64String(await BrowserInterop.AutoSaveLoadMediaAsync());
        if (media.Length > 0)
        {
            using var ms = new MemoryStream(media);
            await ZipFile.ExtractToDirectoryAsync(ms, Root, overwriteFiles: true);
        }
        Directory.CreateDirectory(snapshot.BaseDir);
        Directory.CreateDirectory(snapshot.CurrentDirectory);
        BrowserFileDialogs.ReserveWorkDir(snapshot.BaseDir);
        BrowserFileDialogs.ReserveWorkDir(snapshot.CurrentDirectory);
        _json = json;
        return snapshot;
    }
}
