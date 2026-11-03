using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tupfer.Designer.Services;

/// <summary>Neueres Release auf GitHub mit dem Windows-Setup und (falls angegeben) dessen SHA-256-Prüfsumme.</summary>
public sealed record UpdateRelease(Version Version, string SetupUrl, string? Sha256);

/// <summary>
/// Automatische Aktualisierung der Windows-Version: sucht auf GitHub nach dem neuesten Release, lädt dessen
/// Setup herunter und startet es ohne Rückfragen. Das Setup startet den Tupfer Designer danach wieder
/// (Parameter /NEUSTART=1, siehe deploy/setup/windows.iss).
/// </summary>
public static class Updater
{
    const string Repo = "ManniManfred/Tupfer";
    public const string SetupAsset = "Tupfer-Designer-Setup-Windows-x64.exe";

    static readonly HttpClient Http = CreateClient();

    /// <summary>Nur unter Windows und nur, wenn mit dem Setup installiert (das Deinstallationsprogramm liegt daneben).</summary>
    public static bool IsAvailable { get; } =
        OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        // Ohne User-Agent lehnt die GitHub-API ab
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Tupfer-Designer", "1"));
        return http;
    }

    /// <summary>Das neueste Release, wenn es neuer als <paramref name="current"/> ist und ein Windows-Setup hat; sonst null.</summary>
    public static async Task<UpdateRelease?> FindNewerAsync(string current, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        return ParseRelease(json.RootElement, current);
    }

    /// <summary>Wertet die Antwort von „releases/latest“ aus (siehe <see cref="FindNewerAsync"/>).</summary>
    public static UpdateRelease? ParseRelease(JsonElement release, string current)
    {
        if (!Version.TryParse(current, out var installed)
            || release.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()
            || !Version.TryParse(release.GetProperty("tag_name").GetString()?.TrimStart('v', 'V'), out var latest)
            || Normalize(latest) <= Normalize(installed))
            return null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != SetupAsset)
                continue;
            // GitHub gibt die Prüfsumme als „sha256:…“ an (ältere Releases ohne)
            string? sha = asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d
                          && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? d["sha256:".Length..].ToLowerInvariant()
                : null;
            return new UpdateRelease(latest, asset.GetProperty("browser_download_url").GetString()!, sha);
        }
        return null;
    }

    /// <summary>1.2 und 1.2.0 sind gleich (fehlende Stellen zählen als 0).</summary>
    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>Lädt das Setup in ein temporäres Verzeichnis und prüft die Prüfsumme; liefert den Pfad.</summary>
    public static async Task<string> DownloadAsync(UpdateRelease release, CancellationToken cancel = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "TupferDesigner-Update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, SetupAsset);
        using (var response = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(path);
            await response.Content.CopyToAsync(file, cancel);
        }
        if (release.Sha256 != null)
        {
            await using var file = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancel));
            if (hash != release.Sha256)
            {
                File.Delete(path);
                throw new InvalidDataException("Die Prüfsumme des heruntergeladenen Setups stimmt nicht.");
            }
        }
        return path;
    }

    /// <summary>
    /// Startet das Setup ohne Assistent (nur mit Fortschrittsanzeige); danach muss sich das Programm beenden,
    /// damit das Setup die Dateien ersetzen kann.
    /// </summary>
    public static void StartSetup(string path) =>
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /NEUSTART=1") { UseShellExecute = true })?.Dispose();
}
