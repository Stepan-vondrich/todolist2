using TodoApi.Models;

namespace TodoApi.Tests;

public class TaskManifestModelTests
{
    [Fact]
    public void TaskManifest_DefaultListFields_AreEmptyJsonArrays()
    {
        var m = new TaskManifest();
        Assert.Equal("[]", m.Dependencies);
        Assert.Equal("[]", m.Kdy);
        Assert.Equal("[]", m.MuzeBezetS);
    }

    [Fact]
    public void TaskManifest_DefaultStructuredFields_AreEmptyStrings()
    {
        var m = new TaskManifest();
        Assert.Equal("", m.CekaNaCloveka);
        Assert.Equal("", m.PevnyCas);
        Assert.Equal("", m.Periodicita);
        Assert.Equal("", m.AttentionSplit);
        Assert.False(m.JenVPraci);
    }

    [Fact]
    public void PlannerSettings_DefaultId_IsSingleton()
    {
        var s = new PlannerSettings();
        Assert.Equal(PlannerSettings.SingletonId, s.Id);
        Assert.Equal(1, s.Id);
    }
}
