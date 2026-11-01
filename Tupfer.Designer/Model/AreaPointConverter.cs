using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tupfer.Designer.Model;

/// <summary>
/// Schreibt einen <see cref="AreaPoint"/> kompakt als <c>[x,y]</c>, mit Bogen als <c>[x,y,bulge]</c>,
/// immer in einer Zeile (auch bei eingerücktem JSON). Liest außerdem das alte Format <c>{"X":…,"Y":…,"Bulge":…}</c>.
/// </summary>
public class AreaPointConverter : JsonConverter<AreaPoint>
{
    public override AreaPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadPoint(ref reader);

    internal static AreaPoint ReadPoint(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.StartArray => ReadArray(ref reader),
            JsonTokenType.StartObject => ReadObject(ref reader),
            _ => throw new JsonException("Punkt erwartet: [x,y] oder [x,y,bulge]."),
        };

    static AreaPoint ReadArray(ref Utf8JsonReader reader)
    {
        Span<double> values = stackalloc double[3];
        var count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number || count == 3)
                throw new JsonException("Punkt erwartet: [x,y] oder [x,y,bulge].");
            values[count++] = reader.GetDouble();
        }
        if (count < 2)
            throw new JsonException("Punkt erwartet: [x,y] oder [x,y,bulge].");
        return new AreaPoint(values[0], values[1], values[2]);
    }

    // Altes Format: Objekt mit X, Y und optional Bulge
    static AreaPoint ReadObject(ref Utf8JsonReader reader)
    {
        double x = 0, y = 0, bulge = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "X": x = reader.GetDouble(); break;
                case "Y": y = reader.GetDouble(); break;
                case "Bulge": bulge = reader.GetDouble(); break;
                default: reader.Skip(); break;
            }
        }
        return new AreaPoint(x, y, bulge);
    }

    // Als Rohwert, damit WriteIndented nicht jede Zahl in eine eigene Zeile setzt
    public override void Write(Utf8JsonWriter writer, AreaPoint value, JsonSerializerOptions options) =>
        writer.WriteRawValue(Format(value), skipInputValidation: true);

    internal static string Format(AreaPoint value) =>
        value.Bulge == 0
            ? $"[{Number(value.X)},{Number(value.Y)}]"
            : $"[{Number(value.X)},{Number(value.Y)},{Number(value.Bulge)}]";

    static string Number(double value) =>
        double.IsFinite(value)
            ? value.ToString("R", CultureInfo.InvariantCulture)
            : throw new JsonException($"Ungültige Koordinate: {value}");
}

/// <summary>
/// Schreibt eine Punktliste in einer Zeile als <c>[[x1,y1],[x2,y2],…]</c>; ohne diesen Konverter stünde bei
/// eingerücktem JSON jeder Punkt in einer eigenen Zeile. Liest beide Punktformate (siehe <see cref="AreaPointConverter"/>).
/// </summary>
public class AreaPointListConverter : JsonConverter<List<AreaPoint>>
{
    public override List<AreaPoint> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Punktliste erwartet.");
        var points = new List<AreaPoint>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            points.Add(AreaPointConverter.ReadPoint(ref reader));
        return points;
    }

    public override void Write(Utf8JsonWriter writer, List<AreaPoint> value, JsonSerializerOptions options) =>
        writer.WriteRawValue($"[{string.Join(",", value.Select(AreaPointConverter.Format))}]", skipInputValidation: true);
}
