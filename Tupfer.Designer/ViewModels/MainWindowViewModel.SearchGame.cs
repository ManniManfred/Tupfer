using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

// Suchspiel hinzufügen
public partial class MainWindowViewModel
{
    /// <summary>Auswahl der Flächen fürs Suchspiel (als Ebene über der Ansicht, auch im Browser).</summary>
    public ObservableCollection<SearchGameEntry> SearchGameEntries { get; } = [];

    [ObservableProperty] bool _isChoosingSearchGame;

    TaskCompletionSource<bool>? _searchGameChoice;

    [RelayCommand] void ApplySearchGame() => _searchGameChoice?.TrySetResult(true);
    [RelayCommand] void CancelSearchGame() => _searchGameChoice?.TrySetResult(false);

    /// <summary>
    /// Legt ein Suchspiel an bzw. erneuert es: Auswahl der Flächen und ihrer Namen, dann Skripte und Texte
    /// (<see cref="SearchGame"/>); fehlen Lupe und Fragezeichen, kommen sie auf die aktuelle Seite.
    /// </summary>
    [RelayCommand]
    Task AddSearchGame() => Guard("Suchspiel", async () =>
    {
        var candidates = SearchGame.Candidates(Project);
        if (candidates.Count < 2)
        {
            Notice("Für ein Suchspiel braucht es mindestens zwei OID-Flächen, deren Skript nur einen Text oder eine "
                + "Aufnahme abspielt (z. B. nach „Personen erkennen“).");
            return;
        }
        bool exists = SearchGame.Exists(Project);
        SearchGameEntries.Clear();
        foreach (var (area, script, inGame) in candidates)
            SearchGameEntries.Add(new SearchGameEntry(script,
                string.IsNullOrWhiteSpace(area.Label) ? script.Name : area.Label.Trim(), !exists || inGame));
        _searchGameChoice = new TaskCompletionSource<bool>();
        IsChoosingSearchGame = true;
        List<SearchGame.Target> targets;
        try
        {
            if (!await _searchGameChoice.Task)
                return;
            targets = SearchGameEntries.Where(e => e.Include)
                .Select(e => new SearchGame.Target(e.Script, string.IsNullOrWhiteSpace(e.Name) ? e.Script.Name : e.Name.Trim()))
                .ToList();
        }
        finally
        {
            IsChoosingSearchGame = false;
            SearchGameEntries.Clear();
            _searchGameChoice = null;
        }
        if (targets.Count < 2)
        {
            Notice("Für ein Suchspiel bitte mindestens zwei Flächen auswählen.");
            return;
        }
        var added = SearchGame.Apply(Project, targets, Random.Shared);
        PlaceSearchGameSymbols(Project.SelectedPage ?? Project.Pages.FirstOrDefault());
        Notice($"Suchspiel mit {targets.Count} Flächen angelegt"
            + (added.Count > 0 ? $", {added.Count} neue Texte (Tab „Sprachausgabe“)" : "")
            + ". Antippen der Lupe startet es, das Fragezeichen wiederholt die Frage. Im Testmodus (F8) ausprobieren.");
    });

    /// <summary>Lupe und Fragezeichen unten rechts auf die Seite, sofern noch keine Fläche sie abspielt.</summary>
    void PlaceSearchGameSymbols(PageViewModel? page)
    {
        if (page == null)
            return;
        const double size = Symbols.SizeMm, margin = 10, gap = 5;
        var missing = new[] { Symbols.Search, Symbols.Ask }
            .Where(s => !Project.Pages.SelectMany(p => p.Areas).Any(a => a.IsScript && a.Script == s.Script))
            .ToList();
        var placed = new List<object>();
        for (int i = 0; i < missing.Count; i++)
        {
            var center = new Point(page.WidthMm - margin - size / 2 - (missing.Count - 1 - i) * (size + gap),
                page.HeightMm - margin - size / 2);
            if (InsertSymbolAt(page, missing[i], center) is { } inserted)
                placed.AddRange([inserted.Image, inserted.Area]);
        }
        if (placed.Count > 0)
            page.Select(placed);
    }
}
