using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

/// <summary>Eine GME-Datei auf dem Stift in der Liste des Stift-Managers.</summary>
public partial class PenItem(PenProduct product) : ObservableObject
{
    public PenProduct Product { get; } = product;

    /// <summary>Zugehöriges Produkt aus dem Ravensburger-Katalog, wenn bekannt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    CatalogEntry? _entry;

    [ObservableProperty] bool _updateAvailable;

    /// <summary>Produkt-ID kommt in einer weiteren eigenen Datei vor.</summary>
    [ObservableProperty] bool _isDuplicate;

    public bool IsOfficial => Product.IsOfficial;

    public string Kind => Product.Error != null ? "Fehler" : IsOfficial ? "Ravensburger" : "Eigenes Projekt";

    public string Title => Entry?.Title
                           ?? (IsOfficial || Product.Comment.Length == 0 ? Path.GetFileNameWithoutExtension(Product.RelativePath) : Product.Comment);

    public string Details => string.Join(" · ", new[]
    {
        Product.RelativePath,
        Product.ProductId is { } id ? $"Produkt-ID {id}" : null,
        PenManager.FormatSize(Product.Size),
        Product.Date?.ToString("dd.MM.yyyy"),
        Product.Error,
    }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>Ein Produkt aus dem Ravensburger-Katalog mit seinem Stand auf dem Stift.</summary>
public partial class CatalogItem(CatalogEntry entry) : ObservableObject
{
    public CatalogEntry Entry { get; } = entry;

    [ObservableProperty] Bitmap? _thumbnail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(ActionText), nameof(CanInstall), nameof(IsInstalled), nameof(HasUpdate))]
    CatalogState _state;

    /// <summary>Datei auf dem Stift, die zu diesem Produkt gehört.</summary>
    public PenProduct? OnPen { get; set; }

    public string Title => Entry.Title;

    public string Details => string.Join(" · ", new[]
    {
        Entry.Category,
        Entry.ArticleNumbers.Length > 0 ? $"Art.-Nr. {Entry.ArticleNumbers}" : null,
        Entry.ProductId is { } id ? $"Produkt-ID {id}" : null,
        Entry.Size is { } size ? PenManager.FormatSize(size) : null,
        Entry.Version?.ToString("'Stand' dd.MM.yyyy"),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string StateText => State switch
    {
        CatalogState.Installed => "auf dem Stift",
        CatalogState.UpdateAvailable => "Update verfügbar",
        _ => "",
    };

    public bool IsInstalled => State == CatalogState.Installed;
    public bool HasUpdate => State == CatalogState.UpdateAvailable;
    public bool CanInstall => State != CatalogState.Installed;
    public string ActionText => State == CatalogState.UpdateAvailable ? "Aktualisieren" : "Installieren";
}

/// <summary>
/// Stift-Manager (als Ebene über der Ansicht): GME-Dateien auf dem Stift mit Produkt-ID, Größe und Datum,
/// eigene Projekte und Ravensburger-Produkte getrennt, freier Speicher, Löschen; dazu der Katalog der
/// offiziellen Ravensburger-Produkte zum Installieren und Aktualisieren (nur Desktop, siehe <see cref="RavensburgerCatalog"/>).
/// </summary>
public partial class PenManagerViewModel : ObservableObject
{
    readonly Action _close;
    readonly Action<string> _log;
    readonly uint? _projectProductId;

    /// <summary>Rückfragen und Ordnerdialog; im eigenen Fenster die Dialoge dieses Fensters.</summary>
    public IFileDialogs? Dialogs { get; set; }

    public PenManagerViewModel(IFileDialogs? dialogs, Action close, Action<string> log, uint? projectProductId = null)
    {
        Dialogs = dialogs;
        _close = close;
        _log = log;
        _projectProductId = projectProductId;
    }

    /// <summary>Gefälschter HTTP-Handler für Tests; null = echte Verbindung.</summary>
    public static HttpMessageHandler? Handler { get; set; }

    /// <summary>Höchstens so viele Produktbilder gleichzeitig laden.</summary>
    const int ParallelThumbnails = 4;

    /// <summary>Katalog veraltet nach so vielen Tagen und wird beim Öffnen neu geladen.</summary>
    const int CatalogMaxAgeDays = 7;

    /// <summary>Bereits geladene Produktbilder (nach Adresse), damit sie beim nächsten Öffnen nicht neu kommen.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Bitmap?> ThumbnailCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Katalog und Download von Ravensburger gehen nur am Desktop: Im Browser verhindert CORS den Zugriff
    /// auf die Ravensburger-Server (sie senden keine Access-Control-Allow-Origin-Kopfzeile).
    /// </summary>
    public static bool CatalogAvailable => MainWindowViewModel.IsDesktop;

    public static bool CatalogUnavailable => !CatalogAvailable;

    /// <summary>Am Desktop per Ordnerdialog, im Browser nur mit der File System Access API (Chrome, Edge).</summary>
    public static bool CanPickFolder => MainWindowViewModel.IsDesktop || PenDrives.PickFolder != null;

    public static bool CannotPickFolder => !CanPickFolder;

    /// <summary>Im Browser wird der Stift nicht von selbst gefunden, dort muss sein Ordner gewählt werden.</summary>
    public static bool ShowBrowserPenHint => !MainWindowViewModel.IsDesktop;

    public ObservableCollection<IPenDrive> Drives { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDrive))]
    IPenDrive? _selectedDrive;

    public bool HasDrive => SelectedDrive != null;

    /// <summary>Beim Suchen der Stifte nicht schon beim Umschalten einlesen, das folgt danach.</summary>
    bool _findingDrives;

    partial void OnSelectedDriveChanged(IPenDrive? value)
    {
        if (!_findingDrives)
            _ = RefreshPen();
    }

    public ObservableCollection<PenItem> PenItems { get; } = [];

    [ObservableProperty] string _spaceText = "";
    [ObservableProperty] double _usedFraction;
    [ObservableProperty] string _penSummary = "";
    [ObservableProperty] string _warnings = "";

    List<PenProduct> _products = [];

    // ---------- Katalog ----------

    List<CatalogEntry> _catalog = [];
    readonly List<CatalogItem> _allCatalogItems = [];

    public ObservableCollection<CatalogItem> CatalogItems { get; } = [];

    [ObservableProperty] string _catalogInfo = "";
    [ObservableProperty] string _searchText = "";

    /// <summary>Nur Produkte zeigen, die auf dem Stift ein Update brauchen.</summary>
    [ObservableProperty] bool _onlyUpdates;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnOnlyUpdatesChanged(bool value) => ApplyFilter();

    /// <summary>Ausgewählte Registerkarte: 0 = Stift, 1 = Katalog.</summary>
    [ObservableProperty] int _selectedTab;

    // ---------- Zustand ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    bool _isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty] string _status = "";

    /// <summary>Fortschritt des laufenden Downloads 0 … 1; null = keiner.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress), nameof(ProgressPercent))]
    double? _progress;

    public bool HasProgress => Progress != null;
    public double ProgressPercent => (Progress ?? 0) * 100;

    CancellationTokenSource? _cancel;

    /// <summary>Sucht die Stifte, liest den ersten und den gespeicherten Katalog (veraltet: neu laden).</summary>
    public async Task StartAsync()
    {
        await Refresh();
        if (CatalogAvailable)
        {
            var cache = RavensburgerCatalog.LoadCache();
            if (cache != null)
                SetCatalog(cache);
            if (cache == null || (DateTime.Now - cache.LoadedAt).TotalDays > CatalogMaxAgeDays)
                await LoadCatalog();
        }
        if (SelectedDrive == null && !Status.Contains("Ravensburger"))
            Status = ShowBrowserPenHint
                ? (CanPickFolder ? "Den Stift verbinden und mit „Ordner wählen …“ sein Laufwerk auswählen." : "")
                : "Kein Stift gefunden: Er muss angeschlossen sein und sich als Laufwerk „tiptoi“ melden. Oder „Ordner wählen …“.";
    }

    /// <summary>Sucht erneut nach Stiften und liest den gewählten neu ein.</summary>
    [RelayCommand]
    async Task Refresh()
    {
        var current = SelectedDrive;
        _findingDrives = true;
        try
        {
            Drives.Clear();
            foreach (var d in PenDrives.Find())
                Drives.Add(d);
            // Von Hand gewählte Ordner bleiben in der Liste
            if (current != null && !Drives.Any(d => d.Name == current.Name))
                Drives.Add(current);
            SelectedDrive = Drives.FirstOrDefault(d => d.Name == current?.Name) ?? Drives.FirstOrDefault();
        }
        finally
        {
            _findingDrives = false;
        }
        await RefreshPen();
    }

    /// <summary>Einen Ordner als Stift wählen (Stift ohne Bezeichnung „tiptoi“ oder Kopie auf der Festplatte).</summary>
    [RelayCommand]
    async Task PickFolder()
    {
        IPenDrive? drive = null;
        if (PenDrives.PickFolder != null)
            drive = await PenDrives.PickFolder();
        else if (Dialogs != null && await Dialogs.PickFolderAsync("Laufwerk bzw. Ordner des Stifts wählen") is { } path)
            drive = new FolderPenDrive(path);
        if (drive == null)
            return;
        var existing = Drives.FirstOrDefault(d => d.Name == drive.Name);
        if (existing == null)
            Drives.Add(drive);
        SelectedDrive = existing ?? drive;
    }

    /// <summary>Liest die GME-Dateien und den freien Speicher des gewählten Stifts.</summary>
    public async Task RefreshPen()
    {
        if (SelectedDrive is not { } drive)
        {
            _products = [];
            SpaceText = "";
            PenSummary = "";
            Rebuild();
            return;
        }
        try
        {
            _products = await drive.ReadProductsAsync();
            Status = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _products = [];
            Status = $"Der Stift „{drive.Name}“ lässt sich nicht lesen ({e.Message}). Ist er noch verbunden?";
        }
        var space = drive.GetSpace();
        SpaceText = space is { } s
            ? $"{PenManager.FormatSize(s.Free)} frei von {PenManager.FormatSize(s.Total)}"
            : "Freier Speicher unbekannt (im Browser nicht ermittelbar)";
        UsedFraction = space is { Total: > 0 } t ? 1 - (double)t.Free / t.Total : 0;
        int official = _products.Count(p => p.IsOfficial);
        PenSummary = $"{_products.Count} GME-Datei(en): {official} von Ravensburger, {_products.Count - official} eigene";
        Rebuild();
    }

    /// <summary>Gleicht Stift und Katalog ab und baut beide Listen neu auf.</summary>
    void Rebuild()
    {
        var duplicates = PenContents.Duplicates(_products);
        var duplicateIds = duplicates.Select(g => g.Key).ToHashSet();
        PenItems.Clear();
        foreach (var p in _products.OrderBy(p => p.IsOfficial).ThenBy(p => p.ProductId ?? uint.MaxValue))
        {
            var entry = PenManager.FindInCatalog(p, _products, _catalog);
            PenItems.Add(new PenItem(p)
            {
                Entry = entry,
                UpdateAvailable = entry != null && PenManager.NeedsUpdate(entry, p),
                IsDuplicate = p.ProductId is { } id && duplicateIds.Contains(id),
            });
        }

        var warnings = duplicates
            .Select(g => $"Doppelte Produkt-ID {g.Key}: {string.Join(", ", g.Select(p => p.RelativePath))} – der Stift nimmt dann womöglich die falsche Datei.")
            .ToList();
        if (_projectProductId is { } own && _products.Any(p => p.ProductId == own))
            warnings.Add($"Die Produkt-ID des geöffneten Projekts ({own}) ist auf dem Stift belegt.");
        int updates = PenItems.Count(i => i.UpdateAvailable);
        if (updates > 0)
            warnings.Add($"{updates} Ravensburger-Produkt(e) mit Update – siehe Katalog.");
        Warnings = string.Join(Environment.NewLine, warnings);

        foreach (var item in _allCatalogItems)
        {
            item.OnPen = PenManager.FindOnPen(item.Entry, _products, _catalog);
            item.State = item.OnPen == null ? CatalogState.Missing
                : PenManager.NeedsUpdate(item.Entry, item.OnPen) ? CatalogState.UpdateAvailable
                : CatalogState.Installed;
        }
        ApplyFilter();
    }

    void ApplyFilter()
    {
        var words = (SearchText ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        CatalogItems.Clear();
        foreach (var item in _allCatalogItems
                     .Where(i => !OnlyUpdates || i.HasUpdate)
                     .Where(i => words.All(w => i.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                                || i.Entry.ArticleNumbers.Contains(w, StringComparison.Ordinal)
                                                || i.Entry.Category.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                                || i.Entry.ProductId?.ToString() == w))
                     // Was auf dem Stift ist (Updates zuerst), dann der Rest
                     .OrderBy(i => i.State switch { CatalogState.UpdateAvailable => 0, CatalogState.Installed => 1, _ => 2 }))
            CatalogItems.Add(item);
    }

    void SetCatalog(CatalogSnapshot snapshot)
    {
        _catalog = snapshot.Entries;
        _allCatalogItems.Clear();
        _allCatalogItems.AddRange(_catalog.Select(e => new CatalogItem(e)));
        CatalogInfo = $"{_catalog.Count} Produkte, Stand {snapshot.LoadedAt:dd.MM.yyyy HH:mm}";
        Rebuild();
        _ = LoadThumbnailsAsync();
    }

    /// <summary>Lädt den Katalog neu von den Ravensburger-Service-Seiten.</summary>
    [RelayCommand]
    async Task LoadCatalog()
    {
        if (!CatalogAvailable)
            return;
        await Run(async cancel =>
        {
            using var http = RavensburgerCatalog.CreateHttpClient(Handler);
            var progress = new Progress<string>(s => Status = s);
            var entries = await new RavensburgerCatalog(http).LoadAsync(progress, cancel);
            var snapshot = new CatalogSnapshot { LoadedAt = DateTime.Now, Entries = entries };
            RavensburgerCatalog.SaveCache(snapshot);
            SetCatalog(snapshot);
            Status = $"Katalog geladen: {entries.Count} Produkte.";
            _log($"Ravensburger-Katalog geladen: {entries.Count} Produkte");
        });
    }

    async Task LoadThumbnailsAsync()
    {
        var queue = new Queue<CatalogItem>(_allCatalogItems.Where(i => i.Entry.ImageUrl != null));
        using var http = RavensburgerCatalog.CreateHttpClient(Handler);
        var catalog = new RavensburgerCatalog(http);

        async Task Worker()
        {
            while (queue.TryDequeue(out var item))
            {
                var url = item.Entry.ImageUrl!;
                if (!ThumbnailCache.TryGetValue(url, out var bitmap))
                {
                    var data = await catalog.GetImageAsync(url);
                    try
                    {
                        bitmap = data != null ? new Bitmap(new MemoryStream(data)) : null;
                    }
                    catch (Exception)
                    {
                        // Ohne Bild lässt sich trotzdem installieren
                        bitmap = null;
                    }
                    ThumbnailCache[url] = bitmap;
                }
                item.Thumbnail = bitmap;
            }
        }
        await Task.WhenAll(Enumerable.Range(0, ParallelThumbnails).Select(_ => Worker()));
    }

    // ---------- Aktionen ----------

    /// <summary>Installiert bzw. aktualisiert ein Katalogprodukt auf dem gewählten Stift.</summary>
    [RelayCommand]
    async Task Install(CatalogItem? item)
    {
        if (item == null)
            return;
        if (SelectedDrive is not { } drive)
        {
            Status = "Kein Stift gewählt: Stift anschließen und „Neu einlesen“ oder „Ordner wählen …“.";
            return;
        }
        await Run(async cancel =>
        {
            using var http = RavensburgerCatalog.CreateHttpClient(Handler);
            Status = $"Lade „{item.Title}“ …";
            Progress = 0;
            await PenManager.InstallAsync(drive, new RavensburgerCatalog(http), item.Entry, item.OnPen,
                new Progress<double>(p => Progress = p), cancel);
            Status = $"„{item.Title}“ ist auf dem Stift. Vor dem Abziehen den Stift auswerfen.";
            _log($"Auf den Stift installiert: {item.Entry.FileName}");
        });
        await RefreshPen();
    }

    /// <summary>Aktualisiert eine Datei auf dem Stift aus dem Katalog.</summary>
    [RelayCommand]
    async Task UpdateItem(PenItem? item)
    {
        if (item?.Entry == null)
            return;
        await Install(_allCatalogItems.FirstOrDefault(c => c.Entry == item.Entry));
    }

    /// <summary>Löscht eine Datei vom Stift (nach Rückfrage).</summary>
    [RelayCommand]
    async Task Delete(PenItem? item)
    {
        if (item == null || SelectedDrive is not { } drive)
            return;
        var question = $"„{item.Title}“ ({item.Product.RelativePath}, {PenManager.FormatSize(item.Product.Size)}) vom Stift löschen?" +
                       (item.IsOfficial ? " Ravensburger-Produkte lassen sich später wieder installieren." : " Eigene Projekte lassen sich nur neu erstellen.");
        if (Dialogs != null && !await Dialogs.ConfirmAsync(question))
            return;
        await Run(async _ =>
        {
            await drive.DeleteAsync(item.Product.RelativePath);
            Status = $"Gelöscht: {item.Product.RelativePath}";
            _log($"Vom Stift gelöscht: {item.Product.RelativePath}");
        });
        await RefreshPen();
    }

    /// <summary>Öffnet die Ravensburger-Seite mit allen Audiodateien (im Browser der Weg, sie von Hand zu laden).</summary>
    [RelayCommand]
    void OpenRavensburgerPage() => Dialogs?.OpenUrl(RavensburgerCatalog.IndexUrl);

    /// <summary>Bricht den laufenden Download bzw. Katalogabruf ab.</summary>
    [RelayCommand]
    void Cancel() => _cancel?.Cancel();

    [RelayCommand]
    void Close()
    {
        _cancel?.Cancel();
        _close();
    }

    async Task Run(Func<CancellationToken, Task> body)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            await body(_cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Abgebrochen.";
        }
        catch (Exception e) when (e is PenException or RavensburgerException)
        {
            Status = e.Message;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = $"Fehler: {e.Message} – ist der Stift noch verbunden?";
        }
        finally
        {
            IsBusy = false;
            Progress = null;
            _cancel.Dispose();
            _cancel = null;
        }
    }
}
