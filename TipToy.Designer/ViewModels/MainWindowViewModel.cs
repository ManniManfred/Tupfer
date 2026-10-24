using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TipToy.Designer.Compiler;
using TipToy.Designer.Gme;
using TipToy.Designer.Model;
using TipToy.Designer.Oid;
using TipToy.Designer.Services;

namespace TipToy.Designer.ViewModels;

/// <summary>Dateidialoge, werden vom Fenster bereitgestellt.</summary>
public interface IFileDialogs
{
    Task<string?> OpenAsync(string title, string filterName, params string[] patterns);
    Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension);

    /// <summary>Die Dateien wurden geschrieben (im Browser werden sie jetzt heruntergeladen).</summary>
    Task SavedAsync(params string[] paths) => Task.CompletedTask;

    /// <summary>Ein neues, leeres Projekt wurde angelegt.</summary>
    void ProjectCreated() { }
}

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
        _project = CreateDefaultProject();
    }

    public IFileDialogs? Dialogs { get; set; }

    /// <summary>false im Browser: dort gibt es keine externen Programme wie tttool.</summary>
    public static bool IsDesktop => !OperatingSystem.IsBrowser();

    [ObservableProperty] ProjectViewModel _project;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    string? _filePath;

    [ObservableProperty] string _logText = "";
    [ObservableProperty] double _zoom = 1.0;
    [ObservableProperty] bool _printElements = true;

    /// <summary>Auf der Seite nur OID-Flächen bearbeiten, Bilder und Texte sind gesperrt.</summary>
    [ObservableProperty] bool _areasOnly;

    /// <summary>Die Anfasser eines Bilds schneiden zu statt zu skalieren.</summary>
    [ObservableProperty] bool _cropMode;
    [ObservableProperty] int _selectedTab;
    [ObservableProperty] string _programErrors = "";
    [ObservableProperty] string _programOutput = "";

    const int ProgramTab = 2;

    public string Title => $"TipToy Designer – {(FilePath == null ? "Neues Projekt" : Path.GetFileName(FilePath))}";

    string BaseDir => FilePath != null ? Path.GetDirectoryName(FilePath)! : Environment.CurrentDirectory;

    public void Log(string message) => LogText += message + Environment.NewLine;

    ProjectViewModel CreateDefaultProject()
    {
        var p = new ProjectViewModel(Log) { Comment = "Mein tiptoi-Buch", Welcome = "willkommen" };
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
            Log($"Fehler bei {action}: {ex.Message}");
        }
    }

    // ---------- Datei ----------

    [RelayCommand]
    void NewProject()
    {
        Project = CreateDefaultProject();
        FilePath = null;
        Dialogs?.ProjectCreated();
        ProgramErrors = ProgramOutput = "";
        Log("Neues Projekt angelegt.");
    }

    // ---------- C#-Programm ----------

    /// <summary>Übersetzt das C#-Programm, falls vorhanden. false bei Fehlern.</summary>
    bool EnsureProgram() => string.IsNullOrWhiteSpace(Project.Program) || TryCompileProgram(quiet: true);

    [RelayCommand]
    void CompileProgram()
    {
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
            Log($"C#-Programm: {result.Errors.Count} Fehler – siehe Tab „Programm (C#)“.");
            SelectedTab = ProgramTab;
            return false;
        }
        if (Project.ApplyProgram(result) is { } error)
        {
            ProgramErrors = error;
            Log(error);
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
            Log("Das Programm ist nicht leer – das Beispiel wird nur in ein leeres Programm eingefügt.");
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
        if (!string.IsNullOrWhiteSpace(Project.Program))
            TryCompileProgram(quiet: true);
    }

    [RelayCommand]
    Task OpenProject() => Guard("Öffnen", async () =>
    {
        var path = await Dialogs!.OpenAsync("Projekt öffnen", "TipToy-Projekt", "*" + ProjectStore.Extension);
        if (path != null)
            LoadProject(path);
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
            Log($"Fehler beim Öffnen von {path}: {ex.Message}");
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
        var path = await Dialogs!.SaveAsync("Projekt speichern", "projekt" + ProjectStore.Extension, "TipToy-Projekt", ProjectStore.Extension);
        if (path == null) return;
        if (!path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
            path = Path.ChangeExtension(path, null) + ProjectStore.Extension;
        Project.AssignCodes();
        ProjectStore.Save(Project.ToFile(Path.GetDirectoryName(path)!), path);
        FilePath = path;
        await Dialogs!.SavedAsync(path);
        Log($"Gespeichert: {path}");
    });

    [RelayCommand]
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
        Project.Replay = file.Replay;
        Project.Stop = file.Stop;
        Log($"YAML geschrieben: {path} (+ {Path.GetFileName(TttoolYaml.CodesPathFor(path))})");
    }

    [RelayCommand]
    Task BuildGme() => Guard("GME-Erstellung", async () =>
    {
        if (!EnsureProgram()) return;
        if (!IsDesktop)
        {
            // Im Browser wird die GME-Datei direkt heruntergeladen, ohne das Projekt zu speichern
            var name = FilePath != null ? ProjectStore.BaseName(FilePath) : "projekt";
            var path = Path.Combine(BaseDir, name + ".gme");
            if (await BuildGmeInternal(Project.ToFile(BaseDir), BaseDir, path))
            {
                await Dialogs!.SavedAsync(path);
                Log($"GME erstellt: {name}.gme – auf den Stift kopieren und Produkt-Code {Project.ProductId} antippen.");
            }
            return;
        }
        if (FilePath == null)
        {
            Log("Bitte das Projekt zuerst speichern – die GME-Datei wird neben der Projektdatei erzeugt.");
            await SaveProjectAs();
            if (FilePath == null) return;
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
        if (ok)
            Log($"GME erstellt: {gme} – auf den Stift kopieren und Produkt-Code {Project.ProductId} antippen.");
    });

    void LogFromAnyThread(string message) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Log(message));

    async Task<bool> RunTttoolAssemble(string yaml)
    {
        Log($"Starte: {Project.TttoolPath} assemble {Path.GetFileName(yaml)}");
        int exit = await TttoolRunner.RunAsync(Project.TttoolPath, Path.GetDirectoryName(yaml)!, LogFromAnyThread,
            "assemble", Path.GetFileName(yaml));
        if (exit != 0)
            Log($"tttool wurde mit Code {exit} beendet.");
        return exit == 0;
    }

    /// <summary>Baut die GME-Datei mit dem eingebauten Builder (Sprachausgaben werden zwischengespeichert).</summary>
    async Task<bool> BuildGmeInternal(ProjectFile file, string baseDir, string gmePath)
    {
        Log($"Baue {Path.GetFileName(gmePath)} …");
        if (file.Speak.Count > 0 && TextToSpeech.PrepareAsync is { } prepare)
            await prepare(string.IsNullOrWhiteSpace(file.Language) ? "en" : file.Language);
        var tts = new TextToSpeech(Path.Combine(baseDir, "tts-cache"), LogFromAnyThread);
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
            for (int i = 0; i < areas.Count; i++)
                for (int j = i + 1; j < areas.Count; j++)
                {
                    var (a, b) = (areas[i], areas[j]);
                    if (a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height)
                        problems.Add($"{page.Name}: „{a.Caption}“ und „{b.Caption}“ überlappen sich.");
                }
        }
        return problems;
    }

    [RelayCommand]
    Task ExportPdf() => Guard("PDF-Export", async () =>
    {
        if (!EnsureProgram()) return;
        Project.AssignCodes();
        var name = FilePath != null ? ProjectStore.BaseName(FilePath) : "seiten";
        var path = await Dialogs!.SaveAsync("Seiten als PDF exportieren", name + ".pdf", "PDF", ".pdf");
        if (path == null) return;

        foreach (var p in Validate())
            Log("Warnung: " + p);

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
                spec.Areas.Add(new OidArea(a.X, a.Y, a.Width, a.Height, a.Oid!.Value));
            specs.Add(spec);
        }

        PdfWriter.Write(path, specs, Project.Dpi, Project.PixelSize);
        await Dialogs.SavedAsync(path);
        Log($"PDF geschrieben: {path}. Mit {Project.Dpi} dpi und ohne Skalierung („Tatsächliche Größe“) drucken.");
    });

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
            Log($"GME erstellt: {gme} – auf den Stift kopieren, Seite drucken und die Varianten durchtippen.");
    });

    [RelayCommand]
    Task ExportAreaPng() => Guard("PNG-Export", async () =>
    {
        var area = Project.SelectedPage?.SelectedArea;
        if (area?.Oid is not { } oid)
        {
            Log("Bitte zuerst eine Fläche mit gültigem Code auswählen.");
            return;
        }
        var path = await Dialogs!.SaveAsync("OID als PNG exportieren", $"oid-{oid}.png", "PNG", ".png");
        if (path == null) return;
        PngWriter.WriteOidPng(path, oid, area.Width, area.Height, Project.Dpi, Project.PixelSize);
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

    [RelayCommand]
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

    [RelayCommand] void BringToFront() => MoveElement(toFront: true);
    [RelayCommand] void SendToBack() => MoveElement(toFront: false);

    void MoveElement(bool toFront)
    {
        if (Project.SelectedPage is not { SelectedElement: { } element } page) return;
        int from = page.Elements.IndexOf(element), to = toFront ? page.Elements.Count - 1 : 0;
        if (from != to)
            page.Elements.Move(from, to);
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

    [RelayCommand] void ZoomIn() => Zoom = Math.Min(4, Zoom * 1.25);
    [RelayCommand] void ZoomOut() => Zoom = Math.Max(0.2, Zoom / 1.25);

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
            Log("Dieses Skript stammt aus dem C#-Programm – bitte dort ändern.");
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

    [RelayCommand]
    void RemoveSpeak()
    {
        if (Project.SelectedSpeak is { } s)
            Project.Speak.Remove(s);
    }
}
