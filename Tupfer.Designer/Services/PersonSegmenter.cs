using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Tupfer.Designer.Services;

/// <summary>
/// Personenerkennung am Desktop mit ONNX Runtime: RF-DETR-Seg (Assets/personen.onnx) und YuNet
/// (Assets/gesichter.onnx), beide eingebettet. Im Browser übernimmt das wwwroot/tupfer.js mit onnxruntime-web.
/// </summary>
public static class PersonSegmenter
{
    static readonly Lazy<InferenceSession> Persons = new(() => Load("personen.onnx"));
    static readonly Lazy<InferenceSession> Faces = new(() => Load("gesichter.onnx"));

    static InferenceSession Load(string name)
    {
        using var stream = typeof(PersonSegmenter).Assembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"{name} fehlt im Programm.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new InferenceSession(buffer.ToArray());
    }

    /// <summary>RGB (384 × 384 × 3) → Werte und Masken der Kandidaten, siehe <see cref="PersonDetector.Segmenter"/>.</summary>
    public static Task<(float[] Scores, byte[] Masks)> SegmentAsync(byte[] rgb) => Task.Run(() =>
    {
        int n = PersonDetector.Size;
        var input = NamedOnnxValue.CreateFromTensor("bild", new DenseTensor<byte>(rgb, [1, n, n, 3]));
        using var results = Persons.Value.Run([input]);
        return (results.First(r => r.Name == "werte").AsEnumerable<float>().ToArray(),
            results.First(r => r.Name == "masken").AsEnumerable<byte>().ToArray());
    });

    /// <summary>RGB (Breite × Höhe × 3) → Gesichter je Ankerpunkt, siehe <see cref="PersonDetector.FaceDetector"/>.</summary>
    public static Task<float[]> DetectFacesAsync(byte[] rgb, int width, int height) => Task.Run(() =>
    {
        var input = NamedOnnxValue.CreateFromTensor("bild", new DenseTensor<byte>(rgb, [1, height, width, 3]));
        using var results = Faces.Value.Run([input]);
        return results.First(r => r.Name == "gesichter").AsEnumerable<float>().ToArray();
    });
}
