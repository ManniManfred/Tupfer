namespace Tupfer.Designer.Services;

/// <summary>Wie ein Katalogprodukt zum Stift steht.</summary>
public enum CatalogState
{
    /// <summary>Nicht auf dem Stift.</summary>
    Missing,

    /// <summary>Auf dem Stift, aktuell.</summary>
    Installed,

    /// <summary>Auf dem Stift, aber älter oder anders als die aktuelle Datei von Ravensburger.</summary>
    UpdateAvailable,
}

/// <summary>Fehler im Stift-Manager mit einer Meldung für den Benutzer (Stift voll, nicht verbunden …).</summary>
public class PenException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Abgleich der GME-Dateien auf dem Stift mit dem Ravensburger-Katalog, Installieren und Löschen.</summary>
public static class PenManager
{
    /// <summary>
    /// Sucht die Datei auf dem Stift zu einem Katalogprodukt: zuerst nach Dateinamen (so legt sie der tiptoi
    /// Manager ab), sonst eine Ravensburger-Datei mit derselben Produkt-ID, wenn kein anderes Katalogprodukt
    /// diese ID hat (manche Produkte bestehen aus mehreren Dateien mit gleicher ID).
    /// </summary>
    public static PenProduct? FindOnPen(CatalogEntry entry, IReadOnlyList<PenProduct> products, IReadOnlyList<CatalogEntry> catalog)
    {
        var byName = products.FirstOrDefault(p => string.Equals(Path.GetFileName(p.RelativePath), entry.FileName, StringComparison.OrdinalIgnoreCase));
        if (byName != null || entry.ProductId is not { } id)
            return byName;
        if (catalog.Count(c => c.ProductId == id) != 1)
            return null;
        // Die Datei darf nicht schon unter ihrem Namen einem anderen Katalogprodukt gehören
        return products.FirstOrDefault(p => p.IsOfficial && p.ProductId == id
            && !catalog.Any(c => c != entry && string.Equals(c.FileName, Path.GetFileName(p.RelativePath), StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Katalogprodukt zu einer Datei auf dem Stift (Umkehrung von <see cref="FindOnPen"/>).</summary>
    public static CatalogEntry? FindInCatalog(PenProduct product, IReadOnlyList<PenProduct> products, IReadOnlyList<CatalogEntry> catalog) =>
        catalog.FirstOrDefault(c => string.Equals(c.FileName, Path.GetFileName(product.RelativePath), StringComparison.OrdinalIgnoreCase))
        ?? catalog.FirstOrDefault(c => FindOnPen(c, products, catalog) == product);

    /// <summary>
    /// Update nötig, wenn die Datei auf dem Stift ein älteres Datum im Kopf hat oder eine andere Größe als die
    /// aktuelle Datei bei Ravensburger (ohne Angaben aus dem Katalog gilt sie als aktuell).
    /// </summary>
    public static bool NeedsUpdate(CatalogEntry entry, PenProduct onPen) =>
        (entry.Size is { } size && size != onPen.Size)
        || (entry.Version is { } version && (onPen.Date == null || onPen.Date < version))
        || onPen.Error != null;

    public static CatalogState StateOf(CatalogEntry entry, IReadOnlyList<PenProduct> products, IReadOnlyList<CatalogEntry> catalog) =>
        FindOnPen(entry, products, catalog) is not { } onPen ? CatalogState.Missing
        : NeedsUpdate(entry, onPen) ? CatalogState.UpdateAvailable
        : CatalogState.Installed;

    /// <summary>„1,2 GB“, „830 MB“, „45 KB“.</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{Math.Max(1, bytes / 1024.0):0} KB",
    };

    /// <summary>
    /// Prüft, ob die Datei auf den Stift passt (eine ersetzte Datei gibt ihren Platz frei); sonst
    /// <see cref="PenException"/>. Ohne bekannte Größe bzw. Speicherangabe wird nichts geprüft.
    /// </summary>
    public static void CheckSpace(IPenDrive drive, long? size, PenProduct? replaces)
    {
        if (size is not { } needed || drive.GetSpace() is not { } space)
            return;
        long free = space.Free + (replaces?.Size ?? 0);
        // Etwas Reserve für das Dateisystem
        const long reserve = 1 << 20;
        if (needed + reserve > free)
            throw new PenException($"Zu wenig Speicher auf dem Stift: {FormatSize(needed)} nötig, {FormatSize(Math.Max(0, free))} frei. " +
                                   "Bitte erst andere Dateien löschen.");
    }

    /// <summary>
    /// Lädt das Produkt von Ravensburger und legt es auf den Stift; eine ältere Fassung (<paramref name="replaces"/>)
    /// wird ersetzt. Erst wird in eine temporäre Datei geladen, damit bei Abbrüchen nichts Halbes auf dem Stift liegt.
    /// </summary>
    public static async Task InstallAsync(IPenDrive drive, RavensburgerCatalog catalog, CatalogEntry entry, PenProduct? replaces,
        IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        CheckSpace(drive, entry.Size, replaces);
        var temp = Path.Combine(Path.GetTempPath(), $"tupfer-{Guid.NewGuid():N}.gme");
        try
        {
            await catalog.DownloadAsync(entry, temp, progress, cancel);
            // Größe erst jetzt sicher bekannt (Katalog ohne Kopfabfrage)
            CheckSpace(drive, new FileInfo(temp).Length, replaces);
            try
            {
                await drive.WriteAsync(entry.FileName, temp);
                if (replaces != null && !string.Equals(replaces.RelativePath, entry.FileName, StringComparison.OrdinalIgnoreCase))
                    await drive.DeleteAsync(replaces.RelativePath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new PenException(IsDiskFull(e)
                    ? "Zu wenig Speicher auf dem Stift. Bitte erst andere Dateien löschen."
                    : $"Schreiben auf den Stift fehlgeschlagen ({e.Message}). Ist er noch verbunden?", e);
            }
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    /// <summary>Windows: ERROR_DISK_FULL/ERROR_HANDLE_DISK_FULL, Linux/macOS: ENOSPC.</summary>
    static bool IsDiskFull(Exception e) => e is IOException io && (io.HResult & 0xFFFF) is 0x70 or 0x27 or 28;
}
