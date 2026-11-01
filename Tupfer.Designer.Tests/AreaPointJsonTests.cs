using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Speicherformat der Polygonpunkte: kompakt als [x,y] bzw. [x,y,bulge], altes Objektformat lädt weiter.</summary>
public class AreaPointJsonTests
{
    static ProjectFile WithPoints(params AreaPoint[] points) => new()
    {
        Pages =
        [
            new PageEntry
            {
                Areas = [new AreaEntry { Label = "A", Shape = AreaShape.Polygon, Points = [.. points] }],
                Elements = [new ElementEntry { Points = [.. points] }],
            },
        ],
    };

    [Fact]
    public void NeuesFormatRundlauf()
    {
        AreaPoint[] points = [new(10.5, 20), new(0.1, -3.25, 0.4142135623730951), new(1e-7, 123456.789)];
        var json = ProjectStore.ToJson(WithPoints(points));

        Assert.Contains("[10.5,20]", json);
        Assert.Contains("[0.1,-3.25,0.4142135623730951]", json);
        Assert.DoesNotContain("\"X\": 10.5", json);

        var page = ProjectStore.FromJson(json).Pages[0];
        Assert.Equal(points, page.Areas[0].Points);
        Assert.Equal(points, page.Elements[0].Points);
    }

    [Fact]
    public void BulgeNurWennUngleichNull()
    {
        var json = ProjectStore.ToJson(WithPoints(new(1, 2), new(3, 4, -0.5)));
        Assert.Contains("[1,2]", json);
        Assert.DoesNotContain("[1,2,", json);
        Assert.Contains("[3,4,-0.5]", json);
    }

    [Fact]
    public void PunktlisteInEinerZeile()
    {
        var json = ProjectStore.ToJson(WithPoints(new(1, 2), new(3, 4)));
        Assert.Contains("\"Points\": [[1,2],[3,4]],", json);
    }

    [Fact]
    public void AltesFormatLaedt()
    {
        const string json = """
            {
              "Pages": [
                {
                  "Areas": [
                    { "Label": "A", "Shape": "Polygon",
                      "Points": [ { "X": 1.5, "Y": 2 }, { "X": 3, "Y": 4, "Bulge": 0.25 }, { "X": 0, "Y": 5 } ] }
                  ]
                }
              ]
            }
            """;
        var points = ProjectStore.FromJson(json).Pages[0].Areas[0].Points;
        Assert.Equal([new AreaPoint(1.5, 2), new AreaPoint(3, 4, 0.25), new AreaPoint(0, 5)], points);
    }
}
