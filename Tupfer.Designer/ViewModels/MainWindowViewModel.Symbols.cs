using Avalonia;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

// Symbole für Start (Anmelden), Wiederholen und Stopp einfügen
public partial class MainWindowViewModel
{
    /// <summary>
    /// Fügt das Symbol für einen Sondercode ein (Parameter: „Product“, „Replay“ oder „Stop“): die Grafik als Bild
    /// und darüber eine runde OID-Fläche mit dem Sondercode. Beide sind danach gewählt und lassen sich zusammen
    /// verschieben. Die drei Symbole landen nebeneinander am unteren Rand der Seite.
    /// </summary>
    [RelayCommand]
    void InsertSymbol(string target) => InsertSymbolOn(Project.SelectedPage, Enum.Parse<AreaTarget>(target));

    public (ElementViewModel Image, AreaViewModel Area)? InsertSymbolOn(PageViewModel? page, AreaTarget target)
    {
        if (page == null)
            return null;
        var symbol = Symbols.For(target);
        string path;
        try
        {
            path = Symbols.WriteFile(symbol, NewFilesDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notice($"Das Symbol „{symbol.Label}“ ließ sich nicht anlegen: {e.Message}");
            return null;
        }
        const double size = Symbols.SizeMm, margin = 10, gap = 5;
        int index = Symbols.All.ToList().IndexOf(symbol);
        var center = new Point(margin + size / 2 + index * (size + gap), page.HeightMm - margin - size / 2);
        // Bild und Fläche zusammen: ein Schritt für Rückgängig
        ElementViewModel? image = null;
        AreaViewModel? area = null;
        Project.History.Change(() =>
        {
            image = page.AddImage(path, center);
            if (image == null)
                return;
            // Immer genau so groß wie vorgesehen (die SVG-Größe wird beim Laden auf Pixel gerundet)
            image.Width = image.Height = size;
            image.X = Math.Round(center.X - size / 2, 2);
            image.Y = Math.Round(center.Y - size / 2, 2);
            area = new AreaViewModel(Project)
            {
                Label = symbol.Label, Shape = AreaShape.Ellipse, Target = target,
                X = image.X, Y = image.Y, Width = size, Height = size,
            };
            page.Areas.Add(area);
        });
        if (image == null || area == null)
        {
            Notice($"Das Symbol „{symbol.Label}“ ließ sich nicht laden.");
            return null;
        }
        AreasOnly = false;
        page.Select([image, area]);
        Log(target == AreaTarget.Product
            ? $"Symbol „{symbol.Label}“ eingefügt: meldet das Produkt {Project.ProductId} am Stift an."
            : $"Symbol „{symbol.Label}“ eingefügt.");
        return (image, area);
    }
}
