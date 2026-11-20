using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Gefälschter Immich-Server: Antworten je „Methode Pfad?Abfrage“, merkt sich die Anfragen.</summary>
class FakeImmich : HttpMessageHandler
{
    public readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> Routes = new();
    public readonly List<(HttpRequestMessage Request, string? Body)> Requests = [];

    public void Json(string route, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Routes[route] = _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public void Bytes(string route, byte[] data) =>
        Routes[route] = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request, request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        var key = $"{request.Method} {request.RequestUri!.PathAndQuery}";
        return Routes.TryGetValue(key, out var route)
            ? route(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"message":"Not Found","statusCode":404}""", Encoding.UTF8, "application/json"),
            };
    }
}

/// <summary>Anbindung an Immich: Antworten lesen, Fehler melden, Gesichter auf das Bild umrechnen, Namen zuordnen.</summary>
public class ImmichTests
{
    const string Key = "geheimer-key";
    const string Asset = "4b1e2f7a-1c2d-4e5f-8a9b-0c1d2e3f4a5b";

    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x02, 0xFF, 0xD9];
    static readonly byte[] Heic = [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c'];

    static (ImmichClient Client, FakeImmich Server) Connect(string url = "https://fotos.example.org")
    {
        var server = new FakeImmich();
        return (new ImmichClient(url, Key, server), server);
    }

    [Theory]
    [InlineData("https://fotos.example.org", "https://fotos.example.org")]
    [InlineData(" https://fotos.example.org/ ", "https://fotos.example.org")]
    [InlineData("https://fotos.example.org/api", "https://fotos.example.org")]
    [InlineData("https://fotos.example.org/api/", "https://fotos.example.org")]
    [InlineData("fotos.example.org:2283", "https://fotos.example.org:2283")]
    [InlineData("http://192.168.1.5:2283", "http://192.168.1.5:2283")]
    [InlineData("https://example.org/immich", "https://example.org/immich")]
    [InlineData("", "")]
    public void AdresseWirdVereinheitlicht(string input, string expected) =>
        Assert.Equal(expected, ImmichClient.NormalizeUrl(input));

    [Fact]
    public void UngueltigeAdresseWirdGemeldet()
    {
        var e = Assert.Throws<ImmichException>(() => ImmichClient.NormalizeUrl("ftp://fotos"));
        Assert.Contains("keine gültige Adresse", e.Message);
        Assert.Contains("API-Key", Assert.Throws<ImmichException>(() => new ImmichClient("https://fotos.example.org", " ")).Message);
    }

    [Fact]
    public async Task KeyStehtImHeaderUndAdresseUnterApi()
    {
        var (client, server) = Connect("https://fotos.example.org/api/");
        server.Json("GET /api/users/me", """{"id":"u1","email":"anni@example.org","name":"Anni","isAdmin":false}""");
        Assert.Equal("Anni", await client.CheckAsync());
        var request = server.Requests.Single().Request;
        Assert.Equal("https://fotos.example.org/api/users/me", request.RequestUri!.ToString());
        Assert.Equal(Key, request.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task FalscherKeyWirdVerstaendlichGemeldet()
    {
        var (client, server) = Connect();
        server.Json("GET /api/users/me", """{"message":"Invalid API key","error":"Unauthorized","statusCode":401}""", HttpStatusCode.Unauthorized);
        var e = await Assert.ThrowsAsync<ImmichException>(client.CheckAsync);
        Assert.Contains("API-Key abgelehnt", e.Message);
    }

    [Fact]
    public async Task FehlendeRechteWerdenGenannt()
    {
        var (client, server) = Connect();
        server.Json("GET /api/albums", """{"message":"Missing required permission: album.read","error":"Forbidden","statusCode":403}""", HttpStatusCode.Forbidden);
        var e = await Assert.ThrowsAsync<ImmichException>(client.GetAlbumsAsync);
        Assert.Contains("Missing required permission: album.read", e.Message);
        Assert.Contains("face.read", e.Message);
        Assert.Contains("user.read", e.Message);
    }

    [Fact]
    public async Task NichtErreichbarerServerWirdGemeldet()
    {
        var server = new FakeImmich();
        server.Routes["GET /api/users/me"] = _ => throw new HttpRequestException("Connection refused");
        using var client = new ImmichClient("https://fotos.example.org", Key, server);
        var e = await Assert.ThrowsAsync<ImmichException>(client.CheckAsync);
        Assert.Contains("nicht erreichbar", e.Message);
        Assert.Contains("Connection refused", e.Message);
    }

    [Fact]
    public async Task ImBrowserWirdAufCorsHingewiesen()
    {
        bool before = ImmichClient.IsBrowser;
        ImmichClient.IsBrowser = true;
        try
        {
            var server = new FakeImmich();
            server.Routes["GET /api/users/me"] = _ => throw new HttpRequestException("TypeError: Failed to fetch");
            using var client = new ImmichClient("https://fotos.example.org", Key, server);
            var e = await Assert.ThrowsAsync<ImmichException>(client.CheckAsync);
            Assert.Contains("CORS", e.Message);

            // Web-Version über https: http-Adressen blockiert der Browser ohnehin
            ImmichClient.PageIsSecure = true;
            Assert.Contains("https", Assert.Throws<ImmichException>(() => new ImmichClient("http://192.168.1.5:2283", Key, server)).Message);
        }
        finally
        {
            ImmichClient.IsBrowser = before;
            ImmichClient.PageIsSecure = false;
        }
    }

    [Fact]
    public async Task InNextcloudUeberDieWeiterleitung()
    {
        var before = ImmichClient.Proxy;
        int token = 0;
        ImmichClient.Proxy = new ImmichProxy("https://cloud.example.org/index.php/apps/tupfer/immich/api/",
            "https://fotos.example.org", () => $"token{++token}");
        try
        {
            var server = new FakeImmich();
            server.Json("GET /index.php/apps/tupfer/immich/api/users/me", """{"name":"Anni"}""");
            server.Json($"GET /index.php/apps/tupfer/immich/api/faces?id={Asset}", "[]");
            // Die Adresse aus den Einstellungen gilt nicht, nur der in Nextcloud eingetragene Server
            using var client = new ImmichClient("https://anderswo.example.org", Key, server);
            Assert.Equal("https://fotos.example.org", client.ServerUrl);
            Assert.Equal("Anni", await client.CheckAsync());
            Assert.Empty(await client.GetFacesAsync(Asset));
            // Key wird durchgereicht, das CSRF-Token jedes Mal frisch geholt
            Assert.Equal(Key, server.Requests[0].Request.Headers.GetValues("x-api-key").Single());
            Assert.Equal(["token1", "token2"], server.Requests.Select(r => r.Request.Headers.GetValues("requesttoken").Single()));

            // Fehler der Weiterleitung kommen mit der Meldung von Nextcloud
            server.Json("GET /index.php/apps/tupfer/immich/api/albums",
                """{"message":"Der Immich-Server ist vom Nextcloud-Server aus nicht erreichbar: cURL error 7","statusCode":502}""", HttpStatusCode.BadGateway);
            var e = await Assert.ThrowsAsync<ImmichException>(client.GetAlbumsAsync);
            Assert.Contains("vom Nextcloud-Server aus nicht erreichbar", e.Message);
            Assert.DoesNotContain("CORS", e.Message);

            // Abgelaufene Nextcloud-Sitzung ist kein falscher API-Key
            server.Json("GET /index.php/apps/tupfer/immich/api/users/me", """{"message":"CSRF check failed"}""", HttpStatusCode.PreconditionFailed);
            Assert.Contains("Seite neu laden", (await Assert.ThrowsAsync<ImmichException>(client.CheckAsync)).Message);
            server.Json("GET /index.php/apps/tupfer/immich/api/users/me", """{"message":"Invalid API key","statusCode":401}""", HttpStatusCode.Unauthorized);
            Assert.Contains("API-Key abgelehnt", (await Assert.ThrowsAsync<ImmichException>(client.CheckAsync)).Message);

            // Ohne eingetragenen Server sagt der Designer, was der Administrator tun muss
            ImmichClient.Proxy = ImmichClient.Proxy with { ServerUrl = "" };
            Assert.Contains("occ config:app:set tupfer immich_url",
                Assert.Throws<ImmichException>(() => new ImmichClient("https://fotos.example.org", Key, server)).Message);
        }
        finally
        {
            ImmichClient.Proxy = before;
        }
    }

    [Fact]
    public async Task FalscheAdresseOhneImmichWirdErkannt()
    {
        var (client, server) = Connect();
        server.Routes["GET /api/users/me"] = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>Willkommen</body></html>", Encoding.UTF8, "text/html"),
        };
        var e = await Assert.ThrowsAsync<ImmichException>(client.CheckAsync);
        Assert.Contains("kein Immich-Server", e.Message);
    }

    [Fact]
    public async Task AlbenUndPersonenWerdenGelesen()
    {
        var (client, server) = Connect();
        server.Json("GET /api/albums", """
            [{"id":"a2","albumName":"Urlaub","albumThumbnailAssetId":"t2","assetCount":12,"shared":false,"albumUsers":[],"description":""},
             {"id":"a1","albumName":"familie","albumThumbnailAssetId":null,"assetCount":1,"shared":true,"albumUsers":[],"description":""}]
            """);
        // Personen seitenweise; ohne Namen und versteckte entfallen
        server.Json("GET /api/people?withHidden=false&size=500&page=1", """
            {"total":3,"hidden":0,"hasNextPage":true,"people":[
              {"id":"p1","name":"Paul","isHidden":false,"birthDate":null,"thumbnailPath":"/x"},
              {"id":"p2","name":"","isHidden":false,"birthDate":null,"thumbnailPath":"/x"}]}
            """);
        server.Json("GET /api/people?withHidden=false&size=500&page=2", """
            {"total":3,"hidden":1,"hasNextPage":false,"people":[
              {"id":"p3","name":"Anni","isHidden":false,"birthDate":null,"thumbnailPath":"/x"},
              {"id":"p4","name":"Geheim","isHidden":true,"birthDate":null,"thumbnailPath":"/x"}]}
            """);

        var albums = await client.GetAlbumsAsync();
        Assert.Equal(["familie", "Urlaub"], albums.Select(a => a.AlbumName));
        Assert.Equal((string?)null, albums[0].AlbumThumbnailAssetId);
        Assert.Equal(12, albums[1].AssetCount);

        var people = await client.GetPeopleAsync();
        Assert.Equal(["Anni", "Paul"], people.Select(p => p.Name));
    }

    [Fact]
    public async Task FotosEinesAlbumsUeberDieSuche()
    {
        var (client, server) = Connect();
        server.Json("POST /api/search/metadata", $$$"""
            {"albums":{"total":0,"count":0,"items":[],"facets":[]},
             "assets":{"total":2,"count":2,"nextPage":"2","items":[
               {"id":"{{{Asset}}}","type":"IMAGE","originalFileName":"IMG_0001.HEIC","originalMimeType":"image/heic","isEdited":false},
               {"id":"v1","type":"VIDEO","originalFileName":"film.mp4","originalMimeType":"video/mp4"}],"facets":[]}}
            """);
        var page = await client.SearchAssetsAsync("a1", null, page: 1);
        Assert.Equal([Asset], page.Items.Select(a => a.Id));
        Assert.Equal(2, page.NextPage);

        using var body = JsonDocument.Parse(server.Requests.Single().Body!);
        Assert.Equal("a1", body.RootElement.GetProperty("albumIds")[0].GetString());
        Assert.Equal("IMAGE", body.RootElement.GetProperty("type").GetString());
        Assert.False(body.RootElement.TryGetProperty("personIds", out _));
    }

    [Fact]
    public async Task OriginalOderVorschauHerunterladen()
    {
        var (client, server) = Connect();
        server.Bytes($"GET /api/assets/{Asset}/original", Jpeg);
        server.Bytes($"GET /api/assets/{Asset}/thumbnail?size=preview", Jpeg);

        var asset = new ImmichAsset(Asset, "IMAGE", "IMG_0001.JPG", "image/jpeg", null);
        var original = await client.DownloadAsync(asset, original: true);
        Assert.Equal("IMG_0001.jpg", original.FileName);
        Assert.EndsWith("/original", server.Requests[^1].Request.RequestUri!.AbsolutePath);

        await client.DownloadAsync(asset, original: false);
        Assert.Equal("size=preview", server.Requests[^1].Request.RequestUri!.Query.TrimStart('?'));
    }

    [Fact]
    public async Task HeicWirdDurchDieVorschauErsetzt()
    {
        var (client, server) = Connect();
        server.Bytes($"GET /api/assets/{Asset}/original", Heic);
        server.Bytes($"GET /api/assets/{Asset}/thumbnail?size=preview", Jpeg);
        var log = new List<string>();
        var download = await client.DownloadAsync(new ImmichAsset(Asset, "IMAGE", "IMG_0001.HEIC", "image/heic", false), original: true, log.Add);
        Assert.Equal("IMG_0001.jpg", download.FileName);
        Assert.Equal(Jpeg, download.Data);
        Assert.Contains("große Vorschau", Assert.Single(log));
    }

    [Fact]
    public async Task BearbeiteteFotosInDerBearbeitetenFassung()
    {
        // Die Gesichter gelten dann für die bearbeitete Fassung
        var (client, server) = Connect();
        server.Bytes($"GET /api/assets/{Asset}/thumbnail?size=preview&edited=true", Jpeg);
        await client.DownloadAsync(new ImmichAsset(Asset, "IMAGE", "a.jpg", "image/jpeg", true), original: false);
        Assert.Contains("edited=true", server.Requests[0].Request.RequestUri!.Query);
    }

    const string FacesJson = """
        [{"id":"f1","imageWidth":1000,"imageHeight":500,"boundingBoxX1":700,"boundingBoxY1":100,"boundingBoxX2":800,"boundingBoxY2":200,
          "person":{"id":"p1","name":"Paul","isHidden":false,"birthDate":null,"thumbnailPath":"/x"},"sourceType":"machine-learning"},
         {"id":"f2","imageWidth":1000,"imageHeight":500,"boundingBoxX1":100,"boundingBoxY1":50,"boundingBoxX2":300,"boundingBoxY2":250,
          "person":{"id":"p3","name":"Änni","isHidden":false,"birthDate":null,"thumbnailPath":"/x"},"sourceType":"machine-learning"},
         {"id":"f3","imageWidth":1000,"imageHeight":500,"boundingBoxX1":450,"boundingBoxY1":100,"boundingBoxX2":550,"boundingBoxY2":200,
          "person":null,"sourceType":"machine-learning"}]
        """;

    [Fact]
    public async Task GesichterWerdenGelesen()
    {
        var (client, server) = Connect();
        server.Json($"GET /api/faces?id={Asset}", FacesJson);
        var faces = await client.GetFacesAsync(Asset);
        Assert.Equal(3, faces.Count);
        Assert.Equal((1000, 500, 700, 100, 800, 200), (faces[0].ImageWidth, faces[0].ImageHeight,
            faces[0].BoundingBoxX1, faces[0].BoundingBoxY1, faces[0].BoundingBoxX2, faces[0].BoundingBoxY2));
        Assert.Equal("Paul", faces[0].Person!.Name);
        Assert.Null(faces[2].Person);
    }

    static List<ImmichFace> Faces() => JsonSerializer.Deserialize<List<ImmichFace>>(FacesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    static void AssertRect(List<AreaPoint> outline, double x0, double y0, double x1, double y1)
    {
        Assert.Equal(4, outline.Count);
        Assert.Equal(x0, outline.Min(p => p.X), 2);
        Assert.Equal(y0, outline.Min(p => p.Y), 2);
        Assert.Equal(x1, outline.Max(p => p.X), 2);
        Assert.Equal(y1, outline.Max(p => p.Y), 2);
    }

    static void AssertRect(Rect actual, Rect expected)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Width, actual.Width, 6);
        Assert.Equal(expected.Height, actual.Height, 6);
    }

    [Fact]
    public void BoxenWerdenAufDasBildSkaliertUndSortiert()
    {
        // Bild 100 × 50 mm bei (10, 20): 1 Pixel von Immich = 0,1 mm, egal wie groß die heruntergeladene Datei ist
        var image = new ElementEntry { Kind = ElementKind.Image, X = 10, Y = 20, Width = 100, Height = 50 };
        var areas = ImmichFaces.ToAreas(Faces(), image, orientation: 1, pixelWidth: 2000, pixelHeight: 1000);
        Assert.Equal(["Änni", "", "Paul"], areas.Select(a => a.Name));
        AssertRect(areas[0].Outline, 20, 25, 40, 45);
        AssertRect(areas[1].Outline, 55, 30, 65, 40);
        AssertRect(areas[2].Outline, 80, 30, 90, 40);
        // Rahmen im gespeicherten Bild (für den Kopf-Ausschnitt)
        AssertRect(areas[0].SourcePixels, new Rect(200, 100, 400, 400));
    }

    [Fact]
    public void ZuschnittVerschiebtUndSchneidetAb()
    {
        // Sichtbar ist die rechte Hälfte (x 0,5 … 1) auf 50 × 50 mm: Änni fällt weg, die Mitte wird angeschnitten
        var image = new ElementEntry
        {
            Kind = ElementKind.Image, X = 0, Y = 0, Width = 50, Height = 50, CropX = 0.5, CropWidth = 0.5,
        };
        var areas = ImmichFaces.ToAreas(Faces(), image, orientation: 1);
        Assert.Equal(["", "Paul"], areas.Select(a => a.Name));
        AssertRect(areas[0].Outline, 0, 10, 5, 20);
        AssertRect(areas[1].Outline, 20, 10, 30, 20);
    }

    [Fact]
    public void DrehungDesElementsWirdBeachtet()
    {
        // 90° im Uhrzeigersinn um die Mitte (50, 25): Ännis Rahmen (10 … 30, 5 … 25) landet bei (50 … 70, −15 … 5)
        var image = new ElementEntry { Kind = ElementKind.Image, X = 0, Y = 0, Width = 100, Height = 50, Rotation = 90 };
        // Alle drei liegen jetzt übereinander (gleiches x der Mitte)
        var anni = ImmichFaces.ToAreas(Faces(), image, orientation: 1).Single(a => a.Name == "Änni");
        AssertRect(anni.Outline, 50, -15, 70, 5);
    }

    [Fact]
    public void ExifGedrehteFotos()
    {
        // Gespeichert hochkant 500 × 1000 Pixel mit Orientierung 6 (im Uhrzeigersinn drehen): Immich nennt die
        // Rahmen im gedrehten Bild 1000 × 500. Das Element (50 × 100 mm, ungedreht wie die Pixel) wird um 90° gedreht.
        var image = new ElementEntry { Kind = ElementKind.Image, X = 25, Y = -25, Width = 50, Height = 100, Rotation = ImmichFaces.RotationFor(6) };
        var areas = ImmichFaces.ToAreas(Faces(), image, orientation: 6, pixelWidth: 500, pixelHeight: 1000);
        // Auf der Seite liegt das Bild jetzt wie in Immich: 100 × 50 mm bei (0, 0)
        Assert.Equal(["Änni", "", "Paul"], areas.Select(a => a.Name));
        AssertRect(areas[0].Outline, 10, 5, 30, 25);
        AssertRect(areas[2].Outline, 70, 10, 80, 20);
        // Im gespeicherten (ungedrehten) Bild liegt Änni links unten: x = 0,1 … 0,5 der Breite, y = 0,7 … 0,9 der Höhe
        AssertRect(areas[0].SourcePixels, new Rect(50, 700, 200, 200));
    }

    [Theory]
    [InlineData(1, 0.2, 0.1, 0.2, 0.1)]
    [InlineData(2, 0.2, 0.1, 0.8, 0.1)]
    [InlineData(3, 0.2, 0.1, 0.8, 0.9)]
    [InlineData(4, 0.2, 0.1, 0.2, 0.9)]
    [InlineData(5, 0.2, 0.1, 0.1, 0.2)]
    [InlineData(6, 0.2, 0.1, 0.1, 0.8)]
    [InlineData(7, 0.2, 0.1, 0.9, 0.8)]
    [InlineData(8, 0.2, 0.1, 0.9, 0.2)]
    public void ExifOrientierungen(int orientation, double u, double v, double s, double t)
    {
        var p = ImmichFaces.ToStored(new Point(u, v), orientation);
        Assert.Equal(s, p.X, 6);
        Assert.Equal(t, p.Y, 6);
    }

    [Fact]
    public void ExifUndElementDrehungPassenZusammen()
    {
        // Ein Punkt des gezeigten Bilds muss nach Orientierung und Elementdrehung an derselben Stelle der Seite
        // landen wie im ungedrehten Bild gleicher Größe
        foreach (int orientation in new[] { 3, 6, 8 })
        {
            bool swap = orientation != 3;
            var stored = swap
                ? new ElementEntry { Kind = ElementKind.Image, X = 25, Y = -25, Width = 50, Height = 100, Rotation = ImmichFaces.RotationFor(orientation) }
                : new ElementEntry { Kind = ElementKind.Image, Width = 100, Height = 50, Rotation = ImmichFaces.RotationFor(orientation) };
            var shown = new ElementEntry { Kind = ElementKind.Image, Width = 100, Height = 50 };
            var expected = ImmichFaces.ToAreas(Faces(), shown, 1);
            var actual = ImmichFaces.ToAreas(Faces(), stored, orientation);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Name, actual[i].Name);
                AssertRect(actual[i].Outline, expected[i].Outline.Min(p => p.X), expected[i].Outline.Min(p => p.Y),
                    expected[i].Outline.Max(p => p.X), expected[i].Outline.Max(p => p.Y));
            }
        }
    }

    /// <summary>Kleinstes JPEG-Gerüst mit EXIF-Orientierung (TIFF in Motorola- bzw. Intel-Byte-Reihenfolge).</summary>
    static byte[] JpegWithOrientation(int orientation, bool little)
    {
        byte[] U16(int v) => little ? [(byte)v, (byte)(v >> 8)] : [(byte)(v >> 8), (byte)v];
        byte[] U32(int v) => little ? [(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24)] : [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
        var tiff = new List<byte>(little ? "II"u8.ToArray() : "MM"u8.ToArray());
        tiff.AddRange(U16(42));
        tiff.AddRange(U32(8));
        tiff.AddRange(U16(2));
        // Ein anderes Feld vorweg (ImageWidth), dann Orientation (SHORT, 1 Wert)
        tiff.AddRange([.. U16(0x0100), .. U16(3), .. U32(1), .. U16(640), 0, 0]);
        tiff.AddRange([.. U16(0x0112), .. U16(3), .. U32(1), .. U16(orientation), 0, 0]);
        tiff.AddRange(U32(0));
        var app1 = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        int length = app1.Length + 2;
        return [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x4A, 0x46, 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. app1, 0xFF, 0xDA, 0, 2, 0xFF, 0xD9];
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(8, true)]
    [InlineData(3, true)]
    public void ExifOrientierungWirdGelesen(int orientation, bool little) =>
        Assert.Equal(orientation, ImmichFaces.ReadOrientation(JpegWithOrientation(orientation, little)));

    [Fact]
    public void OhneExifGiltOrientierungEins()
    {
        Assert.Equal(1, ImmichFaces.ReadOrientation(Jpeg));
        Assert.Equal(1, ImmichFaces.ReadOrientation("\x89PNG"u8));
        Assert.Equal(1, ImmichFaces.ReadOrientation([]));
        // Abgeschnittene Datei
        Assert.Equal(1, ImmichFaces.ReadOrientation(JpegWithOrientation(6, false).AsSpan(0, 20)));
        Assert.Equal(".jpg", ImmichClient.ImageExtension(Jpeg));
        Assert.Null(ImmichClient.ImageExtension(Heic));
    }

    [Fact]
    public void NamenAusImmichBenennenFlaechenSkripteUndTexte()
    {
        var vm = new MainWindowViewModel();
        var page = vm.Project.SelectedPage ?? vm.Project.Pages[0];
        var image = new ElementEntry { Kind = ElementKind.Image, X = 10, Y = 20, Width = 100, Height = 50 };
        var areas = ImmichFaces.ToAreas(Faces(), image, 1);
        // So kommen die Namen vorausgefüllt in die Abfrage; der Unbekannte bleibt leer und wird „Person 1“
        var assigned = vm.AddPersonAreas(page, areas.Select(a => new List<List<AreaPoint>> { a.Outline }).ToList(), areas.Select(a => a.Name).ToList());
        Assert.Equal(["aenni", "person1", "paul"], assigned.Select(a => a.Name));
        Assert.Equal(["Änni", "Person 1", "Paul"], page.Areas.Where(a => a.IsScript).Select(a => a.Label));
        Assert.Equal("Paul", vm.Project.Speak.Single(s => s.Name == "paul").Text);
    }

    [Fact]
    public void KeyKommtNichtInsProjekt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tupfer-test-" + Guid.NewGuid());
        try
        {
            var store = new FileSettingsStore(Path.Combine(dir, "einstellungen.json"));
            var previous = Settings.Store;
            Settings.Store = store;
            try
            {
                new Settings { ImmichUrl = "https://fotos.example.org", ImmichApiKey = Key, ImmichOriginal = true }.Save();
                var loaded = Settings.Load();
                Assert.Equal(("https://fotos.example.org", Key, true), (loaded.ImmichUrl, loaded.ImmichApiKey, loaded.ImmichOriginal));
                if (!OperatingSystem.IsWindows())
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(dir, "einstellungen.json")));
            }
            finally
            {
                Settings.Store = previous;
            }

            // Im Projekt steht nur die Kennung des Fotos
            var vm = new MainWindowViewModel();
            var page = vm.Project.SelectedPage ?? vm.Project.Pages[0];
            page.Elements.Add(new ElementViewModel(vm.Project, ElementKind.Image) { ImagePath = "/x/foto.jpg", ImmichAsset = Asset, Width = 10, Height = 10 });
            var json = ProjectStore.ToJson(vm.Project.ToFile(dir));
            Assert.Contains(Asset, json);
            Assert.DoesNotContain(Key, json);
            var back = ProjectStore.FromJson(json);
            Assert.Equal(Asset, back.Pages[0].Elements.Single(e => e.Kind == ElementKind.Image).ImmichAsset);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DefekteEinstellungenGeltenAlsLeer()
    {
        var previous = Settings.Store;
        Settings.Store = new MemoryStore("{kaputt");
        try
        {
            Assert.Equal("", Settings.Load().ImmichUrl);
        }
        finally
        {
            Settings.Store = previous;
        }
    }

    sealed class MemoryStore(string? json) : ISettingsStore
    {
        public string? Read() => json;
        public void Write(string value) => json = value;
    }
}
