using Windows.Win32;
using Windows.Win32.Foundation;

namespace WinTools.Platform.Shell;

public static class WindowFocus
{
    public static void BringToFront(nint hwnd) => PInvoke.SetForegroundWindow((HWND)hwnd);
}
