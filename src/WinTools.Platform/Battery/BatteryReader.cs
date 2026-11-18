using System.Collections.Concurrent;
using WinTools.Core.Battery;

namespace WinTools.Platform.Battery;

/// <summary>Reads battery levels from supported wireless peripherals. Blocks on device I/O; never call from the UI thread.</summary>
public static class BatteryReader
{
    private const ushort RazerVendorId = 0x1532;
    private const ushort KeyboardUsage = 0x06;
    private const uint GenericReadWrite = 0x80000000 | 0x40000000;
    private static readonly TimeSpan HeadsetTimeout = TimeSpan.FromSeconds(1);

    // Razer models answer to different transaction ids; remember the one that worked.
    private static readonly ConcurrentDictionary<string, byte> RazerTransactionIds = new();

    public static async Task<IReadOnlyList<BatteryDevice>> ReadAllAsync()
    {
        var interfaces = HidInterfaces.Enumerate();
        var devices = new List<BatteryDevice>();
        devices.AddRange(ReadRazer(interfaces));
        devices.AddRange(await ReadLogitechHeadsets(interfaces));
        return devices;
    }

    private static IEnumerable<BatteryDevice> ReadRazer(IReadOnlyList<HidInterface> interfaces)
    {
        var groups = interfaces
            .Where(i => i.VendorId == RazerVendorId && i.FeatureLength == RazerReport.Length)
            .GroupBy(i => Id(i));

        foreach (var group in groups)
        {
            var candidates = group.OrderBy(i => i.UsagePage == 0x01 ? 0 : 1).ToList();
            var first = candidates[0];
            var kind = first.Usage == KeyboardUsage ? DeviceKind.Keyboard : DeviceKind.Mouse;
            int? percent = null;
            var charging = false;

            foreach (var candidate in candidates)
            {
                if (ReadRazerValue(candidate, group.Key, RazerReport.BatteryLevelCommand) is { } raw)
                {
                    percent = RazerReport.ToPercent(raw);
                    charging = ReadRazerValue(candidate, group.Key, RazerReport.ChargingStatusCommand) is > 0;
                    break;
                }
            }
            yield return new BatteryDevice(group.Key, Name(first, "Razer mouse"), kind, percent, charging);
        }
    }

    private static byte? ReadRazerValue(HidInterface hid, string id, byte command)
    {
        using var handle = HidInterfaces.Open(hid.Path, 0, overlapped: false);
        if (handle.IsInvalid)
        {
            return null;
        }
        IEnumerable<byte> transactionIds = RazerTransactionIds.TryGetValue(id, out var known)
            ? [known]
            : RazerReport.TransactionIds;

        foreach (var transactionId in transactionIds)
        {
            // Synapse talks to the same device; a busy or foreign reply just means "try again".
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (!HidInterfaces.SetFeature(handle, RazerReport.Request(transactionId, RazerReport.PowerClass, command)))
                {
                    break;
                }
                Thread.Sleep(30 + attempt * 30);
                var response = new byte[RazerReport.Length];
                if (HidInterfaces.GetFeature(handle, response) &&
                    RazerReport.TryParseValue(response, RazerReport.PowerClass, command) is { } value)
                {
                    RazerTransactionIds[id] = transactionId;
                    return value;
                }
            }
        }
        return null;
    }

    private static async Task<List<BatteryDevice>> ReadLogitechHeadsets(IReadOnlyList<HidInterface> interfaces)
    {
        var devices = new List<BatteryDevice>();
        var headsets = interfaces.Where(i =>
            i.VendorId == LogitechHeadsetReport.VendorId &&
            LogitechHeadsetReport.SupportedProductIds.Contains(i.ProductId) &&
            i.UsagePage == LogitechHeadsetReport.UsagePage &&
            i.OutputLength == LogitechHeadsetReport.Length);

        foreach (var hid in headsets)
        {
            var percent = await ReadLogitechHeadset(hid);
            devices.Add(new BatteryDevice(Id(hid), Name(hid, "Headset"), DeviceKind.Headset, percent, IsCharging: false));
        }
        return devices;
    }

    private static async Task<int?> ReadLogitechHeadset(HidInterface hid)
    {
        using var handle = HidInterfaces.Open(hid.Path, GenericReadWrite, overlapped: true);
        if (handle.IsInvalid)
        {
            return null;
        }
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
        using var timeout = new CancellationTokenSource(HeadsetTimeout);
        try
        {
            await stream.WriteAsync(LogitechHeadsetReport.BatteryRequest(), timeout.Token);
            var buffer = new byte[Math.Max(hid.InputLength, LogitechHeadsetReport.Length)];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                if (LogitechHeadsetReport.TryParseBattery(buffer.AsSpan(0, read)) is { } percent)
                {
                    return percent;
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            return null; // headset off or out of range: the receiver acks but never sends a reading
        }
    }

    private static string Id(HidInterface hid) => $"{hid.VendorId:X4}:{hid.ProductId:X4}";

    private static string Name(HidInterface hid, string fallback) => hid.Product.Length > 0 ? hid.Product : fallback;
}
