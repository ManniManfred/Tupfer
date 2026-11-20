namespace Tupfer.Designer.Services;

/// <summary>Freier und gesamter Speicher des Stifts in Bytes.</summary>
public record PenSpace(long Free, long Total);

/// <summary>
/// Zugriff auf den Inhalt eines Stifts: am Desktop ein Laufwerk bzw. Ordner, im Browser ein Ordner, der über
/// die File System Access API gewählt wurde (Chrome, Edge).
/// </summary>
public interface IPenDrive
{
    /// <summary>Anzeige, z. B. „E:\“ oder „/media/anna/tiptoi“.</summary>
    string Name { get; }

    /// <summary>Alle GME-Dateien (auch in Unterordnern) mit dem Kopf, nach Produkt-ID sortiert.</summary>
    Task<List<PenProduct>> ReadProductsAsync();

    /// <summary>Freier Speicher; null, wenn er sich nicht ermitteln lässt (Browser).</summary>
    PenSpace? GetSpace();

    /// <summary>Löscht die Datei (Pfad relativ zum Stift).</summary>
    Task DeleteAsync(string relativePath);

    /// <summary>Kopiert die lokale Datei <paramref name="sourcePath"/> als <paramref name="fileName"/> auf die oberste Ebene des Stifts.</summary>
    Task WriteAsync(string fileName, string sourcePath);
}

/// <summary>Stift als Laufwerk bzw. Ordner im Dateisystem (Desktop, Tests mit einem temporären Ordner).</summary>
public class FolderPenDrive(string root) : IPenDrive
{
    public string Root { get; } = root;

    public string Name => Root;

    public Task<List<PenProduct>> ReadProductsAsync() => Task.Run(() => PenContents.ReadProducts(Root));

    /// <summary>Überschreibt den gemeldeten Speicher (Tests); null = vom Laufwerk.</summary>
    public PenSpace? SpaceOverride { get; set; }

    public PenSpace? GetSpace()
    {
        if (SpaceOverride != null)
            return SpaceOverride;
        try
        {
            var drive = new DriveInfo(Path.GetFullPath(Root));
            return drive.IsReady ? new PenSpace(drive.AvailableFreeSpace, drive.TotalSize) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public Task DeleteAsync(string relativePath)
    {
        var path = Resolve(relativePath);
        File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task WriteAsync(string fileName, string sourcePath)
    {
        var target = Resolve(fileName);
        // Erst unter anderem Namen schreiben: Bricht das Kopieren ab (Stift voll, abgezogen), bleibt keine halbe GME liegen
        var part = target + ".part";
        try
        {
            await using (var source = File.OpenRead(sourcePath))
            await using (var output = File.Create(part))
                await source.CopyToAsync(output);
            File.Move(part, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(part))
                File.Delete(part);
        }
    }

    /// <summary>Pfad auf dem Stift; Pfade, die aus dem Stift hinausführen, werden abgewiesen.</summary>
    string Resolve(string relativePath)
    {
        var root = Path.GetFullPath(Root);
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException($"„{relativePath}“ liegt nicht auf dem Stift.");
        return path;
    }
}

/// <summary>Wie Stifte gefunden bzw. gewählt werden; der Browser ersetzt das durch die File System Access API.</summary>
public static class PenDrives
{
    /// <summary>Angeschlossene Stifte (Desktop: Laufwerke „tiptoi“).</summary>
    public static Func<List<IPenDrive>> Find { get; set; } =
        () => OperatingSystem.IsBrowser() ? [] : PenContents.FindPens().Select(IPenDrive (p) => new FolderPenDrive(p)).ToList();

    /// <summary>
    /// Lässt einen Ordner als Stift wählen (Browser: File System Access API); null = nicht möglich.
    /// Am Desktop übernimmt das der Ordnerdialog (<see cref="ViewModels.IFileDialogs.PickFolderAsync"/>).
    /// </summary>
    public static Func<Task<IPenDrive?>>? PickFolder { get; set; }
}
