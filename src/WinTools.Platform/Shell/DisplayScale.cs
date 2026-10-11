using System.Drawing;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace WinTools.Platform.Shell;

public static class DisplayScale
{
    /// <summary>Effective DPI of the primary monitor divided by 96 (1.0, 1.25, 1.5, …).</summary>
    public static double ForPrimaryMonitor()
    {
        var primary = PInvoke.MonitorFromPoint(new Point(0, 0), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        return PInvoke.GetDpiForMonitor(primary, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded
            ? dpiX / 96.0
            : 1.0;
    }
}
