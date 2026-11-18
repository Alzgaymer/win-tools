namespace WinTools.Core.Battery;

/// <summary>
/// Battery query for Logitech LIGHTSPEED headsets that speak the 0x51-framed protocol on the vendor
/// collection (usage page 0xFFA0, 64-byte reports), verified on the PRO X 2 LIGHTSPEED (046D:0AF7).
/// The device first acks with a short 0x51 report, then sends the reading:
/// <c>51 0B 00 03 10 00 06 00 04 0A [percent] ...</c>.
/// </summary>
public static class LogitechHeadsetReport
{
    public const ushort VendorId = 0x046D;
    public const ushort UsagePage = 0xFFA0;
    public const int Length = 64;

    public static readonly ushort[] SupportedProductIds = [0x0AF7];

    private const byte ReportId = 0x51;

    public static byte[] BatteryRequest()
    {
        var report = new byte[Length];
        byte[] payload = [ReportId, 0x08, 0x00, 0x03, 0x1A, 0x00, 0x03, 0x00, 0x04, 0x0A];
        payload.CopyTo(report, 0);
        return report;
    }

    public static int? TryParseBattery(ReadOnlySpan<byte> response)
    {
        if (response.Length < 11 ||
            response[0] != ReportId ||
            response[1] < 0x0B ||
            response[3] != 0x03 ||
            response[8] != 0x04 ||
            response[9] != 0x0A ||
            response[10] > 100)
        {
            return null;
        }
        return response[10];
    }
}
