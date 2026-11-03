using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

// Automatische Aktualisierung der mit dem Setup installierten Windows-Version
public partial class MainWindowViewModel
{
    /// <summary>„Nach Updates suchen“ gibt es nur in der installierten Windows-Version.</summary>
    public static bool CanUpdate => Updater.IsAvailable;

    bool _updating;

    /// <summary>Beim Start still im Hintergrund suchen; ohne Netz o. Ä. steht nur ein Hinweis im Protokoll.</summary>
    async Task CheckForUpdateOnStartAsync()
    {
        try
        {
            await UpdateAsync(manual: false);
        }
        catch (Exception ex)
        {
            Log($"Suche nach einer neuen Version fehlgeschlagen: {ex.Message}");
        }
    }

    [RelayCommand]
    Task CheckForUpdate() => Guard("der Suche nach einer neuen Version", () => UpdateAsync(manual: true));

    async Task UpdateAsync(bool manual)
    {
        if (_updating)
            return;
        _updating = true;
        try
        {
            if (await Updater.FindNewerAsync(Version) is not { } release)
            {
                if (manual)
                    Notice($"Der Tupfer Designer {Version} ist die neueste Version.");
                return;
            }
            var newVersion = release.Version.ToString(3);
            if (Dialogs == null || !await Dialogs.ConfirmAsync(
                    $"Die neue Version {newVersion} des Tupfer Designers ist verfügbar (installiert: {Version}).\n\n" +
                    "Jetzt herunterladen und installieren? Der Tupfer Designer wird dazu beendet und danach wieder gestartet; " +
                    "ungespeicherte Änderungen bleiben erhalten."))
                return;

            Notice($"Lade Version {newVersion} herunter …");
            var setup = await Updater.DownloadAsync(release);

            // Letzten Stand sichern, beim Neustart wird er wiederhergestellt
            _autoSaveTimer?.Stop();
            await AutoSave();
            Updater.StartSetup(setup);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        finally
        {
            _updating = false;
        }
    }
}
