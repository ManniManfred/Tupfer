using Tupfer.Designer.Model;

namespace Tupfer.Designer.ViewModels;

/// <summary>Objekt mit einem bearbeitbaren Polygon (OID-Fläche oder Farbfläche).</summary>
public interface IPolygonShape
{
    bool IsPolygon { get; }

    /// <summary>Eckpunkte relativ zum Rahmen.</summary>
    IReadOnlyList<AreaPoint> Points { get; }

    /// <summary>Eckpunkte in Seitenkoordinaten (mm).</summary>
    IEnumerable<AreaPoint> PagePoints { get; }

    /// <summary>Setzt das Polygon aus Punkten in Seitenkoordinaten; der Rahmen wird an den Umriss angepasst.</summary>
    void SetPolygon(IReadOnlyList<AreaPoint> pagePoints);
}
