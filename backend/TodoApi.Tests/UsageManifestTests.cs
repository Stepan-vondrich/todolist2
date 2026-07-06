using TodoApi;

namespace TodoApi.Tests;

public class UsageManifestTests
{
    [Fact]
    public void ManifestTotalBytes_SumsConfigAndLayerSizes()
    {
        var json = "{\"config\":{\"size\":100},\"layers\":[{\"size\":200},{\"size\":300}]}";
        Assert.Equal(600, Usage.ManifestTotalBytes(json));
    }

    [Fact]
    public void ManifestTotalBytes_InvalidJson_ReturnsZero()
    {
        Assert.Equal(0, Usage.ManifestTotalBytes("not a manifest"));
    }
}
