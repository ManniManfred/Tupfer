using System.Text.Json;
using Tupfer.Designer.Services;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Auswertung des neuesten Releases für die automatische Aktualisierung.</summary>
public class UpdaterTests
{
    static JsonElement Release(string tag, string asset = Updater.SetupAsset, string? digest = null, bool prerelease = false) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft = false,
            prerelease,
            assets = new object[]
            {
                new { name = "Tupfer-Designer-Linux-x64.deb", browser_download_url = "https://example.org/linux.deb", digest = (string?)null },
                new { name = asset, browser_download_url = "https://example.org/setup.exe", digest },
            },
        })).RootElement;

    [Fact]
    public void NeuereVersion_LiefertSetupUndPruefsumme()
    {
        var release = Updater.ParseRelease(Release("v1.28.0", digest: "sha256:ABCDEF"), "1.27.0");
        Assert.NotNull(release);
        Assert.Equal(new Version(1, 28, 0), release.Version);
        Assert.Equal("https://example.org/setup.exe", release.SetupUrl);
        Assert.Equal("abcdef", release.Sha256);
    }

    [Theory]
    [InlineData("v1.27.0", "1.27.0")]
    [InlineData("v1.27", "1.27.0")]
    [InlineData("v1.26.5", "1.27.0")]
    public void GleicheOderAeltereVersion_KeinUpdate(string tag, string installed) =>
        Assert.Null(Updater.ParseRelease(Release(tag), installed));

    [Fact]
    public void OhneWindowsSetupOderVorabversion_KeinUpdate()
    {
        Assert.Null(Updater.ParseRelease(Release("v2.0.0", asset: "anderes.exe"), "1.27.0"));
        Assert.Null(Updater.ParseRelease(Release("v2.0.0", prerelease: true), "1.27.0"));    }

    [Fact]
    public void OhnePruefsumme_TrotzdemUpdate() =>
        Assert.Null(Updater.ParseRelease(Release("v1.27.1"), "1.27.0")!.Sha256);
}
