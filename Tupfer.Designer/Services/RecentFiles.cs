using System.Text.Json;

namespace Tupfer.Designer.Services;

/// <summary>
/// Liste der zuletzt geöffneten bzw. gespeicherten Projektdateien (Desktop), neueste zuerst.
/// Liegt neben dem automatisch gesicherten Stand im Benutzerprofil.
/// </summary>
public class RecentFiles
{
    public const int MaxCount = 10;

    readonly string _path;
    List<string> _files = new();

    public RecentFiles(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TupferDesigner", "zuletzt.json");
        try
        {
            if (File.Exists(_path))
                _files = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Defekte oder gesperrte Liste: leer anfangen
        }
    }

    public IReadOnlyList<string> Files => _files;

    /// <summary>Setzt die Datei an den Anfang der Liste.</summary>
    public void Add(string path)
    {
        path = Path.GetFullPath(path);
        _files.RemoveAll(f => SamePath(f, path));
        _files.Insert(0, path);
        if (_files.Count > MaxCount)
            _files.RemoveRange(MaxCount, _files.Count - MaxCount);
        Save();
    }

    public void Remove(string path)
    {
        if (_files.RemoveAll(f => SamePath(f, path)) > 0)
            Save();
    }

    static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_files));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Die Liste ist nur eine Bequemlichkeit
        }
    }
}
