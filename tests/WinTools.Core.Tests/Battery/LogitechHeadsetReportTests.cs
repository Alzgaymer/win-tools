using WinTools.Core.Battery;

namespace WinTools.Core.Tests.Battery;

public class LogitechHeadsetReportTests
{
    [Fact]
    public void BatteryRequest_MatchesWireFormat()
    {
        var report = LogitechHeadsetReport.BatteryRequest();

        Assert.Equal(LogitechHeadsetReport.Length, report.Length);
        Assert.Equal("510800031A000300040A00", Convert.ToHexString(report, 0, 11));
    }

    [Fact]
    public void TryParseBattery_ReadsCapturedResponse()
    {
        // Captured from a PRO X 2 LIGHTSPEED (046D:0AF7).
        Assert.Equal(90, LogitechHeadsetReport.TryParseBattery(Convert.FromHexString("510B000310000600040A5A5A00")));
    }

    [Theory]
    [InlineData("510300031A0000000000000000")] // ack that precedes the reading
    [InlineData("110B000310000600040A5A5A00")] // different report id
    [InlineData("510B000310000600040AFF5A00")] // out of range
    [InlineData("510B0003")] // truncated
    public void TryParseBattery_RejectsOtherReports(string hex)
    {
        Assert.Null(LogitechHeadsetReport.TryParseBattery(Convert.FromHexString(hex)));
    }
}
