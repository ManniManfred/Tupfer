using System.Text.Json;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer;

/// <summary>
/// Dateidialoge, wenn der Designer aus Büro (Dokumentenverwaltung) heraus geöffnet wurde. Das Projekt (*.tupfer)
/// ist ein Dokument in Büro: Es wird in ein eigenes Arbeitsverzeichnis geladen, „Speichern“ legt eine neue Version
/// des Dokuments an. Hat inzwischen jemand anderes eine neuere Version gespeichert, wird vor dem Speichern gefragt.
/// „Speichern unter“ und Projekte, die zu keinem Dokument gehören, werden neue Dokumente (in denselben Sammlungen
/// wie das bisherige). Alles andere (Öffnen vom eigenen Rechner, Exporte, Drucken, Stift) läuft wie im Browser.
/// </summary>
class BueroFileDialogs(BrowserFileDialogs browser) : IFileDialogs
{
    /// <summary>Je Arbeitsverzeichnis: das Dokument in Büro, sein Dateiname und die zuletzt bekannte Version.</summary>
    static readonly Dictionary<string, Target> Targets = new();

    record Target(int Id, string Name, int Version, string Collections);

    /// <summary>Ziel von „Speichern unter“, bis die Datei geschrieben ist: lokaler Pfad → Name des neuen Dokuments.</summary>
    readonly Dictionary<string, string> _saveAs = new();

    /// <summary>Lädt die gemerkten Ziele (für den wiederhergestellten Bearbeitungsstand).</summary>
    public static void Init()
    {
        try
        {
            var json = BueroInterop.LoadState();
            if (json.Length > 0 && JsonSerializer.Deserialize<Dictionary<string, Target>>(json) is { } saved)
                foreach (var (dir, target) in saved)
                    Targets[dir] = target;
        }
        catch (JsonException)
        {
        }
        // Neue Arbeitsverzeichnisse (neues Projekt, Beispiel, Datei vom eigenen Rechner) gehören zu keinem Dokument
        BrowserFileDialogs.WorkDirCreated += dir =>
        {
            if (Targets.Remove(dir))
                SaveTargets();
        };
    }

    static void SaveTargets() => BueroInterop.SaveState(JsonSerializer.Serialize(Targets));

    static void SetTarget(string workDir, Target target)
    {
        Targets[workDir] = target;
        SaveTargets();
    }

    static bool IsProject(string path) => path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Für das Protokoll: bei Projekten der Name des Dokuments in Büro.</summary>
    public string DisplayPath(string path) =>
        IsProject(path) && Targets.TryGetValue(BrowserFileDialogs.WorkDirOf(path), out var target)
            ? $"{target.Name} (Büro, Version {target.Version})"
            : path;

    /// <summary>Lädt das Dokument in ein neues Arbeitsverzeichnis. Liefert den lokalen Pfad des Projekts.</summary>
    public static async Task<string> OpenDocumentAsync(int id)
    {
        var json = await BueroInterop.InfoAsync(id);
        if (json.Length == 0)
            throw new FileNotFoundException($"Das Dokument {id} gibt es in Büro nicht (mehr).");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var name = Path.GetFileName(root.GetProperty("original_filename").GetString() ?? "");
        if (!IsProject(name))
            throw new InvalidOperationException($"„{name}“ ist kein Tupfer-Projekt (*{ProjectStore.Extension}).");
        var collections = string.Join(',', root.GetProperty("collection_ids").EnumerateArray().Select(c => c.GetInt64()));

        var previous = Environment.CurrentDirectory;
        var workDir = BrowserFileDialogs.NewWorkDir();
        var local = Path.Combine(workDir, name);
        try
        {
            // Version vor dem Inhalt: Wird dazwischen gespeichert, fragt das nächste Speichern lieber einmal zu oft
            var version = root.GetProperty("version").GetInt32();
            if (await BueroInterop.ReadAsync(id) is not { } base64)
                throw new FileNotFoundException($"Das Dokument {id} gibt es in Büro nicht (mehr).");
            await File.WriteAllBytesAsync(local, Convert.FromBase64String(base64));
            SetTarget(workDir, new Target(id, name, version, collections));
            return local;
        }
        catch
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
            throw;
        }
    }

    public Task<string?> OpenAsync(string title, string filterName, params string[] patterns) =>
        browser.OpenAsync(title, filterName, patterns);

    public async Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension)
    {
        if (extension != ProjectStore.Extension)
            return await browser.SaveAsync(title, suggestedName, filterName, extension);
        // Speichern unter: neues Dokument in Büro; lokal bleibt das Projekt in seinem Verzeichnis
        if (AskName("Name des neuen Dokuments in Büro:", ProjectStore.BaseName(suggestedName)) is not { } name)
            return null;
        var local = Path.Combine(Environment.CurrentDirectory, name);
        _saveAs[local] = name;
        return local;
    }

    /// <summary>Fragt nach dem Dateinamen eines neuen Dokuments (ohne Endung vorbelegt); null bei Abbruch.</summary>
    static string? AskName(string text, string name)
    {
        name = BueroInterop.AskName(text, name);
        if (name.Length == 0)
            return null;
        return string.Concat(ProjectStore.BaseName(name).Select(c => c is '/' or '\\' ? '_' : c)) + ProjectStore.Extension;
    }

    public async Task SavedAsync(params string[] paths)
    {
        var others = new List<string>();
        foreach (var path in paths.Where(File.Exists))
        {
            if (IsProject(path))
                await SaveProjectAsync(path);
            else
                others.Add(path);
        }
        // Exporte (GME, PDF …) wie im Browser herunterladen
        if (others.Count > 0)
            await browser.SavedAsync(others.ToArray());
    }

    /// <summary>Speichert das Projekt als neue Version seines Dokuments bzw. als neues Dokument.</summary>
    async Task SaveProjectAsync(string path)
    {
        var workDir = BrowserFileDialogs.WorkDirOf(path);
        Targets.TryGetValue(workDir, out var known);
        if (_saveAs.Remove(path, out var newName))
        {
            await CreateAsync(path, workDir, newName, known?.Collections ?? "");
            return;
        }
        if (known == null)
        {
            // Neues Projekt, Beispiel oder Datei vom eigenen Rechner: noch kein Dokument in Büro
            var name = AskName("Projekt in Büro speichern – Name des neuen Dokuments:", ProjectStore.BaseName(path))
                       ?? throw new OperationCanceledException("nicht in Büro gespeichert (abgebrochen).");
            await CreateAsync(path, workDir, name, "");
            return;
        }

        BueroInterop.SetPending(await File.ReadAllBytesAsync(path));
        var result = await BueroInterop.SaveVersionAsync(known.Id, known.Name, known.Version);
        if (result == "geaendert")
        {
            if (!BueroInterop.Confirm($"„{known.Name}“ wurde inzwischen in Büro geändert (es gibt eine neuere Version)."
                                      + "\n\nTrotzdem diesen Stand als neueste Version speichern? Die andere Version bleibt in Büro erhalten."))
                throw new OperationCanceledException($"nicht gespeichert – mit „Speichern unter“ lässt sich das Projekt als eigenes Dokument sichern.");
            BueroInterop.SetPending(await File.ReadAllBytesAsync(path));
            result = await BueroInterop.SaveVersionAsync(known.Id, known.Name, 0);
        }
        if (result == "fehlt")
        {
            if (!BueroInterop.Confirm($"„{known.Name}“ gibt es in Büro nicht mehr. Als neues Dokument speichern?"))
                throw new OperationCanceledException("nicht gespeichert (das Dokument wurde in Büro gelöscht).");
            await CreateAsync(path, workDir, known.Name, known.Collections);
            return;
        }
        var version = int.Parse(result);
        SetTarget(workDir, known with { Version = version });
        BueroInterop.Notify(version == known.Version
            ? $"{known.Name}: unverändert (Version {version})"
            : $"In Büro gespeichert: {known.Name}, Version {version}", known.Id);
    }

    async Task CreateAsync(string path, string workDir, string name, string collections)
    {
        BueroInterop.SetPending(await File.ReadAllBytesAsync(path));
        using var doc = JsonDocument.Parse(await BueroInterop.CreateAsync(name, collections));
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetInt32();
        // Bei einem Duplikat heißt das vorhandene Dokument womöglich anders
        name = root.GetProperty("name").GetString() ?? name;
        SetTarget(workDir, new Target(id, name, root.GetProperty("version").GetInt32(), collections));
        BueroInterop.Notify(root.GetProperty("duplicate").GetBoolean()
            ? $"Genau diesen Stand gibt es in Büro schon – weiter mit Dokument {id}"
            : $"In Büro als neues Dokument gespeichert: {name}", id);
    }

    public void ProjectCreated() => browser.ProjectCreated();

    public Task<bool> ConfirmAsync(string message) => browser.ConfirmAsync(message);

    public void OpenUrl(string url) => BrowserInterop.OpenUrl(url);

    public Task<string> PrintAsync(string pdfPath) => browser.PrintAsync(pdfPath);

    public Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create) => browser.SaveToPenAsync(fileName, create);
}
