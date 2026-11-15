using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tupfer.Designer.Services;

namespace Tupfer.Designer.ViewModels;

/// <summary>
/// Schneiden einer Aufnahme und Ändern ihrer Lautstärke. Anfang und Ende sind Anteile (0–1) der Länge,
/// die Lautstärke in Prozent; übernommen wird erst mit <see cref="Result"/>.
/// </summary>
public partial class RecordingEditorViewModel : ObservableObject
{
    public const double MinVolume = 10, MaxVolume = 500;

    /// <summary>Kürzeste Länge des Ausschnitts (Anteil), damit Anfang und Ende nicht aufeinander liegen.</summary>
    const double MinLength = 0.01;

    public RecordingEditorViewModel(SpeakViewModel speak, float[] samples)
    {
        Speak = speak;
        Samples = samples;
        UpdatePeak();
    }

    public SpeakViewModel Speak { get; }

    /// <summary>Die ungeschnittene Aufnahme (mono, <see cref="Audio.SampleRate"/>).</summary>
    public float[] Samples { get; }

    public double Seconds => Samples.Length / (double)Audio.SampleRate;

    public string Title => $"Aufnahme „{Speak.Name}“ bearbeiten";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    double _start;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    double _end = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText), nameof(Gain), nameof(Clips))]
    double _volume = 100;

    partial void OnStartChanged(double value)
    {
        var clamped = Math.Clamp(value, 0, End - MinLength);
        if (clamped != value)
            Start = clamped;
        else
            UpdatePeak();
    }

    partial void OnEndChanged(double value)
    {
        var clamped = Math.Clamp(value, Start + MinLength, 1);
        if (clamped != value)
            End = clamped;
        else
            UpdatePeak();
    }

    partial void OnVolumeChanged(double value)
    {
        var clamped = Math.Clamp(Math.Round(value), MinVolume, MaxVolume);
        if (clamped != value)
            Volume = clamped;
    }

    public double Gain => Volume / 100;

    public string VolumeText => $"{Volume:0} %";

    public string RangeText =>
        $"Von {Start * Seconds:0.00} s bis {End * Seconds:0.00} s – Länge {(End - Start) * Seconds:0.00} s von {Seconds:0.00} s";

    /// <summary>Lautester Wert im Ausschnitt (vor der Lautstärkeänderung).</summary>
    float _peak;

    /// <summary>Bei dieser Lautstärke würde der Ausschnitt übersteuern (verzerrt klingen).</summary>
    public bool Clips => _peak * Gain > 1.001;

    void UpdatePeak()
    {
        var (start, end) = Range;
        _peak = Audio.Peak(Samples.AsSpan(start, end - start));
        OnPropertyChanged(nameof(Clips));
    }

    /// <summary>Ausschnitt in Samples.</summary>
    public (int Start, int End) Range => ((int)Math.Round(Start * Samples.Length), (int)Math.Round(End * Samples.Length));

    /// <summary>Stellt die Lautstärke so ein, dass der lauteste Wert knapp unter dem Anschlag liegt (-1 dB).</summary>
    [RelayCommand]
    void Normalize()
    {
        if (_peak > 0)
            Volume = Math.Clamp(Math.Floor(0.89 / _peak * 100), MinVolume, MaxVolume);
    }

    /// <summary>Ganze Aufnahme in ursprünglicher Lautstärke.</summary>
    [RelayCommand]
    void Reset()
    {
        Start = 0;
        End = 1;
        Volume = 100;
    }

    /// <summary>Die bearbeitete Aufnahme.</summary>
    public float[] Result()
    {
        var (start, end) = Range;
        return Audio.Cut(Samples, start, end, (float)Gain);
    }

    /// <summary>Unverändert gegenüber der Aufnahme (Übernehmen ist dann unnötig).</summary>
    public bool IsUnchanged => Start == 0 && End == 1 && Volume == 100;
}
