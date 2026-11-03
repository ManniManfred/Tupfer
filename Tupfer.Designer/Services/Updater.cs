using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tupfer.Designer.Services;

/// <summary>Neueres Release auf GitHub mit der passenden Datei (Setup bzw. AppImage) und (falls angegeben) deren SHA-256-Prüfsumme.</summary>
public sealed record UpdateRelease(Version Version, string Url, string? Sha256);

/// <summary>
/// Automatische Aktualisierung über die GitHub-Releases:
/// <list type="bullet">
/// <item>Windows (mit dem Setup installiert): Das Setup wird geladen und ohne Rückfragen gestartet; es startet
/// den Tupfer Designer danach wieder (Parameter /NEUSTART=1, siehe deploy/setup/windows.iss).</item>
/// <item>Linux als AppImage: Das neue AppImage ersetzt die Datei, aus der das Programm gestartet wurde, und wird
/// gestartet, sobald dieses beendet ist.</item>
/// </list>
/// </summary>
public static class Updater
{
    const string Repo = "ManniManfred/Tupfer";
    public const string SetupAsset = "Tupfer-Designer-Setup-Windows-x64.exe";
    public const string AppImageAsset = "Tupfer-Designer-Linux-x64.AppImage";

    static readonly HttpClient Http = CreateClient();

    static readonly bool IsWindowsSetup =
        OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    /// <summary>Pfad des laufenden AppImages (setzt dessen Laufzeit in APPIMAGE), sonst null.</summary>
    static readonly string? AppImage =
        OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } p && File.Exists(p) ? p : null;

    /// <summary>Nur in der mit dem Setup installierten Windows-Version und im AppImage unter Linux.</summary>
    public static bool IsAvailable => IsWindowsSetup || AppImage != null;

    static string Asset => AppImage != null ? AppImageAsset : SetupAsset;

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        // Ohne User-Agent lehnt die GitHub-API ab
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Tupfer-Designer", "1"));
        return http;
    }

    /// <summary>Das neueste Release, wenn es neuer als <paramref name="current"/> ist und die passende Datei hat; sonst null.</summary>
    public static async Task<UpdateRelease?> FindNewerAsync(string current, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        return ParseRelease(json.RootElement, current, Asset);
    }

    /// <summary>Wertet die Antwort von „releases/latest“ aus (siehe <see cref="FindNewerAsync"/>).</summary>
    public static UpdateRelease? ParseRelease(JsonElement release, string current, string assetName)
    {
        if (!Version.TryParse(current, out var installed)
            || release.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()
            || !Version.TryParse(release.GetProperty("tag_name").GetString()?.TrimStart('v', 'V'), out var latest)
            || Normalize(latest) <= Normalize(installed))
            return null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != assetName)
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

    /// <summary>
    /// Lädt die Datei und prüft die Prüfsumme; liefert den Pfad. Das Setup kommt in ein temporäres Verzeichnis,
    /// das AppImage neben das laufende (damit es sich danach ohne Kopieren an dessen Stelle setzen lässt).
    /// </summary>
    public static async Task<string> DownloadAsync(UpdateRelease release, CancellationToken cancel = default)
    {
        string path;
        if (AppImage != null)
            path = AppImage + ".neu";
        else
        {
            var dir = Path.Combine(Path.GetTempPath(), "TupferDesigner-Update");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, SetupAsset);
        }
        try
        {
            using (var response = await Http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, cancel))
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
                    throw new InvalidDataException("Die Prüfsumme der heruntergeladenen Datei stimmt nicht.");
            }
            return path;
        }
        catch (UnauthorizedAccessException) when (AppImage != null)
        {
            throw new UnauthorizedAccessException(
                $"Keine Schreibrechte im Ordner {Path.GetDirectoryName(AppImage)}. Die neue Version bitte von der Download-Seite laden.");
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Installiert die geladene Datei und sorgt für den Neustart; danach muss sich das Programm beenden
    /// (das Setup ersetzt erst dann die Dateien, das neue AppImage startet erst dann).
    /// </summary>
    public static void Install(string path)
    {
        if (AppImage == null)
        {
            // Ohne Assistent, nur mit Fortschrittsanzeige
            Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /NEUSTART=1") { UseShellExecute = true })?.Dispose();
            return;
        }
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, File.GetUnixFileMode(AppImage) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        // Das laufende Abbild bleibt eingehängt, die Datei lässt sich trotzdem ersetzen
        File.Move(path, AppImage, overwrite: true);
        // Erst starten, wenn dieses Programm beendet ist: Es gibt vorher sein Arbeitsverzeichnis nicht frei
        Process.Start(new ProcessStartInfo("/bin/sh",
            ["-c", "while kill -0 \"$1\" 2>/dev/null; do sleep 0.2; done; exec \"$0\"", AppImage, Environment.ProcessId.ToString()])
        {
            UseShellExecute = false,
        })?.Dispose();
    }
}
