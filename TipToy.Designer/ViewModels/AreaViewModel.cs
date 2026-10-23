using CommunityToolkit.Mvvm.ComponentModel;
using TipToy.Designer.Model;

namespace TipToy.Designer.ViewModels;

public record TargetOption(AreaTarget Value, string Name);

/// <summary>Eine Fläche auf einer Seite, die mit einem OID-Code bedruckt wird. Maße in mm.</summary>
public partial class AreaViewModel : ObservableObject
{
    public static IReadOnlyList<TargetOption> TargetOptions { get; } = new[]
    {
        new TargetOption(AreaTarget.Script, "Skript"),
        new TargetOption(AreaTarget.Product, "Produkt (Anmelden)"),
        new TargetOption(AreaTarget.Replay, "Wiederholen"),
        new TargetOption(AreaTarget.Stop, "Stopp"),
    };

    public AreaViewModel(ProjectViewModel project)
    {
        Project = project;
    }

    public ProjectViewModel Project { get; }

    [ObservableProperty] string _label = "";
    [ObservableProperty] double _x;
    [ObservableProperty] double _y;
    [ObservableProperty] double _width;
    [ObservableProperty] double _height;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetOption), nameof(IsScript), nameof(Oid), nameof(Caption))]
    AreaTarget _target;

    string _script = "";

    /// <summary>Name des Skripts. null wird ignoriert (ComboBox setzt kurzzeitig null beim Aktualisieren der Liste).</summary>
    public string? Script
    {
        get => _script;
        set
        {
            if (value != null && SetProperty(ref _script, value))
                RefreshOid();
        }
    }

    public bool IsScript => Target == AreaTarget.Script;

    public TargetOption TargetOption
    {
        get => TargetOptions.First(o => o.Value == Target);
        set
        {
            if (value != null)
                Target = value.Value;
        }
    }

    public int? Oid => Target switch
    {
        AreaTarget.Product => (int)Project.ProductId,
        AreaTarget.Replay => Project.Replay,
        AreaTarget.Stop => Project.Stop,
        _ => Project.Scripts.FirstOrDefault(s => s.Name == _script)?.Code,
    };

    public string Caption
    {
        get
        {
            var name = Target == AreaTarget.Script ? _script : TargetOption.Name;
            if (!string.IsNullOrWhiteSpace(Label))
                name = Label;
            return Oid is { } oid ? $"{name} ({oid})" : $"{name} (kein Code)";
        }
    }

    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(Caption));

    public void RefreshOid()
    {
        OnPropertyChanged(nameof(Oid));
        OnPropertyChanged(nameof(Caption));
    }

    public AreaEntry ToEntry() => new()
    {
        Label = Label, X = X, Y = Y, Width = Width, Height = Height, Target = Target, Script = _script,
    };

    public static AreaViewModel FromEntry(ProjectViewModel project, AreaEntry e) => new(project)
    {
        Label = e.Label, X = e.X, Y = e.Y, Width = e.Width, Height = e.Height, Target = e.Target, Script = e.Script,
    };
}
