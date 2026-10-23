using CommunityToolkit.Mvvm.ComponentModel;
using TipToy.Designer.Model;

namespace TipToy.Designer.ViewModels;

public partial class ScriptViewModel : ObservableObject
{
    readonly ProjectViewModel _project;

    public ScriptViewModel(ProjectViewModel project)
    {
        _project = project;
    }

    [ObservableProperty] string _name = "";
    [ObservableProperty] int? _code;

    /// <summary>Skriptzeilen, eine pro Zeile.</summary>
    [ObservableProperty] string _text = "";

    /// <summary>Vom C#-Programm erzeugt – nicht von Hand bearbeitbar.</summary>
    [ObservableProperty] bool _generated;

    /// <summary>Hilfsskript des C#-Programms (wird nur per Sprung erreicht).</summary>
    [ObservableProperty] bool _helper;

    partial void OnNameChanged(string? oldValue, string newValue) => _project.OnScriptRenamed(this, oldValue, newValue);

    partial void OnCodeChanged(int? value) => _project.RefreshAreas();

    public ScriptEntry ToEntry() => new()
    {
        Name = Name,
        Code = Code,
        Generated = Generated,
        Helper = Helper,
        Lines = Text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l)).ToList(),
    };
}

public partial class SpeakViewModel : ObservableObject
{
    [ObservableProperty] string _name = "";
    [ObservableProperty] string _text = "";
}
