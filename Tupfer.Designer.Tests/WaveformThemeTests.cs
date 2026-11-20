using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Tupfer.Designer.Views;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Die Wellenform nimmt ihre Farben aus dem Theme und zeichnet bei Hell/Dunkel-Wechsel neu.</summary>
public class WaveformThemeTests
{
    static readonly Color Light = Color.FromRgb(250, 250, 250), Dark = Color.FromRgb(20, 20, 30);

    [Fact]
    public void HintergrundFolgtDemTheme()
    {
        var (before, after) = HeadlessSkia.Run(() =>
        {
            var themes = new ResourceDictionary();
            themes.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary { ["WaveformBackground"] = new SolidColorBrush(Light) };
            themes.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { ["WaveformBackground"] = new SolidColorBrush(Dark) };
            var window = new Window
            {
                Width = 100, Height = 50, RequestedThemeVariant = ThemeVariant.Light, Resources = themes,
                Content = new WaveformView(),
            };
            window.Show();
            var first = Corner(window);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            var second = Corner(window);
            window.Close();
            return (first, second);
        });
        Assert.Equal(Light, before);
        Assert.Equal(Dark, after);
    }

    static Color Corner(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var px = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + buffer.RowBytes * 5 + 4 * 5, px, 0, 4);
        return frame.Format == Avalonia.Platform.PixelFormat.Bgra8888
            ? Color.FromRgb(px[2], px[1], px[0])
            : Color.FromRgb(px[0], px[1], px[2]);
    }
}
