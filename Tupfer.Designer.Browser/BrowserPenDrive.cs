using System.Text.Json;
using Tupfer.Designer.Services;

namespace Tupfer.Designer;

/// <summary>
/// Stift im Browser: ein Ordner (das Laufwerk des Stifts), den der Benutzer über die File System Access API
/// freigegeben hat (nur Chrome und Edge). Den freien Speicher verrät der Browser nicht.
/// </summary>
class BrowserPenDrive(int id, string name) : IPenDrive
{
    public string Name { get; } = name;

    /// <summary>Lässt den Ordner wählen; null bei Abbruch.</summary>
    public static async Task<IPenDrive?> PickAsync()
    {
        var picked = await BrowserInterop.PickPenFolderAsync();
        int bar = picked.IndexOf('|');
        return bar < 0 ? null : new BrowserPenDrive(int.Parse(picked[..bar]), picked[(bar + 1)..]);
    }

    record FileInfoJson(string Path, long Size, string Head);

    public async Task<List<PenProduct>> ReadProductsAsync()
    {
        var files = JsonSerializer.Deserialize<List<FileInfoJson>>(await BrowserInterop.PenListAsync(id),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        return files
            .Select(f => PenContents.ParseHeader(Convert.FromBase64String(f.Head), f.Path, f.Size))
            .OrderBy(p => p.ProductId ?? uint.MaxValue)
            .ThenBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public PenSpace? GetSpace() => null;

    public Task DeleteAsync(string relativePath) => BrowserInterop.PenDeleteAsync(id, relativePath.Replace('\\', '/'));

    public async Task WriteAsync(string fileName, string sourcePath)
    {
        BrowserInterop.PenSetPending(await File.ReadAllBytesAsync(sourcePath));
        await BrowserInterop.PenWriteAsync(id, fileName);
    }
}
