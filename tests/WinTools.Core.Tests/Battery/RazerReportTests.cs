using WinTools.Core.Battery;

namespace WinTools.Core.Tests.Battery;

public class RazerReportTests
{
    [Fact]
    public void BatteryRequest_MatchesWireFormat()
    {
        var report = RazerReport.Request(0x1F, RazerReport.PowerClass, RazerReport.BatteryLevelCommand);

        Assert.Equal(RazerReport.Length, report.Length);
        Assert.Equal("00001F00000002078000", Convert.ToHexString(report, 0, 10));
        Assert.Equal(0x02 ^ 0x07 ^ 0x80, report[89]);
    }

    [Fact]
    public void TryParseValue_ReadsBatteryFromCapturedResponse()
    {
        // Captured from a Razer Viper V3 Pro (1532:00C1).
        var response = Response("00021F00000002078000E6");

        var raw = RazerReport.TryParseValue(response, RazerReport.PowerClass, RazerReport.BatteryLevelCommand);

        Assert.Equal((byte)0xE6, raw);
        Assert.Equal(90, RazerReport.ToPercent(raw!.Value));
    }

    [Theory]
    [InlineData("00011F00000002078000E6")] // busy
    [InlineData("00041F00000002078000E6")] // timeout (mouse asleep)
    [InlineData("00021F00000002078400E6")] // answer to a different command
    public void TryParseValue_RejectsUnsuccessfulOrForeignResponses(string hex)
    {
        Assert.Null(RazerReport.TryParseValue(Response(hex), RazerReport.PowerClass, RazerReport.BatteryLevelCommand));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 100)]
    [InlineData(128, 50)]
    public void ToPercent_ScalesFrom255(byte raw, int percent)
    {
        Assert.Equal(percent, RazerReport.ToPercent(raw));
    }

    private static byte[] Response(string prefixHex)
    {
        var report = new byte[RazerReport.Length];
        Convert.FromHexString(prefixHex).CopyTo(report, 0);
        return report;
    }
}
