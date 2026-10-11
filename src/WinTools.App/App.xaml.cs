using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinTools.App.Shell;
using WinTools.Platform.Shell;

namespace WinTools.App;

public partial class App : Application
{
    private FlyoutWindow? _flyout;
    private TrayIcon? _tray;
    private HotkeyListener? _hotkeys;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var flyout = _flyout = new FlyoutWindow();

        _tray = new TrayIcon();
        _tray.ToggleRequested += flyout.Toggle;
        _tray.QuitRequested += Quit;

        _hotkeys = new HotkeyListener(flyout.Hwnd);
        _hotkeys.Pressed += flyout.Toggle;
        _hotkeys.DisplayChanged += flyout.BrightnessSection.ScheduleRebuild;
        if (!_hotkeys.IsRegistered)
        {
            _tray.ShowNotification("WinTools", "Alt+B is in use by another app. Use the tray icon instead.");
        }

        // A second launch (e.g. from the Start menu) is redirected here by Program.Main.
        AppInstance.GetCurrent().Activated += (_, _) => flyout.DispatcherQueue.TryEnqueue(flyout.ShowFlyout);

        await flyout.BrightnessSection.LoadAsync();
    }

    private async void Quit()
    {
        _hotkeys?.Dispose();
        if (_flyout is not null)
        {
            await _flyout.BrightnessSection.ReleaseAsync();
        }
        _tray?.Dispose();
        _flyout?.CloseForQuit();
        Exit();
    }
}
