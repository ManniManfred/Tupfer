using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Statische Einstellungen für Stift-Suche und Einhängen: Diese Tests laufen nacheinander.</summary>
[CollectionDefinition("Stift", DisableParallelization = true)]
public class StiftCollection;

/// <summary>Gefälschte Programme: Antworten je Programmname, null = nicht installiert.</summary>
class FakeRunner : ICommandRunner
{
    public readonly Dictionary<string, Func<string[], CommandResult?>> Programs = new();
    public readonly List<string> Calls = [];

    public Task<CommandResult?> RunAsync(string program, params string[] args)
    {
        Calls.Add(program + " " + string.Join(" ", args));
        return Task.FromResult(Programs.TryGetValue(program, out var run) ? run(args) : null);
    }
}

/// <summary>Linux: angesteckten, aber nicht eingehängten Stift mit udisksctl einhängen.</summary>
[Collection("Stift")]
public class PenMounterTests : IDisposable
{
    /// <summary>Stift als ganzes Gerät (ohne Partitionstabelle), ein eingehängter Stift und andere Laufwerke.</summary>
    const string Lsblk = """
        {
           "blockdevices": [
              {"path": "/dev/loop0", "label": null, "mountpoint": "/snap/bare/5", "type": "loop"},
              {"path": "/dev/sda", "label": null, "mountpoint": null, "type": "disk",
               "children": [
                  {"path": "/dev/sda1", "label": "EFI", "mountpoint": "/boot/efi", "type": "part"},
                  {"path": "/dev/sda2", "label": "tiptoi", "mountpoint": null, "type": "part"}
               ]},
              {"path": "/dev/sdb", "label": "tiptoi", "mountpoint": null, "type": "disk"},
              {"path": "/dev/sdc", "label": "TIPTOI", "mountpoint": "/run/media/anna/TIPTOI", "type": "disk"},
              {"path": "/dev/sdd", "label": "USB-STICK", "mountpoint": null, "type": "disk"}
           ]
        }
        """;

    readonly string _mount = Directory.CreateTempSubdirectory("tupfer-eingehaengt").FullName;
    readonly FakeRunner _runner = new();

    public PenMounterTests()
    {
        PenMounter.IsSupported = true;
        PenMounter.Runner = _runner;
        _runner.Programs["lsblk"] = _ => new CommandResult(0, Lsblk, "");
    }

    public void Dispose()
    {
        PenMounter.IsSupported = false;
        PenMounter.Runner = new ProcessRunner();
        lock (PenMounter.MountedByUs)
            PenMounter.MountedByUs.Clear();
        PenDrives.Find = () => [];
        Directory.Delete(_mount, true);
    }

    [Fact]
    public void NichtEingehaengteStifteWerdenErkannt()
    {
        Assert.Equal(["/dev/sda2", "/dev/sdb"], PenMounter.UnmountedPens(Lsblk));
    }

    [Theory]
    [InlineData("Mounted /dev/sdb at /run/media/anna/tiptoi\n", "/run/media/anna/tiptoi")]
    [InlineData("Mounted /dev/sdb at /media/anna/tiptoi.\n", "/media/anna/tiptoi")]
    [InlineData("Error mounting /dev/sdb", null)]
    public void EinhaengepunktAusDerAusgabe(string output, string? expected) =>
        Assert.Equal(expected, PenMounter.MountPoint(output));

    [Fact]
    public async Task StiftWirdEingehaengtUndGefunden()
    {
        // Der Ordner heißt nicht „tiptoi“ (udisks hängt bei Namensgleichheit z. B. „tiptoi1“ ein)
        _runner.Programs["udisksctl"] = args => new CommandResult(0, $"Mounted {args[2]} at {_mount}\n", "");

        var result = await PenMounter.MountAsync();

        Assert.Equal([_mount, _mount], result.MountPoints);
        Assert.Contains($"Stift eingehängt: {_mount}", result.Messages);
        Assert.Contains("udisksctl mount -b /dev/sdb --no-user-interaction", _runner.Calls);
        Assert.DoesNotContain(_runner.Calls, c => c.Contains("sudo"));
        Assert.Single(PenContents.FindPens(), p => p.TrimEnd('/') == _mount.TrimEnd('/'));
    }

    [Fact]
    public async Task OhneUdisksctlVerstaendlicheMeldung()
    {
        var result = await PenMounter.MountAsync();
        Assert.Empty(result.MountPoints);
        var message = Assert.Single(result.Messages);
        Assert.Contains("udisksctl fehlt", message);
        Assert.Contains("Dateimanager", message);
    }

    [Fact]
    public async Task FehlschlagWirdGemeldet()
    {
        _runner.Programs["udisksctl"] = _ => new CommandResult(1, "", "Error mounting /dev/sdb: GDBus.Error: Not authorized\n");
        var result = await PenMounter.MountAsync();
        Assert.Empty(result.MountPoints);
        Assert.Contains(result.Messages, m => m.Contains("ließ sich nicht einhängen: Error mounting /dev/sdb: GDBus.Error: Not authorized"));
    }

    [Fact]
    public async Task OhneStiftOderAusserhalbVonLinuxNichts()
    {
        _runner.Programs["lsblk"] = _ => new CommandResult(0, """{"blockdevices": []}""", "");
        Assert.Empty((await PenMounter.MountAsync()).Messages);

        PenMounter.IsSupported = false;
        _runner.Calls.Clear();
        Assert.Empty((await PenMounter.MountAsync()).Messages);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task StiftManagerHaengtEinUndZeigtFehler()
    {
        _runner.Programs["udisksctl"] = _ => new CommandResult(1, "", "Not authorized");
        PenManagerViewModel.Handler = new FakeRavensburger();
        RavensburgerCatalog.CachePath = Path.Combine(_mount, "katalog.json");
        var log = new List<string>();
        var vm = new PenManagerViewModel(null, () => { }, log.Add);

        await vm.StartAsync();

        Assert.Contains("ließ sich nicht einhängen", vm.Status);
        Assert.Contains(log, l => l.Contains("ließ sich nicht einhängen"));
        PenManagerViewModel.Handler = null;
    }
}
