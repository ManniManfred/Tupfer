using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Tupfer.Designer.Oid;
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

    public void OpenUrl(string url) => _ = topLevel.Launcher.LaunchUriAsync(new Uri(url));

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
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

    public async Task<string?> PrintDirectAsync(IReadOnlyList<PdfPageSpec> pages, int dpi, int pixelSize, bool booklet = false)
    {
        if (DirectPrinting.Printer is not { } printer || topLevel is not Window owner)
            return null;
        var printers = printer.Printers();
        if (printers.Count == 0)
            return null;
        var info = $"Gedruckt wird in Originalgröße mit {dpi} dpi (sofern der Drucker das kann), ohne PDF-Programm."
                   + (booklet ? " Als Broschüre: je zwei Seiten nebeneinander, beidseitig an der kurzen Kante gewendet." : "");
        if (await ChoosePrinterAsync(owner, printers, printer.DefaultPrinter(), info) is not { } chosen)
            return "";
        return await Task.Run(() => printer.Print(pages, dpi, pixelSize, chosen, booklet));
    }

    /// <summary>Fragt nach dem Drucker (<paramref name="preferred"/> vorausgewählt); null bei Abbruch.</summary>
    static async Task<string?> ChoosePrinterAsync(Window owner, IReadOnlyList<string> printers, string? preferred, string info)
    {
        await WhenVisible(owner);

        // Drucker wählen; der Standarddrucker ist vorausgewählt
        string? chosen = null;
        var dialog = new Window
        {
            Title = "Drucken",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var list = new ComboBox { ItemsSource = printers, MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Stretch };
        list.SelectedItem = preferred != null && printers.Contains(preferred) ? preferred : printers[0];
        var print = new Button { Content = "Drucken", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        print.Click += (_, _) => { chosen = list.SelectedItem as string; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            MaxWidth = 480,
            Children =
            {
                new TextBlock { Text = "Drucker" },
                list,
                new TextBlock
                {
                    Text = info,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { print, cancel } },
            },
        };
        await dialog.ShowDialog(owner);
        return chosen;
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
            // CUPS (Linux, macOS): Drucker wählen (es gibt nicht immer einen Standarddrucker), ohne an die Seite anzupassen
            var printers = Lpstat("-e")?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            if (printers.Length > 0 && topLevel is Window owner)
            {
                var info = "Gedruckt wird über CUPS ohne PDF-Programm, in Originalgröße („print-scaling=none“).";
                if (await ChoosePrinterAsync(owner, printers, CupsDefaultPrinter(), info) is not { } chosen)
                    return "";
                var (exitCode, error) = await RunAsync("lp", "-d", chosen, "-o", "print-scaling=none", pdfPath);
                if (exitCode == 0)
                {
                    // Angehaltener Drucker (z. B. Warteschlange von cups-browsed ohne Ziel): Auftrag wartet nur
                    await Task.Delay(1500);
                    if (Lpstat($"-p{chosen}") is { } state && state.Contains(" disabled", StringComparison.Ordinal))
                    {
                        var reason = string.Join(" ", state.Split('\n').Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0));
                        return $"Der Auftrag wartet: Der Drucker „{chosen}“ ist angehalten{(reason.Length > 0 ? $" ({reason})" : "")}. "
                               + "Das ist eine Einstellung des Systems (CUPS) – in der Druckerwarteschlange nachsehen oder den Drucker neu einrichten.";
                    }
                    return $"An „{chosen}“ gesendet.";
                }
                OpenInViewer(pdfPath);
                return $"Drucken über CUPS ging nicht ({error.Trim()}), das PDF wurde im PDF-Programm geöffnet – bitte dort drucken.";
            }
        }

        // Sonst im PDF-Programm öffnen und dort drucken
        OpenInViewer(pdfPath);
        return "Direktes Drucken ist hier nicht möglich, das PDF wurde im PDF-Programm geöffnet – bitte dort drucken.";
    }

    /// <summary>Ausgabe von <c>lpstat</c> (englisch, damit sie sich auswerten lässt); null, wenn es CUPS nicht gibt.</summary>
    static string? Lpstat(string option)
    {
        try
        {
            var psi = new ProcessStartInfo("lpstat", [option]) { RedirectStandardOutput = true, RedirectStandardError = true };
            psi.Environment["LC_ALL"] = "C";
            using var process = Process.Start(psi);
            if (process == null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Standarddrucker von CUPS („system default destination: Name“), sonst null.</summary>
    static string? CupsDefaultPrinter() =>
        Lpstat("-d") is { } output && output.IndexOf(':') is var colon and >= 0
            && output.StartsWith("system default destination", StringComparison.Ordinal)
            ? output[(colon + 1)..].Trim()
            : null;

    static async Task<(int ExitCode, string Error)> RunAsync(string program, params string[] args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, args) { RedirectStandardOutput = true, RedirectStandardError = true });
            if (process == null)
                return (-1, $"{program} ließ sich nicht starten");
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await error);
        }
        catch (Win32Exception)
        {
            return (-1, $"{program} ist nicht installiert");
        }
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
