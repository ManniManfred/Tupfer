using CommunityToolkit.Mvvm.Input;

namespace Tupfer.Designer.ViewModels;

// Ebenen-Liste im Tab „Seiten“: OID-Flächen sowie Bilder, Texte und Farbflächen
public partial class MainWindowViewModel
{
    /// <summary>Wählt eine OID-Fläche aus der Ebenen-Liste (auch wenn sie verdeckt ist).</summary>
    [RelayCommand]
    void SelectArea(AreaViewModel area)
    {
        if (Project.SelectedPage is { } page)
            page.SelectedArea = area;
    }

    /// <summary>Nimmt einen Eintrag der Ebenen-Liste in die Auswahl auf oder entfernt ihn (Strg-Klick).</summary>
    public void ToggleLayerSelection(object item)
    {
        if (Project.SelectedPage is not { } page) return;
        if (item is ElementViewModel)
            AreasOnly = false;
        page.ToggleSelection(item);
    }
}
