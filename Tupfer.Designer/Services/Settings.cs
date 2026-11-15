using System.Text.Json;

namespace Tupfer.Designer.Services;

/// <summary>
/// Einstellungen des Programms, die nicht zum Projekt gehören (u. a. der Zugang zu Immich). Liegen am Desktop
/// neben der Liste der zuletzt geöffneten Dateien im Benutzerprofil, im Browser im Speicher der Seite
/// (localStorage). Der API-Key kommt damit nie ins Projekt.
/// </summary>
public class Settings
{
    /// <summary>Adresse des Immich-Servers, z. B. „https://fotos.example.org“.</summary>
    public string ImmichUrl { get; set; } = "";

    public string ImmichApiKey { get; set; } = "";

    /// <summary>Fotos aus Immich im Original statt als große Vorschau herunterladen.</summary>
    public bool ImmichOriginal { get; set; }

    /// <summary>Liest und schreibt die Einstellungen (JSON); im Browser durch localStorage ersetzt.</summary>
    public static ISettingsStore Store { get; set; } = new FileSettingsStore();

    public static Settings Load()
    {
        try
        {
            return Store.Read() is { Length: > 0 } json ? JsonSerializer.Deserialize<Settings>(json) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Defekte oder gesperrte Einstellungen: mit den Vorgaben weitermachen
            return new();
        }
    }

    public void Save() => Store.Write(JsonSerializer.Serialize(this));
}

public interface ISettingsStore
{
    string? Read();
    void Write(string json);
}

/// <summary>Desktop: einstellungen.json im Benutzerprofil (Windows: %AppData%\TupferDesigner, Linux: ~/.config/TupferDesigner).</summary>
public class FileSettingsStore(string? path = null) : ISettingsStore
{
    readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TupferDesigner", "einstellungen.json");

    public string? Read() => File.Exists(_path) ? File.ReadAllText(_path) : null;

    public void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, json);
        // Enthält den API-Key: nur für den Benutzer lesbar
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsBrowser())
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
