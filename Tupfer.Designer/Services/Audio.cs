using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Encoding = System.Text.Encoding;
using NVorbis;
using OggVorbisEncoder;

namespace Tupfer.Designer.Services;

/// <summary>Abspielen und Aufnehmen von Audio.</summary>
public interface IAudioDevice
{
    /// <summary>
    /// Spielt Audiodaten ab (Dateiendung wie ".wav"); eine laufende Wiedergabe wird beendet.
    /// Ist fertig, wenn die Wiedergabe zu Ende ist oder abgebrochen wurde.
    /// </summary>
    Task PlayAsync(byte[] data, string extension);

    /// <summary>Beendet eine laufende Wiedergabe.</summary>
    void StopPlayback();

    /// <summary>Startet eine Aufnahme vom Mikrofon.</summary>
    Task StartRecordingAsync();

    /// <summary>Beendet die Aufnahme und liefert sie als WAV (mono, 16 Bit).</summary>
    Task<byte[]> StopRecordingAsync();
}

public static class Audio
{
    /// <summary>Wird im Browser ersetzt.</summary>
    public static IAudioDevice Device { get; set; } = new DesktopAudio();

    public const int SampleRate = 22050;

    /// <summary>Dateiendung anhand des Dateianfangs (OGG, FLAC, MP3 oder sonst WAV).</summary>
    public static string ExtensionOf(byte[] data)
    {
        if (data.Length < 4)
            return ".wav";
        var magic = Encoding.ASCII.GetString(data, 0, 4);
        if (magic == "OggS")
            return ".ogg";
        if (magic == "fLaC")
            return ".flac";
        if (magic.StartsWith("ID3") || (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0))
            return ".mp3";
        return ".wav";
    }

    /// <summary>Erzeugt eine WAV-Datei aus 16-Bit-Samples (mono).</summary>
    public static byte[] Wav(ReadOnlySpan<short> samples, int sampleRate = SampleRate)
    {
        var data = new byte[44 + samples.Length * 2];
        WriteHeader(data, sampleRate, samples.Length * 2);
        MemoryMarshal.AsBytes(samples).CopyTo(data.AsSpan(44));
        return data;
    }

    static void WriteHeader(byte[] d, int sampleRate, int dataLength)
    {
        Encoding.ASCII.GetBytes("RIFF").CopyTo(d, 0);
        BitConverter.TryWriteBytes(d.AsSpan(4), 36 + dataLength);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(d, 8);
        BitConverter.TryWriteBytes(d.AsSpan(16), 16);
        BitConverter.TryWriteBytes(d.AsSpan(20), (short)1);          // PCM
        BitConverter.TryWriteBytes(d.AsSpan(22), (short)1);          // mono
        BitConverter.TryWriteBytes(d.AsSpan(24), sampleRate);
        BitConverter.TryWriteBytes(d.AsSpan(28), sampleRate * 2);
        BitConverter.TryWriteBytes(d.AsSpan(32), (short)2);
        BitConverter.TryWriteBytes(d.AsSpan(34), (short)16);
        Encoding.ASCII.GetBytes("data").CopyTo(d, 36);
        BitConverter.TryWriteBytes(d.AsSpan(40), dataLength);
    }

    /// <summary>
    /// Korrigiert die Längenangaben einer WAV-Datei, deren Aufnahmeprogramm abgebrochen wurde
    /// (der Kopf enthält dann oft 0 oder die Maximallänge).
    /// </summary>
    public static byte[] FixWavLengths(byte[] wav)
    {
        if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            return wav;
        BitConverter.TryWriteBytes(wav.AsSpan(4), wav.Length - 8);
        for (int pos = 12; pos + 8 <= wav.Length;)
        {
            var id = Encoding.ASCII.GetString(wav, pos, 4);
            int size = BitConverter.ToInt32(wav, pos + 4);
            if (id == "data")
            {
                BitConverter.TryWriteBytes(wav.AsSpan(pos + 4), wav.Length - pos - 8);
                break;
            }
            if (size < 0 || pos + 8 + size > wav.Length)
                break;
            pos += 8 + size + (size & 1);
        }
        return wav;
    }

    /// <summary>Dauer einer WAV-Datei in Sekunden (ungefähr, aus der Dateigröße).</summary>
    public static double WavSeconds(byte[] wav) =>
        wav.Length > 44 ? (wav.Length - 44) / (double)Math.Max(1, BitConverter.ToInt32(wav, 28)) : 0;

    /// <summary>Vorbis-Qualität (-0.1 … 1): 0.4 entspricht „oggenc -q 4“ und klingt für Sprache gut.</summary>
    public const float OggQuality = 0.4f;

    /// <summary>
    /// Wandelt eine WAV-Datei (PCM 8/16/24/32 Bit oder Float, beliebige Abtastrate und Kanäle) in
    /// Ogg Vorbis mono mit <see cref="SampleRate"/> um, wie es der Stift erwartet.
    /// Das Ergebnis ist bei gleicher Eingabe immer gleich (feste Seriennummer).
    /// Mit <paramref name="denoise"/> wird vorher das Hintergrundrauschen unterdrückt (<see cref="NoiseReduction"/>).
    /// </summary>
    public static byte[] WavToOgg(byte[] wav, float quality = OggQuality, bool denoise = false)
    {
        var samples = Resample(ReadWav(wav, out int rate), rate, SampleRate);
        if (denoise)
            samples = NoiseReduction.Apply(samples, SampleRate);
        var info = VorbisInfo.InitVariableBitRate(1, SampleRate, quality);
        var ogg = new OggStream(0x54757066); // „Tupf“
        using var output = new MemoryStream();

        void Flush(bool force)
        {
            while (ogg.PageOut(out var page, force))
            {
                output.Write(page.Header, 0, page.Header.Length);
                output.Write(page.Body, 0, page.Body.Length);
            }
        }

        var comments = new Comments();
        comments.AddTag("ENCODER", "Tupfer");
        ogg.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        ogg.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        ogg.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        // Die Kopfpakete stehen auf eigenen Seiten vor den Audiodaten
        Flush(true);

        var state = ProcessingState.Create(info);
        const int block = 1024;
        var buffer = new[] { new float[block] };
        for (int pos = 0; pos < samples.Length; pos += block)
        {
            int n = Math.Min(block, samples.Length - pos);
            Array.Copy(samples, pos, buffer[0], 0, n);
            state.WriteData(buffer, n);
            while (!ogg.Finished && state.PacketOut(out var packet))
            {
                ogg.PacketIn(packet);
                Flush(false);
            }
        }
        state.WriteEndOfStream();
        while (!ogg.Finished && state.PacketOut(out var packet))
            ogg.PacketIn(packet);
        Flush(true);
        return output.ToArray();
    }

    /// <summary>Liest eine WAV-Datei als Samples (mono, -1 … 1); mehrere Kanäle werden gemittelt.</summary>
    static float[] ReadWav(byte[] wav, out int sampleRate)
    {
        if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            throw new InvalidDataException("Keine WAV-Datei.");
        int format = 0, channels = 0, bits = 0;
        sampleRate = 0;
        for (int pos = 12; pos + 8 <= wav.Length;)
        {
            var id = Encoding.ASCII.GetString(wav, pos, 4);
            int size = BitConverter.ToInt32(wav, pos + 4);
            int start = pos + 8;
            if (id == "fmt " && size >= 16 && start + 16 <= wav.Length)
            {
                format = BitConverter.ToUInt16(wav, start);
                channels = BitConverter.ToUInt16(wav, start + 2);
                sampleRate = BitConverter.ToInt32(wav, start + 4);
                bits = BitConverter.ToUInt16(wav, start + 14);
                // WAVE_FORMAT_EXTENSIBLE: eigentliches Format im Untertyp
                if (format == 0xFFFE && size >= 26 && start + 26 <= wav.Length)
                    format = BitConverter.ToUInt16(wav, start + 24);
            }
            else if (id == "data")
            {
                if (channels < 1 || sampleRate < 1 || !(format == 1 && bits is 8 or 16 or 24 or 32 || format == 3 && bits == 32))
                    throw new InvalidDataException("Nicht unterstütztes WAV-Format (erwartet PCM oder Float).");
                // Längenangabe kann nach einem abgebrochenen Aufnahmeprogramm falsch sein
                int length = size < 0 || start + size > wav.Length ? wav.Length - start : size;
                int bytes = bits / 8, frame = bytes * channels;
                var samples = new float[length / frame];
                for (int i = 0; i < samples.Length; i++)
                {
                    float sum = 0;
                    for (int c = 0; c < channels; c++)
                        sum += Sample(wav, start + i * frame + c * bytes, bits, format == 3);
                    samples[i] = sum / channels;
                }
                return samples;
            }
            if (size < 0)
                break;
            pos = start + size + (size & 1);
        }
        throw new InvalidDataException("WAV-Datei ohne Audiodaten.");
    }

    static float Sample(byte[] d, int i, int bits, bool isFloat) => bits switch
    {
        8 => (d[i] - 128) / 128f,
        16 => BitConverter.ToInt16(d, i) / 32768f,
        24 => (d[i] | d[i + 1] << 8 | (sbyte)d[i + 2] << 16) / 8388608f,
        _ => isFloat ? Math.Clamp(BitConverter.ToSingle(d, i), -1f, 1f) : BitConverter.ToInt32(d, i) / 2147483648f,
    };

    /// <summary>Ändert die Abtastrate (lineare Interpolation, für Sprache ausreichend).</summary>
    static float[] Resample(float[] samples, int from, int to)
    {
        if (from == to || samples.Length == 0)
            return samples;
        var result = new float[(int)((long)samples.Length * to / from)];
        double step = (double)from / to;
        for (int i = 0; i < result.Length; i++)
        {
            double x = i * step;
            int j = (int)x;
            float a = samples[Math.Min(j, samples.Length - 1)], b = samples[Math.Min(j + 1, samples.Length - 1)];
            result[i] = a + (b - a) * (float)(x - j);
        }
        return result;
    }

    /// <summary>Wandelt Ogg Vorbis in WAV (mono oder Stereo, 16 Bit), z. B. zum Abspielen unter Windows.</summary>
    public static byte[] OggToWav(byte[] ogg)
    {
        using var reader = new VorbisReader(new MemoryStream(ogg), true);
        var samples = new List<short>();
        var buffer = new float[4096 * reader.Channels];
        int n;
        while ((n = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < n; i++)
                samples.Add((short)Math.Round(Math.Clamp(buffer[i], -1f, 1f) * 32767));
        var wav = Wav(samples.ToArray(), reader.SampleRate);
        if (reader.Channels > 1)
        {
            BitConverter.TryWriteBytes(wav.AsSpan(22), (short)reader.Channels);
            BitConverter.TryWriteBytes(wav.AsSpan(28), reader.SampleRate * 2 * reader.Channels);
            BitConverter.TryWriteBytes(wav.AsSpan(32), (short)(2 * reader.Channels));
        }
        return wav;
    }

    /// <summary>Dauer einer Ogg-Vorbis-Datei in Sekunden.</summary>
    public static double OggSeconds(byte[] ogg)
    {
        using var reader = new VorbisReader(new MemoryStream(ogg), true);
        return reader.TotalTime.TotalSeconds;
    }
}

/// <summary>
/// Desktop: Wiedergabe und Aufnahme unter Windows über MCI (winmm), sonst über externe Programme
/// (paplay/aplay/ffplay/afplay bzw. arecord/sox/ffmpeg).
/// </summary>
public class DesktopAudio : IAudioDevice
{
    Process? _player;
    Process? _recorder;
    string? _recordFile;

    static string TempFile(string name) => Path.Combine(Path.GetTempPath(), $"tupfer-{Environment.ProcessId}-{name}");

    /// <summary>Zählt die Wiedergaben, damit eine beendete nicht mehr auf ihr Ende wartet.</summary>
    int _playId;

    public async Task PlayAsync(byte[] data, string extension)
    {
        StopPlayback();
        int id = _playId;
        // MCI kennt kein Ogg Vorbis: vorher in WAV umwandeln
        if (OperatingSystem.IsWindows() && extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            data = await Task.Run(() => Audio.OggToWav(data));
            extension = ".wav";
        }
        var file = TempFile("play" + extension.ToLowerInvariant());
        File.WriteAllBytes(file, data);
        if (OperatingSystem.IsWindows())
        {
            Mci($"open \"{file}\" alias tupferplay");
            Mci("play tupferplay");
            while (id == _playId && MciStatus("status tupferplay mode") == "playing")
                await Task.Delay(100);
            return;
        }
        bool wav = extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);
        var players = new List<string[]>();
        if (OperatingSystem.IsMacOS())
            players.Add(["afplay", file]);
        if (wav)
            players.AddRange([["paplay", file], ["aplay", "-q", file]]);
        players.Add(["ffplay", "-nodisp", "-autoexit", "-loglevel", "quiet", file]);
        if (!wav)
            players.Add(["paplay", file]);
        var player = _player = StartFirst(players)
            ?? throw new InvalidOperationException("Kein Programm zum Abspielen gefunden (paplay, aplay, ffplay oder afplay).");
        await player.WaitForExitAsync();
    }

    public void StopPlayback()
    {
        _playId++;
        if (OperatingSystem.IsWindows())
        {
            Mci("close tupferplay", throwOnError: false);
            return;
        }
        try
        {
            if (_player is { HasExited: false })
                _player.Kill();
        }
        catch (InvalidOperationException)
        {
        }
        _player = null;
    }

    public Task StartRecordingAsync()
    {
        _recordFile = TempFile("aufnahme.wav");
        if (File.Exists(_recordFile))
            File.Delete(_recordFile);
        if (OperatingSystem.IsWindows())
        {
            Mci("open new type waveaudio alias tupferrec");
            Mci($"set tupferrec bitspersample 16 channels 1 samplespersec {Audio.SampleRate} alignment 2 bytespersec {Audio.SampleRate * 2}");
            Mci("record tupferrec");
            return Task.CompletedTask;
        }
        string rate = Audio.SampleRate.ToString();
        var recorders = new List<string[]>
        {
            new[] { "arecord", "-q", "-f", "S16_LE", "-r", rate, "-c", "1", _recordFile },
            new[] { "rec", "-q", "-c", "1", "-r", rate, "-b", "16", _recordFile },
        };
        if (OperatingSystem.IsMacOS())
            recorders.Add(["ffmpeg", "-loglevel", "quiet", "-f", "avfoundation", "-i", ":0", "-ac", "1", "-ar", rate, _recordFile]);
        else
            recorders.Add(["ffmpeg", "-loglevel", "quiet", "-f", "pulse", "-i", "default", "-ac", "1", "-ar", rate, _recordFile]);
        _recorder = StartFirst(recorders)
            ?? throw new InvalidOperationException("Kein Aufnahmeprogramm gefunden (arecord, sox oder ffmpeg).");
        return Task.CompletedTask;
    }

    public async Task<byte[]> StopRecordingAsync()
    {
        var file = _recordFile ?? throw new InvalidOperationException("Es läuft keine Aufnahme.");
        _recordFile = null;
        if (OperatingSystem.IsWindows())
        {
            Mci("stop tupferrec");
            Mci($"save tupferrec \"{file}\"");
            Mci("close tupferrec");
        }
        else if (_recorder is { } recorder)
        {
            _recorder = null;
            // Mit SIGINT beenden die Programme die Datei sauber; notfalls hart abbrechen
            try
            {
                using (var kill = Process.Start("kill", ["-INT", recorder.Id.ToString()]))
                    await kill.WaitForExitAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await recorder.WaitForExitAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is Win32Exception or OperationCanceledException or InvalidOperationException)
            {
                if (!recorder.HasExited)
                    recorder.Kill();
            }
        }
        if (!File.Exists(file))
            throw new InvalidOperationException("Die Aufnahme ist leer – ist ein Mikrofon angeschlossen?");
        var wav = Audio.FixWavLengths(await File.ReadAllBytesAsync(file));
        File.Delete(file);
        return wav;
    }

    /// <summary>Startet das erste vorhandene Programm (Name und Argumente).</summary>
    static Process? StartFirst(IEnumerable<string[]> commands)
    {
        foreach (var command in commands)
        {
            var psi = new ProcessStartInfo(command[0])
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in command.Skip(1))
                psi.ArgumentList.Add(arg);
            try
            {
                var p = Process.Start(psi);
                if (p == null)
                    continue;
                p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEndAsync();
                return p;
            }
            catch (Win32Exception)
            {
                // Programm nicht installiert: nächstes versuchen
            }
        }
        return null;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern bool mciGetErrorString(int error, StringBuilder text, int length);

    /// <summary>Abfrage über MCI, z. B. „status … mode“; bei Fehlern leer.</summary>
    static string MciStatus(string command)
    {
        var result = new StringBuilder(64);
        return mciSendString(command, result, result.Capacity, IntPtr.Zero) == 0 ? result.ToString() : "";
    }

    static void Mci(string command, bool throwOnError = true)
    {
        int error = mciSendString(command, null, 0, IntPtr.Zero);
        if (error == 0 || !throwOnError)
            return;
        var text = new StringBuilder(256);
        mciGetErrorString(error, text, text.Capacity);
        throw new InvalidOperationException($"Audio (MCI): {text}");
    }
}
