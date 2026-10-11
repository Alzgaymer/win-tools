using Microsoft.UI.Xaml;
using WinTools.App.Shell;

namespace WinTools.App;

public partial class App : Application
{
    private FlyoutWindow? _flyout;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _flyout = new FlyoutWindow();
        _flyout.Activate();
        await _flyout.BrightnessSection.LoadAsync();
    }
}
