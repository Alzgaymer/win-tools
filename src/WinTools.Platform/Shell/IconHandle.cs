using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinTools.Platform.Shell;

public static class IconHandle
{
    /// <summary>Releases an HICON created by e.g. Bitmap.GetHicon.</summary>
    public static void Destroy(nint handle) => PInvoke.DestroyIcon((HICON)handle);
}
