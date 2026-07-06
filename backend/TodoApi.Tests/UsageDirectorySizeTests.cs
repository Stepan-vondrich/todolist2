using TodoApi;

namespace TodoApi.Tests;

public class UsageDirectorySizeTests
{
    [Fact]
    public void DirectorySize_SumsFileBytesAndCounts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "usagetest_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(dir, "b.bin"), new byte[250]);
        try
        {
            var (bytes, count) = Usage.DirectorySize(dir);
            Assert.Equal(350, bytes);
            Assert.Equal(2, count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DirectorySize_MissingDirectory_ReturnsZero()
    {
        var (bytes, count) = Usage.DirectorySize(Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid()));
        Assert.Equal(0, bytes);
        Assert.Equal(0, count);
    }
}
