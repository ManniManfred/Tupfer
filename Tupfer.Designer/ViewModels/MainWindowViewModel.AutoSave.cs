using Avalonia.Threading;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

// Automatisches Sichern des Bearbeitungsstands, damit nach einem Neuladen der Seite oder einem Absturz nichts verloren geht
public partial class MainWindowViewModel
{
    /// <summary>Ablage für das automatische Sichern; null = aus (z. B. in Tests).</summary>
    public static IAutoSaveStore? AutoSaveStore { get; set; }

    DispatcherTimer? _autoSaveTimer;
    bool _autoSaving;
    bool _autoSaveFailed;

    /// <summary>
    /// Startet die App: öffnet das Beispiel oder stellt den zuletzt gesicherten Stand wieder her
    /// (wenn <paramref name="restore"/>) und sichert danach jede Sekunde, falls sich etwas geändert hat.
    /// </summary>
    public async Task StartAsync(ExampleProject? example, bool restore)
    {
        if (example != null)
            await OpenExampleCommand.ExecuteAsync(example);
        else if (restore && AutoSaveStore != null)
            await RestoreAutoSave(AutoSaveStore);

        // Erst jetzt, sonst würde der gesicherte Stand gleich mit dem leeren Projekt überschrieben
        if (AutoSaveStore != null)
        {
            _autoSaveTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) => await AutoSave());
            _autoSaveTimer.Start();
        }
    }

    async Task RestoreAutoSave(IAutoSaveStore store)
    {
        try
        {
            if (await store.LoadAsync() is not { } s)
                return;
            if (Directory.Exists(s.CurrentDirectory))
                Environment.CurrentDirectory = s.CurrentDirectory;
            Project = ProjectViewModel.FromFile(ProjectStore.FromJson(s.ProjectJson), s.BaseDir, Log);
            FilePath = s.FilePath;
            Log(s.FilePath != null
                ? $"Letzten Bearbeitungsstand wiederhergestellt ({Path.GetFileName(s.FilePath)})."
                : "Letzten Bearbeitungsstand wiederhergestellt (noch nicht gespeichertes Projekt).");
            AfterLoad();
        }
        catch (Exception ex)
        {
            Notice($"Der automatisch gesicherte Stand ließ sich nicht wiederherstellen: {ex.Message}");
        }
    }

    async Task AutoSave()
    {
        if (_autoSaving || AutoSaveStore == null)
            return;
        _autoSaving = true;
        try
        {
            var json = ProjectStore.ToJson(Project.ToFile(BaseDir));
            await AutoSaveStore.SaveAsync(new AutoSaveSnapshot(FilePath, BaseDir, Environment.CurrentDirectory, json));
            _autoSaveFailed = false;
        }
        catch (Exception ex)
        {
            // Nur einmal melden, nicht jede Sekunde
            if (!_autoSaveFailed)
                Notice($"Automatisches Sichern fehlgeschlagen: {ex.Message}");
            _autoSaveFailed = true;
        }
        finally
        {
            _autoSaving = false;
        }
    }
}
