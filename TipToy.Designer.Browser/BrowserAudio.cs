using TipToy.Designer.Services;

namespace TipToy.Designer;

/// <summary>Wiedergabe und Aufnahme im Browser (wwwroot/tiptoy.js).</summary>
class BrowserAudio : IAudioDevice
{
    public Task PlayAsync(byte[] data, string extension)
    {
        var mime = extension.ToLowerInvariant() switch
        {
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            _ => "audio/wav",
        };
        BrowserInterop.PlayAudio(data, mime);
        return Task.CompletedTask;
    }

    public Task StartRecordingAsync() => BrowserInterop.StartRecordingAsync();

    public async Task<byte[]> StopRecordingAsync() =>
        Convert.FromBase64String(await BrowserInterop.StopRecordingAsync(Audio.SampleRate));
}
