using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tupfer.Designer.Services;

/// <summary>Führt ein Programm aus und liefert Rückgabewert und Ausgaben; in Tests gefälscht.</summary>
public interface ICommandRunner
{
    /// <summary>null, wenn sich das Programm nicht starten lässt (nicht installiert).</summary>
    Task<CommandResult?> RunAsync(string program, params string[] args);
}

public record CommandResult(int ExitCode, string Output, string Error);

/// <summary>Startet echte Prozesse.</summary>
public class ProcessRunner : ICommandRunner
{
    public async Task<CommandResult?> RunAsync(string program, params string[] args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process == null)
                return null;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new CommandResult(process.ExitCode, await output, await error);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}

/// <summary>Ergebnis von <see cref="PenMounter.MountAsync"/>: neu eingehängte Stifte und Meldungen für den Benutzer.</summary>
public record MountResult(List<string> MountPoints, List<string> Messages);

/// <summary>
/// Linux: Ein angesteckter, aber nicht eingehängter Stift wird mit udisks eingehängt (ohne root, wie es der
/// Dateimanager auch tut). Erkannt wird er an der Bezeichnung „tiptoi“ seines Dateisystems (<c>lsblk</c>).
/// Fehlt <c>udisksctl</c> oder schlägt das Einhängen fehl, gibt es eine Meldung; sudo wird nie verwendet.
/// </summary>
public static class PenMounter
{
    /// <summary>Nur unter Linux (nicht Windows, macOS oder Browser); in Tests umschaltbar.</summary>
    public static bool IsSupported { get; set; } = OperatingSystem.IsLinux();

    public static ICommandRunner Runner { get; set; } = new ProcessRunner();

    /// <summary>Von Tupfer eingehängte Stifte; <see cref="PenContents.FindPens"/> nimmt sie mit, auch wenn der Ordner anders heißt.</summary>
    public static List<string> MountedByUs { get; } = [];

    /// <summary>Ein Blockgerät aus <c>lsblk -J</c> mit Unterpartitionen.</summary>
    record BlockDevice(string? Path, string? Label, string? Mountpoint, string? Type, List<BlockDevice>? Children);

    /// <summary>Nicht eingehängte Dateisysteme mit der Bezeichnung „tiptoi“ (Gerätepfade wie /dev/sdb).</summary>
    public static List<string> UnmountedPens(string lsblkJson)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var root = JsonSerializer.Deserialize<Dictionary<string, List<BlockDevice>>>(lsblkJson, options);
        var result = new List<string>();

        void Visit(IEnumerable<BlockDevice>? devices)
        {
            foreach (var d in devices ?? [])
            {
                if (d.Path != null && string.IsNullOrEmpty(d.Mountpoint) && d.Type is "disk" or "part"
                    && string.Equals(d.Label?.Trim(), "tiptoi", StringComparison.OrdinalIgnoreCase))
                    result.Add(d.Path);
                Visit(d.Children);
            }
        }

        Visit(root?.GetValueOrDefault("blockdevices"));
        return result;
    }

    /// <summary>
    /// Hängt alle angesteckten, nicht eingehängten Stifte ein. Ohne Stift (oder außerhalb von Linux) passiert
    /// nichts und es gibt keine Meldung.
    /// </summary>
    public static async Task<MountResult> MountAsync()
    {
        var result = new MountResult([], []);
        if (!IsSupported)
            return result;
        var lsblk = await Runner.RunAsync("lsblk", "-J", "-o", "PATH,LABEL,MOUNTPOINT,TYPE");
        if (lsblk is not { ExitCode: 0 })
            return result;
        List<string> devices;
        try
        {
            devices = UnmountedPens(lsblk.Output);
        }
        catch (JsonException)
        {
            return result;
        }

        foreach (var device in devices)
        {
            // --no-user-interaction: lieber eine Meldung als ein Passwortdialog, der im Hintergrund hängt
            var mount = await Runner.RunAsync("udisksctl", "mount", "-b", device, "--no-user-interaction");
            if (mount == null)
            {
                result.Messages.Add($"Der Stift ({device}) ist angesteckt, aber nicht eingehängt, und udisksctl fehlt " +
                                    "(Paket „udisks2“). Bitte den Stift im Dateimanager öffnen, dann wird er eingehängt.");
                break;
            }
            if (mount.ExitCode != 0 || MountPoint(mount.Output) is not { } point)
            {
                var reason = (mount.Error.Length > 0 ? mount.Error : mount.Output).Trim();
                result.Messages.Add($"Der Stift ({device}) ließ sich nicht einhängen{(reason.Length > 0 ? $": {reason}" : "")}. " +
                                    "Bitte den Stift im Dateimanager öffnen.");
                continue;
            }
            result.MountPoints.Add(point);
            lock (MountedByUs)
                if (!MountedByUs.Contains(point))
                    MountedByUs.Add(point);
            result.Messages.Add($"Stift eingehängt: {point}");
        }
        return result;
    }

    /// <summary>„Mounted /dev/sdb at /run/media/anna/tiptoi“ (ältere udisks mit Punkt am Ende) → Einhängepunkt.</summary>
    public static string? MountPoint(string udisksOutput) =>
        Regex.Match(udisksOutput, @"Mounted\s+\S+\s+at\s+(.+?)\.?\s*$", RegexOptions.Multiline) is { Success: true } m
            ? m.Groups[1].Value.Trim()
            : null;
}
