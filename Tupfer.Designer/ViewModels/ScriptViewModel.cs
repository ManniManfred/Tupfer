using CommunityToolkit.Mvvm.ComponentModel;
using Tupfer.Designer.Model;

namespace Tupfer.Designer.ViewModels;

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

    partial void OnNameChanged(string? oldValue, string newValue) => _project.OnScriptRenamed(this, oldValue, newValue);

    partial void OnCodeChanged(int? value) => _project.RefreshAreas();

    // Flächen zeigen an, ob ihr Skript nur einen Text abspielt
    partial void OnTextChanged(string value) => _project.RefreshAreas();

    public ScriptEntry ToEntry() => new()
    {
        Name = Name,
        Code = Code,
        Lines = Text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l)).ToList(),
    };
}

public partial class SpeakViewModel : ObservableObject
{
    /// <summary>Projekt, zu dem der Text gehört (wird beim Hinzufügen gesetzt).</summary>
    internal ProjectViewModel? Project { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditName))]
    string _name = "";

    partial void OnNameChanged(string? oldValue, string newValue) => Project?.OnSpeakRenamed(oldValue ?? "", newValue);

    /// <summary>Name zum Bearbeiten: Ungültige oder schon vergebene Namen werden verworfen.</summary>
    public string EditName
    {
        get => Name;
        set
        {
            var name = value.Trim();
            if (name != Name && (Project == null || Project.IsValidSpeakName(this, name)))
                Name = name;
            else
                OnPropertyChanged();
        }
    }
    [ObservableProperty] string _text = "";

    /// <summary>Eigene Stimme (Id aus Gme.Voices); leer = Stimme des Projekts.</summary>
    [ObservableProperty] string _voice = "";

    /// <summary>Eigene Aufnahme (absoluter Pfad); ersetzt beim Bauen die Sprachausgabe.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecording), nameof(RecordingName))]
    string? _recording;

    /// <summary>Für diesen Text läuft gerade eine Aufnahme.</summary>
    [ObservableProperty] bool _isRecording;

    /// <summary>Dieser Text wird gerade abgespielt (auch wenn angehalten).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotPlaying))]
    bool _isPlaying;

    /// <summary>Die Wiedergabe dieses Textes ist angehalten.</summary>
    [ObservableProperty] bool _isPaused;

    public bool IsNotPlaying => !IsPlaying;

    public bool HasRecording => !string.IsNullOrEmpty(Recording);

    public string RecordingName => HasRecording ? Path.GetFileName(Recording!) : "";
}
