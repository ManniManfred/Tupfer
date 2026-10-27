using Avalonia;

namespace Tupfer.Designer.ViewModels;

/// <summary>Ausrichten mehrerer Objekte bzw. eines Objekts an der Seite.</summary>
public enum AlignMode { Left, CenterX, Right, Top, CenterY, Bottom }

/// <summary>
/// Mehrfachauswahl: Flächen, Bilder und Texte lassen sich gemeinsam wählen, verschieben, löschen,
/// ausrichten und verteilen. Ist genau ein Objekt gewählt, steht es außerdem in
/// <see cref="SelectedArea"/> bzw. <see cref="SelectedElement"/> (Eigenschaften, Anfasser);
/// bei mehreren sind beide null.
/// </summary>
public partial class PageViewModel
{
    readonly List<object> _selection = new();

    /// <summary>Während die Einzelauswahl aus der Mehrfachauswahl gesetzt wird.</summary>
    bool _syncingSelection;

    /// <summary>Gewählte Flächen und Elemente in der Reihenfolge des Wählens.</summary>
    public IReadOnlyList<object> Selection => _selection;

    public bool IsMultiSelection => _selection.Count > 1;

    /// <summary>Verteilen braucht mindestens drei Objekte.</summary>
    public bool CanDistribute => _selection.Count >= 3;

    public string SelectionCaption => $"{_selection.Count} Objekte ausgewählt";

    /// <summary>Gleicht die Mehrfachauswahl an eine neu gesetzte Einzelauswahl an.</summary>
    void OnSingleSelectionChanged(object? item)
    {
        if (_syncingSelection)
            return;
        if (item != null)
            SetSelection([item]);
        else if (SelectedArea == null && SelectedElement == null)
            SetSelection([]);
    }

    void SetSelection(List<object> items)
    {
        _selection.Clear();
        _selection.AddRange(items.Distinct());
        _syncingSelection = true;
        try
        {
            SelectedArea = _selection is [AreaViewModel a] ? a : null;
            SelectedElement = _selection is [ElementViewModel e] ? e : null;
        }
        finally
        {
            _syncingSelection = false;
        }
        foreach (var element in Elements)
            element.IsSelected = _selection.Contains(element);
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(IsMultiSelection));
        OnPropertyChanged(nameof(CanDistribute));
        OnPropertyChanged(nameof(SelectionCaption));
        OnPropertyChanged(nameof(HasSelection));
    }

    public void Select(IEnumerable<object> items) => SetSelection(items.ToList());

    /// <summary>Nimmt ein Objekt in die Auswahl auf oder entfernt es daraus.</summary>
    public void ToggleSelection(object item)
    {
        var items = _selection.ToList();
        if (!items.Remove(item))
            items.Add(item);
        SetSelection(items);
    }

    /// <summary>Wählt alle Flächen und (wenn nicht gesperrt) alle Bilder und Texte.</summary>
    public void SelectAll(bool elements) =>
        SetSelection(Areas.Cast<object>().Concat(elements ? Elements : []).ToList());

    /// <summary>Entfernt alle gewählten Objekte von der Seite.</summary>
    public void DeleteSelection()
    {
        foreach (var item in _selection.ToList())
        {
            if (item is AreaViewModel a)
                Areas.Remove(a);
            else if (item is ElementViewModel e)
                Elements.Remove(e);
        }
        SetSelection([]);
    }

    /// <summary>Indizes der gewählten Flächen bzw. Elemente (für Rückgängig/Wiederholen).</summary>
    public (int[] Areas, int[] Elements) SelectionIndices() =>
        (_selection.OfType<AreaViewModel>().Select(a => Areas.IndexOf(a)).ToArray(),
         _selection.OfType<ElementViewModel>().Select(e => Elements.IndexOf(e)).ToArray());

    /// <summary>Umschließendes Rechteck auf der Seite (mm), bei Elementen auch gedreht.</summary>
    public static Rect BoundsOf(object item) => item switch
    {
        AreaViewModel a => new Rect(a.X, a.Y, a.Width, a.Height),
        ElementViewModel e => e.Bounds(),
        _ => default,
    };

    /// <summary>Verschiebt Flächen und Elemente; die Lage wird wie beim Ziehen auf 0,01 mm gerundet.</summary>
    public static void MoveBy(object item, double dx, double dy)
    {
        if (item is AreaViewModel a)
        {
            a.X = Math.Round(a.X + dx, 2);
            a.Y = Math.Round(a.Y + dy, 2);
        }
        else if (item is ElementViewModel e)
        {
            e.X = Math.Round(e.X + dx, 2);
            e.Y = Math.Round(e.Y + dy, 2);
        }
    }

    /// <summary>
    /// Richtet die Objekte aneinander aus (am umschließenden Rechteck aller); ein einzelnes Objekt
    /// wird an der Seite ausgerichtet.
    /// </summary>
    public void Align(IReadOnlyList<object> items, AlignMode mode)
    {
        if (items.Count == 0)
            return;
        var target = items.Count == 1
            ? new Rect(0, 0, WidthMm, HeightMm)
            : items.Select(BoundsOf).Aggregate((a, b) => a.Union(b));
        foreach (var item in items)
        {
            var b = BoundsOf(item);
            double dx = mode switch
            {
                AlignMode.Left => target.Left - b.Left,
                AlignMode.CenterX => target.Center.X - b.Center.X,
                AlignMode.Right => target.Right - b.Right,
                _ => 0,
            };
            double dy = mode switch
            {
                AlignMode.Top => target.Top - b.Top,
                AlignMode.CenterY => target.Center.Y - b.Center.Y,
                AlignMode.Bottom => target.Bottom - b.Bottom,
                _ => 0,
            };
            MoveBy(item, dx, dy);
        }
    }

    /// <summary>
    /// Verteilt mindestens drei Objekte mit gleichen Abständen: Das erste und das letzte
    /// (von links bzw. oben) bleiben stehen, die übrigen rücken dazwischen.
    /// </summary>
    public void Distribute(IReadOnlyList<object> items, bool horizontal)
    {
        if (items.Count < 3)
            return;
        double Start(Rect r) => horizontal ? r.Left : r.Top;
        double Size(Rect r) => horizontal ? r.Width : r.Height;
        var sorted = items.Select(i => (Item: i, Bounds: BoundsOf(i)))
            .OrderBy(x => Start(x.Bounds) + Size(x.Bounds) / 2).ToList();
        var first = sorted[0].Bounds;
        var last = sorted[^1].Bounds;
        double gap = (Start(last) + Size(last) - Start(first) - sorted.Sum(x => Size(x.Bounds))) / (sorted.Count - 1);
        double pos = Start(first) + Size(first) + gap;
        foreach (var (item, bounds) in sorted.Skip(1).SkipLast(1))
        {
            double d = pos - Start(bounds);
            MoveBy(item, horizontal ? d : 0, horizontal ? 0 : d);
            pos += Size(bounds) + gap;
        }
    }
}
