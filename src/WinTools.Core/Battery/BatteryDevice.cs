namespace WinTools.Core.Battery;

public enum DeviceKind
{
    Mouse,
    Headset,
    Keyboard,
}

/// <param name="Id">Stable per model (VID:PID), used to keep one tray icon per device.</param>
/// <param name="Percent">0-100, or null when the device is connected but did not answer (asleep, out of range).</param>
public sealed record BatteryDevice(string Id, string Name, DeviceKind Kind, int? Percent, bool IsCharging);
