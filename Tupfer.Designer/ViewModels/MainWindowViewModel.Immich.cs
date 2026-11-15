using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

// Fotos aus Immich einfügen und deren Gesichter bei „Personen erkennen“ verwenden
public partial class MainWindowViewModel
{
    /// <summary>Offene Auswahl von Fotos aus Immich; null = keine.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowsingImmich))]
    ImmichBrowserViewModel? _immichBrowser;

    public bool IsBrowsingImmich => ImmichBrowser != null;

    /// <summary>Öffnet die Auswahl von Fotos aus Immich (beim ersten Mal mit der Eingabe von Adresse und API-Key).</summary>
    [RelayCommand]
    Task InsertImmichImage() => Guard("Bild aus Immich", async () =>
    {
        if (Project.SelectedPage is not { } page)
            return;
        var browser = new ImmichBrowserViewModel(Settings.Load(),
            (assets, original) => InsertImmichAssetsAsync(page, assets, original),
            () => ImmichBrowser = null);
        ImmichBrowser = browser;
        await browser.StartAsync();
    });

    /// <summary>
    /// Lädt die Fotos herunter, legt sie zu den Dateien des Projekts (beim Speichern kommen sie ins *.tupfer)
    /// und fügt sie auf der Seite ein. Laut EXIF gedreht gespeicherte Fotos werden als Element gedreht.
    /// </summary>
    public async Task InsertImmichAssetsAsync(PageViewModel page, IReadOnlyList<ImmichAsset> assets, bool original)
    {
        var settings = Settings.Load();
        using var client = new ImmichClient(settings.ImmichUrl, settings.ImmichApiKey, ImmichBrowserViewModel.Handler);
        var dir = ImmichDir;
        var center = new Point(page.WidthMm / 2, page.HeightMm / 2);
        var inserted = new List<ElementViewModel>();
        for (int i = 0; i < assets.Count; i++)
        {
            var download = await client.DownloadAsync(assets[i], original, Log);
            var path = WorkFiles.UniquePath(dir, download.FileName);
            await File.WriteAllBytesAsync(path, download.Data);
            // Mehrere Fotos leicht versetzt, damit sie nicht genau übereinander liegen
            var element = page.AddImage(path, new Point(center.X + i * 5, center.Y + i * 5));
            if (element == null)
            {
                Notice($"„{download.FileName}“ aus Immich ließ sich nicht laden.");
                continue;
            }
            element.ImmichAsset = assets[i].Id;
            var rotation = ImmichFaces.RotationFor(ImmichFaces.ReadOrientation(download.Data));
            if (rotation != 0)
                element.Rotation = rotation;
            inserted.Add(element);
            Log($"Aus Immich eingefügt: {download.FileName}");
        }
        if (inserted.Count > 0)
        {
            AreasOnly = false;
            page.SelectedElement = inserted[^1];
        }
    }

    /// <summary>
    /// Verzeichnis für Fotos aus Immich: dateien/ im Projektverzeichnis (bzw. im entpackten *.tupfer), am
    /// Desktop ohne gespeichertes Projekt ein temporäres (beim Speichern wird ohnehin alles ins *.tupfer kopiert).
    /// </summary>
    string ImmichDir => FilePath == null && _archiveDir == null && IsDesktop
        ? Path.Combine(Path.GetTempPath(), "tupfer-immich")
        : Path.Combine(BaseDir, "dateien");

    /// <summary>
    /// Personen auf einem Foto aus Immich: die Gesichter mit ihren Namen von dort, umgerechnet auf das Bild.
    /// null, wenn das nicht geht (Meldung ist schon ausgegeben); dann wird ggf. lokal erkannt.
    /// </summary>
    async Task<(List<PersonDetector.Person> Persons, List<string> Names)?> ImmichPersonsAsync(ElementViewModel image, string assetId)
    {
        string fallback = CanDetectPersons ? " Es wird stattdessen auf dem Bild selbst gesucht." : "";
        if (image.Image is not { } bitmap)
            throw new InvalidOperationException($"Das Bild „{image.FileName}“ fehlt.");
        List<ImmichFace> faces;
        try
        {
            var settings = Settings.Load();
            using var client = new ImmichClient(settings.ImmichUrl, settings.ImmichApiKey, ImmichBrowserViewModel.Handler);
            Log($"Hole die Gesichter zu „{image.FileName}“ aus Immich …");
            faces = await client.GetFacesAsync(assetId);
        }
        catch (ImmichException e)
        {
            Notice($"Immich: {e.Message}{fallback}");
            return null;
        }
        if (faces.Count == 0)
        {
            Notice($"Immich kennt auf diesem Foto keine Gesichter (keine Personen darauf, oder die Gesichtserkennung von Immich ist noch nicht gelaufen).{fallback}");
            return null;
        }

        int orientation = 1;
        try
        {
            orientation = ImmichFaces.ReadOrientation(await File.ReadAllBytesAsync(image.ImagePath));
        }
        catch (IOException)
        {
            // Ohne Datei gilt das Bild als richtig gedreht
        }
        var areas = ImmichFaces.ToAreas(faces, image.ToEntry(null), orientation, bitmap.Size.Width, bitmap.Size.Height);
        if (areas.Count == 0)
        {
            Notice($"Die Gesichter, die Immich kennt, liegen alle außerhalb des sichtbaren Ausschnitts.{fallback}");
            return null;
        }
        var rotation = ImmichFaces.RotationFor(orientation);
        var persons = areas.Select(a => new PersonDetector.Person([a.Outline], PersonDetector.FaceImage(bitmap, a.SourcePixels, rotation))).ToList();
        var named = areas.Count(a => a.Name.Length > 0);
        Log(named == areas.Count
            ? $"Immich kennt {areas.Count} Gesicht(er) mit Namen."
            : $"Immich kennt {areas.Count} Gesicht(er), {named} davon mit Namen.");
        return (persons, areas.Select(a => a.Name).ToList());
    }
}
