using WinTools.Core.Brightness;

namespace WinTools.Core.Tests.Brightness;

public class MonitorNamesTests
{
    [Fact]
    public void UniqueNames_AreUnchanged()
    {
        Assert.Equal(new[] { "DELL U2720Q", "LG 27UK850" }, MonitorNames.Disambiguate(["DELL U2720Q", "LG 27UK850"]));
    }

    [Fact]
    public void DuplicateNames_GetOrdinalSuffixes()
    {
        Assert.Equal(
            new[] { "DELL U2720Q (1)", "LG 27UK850", "DELL U2720Q (2)" },
            MonitorNames.Disambiguate(["DELL U2720Q", "LG 27UK850", "DELL U2720Q"]));
    }

    [Fact]
    public void DuplicateDetection_IgnoresCase()
    {
        Assert.Equal(new[] { "Dell (1)", "DELL (2)" }, MonitorNames.Disambiguate(["Dell", "DELL"]));
    }

    [Fact]
    public void EmptyList_ReturnsEmpty()
    {
        Assert.Empty(MonitorNames.Disambiguate([]));
    }
}
