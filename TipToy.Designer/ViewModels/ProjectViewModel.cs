using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TipToy.Designer.Model;
using TipToy.Designer.Services;

namespace TipToy.Designer.ViewModels;

public partial class ProjectViewModel : ObservableObject
{
    readonly Action<string> _log;

    public ProjectViewModel(Action<string> log)
    {
        _log = log;
    }

    [ObservableProperty] uint _productId = 900;
    [ObservableProperty] string _comment = "";
    [ObservableProperty] string _language = "de";
    [ObservableProperty] string _gmeLang = "";
    [ObservableProperty] string _init = "";
    [ObservableProperty] string _welcome = "";
    [ObservableProperty] string _mediaPath = "";
    [ObservableProperty] string _extraYaml = "";
    [ObservableProperty] int? _replay;
    [ObservableProperty] int? _stop;

    [ObservableProperty] string _tttoolPath = "tttool";
    [ObservableProperty] bool _useTttool;
    [ObservableProperty] int _dpi = 1200;
    [ObservableProperty] int _pixelSize = 2;

    [ObservableProperty] ScriptViewModel? _selectedScript;
    [ObservableProperty] SpeakViewModel? _selectedSpeak;
    [ObservableProperty] PageViewModel? _selectedPage;

    public ObservableCollection<ScriptViewModel> Scripts { get; } = new();
    public ObservableCollection<SpeakViewModel> Speak { get; } = new();
    public ObservableCollection<PageViewModel> Pages { get; } = new();

    /// <summary>Skriptnamen für die Auswahl im Flächen-Editor.</summary>
    public ObservableCollection<string> ScriptNames { get; } = new();

    public void Log(string message) => _log(message);

    partial void OnProductIdChanged(uint value) => RefreshAreas();
    partial void OnReplayChanged(int? value) => RefreshAreas();
    partial void OnStopChanged(int? value) => RefreshAreas();

    public void RefreshAreas()
    {
        foreach (var area in Pages.SelectMany(p => p.Areas))
            area.RefreshOid();
    }

    internal void OnScriptRenamed(ScriptViewModel script, string? oldName, string newName)
    {
        int idx = Scripts.IndexOf(script);
        if (idx < 0)
            return;
        ScriptNames[idx] = newName;
        foreach (var area in Pages.SelectMany(p => p.Areas).Where(a => a.Target == AreaTarget.Script && a.Script == oldName))
            area.Script = newName;
        if (int.TryParse(newName, out _))
            AssignCodes();
        RefreshAreas();
    }

    public ScriptViewModel AddScript(string name, string text = "")
    {
        var script = new ScriptViewModel(this) { Name = name, Text = text };
        Scripts.Add(script);
        ScriptNames.Add(name);
        return script;
    }

    public void RemoveScript(ScriptViewModel script)
    {
        int idx = Scripts.IndexOf(script);
        if (idx < 0)
            return;
        Scripts.RemoveAt(idx);
        ScriptNames.RemoveAt(idx);
        RefreshAreas();
    }

    public string UniqueScriptName(string prefix)
    {
        for (int i = 1; ; i++)
        {
            var name = $"{prefix}{i}";
            if (Scripts.All(s => s.Name != name))
                return name;
        }
    }

    /// <summary>Vergibt fehlende Codes (wie tttool) und übernimmt sie in die Skripte.</summary>
    public void AssignCodes()
    {
        var file = new ProjectFile { ProductId = ProductId, Replay = Replay, Stop = Stop };
        file.Scripts.AddRange(Scripts.Select(s => new ScriptEntry { Name = s.Name, Code = s.Code }));
        try
        {
            CodeAssigner.Assign(file);
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            return;
        }
        Replay = file.Replay;
        Stop = file.Stop;
        for (int i = 0; i < Scripts.Count; i++)
            Scripts[i].Code = file.Scripts[i].Code;
    }

    public ProjectFile ToFile(string baseDir) => new()
    {
        ProductId = ProductId,
        Comment = Comment,
        Language = Language,
        GmeLang = GmeLang,
        Init = Init,
        Welcome = Welcome,
        MediaPath = MediaPath,
        ExtraYaml = ExtraYaml,
        Replay = Replay,
        Stop = Stop,
        TttoolPath = TttoolPath,
        UseTttool = UseTttool,
        Dpi = Dpi,
        PixelSize = PixelSize,
        Scripts = Scripts.Select(s => s.ToEntry()).ToList(),
        Speak = Speak.Select(s => new SpeakEntry { Name = s.Name, Text = s.Text }).ToList(),
        Pages = Pages.Select(p => p.ToEntry(baseDir)).ToList(),
    };

    public static ProjectViewModel FromFile(ProjectFile f, string baseDir, Action<string> log)
    {
        var vm = new ProjectViewModel(log)
        {
            ProductId = f.ProductId,
            Comment = f.Comment,
            Language = f.Language,
            GmeLang = f.GmeLang,
            Init = f.Init,
            Welcome = f.Welcome,
            MediaPath = f.MediaPath,
            ExtraYaml = f.ExtraYaml,
            Replay = f.Replay,
            Stop = f.Stop,
            TttoolPath = string.IsNullOrWhiteSpace(f.TttoolPath) ? "tttool" : f.TttoolPath,
            UseTttool = f.UseTttool,
            Dpi = f.Dpi,
            PixelSize = f.PixelSize,
        };
        foreach (var s in f.Scripts)
        {
            var script = vm.AddScript(s.Name, string.Join("\n", s.Lines));
            script.Code = s.Code;
        }
        foreach (var s in f.Speak)
            vm.Speak.Add(new SpeakViewModel { Name = s.Name, Text = s.Text });
        foreach (var p in f.Pages)
            vm.Pages.Add(PageViewModel.FromEntry(vm, p, baseDir));
        if (vm.Pages.Count == 0)
            vm.Pages.Add(new PageViewModel(vm) { Name = "Seite 1" });

        vm.AssignCodes();
        vm.SelectedPage = vm.Pages.FirstOrDefault();
        vm.SelectedScript = vm.Scripts.FirstOrDefault();
        return vm;
    }
}
