using Tupfer.Designer.Model;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Personen erneut erkennen (alte Flächen ersetzen) und unbenutzte Skripte und Texte entfernen.</summary>
public class CleanupTests
{
    static List<List<AreaPoint>> Square(double x) => [[new(x, 10), new(x + 10, 10), new(x + 10, 20), new(x, 20)]];

    static (MainWindowViewModel Vm, PageViewModel Page, ElementViewModel Image) Create()
    {
        var vm = new MainWindowViewModel();
        var page = vm.Project.SelectedPage ?? vm.Project.Pages[0];
        var image = new ElementViewModel(vm.Project, ElementKind.Image) { X = 0, Y = 0, Width = 100, Height = 50 };
        page.Elements.Add(image);
        return (vm, page, image);
    }

    [Fact]
    public void ErneutErkennenErsetztFlaechenUndBehaeltGleicheNamen()
    {
        var (vm, page, image) = Create();
        var project = vm.Project;
        vm.AddPersonAreas(page, [Square(0), Square(20), Square(40)], ["Alex", "Nicola", "Oma"]);
        var alexCode = project.Scripts.Single(s => s.Name == "alex").Code;
        var alexSpeak = project.Speak.Single(s => s.Name == "alex");
        alexSpeak.Text = "Das ist Alex";
        project.Speak.Single(s => s.Name == "oma").Recording = "/tmp/oma.wav";
        // Eine Fläche außerhalb des Bilds gehört nicht dazu
        vm.AddPersonAreas(page, [[[new(150, 100), new(160, 100), new(160, 110), new(150, 110)]]], ["Hund"]);

        var existing = MainWindowViewModel.PersonAreasOn(page, image);
        Assert.Equal(["Alex", "Nicola", "Oma"], existing.Select(a => a.Label));

        vm.AddPersonAreas(page, [Square(0), Square(20)], ["Alex", "Julia"], existing);

        // Keine Nummern, Alex behält Text, Skript und Code
        Assert.Equal(["Hund", "Alex", "Julia"], page.Areas.Select(a => a.Label));
        Assert.Equal(["alex", "hund", "julia"], page.Areas.Select(a => a.Script).Order());
        Assert.Same(alexSpeak, page.Areas.Single(a => a.Label == "Alex").SpeakEntry);
        Assert.Equal("Das ist Alex", alexSpeak.Text);
        Assert.Equal(alexCode, project.Scripts.Single(s => s.Name == "alex").Code);
        // Nicola wird nicht mehr gebraucht; Oma auch nicht, aber ihre Aufnahme bleibt (der Text bleibt)
        Assert.DoesNotContain(project.Scripts, s => s.Name is "nicola" or "oma");
        Assert.DoesNotContain(project.Speak, s => s.Name == "nicola");
        Assert.Contains(project.Speak, s => s.Name == "oma");
        Assert.DoesNotContain(project.Speak, s => s.Name.EndsWith('2'));
    }

    [Fact]
    public void OhneErsetzenKommenDieNeuenDazu()
    {
        var (vm, page, _) = Create();
        vm.AddPersonAreas(page, [Square(0)], ["Alex"]);
        vm.AddPersonAreas(page, [Square(0)], ["Alex"]);
        Assert.Equal(["alex", "alex2"], page.Areas.Select(a => a.Script));
    }

    [Fact]
    public void FindetNurWirklichUnbenutztes()
    {
        var (vm, page, _) = Create();
        var project = vm.Project;
        project.Scripts.Clear();
        project.Speak.Clear();
        vm.AddPersonAreas(page, [Square(0)], ["Alex"]);
        project.Speak.Add(new SpeakViewModel { Name = "willkommen", Text = "Hallo" });
        project.Welcome = "willkommen";
        project.Speak.Add(new SpeakViewModel { Name = "alt", Text = "weg" });
        project.Speak.Add(new SpeakViewModel { Name = "gerufen", Text = "bleibt" });
        project.AddScript("start", "P(gerufen)");
        project.Init = "J(start)";
        project.AddScript("hilfe", "P(alt)"); // ruft nur Unbenutztes auf und wird selbst nicht gebraucht
        project.AddScript("unterprogramm", "P(gerufen)");
        project.AddScript("rufer", "J(unterprogramm)"); // rufer ist unbenutzt, also auch unterprogramm
        project.AddScript("alex2", "P(alex)");

        var (scripts, speak) = project.FindUnused();
        Assert.Equal(["hilfe", "unterprogramm", "rufer", "alex2"], scripts.Select(s => s.Name));
        Assert.Equal(["alt"], speak.Select(s => s.Name));

        project.Remove(scripts, speak);
        Assert.Equal(["alex", "start"], project.Scripts.Select(s => s.Name));
        Assert.Equal(["alex", "willkommen", "gerufen"], project.Speak.Select(s => s.Name));
        Assert.Empty(project.FindUnused().Scripts);
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
    public async Task BefehlFragtVorDemEntfernen(bool answer)
    {
        var (vm, page, _) = Create();
        var dialogs = new Confirm(answer);
        vm.Dialogs = dialogs;
        vm.AddPersonAreas(page, [Square(0)], ["Alex"]);
        page.Areas.Clear();

        await vm.RemoveUnusedCommand.ExecuteAsync(null);

        Assert.Contains("alex", dialogs.Message);
        Assert.Equal(!answer, vm.Project.Scripts.Any(s => s.Name == "alex"));
        Assert.Equal(!answer, vm.Project.Speak.Any(s => s.Name == "alex"));
    }
}
