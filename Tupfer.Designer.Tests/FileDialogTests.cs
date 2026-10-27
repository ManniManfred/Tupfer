using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Werkzeugleiste und Dateiauswahl, die nicht zurückkehrt (Browser: Dialog abgebrochen).</summary>
public class FileDialogTests
{
    class HangingDialogs : IFileDialogs
    {
        public readonly List<TaskCompletionSource<string?>> Open = new();

        public Task<string?> OpenAsync(string title, string filterName, params string[] patterns)
        {
            var dialog = new TaskCompletionSource<string?>();
            Open.Add(dialog);
            return dialog.Task;
        }

        public Task<string?> SaveAsync(string title, string suggestedName, string filterName, string extension) =>
            Task.FromResult<string?>(null);

        public Task<string> PrintAsync(string pdfPath) => Task.FromResult("");

        public Task<string?> SaveToPenAsync(string fileName, Func<Task<string?>> create) => Task.FromResult<string?>(null);
    }

    [Fact]
    public void AbgebrochenerDialogSperrtDenBefehlNicht()
    {
        var dialogs = new HangingDialogs();
        var vm = new MainWindowViewModel { Dialogs = dialogs };
        try
        {
            vm.InsertImageCommand.Execute(null);
            Assert.True(vm.InsertImageCommand.CanExecute(null));
            vm.InsertImageCommand.Execute(null);
            Assert.Equal(2, dialogs.Open.Count);

            vm.OpenProjectCommand.Execute(null);
            Assert.True(vm.OpenProjectCommand.CanExecute(null));
            vm.ImportYamlCommand.Execute(null);
            Assert.True(vm.ImportYamlCommand.CanExecute(null));
        }
        finally
        {
            // Sonst wartet xUnit am Ende auf die offenen Befehle
            dialogs.Open.ForEach(d => d.SetResult(null));
        }
    }

    [Fact]
    public void FarbknopfUndNurOidFlaechenSchliessenSichAus()
    {
        var vm = new MainWindowViewModel { AreaTool = null, AreasOnly = true };
        vm.DrawColorShapes = true;
        Assert.False(vm.AreasOnly);
        Assert.Equal(Model.AreaShape.Rectangle, vm.AreaTool);
        vm.AreasOnly = true;
        Assert.False(vm.DrawColorShapes);
    }
}
