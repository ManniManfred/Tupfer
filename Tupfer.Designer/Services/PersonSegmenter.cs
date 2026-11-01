using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Tupfer.Designer.Services;

/// <summary>
/// Personenerkennung am Desktop: DeepLab v3 (Assets/personen.onnx, eingebettet) mit ONNX Runtime.
/// Im Browser übernimmt das wwwroot/tupfer.js mit onnxruntime-web.
/// </summary>
public static class PersonSegmenter
{
    static readonly Lazy<InferenceSession> Session = new(() =>
    {
        using var stream = typeof(PersonSegmenter).Assembly.GetManifestResourceStream("personen.onnx")
            ?? throw new FileNotFoundException("personen.onnx fehlt im Programm.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new InferenceSession(buffer.ToArray());
    });

    /// <summary>RGB (257 × 257 × 3) → Maske (257 × 257, 1 = Person), siehe <see cref="PersonDetector.Segmenter"/>.</summary>
    public static Task<byte[]> SegmentAsync(byte[] rgb) => Task.Run(() =>
    {
        int n = PersonDetector.Size;
        var input = NamedOnnxValue.CreateFromTensor("bild", new DenseTensor<byte>(rgb, [1, n, n, 3]));
        using var results = Session.Value.Run([input]);
        return results.First(r => r.Name == "person").AsEnumerable<byte>().ToArray();
    });
}
