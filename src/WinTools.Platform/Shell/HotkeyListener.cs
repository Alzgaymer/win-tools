using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;

namespace WinTools.Platform.Shell;

/// <summary>
/// Registers Alt+B on a window and listens for WM_HOTKEY and WM_DISPLAYCHANGE by subclassing it.
/// Events fire on the window's UI thread.
/// </summary>
public sealed class HotkeyListener : IDisposable
{
    private const int HotkeyId = 1;
    private const nuint SubclassId = 1;

    private readonly HWND _hwnd;
    private readonly SUBCLASSPROC _proc; // held so the GC never collects the native callback

    public HotkeyListener(nint hwnd)
    {
        _hwnd = (HWND)hwnd;
        _proc = WndProc;
        PInvoke.SetWindowSubclass(_hwnd, _proc, SubclassId, 0);
        IsRegistered = PInvoke.RegisterHotKey(_hwnd, HotkeyId, HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_NOREPEAT, 'B');
    }

    public bool IsRegistered { get; }

    public event Action? Pressed;

    public event Action? DisplayChanged;

    public void Dispose()
    {
        if (IsRegistered)
        {
            PInvoke.UnregisterHotKey(_hwnd, HotkeyId);
        }
        PInvoke.RemoveWindowSubclass(_hwnd, _proc, SubclassId);
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam, nuint subclassId, nuint refData)
    {
        if (msg == PInvoke.WM_HOTKEY && wParam.Value == HotkeyId)
        {
            Pressed?.Invoke();
            return new LRESULT(0);
        }
        if (msg == PInvoke.WM_DISPLAYCHANGE)
        {
            DisplayChanged?.Invoke();
        }
        return PInvoke.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
