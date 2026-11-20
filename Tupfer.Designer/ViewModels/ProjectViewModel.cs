using Tupfer.Designer.Gme;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

public partial class ProjectViewModel : ObservableObject
{
    readonly Action<string> _log;

    public ProjectViewModel(Action<string> log)
    {
        _log = log;
        History = new PageHistory(this);
        // Texte kennen ihr Projekt, damit Umbenennen die Skripte nachzieht
        Speak.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                return;
            foreach (SpeakViewModel speak in e.NewItems ?? Array.Empty<SpeakViewModel>())
                speak.Project = this;
        };
    }

    [ObservableProperty] uint _productId = 900;
    [ObservableProperty] string _comment = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VoiceOptions), nameof(SpeakVoiceOptions))]
    string _language = "de";

    /// <summary>Stimme der natürlichen Sprachausgabe (Id aus Gme.Voices); leer = Standard der Sprache.</summary>
    [ObservableProperty] string _voice = "";

    /// <summary>Auswahl für die Projektstimme: Standard und die Stimmen der Sprache.</summary>
    public IReadOnlyList<VoiceOption> VoiceOptions =>
        [new("", Voices.Default(Language) is { } d ? $"Standard ({d.Name})" : "Standard"), .. Voices.For(Language)];

    /// <summary>Auswahl für die Stimme eines Textes: wie das Projekt oder eine Stimme der Sprache.</summary>
    public IReadOnlyList<VoiceOption> SpeakVoiceOptions => [new("", "Projektstimme"), .. Voices.For(Language)];
    [ObservableProperty] string _gmeLang = "";
    [ObservableProperty] string _init = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WelcomeProblem), nameof(HasWelcomeProblem))]
    string _welcome = "";

    /// <summary>Die Begrüßung ist keine Liste von Namen (z. B. ein ganzer Satz); sonst null.</summary>
    public string? WelcomeProblem
    {
        get
        {
            try
            {
                ScriptParser.ParsePlayList(Welcome);
                return null;
            }
            catch (ScriptParseException ex)
            {
                return $"Hier stehen Namen von Texten aus dem Tab „Sprachausgabe“ (z. B. willkommen), kein Satz ({ex.Message}). Beim Bauen wird der Satz automatisch als Text angelegt.";
            }
        }
    }

    public bool HasWelcomeProblem => WelcomeProblem != null;

    /// <summary>Legt den Satz in der Begrüßung als neuen Text an und trägt dessen Namen ein.</summary>
    [RelayCommand]
    void WelcomeAsText()
    {
        if (!HasWelcomeProblem)
            return;
        var speak = AddSpeak("begruessung");
        speak.Text = Welcome.Trim();
        Welcome = speak.Name;
    }
    [ObservableProperty] string _mediaPath = "";
    [ObservableProperty] string _extraYaml = "";
    [ObservableProperty] int? _replay;
    [ObservableProperty] int? _stop;

    [ObservableProperty] string _tttoolPath = "tttool";
    [ObservableProperty] bool _useTttool;
    [ObservableProperty] bool _denoiseRecordings = true;
    [ObservableProperty] int _dpi = 1200;
    [ObservableProperty] int _pixelSize = 2;
    [ObservableProperty] bool _cmykWithoutBlack;

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

    readonly Dictionary<string, IImage?> _images = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bilder, die sich nicht laden ließen, mit dem Stand der Datei damals (ändert sie sich, neuer Versuch).</summary>
    readonly Dictionary<string, DateTime> _failedImages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lädt ein Bild (zwischengespeichert, damit z. B. Rückgängig nicht neu lädt): eine Rastergrafik als
    /// <see cref="Bitmap"/>, eine SVG-Grafik als <see cref="SvgImage"/>. null, wenn es fehlt.
    /// </summary>
    public IImage? LoadImage(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (_images.TryGetValue(path, out var bmp))
            return bmp;
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_failedImages.TryGetValue(path, out var failed) && failed == stamp)
            return null;
        try
        {
            bmp = SvgImage.IsSvg(path) ? SvgImage.Load(path) : new Bitmap(path);
        }
        catch (Exception ex)
        {
            // Wird beim Zeichnen aufgerufen – das Protokoll erst danach ändern
            var message = $"Bild '{path}' konnte nicht geladen werden: {ex.Message}";
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Log(message));
            _failedImages[path] = stamp;
            return null;
        }
        _failedImages.Remove(path);
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

    public ScriptViewModel AddScript(string name, string text = "")
    {
        var script = new ScriptViewModel(this) { Name = name, Text = text };
        Scripts.Add(script);
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
    /// Skripte und Texte, die nichts mehr benutzt (nur unter <paramref name="scripts"/> bzw. <paramref name="speak"/>,
    /// null = alle). Ein Skript ist benutzt, wenn eine OID-Fläche es abspielt oder sein Name in einem anderen
    /// benutzten Skript, in Init, Begrüßung oder Zusatz-YAML vorkommt; ein Text, wenn sein Name in einem
    /// benutzten Skript, in Init, Begrüßung oder Zusatz-YAML vorkommt. Im Zweifel gilt etwas als benutzt.
    /// </summary>
    public (List<ScriptViewModel> Scripts, List<SpeakViewModel> Speak) FindUnused(
        IEnumerable<ScriptViewModel>? scripts = null, IEnumerable<SpeakViewModel>? speak = null)
    {
        var areaScripts = Pages.SelectMany(p => p.Areas).Where(a => a.IsScript).Select(a => a.Script ?? "").ToHashSet();
        string fixedText = string.Join("\n", Init, Welcome, ExtraYaml);
        static bool Mentions(string text, string name) =>
            name.Length > 0 && Regex.IsMatch(text, $@"(?<![\w$.]){Regex.Escape(name)}(?![\w])");

        // Erst die Skripte: Ein Skript, das nur von unbenutzten Skripten aufgerufen wird, ist selbst unbenutzt
        var candidates = (scripts ?? Scripts).Where(Scripts.Contains).Distinct().ToList();
        var unused = candidates.Where(s => !areaScripts.Contains(s.Name) && !Mentions(fixedText, s.Name)).ToHashSet();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var script in unused.ToList())
                if (Scripts.Any(other => other != script && !unused.Contains(other) && Mentions(other.Text, script.Name)))
                {
                    unused.Remove(script);
                    changed = true;
                }
        }
        var remaining = string.Join("\n", Scripts.Where(s => !unused.Contains(s)).Select(s => s.Text).Append(fixedText));
        var unusedSpeak = (speak ?? Speak).Where(Speak.Contains).Distinct().Where(sp => !Mentions(remaining, sp.Name)).ToList();
        return (Scripts.Where(unused.Contains).ToList(), unusedSpeak);
    }

    /// <summary>Entfernt die Skripte und Texte (z. B. aus <see cref="FindUnused"/>); Aufnahmen bleiben als Datei liegen.</summary>
    public void Remove(IEnumerable<ScriptViewModel> scripts, IEnumerable<SpeakViewModel> speak)
    {
        foreach (var script in scripts.ToList())
            RemoveScript(script);
        foreach (var entry in speak.ToList())
            Speak.Remove(entry);
        if (SelectedScript != null && !Scripts.Contains(SelectedScript))
            SelectedScript = Scripts.FirstOrDefault();
        if (SelectedSpeak != null && !Speak.Contains(SelectedSpeak))
            SelectedSpeak = Speak.FirstOrDefault();
    }

    /// <summary>Zeile eines Skripts, das nur einen Text abspielt.</summary>
    static string PlayLine(string speakName) => $"P({speakName})";

    /// <summary>Text, den das Skript abspielt, wenn es nur aus „P(name)“ besteht; sonst null.</summary>
    public SpeakViewModel? SpeakOfScript(string? scriptName)
    {
        if (Scripts.FirstOrDefault(s => s.Name == scriptName) is not { } script)
            return null;
        var lines = script.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return lines.Count == 1 ? Speak.FirstOrDefault(sp => PlayLine(sp.Name) == lines[0]) : null;
    }

    /// <summary>
    /// Skript, das nur diesen Text abspielt: ein vorhandenes („P(name)“) oder ein neues mit dem
    /// Namen des Texts. Liefert den Namen des Skripts.
    /// </summary>
    public string ScriptForSpeak(SpeakViewModel speak)
    {
        if (Scripts.FirstOrDefault(s => SpeakOfScript(s.Name) == speak) is { } existing)
            return existing.Name;
        var name = Scripts.Any(s => s.Name == speak.Name) || string.IsNullOrWhiteSpace(speak.Name)
            ? UniqueScriptName(string.IsNullOrWhiteSpace(speak.Name) ? "text" : speak.Name)
            : speak.Name;
        AddScript(name, PlayLine(speak.Name));
        AssignCodes();
        return name;
    }

    /// <summary>Gültiger neuer Name für einen Text: nicht leer, ohne Anführungszeichen im Skript nutzbar und noch frei.</summary>
    public bool IsValidSpeakName(SpeakViewModel speak, string name) =>
        name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '.')
        && Speak.All(s => s == speak || s.Name != name);

    /// <summary>
    /// Ein Text wurde umbenannt: Aufrufe in den Skripten (P, PA, P*, …) und in der Begrüßung anpassen; ein Skript,
    /// das so hieß wie der Text, heißt danach ebenfalls neu (die Flächen ziehen mit).
    /// </summary>
    internal void OnSpeakRenamed(string oldName, string newName)
    {
        if (oldName.Length == 0 || oldName == newName)
            return;
        foreach (var script in Scripts.ToList())
        {
            script.Text = PlayCall().Replace(script.Text, m =>
                m.Groups["head"].Value + RenameInList(m.Groups["names"].Value, oldName, newName) + ")");
            if (script.Name == oldName && !Scripts.Any(s => s.Name == newName))
                script.Name = newName;
        }
        Welcome = RenameInList(Welcome, oldName, newName);
        RefreshAreas();
    }

    [GeneratedRegex(@"(?<head>(?<![\w$.])PA?\*?\??\s*\()(?<names>[^)]*)\)")]
    private static partial Regex PlayCall();

    static string RenameInList(string list, string oldName, string newName) =>
        string.Join(",", list.Split(',').Select(item =>
        {
            var name = item.Trim();
            if (name != oldName && name != $"\"{oldName}\"")
                return item;
            return item.Replace(name, newName);
        }));

    /// <summary>Neuer, leerer Text mit eindeutigem Namen (aus <paramref name="prefix"/>, sonst „textN“).</summary>
    public SpeakViewModel AddSpeak(string? prefix = null)
    {
        var name = string.IsNullOrWhiteSpace(prefix) ? "" : prefix;
        if (name.Length == 0 || Speak.Any(s => s.Name == name))
        {
            var start = name.Length == 0 ? "text" : name;
            for (int i = name.Length == 0 ? Speak.Count + 1 : 2; ; i++)
                if (Speak.All(s => s.Name != start + i))
                {
                    name = start + i;
                    break;
                }
        }
        var speak = new SpeakViewModel { Name = name };
        Speak.Add(speak);
        return speak;
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
        Voice = string.IsNullOrEmpty(Voice) ? null : Voice,
        GmeLang = GmeLang,
        Init = Init,
        Welcome = Welcome,
        MediaPath = MediaPath,
        ExtraYaml = ExtraYaml,
        Replay = Replay,
        Stop = Stop,
        TttoolPath = TttoolPath,
        UseTttool = UseTttool,
        DenoiseRecordings = DenoiseRecordings,
        Dpi = Dpi,
        PixelSize = PixelSize,
        CmykWithoutBlack = CmykWithoutBlack,
        Scripts = Scripts.Select(s => s.ToEntry()).ToList(),
        Speak = Speak.Select(s => new SpeakEntry
        {
            Name = s.Name,
            Text = s.Text,
            Voice = string.IsNullOrEmpty(s.Voice) ? null : s.Voice,
            Recording = s.HasRecording ? Path.GetRelativePath(baseDir, s.Recording!).Replace('\\', '/') : null,
        }).ToList(),
        Pages = Pages.Select(p => p.ToEntry(baseDir)).ToList(),
    };

    public static ProjectViewModel FromFile(ProjectFile f, string baseDir, Action<string> log)
    {
        var vm = new ProjectViewModel(log)
        {
            ProductId = f.ProductId,
            Comment = f.Comment,
            Language = f.Language,
            Voice = f.Voice ?? "",
            GmeLang = f.GmeLang,
            Init = f.Init,
            Welcome = f.Welcome,
            MediaPath = f.MediaPath,
            ExtraYaml = f.ExtraYaml,
            Replay = f.Replay,
            Stop = f.Stop,
            TttoolPath = string.IsNullOrWhiteSpace(f.TttoolPath) ? "tttool" : f.TttoolPath,
            UseTttool = f.UseTttool,
            DenoiseRecordings = f.DenoiseRecordings,
            Dpi = f.Dpi,
            PixelSize = f.PixelSize,
            CmykWithoutBlack = f.CmykWithoutBlack,
        };
        foreach (var s in f.Scripts)
        {
            var script = vm.AddScript(s.Name, string.Join("\n", s.Lines));
            script.Code = s.Code;
        }
        foreach (var s in f.Speak)
            vm.Speak.Add(new SpeakViewModel
            {
                Name = s.Name,
                Text = s.Text,
                Voice = s.Voice ?? "",
                Recording = string.IsNullOrEmpty(s.Recording) ? null : Path.GetFullPath(s.Recording, baseDir),
            });
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
