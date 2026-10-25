using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Tupfer.Designer.Model;

namespace Tupfer.Designer.ViewModels;

/// <summary>
/// Rückgängig/Wiederholen beim Bearbeiten der Seiten. Vor jeder Änderung der gewählten Seite wird
/// ihr bisheriger Zustand gespeichert. Aufeinanderfolgende Änderungen derselben Eigenschaft
/// (z. B. Tippen in einem Textfeld) ergeben einen Schritt, ebenso alles innerhalb von <see cref="Change"/>
/// bzw. zwischen <see cref="BeginChange"/> und <see cref="EndChange"/>.
/// </summary>
public partial class PageHistory : ObservableObject
{
    const int MaxSteps = 200;

    static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    record Step(PageViewModel Page, string State, int Area, int Element);

    readonly ProjectViewModel _project;
    readonly List<Step> _undo = new();
    readonly List<Step> _redo = new();
    PageViewModel? _page;

    /// <summary>Zustand von _page nach der letzten erfassten Änderung.</summary>
    string _state = "";
    (object Sender, string Property)? _lastChange;
    int _batch;
    Step? _batchStart;
    bool _restoring;

    [ObservableProperty] bool _canUndo;
    [ObservableProperty] bool _canRedo;

    public PageHistory(ProjectViewModel project)
    {
        _project = project;
    }

    /// <summary>Beobachtet die gewählte Seite.</summary>
    public void Attach(PageViewModel? page)
    {
        if (_page != null)
            _page.Changed -= OnChanged;
        _page = page;
        _lastChange = null;
        if (page != null)
        {
            page.Changed += OnChanged;
            _state = Serialize(page);
        }
    }

    static string Serialize(PageViewModel page) => JsonSerializer.Serialize(page.ToEntry(null), Options);

    Step Capture(PageViewModel page) =>
        new(page, _state, page.SelectedArea is { } a ? page.Areas.IndexOf(a) : -1,
            page.SelectedElement is { } e ? page.Elements.IndexOf(e) : -1);

    void OnChanged(object sender, string? property)
    {
        if (_restoring || _page == null || _batch > 0)
            return;
        if (property is nameof(PageViewModel.SelectedArea) or nameof(PageViewModel.SelectedElement))
        {
            // Nach einem Wechsel der Auswahl beginnt ein neuer Schritt
            _lastChange = null;
            return;
        }
        var state = Serialize(_page);
        if (state == _state)
            return;
        (object, string)? key = property == null ? null : (sender, property);
        if (key == null || !key.Equals(_lastChange))
            Push(Capture(_page));
        _lastChange = key;
        _state = state;
    }

    void Push(Step step)
    {
        _undo.Add(step);
        if (_undo.Count > MaxSteps)
            _undo.RemoveAt(0);
        _redo.Clear();
        Update();
    }

    void Update()
    {
        CanUndo = _undo.Count > 0;
        CanRedo = _redo.Count > 0;
    }

    public void BeginChange()
    {
        if (_batch++ == 0 && _page != null)
            _batchStart = Capture(_page);
    }

    public void EndChange()
    {
        if (--_batch > 0 || _page == null)
            return;
        var state = Serialize(_page);
        if (state != _state && _batchStart?.Page == _page)
            Push(_batchStart);
        _state = state;
        _lastChange = null;
        _batchStart = null;
    }

    /// <summary>Führt mehrere Änderungen als einen Schritt aus.</summary>
    public void Change(Action action)
    {
        BeginChange();
        try
        {
            action();
        }
        finally
        {
            EndChange();
        }
    }

    public void Undo() => Apply(_undo, _redo);
    public void Redo() => Apply(_redo, _undo);

    void Apply(List<Step> from, List<Step> to)
    {
        while (from.Count > 0)
        {
            var step = from[^1];
            from.RemoveAt(from.Count - 1);
            // Schritte gelöschter Seiten überspringen
            if (!_project.Pages.Contains(step.Page))
                continue;
            _project.SelectedPage = step.Page;
            to.Add(Capture(step.Page));
            _restoring = true;
            try
            {
                step.Page.Restore(JsonSerializer.Deserialize<PageEntry>(step.State, Options)!, null, step.Area, step.Element);
            }
            finally
            {
                _restoring = false;
            }
            _state = Serialize(step.Page);
            _lastChange = null;
            break;
        }
        Update();
    }
}
