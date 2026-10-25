using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TipToy.Designer.Gme;
using TipToy.Designer.Model;
using TipToy.Designer.Services;

namespace TipToy.Designer.ViewModels;

// Testmodus: Flächen antippen wie mit dem Stift
public partial class MainWindowViewModel
{
    const int MaxTraceLines = 200;

    readonly PenSimulator _pen = new();

    /// <summary>Zählt die Wiedergaben im Test; eine neue bricht die laufende Folge ab.</summary>
    int _testPlayId;

    /// <summary>Auf der Seite antippen führt das Skript aus, statt zu bearbeiten.</summary>
    [ObservableProperty] bool _testMode;

    /// <summary>Ablauf der letzten Antipp-Vorgänge (neueste oben).</summary>
    [ObservableProperty] string _testTrace = "";

    /// <summary>Aktuelle Registerwerte, eine Zeile je Register.</summary>
    [ObservableProperty] string _testRegisters = "";

    partial void OnTestModeChanged(bool value)
    {
        StopTestAudio();
        if (!value)
            return;
        if (Project.SelectedPage is { } page)
        {
            page.SelectedArea = null;
            page.SelectedElement = null;
        }
        TestTrace = "";
        _ = TestRestart();
    }

    [RelayCommand] void ToggleTestMode() => TestMode = !TestMode;

    /// <summary>Wie das Antippen des Produkt-Codes: Register zurücksetzen und Begrüßung abspielen.</summary>
    [RelayCommand]
    Task TestRestart() => RunTest(() => _pen.Start());

    [RelayCommand]
    Task TestReplay() => RunTest(() => new PenResult(_pen.LastMedia.ToList(), ["Wiederholen"], false));

    [RelayCommand]
    void TestStop()
    {
        StopTestAudio();
        AddTrace(["Stopp"]);
    }

    /// <summary>Eine Fläche wurde im Testmodus angetippt.</summary>
    [RelayCommand]
    Task TestTap(AreaViewModel area) => area.Target switch
    {
        AreaTarget.Product => TestRestart(),
        AreaTarget.Replay => TestReplay(),
        AreaTarget.Stop => StopTask(),
        _ when area.Script is { } script => RunTest(() => _pen.Tap(script)),
        _ => NoScript(area),
    };

    Task NoScript(AreaViewModel area)
    {
        AddTrace([$"Fläche „{area.Caption}“ ist keinem Skript zugeordnet"]);
        return Task.CompletedTask;
    }

    Task StopTask()
    {
        TestStop();
        return Task.CompletedTask;
    }

    Task RunTest(Func<PenResult> action) => Guard("Test", async () =>
    {
        if (!EnsureProgram())
            return;
        try
        {
            _pen.Load(Project.ToFile(BaseDir));
        }
        catch (ScriptParseException ex)
        {
            AddTrace([ex.Message]);
            Notice("Test: " + ex.Message);
            return;
        }
        var result = action();
        AddTrace(result.Trace);
        TestRegisters = _pen.Registers.Count == 0
            ? "(keine)"
            : string.Join(Environment.NewLine, _pen.Registers.Select(r => $"{r.Key} = {r.Value}"));
        if (result.Media.Count > 0)
            await PlayTestAsync(result.Media);
        else if (result.Stop)
            StopTestAudio();
    });

    void AddTrace(IEnumerable<string> lines)
    {
        var all = lines.Concat(TestTrace.Length == 0 ? [] : ["", .. TestTrace.Split(Environment.NewLine)]);
        TestTrace = string.Join(Environment.NewLine, all.Take(MaxTraceLines));
    }

    void StopTestAudio()
    {
        _testPlayId++;
        Audio.Device.StopPlayback();
    }

    /// <summary>Spielt die Audio-Namen nacheinander ab (Aufnahme, Sprachausgabe oder Datei wie in der GME-Datei).</summary>
    async Task PlayTestAsync(List<string> names)
    {
        StopTestAudio();
        int id = _testPlayId;
        var file = Project.ToFile(BaseDir);
        var language = string.IsNullOrWhiteSpace(Project.Language) ? "en" : Project.Language;
        var tts = new TextToSpeech(TtsCacheDir, LogFromAnyThread, Notice);
        await tts.PrepareAsync(language, file.Speak.Where(s => names.Contains(s.Name) && string.IsNullOrEmpty(s.Recording)).Select(s => s.Text));
        foreach (var name in names)
        {
            byte[] data;
            try
            {
                data = await Task.Run(() => GmeBuilder.LoadAudio(file, BaseDir, name, tts.Speak));
            }
            catch (GmeBuildException ex)
            {
                AddTrace([ex.Message]);
                continue;
            }
            if (id != _testPlayId)
                return;
            await Audio.Device.PlayAsync(data, Audio.ExtensionOf(data));
            if (id != _testPlayId)
                return;
        }
    }
}
