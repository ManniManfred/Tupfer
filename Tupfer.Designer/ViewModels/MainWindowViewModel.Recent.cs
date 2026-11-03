using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

/// <summary>Eintrag in „Zuletzt geöffnet“.</summary>
public record RecentProject(string FilePath)
{
    public string Name => Path.GetFileName(FilePath);
    public string Folder => Path.GetDirectoryName(FilePath) ?? "";
}

// Desktop: Jedes Projekt ist eine *.tupfer-Datei, Startbildschirm und zuletzt geöffnete Dateien
public partial class MainWindowViewModel
{
    /// <summary>
    /// Desktop: Ein Projekt hat immer eine Projektdatei. „Neu“ und der YAML-Import fragen gleich nach dem
    /// Speicherort, ohne Datei beim Start erscheint der Startbildschirm. Im Browser und in Tests aus.
    /// </summary>
    public bool RequireProjectFile { get; init; }

    /// <summary>Zuletzt geöffnete Dateien; null = keine Liste (Browser, Tests).</summary>
    public RecentFiles? Recent
    {
        get => _recent;
        init
        {
            _recent = value;
            RefreshRecent();
        }
    }

    readonly RecentFiles? _recent;

    public ObservableCollection<RecentProject> RecentProjects { get; } = new();

    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>Startbildschirm statt des Projekts (Desktop ohne geöffnete Datei).</summary>
    [ObservableProperty] bool _showStartScreen;

    // Jedes geladene oder neue Projekt beendet den Startbildschirm
    partial void OnProjectChanged(ProjectViewModel value) => ShowStartScreen = false;

    void AddRecent(string path)
    {
        if (_recent == null || ProjectStore.IsLegacy(path))
            return;
        _recent.Add(path);
        RefreshRecent();
    }

    void RefreshRecent()
    {
        RecentProjects.Clear();
        foreach (var file in _recent?.Files ?? [])
            RecentProjects.Add(new RecentProject(file));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    [RelayCommand]
    Task OpenRecent(RecentProject recent) => Guard("Öffnen", async () =>
    {
        if (!File.Exists(recent.FilePath))
        {
            Notice($"Die Datei gibt es nicht mehr: {recent.FilePath}");
            _recent?.Remove(recent.FilePath);
            RefreshRecent();
            return;
        }
        if (await ConfirmDiscardAsync())
            Load(recent.FilePath);
    });

    /// <summary>Fragt nach dem Speicherort einer Projektdatei; ergänzt die Endung, null bei Abbruch.</summary>
    async Task<string?> AskProjectPathAsync(string title, string baseName)
    {
        var path = await Dialogs!.SaveAsync(title, baseName + ProjectStore.Extension, "Tupfer-Projekt", ProjectStore.Extension);
        if (path != null && !path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(Path.GetDirectoryName(path)!, ProjectStore.BaseName(path) + ProjectStore.Extension);
        return path;
    }
}
