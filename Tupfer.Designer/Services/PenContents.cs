using System.Globalization;
using System.Text;

namespace Tupfer.Designer.Services;

/// <summary>Eine GME-Datei auf dem Stift; <paramref name="Error"/> ist gesetzt, wenn der Kopf nicht lesbar war.</summary>
public record PenProduct(string RelativePath, uint? ProductId, string Comment, DateTime? Date, long Size, string? Error = null)
{
    /// <summary>Datei von Ravensburger (Kommentar „CHOMPTECH DATA FORMAT …“).</summary>
    public bool IsOfficial => Comment.StartsWith("CHOMPTECH", StringComparison.Ordinal);
}

/// <summary>Stift finden (Laufwerk „tiptoi“) und die Produkte der GME-Dateien darauf auslesen (Desktop).</summary>
public static class PenContents
{
    /// <summary>Wurzelverzeichnisse aller angeschlossenen Stifte (Windows: Bezeichnung, Linux/macOS: Einhängepunkt).</summary>
    public static List<string> FindPens() => DriveInfo.GetDrives()
        .Where(d =>
        {
            try
            {
                return d.IsReady && (string.Equals(d.VolumeLabel, "tiptoi", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(Path.GetFileName(d.RootDirectory.FullName.TrimEnd('/', '\\')), "tiptoi", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        })
        .Select(d => d.RootDirectory.FullName)
        .ToList();

    /// <summary>Alle GME-Dateien unter <paramref name="root"/> (auch in Unterordnern), nach Produkt-ID sortiert.</summary>
    public static List<PenProduct> ReadProducts(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(root, "*.gme", options)
            .Select(f => ReadHeader(f, Path.GetRelativePath(root, f)))
            .OrderBy(p => p.ProductId ?? uint.MaxValue)
            .ThenBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Liest Produkt-ID (0x14), Kommentar (Länge bei 0x20) und Datum (JJJJMMTT danach) aus dem Kopf.</summary>
    public static PenProduct ReadHeader(string path, string relativePath)
    {
        long size = 0;
        try
        {
            using var stream = File.OpenRead(path);
            size = stream.Length;
            var head = new byte[0x80];
            int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            if (read < 0x30 || BitConverter.ToUInt32(head, 0x08) != 0x238B)
                return new PenProduct(relativePath, null, "", null, size, "keine gültige GME-Datei");
            uint product = BitConverter.ToUInt32(head, 0x14);
            int length = Math.Min(head[0x20], read - 0x21);
            var comment = Encoding.Latin1.GetString(head, 0x21, length);
            DateTime? date = null;
            if (0x21 + length + 8 <= read
                && DateTime.TryParseExact(Encoding.ASCII.GetString(head, 0x21 + length, 8), "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                date = d;
            return new PenProduct(relativePath, product, comment, date, size);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new PenProduct(relativePath, null, "", null, size, e.Message);
        }
    }

    /// <summary>
    /// Produkt-IDs, die in mehr als einer Datei vorkommen und an denen eine eigene Datei beteiligt ist.
    /// Ravensburger verteilt manche Produkte selbst auf mehrere Dateien (z. B. Spielfiguren), das ist kein Fehler.
    /// </summary>
    public static List<IGrouping<uint, PenProduct>> Duplicates(IEnumerable<PenProduct> products) => SharedIds(products)
        .Where(g => g.Any(p => !p.IsOfficial))
        .ToList();

    static IEnumerable<IGrouping<uint, PenProduct>> SharedIds(IEnumerable<PenProduct> products) => products
        .Where(p => p.ProductId.HasValue)
        .GroupBy(p => p.ProductId!.Value)
        .Where(g => g.Count() > 1);

    /// <summary>Übersicht als Text: Warnung zu doppelten IDs, dann eine Zeile je Datei.</summary>
    public static string Describe(string root, IReadOnlyList<PenProduct> products, uint? projectProductId = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Stift: {root} – {products.Count} GME-Datei(en)");
        sb.AppendLine();
        var duplicates = Duplicates(products);
        if (duplicates.Count > 0)
        {
            sb.AppendLine("Achtung, doppelte Produkt-IDs – der Stift nimmt dann womöglich die falsche Datei:");
            foreach (var g in duplicates)
                sb.AppendLine($"  {g.Key}: {string.Join(", ", g.Select(p => p.RelativePath))}");
        }
        else
            sb.AppendLine("Keine doppelten Produkt-IDs.");
        var official = SharedIds(products).Where(g => g.All(p => p.IsOfficial)).Select(g => g.Key).ToList();
        if (official.Count > 0)
            sb.AppendLine($"Mehrere Ravensburger-Dateien je Produkt (so vorgesehen): {string.Join(", ", official)}");
        if (projectProductId is { } own && products.Any(p => p.ProductId == own))
            sb.AppendLine($"Die Produkt-ID des geöffneten Projekts ({own}) ist auf dem Stift belegt.");
        sb.AppendLine();

        int nameWidth = Math.Clamp(products.Select(p => p.RelativePath.Length).DefaultIfEmpty(0).Max(), 5, 60);
        sb.AppendLine($"{"Produkt",7}  {"Datei".PadRight(nameWidth)}  {"Datum",10}  {"Größe",8}  Kommentar");
        var duplicateIds = duplicates.Select(g => g.Key).ToHashSet();
        foreach (var p in products)
        {
            var id = p.ProductId?.ToString() ?? "?";
            var mark = p.ProductId is { } pid && duplicateIds.Contains(pid) ? "!" : " ";
            var date = p.Date?.ToString("dd.MM.yyyy") ?? "";
            var size = $"{p.Size / 1024.0:0} KB";
            sb.AppendLine($"{mark}{id,6}  {p.RelativePath.PadRight(nameWidth)}  {date,10}  {size,8}  {p.Error ?? p.Comment}");
        }
        return sb.ToString();
    }
}
