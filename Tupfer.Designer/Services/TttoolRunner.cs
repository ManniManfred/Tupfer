using System.Diagnostics;

namespace Tupfer.Designer.Services;

/// <summary>Ruft das externe tttool auf (z. B. "tttool assemble datei.yaml").</summary>
public static class TttoolRunner
{
    public static async Task<int> RunAsync(string tttoolPath, string workingDir, Action<string> output, params string[] args)
    {
        var psi = new ProcessStartInfo(string.IsNullOrWhiteSpace(tttoolPath) ? "tttool" : tttoolPath)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
