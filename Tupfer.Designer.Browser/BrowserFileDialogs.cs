using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Tupfer.Designer.Gme;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Tupfer.Designer.Views;

namespace Tupfer.Designer;

/// <summary>
/// Dateidialoge im Browser. Die App arbeitet auf dem Speicher-Dateisystem von WebAssembly:
/// Ausgewählte Dateien werden ins Arbeitsverzeichnis des Projekts kopiert, gespeicherte
/// Dateien werden anschließend heruntergeladen. Ein Projekt mit Bildern oder Audiodateien
/// wird als ZIP heruntergeladen und kann so auch wieder geöffnet werden.
/// </summary>
class BrowserFileDialogs(TopLevel topLevel) : IFileDialogs
{
    const string Root = "/tupfer";
    static int _workDirs;

    /// <summary>Legt ein leeres Arbeitsverzeichnis an und macht es zum aktuellen Verzeichnis.</summary>
    public static string NewWorkDir()
    {
        var dir = Path.Combine(Root, $"projekt{++_workDirs}");
        Directory.CreateDirectory(dir);
        Environment.CurrentDirectory = dir;
        return dir;
    }

    static bool IsProjectPattern(string pattern) =>
        ProjectStore.IsProjectFile(pattern)
        || pattern.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
        || pattern.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);

    static bool Matches(string path, string pattern) =>
        path.EndsWith(pattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase);

    public async Task<string?> OpenAsync(string title, string filterName, params string[] patterns)
    {
        // Projekte: mehrere Dateien (z. B. Projekt mit Bildern) oder ein ZIP; Browser kennen nur einfache Endungen
        bool project = patterns.Any(IsProjectPattern);
        var browserPatterns = patterns.Select(p => "*" + Path.GetExtension(p));
        if (project)
            browserPatterns = browserPatterns.Append("*.zip");
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = project,
            FileTypeFilter = new[] { new FilePickerFileType(filterName) { Patterns = browserPatterns.Distinct().ToList() } },
        });
        if (files.Count == 0)
            return null;

        var dir = project ? NewWorkDir() : Environment.CurrentDirectory;
        var copied = new List<string>();
        foreach (var file in files)
        {
            if (project && file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = await file.OpenReadAsync();
                await ZipFile.ExtractToDirectoryAsync(stream, dir, overwriteFiles: true);
                copied.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Order());
            }
            else
            {
                copied.Add(await WorkFiles.CopyAsync(file, dir));
            }
        }
        var result = copied.Where(p => patterns.Any(pattern => Matches(p, pattern)))
            .OrderBy(p => p.EndsWith(".codes.yaml", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        // Neue Bilder sollen neben der Projektdatei landen, auch wenn sie im ZIP in einem Unterordner lag
        if (project && result != null)
            Environment.CurrentDirectory = Path.GetDirectoryName(result)!;
        return result;
    }

    public Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension)
    {
        // Kein Dialog: Die Datei wird geschrieben und danach unter diesem Namen heruntergeladen
        if (!suggestedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            suggestedName += extension;
        return Task.FromResult<string?>(Path.Combine(Environment.CurrentDirectory, suggestedName));
    }

    public Task SavedAsync(params string[] paths)
    {
        foreach (var path in paths.Where(File.Exists))
        {
            if (ProjectStore.IsProjectFile(path))
            {
                DownloadProject(path);
            }
            else
            {
                BrowserInterop.Download(Path.GetFileName(path), File.ReadAllBytes(path), MimeType(path));
                File.Delete(path);
            }
        }
        return Task.CompletedTask;
    }

    public void ProjectCreated() => NewWorkDir();

    public async Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create)
    {
        // Den Dialog sofort öffnen: Der Browser erlaubt ihn nur kurz nach dem Klick, das Bauen kann dauern
        if (!await BrowserInterop.PickSaveFileAsync(fileName))
            return null;
        var path = await create();
        if (path == null)
            return null;
        BrowserInterop.SetPendingFile(File.ReadAllBytes(path));
        return await BrowserInterop.WritePickedFileAsync();
    }

    public Task<string> PrintAsync(string pdfPath)
    {
        var data = File.ReadAllBytes(pdfPath);
        File.Delete(pdfPath);
        return Task.FromResult(BrowserInterop.PrintPdf(data)
            ? "Druckdialog geöffnet."
            : "Das PDF wurde in einem neuen Tab geöffnet – bitte dort drucken.");
    }

    /// <summary>
    /// Lädt die Projektdatei herunter, zusammen mit Bildern und Audiodateien als ZIP. Das ZIP enthält das
    /// ganze Arbeitsverzeichnis, damit auch Pfade wie ../bilder/… beim erneuten Öffnen stimmen.
    /// </summary>
    static void DownloadProject(string path)
    {
        var dir = WorkDirOf(path);
        var media = MediaFiles(dir);
        if (media.Count == 0)
        {
            BrowserInterop.Download(Path.GetFileName(path), File.ReadAllBytes(path), "application/json");
            return;
        }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var file in media.Prepend(path))
                zip.CreateEntryFromFile(file, Path.GetRelativePath(dir, file).Replace('\\', '/'));
        BrowserInterop.Download(ProjectStore.BaseName(path) + ".zip", ms.ToArray(), "application/zip");
    }

    /// <summary>Das Arbeitsverzeichnis (/tupfer/projektN), in dem die Datei liegt.</summary>
    public static string WorkDirOf(string path)
    {
        var relative = Path.GetRelativePath(Root, path);
        return relative.StartsWith("..") ? Path.GetDirectoryName(path)! : Path.Combine(Root, relative.Split('/')[0]);
    }

    /// <summary>Bilder und Audiodateien im Verzeichnis (ohne den Zwischenspeicher der Sprachausgabe).</summary>
    public static List<string> MediaFiles(string dir) =>
        !Directory.Exists(dir) ? [] : Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/tts-cache/", StringComparison.Ordinal))
            .Where(f => PageCanvas.ImageExtensions.Contains(Path.GetExtension(f))
                        || GmeBuilder.MediaExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Order()
            .ToList();

    /// <summary>Ein wiederhergestelltes Arbeitsverzeichnis: Neue bekommen eine höhere Nummer.</summary>
    public static void ReserveWorkDir(string path)
    {
        var name = Path.GetRelativePath(Root, path).Split('/')[0];
        if (name.StartsWith("projekt") && int.TryParse(name["projekt".Length..], out var n))
            _workDirs = Math.Max(_workDirs, n);
    }

    static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".yaml" or ".yml" => "application/yaml",
        ".json" => "application/json",
        _ => "application/octet-stream",
    };
}
