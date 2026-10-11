using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using WinTools.Core.Brightness;

namespace WinTools.Platform.Brightness;

public sealed record PhysicalMonitor(nint Handle, string Name, bool Supported, uint Brightness);

/// <summary>DDC/CI brightness via dxva2. All calls block on I2C; never call from the UI thread.</summary>
public static class PhysicalMonitors
{
    public static IReadOnlyList<PhysicalMonitor> Enumerate()
    {
        var friendlyNames = FriendlyNamesByGdiDevice();
        var found = new List<(nint Handle, string Name, uint? Brightness)>();

        foreach (var hmonitor in DisplayMonitors())
        {
            var gdiName = GdiDeviceName(hmonitor);
            foreach (var handle in PhysicalHandles(hmonitor))
            {
                var name = gdiName is not null && friendlyNames.TryGetValue(gdiName, out var friendly)
                    ? friendly
                    : $"Monitor {found.Count + 1}";
                found.Add((handle, name, TryGetBrightness(handle)));
            }
        }

        var names = MonitorNames.Disambiguate(found.Select(m => m.Name).ToList());
        return found
            .Select((m, i) => new PhysicalMonitor(m.Handle, names[i], m.Brightness is not null, m.Brightness ?? 0))
            .ToList();
    }

    public static unsafe uint? TryGetBrightness(nint handle)
    {
        uint min, current, max;
        var ok = PInvoke.GetMonitorBrightness((HANDLE)handle, &min, &current, &max) != 0;
        return ok ? Math.Clamp(current, 1u, 100u) : null;
    }

    public static void SetBrightness(nint handle, uint value) =>
        PInvoke.SetMonitorBrightness((HANDLE)handle, value);

    public static void Destroy(IEnumerable<nint> handles)
    {
        foreach (var handle in handles)
        {
            PInvoke.DestroyPhysicalMonitor((HANDLE)handle);
        }
    }

    private static unsafe List<HMONITOR> DisplayMonitors()
    {
        var monitors = new List<HMONITOR>();
        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (hmonitor, _, _, _) =>
        {
            monitors.Add(hmonitor);
            return true;
        }, default);
        return monitors;
    }

    private static List<nint> PhysicalHandles(HMONITOR hmonitor)
    {
        if (!PInvoke.GetNumberOfPhysicalMonitorsFromHMONITOR(hmonitor, out var count) || count == 0)
        {
            return [];
        }
        var physical = new PHYSICAL_MONITOR[count];
        if (!PInvoke.GetPhysicalMonitorsFromHMONITOR(hmonitor, physical))
        {
            return [];
        }
        return physical.Select(p => (nint)p.hPhysicalMonitor).ToList();
    }

    private static unsafe string? GdiDeviceName(HMONITOR hmonitor)
    {
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        return PInvoke.GetMonitorInfo(hmonitor, (MONITORINFO*)&info)
            ? CString(info.szDevice.AsReadOnlySpan())
            : null;
    }

    /// <summary>Maps GDI device names (\\.\DISPLAY1) to EDID friendly names ("DELL U2720Q").</summary>
    private static unsafe Dictionary<string, string> FriendlyNamesByGdiDevice()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        const QUERY_DISPLAY_CONFIG_FLAGS flags = QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS;

        if (PInvoke.GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount) != WIN32_ERROR.ERROR_SUCCESS)
        {
            return result;
        }
        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        if (PInvoke.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes) != WIN32_ERROR.ERROR_SUCCESS)
        {
            return result;
        }

        foreach (var path in paths.AsSpan(0, (int)pathCount))
        {
            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            source.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            source.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
            source.header.adapterId = path.sourceInfo.adapterId;
            source.header.id = path.sourceInfo.id;

            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
            target.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            target.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
            target.header.adapterId = path.targetInfo.adapterId;
            target.header.id = path.targetInfo.id;

            if (PInvoke.DisplayConfigGetDeviceInfo(&source.header) != 0 ||
                PInvoke.DisplayConfigGetDeviceInfo(&target.header) != 0)
            {
                continue;
            }
            var gdiName = CString(source.viewGdiDeviceName.AsReadOnlySpan());
            var friendly = CString(target.monitorFriendlyDeviceName.AsReadOnlySpan());
            if (friendly.Length > 0)
            {
                result.TryAdd(gdiName, friendly);
            }
        }
        return result;
    }

    private static string CString(ReadOnlySpan<char> buffer)
    {
        var end = buffer.IndexOf('\0');
        return (end < 0 ? buffer : buffer[..end]).ToString();
    }
}
