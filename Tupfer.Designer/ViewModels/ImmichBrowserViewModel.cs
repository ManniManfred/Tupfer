using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

/// <summary>Ein Album, eine Person oder ein Foto in der Auswahl aus Immich.</summary>
public partial class ImmichItem(string title, string? thumbnailId, bool isPerson = false) : ObservableObject
{
    public string Title { get; } = title;

    /// <summary>Foto (bzw. bei Personen die Person), dessen Vorschaubild gezeigt wird.</summary>
    public string? ThumbnailId { get; } = thumbnailId;
    public bool IsPerson { get; } = isPerson;

    public string? AlbumId { get; init; }
    public string? PersonId { get; init; }
    public ImmichAsset? Asset { get; init; }
    public string Subtitle { get; init; } = "";

    [ObservableProperty] Bitmap? _thumbnail;
    [ObservableProperty] bool _isChecked;
}

/// <summary>
/// Auswahl von Fotos aus Immich (als Ebene über der Ansicht, auch im Browser): Zugangsdaten, Alben bzw.
/// Personen mit Vorschaubildern, darin die Fotos zum Ankreuzen.
/// </summary>
public partial class ImmichBrowserViewModel : ObservableObject
{
    readonly Settings _settings;
    readonly Func<IReadOnlyList<ImmichAsset>, bool, Task> _insert;
    readonly Action _close;

    /// <param name="insert">Fügt die gewählten Fotos ein (Original ja/nein).</param>
    public ImmichBrowserViewModel(Settings settings, Func<IReadOnlyList<ImmichAsset>, bool, Task> insert, Action close)
    {
        _settings = settings;
        _insert = insert;
        _close = close;
        _serverUrl = settings.ImmichUrl;
        _apiKey = settings.ImmichApiKey;
        _downloadOriginal = settings.ImmichOriginal;
        _showSettings = settings.ImmichUrl.Length == 0 || settings.ImmichApiKey.Length == 0;
    }

    /// <summary>Gefälschter HTTP-Handler für Tests; null = echte Verbindung.</summary>
    public static HttpMessageHandler? Handler { get; set; }

    /// <summary>Höchstens so viele Vorschaubilder gleichzeitig laden.</summary>
    const int ParallelThumbnails = 6;

    // ---------- Zugang ----------

    [ObservableProperty] string _serverUrl;
    [ObservableProperty] string _apiKey;
    [ObservableProperty] bool _downloadOriginal;
    [ObservableProperty] bool _showSettings;

    /// <summary>Hinweis für den Browser: Immich erlaubt Zugriffe anderer Seiten (CORS) meist nicht.</summary>
    public static bool ShowCorsHint => !MainWindowViewModel.IsDesktop;

    partial void OnDownloadOriginalChanged(bool value)
    {
        _settings.ImmichOriginal = value;
        SaveSettings();
    }

    void SaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = $"Die Einstellungen ließen sich nicht speichern: {e.Message}";
        }
    }

    ImmichClient CreateClient() => new(_settings.ImmichUrl, _settings.ImmichApiKey, Handler);

    /// <summary>Übernimmt Adresse und Key, prüft die Verbindung und zeigt dann die Alben.</summary>
    [RelayCommand]
    async Task Connect()
    {
        await Run(async () =>
        {
            _settings.ImmichUrl = ImmichClient.NormalizeUrl(ServerUrl);
            _settings.ImmichApiKey = (ApiKey ?? "").Trim();
            ServerUrl = _settings.ImmichUrl;
            using var client = CreateClient();
            var name = await client.CheckAsync();
            SaveSettings();
            ShowSettings = false;
            Status = name.Length > 0 ? $"Verbunden als {name}." : "Verbunden.";
        });
        if (!ShowSettings)
            await LoadCollections();
    }

    [RelayCommand] void EditSettings() => ShowSettings = true;

    // ---------- Alben und Personen ----------

    /// <summary>0 = Alben, 1 = Personen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumMode), nameof(PeopleMode))]
    int _mode;

    partial void OnModeChanged(int value) => _ = LoadCollections();

    // Für die Umschalter
    public bool AlbumMode { get => Mode == 0; set { if (value) Mode = 0; } }
    public bool PeopleMode { get => Mode == 1; set { if (value) Mode = 1; } }

    public ObservableCollection<ImmichItem> Collections { get; } = [];
    public ObservableCollection<ImmichItem> Photos { get; } = [];

    /// <summary>Geöffnetes Album bzw. geöffnete Person; null = Übersicht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPhotos), nameof(ShowCollections))]
    ImmichItem? _current;

    public bool ShowPhotos => Current != null && !ShowSettings;
    public bool ShowCollections => Current == null && !ShowSettings;

    partial void OnShowSettingsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPhotos));
        OnPropertyChanged(nameof(ShowCollections));
    }

    [ObservableProperty] string _status = "";
    [ObservableProperty] bool _isBusy;
    [ObservableProperty] bool _hasMore;

    int? _nextPage;

    /// <summary>Zählt hoch, wenn eine neue Liste angezeigt wird; ältere Ladevorgänge hören dann auf.</summary>
    int _generation;

    /// <summary>Zeigt die Alben bzw. Personen (beim Öffnen, wenn der Zugang schon eingerichtet ist).</summary>
    public async Task StartAsync()
    {
        if (!ShowSettings)
            await LoadCollections();
    }

    async Task LoadCollections()
    {
        int generation = ++_generation;
        Current = null;
        Collections.Clear();
        Photos.Clear();
        await Run(async () =>
        {
            using var client = CreateClient();
            List<ImmichItem> items;
            if (Mode == 0)
            {
                var albums = await client.GetAlbumsAsync();
                items = albums.Select(a => new ImmichItem(a.AlbumName, a.AlbumThumbnailAssetId)
                {
                    AlbumId = a.Id, Subtitle = a.AssetCount == 1 ? "1 Foto" : $"{a.AssetCount} Fotos",
                }).ToList();
                Status = albums.Count == 0 ? "In Immich gibt es keine Alben." : "";
            }
            else
            {
                var people = await client.GetPeopleAsync();
                items = people.Select(p => new ImmichItem(p.Name, p.Id, isPerson: true) { PersonId = p.Id }).ToList();
                Status = people.Count == 0 ? "In Immich gibt es keine Personen mit Namen." : "";
            }
            if (generation != _generation)
                return;
            foreach (var item in items)
                Collections.Add(item);
        });
        await LoadThumbnailsAsync(Collections.ToList(), generation);
    }

    /// <summary>Öffnet ein Album bzw. eine Person und zeigt ihre Fotos.</summary>
    [RelayCommand]
    async Task Open(ImmichItem item)
    {
        int generation = ++_generation;
        Current = item;
        Photos.Clear();
        _nextPage = 1;
        await LoadMoreFor(generation);
    }

    [RelayCommand]
    void Back()
    {
        ++_generation;
        Current = null;
        Photos.Clear();
        HasMore = false;
        OnPropertyChanged(nameof(SelectedCount));
        Status = "";
    }

    [RelayCommand] Task LoadMore() => LoadMoreFor(_generation);

    async Task LoadMoreFor(int generation)
    {
        if (Current is not { } current || _nextPage is not { } page)
            return;
        var added = new List<ImmichItem>();
        await Run(async () =>
        {
            using var client = CreateClient();
            var result = await client.SearchAssetsAsync(current.AlbumId, current.PersonId, page);
            if (generation != _generation)
                return;
            foreach (var asset in result.Items)
            {
                var item = new ImmichItem(asset.OriginalFileName ?? "", asset.Id) { Asset = asset };
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ImmichItem.IsChecked))
                        OnPropertyChanged(nameof(SelectedCount));
                };
                Photos.Add(item);
                added.Add(item);
            }
            _nextPage = result.NextPage;
            HasMore = result.NextPage != null;
            Status = Photos.Count == 0 ? "Hier gibt es keine Fotos." : "Fotos zum Einfügen antippen.";
        });
        await LoadThumbnailsAsync(added, generation);
    }

    async Task LoadThumbnailsAsync(List<ImmichItem> items, int generation)
    {
        if (items.Count == 0)
            return;
        ImmichClient client;
        try
        {
            client = CreateClient();
        }
        catch (ImmichException)
        {
            return;
        }
        using (client)
        {
            int next = 0;
            async Task Worker()
            {
                while (generation == _generation && next < items.Count)
                {
                    var item = items[next++];
                    if (item.ThumbnailId == null)
                        continue;
                    try
                    {
                        var data = item.IsPerson
                            ? await client.GetPersonThumbnailAsync(item.ThumbnailId)
                            : await client.GetThumbnailAsync(item.ThumbnailId);
                        using var stream = new MemoryStream(data);
                        item.Thumbnail = new Bitmap(stream);
                    }
                    catch (Exception)
                    {
                        // Ohne Vorschaubild lässt sich trotzdem wählen
                    }
                }
            }
            await Task.WhenAll(Enumerable.Range(0, ParallelThumbnails).Select(_ => Worker()));
        }
    }

    // ---------- Einfügen ----------

    public int SelectedCount => Photos.Count(p => p.IsChecked);

    [RelayCommand]
    async Task Insert()
    {
        var assets = Photos.Where(p => p.IsChecked && p.Asset != null).Select(p => p.Asset!).ToList();
        if (assets.Count == 0)
        {
            Status = "Bitte zuerst Fotos antippen.";
            return;
        }
        bool ok = false;
        await Run(async () =>
        {
            Status = assets.Count == 1 ? "Lade das Foto herunter …" : $"Lade {assets.Count} Fotos herunter …";
            await _insert(assets, DownloadOriginal);
            ok = true;
        });
        if (ok)
            Close();
    }

    [RelayCommand]
    void Close()
    {
        ++_generation;
        _close();
    }

    /// <summary>Führt einen Zugriff aus und zeigt Fehler als Status an.</summary>
    async Task Run(Func<Task> body)
    {
        IsBusy = true;
        try
        {
            await body();
        }
        catch (ImmichException e)
        {
            Status = e.Message;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = $"Fehler: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
