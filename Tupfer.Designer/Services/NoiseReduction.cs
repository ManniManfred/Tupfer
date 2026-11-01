namespace Tupfer.Designer.Services;

/// <summary>
/// Unterdrückt gleichmäßiges Hintergrundrauschen (Lüfter, Rauschen des Mikrofons, Brummen) in Sprachaufnahmen.
/// Reines C# ohne native Bibliotheken, läuft daher auch im Browser (WebAssembly).
/// </summary>
/// <remarks>
/// Ablauf: Hochpass (80 Hz) gegen Brummen und Trittschall, dann Wiener-Filter im Spektrum (STFT mit Hann-Fenster,
/// 75 % Überlappung, Overlap-Add). Das Rauschprofil stammt aus den leisesten Abschnitten der Aufnahme.
/// Die Verstärkung wird nach Ephraim/Malah („decision directed“) über die Zeit und zusätzlich über benachbarte
/// Frequenzen geglättet und nach unten begrenzt; das verhindert „musical noise“ und dumpfe Sprache.
/// Ein sanftes Noise-Gate senkt Pausen etwas weiter ab. Die Länge der Aufnahme bleibt unverändert.
/// </remarks>
public static class NoiseReduction
{
    /// <summary>Stärkste Absenkung einzelner Frequenzen durch den Wiener-Filter (-18 dB).</summary>
    const float MinGain = 0.126f;

    /// <summary>Zusätzliche Absenkung in Sprechpausen durch das Noise-Gate (-9 dB).</summary>
    const float GateGain = 0.35f;

    /// <summary>
    /// Abschnitte, die leiser sind (mittlere Leistung, etwa -70 dBFS), gelten als digitale Stille, z. B. beim Start
    /// des Mikrofons, und zählen nicht zum Rauschprofil.
    /// </summary>
    const double SilencePower = 1e-7;

    /// <summary>Glättung des a-priori-Rauschabstands über die Zeit.</summary>
    const float Alpha = 0.98f;

    /// <summary>Anteil der leisesten Abschnitte, aus denen das Rauschprofil geschätzt wird.</summary>
    const double NoisePercentile = 0.1;

    /// <summary>
    /// Bearbeitet Samples (mono, -1 … 1) und liefert ein neues Feld gleicher Länge.
    /// Gibt es keine deutlich leiseren Abschnitte (kein erkennbares Rauschen), wird nur der Hochpass angewendet.
    /// </summary>
    public static float[] Apply(float[] samples, int sampleRate)
    {
        var x = HighPass(samples, sampleRate, 80);
        // Etwa 23 ms je Abschnitt (512 Samples bei 22050 Hz)
        int n = 256;
        while (n < sampleRate * 0.023)
            n *= 2;
        int hop = n / 4;
        if (x.Length < 2 * n)
            return x;

        int bins = n / 2 + 1;
        var window = new float[n];
        for (int i = 0; i < n; i++)
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n));

        // Abschnitte so legen, dass jedes Sample abgedeckt ist (Anfang und Ende mit Nullen aufgefüllt)
        int frames = (x.Length + n) / hop + 1;
        int offset = -n + hop;
        var power = new float[frames][];
        var energy = new double[frames];
        var meanSquare = new double[frames];
        var re = new double[n];
        var im = new double[n];
        var spectra = new (float[] Re, float[] Im)[frames];
        for (int f = 0; f < frames; f++)
        {
            int start = offset + f * hop;
            for (int i = 0; i < n; i++)
            {
                int j = start + i;
                re[i] = j >= 0 && j < x.Length ? x[j] * window[i] : 0;
                im[i] = 0;
                meanSquare[f] += re[i] * re[i];
            }
            meanSquare[f] /= 0.375 * n; // mittleres Quadrat des Hann-Fensters
            Fft(re, im, false);
            var r = new float[bins];
            var m = new float[bins];
            var p = new float[bins];
            double e = 0;
            for (int k = 0; k < bins; k++)
            {
                r[k] = (float)re[k];
                m[k] = (float)im[k];
                p[k] = r[k] * r[k] + m[k] * m[k];
                e += p[k];
            }
            spectra[f] = (r, m);
            power[f] = p;
            energy[f] = e;
        }

        // Rauschprofil: Mittel der leisesten Abschnitte (nur vollständig in der Aufnahme liegende, ohne digitale
        // Stille: sonst wäre das Profil fast null und es würde nichts unterdrückt)
        var inside = Enumerable.Range(0, frames)
            .Where(f => offset + f * hop >= 0 && offset + f * hop + n <= x.Length)
            .OrderBy(f => energy[f]).ToArray();
        if (inside.Length < 10)
            return x;
        double loudEnergy = energy[inside[inside.Length * 9 / 10]];
        inside = inside.Where(f => meanSquare[f] > SilencePower && energy[f] > loudEnergy * 1e-6).ToArray();
        if (inside.Length < 10)
            return x;
        int quiet = Math.Max(3, (int)(inside.Length * NoisePercentile));
        double quietEnergy = inside.Take(quiet).Average(f => energy[f]);
        // Ohne deutlich leisere Abschnitte (mindestens 10 dB unter den lauten) wäre das „Rauschen“ Sprache
        if (quietEnergy * 10 > loudEnergy)
            return x;
        var noise = new float[bins];
        foreach (var f in inside.Take(quiet))
            for (int k = 0; k < bins; k++)
                noise[k] += power[f][k] / quiet;
        for (int k = 0; k < bins; k++)
            noise[k] = Math.Max(noise[k], 1e-12f);

        var output = new double[x.Length];
        var norm = new double[x.Length];
        var gain = new float[bins];
        var smoothed = new float[bins];
        var previous = new float[bins]; // |G·X|² des vorigen Abschnitts
        bool first = true;
        // Noise-Gate: öffnet sofort, schließt erst nach einer Haltezeit langsam (Wortenden bleiben erhalten)
        float gate = 1;
        int hold = 0, holdFrames = (int)(0.2 * sampleRate / hop);
        float release = (float)Math.Exp(-hop / (0.08 * sampleRate));
        double noiseEnergy = noise.Sum(v => (double)v);
        for (int f = 0; f < frames; f++)
        {
            var p = power[f];
            for (int k = 0; k < bins; k++)
            {
                float post = p[k] / noise[k];
                float prio = Math.Max(post - 1, 0);
                if (!first)
                    prio = Alpha * previous[k] / noise[k] + (1 - Alpha) * prio;
                gain[k] = Math.Max(prio / (1 + prio), MinGain);
            }
            first = false;
            // Glättung über benachbarte Frequenzen gegen einzelne „zwitschernde“ Bins
            for (int k = 0; k < bins; k++)
            {
                float a = gain[Math.Max(k - 1, 0)], b = gain[Math.Min(k + 1, bins - 1)];
                smoothed[k] = 0.25f * a + 0.5f * gain[k] + 0.25f * b;
                previous[k] = smoothed[k] * smoothed[k] * p[k];
            }

            bool speech = energy[f] > 2 * noiseEnergy; // 3 dB über dem Rauschen
            if (speech)
            {
                gate = 1;
                hold = holdFrames;
            }
            else if (hold > 0)
                hold--;
            else
                gate = GateGain + (gate - GateGain) * release;

            var (r, m) = spectra[f];
            for (int k = 0; k < bins; k++)
            {
                float g = smoothed[k] * gate;
                re[k] = r[k] * g;
                im[k] = m[k] * g;
            }
            // Spiegeln für ein reelles Ergebnis
            for (int k = bins; k < n; k++)
            {
                re[k] = re[n - k];
                im[k] = -im[n - k];
            }
            Fft(re, im, true);
            int start = offset + f * hop;
            for (int i = 0; i < n; i++)
            {
                int j = start + i;
                if (j < 0 || j >= x.Length)
                    continue;
                output[j] += re[i] * window[i];
                norm[j] += window[i] * window[i];
            }
        }
        var result = new float[x.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = norm[i] > 1e-9 ? Math.Clamp((float)(output[i] / norm[i]), -1f, 1f) : 0;
        return result;
    }

    /// <summary>Hochpass 2. Ordnung (Butterworth).</summary>
    static float[] HighPass(float[] x, int sampleRate, double cutoff)
    {
        double w = 2 * Math.PI * cutoff / sampleRate, cos = Math.Cos(w), alpha = Math.Sin(w) / Math.Sqrt(2);
        double a0 = 1 + alpha;
        double b0 = (1 + cos) / 2 / a0, b1 = -(1 + cos) / a0, b2 = b0;
        double a1 = -2 * cos / a0, a2 = (1 - alpha) / a0;
        var y = new float[x.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = x[i];
            y2 = y1;
            y1 = v;
            y[i] = (float)v;
        }
        return y;
    }

    /// <summary>Radix-2-FFT an Ort und Stelle; die inverse ist durch die Länge geteilt.</summary>
    static void Fft(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = 2 * Math.PI / len * (inverse ? 1 : -1);
            double wr = Math.Cos(angle), wi = Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    double t = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = t;
                }
            }
        }
        if (inverse)
            for (int i = 0; i < n; i++)
            {
                re[i] /= n;
                im[i] /= n;
            }
    }
}
