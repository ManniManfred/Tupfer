using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tupfer.Designer.Services;

// Antworten der Immich-API (nur die gebrauchten Felder; Namen wie in api.immich.app, Stand 3.4)

public sealed record ImmichAlbum(string Id, string AlbumName, string? AlbumThumbnailAssetId, int AssetCount);

public sealed record ImmichPerson(string Id, string Name, bool IsHidden);

public sealed record ImmichAsset(string Id, string? Type, string? OriginalFileName, string? OriginalMimeType, bool? IsEdited);

/// <summary>Ein Gesicht: Rahmen in Pixeln eines Bilds von <see cref="ImageWidth"/> × <see cref="ImageHeight"/>, Person evtl. ohne Namen oder null.</summary>
public sealed record ImmichFace(string Id, int ImageWidth, int ImageHeight,
    int BoundingBoxX1, int BoundingBoxY1, int BoundingBoxX2, int BoundingBoxY2, ImmichPerson? Person);

/// <summary>Seite einer Suche: Bilder und die nächste Seite (null = keine weitere).</summary>
public sealed record ImmichAssetPage(List<ImmichAsset> Items, int? NextPage);

/// <summary>Heruntergeladenes Bild: Inhalt und Dateiname mit passender Endung.</summary>
public sealed record ImmichDownload(byte[] Data, string FileName);

/// <summary>
/// Weiterleitung zu Immich (in der Nextcloud-App): <paramref name="ApiUrl"/> steht für …/api/ von Immich,
/// <paramref name="ServerUrl"/> ist der dort eingetragene Server (leer = keiner), <paramref name="RequestToken"/>
/// liefert das aktuelle CSRF-Token für den Header „requesttoken“.
/// </summary>
public sealed record ImmichProxy(string ApiUrl, string ServerUrl, Func<string> RequestToken);

/// <summary>Fehler beim Zugriff auf Immich mit einer verständlichen Meldung.</summary>
public class ImmichException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Zugriff auf einen Immich-Server (selbst gehosteter Fotodienst) über seine REST-API mit API-Key im Header
/// <c>x-api-key</c>. Gebraucht werden Alben, Personen, die Suche nach Bildern, Vorschauen, Originale und
/// Gesichter; der Key braucht dafür die Rechte user.read (Prüfen der Verbindung), album.read, person.read,
/// asset.read, asset.view, asset.download und face.read.
/// </summary>
public sealed class ImmichClient : IDisposable
{
    /// <summary>Im Browser: Zugriffe scheitern ohne CORS-Freigabe des Servers mit „Failed to fetch“.</summary>
    public static bool IsBrowser { get; set; } = OperatingSystem.IsBrowser();

    /// <summary>Im Browser: Die Seite selbst kommt über https (dann blockiert der Browser http-Adressen).</summary>
    public static bool PageIsSecure { get; set; }

    /// <summary>
    /// In der Nextcloud-App: Zugriffe laufen über den Nextcloud-Server (kein CORS nötig), und zwar nur zu dem dort
    /// eingetragenen Server; die Adresse in den Einstellungen gilt dann nicht. null = direkt zu Immich.
    /// </summary>
    public static ImmichProxy? Proxy { get; set; }

    /// <summary>Die Adresse des Servers ist fest vorgegeben (Nextcloud) und lässt sich nicht ändern.</summary>
    public static bool HasFixedServer => Proxy != null;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    readonly HttpClient _http;

    /// <summary>Adresse des Servers ohne „/api“, z. B. „https://fotos.example.org“.</summary>
    public string ServerUrl { get; }

    /// <param name="handler">Für Tests ein gefälschter Handler (wird nicht freigegeben), sonst der Standard.</param>
    public ImmichClient(string serverUrl, string apiKey, HttpMessageHandler? handler = null)
    {
        var proxy = Proxy;
        if (proxy != null && proxy.ServerUrl.Length == 0)
            throw new ImmichException("In dieser Nextcloud ist kein Immich-Server eingetragen. Der Administrator trägt ihn ein mit: " +
                                      "occ config:app:set tupfer immich_url --value=https://fotos.example.org");
        ServerUrl = NormalizeUrl(proxy?.ServerUrl ?? serverUrl);
        if (ServerUrl.Length == 0)
            throw new ImmichException("Bitte zuerst die Adresse des Immich-Servers eingeben.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ImmichException("Bitte zuerst einen API-Key eingeben (in Immich unter Kontoeinstellungen → API-Schlüssel).");
        if (proxy == null && IsBrowser && PageIsSecure && ServerUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            throw new ImmichException("Die Web-Version kommt über https; der Browser erlaubt dann keine Zugriffe auf " +
                                      $"{ServerUrl}. Bitte die https-Adresse des Immich-Servers eingeben.");
        _proxy = proxy;
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(proxy == null ? ServerUrl + "/api/" : proxy.ApiUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey.Trim());
    }

    readonly ImmichProxy? _proxy;

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Adresse ohne Leerzeichen, abschließenden Schrägstrich und „/api“; ohne Schema mit https://.
    /// Leer, wenn keine Adresse eingegeben ist; Fehler, wenn sie keine gültige http(s)-Adresse ist.
    /// </summary>
    public static string NormalizeUrl(string? url)
    {
        var text = (url ?? "").Trim().TrimEnd('/');
        if (text.Length == 0)
            return "";
        if (!text.Contains("://"))
            text = "https://" + text;
        if (text.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            text = text[..^4].TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
            throw new ImmichException($"„{url}“ ist keine gültige Adresse eines Immich-Servers (z. B. https://fotos.example.org).");
        return text;
    }

    /// <summary>Prüft Adresse und Key; liefert den Namen des Kontos.</summary>
    public async Task<string> CheckAsync()
    {
        var user = await GetJsonAsync<JsonElement>("users/me", "die Anmeldung");
        return user.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } n ? n
            : user.TryGetProperty("email", out var mail) ? mail.GetString() ?? "" : "";
    }

    /// <summary>Alle Alben (eigene und geteilte), nach Namen sortiert.</summary>
    public async Task<List<ImmichAlbum>> GetAlbumsAsync()
    {
        var albums = await GetJsonAsync<List<ImmichAlbum>>("albums", "den Alben");
        return albums.OrderBy(a => a.AlbumName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Alle sichtbaren Personen mit Namen, nach Namen sortiert (Personen ohne Namen gibt es in Immich oft sehr viele).</summary>
    public async Task<List<ImmichPerson>> GetPeopleAsync()
    {
        var people = new List<ImmichPerson>();
        for (int page = 1; page <= 100; page++)
        {
            var response = await GetJsonAsync<PeopleResponse>($"people?withHidden=false&size=500&page={page}", "den Personen");
            people.AddRange(response.People);
            if (response.HasNextPage != true || response.People.Count == 0)
                break;
        }
        return people.Where(p => !p.IsHidden && !string.IsNullOrWhiteSpace(p.Name))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    sealed record PeopleResponse(List<ImmichPerson> People, bool? HasNextPage);

    /// <summary>Fotos eines Albums oder einer Person (neueste zuerst), seitenweise.</summary>
    public async Task<ImmichAssetPage> SearchAssetsAsync(string? albumId, string? personId, int page = 1, int size = 100)
    {
        // albumIds/personIds gelten ab Immich 3.2 als veraltet, gehen aber in allen Versionen (die neue Form erst ab 3.2)
        var query = new SearchRequest(albumId == null ? null : [albumId], personId == null ? null : [personId], "IMAGE", page, size);
        var response = await SendJsonAsync<SearchResponse>(HttpMethod.Post, "search/metadata", query, "der Suche nach Fotos");
        return new ImmichAssetPage(response.Assets.Items.Where(a => a.Type is null or "IMAGE").ToList(),
            int.TryParse(response.Assets.NextPage, out var next) ? next : null);
    }

    sealed record SearchRequest(string[]? AlbumIds, string[]? PersonIds, string Type, int Page, int Size);
    sealed record SearchResponse(SearchAssets Assets);
    sealed record SearchAssets(List<ImmichAsset> Items, string? NextPage);

    public Task<ImmichAsset> GetAssetAsync(string assetId) =>
        GetJsonAsync<ImmichAsset>($"assets/{Uri.EscapeDataString(assetId)}", "dem Foto");

    /// <summary>Kleines Vorschaubild eines Fotos (für die Auswahl).</summary>
    public Task<byte[]> GetThumbnailAsync(string assetId) =>
        GetBytesAsync($"assets/{Uri.EscapeDataString(assetId)}/thumbnail?size=thumbnail", "dem Vorschaubild");

    /// <summary>Vorschaubild einer Person (ihr Gesicht).</summary>
    public Task<byte[]> GetPersonThumbnailAsync(string personId) =>
        GetBytesAsync($"people/{Uri.EscapeDataString(personId)}/thumbnail", "dem Bild der Person");

    /// <summary>
    /// Gesichter auf einem Foto. Die Rahmen gelten für das richtig gedrehte (und in Immich bearbeitete) Bild,
    /// siehe <see cref="ImmichFaces"/>.
    /// </summary>
    public Task<List<ImmichFace>> GetFacesAsync(string assetId) =>
        GetJsonAsync<List<ImmichFace>>($"faces?id={Uri.EscapeDataString(assetId)}", "den Gesichtern");

    /// <summary>
    /// Lädt ein Foto herunter: das Original (bei in Immich bearbeiteten Fotos die bearbeitete Fassung) oder die
    /// große Vorschau. Originale, die sich hier nicht anzeigen lassen (z. B. HEIC oder RAW), werden durch die
    /// Vorschau ersetzt; <paramref name="log"/> meldet das.
    /// </summary>
    public async Task<ImmichDownload> DownloadAsync(ImmichAsset asset, bool original, Action<string>? log = null)
    {
        var id = Uri.EscapeDataString(asset.Id);
        // „edited“ kennt Immich erst ab 2.5 (dort gibt es auch isEdited); die Gesichter gelten für die bearbeitete Fassung
        var edited = asset.IsEdited == true ? "edited=true" : null;
        var stem = Path.GetFileNameWithoutExtension(asset.OriginalFileName ?? "");
        if (string.IsNullOrWhiteSpace(stem))
            stem = "immich-" + asset.Id[..Math.Min(8, asset.Id.Length)];
        if (original)
        {
            var data = await GetBytesAsync($"assets/{id}/original" + (edited == null ? "" : "?" + edited), "dem Original");
            if (ImageExtension(data) is { } ext)
                return new ImmichDownload(data, stem + ext);
            log?.Invoke($"„{asset.OriginalFileName}“ lässt sich als Original hier nicht anzeigen, es wird die große Vorschau genommen.");
        }
        var preview = await GetBytesAsync($"assets/{id}/thumbnail?size=preview" + (edited == null ? "" : "&" + edited), "der Vorschau");
        return new ImmichDownload(preview, stem + (ImageExtension(preview)
            ?? throw new ImmichException($"Immich hat für „{asset.OriginalFileName}“ kein lesbares Bild geliefert.")));
    }

    /// <summary>Endung zu den ersten Bytes eines Bilds (JPEG, PNG, WebP, GIF, BMP), null für andere Formate.</summary>
    public static string? ImageExtension(ReadOnlySpan<byte> data) => data switch
    {
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => ".png",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => ".webp",
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => ".gif",
        [(byte)'B', (byte)'M', ..] => ".bmp",
        _ => null,
    };

    // ---------- HTTP und Fehler ----------

    Task<T> GetJsonAsync<T>(string path, string what) => SendJsonAsync<T>(HttpMethod.Get, path, null, what);

    async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body, string what)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await SendAsync(request, what);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json)
                   ?? throw new JsonException("leere Antwort");
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            throw new ImmichException($"Unter {ServerUrl} antwortet kein Immich-Server (unerwartete Antwort bei {what}). Stimmt die Adresse?", e);
        }
    }

    async Task<byte[]> GetBytesAsync(string path, string what)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, what);
        return await response.Content.ReadAsByteArrayAsync();
    }

    async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string what)
    {
        // Nextcloud verlangt bei Anfragen an Apps das CSRF-Token
        if (_proxy != null)
            request.Headers.TryAddWithoutValidation("requesttoken", _proxy.RequestToken());
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (TaskCanceledException e)
        {
            throw new ImmichException($"Der Immich-Server unter {ServerUrl} antwortet nicht (Zeitüberschreitung bei {what}).", e);
        }
        catch (HttpRequestException e)
        {
            throw new ImmichException(_proxy != null
                ? $"Der Nextcloud-Server ist nicht erreichbar: {e.Message}"
                : IsBrowser
                ? $"Der Immich-Server unter {ServerUrl} ist nicht erreichbar. Im Browser liegt das meist daran, dass Immich keine " +
                  "Zugriffe von anderen Seiten (CORS) erlaubt; dann geht es nur mit einer Freigabe im Webserver vor Immich " +
                  "oder in der Desktop-Version (siehe README)."
                : $"Der Immich-Server unter {ServerUrl} ist nicht erreichbar: {e.Message}", e);
        }
        if (response.IsSuccessStatusCode)
            return response;
        using (response)
        {
            var detail = await ErrorMessageAsync(response);
            throw new ImmichException(response.StatusCode switch
            {
                // Nextcloud selbst: abgemeldet oder CSRF-Token abgelaufen
                HttpStatusCode.Unauthorized or HttpStatusCode.PreconditionFailed when _proxy != null && !detail.Contains("API key", StringComparison.OrdinalIgnoreCase)
                                                                                    && (response.StatusCode == HttpStatusCode.PreconditionFailed || detail.Contains("logged in", StringComparison.OrdinalIgnoreCase)) =>
                    "Die Anmeldung bei Nextcloud ist abgelaufen. Bitte die Seite neu laden.",
                HttpStatusCode.Unauthorized => "Immich hat den API-Key abgelehnt. Bitte den Key in den Immich-Einstellungen prüfen.",
                HttpStatusCode.Forbidden => $"Der API-Key darf das nicht ({detail}). Er braucht in Immich die Rechte user.read, album.read, " +
                                            "person.read, asset.read, asset.view, asset.download und face.read.",
                HttpStatusCode.NotFound => $"Immich kennt {what} nicht (404). Stimmt die Adresse {ServerUrl}?",
                // Fehler der Weiterleitung in Nextcloud (Server nicht erreichbar oder nicht eingetragen)
                HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout when _proxy != null =>
                    $"{detail} ({ServerUrl})",
                _ => $"Immich meldet einen Fehler bei {what}: {(int)response.StatusCode} {detail}",
            });
        }
    }

    /// <summary>Meldung aus der Fehlerantwort von Immich ({"message": …}), sonst der Statustext.</summary>
    static async Task<string> ErrorMessageAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("message", out var message))
                return message.ValueKind == JsonValueKind.Array
                    ? string.Join("; ", message.EnumerateArray().Select(m => m.ToString()))
                    : message.ToString();
        }
        catch (Exception e) when (e is JsonException or HttpRequestException)
        {
            // Keine JSON-Antwort
        }
        return response.ReasonPhrase ?? "";
    }
}
