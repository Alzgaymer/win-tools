using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Capture show events before launching the app: its flyout may auto-hide between polls.
public sealed class FlyoutProbe : IDisposable
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr state);
    private delegate void WinEventProc(IntPtr hook, uint eventId, IntPtr hwnd, int objectId, int childId, uint threadId, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Hwnd;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out Message message, IntPtr hwnd, uint first, uint last);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private readonly object _gate = new();
    private readonly Dictionary<uint, int> _shown = new();
    private readonly List<string> _events = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private uint _threadId;
    private Exception _failure;

    public FlyoutProbe()
    {
        _thread = new Thread(Watch) { IsBackground = true, Name = "Flyout visibility probe" };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Window event probe did not start.");
        if (_failure != null) throw new InvalidOperationException("Window event probe failed.", _failure);
    }

    public bool WasShown(int processId)
    {
        lock (_gate) return _shown.ContainsKey((uint)processId);
    }

    public string Describe(int processId)
    {
        var windows = new List<string>();
        EnumWindows((hwnd, state) =>
        {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner == processId) windows.Add($"hwnd={hwnd}, visible={IsWindowVisible(hwnd)}, title='{Title(hwnd)}'");
            return true;
        }, IntPtr.Zero);
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint foregroundOwner);
        lock (_gate) return $"Windows: [{string.Join("; ", windows)}]. Foreground: pid={foregroundOwner}, title='{Title(foreground)}'. Events: [{string.Join("; ", _events)}].";
    }

    public static bool Find(int processId, bool visible)
    {
        bool found = false;
        EnumWindows((hwnd, state) =>
        {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != processId || IsWindowVisible(hwnd) != visible) return true;
            found = Title(hwnd) == "WinTools";
            return !found;
        }, IntPtr.Zero);
        return found;
    }

    public void Dispose()
    {
        PostThreadMessage(_threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero); // WM_QUIT
        if (!_thread.Join(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Window event probe did not stop.");
        _ready.Dispose();
    }

    private static string Title(IntPtr hwnd)
    {
        var text = new StringBuilder(256);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    private void Watch()
    {
        WinEventProc callback = (hook, eventId, hwnd, objectId, childId, threadId, time) =>
        {
            if (objectId != 0 || childId != 0 || Title(hwnd) != "WinTools") return;
            GetWindowThreadProcessId(hwnd, out uint owner);
            lock (_gate)
            {
                _events.Add($"pid={owner}, event={(eventId == 0x8002 ? "show" : "hide")}, time={time}");
                if (eventId == 0x8002) _shown[owner] = _shown.GetValueOrDefault(owner) + 1;
            }
        };
        var callbackRoot = GCHandle.Alloc(callback);
        IntPtr eventHook = IntPtr.Zero;
        try
        {
            _threadId = GetCurrentThreadId();
            // EVENT_OBJECT_SHOW..HIDE, out of context, excluding this probe's process.
            eventHook = SetWinEventHook(0x8002, 0x8003, IntPtr.Zero, callback, 0, 0, 2);
            if (eventHook == IntPtr.Zero) throw new InvalidOperationException("SetWinEventHook failed.");
            _ready.Set();
            int result;
            while ((result = GetMessage(out var message, IntPtr.Zero, 0, 0)) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
            if (result < 0) throw new InvalidOperationException("The window event message loop failed.");
        }
        catch (Exception error)
        {
            _failure = error;
        }
        finally
        {
            _ready.Set();
            if (eventHook != IntPtr.Zero) UnhookWinEvent(eventHook);
            callbackRoot.Free();
        }
    }
}
