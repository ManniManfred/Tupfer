using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Model;
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
        var dir = NewFilesDir;
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
    /// Verzeichnis für Dateien, die der Designer selbst anlegt (Fotos aus Immich, Symbole): dateien/ im
    /// Projektverzeichnis (bzw. im entpackten *.tupfer), am Desktop ohne gespeichertes Projekt ein temporäres
    /// (beim Speichern wird ohnehin alles ins *.tupfer kopiert).
    /// </summary>
    string NewFilesDir => FilePath == null && _archiveDir == null && IsDesktop
        ? Path.Combine(Path.GetTempPath(), "tupfer-dateien")
        : Path.Combine(BaseDir, "dateien");

    /// <summary>
    /// Gesichter auf einem Foto aus Immich mit ihren Namen, umgerechnet auf das Bild, dazu je ein Kopf-Ausschnitt.
    /// null, wenn das nicht geht (Meldung ist schon ausgegeben); dann wird ohne Namen erkannt.
    /// </summary>
    async Task<List<(ImmichFaceArea Area, Avalonia.Media.Imaging.Bitmap? Face)>?> ImmichFacesAsync(ElementViewModel image, string assetId)
    {
        string fallback = CanDetectPersons ? " Die Personen werden ohne Namen aus Immich erkannt." : "";
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
        var named = areas.Count(a => a.Name.Length > 0);
        Log(named == areas.Count
            ? $"Immich kennt {areas.Count} Gesicht(er) mit Namen."
            : $"Immich kennt {areas.Count} Gesicht(er), {named} davon mit Namen.");
        return areas.Select(a => (a, PersonDetector.FaceImage(bitmap, a.SourcePixels, rotation))).ToList();
    }

    /// <summary>
    /// Verbindet die erkannten Personen (Umrisse) mit den Gesichtern aus Immich: Eine Person bekommt den Namen
    /// des Gesichts in ihrem Umriss (und dessen Kopf-Ausschnitt). Gesichter ohne erkannte Person werden als
    /// rechteckige Fläche angelegt, damit niemand mit Namen fehlt. Von links nach rechts sortiert.
    /// </summary>
    public List<PersonDetector.Person> CombinePersons(List<PersonDetector.Person> persons,
        List<(ImmichFaceArea Area, Avalonia.Media.Imaging.Bitmap? Face)> faces, out List<string> names)
    {
        var match = ImmichFaces.MatchFaces(persons.Select(p => p.Outlines).ToList(), faces.Select(f => f.Area).ToList());
        var result = new List<(double Order, PersonDetector.Person Person, string Name)>();
        static double CenterX(List<List<AreaPoint>> outlines)
        {
            var xs = outlines.SelectMany(o => o).Select(p => p.X).ToList();
            return xs.Count == 0 ? 0 : (xs.Min() + xs.Max()) / 2;
        }
        for (int p = 0; p < persons.Count; p++)
        {
            int f = Array.IndexOf(match, p);
            result.Add((CenterX(persons[p].Outlines),
                f >= 0 ? persons[p] with { Face = faces[f].Face ?? persons[p].Face } : persons[p],
                f >= 0 ? faces[f].Area.Name : ""));
        }
        int unmatched = 0;
        for (int f = 0; f < faces.Count; f++)
            if (match[f] < 0)
            {
                unmatched++;
                List<List<AreaPoint>> outline = [faces[f].Area.Outline];
                result.Add((CenterX(outline), new PersonDetector.Person(outline, faces[f].Face), faces[f].Area.Name));
            }
        if (unmatched > 0)
            Log(unmatched == 1
                ? "Zu 1 Gesicht aus Immich wurde kein Umriss gefunden; es bekommt eine rechteckige Fläche."
                : $"Zu {unmatched} Gesichtern aus Immich wurde kein Umriss gefunden; sie bekommen rechteckige Flächen.");
        var sorted = result.OrderBy(r => r.Order).ToList();
        names = sorted.Select(r => r.Name).ToList();
        return sorted.Select(r => r.Person).ToList();
    }
}
