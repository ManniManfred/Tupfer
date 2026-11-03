using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer.Views;

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

    /// <summary>Wartet, bis das Hauptfenster zu sehen ist (beim Start, z. B. Projekt per Doppelklick).</summary>
    static async Task WhenVisible(Window owner)
    {
        if (owner.IsVisible)
            return;
        var opened = new TaskCompletionSource();
        owner.Opened += (_, _) => opened.TrySetResult();
        if (!owner.IsVisible)
            await opened.Task;
    }

    public async Task ShowTextAsync(string title, string text)
    {
        if (topLevel is not Window owner)
            return;
        await WhenVisible(owner);
        var dialog = new Window
        {
            Title = title,
            Width = 760,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var close = new Button { Content = "Schließen", IsDefault = true, IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => dialog.Close();
        var panel = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { close } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        // Auswählbar, damit sich die Liste kopieren lässt; Festbreitenschrift für die Spalten
        panel.Children.Add(new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"),
            FontSize = 12,
        });
        dialog.Content = panel;
        await dialog.ShowDialog(owner);
    }

    public async Task<bool> ConfirmAsync(string message)
    {
        if (topLevel is not Window owner)
            return true;
        await WhenVisible(owner);
        bool result = false;
        var dialog = new Window
        {
            Title = "Tupfer Designer",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var yes = new Button { Content = "Ja", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var no = new Button { Content = "Nein", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            MaxWidth = 480,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { yes, no },
                },
            },
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    public void OpenInViewer(string path)
    {
        var open = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(path) { UseShellExecute = true }
            : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", [path]);
        try
        {
            Process.Start(open)?.Dispose();
        }
        catch (Win32Exception)
        {
            // Kein Programm für den Dateityp; die Datei ist trotzdem geschrieben
        }
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
        OpenInViewer(pdfPath);
        return "Direktes Drucken ist hier nicht möglich, das PDF wurde im PDF-Programm geöffnet – bitte dort drucken.";
    }

    public async Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create)
    {
        var pens = PenContents.FindPens();
        var target = pens.Count == 1
            ? Path.Combine(pens[0], fileName)
            : await SaveAsync("Auf den Stift speichern (Laufwerk „tiptoi“ wählen)", fileName, "GME-Datei", ".gme");
        if (target == null)
            return null;

        var source = await create();
        if (source == null)
            return null;
        File.Copy(source, target, overwrite: true);
        return target;
    }
}
