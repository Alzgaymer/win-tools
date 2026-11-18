namespace WinTools.Core.Battery;

/// <summary>
/// Razer's 90-byte control report, sent as HID feature report 0 (91 bytes including the report ID).
/// Layout after the report ID: status, transaction id, remaining packets (2), protocol type, data size,
/// command class, command id, 80 argument bytes, CRC (XOR of bytes 2..87), reserved.
/// </summary>
public static class RazerReport
{
    public const int Length = 91;

    public const byte PowerClass = 0x07;
    public const byte BatteryLevelCommand = 0x80;
    public const byte ChargingStatusCommand = 0x84;

    /// <summary>Models differ in the transaction id they answer to; newer wireless mice use 0x1F.</summary>
    public static readonly byte[] TransactionIds = [0x1F, 0xFF, 0x3F];

    private const byte StatusSuccess = 0x02;

    public static byte[] Request(byte transactionId, byte commandClass, byte commandId)
    {
        var report = new byte[Length];
        report[2] = transactionId;
        report[6] = 0x02; // data size: the device answers with two argument bytes
        report[7] = commandClass;
        report[8] = commandId;
        report[89] = Crc(report);
        return report;
    }

    /// <summary>Returns the second argument byte of a successful response to the given command.</summary>
    public static byte? TryParseValue(ReadOnlySpan<byte> response, byte commandClass, byte commandId)
    {
        if (response.Length < Length ||
            response[1] != StatusSuccess ||
            response[7] != commandClass ||
            response[8] != commandId)
        {
            return null;
        }
        return response[10];
    }

    /// <summary>Battery is reported as 0-255.</summary>
    public static int ToPercent(byte raw) => (int)Math.Round(raw * 100 / 255.0);

    private static byte Crc(ReadOnlySpan<byte> report)
    {
        byte crc = 0;
        foreach (var b in report[3..89])
        {
            crc ^= b;
        }
        return crc;
    }
}
