using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TipToy.Designer.Model;

namespace TipToy.Designer.ViewModels;

public partial class PageViewModel : ObservableObject
{
    public PageViewModel(ProjectViewModel project)
    {
        Project = project;
    }

    public ProjectViewModel Project { get; }

    [ObservableProperty] string _name = "";
    [ObservableProperty] double _widthMm = 210;
    [ObservableProperty] double _heightMm = 297;

    /// <summary>Absoluter Pfad des Hintergrundbilds.</summary>
    [ObservableProperty] string _backgroundPath = "";

    [ObservableProperty] Bitmap? _background;

    [ObservableProperty] AreaViewModel? _selectedArea;

    public ObservableCollection<AreaViewModel> Areas { get; } = new();

    partial void OnBackgroundPathChanged(string value)
    {
        var old = Background;
        Background = null;
        old?.Dispose();
        if (string.IsNullOrEmpty(value))
            return;
        try
        {
            Background = new Bitmap(value);
        }
        catch (Exception ex)
        {
            Project.Log($"Hintergrundbild '{value}' konnte nicht geladen werden: {ex.Message}");
        }
    }

    /// <summary>Passt die Seitenhöhe an das Seitenverhältnis des Hintergrundbilds an.</summary>
    public void FitHeightToBackground()
    {
        if (Background is { } bmp && bmp.PixelSize.Width > 0)
            HeightMm = Math.Round(WidthMm * bmp.PixelSize.Height / bmp.PixelSize.Width, 1);
    }

    public PageEntry ToEntry(string baseDir) => new()
    {
        Name = Name,
        WidthMm = WidthMm,
        HeightMm = HeightMm,
        BackgroundImage = string.IsNullOrEmpty(BackgroundPath) ? "" : Path.GetRelativePath(baseDir, BackgroundPath),
        Areas = Areas.Select(a => a.ToEntry()).ToList(),
    };

    public static PageViewModel FromEntry(ProjectViewModel project, PageEntry e, string baseDir)
    {
        var page = new PageViewModel(project)
        {
            Name = e.Name,
            WidthMm = e.WidthMm,
            HeightMm = e.HeightMm,
            BackgroundPath = string.IsNullOrEmpty(e.BackgroundImage) ? "" : Path.GetFullPath(e.BackgroundImage, baseDir),
        };
        foreach (var a in e.Areas)
            page.Areas.Add(AreaViewModel.FromEntry(project, a));
        return page;
    }
}
