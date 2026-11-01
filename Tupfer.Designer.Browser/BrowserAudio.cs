using Tupfer.Designer.Services;

namespace Tupfer.Designer;

/// <summary>Wiedergabe und Aufnahme im Browser (wwwroot/tupfer.js).</summary>
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
        // Ältere Safari-Versionen spielen kein Ogg Vorbis: dann als WAV
        if (mime == "audio/ogg" && !BrowserInterop.CanPlayAudio("audio/ogg; codecs=vorbis"))
        {
            data = Audio.OggToWav(data);
            mime = "audio/wav";
        }
        BrowserInterop.PlayAudio(data, mime);
        return BrowserInterop.WaitAudioAsync();
    }

    public void StopPlayback() => BrowserInterop.StopAudio();

    public Task StartRecordingAsync() => BrowserInterop.StartRecordingAsync();

    public async Task<byte[]> StopRecordingAsync() =>
        Convert.FromBase64String(await BrowserInterop.StopRecordingAsync(Audio.SampleRate));
}
