using Avalonia.Platform.Storage;

namespace TipToy.Designer.Services;

/// <summary>Kopiert Dateien ohne lokalen Pfad (z. B. im Browser ausgewählte) in ein Verzeichnis.</summary>
public static class WorkFiles
{
    /// <summary>Kopiert die Datei nach <paramref name="dir"/>, ohne vorhandene zu überschreiben. Liefert den neuen Pfad.</summary>
    public static async Task<string> CopyAsync(IStorageFile file, string dir)
    {
        await using var source = await file.OpenReadAsync();
        var path = UniquePath(dir, file.Name);
        await using (var target = File.Create(path))
            await source.CopyToAsync(target);
        return path;
    }

    /// <summary>Pfad für <paramref name="name"/> in <paramref name="dir"/>; existiert er schon, mit angehängter Nummer.</summary>
    public static string UniquePath(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(name));
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(dir, $"{stem}-{i}{ext}");
        return path;
    }
}
