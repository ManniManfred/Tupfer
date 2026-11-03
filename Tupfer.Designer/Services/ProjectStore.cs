using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;

namespace Tupfer.Designer.Services;

/// <summary>
/// Projektdateien. Gespeichert wird als <c>*.tupfer</c>: ein ZIP mit dem Projekt (<see cref="ArchiveEntry"/>)
/// und allen Bildern, Aufnahmen und Audiodateien, auf die es verweist. Ältere Projekte (<c>*.tupfer.json</c>)
/// enthalten nur das Projekt und verweisen auf Dateien daneben; sie lassen sich weiter öffnen.
/// </summary>
public static class ProjectStore
{
    public const string Extension = ".tupfer";

    /// <summary>Endung des alten Formats (nur das Projekt, ohne Bilder und Aufnahmen).</summary>
    public const string LegacyExtension = ".tupfer.json";

    /// <summary>Name des Projekts im ZIP.</summary>
    public const string ArchiveEntry = "projekt.json";

    /// <summary>Ordner im ZIP für Dateien, die außerhalb des Projektverzeichnisses lagen.</summary>
    const string OutsideDir = "dateien";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Verzeichnis, in das ein <c>*.tupfer</c> zum Bearbeiten entpackt wird. Desktop: ein leeres Verzeichnis
    /// im Benutzerprofil (je Projektdatei immer dasselbe); im Browser wird es ersetzt (dort neben die Datei).
    /// </summary>
    public static Func<string, string> ArchiveWorkDir { get; set; } = DefaultArchiveWorkDir;

    /// <summary>Sperrdatei im Arbeitsverzeichnis: zeigt, dass eine laufende Programminstanz es benutzt.</summary>
    public const string LockFileName = ".tupfer-in-benutzung";

    static FileStream? _workDirLock;
    static string? _lockedWorkDir;

    static string DefaultArchiveWorkDir(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path))))[..12].ToLowerInvariant();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TupferDesigner", "projekte");
        // Benutzt eine andere Instanz (z. B. ein zweites Fenster mit demselben Projekt) das Verzeichnis, ein anderes nehmen:
        // Leeren würde dort Bilder und Aufnahmen löschen, die noch nicht gespeichert sind
        for (int i = 1; ; i++)
        {
            var dir = Path.Combine(root, i == 1 ? $"{BaseName(path)}-{hash}" : $"{BaseName(path)}-{hash}-{i}");
            if (ClaimWorkDir(dir, clear: true))
                return dir;
        }
    }

    /// <summary>
    /// Belegt ein Arbeitsverzeichnis für diese Programminstanz (Sperrdatei, das vorher belegte wird frei).
    /// Mit <paramref name="clear"/> wird es geleert. false, wenn eine andere Instanz es benutzt.
    /// </summary>
    public static bool ClaimWorkDir(string dir, bool clear)
    {
        dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(dir);
        var lockFile = _workDirLock;
        if (!string.Equals(_lockedWorkDir, dir, StringComparison.Ordinal))
        {
            try
            {
                lockFile = new FileStream(Path.Combine(dir, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
        if (clear)
        {
            foreach (var sub in Directory.EnumerateDirectories(dir))
                Directory.Delete(sub, true);
            foreach (var file in Directory.EnumerateFiles(dir).Where(f => Path.GetFileName(f) != LockFileName))
                File.Delete(file);
        }
        if (!ReferenceEquals(lockFile, _workDirLock))
        {
            _workDirLock?.Dispose();
            _workDirLock = lockFile;
            _lockedWorkDir = dir;
        }
        return true;
    }

    /// <summary>Gibt das belegte Arbeitsverzeichnis frei (unter Windows lässt es sich sonst nicht löschen).</summary>
    public static void ReleaseWorkDir()
    {
        _workDirLock?.Dispose();
        _workDirLock = null;
        _lockedWorkDir = null;
    }

    /// <summary>Liest eine JSON-Projektdatei (altes Format).</summary>
    public static ProjectFile Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>Schreibt nur das Projekt als JSON (altes Format, z. B. für Tests).</summary>
    public static void Save(ProjectFile project, string path) => File.WriteAllText(path, ToJson(project));

    public static ProjectFile FromJson(string json) =>
        JsonSerializer.Deserialize<ProjectFile>(json, Options) ?? throw new InvalidDataException("Leere Projektdatei.");

    public static string ToJson(ProjectFile project) => JsonSerializer.Serialize(project, Options);

    /// <summary>true, wenn die Datei ein ZIP ist (<c>*.tupfer</c>), unabhängig von der Endung.</summary>
    public static bool IsArchive(string path)
    {
        Span<byte> head = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.ReadAtLeast(head, 4, throwOnEndOfStream: false) == 4 && head.SequenceEqual("PK\x03\x04"u8);
    }

    /// <summary>
    /// Speichert das Projekt als ZIP. Pfade im Projekt sind relativ zu <paramref name="baseDir"/>; Dateien darin
    /// behalten ihren relativen Pfad, Dateien außerhalb kommen nach <c>dateien/</c> (bzw. <c>medien/</c> für das
    /// Medienverzeichnis), die Verweise werden angepasst. Fehlende Dateien werden übergangen.
    /// Liefert zu jeder aufgenommenen Datei (voller Pfad) ihren Pfad im ZIP, das Medienverzeichnis im ZIP und die
    /// Dateien, auf die das Projekt verweist, die es aber nicht gibt (sie fehlen im ZIP).
    /// </summary>
    public static (Dictionary<string, string> Entries, string MediaPath, List<string> Missing) SaveArchive(ProjectFile project, string baseDir, string path)
    {
        var missing = new List<string>();
        var file = FromJson(ToJson(project));
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ArchiveEntry };

        string Unique(string entry)
        {
            var dir = Path.GetDirectoryName(entry)!.Replace('\\', '/');
            var stem = Path.GetFileNameWithoutExtension(entry);
            var ext = Path.GetExtension(entry);
            for (int i = 2; used.Contains(entry); i++)
                entry = $"{dir}/{stem}-{i}{ext}";
            used.Add(entry);
            return entry;
        }

        string? Add(string reference, string? entry = null)
        {
            var full = Path.GetFullPath(reference, baseDir);
            if (entries.TryGetValue(full, out var known))
                return known;
            if (!File.Exists(full))
            {
                if (!missing.Contains(full))
                    missing.Add(full);
                return null;
            }
            entry ??= Inside(baseDir, full) is { } inside && !used.Contains(inside)
                ? inside
                : Unique($"{OutsideDir}/{Path.GetFileName(full)}");
            used.Add(entry);
            return entries[full] = entry;
        }

        string Rewrite(string reference) =>
            string.IsNullOrEmpty(reference) ? reference : Add(reference) ?? reference;

        // Audiodateien im Medienverzeichnis zuerst: Liegt es außerhalb, kommt es als Ganzes nach medien/
        var media = GmeBuilder.MediaFiles(file, baseDir);
        if (media.Any(f => Inside(baseDir, f) == null))
        {
            var pattern = string.IsNullOrWhiteSpace(file.MediaPath) ? "%s" : file.MediaPath;
            file.MediaPath = "medien/" + Path.GetFileName(pattern);
            foreach (var f in media)
                Add(f, "medien/" + Path.GetFileName(f));
        }
        else
        {
            foreach (var f in media)
                Add(f);
        }

        foreach (var page in file.Pages)
        {
            if (page.BackgroundImage != null)
                page.BackgroundImage = Rewrite(page.BackgroundImage);
            foreach (var e in page.Elements.Where(e => e.Kind == ElementKind.Image))
                e.ImagePath = Rewrite(e.ImagePath);
        }
        foreach (var s in file.Speak.Where(s => !string.IsNullOrEmpty(s.Recording)))
            s.Recording = Rewrite(s.Recording!);

        // Erst vollständig schreiben, dann ersetzen: Ein Fehler beim Schreiben zerstört die alte Datei nicht
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var json = zip.CreateEntry(ArchiveEntry);
            using (var writer = new StreamWriter(json.Open(), new UTF8Encoding(false)))
                writer.Write(ToJson(file));
            foreach (var (source, entry) in entries)
                zip.CreateEntryFromFile(source, entry);
        }
        File.Move(temp, path, overwrite: true);
        return (entries, file.MediaPath, missing);
    }

    /// <summary>Pfad von <paramref name="full"/> relativ zu <paramref name="dir"/> (mit „/“), null, wenn sie außerhalb liegt.</summary>
    static string? Inside(string dir, string full)
    {
        var relative = Path.GetRelativePath(dir, full).Replace('\\', '/');
        return Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) ? null : relative;
    }

    /// <summary>
    /// Entpackt ein <c>*.tupfer</c> nach <paramref name="dir"/> und liest das Projekt. Liefert es zusammen mit dem
    /// Verzeichnis, relativ zu dem seine Pfade gelten. Auch ein ZIP mit einer <c>*.tupfer.json</c> wird gelesen.
    /// </summary>
    public static (ProjectFile Project, string BaseDir) LoadArchive(string path, string dir)
    {
        string entry;
        using (var zip = ZipFile.OpenRead(path))
        {
            entry = zip.Entries.Select(e => e.FullName).FirstOrDefault(n => n == ArchiveEntry)
                    ?? zip.Entries.Select(e => e.FullName)
                        .Where(n => n.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(n => n.Count(c => c == '/')).ThenBy(n => n, StringComparer.Ordinal)
                        .FirstOrDefault()
                    ?? throw new InvalidDataException("Die Datei enthält kein Tupfer-Projekt.");
        }
        // Schützt auch vor Einträgen, die aus dem Verzeichnis hinausführen (../)
        ZipFile.ExtractToDirectory(path, dir, overwriteFiles: true);
        var json = Path.GetFullPath(entry, dir);
        return (Load(json), Path.GetDirectoryName(json)!);
    }

    /// <summary>true für Projektdateien im alten Format (<c>*.tupfer.json</c>).</summary>
    public static bool IsLegacy(string path) => path.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Basisname ohne ".tupfer" bzw. ".tupfer.json", z. B. "tic-tac-toi".</summary>
    public static string BaseName(string projectPath)
    {
        var name = Path.GetFileName(projectPath);
        foreach (var ext in new[] { LegacyExtension, Extension })
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name[..^ext.Length];
        return Path.GetFileNameWithoutExtension(name);
    }
}
