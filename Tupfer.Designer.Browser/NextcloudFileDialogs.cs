using System.Security.Cryptography;
using System.Text.Json;
using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer;

/// <summary>
/// Dateidialoge in der Nextcloud-App: Projekte, Bilder und Audiodateien werden mit der Dateiauswahl von Nextcloud
/// gewählt und in das Arbeitsverzeichnis geladen, beim Speichern wird zurück nach Nextcloud geschrieben.
/// Ein aus Nextcloud geöffnetes Projekt liegt im Arbeitsverzeichnis unter demselben Pfad wie in Nextcloud
/// (/tupfer/projekt3/Tiptoi/buch/buch.tupfer.json für /Tiptoi/buch/buch.tupfer.json), damit relative Pfade
/// wie ../bilder/… stimmen. Drucken und „Auf den Stift“ laufen wie im Browser.
/// </summary>
class NextcloudFileDialogs(BrowserFileDialogs browser) : IFileDialogs
{
    /// <summary>Je Arbeitsverzeichnis: welches lokale Verzeichnis welchem Nextcloud-Ordner entspricht.</summary>
    static readonly Dictionary<string, Anchor> Anchors = new();

    /// <summary>Bekannter Inhalt in Nextcloud (Pfad → SHA-256), damit nur Geändertes hochgeladen wird.</summary>
    static readonly Dictionary<string, string> Known = new();

    record Anchor(string Local, string Remote);

    /// <summary>Lädt die gemerkten Zuordnungen (für den wiederhergestellten Bearbeitungsstand).</summary>
    public static void Init()
    {
        try
        {
            var json = NextcloudInterop.LoadState();
            if (json.Length > 0 && JsonSerializer.Deserialize<Dictionary<string, Anchor>>(json) is { } saved)
                foreach (var (dir, anchor) in saved)
                    Anchors[dir] = anchor;
        }
        catch (JsonException)
        {
        }
        // Neue Arbeitsverzeichnisse (neues Projekt, Beispiel) gehören noch zu keinem Ordner
        BrowserFileDialogs.WorkDirCreated += dir =>
        {
            if (Anchors.Remove(dir))
                SaveAnchors();
        };
    }

    static void SaveAnchors() => NextcloudInterop.SaveState(JsonSerializer.Serialize(Anchors));

    static void SetAnchor(string workDir, Anchor anchor)
    {
        Anchors[workDir] = anchor;
        SaveAnchors();
    }

    // ---------- Pfade ----------

    /// <summary>Nextcloud-Pfad zu einer lokalen Datei, oder null, wenn ihr Projekt (noch) nicht in Nextcloud liegt.</summary>
    static string? RemotePath(string local)
    {
        if (!Anchors.TryGetValue(BrowserFileDialogs.WorkDirOf(local), out var anchor))
            return null;
        return Normalize(anchor.Remote + "/" + Path.GetRelativePath(anchor.Local, local));
    }

    /// <summary>Lokaler Pfad zu einer Nextcloud-Datei im Arbeitsverzeichnis, oder null, wenn sie außerhalb läge.</summary>
    static string? LocalPath(string workDir, string remote)
    {
        if (!Anchors.TryGetValue(workDir, out var anchor))
            return null;
        var local = Path.GetFullPath(Path.Combine(anchor.Local, Path.GetRelativePath(anchor.Remote, remote)));
        return Path.GetRelativePath(workDir, local).StartsWith("..") ? null : local;
    }

    /// <summary>„/a/./b/../c“ → „/a/c“; null, wenn der Pfad über den obersten Ordner hinausführt.</summary>
    public static string? Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part is "" or ".")
                continue;
            if (part == "..")
            {
                if (parts.Count == 0)
                    return null;
                parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                parts.Add(part);
            }
        }
        return "/" + string.Join('/', parts);
    }

    static string RemoteParent(string remote) => Normalize(remote + "/..") ?? "/";

    static string RemoteName(string remote) => remote[(remote.LastIndexOf('/') + 1)..];

    /// <summary>Ordner, in dem die Dateiauswahl beginnt: der des aktuellen Projekts, sonst der oberste.</summary>
    static string StartFolder() => RemotePath(Environment.CurrentDirectory) ?? "/";

    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    // ---------- Laden ----------

    /// <summary>Lädt die Datei an <paramref name="local"/>; false, wenn es sie in Nextcloud nicht gibt.</summary>
    static async Task<bool> DownloadAsync(string remote, string local)
    {
        if (await NextcloudInterop.ReadAsync(remote) is not { } base64)
            return false;
        var data = Convert.FromBase64String(base64);
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        await File.WriteAllBytesAsync(local, data);
        Known[remote] = Hash(data);
        return true;
    }

    /// <summary>
    /// Öffnet ein Projekt (oder eine tttool-YAML-Datei) aus Nextcloud in einem neuen Arbeitsverzeichnis,
    /// zusammen mit den Bildern, Aufnahmen und Audiodateien, auf die es verweist. Liefert den lokalen Pfad.
    /// </summary>
    public static async Task<string> OpenProjectAsync(string remote)
    {
        var previous = Environment.CurrentDirectory;
        try
        {
            return await LoadProjectAsync(remote);
        }
        catch
        {
            // Das bisherige Projekt bleibt offen, neue Bilder sollen weiter dort landen
            Environment.CurrentDirectory = previous;
            throw;
        }
    }

    static async Task<string> LoadProjectAsync(string remote)
    {
        var workDir = BrowserFileDialogs.NewWorkDir();
        SetAnchor(workDir, new Anchor(workDir, "/"));
        var local = LocalPath(workDir, remote)!;
        if (!await DownloadAsync(remote, local))
            throw new FileNotFoundException($"„{remote}“ gibt es in Nextcloud nicht (mehr).");

        var remoteDir = RemoteParent(remote);
        var references = new List<string>();
        ProjectFile file;
        if (local.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
        {
            file = ProjectStore.Load(local);
            references.AddRange(file.Pages.SelectMany(p => p.Elements).Select(e => e.ImagePath));
            references.AddRange(file.Pages.Select(p => p.BackgroundImage ?? ""));   // ältere Projekte
            references.AddRange(file.Speak.Select(s => s.Recording ?? ""));
        }
        else
        {
            var codes = TttoolYaml.CodesPathFor(local);
            await DownloadAsync(RemotePath(codes)!, codes);
            file = TttoolYaml.Import(local);
        }

        // Audiodateien im Medienverzeichnis (z. B. „audio/%s“)
        var pattern = string.IsNullOrWhiteSpace(file.MediaPath) ? "%s" : file.MediaPath;
        var mediaDir = Path.GetDirectoryName(pattern) ?? "";
        if (!mediaDir.Contains('%') && !Path.IsPathRooted(mediaDir) && Normalize(remoteDir + "/" + mediaDir) is { } remoteMedia)
        {
            var names = JsonSerializer.Deserialize<string[]>(await NextcloudInterop.ListAsync(remoteMedia)) ?? [];
            references.AddRange(names
                .Where(n => GmeBuilder.MediaExtensions.Contains(Path.GetExtension(n).ToLowerInvariant()))
                .Select(n => Path.Combine(mediaDir, n)));
        }

        foreach (var reference in references.Where(r => r.Length > 0 && !Path.IsPathRooted(r)).Distinct())
        {
            // Fehlende Dateien zeigt das Projekt selbst an
            if (Normalize(remoteDir + "/" + reference) is { } path && LocalPath(workDir, path) is { } target)
                await DownloadAsync(path, target);
        }

        Environment.CurrentDirectory = Path.GetDirectoryName(local)!;
        return local;
    }

    /// <summary>
    /// Legt ein neues Projekt (Inhalt wie „Datei → Neu“) in Nextcloud an und öffnet es. Gibt es die Datei
    /// schon, wird sie nur geöffnet.
    /// </summary>
    public static async Task<string> CreateProjectAsync(string remote)
    {
        if (await NextcloudInterop.ExistsAsync(remote))
            return await OpenProjectAsync(remote);
        var workDir = BrowserFileDialogs.NewWorkDir();
        SetAnchor(workDir, new Anchor(workDir, "/"));
        var local = LocalPath(workDir, remote)!;
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        Environment.CurrentDirectory = Path.GetDirectoryName(local)!;
        ProjectStore.Save(MainWindowViewModel.DefaultProjectFile(), local);
        await UploadAsync(local, remote, always: true);
        NextcloudInterop.Notify($"In Nextcloud angelegt: {remote}");
        return local;
    }

    public async Task<string?> OpenAsync(string title, string filterName, params string[] patterns)
    {
        var endings = patterns.Select(p => p.TrimStart('*')).ToList();
        var remote = await NextcloudInterop.PickAsync(title, string.Join(',', endings), false, StartFolder());
        if (remote.Length == 0)
            return null;
        if (!endings.Any(e => remote.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"„{RemoteName(remote)}“ ist keine passende Datei ({string.Join(", ", endings)}).");
        if (patterns.Any(BrowserFileDialogs.IsProjectPattern))
            return await OpenProjectAsync(remote);

        // Bild, SVG oder Audiodatei: an ihre Stelle im Arbeitsverzeichnis, sonst als Kopie ins aktuelle Verzeichnis
        var current = Environment.CurrentDirectory;
        var local = LocalPath(BrowserFileDialogs.WorkDirOf(Path.Combine(current, "x")), remote);
        if (local == null || File.Exists(local) && !Known.ContainsKey(remote))
            local = WorkFiles.UniquePath(current, RemoteName(remote));
        if (!await DownloadAsync(remote, local))
            throw new FileNotFoundException($"„{remote}“ gibt es in Nextcloud nicht (mehr).");
        return local;
    }

    // ---------- Speichern ----------

    public async Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension)
    {
        if (!suggestedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            suggestedName += extension;
        var current = Environment.CurrentDirectory;
        if (extension != ProjectStore.Extension)
        {
            // Exporte neben das Projekt in Nextcloud; liegt es noch nicht dort, wie im Browser herunterladen
            return RemotePath(current) != null
                ? Path.Combine(current, suggestedName)
                : await browser.SaveAsync(title, suggestedName, filterName, extension);
        }

        if (await PickTargetAsync(title, ProjectStore.BaseName(suggestedName), askName: true) is not { } remote)
            return null;
        var workDir = BrowserFileDialogs.WorkDirOf(Path.Combine(current, "x"));
        // Im selben Arbeitsverzeichnis bleiben, damit Bilder und Aufnahmen weiter gefunden werden
        var local = LocalPath(workDir, remote);
        if (local == null)
        {
            local = Path.Combine(current, RemoteName(remote));
            SetAnchor(workDir, new Anchor(current, RemoteParent(remote)));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        Environment.CurrentDirectory = Path.GetDirectoryName(local)!;
        return local;
    }

    /// <summary>
    /// Fragt nach dem Ordner in Nextcloud und, wenn <paramref name="askName"/>, nach dem Namen des Projekts
    /// (vorbelegt mit <paramref name="name"/>, ohne Endung). Liefert den Pfad, null bei Abbruch.
    /// </summary>
    static async Task<string?> PickTargetAsync(string title, string name, bool askName)
    {
        var folder = await NextcloudInterop.PickAsync(title + ": Ordner wählen", "", true, StartFolder());
        if (folder.Length == 0)
            return null;
        if (askName)
        {
            name = NextcloudInterop.AskName($"Name des Projekts in {folder}:", name);
            if (name.Length == 0)
                return null;
        }
        if (name.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
            name = name[..^ProjectStore.Extension.Length];
        name = string.Concat(name.Select(c => c is '/' or '\\' ? '_' : c)) + ProjectStore.Extension;
        var remote = Normalize(folder + "/" + name)!;
        if (await NextcloudInterop.ExistsAsync(remote) && !NextcloudInterop.Confirm($"„{remote}“ gibt es schon. Überschreiben?"))
            return null;
        return remote;
    }

    public async Task SavedAsync(params string[] paths)
    {
        var uploaded = new List<string>();
        foreach (var path in paths.Where(File.Exists))
        {
            var remote = RemotePath(path);
            bool project = path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase);
            if (remote == null && project)
            {
                // Projekt, das noch nicht in Nextcloud liegt (z. B. ein Beispiel): Ordner wählen, der Name bleibt
                remote = await PickTargetAsync("Projekt in Nextcloud speichern", Path.GetFileName(path), askName: false);
                if (remote == null)
                    continue;
                SetAnchor(BrowserFileDialogs.WorkDirOf(path), new Anchor(Path.GetDirectoryName(path)!, RemoteParent(remote)));
            }
            if (remote == null)
            {
                await browser.SavedAsync(path);
                continue;
            }

            if (project)
            {
                // Erst Bilder und Audiodateien, dann das Projekt, das auf sie verweist
                foreach (var media in BrowserFileDialogs.MediaFiles(BrowserFileDialogs.WorkDirOf(path)))
                    if (RemotePath(media) is { } target && await UploadAsync(media, target))
                        uploaded.Add(target);
                await UploadAsync(path, remote, always: true);
            }
            else
            {
                await UploadAsync(path, remote, always: true);
                File.Delete(path);
            }
            uploaded.Insert(0, remote);
        }
        if (uploaded.Count > 0)
            NextcloudInterop.Notify(uploaded.Count == 1
                ? $"In Nextcloud gespeichert: {uploaded[0]}"
                : $"In Nextcloud gespeichert: {uploaded[0]} und {uploaded.Count - 1} weitere Dateien");
    }

    /// <summary>Lädt die Datei hoch, wenn sie sich geändert hat (oder immer); true, wenn hochgeladen.</summary>
    static async Task<bool> UploadAsync(string local, string remote, bool always = false)
    {
        var data = await File.ReadAllBytesAsync(local);
        var hash = Hash(data);
        if (!always && Known.TryGetValue(remote, out var known) && known == hash)
            return false;
        NextcloudInterop.SetPending(data);
        await NextcloudInterop.WriteAsync(remote);
        Known[remote] = hash;
        return true;
    }

    public void ProjectCreated() => browser.ProjectCreated();

    public Task<string> PrintAsync(string pdfPath) => browser.PrintAsync(pdfPath);

    public Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create) => browser.SaveToPenAsync(fileName, create);
}
