using System.Text.Json;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer;

/// <summary>
/// Dateidialoge in der Nextcloud-App. Bearbeitet werden nur Projekte im Format *.tupfer (ein ZIP mit Projekt,
/// Bildern und Aufnahmen): Ein Projekt aus Nextcloud wird in ein eigenes Arbeitsverzeichnis geladen und dort
/// entpackt, „Speichern“ schreibt es an dieselbe Stelle in Nextcloud zurück, „Speichern unter“ fragt nach Ordner
/// und Namen in Nextcloud. Wurde die Datei inzwischen anderswo geändert, wird vor dem Überschreiben gefragt.
/// Bilder, SVG- und Audiodateien kommen aus Nextcloud und werden beim Speichern ins Projekt übernommen; Exporte
/// (GME, PDF …) landen neben dem Projekt. Drucken und „Auf den Stift“ laufen wie im Browser.
/// </summary>
class NextcloudFileDialogs(BrowserFileDialogs browser) : IFileDialogs
{
    /// <summary>Je Arbeitsverzeichnis: die Projektdatei in Nextcloud und ihr zuletzt bekannter Stand (ETag).</summary>
    static readonly Dictionary<string, Target> Targets = new();

    record Target(string Remote, string ETag);

    /// <summary>Ziel von „Speichern unter“, bis die Datei geschrieben ist: lokaler Pfad → Pfad in Nextcloud.</summary>
    readonly Dictionary<string, string> _saveAs = new();

    /// <summary>Exporte in Nextcloud, die in dieser Sitzung geschrieben werden dürfen (neu oder Überschreiben bestätigt).</summary>
    readonly HashSet<string> _exportsAllowed = new(StringComparer.Ordinal);

    /// <summary>Lädt die gemerkten Ziele (für den wiederhergestellten Bearbeitungsstand).</summary>
    public static void Init()
    {
        try
        {
            var json = NextcloudInterop.LoadState();
            if (json.Length > 0 && JsonSerializer.Deserialize<Dictionary<string, Target>>(json) is { } saved)
                foreach (var (dir, target) in saved)
                {
                    Targets[dir] = target;
                    NextcloudInterop.SetETag(target.Remote, target.ETag);
                }
        }
        catch (JsonException)
        {
        }
        // Neue Arbeitsverzeichnisse (neues Projekt, Beispiel) gehören noch zu keiner Datei
        BrowserFileDialogs.WorkDirCreated += dir =>
        {
            if (Targets.Remove(dir))
                SaveTargets();
        };
    }

    static void SaveTargets() => NextcloudInterop.SaveState(JsonSerializer.Serialize(Targets));

    static void SetTarget(string workDir, string remote)
    {
        Targets[workDir] = new Target(remote, NextcloudInterop.ETag(remote));
        SaveTargets();
    }

    static string WorkDir(string local) => BrowserFileDialogs.WorkDirOf(local);

    /// <summary>Projektdatei in Nextcloud, zu der das lokale Verzeichnis gehört, sonst null.</summary>
    static string? RemoteOf(string localDir) =>
        Targets.TryGetValue(WorkDir(Path.Combine(localDir, "x")), out var target) ? target.Remote : null;

    /// <summary>Für das Protokoll: bei Projekten der Pfad in Nextcloud.</summary>
    public string DisplayPath(string path) =>
        IsProject(path) && Targets.TryGetValue(WorkDir(path), out var target) ? target.Remote : path;

    // ---------- Pfade ----------

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

    static bool IsProject(string path) => path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Ordner, in dem die Dateiauswahl beginnt: der des aktuellen Projekts, sonst der oberste.</summary>
    static string StartFolder() => RemoteOf(Environment.CurrentDirectory) is { } remote ? RemoteParent(remote) : "/";

    // ---------- Laden ----------

    /// <summary>Lädt die Datei nach <paramref name="local"/>; false, wenn es sie in Nextcloud nicht gibt.</summary>
    static async Task<bool> DownloadAsync(string remote, string local)
    {
        if (await NextcloudInterop.ReadAsync(remote) is not { } base64)
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        await File.WriteAllBytesAsync(local, Convert.FromBase64String(base64));
        return true;
    }

    /// <summary>Lädt ein Projekt (*.tupfer) aus Nextcloud in ein neues Arbeitsverzeichnis. Liefert den lokalen Pfad.</summary>
    public static async Task<string> OpenProjectAsync(string remote)
    {
        if (!IsProject(remote))
            throw new InvalidOperationException($"„{RemoteName(remote)}“ ist kein Tupfer-Projekt (*{ProjectStore.Extension}).");
        var previous = Environment.CurrentDirectory;
        var workDir = BrowserFileDialogs.NewWorkDir();
        var local = Path.Combine(workDir, RemoteName(remote));
        bool found;
        try
        {
            found = await DownloadAsync(remote, local);
        }
        catch
        {
            Discard(workDir, previous);
            throw;
        }
        if (!found)
        {
            // Das bisherige Projekt bleibt offen, neue Bilder sollen weiter dort landen
            Discard(workDir, previous);
            throw new FileNotFoundException($"„{remote}“ gibt es in Nextcloud nicht (mehr).");
        }
        SetTarget(workDir, remote);
        return local;
    }

    /// <summary>Verwirft ein nicht gebrauchtes Arbeitsverzeichnis und kehrt ins vorige zurück.</summary>
    static void Discard(string workDir, string previous)
    {
        if (Directory.Exists(previous))
            Environment.CurrentDirectory = previous;
        try
        {
            Directory.Delete(workDir, true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Legt ein neues Projekt (Inhalt wie „Datei → Neu“) in Nextcloud an und öffnet es. Gibt es die Datei
    /// schon, wird sie nur geöffnet.
    /// </summary>
    public static async Task<string> CreateProjectAsync(string remote)
    {
        if (!IsProject(remote))
            remote += ProjectStore.Extension;
        var previous = Environment.CurrentDirectory;
        var workDir = BrowserFileDialogs.NewWorkDir();
        var local = Path.Combine(workDir, RemoteName(remote));
        ProjectStore.SaveArchive(MainWindowViewModel.DefaultProjectFile(), workDir, local);
        if (await UploadAsync(local, remote, "neu") == "vorhanden")
        {
            Discard(workDir, previous);
            return await OpenProjectAsync(remote);
        }
        SetTarget(workDir, remote);
        NextcloudInterop.Notify($"In Nextcloud angelegt: {remote}");
        return local;
    }

    public async Task<string?> OpenAsync(string title, string filterName, params string[] patterns)
    {
        // tttool-YAML ist ein Import vom eigenen Rechner, kein Projekt in Nextcloud
        if (patterns.Any(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)))
            return await browser.OpenAsync(title, filterName, patterns);

        bool project = patterns.Any(BrowserFileDialogs.IsProjectPattern);
        List<string> endings = project ? [ProjectStore.Extension] : patterns.Select(p => p.TrimStart('*')).ToList();
        var remote = await NextcloudInterop.PickAsync(title, string.Join(',', endings), false, StartFolder());
        if (remote.Length == 0)
            return null;
        if (!endings.Any(e => remote.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"„{RemoteName(remote)}“ ist keine passende Datei ({string.Join(", ", endings)}).");
        if (project)
            return await OpenProjectAsync(remote);

        // Bild, SVG oder Audiodatei: als Kopie ins Projekt, beim Speichern wird sie Teil der *.tupfer
        var local = WorkFiles.UniquePath(Environment.CurrentDirectory, RemoteName(remote));
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
            return RemoteOf(current) != null
                ? Path.Combine(current, suggestedName)
                : await browser.SaveAsync(title, suggestedName, filterName, extension);
        }

        // Speichern unter: Ordner und Namen in Nextcloud wählen; lokal bleibt das Projekt in seinem Verzeichnis
        if (await PickTargetAsync(title, ProjectStore.BaseName(suggestedName)) is not { } remote)
            return null;
        var local = Path.Combine(current, RemoteName(remote));
        _saveAs[local] = remote;
        return local;
    }

    /// <summary>
    /// Fragt nach Ordner und Namen (vorbelegt mit <paramref name="name"/>, ohne Endung) für ein Projekt in
    /// Nextcloud; gibt es die Datei schon, wird vor dem Überschreiben gefragt. Liefert den Pfad, null bei Abbruch.
    /// </summary>
    static async Task<string?> PickTargetAsync(string title, string name)
    {
        var folder = await NextcloudInterop.PickAsync(title + ": Ordner wählen", "", true, StartFolder());
        if (folder.Length == 0)
            return null;
        name = NextcloudInterop.AskName($"Name des Projekts in {folder}:", name);
        if (name.Length == 0)
            return null;
        name = string.Concat(ProjectStore.BaseName(name).Select(c => c is '/' or '\\' ? '_' : c)) + ProjectStore.Extension;
        var remote = Normalize(folder + "/" + name)!;
        if (await NextcloudInterop.ExistsAsync(remote) && !NextcloudInterop.Confirm($"„{remote}“ gibt es schon. Überschreiben?"))
            return null;
        return remote;
    }

    public async Task SavedAsync(params string[] paths)
    {
        var saved = new List<string>();
        foreach (var path in paths.Where(File.Exists))
        {
            if (IsProject(path))
            {
                saved.Add(await SaveProjectAsync(path));
                continue;
            }
            // Export: neben das Projekt in Nextcloud, sonst herunterladen
            if (RemoteOf(Path.GetDirectoryName(path)!) is not { } project)
            {
                await browser.SavedAsync(path);
                continue;
            }
            var remote = Normalize(RemoteParent(project) + "/" + Path.GetFileName(path))!;
            // Vorhandene Dateien nicht ungefragt überschreiben (einmal je Datei und Sitzung fragen)
            if (!_exportsAllowed.Contains(remote))
            {
                if (await NextcloudInterop.ExistsAsync(remote) && !NextcloudInterop.Confirm($"„{remote}“ gibt es schon. Überschreiben?"))
                {
                    File.Delete(path);
                    continue;
                }
                _exportsAllowed.Add(remote);
            }
            await UploadAsync(path, remote, "immer");
            File.Delete(path);
            saved.Add(remote);
        }
        if (saved.Count > 0)
            NextcloudInterop.Notify(saved.Count == 1
                ? $"In Nextcloud gespeichert: {saved[0]}"
                : $"In Nextcloud gespeichert: {saved[0]} und {saved.Count - 1} weitere Dateien");
    }

    /// <summary>Schreibt das Projekt in seine Datei in Nextcloud (bzw. das Ziel von „Speichern unter“).</summary>
    async Task<string> SaveProjectAsync(string path)
    {
        var workDir = WorkDir(path);
        string remote, mode;
        if (_saveAs.Remove(path, out var target))
        {
            // Überschreiben wurde bei der Auswahl schon bestätigt
            (remote, mode) = (target, "immer");
        }
        else if (Targets.TryGetValue(workDir, out var known))
        {
            (remote, mode) = (known.Remote, "ersetzen");
        }
        else
        {
            // Neues Projekt oder Beispiel: noch keine Datei in Nextcloud
            remote = await PickTargetAsync("Projekt in Nextcloud speichern", ProjectStore.BaseName(path))
                     ?? throw new OperationCanceledException("nicht in Nextcloud gespeichert (abgebrochen).");
            mode = "immer";
        }

        var result = await UploadAsync(path, remote, mode);
        if (result is "geaendert" or "unbekannt")
        {
            var reason = result == "geaendert"
                ? $"„{remote}“ wurde inzwischen in Nextcloud geändert (z. B. auf einem anderen Gerät)."
                : $"Ob „{remote}“ inzwischen in Nextcloud geändert wurde, lässt sich nicht prüfen (der zuletzt geladene Stand ist nicht mehr bekannt).";
            if (!NextcloudInterop.Confirm(reason + "\n\nTrotzdem mit diesem Stand überschreiben?"))
                throw new OperationCanceledException($"nicht gespeichert – „{remote}“ wurde nicht überschrieben. Mit „Speichern unter“ lässt sich das Projekt unter anderem Namen sichern.");
            await UploadAsync(path, remote, "immer");
        }
        SetTarget(workDir, remote);
        return remote;
    }

    /// <summary>Lädt die Datei hoch (Modus siehe <see cref="NextcloudInterop.WriteAsync"/>).</summary>
    static async Task<string> UploadAsync(string local, string remote, string mode)
    {
        NextcloudInterop.SetPending(await File.ReadAllBytesAsync(local));
        return await NextcloudInterop.WriteAsync(remote, mode);
    }

    public void ProjectCreated() => browser.ProjectCreated();

    public Task<bool> ConfirmAsync(string message) => browser.ConfirmAsync(message);

    public Task<string> PrintAsync(string pdfPath) => browser.PrintAsync(pdfPath);

    public Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create) => browser.SaveToPenAsync(fileName, create);
}
