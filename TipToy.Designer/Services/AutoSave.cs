using System.Text.Json;

namespace TipToy.Designer.Services;

/// <summary>
/// Automatisch gesicherter Bearbeitungsstand: Projekt (wie in der Projektdatei, Pfade relativ zu
/// <paramref name="BaseDir"/>), der Pfad der Projektdatei (null = noch nicht gespeichert) und das
/// aktuelle Verzeichnis (dorthin werden neue Bilder und Aufnahmen kopiert).
/// </summary>
public record AutoSaveSnapshot(string? FilePath, string BaseDir, string CurrentDirectory, string ProjectJson);

/// <summary>Ablage für den automatisch gesicherten Stand.</summary>
public interface IAutoSaveStore
{
    /// <summary>Sichert den Stand; wird regelmäßig aufgerufen und schreibt nur, wenn sich etwas geändert hat.</summary>
    Task SaveAsync(AutoSaveSnapshot snapshot);

    /// <summary>Liefert den zuletzt gesicherten Stand oder null.</summary>
    Task<AutoSaveSnapshot?> LoadAsync();
}

/// <summary>
/// Desktop: eine Datei im Benutzerprofil (Windows: %AppData%\TipToyDesigner, Linux: ~/.config/TipToyDesigner).
/// Bilder und Audiodateien liegen ohnehin auf der Festplatte.
/// </summary>
public class FileAutoSaveStore : IAutoSaveStore
{
    readonly string _path;
    string? _last;

    public FileAutoSaveStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TipToyDesigner", "autosave.json");
    }

    public async Task SaveAsync(AutoSaveSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot);
        if (json == _last)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Erst vollständig schreiben, dann ersetzen: Ein Absturz beim Schreiben zerstört den alten Stand nicht
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, json);
        File.Move(temp, _path, overwrite: true);
        _last = json;
    }

    public async Task<AutoSaveSnapshot?> LoadAsync()
    {
        if (!File.Exists(_path))
            return null;
        var json = await File.ReadAllTextAsync(_path);
        _last = json;
        return JsonSerializer.Deserialize<AutoSaveSnapshot>(json);
    }
}
