using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Compiler;
using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;
using Tupfer.Designer.Oid;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

/// <summary>Dateidialoge, werden vom Fenster bereitgestellt.</summary>
public interface IFileDialogs
{
    Task<string?> OpenAsync(string title, string filterName, params string[] patterns);
    Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension);

    /// <summary>Die Dateien wurden geschrieben (im Browser werden sie jetzt heruntergeladen).</summary>
    Task SavedAsync(params string[] paths) => Task.CompletedTask;

    /// <summary>Ein neues, leeres Projekt wurde angelegt.</summary>
    void ProjectCreated() { }

    /// <summary>
    /// Druckt die PDF-Datei direkt (Desktop: Standarddrucker, Browser: Druckdialog) und löscht sie danach,
    /// sobald sie nicht mehr gebraucht wird. Liefert einen Hinweis, wie es weitergeht.
    /// </summary>
    Task<string> PrintAsync(string pdfPath);

    /// <summary>
    /// Speichert eine Datei auf den Stift: erst wird das Ziel gewählt (Laufwerk „tiptoi“ oder Dialog),
    /// dann erzeugt <paramref name="create"/> die Datei (null = Fehler) und sie wird dorthin kopiert.
    /// Liefert den Zielnamen, oder null bei Abbruch oder Fehler.
    /// </summary>
    Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create);
}

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
        _project = CreateDefaultProject(Log);
    }

    public IFileDialogs? Dialogs { get; set; }

    /// <summary>false im Browser: dort gibt es keine externen Programme wie tttool.</summary>
    public static bool IsDesktop => !OperatingSystem.IsBrowser();

    /// <summary>Beispielprojekte unter „Datei → Beispiele“; leer, wenn keine mitgeliefert werden.</summary>
    public static IReadOnlyList<ExampleProject> Examples { get; set; } = [];

    public static bool HasExamples => Examples.Count > 0;

    /// <summary>Öffnet die Download-Seite mit den Setups (nur im Browser, sonst null).</summary>
    public static Action? OpenDownloads { get; set; }

    public static bool HasDownloads => OpenDownloads != null;

    /// <summary>false in Browsern ohne Speichern-Dialog (z. B. auf dem Handy): Dort gibt es „Auf den Stift speichern“ nicht.</summary>
    public static bool CanSaveToPen { get; set; } = true;

    /// <summary>Stimmen lassen sich wählen (nur mit der natürlichen Sprachausgabe im Browser).</summary>
    public static bool HasVoices => TextToSpeech.NaturalSynthesizer != null;

    [ObservableProperty] ProjectViewModel _project;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    string? _filePath;

    [ObservableProperty] string _logText = "";
    /// <summary>Kleinster und größter Zoom (20 % … 1000 %).</summary>
    public const double MinZoom = 0.2, MaxZoom = 10;

    double _zoom = 1.0;
    /// <summary>Zoom der Seitenansicht, begrenzt auf <see cref="MinZoom"/> … <see cref="MaxZoom"/>.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            if (SetProperty(ref _zoom, Math.Clamp(value, MinZoom, MaxZoom)))
                OnPropertyChanged(nameof(ZoomText));
        }
    }

    /// <summary>Zoom als bearbeitbarer Text in Prozent, z. B. „150 %“. Ungültige Eingaben werden verworfen.</summary>
    public string ZoomText
    {
        get => $"{Math.Round(Zoom * 100)} %";
        set
        {
            var text = (value ?? "").Replace("%", "").Trim().Replace(',', '.');
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var percent) && percent > 0)
                Zoom = percent / 100;
            // Text immer neu anzeigen (auch nach ungültiger oder begrenzter Eingabe)
            OnPropertyChanged();
        }
    }
    [ObservableProperty] bool _printElements = true;

    /// <summary>Auf der Seite nur OID-Flächen bearbeiten, Bilder und Texte sind gesperrt.</summary>
    [ObservableProperty] bool _areasOnly;

    /// <summary>Klicken auf der Seite nimmt Objekte in die Auswahl auf bzw. entfernt sie (für Touch).</summary>
    [ObservableProperty] bool _multiSelect;

    /// <summary>Form neuer OID-Flächen; <c>null</c>: kein Werkzeug, Ziehen auf freier Stelle legt keine Fläche an.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RectangleTool), nameof(EllipseTool), nameof(PolygonTool))]
    AreaShape? _areaTool = AreaShape.Rectangle;

    // Für die Werkzeugknöpfe: Einschalten wählt die Form, Ausschalten schaltet das Werkzeug ab
    public bool RectangleTool { get => AreaTool == AreaShape.Rectangle; set => SetTool(AreaShape.Rectangle, value); }
    public bool EllipseTool { get => AreaTool == AreaShape.Ellipse; set => SetTool(AreaShape.Ellipse, value); }
    public bool PolygonTool { get => AreaTool == AreaShape.Polygon; set => SetTool(AreaShape.Polygon, value); }

    /// <summary>Die Zeichenwerkzeuge legen Farbflächen statt OID-Flächen an.</summary>
    [ObservableProperty] bool _drawColorShapes;

    partial void OnDrawColorShapesChanged(bool value)
    {
        if (!value)
            return;
        // Farbflächen sind Elemente: dürfen nicht gesperrt sein, und ein Werkzeug muss gewählt sein
        AreasOnly = false;
        AreaTool ??= AreaShape.Rectangle;
    }

    partial void OnAreasOnlyChanged(bool value)
    {
        if (value)
            DrawColorShapes = false;
    }

    void SetTool(AreaShape shape, bool on)
    {
        if (on)
            AreaTool = shape;
        else if (AreaTool == shape)
            AreaTool = null;
    }

    /// <summary>Die Anfasser eines Bilds schneiden zu statt zu skalieren.</summary>
    [ObservableProperty] bool _cropMode;
    [ObservableProperty] int _selectedTab;
    [ObservableProperty] string _programErrors = "";
    [ObservableProperty] string _programOutput = "";

    const int ProgramTab = 2;

    /// <summary>Programmversion (aus Directory.Build.props), z. B. „1.2.0“.</summary>
    public static string Version { get; } = typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    /// <summary>Programmname mit Version.</summary>
    public static string AppName { get; } = $"Tupfer Designer {Version}";

    public string Title => $"{AppName} – {(FilePath == null ? "Neues Projekt" : Path.GetFileName(FilePath))}";

    string BaseDir => FilePath != null ? Path.GetDirectoryName(FilePath)! : Environment.CurrentDirectory;

    /// <summary>Zwischenspeicher der Sprachausgabe beim Anhören; ungespeicherte Projekte haben noch kein Verzeichnis.</summary>
    string TtsCacheDir => FilePath == null && IsDesktop
        ? Path.Combine(Path.GetTempPath(), "tupfer-tts-cache")
        : Path.Combine(BaseDir, "tts-cache");

    public void Log(string message) => LogText += message + Environment.NewLine;

    /// <summary>Protokoll unten anzeigen (Menü Erstellen); Fehler und Hinweise blenden es ein.</summary>
    [ObservableProperty] bool _showLog;

    /// <summary>Meldung, die gelesen werden soll (Fehler, Hinweis, Ergebnis): blendet das Protokoll ein.</summary>
    void Notice(string message)
    {
        Log(message);
        ShowLog = true;
    }

    /// <summary>Inhalt eines neuen Projekts (wie „Datei → Neu“), z. B. zum Anlegen einer Projektdatei.</summary>
    public static ProjectFile DefaultProjectFile() => CreateDefaultProject(_ => { }).ToFile(Environment.CurrentDirectory);

    static ProjectViewModel CreateDefaultProject(Action<string> log)
    {
        var p = new ProjectViewModel(log) { Comment = "Mein tiptoi-Buch", Welcome = "willkommen" };
        p.Speak.Add(new SpeakViewModel { Name = "willkommen", Text = "Hallo! Tippe auf ein Bild." });
        p.Speak.Add(new SpeakViewModel { Name = "hallo", Text = "Das ist ein Beispiel." });
        p.AddScript("beispiel", "P(hallo)");
        p.Pages.Add(new PageViewModel(p) { Name = "Seite 1" });
        p.AssignCodes();
        p.SelectedPage = p.Pages[0];
        p.SelectedScript = p.Scripts[0];
        return p;
    }

    async Task Guard(string action, Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            Notice($"Fehler bei {action}: {ex.Message}");
        }
    }

    // ---------- Datei ----------

    [RelayCommand]
    void NewProject()
    {
        Project = CreateDefaultProject(Log);
        FilePath = null;
        Dialogs?.ProjectCreated();
        ProgramErrors = ProgramOutput = "";
        Log("Neues Projekt angelegt.");
    }

    // ---------- C#-Programm ----------

    /// <summary>Übersetzt das C#-Programm, falls vorhanden. false bei Fehlern.</summary>
    /// <summary>Ohne Übersetzer (Web-App) werden die zuletzt übersetzten, gespeicherten Skripte verwendet.</summary>
    bool EnsureProgram() => string.IsNullOrWhiteSpace(Project.Program) || !ProgramCompiler.IsAvailable || TryCompileProgram(quiet: true);

    [RelayCommand]
    void CompileProgram()
    {
        if (!ProgramCompiler.IsAvailable)
            return;
        if (string.IsNullOrWhiteSpace(Project.Program))
        {
            ProgramErrors = "Noch kein Programm – „Beispiel einfügen“ zeigt, wie es geht.";
            return;
        }
        TryCompileProgram(quiet: false);
    }

    bool TryCompileProgram(bool quiet)
    {
        var result = ProgramCompiler.Compile(Project.Program);
        if (!result.Success)
        {
            ProgramErrors = string.Join(Environment.NewLine, result.Errors);
            Notice($"C#-Programm: {result.Errors.Count} Fehler – siehe Tab „Programm (C#)“.");
            SelectedTab = ProgramTab;
            return false;
        }
        if (Project.ApplyProgram(result) is { } error)
        {
            ProgramErrors = error;
            Notice(error);
            SelectedTab = ProgramTab;
            return false;
        }
        int helpers = result.Scripts.Count - result.EntryPoints.Count;
        ProgramErrors = $"Keine Fehler. {result.EntryPoints.Count} Skripte zum Antippen, {helpers} Hilfsskripte.";
        var sb = new System.Text.StringBuilder();
        if (result.Init != "")
            sb.AppendLine($"init: {result.Init}").AppendLine();
        foreach (var s in result.Scripts)
        {
            var code = Project.Scripts.FirstOrDefault(x => x.Name == s.Name)?.Code;
            sb.AppendLine($"{s.Name}:   # OID {code}{(result.EntryPoints.Contains(s.Name) ? "" : ", Hilfsskript")}");
            foreach (var l in s.Lines)
                sb.AppendLine($"- {l}");
        }
        ProgramOutput = sb.ToString();
        if (!quiet)
            Log($"C#-Programm übersetzt: {result.EntryPoints.Count} Skripte zum Antippen, {helpers} Hilfsskripte.");
        return true;
    }

    [RelayCommand]
    void InsertProgramExample()
    {
        if (!string.IsNullOrWhiteSpace(Project.Program))
        {
            Notice("Das Programm ist nicht leer – das Beispiel wird nur in ein leeres Programm eingefügt.");
            return;
        }
        Project.Program = ProgramExample;
    }

    const string ProgramExample = """
        // Jede Methode mit [Oid] wird ein Skript zum Antippen (in „Seiten“ einer Fläche zuordnen).
        // Felder der Klasse sind die Register des Stifts: ganze Zahlen von 0 bis 65535 oder bool.
        // Play("name") spielt einen Text aus „Sprachausgabe“ oder eine Audiodatei.

        class MeinSpiel : TipToi
        {
            int punkte;

            [Oid] void Apfel()
            {
                if (punkte < 5)
                {
                    punkte++;
                    Play("richtig");
                }
                else
                {
                    punkte = 0;
                    Play("fertig");
                }
            }
        }
        """;

    void AfterLoad()
    {
        ProgramErrors = ProgramOutput = "";
        if (string.IsNullOrWhiteSpace(Project.Program))
            return;
        if (ProgramCompiler.IsAvailable)
            TryCompileProgram(quiet: true);
        else
            Log("Hinweis: Das C#-Programm lässt sich hier nicht bearbeiten – es werden die zuletzt übersetzten Skripte verwendet.");
    }

    // Im Browser kehrt die Dateiauswahl nach Abbrechen teils nie zurück: Befehl trotzdem freigeben
    [RelayCommand(AllowConcurrentExecutions = true)]
    Task OpenProject() => Guard("Öffnen", async () =>
    {
        var path = await Dialogs!.OpenAsync("Projekt öffnen", "Tupfer-Projekt", "*" + ProjectStore.Extension);
        if (path != null)
            LoadProject(path);
    });

    [RelayCommand]
    void ShowDownloads() => OpenDownloads?.Invoke();

    [RelayCommand]
    Task OpenExample(ExampleProject example) => Guard("Öffnen", async () =>
    {
        Log($"Lade „{example.Title}“ …");
        Load(await example.FetchAsync());
    });

    /// <summary>Öffnet ein Projekt oder importiert eine tttool-YAML-Datei (z. B. von der Kommandozeile).</summary>
    public void Load(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                LoadYaml(path);
            else
                LoadProject(path);
        }
        catch (Exception ex)
        {
            Notice($"Fehler beim Öffnen von {path}: {ex.Message}");
        }
    }

    void LoadProject(string path)
    {
        var file = ProjectStore.Load(path);
        Project = ProjectViewModel.FromFile(file, Path.GetDirectoryName(path)!, Log);
        FilePath = path;
        Log($"Projekt geladen: {path}");
        AfterLoad();
    }

    void LoadYaml(string path)
    {
        var file = TttoolYaml.Import(path);
        Project = ProjectViewModel.FromFile(file, Path.GetDirectoryName(path)!, Log);
        FilePath = null;
        AfterLoad();
        Log($"Importiert: {path} ({file.Scripts.Count} Skripte, {file.Speak.Count} Texte).");
        if (!string.IsNullOrWhiteSpace(file.ExtraYaml))
            Log("Hinweis: Nicht unterstützte Einträge wurden unter Projekt → Weitere YAML-Einträge übernommen.");
    }

    [RelayCommand]
    Task SaveProject() => Guard("Speichern", async () =>
    {
        if (FilePath == null)
        {
            await SaveProjectAs();
            return;
        }
        Project.AssignCodes();
        ProjectStore.Save(Project.ToFile(BaseDir), FilePath);
        await Dialogs!.SavedAsync(FilePath);
        Log($"Gespeichert: {FilePath}");
    });

    [RelayCommand]
    Task SaveProjectAs() => Guard("Speichern", async () =>
    {
        var path = await Dialogs!.SaveAsync("Projekt speichern", "projekt" + ProjectStore.Extension, "Tupfer-Projekt", ProjectStore.Extension);
        if (path == null) return;
        if (!path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
            path = Path.ChangeExtension(path, null) + ProjectStore.Extension;
        Project.AssignCodes();
        ProjectStore.Save(Project.ToFile(Path.GetDirectoryName(path)!), path);
        FilePath = path;
        await Dialogs!.SavedAsync(path);
        Log($"Gespeichert: {path}");
    });

    // Im Browser kehrt die Dateiauswahl nach Abbrechen teils nie zurück: Befehl trotzdem freigeben
    [RelayCommand(AllowConcurrentExecutions = true)]
    Task ImportYaml() => Guard("YAML-Import", async () =>
    {
        var path = await Dialogs!.OpenAsync("tttool-YAML importieren", "tttool YAML", "*.yaml", "*.yml");
        if (path != null)
            LoadYaml(path);
    });

    [RelayCommand]
    Task ExportYaml() => Guard("YAML-Export", async () =>
    {
        if (!EnsureProgram()) return;
        var name = FilePath != null ? ProjectStore.BaseName(FilePath) : "projekt";
        var path = await Dialogs!.SaveAsync("tttool-YAML exportieren", name + ".yaml", "tttool YAML", ".yaml");
        if (path == null) return;
        WriteYaml(path);
        await Dialogs!.SavedAsync(path, TttoolYaml.CodesPathFor(path));
    });

    void WriteYaml(string path)
    {
        var file = Project.ToFile(BaseDir);
        TttoolYaml.Export(file, path);
        CopyRecordingsForTttool(file, Path.GetDirectoryName(path)!);
        Project.Replay = file.Replay;
        Project.Stop = file.Stop;
        Log($"YAML geschrieben: {path} (+ {Path.GetFileName(TttoolYaml.CodesPathFor(path))})");
    }

    [RelayCommand]
    Task BuildGme() => Guard("GME-Erstellung", async () =>
    {
        var gme = await CreateGme();
        if (gme == null) return;
        if (!IsDesktop)
            await Dialogs!.SavedAsync(gme);
        Notice($"GME erstellt: {(IsDesktop ? gme : Path.GetFileName(gme))} – auf den Stift kopieren und Produkt-Code {Project.ProductId} antippen.");
    });

    [RelayCommand]
    Task SaveToPen() => Guard("Auf den Stift speichern", async () =>
    {
        if (!EnsureProgram()) return;
        var name = (FilePath != null ? ProjectStore.BaseName(FilePath) : "projekt") + ".gme";
        string? built = null;
        var target = await Dialogs!.SaveToPenAsync(name, async () => built = await CreateGme());
        // Im Browser liegt die Datei nur im Speicher-Dateisystem und wird nicht mehr gebraucht
        if (!IsDesktop && built != null)
            File.Delete(built);
        if (target != null)
            Notice($"Auf den Stift gespeichert: {target}. Stift abmelden („Auswerfen“), dann Produkt-Code {Project.ProductId} antippen.");
    });

    /// <summary>
    /// Baut die GME-Datei und liefert ihren Pfad (null bei Fehler). Auf dem Desktop wird das Projekt
    /// gespeichert und die GME daneben erzeugt, im Browser im Arbeitsverzeichnis.
    /// </summary>
    async Task<string?> CreateGme()
    {
        if (!EnsureProgram()) return null;
        if (!IsDesktop)
        {
            var path = Path.Combine(BaseDir, (FilePath != null ? ProjectStore.BaseName(FilePath) : "projekt") + ".gme");
            return await BuildGmeInternal(Project.ToFile(BaseDir), BaseDir, path) ? path : null;
        }
        if (FilePath == null)
        {
            Notice("Bitte das Projekt zuerst speichern – die GME-Datei wird neben der Projektdatei erzeugt.");
            await SaveProjectAs();
            if (FilePath == null) return null;
        }
        else
        {
            await SaveProject();
        }

        var dir = Path.GetDirectoryName(FilePath)!;
        var gme = Path.Combine(dir, ProjectStore.BaseName(FilePath) + ".gme");
        bool ok;
        if (Project.UseTttool)
        {
            var yaml = Path.ChangeExtension(gme, ".yaml");
            WriteYaml(yaml);
            ok = await RunTttoolAssemble(yaml);
        }
        else
        {
            ok = await BuildGmeInternal(Project.ToFile(dir), dir, gme);
        }
        return ok ? gme : null;
    }

    void LogFromAnyThread(string message) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Log(message));

    async Task<bool> RunTttoolAssemble(string yaml)
    {
        Log($"Starte: {Project.TttoolPath} assemble {Path.GetFileName(yaml)}");
        int exit = await TttoolRunner.RunAsync(Project.TttoolPath, Path.GetDirectoryName(yaml)!, LogFromAnyThread,
            "assemble", Path.GetFileName(yaml));
        if (exit != 0)
            Notice($"tttool wurde mit Code {exit} beendet.");
        return exit == 0;
    }

    /// <summary>Baut die GME-Datei mit dem eingebauten Builder (Sprachausgaben werden zwischengespeichert).</summary>
    async Task<bool> BuildGmeInternal(ProjectFile file, string baseDir, string gmePath)
    {
        Log($"Baue {Path.GetFileName(gmePath)} …");
        var tts = new TextToSpeech(Path.Combine(baseDir, "tts-cache"), LogFromAnyThread, Notice);
        await tts.PrepareAsync(file.Speak.Where(s => string.IsNullOrEmpty(s.Recording)).Select(s => (Voices.SpeechKey(file, s), s.Text)));
        var bytes = await Task.Run(() => GmeBuilder.Build(file, baseDir, tts.Speak, warn: m => LogFromAnyThread("Warnung: " + m)));
        await File.WriteAllBytesAsync(gmePath, bytes);
        Log($"{bytes.Length / 1024.0:0.#} KB, {file.Scripts.Count} Skripte.");
        return true;
    }

    // ---------- Druck ----------

    List<string> Validate()
    {
        var problems = new List<string>();
        foreach (var page in Project.Pages)
        {
            var areas = page.Areas.ToList();
            foreach (var a in areas.Where(a => a.Oid == null))
                problems.Add($"{page.Name}: Fläche „{a.Caption}“ hat keinen gültigen Code.");
            var outlines = areas.Select(a => a.Outline()).ToList();
            for (int i = 0; i < areas.Count; i++)
                for (int j = i + 1; j < areas.Count; j++)
                    if (AreaGeometry.Overlaps(outlines[i], outlines[j]))
                        problems.Add($"{page.Name}: „{areas[i].Caption}“ und „{areas[j].Caption}“ überlappen sich.");
        }
        return problems;
    }

    // Im Browser kehrt die Dateiauswahl nach Abbrechen teils nie zurück: Befehl trotzdem freigeben
    [RelayCommand(AllowConcurrentExecutions = true)]
    Task ImportSvg() => Guard("SVG-Import", async () =>
    {
        if (Project.SelectedPage is not { } page) return;
        var path = await Dialogs!.OpenAsync("SVG importieren", "SVG-Grafik", "*.svg");
        if (path == null) return;
        var result = SvgImporter.Import(await File.ReadAllTextAsync(path), Path.GetDirectoryName(path), BaseDir);
        // Eine leere Seite übernimmt die Größe der Zeichnung
        bool resize = page.Elements.Count == 0 && page.Areas.Count == 0
            && result.WidthMm is double w && w > 0 && result.HeightMm is double h && h > 0;
        Project.History.Change(() =>
        {
            if (resize)
            {
                page.WidthMm = Math.Round(result.WidthMm!.Value, 2);
                page.HeightMm = Math.Round(result.HeightMm!.Value, 2);
            }
            foreach (var entry in result.Elements)
            {
                var element = ElementViewModel.FromEntry(Project, entry, null);
                page.Elements.Add(element);
                if (element.IsText)
                    FitText(element);
            }
            foreach (var entry in result.Areas)
            {
                if (entry.Target == AreaTarget.Script && entry.Script.Length == 0)
                    entry.Script = Project.SelectedScript?.Name ?? Project.Scripts.FirstOrDefault()?.Name ?? "";
                page.Areas.Add(AreaViewModel.FromEntry(Project, entry));
            }
        });
        if (result.Elements.Count > 0)
            AreasOnly = false;
        Log($"SVG importiert: {result.Elements.Count} Bilder, Texte und Farbflächen, {result.Areas.Count} OID-Flächen"
            + (resize ? $", Seitengröße {page.WidthMm} × {page.HeightMm} mm übernommen." : "."));
        foreach (var warning in result.Warnings.Distinct())
            Notice("Hinweis: " + warning);
    });

    /// <summary>
    /// Passt den Rahmen eines importierten Texts an die gemessene Größe an; die Grundlinie am Ankerpunkt
    /// (links, Mitte bzw. rechts je nach Ausrichtung) bleibt dabei stehen.
    /// </summary>
    static void FitText(ElementViewModel e)
    {
        const double scale = 10; // Pixel pro mm zum Messen
        var old = e.ToEntry(null);
        double f = e.Align switch { TextAlign.Center => 0.5, TextAlign.Right => 1, _ => 0 };
        double sizeMm = e.FontSize * 25.4 / 72;
        var anchor = ElementViewModel.ToPage(old, new Avalonia.Point(f * old.Width, SvgImporter.AscentFactor * sizeMm));
        try
        {
            e.Width = 10000;
            var text = e.CreateText(scale);
            double width = Math.Ceiling(text.Width / scale * 10) / 10 + 0.5, height = text.Height / scale, baseline = text.Baseline / scale;
            // Vom Anker zur Mitte des neuen Rahmens, gedreht wie das Element
            double vx = width / 2 - f * width, vy = height / 2 - baseline;
            double r = e.Rotation * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
            e.Width = Math.Round(width, 2);
            e.Height = Math.Round(height, 2);
            e.X = Math.Round(anchor.X + vx * cos - vy * sin - width / 2, 2);
            e.Y = Math.Round(anchor.Y + vx * sin + vy * cos - height / 2, 2);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or ArgumentException)
        {
            // Ohne Schriftsystem bleibt die Schätzung des Imports
            e.Width = old.Width;
        }
    }

    [RelayCommand]
    Task ExportSvg() => Guard("SVG-Export", async () =>
    {
        if (Project.SelectedPage is not { } page) return;
        var name = (FilePath != null ? ProjectStore.BaseName(FilePath) + "-" : "") + page.Name;
        name = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = await Dialogs!.SaveAsync("Seite als SVG exportieren", name + ".svg", "SVG-Grafik", ".svg");
        if (path == null) return;
        var warnings = new List<string>();
        await File.WriteAllTextAsync(path, SvgExporter.Export(page, warnings));
        await Dialogs.SavedAsync(path);
        Log($"SVG geschrieben: {path} (OID-Flächen auf einer ausgeblendeten Ebene).");
        foreach (var warning in warnings)
            Notice("Hinweis: " + warning);
    });

    [RelayCommand]
    Task ExportPdf() => Guard("PDF-Export", async () =>
    {
        if (!EnsureProgram()) return;
        Project.AssignCodes();
        var name = FilePath != null ? ProjectStore.BaseName(FilePath) : "seiten";
        var path = await Dialogs!.SaveAsync("Seiten als PDF exportieren", name + ".pdf", "PDF", ".pdf");
        if (path == null) return;

        PdfWriter.Write(path, CreatePageSpecs(), Project.Dpi, Project.PixelSize);
        await Dialogs.SavedAsync(path);
        Log($"PDF geschrieben: {path}. Mit {Project.Dpi} dpi und ohne Skalierung („Tatsächliche Größe“) drucken.");
    });

    [RelayCommand]
    Task PrintPages() => Guard("Drucken", async () =>
    {
        if (!EnsureProgram()) return;
        Project.AssignCodes();
        var name = FilePath != null ? ProjectStore.BaseName(FilePath) : "seiten";
        var dir = Path.Combine(Path.GetTempPath(), "tupfer-druck");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");

        PdfWriter.Write(path, CreatePageSpecs(), Project.Dpi, Project.PixelSize);
        var hint = await Dialogs!.PrintAsync(path);
        Notice($"{hint} Ohne Skalierung drucken („Tatsächliche Größe“ / 100 %), sonst erkennt der Stift die Codes nicht.");
    });

    /// <summary>PDF-Seiten mit OID-Mustern (und je nach Einstellung Bildern und Texten); meldet Probleme als Warnung.</summary>
    List<PdfPageSpec> CreatePageSpecs()
    {
        foreach (var p in Validate())
            Notice("Warnung: " + p);

        var specs = new List<PdfPageSpec>();
        foreach (var page in Project.Pages)
        {
            var spec = new PdfPageSpec
            {
                WidthMm = page.WidthMm,
                HeightMm = page.HeightMm,
                Background = PrintElements ? ImageConverter.RenderPage(page) : null,
            };
            foreach (var a in page.Areas.Where(a => a.Oid != null && a.Width > 0 && a.Height > 0))
                spec.Areas.Add(new OidArea(a.X, a.Y, a.Width, a.Height, a.Oid!.Value, Outline: a.IsRectangle ? null : a.Outline()));
            specs.Add(spec);
        }
        return specs;
    }

    [RelayCommand]
    Task ExportOidTable() => Guard("OID-Tabelle", async () =>
    {
        if (!EnsureProgram()) return;
        Project.AssignCodes();
        var name = FilePath != null ? ProjectStore.BaseName(FilePath) : "projekt";
        var path = await Dialogs!.SaveAsync("OID-Tabelle exportieren", name + "-oids.pdf", "PDF", ".pdf");
        if (path == null) return;

        var entries = new List<(string, int)> { ("Produkt", (int)Project.ProductId) };
        if (Project.Replay is { } r) entries.Add(("Wiederholen", r));
        if (Project.Stop is { } s) entries.Add(("Stopp", s));
        entries.AddRange(Project.Scripts.Where(x => x.Code.HasValue && !x.Helper).Select(x => (x.Name, x.Code!.Value)));

        PdfWriter.WriteOidTable(path, Project.Comment, entries, Project.Dpi, Project.PixelSize);
        await Dialogs.SavedAsync(path);
        Log($"OID-Tabelle geschrieben: {path}");
    });

    [RelayCommand]
    Task ExportPrinterTest() => Guard("Drucker-Testseite", async () =>
    {
        var path = await Dialogs!.SaveAsync("Drucker-Testseite speichern", PrinterTest.BaseName + ".pdf", "PDF", ".pdf");
        if (path == null) return;
        var yaml = PrinterTest.Write(path);
        Log($"Testseite geschrieben: {path}");
        var gme = Path.ChangeExtension(yaml, ".gme");
        bool ok = Project.UseTttool && IsDesktop
            ? await RunTttoolAssemble(yaml)
            : await BuildGmeInternal(PrinterTest.CreateProject(), Path.GetDirectoryName(yaml)!, gme);
        await Dialogs.SavedAsync(ok ? [path, gme] : [path]);
        if (ok)
            Notice($"GME erstellt: {gme} – auf den Stift kopieren, Seite drucken und die Varianten durchtippen.");
    });

    [RelayCommand]
    Task ExportAreaPng() => Guard("PNG-Export", async () =>
    {
        var area = Project.SelectedPage?.SelectedArea;
        if (area?.Oid is not { } oid)
        {
            Notice("Bitte zuerst eine Fläche mit gültigem Code auswählen.");
            return;
        }
        var path = await Dialogs!.SaveAsync("OID als PNG exportieren", $"oid-{oid}.png", "PNG", ".png");
        if (path == null) return;
        var outline = area.IsRectangle ? null : area.Outline().Select(p => (p.X - area.X, p.Y - area.Y)).ToList();
        PngWriter.WriteOidPng(path, oid, area.Width, area.Height, Project.Dpi, Project.PixelSize, outline);
        await Dialogs.SavedAsync(path);
        Log($"PNG geschrieben: {path} ({area.Width:0.#} × {area.Height:0.#} mm bei {Project.Dpi} dpi)");
    });

    // ---------- Seiten ----------

    [RelayCommand]
    void AddPage()
    {
        var page = new PageViewModel(Project) { Name = $"Seite {Project.Pages.Count + 1}" };
        Project.Pages.Add(page);
        Project.SelectedPage = page;
    }

    [RelayCommand]
    void RemovePage()
    {
        if (Project.SelectedPage is not { } page) return;
        int idx = Project.Pages.IndexOf(page);
        Project.Pages.Remove(page);
        Project.SelectedPage = Project.Pages.Count > 0 ? Project.Pages[Math.Min(idx, Project.Pages.Count - 1)] : null;
    }

    [RelayCommand] void Undo() => Project.History.Undo();
    [RelayCommand] void Redo() => Project.History.Redo();

    // Im Browser kehrt die Dateiauswahl nach Abbrechen teils nie zurück: Befehl trotzdem freigeben
    [RelayCommand(AllowConcurrentExecutions = true)]
    Task InsertImage() => Guard("Bild einfügen", async () =>
    {
        if (Project.SelectedPage is not { } page) return;
        var path = await Dialogs!.OpenAsync("Bild einfügen", "Bilder", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp");
        if (path == null) return;
        if (page.AddImage(path) != null)
            AreasOnly = false;
    });

    [RelayCommand]
    void InsertText()
    {
        if (Project.SelectedPage is not { } page) return;
        page.AddText();
        AreasOnly = false;
    }

    [RelayCommand]
    void InsertShape()
    {
        if (Project.SelectedPage is not { } page) return;
        page.AddShape();
        AreasOnly = false;
    }

    [RelayCommand]
    void DeleteElement()
    {
        if (Project.SelectedPage is { SelectedElement: { } element } page)
        {
            page.Elements.Remove(element);
            page.SelectedElement = null;
        }
    }

    [RelayCommand]
    void DuplicateElement()
    {
        if (Project.SelectedPage is not { SelectedElement: { } element } page) return;
        var copy = ElementViewModel.FromEntry(Project, element.ToEntry(null), null);
        copy.X += 5;
        copy.Y += 5;
        page.Elements.Add(copy);
        page.SelectedElement = copy;
    }

    [RelayCommand] void BringToFront() => MoveElement(ElementOrder.Front);
    [RelayCommand] void BringForward() => MoveElement(ElementOrder.Forward);
    [RelayCommand] void SendBackward() => MoveElement(ElementOrder.Backward);
    [RelayCommand] void SendToBack() => MoveElement(ElementOrder.Back);

    void MoveElement(ElementOrder order)
    {
        if (Project.SelectedPage is { SelectedElement: { } element } page)
            page.MoveElement(element, order);
    }

    /// <summary>Wählt ein Bild oder einen Text aus der Ebenen-Liste (auch wenn es verdeckt ist).</summary>
    [RelayCommand]
    void SelectElement(ElementViewModel element)
    {
        if (Project.SelectedPage is not { } page) return;
        page.SelectedElement = element;
        AreasOnly = false;
    }

    /// <summary>Passt das Element ungedreht und mit gleichem Seitenverhältnis in die Seite ein.</summary>
    [RelayCommand]
    void FitElementToPage()
    {
        if (Project.SelectedPage is not { SelectedElement: { } e } page || e.Width <= 0 || e.Height <= 0) return;
        double f = Math.Min(page.WidthMm / e.Width, page.HeightMm / e.Height);
        Project.History.Change(() =>
        {
            e.Rotation = 0;
            e.Width = Math.Round(e.Width * f, 1);
            e.Height = Math.Round(e.Height * f, 1);
            e.X = Math.Round((page.WidthMm - e.Width) / 2, 1);
            e.Y = Math.Round((page.HeightMm - e.Height) / 2, 1);
        });
    }

    /// <summary>Löscht alle gewählten Flächen, Bilder und Texte.</summary>
    [RelayCommand]
    void DeleteSelection()
    {
        if (Project.SelectedPage is { } page)
            Project.History.Change(page.DeleteSelection);
    }

    /// <summary>Wählt alle Flächen und – außer im Modus „Nur OID-Flächen“ – alle Bilder und Texte.</summary>
    [RelayCommand]
    void SelectAll() => Project.SelectedPage?.SelectAll(!AreasOnly);

    /// <summary>Gewählte Objekte, die sich bearbeiten lassen (im Modus „Nur OID-Flächen“ nur Flächen).</summary>
    List<object> ArrangeItems() =>
        Project.SelectedPage?.Selection.Where(i => !AreasOnly || i is AreaViewModel).ToList() ?? [];

    /// <summary>Richtet die gewählten Objekte aneinander aus, ein einzelnes an der Seite.</summary>
    [RelayCommand]
    void Align(string mode)
    {
        if (Project.SelectedPage is { } page && Enum.TryParse<AlignMode>(mode, out var m))
            Project.History.Change(() => page.Align(ArrangeItems(), m));
    }

    /// <summary>Verteilt die gewählten Objekte mit gleichen Abständen („Horizontal“ oder „Vertical“).</summary>
    [RelayCommand]
    void Distribute(string direction)
    {
        if (Project.SelectedPage is { } page)
            Project.History.Change(() => page.Distribute(ArrangeItems(), direction == "Horizontal"));
    }

    [RelayCommand]
    void DeleteArea()
    {
        if (Project.SelectedPage is { SelectedArea: { } area } page)
        {
            page.Areas.Remove(area);
            page.SelectedArea = null;
        }
    }

    [RelayCommand]
    void DuplicateArea()
    {
        if (Project.SelectedPage is not { SelectedArea: { } area } page) return;
        var copy = AreaViewModel.FromEntry(Project, area.ToEntry());
        copy.X += 5;
        copy.Y += 5;
        page.Areas.Add(copy);
        page.SelectedArea = copy;
    }

    /// <summary>Legt für die gewählte Fläche ein neues Skript an und öffnet es.</summary>
    [RelayCommand]
    void CreateScriptForArea()
    {
        if (Project.SelectedPage?.SelectedArea is not { } area) return;
        var baseName = string.IsNullOrWhiteSpace(area.Label) ? "feld" : new string(area.Label.Where(char.IsLetterOrDigit).ToArray());
        var name = Project.Scripts.Any(s => s.Name == baseName) || baseName.Length == 0 ? Project.UniqueScriptName(baseName.Length == 0 ? "feld" : baseName) : baseName;
        var script = Project.AddScript(name);
        Project.AssignCodes();
        area.Target = AreaTarget.Script;
        area.Script = name;
        Project.SelectedScript = script;
        SelectedTab = 1;
    }

    [RelayCommand]
    void EditAreaScript()
    {
        if (Project.SelectedPage?.SelectedArea is not { IsScript: true } area) return;
        var script = Project.Scripts.FirstOrDefault(s => s.Name == area.Script);
        if (script == null) return;
        Project.SelectedScript = script;
        SelectedTab = 1;
    }

    [RelayCommand] void ZoomIn() => Zoom *= 1.25;
    [RelayCommand] void ZoomOut() => Zoom /= 1.25;

    // ---------- Skripte & Texte ----------

    [RelayCommand]
    void AddScript()
    {
        var script = Project.AddScript(Project.UniqueScriptName("skript"));
        Project.AssignCodes();
        Project.SelectedScript = script;
    }

    [RelayCommand]
    void RemoveScript()
    {
        if (Project.SelectedScript is { Generated: true })
        {
            Notice("Dieses Skript stammt aus dem C#-Programm – bitte dort ändern.");
            return;
        }
        if (Project.SelectedScript is { } s)
            Project.RemoveScript(s);
        Project.SelectedScript = Project.Scripts.FirstOrDefault();
    }

    [RelayCommand]
    void AddSpeak()
    {
        var speak = new SpeakViewModel { Name = $"text{Project.Speak.Count + 1}" };
        Project.Speak.Add(speak);
        Project.SelectedSpeak = speak;
    }

    /// <summary>
    /// tttool kennt keine Aufnahmen zu Texten, sondern sucht Audiodateien im Medienverzeichnis:
    /// Aufnahmen dorthin kopieren (z. B. für „GME mit externem tttool bauen“).
    /// </summary>
    void CopyRecordingsForTttool(ProjectFile file, string yamlDir)
    {
        if (!IsDesktop)
            return;
        var pattern = string.IsNullOrWhiteSpace(file.MediaPath) ? "%s" : file.MediaPath;
        foreach (var s in file.Speak.Where(s => !string.IsNullOrEmpty(s.Recording)))
        {
            var source = Path.GetFullPath(s.Recording!, BaseDir);
            var target = Path.GetFullPath(pattern.Replace("%s", s.Name) + Path.GetExtension(source), yamlDir);
            if (!File.Exists(source) || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
        }
    }

    // ---------- Anhören und Aufnehmen ----------

    /// <summary>Spielt die Aufnahme bzw. die Sprachausgabe eines Textes ab.</summary>
    // Auch während ein anderer Text noch geladen wird; die neue Wiedergabe beendet die alte
    [RelayCommand(AllowConcurrentExecutions = true)]
    Task PlaySpeak(SpeakViewModel speak) => Guard("Wiedergabe", async () =>
    {
        byte[] data;
        string extension;
        if (speak.HasRecording)
        {
            data = await File.ReadAllBytesAsync(speak.Recording!);
            extension = Path.GetExtension(speak.Recording!);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(speak.Text))
            {
                Notice($"„{speak.Name}“ hat noch keinen Text.");
                return;
            }
            var key = Voices.SpeechKey(Project.Language, Project.Voice, speak.Voice);
            var tts = new TextToSpeech(TtsCacheDir, LogFromAnyThread, Notice);
            await tts.PrepareAsync([(key, speak.Text)]);
            data = await Task.Run(() => tts.Speak(key, speak.Text));
            extension = Audio.ExtensionOf(data);
        }
        // Nicht auf das Ende warten (der Knopf bliebe so lange gesperrt), nur sofortige Fehler melden
        var playing = Audio.Device.PlayAsync(data, extension);
        if (playing.IsCompleted)
            await playing;
    });

    SpeakViewModel? _recordingSpeak;

    /// <summary>Startet eine Aufnahme für den Text bzw. beendet die laufende und speichert sie.</summary>
    [RelayCommand]
    Task RecordSpeak(SpeakViewModel speak) => Guard("Aufnahme", async () =>
    {
        if (_recordingSpeak is { } current)
        {
            _recordingSpeak = null;
            current.IsRecording = false;
            var wav = await Audio.Device.StopRecordingAsync();
            var path = RecordingPath(current, ".wav");
            await File.WriteAllBytesAsync(path, wav);
            current.Recording = path;
            Log($"Aufnahme für „{current.Name}“ gespeichert ({Audio.WavSeconds(wav):0.0} s): {path}");
            return;
        }
        if (!await EnsureProjectDir())
            return;
        await Audio.Device.StartRecordingAsync();
        _recordingSpeak = speak;
        speak.IsRecording = true;
        Log($"Aufnahme für „{speak.Name}“ läuft – zum Beenden noch einmal auf den Stopp-Knopf klicken.");
    });

    /// <summary>Übernimmt eine vorhandene Audiodatei als Aufnahme des Textes.</summary>
    // Im Browser kehrt die Dateiauswahl nach Abbrechen teils nie zurück: Befehl trotzdem freigeben
    [RelayCommand(AllowConcurrentExecutions = true)]
    Task ImportRecording(SpeakViewModel speak) => Guard("Aufnahme laden", async () =>
    {
        if (!await EnsureProjectDir())
            return;
        var source = await Dialogs!.OpenAsync("Audiodatei wählen", "Audio", GmeBuilder.MediaExtensions.Select(e => "*" + e).ToArray());
        if (source == null)
            return;
        var path = RecordingPath(speak, Path.GetExtension(source).ToLowerInvariant());
        if (!string.Equals(Path.GetFullPath(source), path, StringComparison.OrdinalIgnoreCase))
        {
            // Im Browser liegt die gewählte Datei schon als Kopie im Projektverzeichnis
            if (IsDesktop)
                File.Copy(source, path, true);
            else
                File.Move(source, path, true);
        }
        speak.Recording = path;
        Log($"Aufnahme für „{speak.Name}“: {path}");
    });

    [RelayCommand]
    void RemoveRecording(SpeakViewModel speak)
    {
        if (!speak.HasRecording)
            return;
        speak.Recording = null;
        Log($"„{speak.Name}“ verwendet wieder die Sprachausgabe.");
    }

    /// <summary>Aufnahmen liegen im Projektverzeichnis unter aufnahmen/, benannt nach dem Text.</summary>
    string RecordingPath(SpeakViewModel speak, string extension)
    {
        var dir = Path.Combine(BaseDir, "aufnahmen");
        Directory.CreateDirectory(dir);
        var name = string.Concat(speak.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(dir, (name.Length > 0 ? name : "aufnahme") + extension);
    }

    /// <summary>Auf dem Desktop brauchen Aufnahmen ein gespeichertes Projekt (sie liegen daneben).</summary>
    async Task<bool> EnsureProjectDir()
    {
        if (FilePath != null || !IsDesktop)
            return true;
        Notice("Bitte das Projekt zuerst speichern – Aufnahmen werden neben der Projektdatei abgelegt.");
        await SaveProjectAs();
        return FilePath != null;
    }

    [RelayCommand]
    void RemoveSpeak()
    {
        if (Project.SelectedSpeak is { } s)
            Project.Speak.Remove(s);
    }
}
