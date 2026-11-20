using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tupfer.Designer.Services;

/// <summary>Ein offizielles Produkt (eine GME-Datei) aus dem Ravensburger-Katalog.</summary>
public class CatalogEntry
{
    /// <summary>Name des Produkts laut Service-Seite, bei mehreren Dateien je Seite mit Dateinamen.</summary>
    public string Title { get; set; } = "";

    /// <summary>Rubrik der Service-Seite, z. B. „Bücher“, „Spiele“.</summary>
    public string Category { get; set; } = "";

    /// <summary>Ravensburger-Artikelnummern aus dem Seitentitel (z. B. „00787, 00558“); nicht die Produkt-ID der GME.</summary>
    public string ArticleNumbers { get; set; } = "";

    public string FileName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string? ImageUrl { get; set; }

    /// <summary>Service-Seite des Produkts (zum Nachlesen im Browser).</summary>
    public string PageUrl { get; set; } = "";

    /// <summary>Produkt-ID aus dem Kopf der GME (per HTTP-Range abgefragt), null wenn unbekannt.</summary>
    public uint? ProductId { get; set; }

    /// <summary>Datum im Kopf der GME (Stand der Datei).</summary>
    public DateTime? Version { get; set; }

    /// <summary>Größe der aktuellen Datei in Bytes.</summary>
    public long? Size { get; set; }
}

/// <summary>Gespeicherter Katalog mit dem Zeitpunkt des Abrufs.</summary>
public class CatalogSnapshot
{
    public DateTime LoadedAt { get; set; }
    public List<CatalogEntry> Entries { get; set; } = [];
}

/// <summary>Fehler beim Abruf des Katalogs oder beim Download, die Meldung ist für den Benutzer gedacht.</summary>
public class RavensburgerException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Katalog der offiziellen tiptoi-Audiodateien von Ravensburger.
/// <para>
/// Quelle: die öffentlichen Service-Seiten „tiptoi® Audiodateien“ (<see cref="IndexUrl"/>). Ravensburger bietet
/// dort jede GME-Datei zum freien Download an, gedacht für alle, die den „tiptoi Manager“ nicht nutzen wollen
/// oder können. Die Übersicht verlinkt je Produkt eine Seite mit Bild und Download-Link(s) auf
/// <c>ravensburger.cloud/…/db/applications/*.gme</c> (früher <c>cdn.ravensburger.de/db/applications/</c>, so auch
/// <c>testsuite/download.sh</c> in github.com/entropia/tip-toi-reveng).
/// </para>
/// <para>
/// Die Produkt-ID steht nur im Kopf der GME-Datei. Sie wird mit einer HTTP-Range-Anfrage über die ersten
/// 128 Bytes gelesen; die Antwort (Content-Range) liefert zugleich die Größe. So lassen sich Updates erkennen,
/// ohne die oft über 100 MB großen Dateien zu laden.
/// </para>
/// <para>
/// Nicht genutzt wird die JSON-Schnittstelle des tiptoi Managers (<c>ttapiv2.ravensburger.com/api/v2/catalog</c>,
/// beschrieben in github.com/Bouni/tiptoi-dl): Sie liefert ohne Anmeldung einen leeren Katalog und verlangt
/// ein OAuth-Token, das nur mit den im tiptoi Manager eingebauten Zugangsdaten zu bekommen ist.
/// </para>
/// <para>
/// Im Browser geht das alles nicht: Die Ravensburger-Server senden keine CORS-Kopfzeilen (Stand November 2026).
/// </para>
/// </summary>
public partial class RavensburgerCatalog(HttpClient http)
{
    /// <summary>Übersicht aller Audiodateien (Ravensburger-Serviceportal, MindTouch).</summary>
    public const string IndexUrl = "https://service.ravensburger.de/tiptoi%C2%AE/tiptoi%C2%AE_Audiodateien";

    /// <summary>Höchstens so viele Anfragen gleichzeitig, um die Server zu schonen.</summary>
    const int Parallel = 4;

    /// <summary>HttpClient mit Kennung des Programms; <paramref name="handler"/> für Tests.</summary>
    public static HttpClient CreateHttpClient(HttpMessageHandler? handler = null)
    {
        var http = handler != null ? new HttpClient(handler) : new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(10);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Tupfer-Designer", "1"));
        return http;
    }

    /// <summary>Lädt den ganzen Katalog: Übersicht, Produktseiten und die Köpfe der GME-Dateien.</summary>
    public async Task<List<CatalogEntry>> LoadAsync(IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        progress?.Report("Lade die Übersicht der Ravensburger-Audiodateien …");
        var index = await GetStringAsync(IndexUrl, cancel);
        var pages = ProductPages(index);
        if (pages.Count == 0)
            throw new RavensburgerException("Auf der Ravensburger-Seite wurden keine Produkte gefunden – vielleicht hat sie sich geändert.");

        var entries = new List<CatalogEntry>[pages.Count];
        int done = 0;
        await ForEachAsync(Enumerable.Range(0, pages.Count), async i =>
        {
            try
            {
                entries[i] = ParseProductPage(await GetStringAsync(pages[i].Url, cancel), pages[i].Url, pages[i].Category);
            }
            catch (RavensburgerException)
            {
                // Eine fehlende Produktseite soll nicht den ganzen Katalog verhindern
                entries[i] = [];
            }
            progress?.Report($"Produktseiten: {Interlocked.Increment(ref done)} von {pages.Count} …");
        }, cancel);

        var all = entries.SelectMany(e => e)
            .GroupBy(e => e.DownloadUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        done = 0;
        await ForEachAsync(all, async entry =>
        {
            await ProbeAsync(entry, cancel);
            progress?.Report($"Dateiköpfe: {Interlocked.Increment(ref done)} von {all.Count} …");
        }, cancel);
        return all.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static Task ForEachAsync<T>(IEnumerable<T> items, Func<T, Task> body, CancellationToken cancel) =>
        System.Threading.Tasks.Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = Parallel, CancellationToken = cancel },
            async (item, _) => await body(item));

    async Task<string> GetStringAsync(string url, CancellationToken cancel)
    {
        try
        {
            using var response = await http.GetAsync(url, cancel);
            if (!response.IsSuccessStatusCode)
                throw new RavensburgerException($"Ravensburger antwortet mit Fehler {(int)response.StatusCode} ({url}).");
            return await response.Content.ReadAsStringAsync(cancel);
        }
        catch (HttpRequestException e)
        {
            throw new RavensburgerException($"Keine Verbindung zu Ravensburger: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!cancel.IsCancellationRequested)
        {
            throw new RavensburgerException("Ravensburger antwortet nicht (Zeitüberschreitung).", e);
        }
    }

    /// <summary>Eine Produktseite mit ihrer Rubrik.</summary>
    public record ProductPage(string Url, string Category);

    [GeneratedRegex(@"href=""(https://service\.ravensburger\.de/tiptoi%C2%AE/tiptoi%C2%AE_Audiodateien/[^""#?]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex IndexLinkRegex();

    /// <summary>
    /// Produktseiten aus der Übersicht: alle Unterseiten, die selbst keine Unterseiten haben (die anderen sind
    /// Rubriken wie „Audiodateien tiptoi® Bücher“).
    /// </summary>
    public static List<ProductPage> ProductPages(string indexHtml)
    {
        var links = IndexLinkRegex().Matches(indexHtml).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value).TrimEnd('/'))
            .Distinct(StringComparer.Ordinal).ToList();
        var prefix = IndexUrl + "/";
        return links
            .Where(l => !links.Any(other => other.StartsWith(l + "/", StringComparison.Ordinal)))
            .Select(l =>
            {
                var parts = l[prefix.Length..].Split('/');
                var category = parts.Length > 1 ? CategoryName(Uri.UnescapeDataString(parts[0])) : "Sonstiges";
                return new ProductPage(l, category);
            })
            .ToList();
    }

    /// <summary>„Audiodateien_tiptoi®_Bücher“ → „Bücher“.</summary>
    static string CategoryName(string segment)
    {
        var name = segment.Replace('_', ' ');
        foreach (var prefix in new[] { "Audiodateien tiptoi® ", "Audiodateien tiptoi ", "Audiodateien " })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return name[prefix.Length..];
        return name;
    }

    [GeneratedRegex(@"<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("""<a\b[^>]*?href="([^"]+?\.gme)"[^>]*>(.*?)</a>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex GmeLinkRegex();

    [GeneratedRegex(@"<img\b[^>]*?src=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\b\d{5}\b")]
    private static partial Regex ArticleNumberRegex();

    /// <summary>Liest Titel, Bild und Download-Links einer Produktseite; eine Seite kann mehrere GME-Dateien haben.</summary>
    public static List<CatalogEntry> ParseProductPage(string html, string pageUrl, string category)
    {
        var title = TitleRegex().Match(html) is { Success: true } t ? WebUtility.HtmlDecode(t.Groups[1].Value).Trim() : "";
        title = Regex.Replace(title, @"\s+-\s+Serviceportal Ravensburger\s*$", "");
        title = Regex.Replace(title, @"^(tiptoi®?\s+)?Audiodatei(en)?\s+", "", RegexOptions.IgnoreCase).Trim();
        var articles = string.Join(", ", ArticleNumberRegex().Matches(title).Select(m => m.Value).Distinct());

        // Produktbild außerhalb der Links: das erste Bild aus den Dateien der Seite (ohne die Logos im Kopf)
        string? pageImage = ImageRegex().Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .FirstOrDefault(IsProductImage);

        var links = GmeLinkRegex().Matches(html)
            .Select(m => (Url: WebUtility.HtmlDecode(m.Groups[1].Value), Inner: m.Groups[2].Value))
            .Where(l => IsRavensburgerDownload(l.Url))
            .DistinctBy(l => l.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return links.Select(l =>
        {
            var fileName = Uri.UnescapeDataString(new Uri(l.Url).Segments[^1]);
            var image = ImageRegex().Match(l.Inner) is { Success: true } img ? WebUtility.HtmlDecode(img.Groups[1].Value) : pageImage;
            return new CatalogEntry
            {
                Title = links.Count > 1 ? $"{title} – {Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ')}" : title,
                Category = category,
                ArticleNumbers = articles,
                FileName = fileName,
                DownloadUrl = l.Url,
                ImageUrl = image != null && IsProductImage(image) ? image : pageImage,
                PageUrl = pageUrl,
            };
        }).ToList();
    }

    static bool IsProductImage(string url) =>
        url.Contains("/@api/deki/files/", StringComparison.Ordinal) && !url.Contains("/Logo_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nur Dateien, die Ravensburger selbst anbietet (https auf ravensburger.cloud bzw. ravensburger.de).</summary>
    public static bool IsRavensburgerDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("ravensburger.cloud", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".ravensburger.cloud", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".ravensburger.de", StringComparison.OrdinalIgnoreCase))
        && uri.AbsolutePath.EndsWith(".gme", StringComparison.OrdinalIgnoreCase);

    /// <summary>Liest Produkt-ID, Datum und Größe der GME per Range-Anfrage (nur die ersten 128 Bytes).</summary>
    public async Task ProbeAsync(CatalogEntry entry, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, entry.DownloadUrl);
            request.Headers.Range = new RangeHeaderValue(0, PenContents.HeaderLength - 1);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode)
                return;
            entry.Size = response.Content.Headers.ContentRange?.Length
                         ?? (response.StatusCode == HttpStatusCode.OK ? response.Content.Headers.ContentLength : null);
            await using var stream = await response.Content.ReadAsStreamAsync(cancel);
            var head = new byte[PenContents.HeaderLength];
            int read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancel);
            var header = PenContents.ParseHeader(head.AsSpan(0, read), entry.FileName, entry.Size ?? 0);
            entry.ProductId = header.ProductId;
            entry.Version = header.Date;
        }
        catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            // Ohne Kopf bleibt das Produkt im Katalog, nur ohne Produkt-ID und Updateprüfung
        }
    }

    /// <summary>
    /// Lädt die GME-Datei nach <paramref name="targetPath"/> und prüft, ob sie wirklich eine GME ist.
    /// <paramref name="progress"/> bekommt den Anteil 0 … 1 (wenn die Größe bekannt ist).
    /// </summary>
    public async Task DownloadAsync(CatalogEntry entry, string targetPath, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (!IsRavensburgerDownload(entry.DownloadUrl))
            throw new RavensburgerException($"„{entry.DownloadUrl}“ ist kein Download von Ravensburger.");
        try
        {
            using var response = await http.GetAsync(entry.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode)
                throw new RavensburgerException($"Download von „{entry.FileName}“ fehlgeschlagen: Ravensburger antwortet mit Fehler {(int)response.StatusCode}.");
            long? total = response.Content.Headers.ContentLength ?? entry.Size;
            await using (var input = await response.Content.ReadAsStreamAsync(cancel))
            await using (var output = File.Create(targetPath))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancel)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                    written += read;
                    if (total > 0)
                        progress?.Report((double)written / total.Value);
                }
                if (total > 0 && written != total)
                    throw new RavensburgerException($"Download von „{entry.FileName}“ unvollständig ({written} von {total} Bytes).");
            }
            if (PenContents.ReadHeader(targetPath, entry.FileName).Error != null)
                throw new RavensburgerException($"„{entry.FileName}“ von Ravensburger ist keine gültige GME-Datei.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            File.Delete(targetPath);
            throw new RavensburgerException(e is TaskCanceledException
                ? $"Download von „{entry.FileName}“ abgebrochen: Ravensburger antwortet nicht."
                : $"Download von „{entry.FileName}“ fehlgeschlagen: {e.Message}", e);
        }
        catch
        {
            File.Delete(targetPath);
            throw;
        }
    }

    /// <summary>Lädt ein Bild (Produktbild) als Bytes; null bei Fehlern.</summary>
    public async Task<byte[]?> GetImageAsync(string url, CancellationToken cancel = default)
    {
        try
        {
            return await http.GetByteArrayAsync(url, cancel);
        }
        catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            return null;
        }
    }

    // ---------- Zwischenspeicher ----------

    /// <summary>Datei für den zuletzt geladenen Katalog (Desktop); null = nicht speichern (Browser).</summary>
    public static string? CachePath { get; set; } = OperatingSystem.IsBrowser()
        ? null
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TupferDesigner", "ravensburger-katalog.json");

    public static CatalogSnapshot? LoadCache()
    {
        try
        {
            return CachePath != null && File.Exists(CachePath)
                ? JsonSerializer.Deserialize<CatalogSnapshot>(File.ReadAllText(CachePath))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void SaveCache(CatalogSnapshot snapshot)
    {
        if (CachePath == null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Dann wird beim nächsten Mal neu geladen
        }
    }
}
