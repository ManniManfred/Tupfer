using Tupfer.Designer.Services;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Rauschunterdrückung für Mikrofonaufnahmen.</summary>
public class NoiseReductionTests
{
    const int Rate = Audio.SampleRate;

    /// <summary>Sprachähnliches Signal: abwechselnd 0,5 s Pause und 0,5 s Klang (Grundton mit Obertönen, moduliert).</summary>
    static float[] Speech(double seconds)
    {
        var s = new float[(int)(seconds * Rate)];
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate;
            if (IsSpeech(i))
            {
                double f0 = 180 + 30 * Math.Sin(2 * Math.PI * 1.5 * t);
                double v = 0;
                for (int h = 1; h <= 8; h++)
                    v += Math.Sin(2 * Math.PI * f0 * h * t) / h;
                double envelope = Math.Sin(Math.PI * (t % 0.5) / 0.5);
                s[i] = (float)(0.15 * v * envelope);
            }
        }
        return s;
    }

    static bool IsSpeech(int i) => (i / (Rate / 2)) % 2 == 1;

    static float[] Noise(int length, double sigma, int seed = 1)
    {
        var random = new Random(seed);
        var n = new float[length];
        for (int i = 0; i < length; i++)
        {
            // Normalverteilt (Box-Muller)
            double u = 1 - random.NextDouble(), v = random.NextDouble();
            n[i] = (float)(sigma * Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v));
        }
        return n;
    }

    /// <summary>Leistung im Bereich, ohne die Übergänge (je 50 ms Rand).</summary>
    static double Power(float[] s, bool speech)
    {
        double sum = 0;
        int count = 0, margin = Rate / 20;
        for (int i = 0; i < s.Length; i++)
        {
            int inSegment = i % (Rate / 2);
            if (IsSpeech(i) != speech || inSegment < margin || inSegment > Rate / 2 - margin)
                continue;
            sum += s[i] * (double)s[i];
            count++;
        }
        return sum / count;
    }

    static double Db(double ratio) => 10 * Math.Log10(ratio);

    [Fact]
    public void SenktRauschenInPausen()
    {
        var clean = Speech(4);
        var noise = Noise(clean.Length, 0.01);
        var noisy = clean.Zip(noise, (a, b) => a + b).ToArray();

        var result = NoiseReduction.Apply(noisy, Rate);

        Assert.Equal(noisy.Length, result.Length);
        // Pausen: mindestens 15 dB leiser
        Assert.True(Db(Power(noisy, false) / Power(result, false)) > 15,
            $"Rauschen nur um {Db(Power(noisy, false) / Power(result, false)):0.0} dB gesenkt");
        // Sprache: Lautstärke höchstens 1 dB verändert
        double speech = Db(Power(result, true) / Power(clean, true));
        Assert.True(Math.Abs(speech) < 1, $"Sprache um {speech:0.0} dB verändert");
    }

    [Fact]
    public void ErhaeltSignalform()
    {
        var clean = Speech(4);
        var noisy = clean.Zip(Noise(clean.Length, 0.01), (a, b) => a + b).ToArray();
        var result = NoiseReduction.Apply(noisy, Rate);
        // Vergleich mit dem sauberen Signal nach demselben Hochpass (ohne Rauschen wird nur er angewendet),
        // da der Hochpass die Phase der tiefen Töne verschiebt
        clean = NoiseReduction.Apply(clean, Rate);
        double before = 0, after = 0;
        for (int i = 0; i < clean.Length; i++)
            if (IsSpeech(i))
            {
                before += Math.Pow(noisy[i] - clean[i], 2);
                after += Math.Pow(result[i] - clean[i], 2);
            }
        Assert.True(Db(Power(clean, true) * clean.Length / 2 / after) > 10,
            $"Sprache zu stark verfälscht (Abstand {Db(Power(clean, true) * clean.Length / 2 / after):0.0} dB)");
        Assert.True(after < before / 4);
    }

    [Fact]
    public void OhnePausenNurHochpass()
    {
        // Durchgehender Ton ohne leise Abschnitte: nichts als „Rauschen“ abziehen, nur Hochpass (ändert bei 440 Hz
        // die Lautstärke kaum)
        var tone = Enumerable.Range(0, 2 * Rate).Select(i => (float)(0.3 * Math.Sin(2 * Math.PI * 440 * i / Rate))).ToArray();
        var noisy = tone.Zip(Noise(tone.Length, 0.01), (a, b) => a + b).ToArray();
        var result = NoiseReduction.Apply(noisy, Rate);
        Assert.Equal(noisy.Length, result.Length);
        double before = noisy.Skip(Rate / 10).Sum(v => v * (double)v), after = result.Skip(Rate / 10).Sum(v => v * (double)v);
        Assert.True(Math.Abs(Db(after / before)) < 0.2, $"Lautstärke um {Db(after / before):0.00} dB verändert");
    }

    [Fact]
    public void StilleUndKurzeAufnahmen()
    {
        Assert.Empty(NoiseReduction.Apply([], Rate));
        Assert.All(NoiseReduction.Apply(new float[Rate], Rate), v => Assert.Equal(0, v));
        Assert.Equal(100, NoiseReduction.Apply(new float[100], Rate).Length);
    }

    [Fact]
    public void OggMitRauschunterdrueckung()
    {
        var noisy = Speech(2).Zip(Noise(2 * Rate, 0.01), (a, b) => (short)Math.Clamp((a + b) * 32767, -32768, 32767)).ToArray();
        var wav = Audio.Wav(noisy);
        var ogg = Audio.WavToOgg(wav, denoise: true);
        Assert.Equal(Audio.OggSeconds(Audio.WavToOgg(wav)), Audio.OggSeconds(ogg), 3);
        // Rauschen lässt sich schlechter komprimieren: entrauscht kleiner
        Assert.True(ogg.Length < Audio.WavToOgg(wav).Length);
    }
}
