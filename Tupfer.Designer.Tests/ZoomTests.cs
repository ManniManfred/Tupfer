using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Zoom: Grenzen und Eingabe in Prozent.</summary>
public class ZoomTests
{
    [Fact]
    public void ZoomTextUndGrenzen()
    {
        var vm = new MainWindowViewModel();
        Assert.Equal("100 %", vm.ZoomText);

        vm.ZoomText = "250";
        Assert.Equal(2.5, vm.Zoom, 6);
        vm.ZoomText = " 37,5 % ";
        Assert.Equal(0.375, vm.Zoom, 6);
        Assert.Equal("38 %", vm.ZoomText);

        vm.ZoomText = "unsinn";     // verworfen
        Assert.Equal(0.375, vm.Zoom, 6);

        vm.ZoomText = "5000";       // bis 1000 %
        Assert.Equal(MainWindowViewModel.MaxZoom, vm.Zoom);
        vm.ZoomInCommand.Execute(null);
        Assert.Equal(10, vm.Zoom);
        vm.ZoomText = "1";
        Assert.Equal(MainWindowViewModel.MinZoom, vm.Zoom);
    }
}
