using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace WinTools.App.Shell;

public sealed partial class FlyoutWindow : Window
{
    public FlyoutWindow()
    {
        InitializeComponent();
        Hwnd = WindowNative.GetWindowHandle(this);
    }

    public nint Hwnd { get; }
}
