using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
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
        History = new PageHistory(this);
    }

    [ObservableProperty] uint _productId = 900;
    [ObservableProperty] string _comment = "";
    [ObservableProperty] string _language = "de";
    [ObservableProperty] string _gmeLang = "";
    [ObservableProperty] string _init = "";
    [ObservableProperty] string _welcome = "";
    [ObservableProperty] string _mediaPath = "";
    [ObservableProperty] string _extraYaml = "";
    [ObservableProperty] string _program = "";
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

    /// <summary>Rückgängig/Wiederholen für das Bearbeiten der Seiten.</summary>
    public PageHistory History { get; }

    partial void OnSelectedPageChanged(PageViewModel? value) => History.Attach(value);

    readonly Dictionary<string, Bitmap?> _images = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lädt ein Bild (zwischengespeichert, damit z. B. Rückgängig nicht neu lädt). null, wenn es fehlt.</summary>
    public Bitmap? LoadImage(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (_images.TryGetValue(path, out var bmp))
            return bmp;
        try
        {
            bmp = new Bitmap(path);
        }
        catch (Exception ex)
        {
            // Wird beim Zeichnen aufgerufen – das Protokoll erst danach ändern
            var message = $"Bild '{path}' konnte nicht geladen werden: {ex.Message}";
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Log(message));
        }
        _images[path] = bmp;
        return bmp;
    }

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
        if (!Scripts.Contains(script))
            return;
        int idx = ScriptNames.IndexOf(oldName ?? "");
        if (idx >= 0)
            ScriptNames[idx] = newName;
        foreach (var area in Pages.SelectMany(p => p.Areas).Where(a => a.Target == AreaTarget.Script && a.Script == oldName))
            area.Script = newName;
        if (int.TryParse(newName, out _))
            AssignCodes();
        RefreshAreas();
    }

    /// <param name="helper">Hilfsskript des C#-Programms: erscheint nicht in der Auswahl für Flächen.</param>
    public ScriptViewModel AddScript(string name, string text = "", bool helper = false)
    {
        var script = new ScriptViewModel(this) { Name = name, Text = text, Helper = helper };
        Scripts.Add(script);
        if (!helper)
            ScriptNames.Add(name);
        return script;
    }

    public void RemoveScript(ScriptViewModel script)
    {
        if (!Scripts.Remove(script))
            return;
        ScriptNames.Remove(script.Name);
        RefreshAreas();
    }

    /// <summary>
    /// Ersetzt die vom C#-Programm erzeugten Skripte. Codes bleiben pro Name erhalten,
    /// damit gedruckte Seiten gültig bleiben. Liefert eine Fehlermeldung oder null.
    /// </summary>
    public string? ApplyProgram(Compiler.CompileResult result)
    {
        var manual = Scripts.Where(s => !s.Generated).Select(s => s.Name).ToHashSet();
        var clash = result.Scripts.FirstOrDefault(s => manual.Contains(s.Name));
        if (clash != null)
            return $"Das C#-Programm erzeugt das Skript „{clash.Name}“, das es schon als normales Skript gibt. Bitte eines umbenennen.";

        // Vorhandene Skripte gleichen Namens werden aktualisiert statt ersetzt, damit Code und
        // Auswahl in den Flächen erhalten bleiben
        var existing = Scripts.Where(s => s.Generated).ToDictionary(s => s.Name);
        var selected = SelectedScript?.Name;
        foreach (var old in existing.Values.Where(s => result.Scripts.All(cs => cs.Name != s.Name)).ToList())
            RemoveScript(old);
        foreach (var cs in result.Scripts)
        {
            bool helper = !result.EntryPoints.Contains(cs.Name);
            var text = string.Join("\n", cs.Lines);
            if (existing.TryGetValue(cs.Name, out var script) && script.Helper == helper)
            {
                script.Text = text;
                continue;
            }
            int? code = script?.Code;
            if (script != null)
                RemoveScript(script);
            script = AddScript(cs.Name, text, helper);
            script.Generated = true;
            script.Code = code;
        }
        Init = result.Init;
        AssignCodes();
        RefreshAreas();
        SelectedScript = Scripts.FirstOrDefault(s => s.Name == selected) ?? Scripts.FirstOrDefault();
        return null;
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
        Program = Program,
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
            Program = f.Program ?? "",
            Replay = f.Replay,
            Stop = f.Stop,
            TttoolPath = string.IsNullOrWhiteSpace(f.TttoolPath) ? "tttool" : f.TttoolPath,
            UseTttool = f.UseTttool,
            Dpi = f.Dpi,
            PixelSize = f.PixelSize,
        };
        foreach (var s in f.Scripts)
        {
            var script = vm.AddScript(s.Name, string.Join("\n", s.Lines), s.Helper);
            script.Code = s.Code;
            script.Generated = s.Generated;
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
