using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TipToy.Designer.ViewModels;

namespace TipToy.Designer.Views;

/// <summary>Dateidialoge des Betriebssystems (Desktop), arbeiten direkt mit lokalen Pfaden.</summary>
public class StorageFileDialogs(TopLevel topLevel) : IFileDialogs
{
    public async Task<string?> OpenAsync(string title, string filterName, params string[] patterns)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(filterName) { Patterns = patterns } },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType(filterName) { Patterns = new[] { "*" + extension } } },
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string> PrintAsync(string pdfPath)
    {
        // Ältere Druckdateien aufräumen (die aktuelle braucht das PDF-Programm evtl. noch eine Weile)
        foreach (var old in Directory.EnumerateFiles(Path.GetDirectoryName(pdfPath)!, "*.pdf"))
            if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-1))
                try { File.Delete(old); } catch (IOException) { }

        if (OperatingSystem.IsWindows())
        {
            // „print“ gibt es nur, wenn das PDF-Programm es anbietet (z. B. Adobe Reader, nicht Edge)
            var psi = new ProcessStartInfo(pdfPath) { UseShellExecute = true };
            if (psi.Verbs.Contains("print", StringComparer.OrdinalIgnoreCase))
            {
                psi.Verb = "print";
                Process.Start(psi)?.Dispose();
                return "An den Standarddrucker gesendet.";
            }
        }
        else
        {
            // CUPS (Linux, macOS): an den Standarddrucker, ohne an die Seite anzupassen
            try
            {
                using var lp = Process.Start(new ProcessStartInfo("lp", ["-o", "print-scaling=none", pdfPath])
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                if (lp != null)
                {
                    await lp.WaitForExitAsync();
                    if (lp.ExitCode == 0)
                        return "An den Standarddrucker gesendet.";
                }
            }
            catch (Win32Exception)
            {
                // lp ist nicht installiert
            }
        }

        // Sonst im PDF-Programm öffnen und dort drucken
        var open = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(pdfPath) { UseShellExecute = true }
            : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", [pdfPath]);
        Process.Start(open)?.Dispose();
        return "Direktes Drucken ist hier nicht möglich, das PDF wurde im PDF-Programm geöffnet – bitte dort drucken.";
    }
}
