using System.Net;
using System.Net.Http.Headers;
using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Gefälschte Ravensburger-Server: feste Seiten und GME-Dateien, beantwortet auch Range-Anfragen.</summary>
class FakeRavensburger : HttpMessageHandler
{
    public readonly Dictionary<string, string> Pages = new();
    public readonly Dictionary<string, byte[]> Files = new();
    public readonly List<string> Requests = [];

    /// <summary>Downloads (ohne Range) schlagen mit diesem Status fehl.</summary>
    public HttpStatusCode? DownloadStatus;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        lock (Requests)
            Requests.Add((request.Headers.Range != null ? "RANGE " : "GET ") + url);
        if (Pages.TryGetValue(url, out var html))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        if (Files.TryGetValue(url, out var data))
        {
            if (request.Headers.Range?.Ranges.FirstOrDefault() is { } range)
            {
                int from = (int)range.From!.Value, to = (int)Math.Min(range.To!.Value, data.Length - 1);
                var part = new ByteArrayContent(data[from..(to + 1)]);
                part.Headers.ContentRange = new ContentRangeHeaderValue(from, to, data.Length);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = part });
            }
            if (DownloadStatus is { } status)
                return Task.FromResult(new HttpResponseMessage(status));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>Stift-Manager: Ravensburger-Katalog (mit gefälschtem Server), Abgleich, Installieren, Löschen.</summary>
[Collection("Stift")]
public class PenManagerTests : IDisposable
{
    const string Official = "CHOMPTECH DATA FORMAT CopyRight 2009 Ver2.10.0901";
    const string Index = RavensburgerCatalog.IndexUrl;
    const string Books = Index + "/Audiodateien_tiptoi%C2%AE_B%C3%BCcher";
    const string Globe = Index + "/Audiodateien_tiptoi%C2%AE_Globen/tiptoi%C2%AE_Audiodatei_Globus_00787_und_00515";
    const string Farm = Books + "/tiptoi%C2%AE_Audiodatei_Bauernhof_00123";
    const string GlobeGme = "https://ravensburger.cloud/rvwebsite/rvDE/db/applications/Der%20interaktive%20Globus.gme";
    const string PuzzleGme = "https://ravensburger.cloud/rvwebsite/rvDE/db/applications/Globus_puzzleball.gme";
    const string FarmGme = "https://ravensburger.cloud/tiptoi/db/applications/Bauernhof.gme";

    readonly string _pen = Directory.CreateTempSubdirectory("tupfer-stift").FullName;
    readonly string _work = Directory.CreateTempSubdirectory("tupfer-katalog").FullName;
    readonly FakeRavensburger _server = new();

    public PenManagerTests()
    {
        _server.Pages[Index] = $"""
            <html><body>
            <a href="{Books}">Bücher</a>
            <a href="{Farm}">Bauernhof</a>
            <a href="{Index}/Audiodateien_tiptoi%C2%AE_Globen">Globen</a>
            <a href="{Globe}">Globus</a>
            <a href="https://service.ravensburger.de/Special:Search?tags=tiptoi">Suche</a>
            </body></html>
            """;
        _server.Pages[Globe] = $"""
            <html><head><title>tiptoi® Audiodatei Der interaktive Globus 00787 und Puzzleball 00515 - Serviceportal Ravensburger</title></head>
            <body><img src="https://service.ravensburger.de/@api/deki/files/676/Logo_Ravensburger.png" />
            <p><a href="{GlobeGme}" class="link-https"><img src="https://service.ravensburger.de/@api/deki/files/1775/globus.png?revision=1&amp;size=bestfit" /></a></p>
            <p><a title="x" href="{PuzzleGme}"><img src="https://service.ravensburger.de/@api/deki/files/1776/puzzle.png" /></a></p>
            <p><a href="https://example.org/fremd.gme">fremde Datei</a></p>
            </body></html>
            """;
        _server.Pages[Farm] = $"""
            <html><head><title>tiptoi® Audiodatei Bauernhof 00123 - Serviceportal Ravensburger</title></head>
            <body><img src="https://service.ravensburger.de/@api/deki/files/12/bauernhof.png" />
            <a href="{FarmGme}">Download</a></body></html>
            """;
        _server.Files[GlobeGme] = Gme(500, Official, new DateTime(2024, 5, 8), 4000);
        _server.Files[PuzzleGme] = Gme(501, Official, new DateTime(2023, 1, 2), 100);
        _server.Files[FarmGme] = Gme(600, Official, new DateTime(2025, 3, 19), 2000);
        RavensburgerCatalog.CachePath = Path.Combine(_work, "katalog.json");
        // Kein echtes lsblk/udisksctl in Tests
        PenMounter.IsSupported = false;
    }

    public void Dispose()
    {
        Directory.Delete(_pen, true);
        Directory.Delete(_work, true);
        PenManagerViewModel.Handler = null;
        PenDrives.Find = () => [];
    }

    /// <summary>Eine echte kleine GME; <paramref name="padding"/> Bytes Audio machen sie größer.</summary>
    static byte[] Gme(uint productId, string comment, DateTime date, int padding = 3)
    {
        var project = new ProjectFile { ProductId = productId, Comment = comment };
        project.Scripts.Add(new ScriptEntry { Name = "a", Lines = ["P(hallo)"] });
        project.Speak.Add(new SpeakEntry { Name = "hallo", Text = "Hallo" });
        return GmeBuilder.Build(project, Path.GetTempPath(), (_, _) => new byte[padding], date);
    }

    void OnPen(string name, byte[] data)
    {
        var path = Path.Combine(_pen, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    async Task<List<CatalogEntry>> LoadCatalog()
    {
        using var http = RavensburgerCatalog.CreateHttpClient(_server);
        return await new RavensburgerCatalog(http).LoadAsync();
    }

    [Fact]
    public void ProduktseitenUndRubrikenWerdenErkannt()
    {
        var pages = RavensburgerCatalog.ProductPages(_server.Pages[Index]);
        Assert.Equal([(Farm, "Bücher"), (Globe, "Globen")], pages.Select(p => (p.Url, p.Category)));
    }

    [Fact]
    public void ProduktseiteMitMehrerenDateien()
    {
        var entries = RavensburgerCatalog.ParseProductPage(_server.Pages[Globe], Globe, "Globen");
        Assert.Equal(2, entries.Count);
        Assert.Equal("Der interaktive Globus 00787 und Puzzleball 00515 – Der interaktive Globus", entries[0].Title);
        Assert.Equal("00787, 00515", entries[0].ArticleNumbers);
        Assert.Equal("Der interaktive Globus.gme", entries[0].FileName);
        Assert.Equal("https://service.ravensburger.de/@api/deki/files/1775/globus.png?revision=1&size=bestfit", entries[0].ImageUrl);
        Assert.Equal("https://service.ravensburger.de/@api/deki/files/1776/puzzle.png", entries[1].ImageUrl);
        // Nur Downloads von Ravensburger selbst
        Assert.DoesNotContain(entries, e => e.DownloadUrl.Contains("example.org"));
    }

    [Fact]
    public async Task KatalogLiestProduktIdUndGroesseAusDemKopf()
    {
        var entries = await LoadCatalog();
        Assert.Equal(3, entries.Count);
        var farm = Assert.Single(entries, e => e.FileName == "Bauernhof.gme");
        Assert.Equal("Bauernhof 00123", farm.Title);
        Assert.Equal("https://service.ravensburger.de/@api/deki/files/12/bauernhof.png", farm.ImageUrl);
        Assert.Equal(600u, farm.ProductId);
        Assert.Equal(new DateTime(2025, 3, 19), farm.Version);
        Assert.Equal(_server.Files[FarmGme].Length, farm.Size);
        // Nur Köpfe abgefragt, nichts ganz geladen; Rubrikseiten nicht abgerufen
        Assert.All(_server.Requests.Where(r => r.EndsWith(".gme")), r => Assert.StartsWith("RANGE ", r));
        Assert.DoesNotContain("GET " + Books, _server.Requests);
    }

    [Fact]
    public async Task OhneVerbindungVerstaendlicheMeldung()
    {
        _server.Pages.Clear();
        var e = await Assert.ThrowsAsync<RavensburgerException>(LoadCatalog);
        Assert.Contains("Fehler 404", e.Message);
    }

    [Fact]
    public async Task StandAufDemStiftWirdErkannt()
    {
        var catalog = await LoadCatalog();
        OnPen("Bauernhof.gme", _server.Files[FarmGme]);
        // Ältere Fassung unter anderem Namen, an der Produkt-ID erkannt
        OnPen("alt/globus-alt.gme", Gme(500, Official, new DateTime(2020, 1, 1), 4000));
        OnPen("eigenes.gme", Gme(900, "Mein Buch", new DateTime(2026, 1, 1)));
        var products = PenContents.ReadProducts(_pen);

        var globe = catalog.Single(e => e.ProductId == 500);
        Assert.Equal(CatalogState.Installed, PenManager.StateOf(catalog.Single(e => e.ProductId == 600), products, catalog));
        Assert.Equal(CatalogState.UpdateAvailable, PenManager.StateOf(globe, products, catalog));
        Assert.Equal(CatalogState.Missing, PenManager.StateOf(catalog.Single(e => e.ProductId == 501), products, catalog));
        Assert.Equal("alt" + Path.DirectorySeparatorChar + "globus-alt.gme", PenManager.FindOnPen(globe, products, catalog)!.RelativePath);
        Assert.Null(PenManager.FindInCatalog(products.Single(p => p.ProductId == 900), products, catalog));

        // Gleiches Datum, aber andere Größe: auch ein Update
        File.WriteAllBytes(Path.Combine(_pen, "Bauernhof.gme"), Gme(600, Official, new DateTime(2025, 3, 19), 10));
        products = PenContents.ReadProducts(_pen);
        Assert.Equal(CatalogState.UpdateAvailable, PenManager.StateOf(catalog.Single(e => e.ProductId == 600), products, catalog));
    }

    [Fact]
    public async Task InstallierenErsetztAlteFassung()
    {
        var catalog = await LoadCatalog();
        OnPen("alt/globus-alt.gme", Gme(500, Official, new DateTime(2020, 1, 1)));
        var drive = new FolderPenDrive(_pen);
        var globe = catalog.Single(e => e.ProductId == 500);
        var old = PenManager.FindOnPen(globe, PenContents.ReadProducts(_pen), catalog);
        double last = 0;

        using var http = RavensburgerCatalog.CreateHttpClient(_server);
        await PenManager.InstallAsync(drive, new RavensburgerCatalog(http), globe, old, new SyncProgress(p => last = p));

        Assert.Equal(_server.Files[GlobeGme], File.ReadAllBytes(Path.Combine(_pen, "Der interaktive Globus.gme")));
        Assert.False(File.Exists(Path.Combine(_pen, "alt", "globus-alt.gme")));
        Assert.Equal(1.0, last);
        Assert.Empty(Directory.GetFiles(_pen, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ZuWenigSpeicher()
    {
        var catalog = await LoadCatalog();
        var drive = new FolderPenDrive(_pen) { SpaceOverride = new PenSpace(1000, 1 << 30) };
        using var http = RavensburgerCatalog.CreateHttpClient(_server);
        var e = await Assert.ThrowsAsync<PenException>(() =>
            PenManager.InstallAsync(drive, new RavensburgerCatalog(http), catalog[0], null));
        Assert.Contains("Zu wenig Speicher", e.Message);
        Assert.Empty(Directory.GetFiles(_pen));
        Assert.DoesNotContain(_server.Requests, r => r.StartsWith("GET ") && r.EndsWith(".gme"));
    }

    [Fact]
    public async Task DownloadfehlerLaesstStiftUnveraendert()
    {
        var catalog = await LoadCatalog();
        _server.DownloadStatus = HttpStatusCode.InternalServerError;
        using var http = RavensburgerCatalog.CreateHttpClient(_server);
        var e = await Assert.ThrowsAsync<RavensburgerException>(() =>
            PenManager.InstallAsync(new FolderPenDrive(_pen), new RavensburgerCatalog(http), catalog[0], null));
        Assert.Contains("Download", e.Message);
        Assert.Contains("500", e.Message);
        Assert.Empty(Directory.GetFiles(_pen));
    }

    [Fact]
    public async Task LoeschenNurInnerhalbDesStifts()
    {
        var drive = new FolderPenDrive(_pen);
        await Assert.ThrowsAsync<IOException>(() => drive.DeleteAsync(Path.Combine("..", "fremd.gme")));
    }

    class Confirm(bool answer) : IFileDialogs
    {
        public string? Message;
        public Task<string?> OpenAsync(string title, string filterName, params string[] patterns) => Task.FromResult<string?>(null);
        public Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension) => Task.FromResult<string?>(null);
        public Task<string> PrintAsync(string pdfPath) => Task.FromResult("");
        public Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string message)
        {
            Message = message;
            return Task.FromResult(answer);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManagerZeigtStiftUndKatalog(bool confirmDelete)
    {
        OnPen("Bauernhof.gme", Gme(600, Official, new DateTime(2024, 1, 1), 2000));
        OnPen("eigenes.gme", Gme(900, "Mein Buch", new DateTime(2026, 1, 1)));
        OnPen("kopie.gme", Gme(900, "Mein Buch alt", new DateTime(2025, 1, 1)));
        PenManagerViewModel.Handler = _server;
        PenDrives.Find = () => [new FolderPenDrive(_pen)];
        var dialogs = new Confirm(confirmDelete);
        var log = new List<string>();
        var vm = new PenManagerViewModel(dialogs, () => { }, log.Add, projectProductId: 900);

        await vm.StartAsync();
        await vm.RefreshPen();

        Assert.Equal(_pen, vm.SelectedDrive!.Name);
        Assert.Contains("frei von", vm.SpaceText);
        Assert.Equal("3 GME-Datei(en): 1 von Ravensburger, 2 eigene", vm.PenSummary);
        Assert.Contains("Doppelte Produkt-ID 900", vm.Warnings);
        Assert.Contains("Produkt-ID des geöffneten Projekts (900)", vm.Warnings);
        var farm = Assert.Single(vm.PenItems, i => i.IsOfficial);
        Assert.Equal("Bauernhof 00123", farm.Title);
        Assert.True(farm.UpdateAvailable);
        Assert.Equal("Mein Buch", vm.PenItems.First(i => !i.IsOfficial).Title);
        Assert.Equal(3, vm.CatalogItems.Count);
        Assert.Equal(CatalogState.UpdateAvailable, vm.CatalogItems[0].State);
        Assert.True(File.Exists(RavensburgerCatalog.CachePath));

        // Update aus der Stift-Liste
        await vm.UpdateItemCommand.ExecuteAsync(farm);
        Assert.Equal(_server.Files[FarmGme], File.ReadAllBytes(Path.Combine(_pen, "Bauernhof.gme")));
        Assert.False(vm.PenItems.Single(i => i.IsOfficial).UpdateAvailable);
        Assert.Contains(log, l => l.Contains("Bauernhof.gme"));

        // Installieren aus dem Katalog
        vm.SearchText = "puzzleball";
        Assert.Equal(2, vm.CatalogItems.Count);
        vm.OnlyUpdates = true;
        Assert.Empty(vm.CatalogItems);
        vm.OnlyUpdates = false;
        vm.SearchText = "501";
        var puzzle = Assert.Single(vm.CatalogItems);
        await vm.InstallCommand.ExecuteAsync(puzzle);
        Assert.True(File.Exists(Path.Combine(_pen, "Globus_puzzleball.gme")));
        Assert.Equal(CatalogState.Installed, puzzle.State);

        // Löschen nur nach Rückfrage
        await vm.DeleteCommand.ExecuteAsync(vm.PenItems.Single(i => i.Product.RelativePath == "kopie.gme"));
        Assert.Contains("kopie.gme", dialogs.Message);
        Assert.Equal(!confirmDelete, File.Exists(Path.Combine(_pen, "kopie.gme")));

        // Zweites Öffnen: Katalog aus dem Zwischenspeicher, ohne neue Anfragen
        int requests = _server.Requests.Count;
        var again = new PenManagerViewModel(dialogs, () => { }, _ => { });
        await again.StartAsync();
        Assert.Equal(3, again.CatalogItems.Count);
        Assert.DoesNotContain(_server.Requests.Skip(requests), r => r.Contains("service.ravensburger.de/tiptoi"));
    }

    [Fact]
    public async Task OhneStiftHinweis()
    {
        PenManagerViewModel.Handler = _server;
        PenDrives.Find = () => [];
        var vm = new PenManagerViewModel(null, () => { }, _ => { });
        await vm.StartAsync();
        Assert.Null(vm.SelectedDrive);
        Assert.Contains("Kein Stift gefunden", vm.Status);

        await vm.InstallCommand.ExecuteAsync(vm.CatalogItems[0]);
        Assert.Contains("Kein Stift gewählt", vm.Status);
    }

    /// <summary>Wie Progress, aber sofort (ohne Umweg über den Thread-Pool).</summary>
    class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
