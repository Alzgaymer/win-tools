using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinTools.Platform.Shell;

namespace WinTools.App.Shell;

/// <summary>Never placed in a visual tree; ForceCreate puts the icon in the notification area.</summary>
public sealed partial class TrayIcon : UserControl, IDisposable
{
    private readonly XamlUICommand _toggleCommand = new();

    public TrayIcon()
    {
        InitializeComponent();
        _toggleCommand.ExecuteRequested += (_, _) => ToggleRequested?.Invoke();
        Tray.LeftClickCommand = _toggleCommand;
        Tray.ForceCreate(enablesEfficiencyMode: false);
    }

    public event Action? ToggleRequested;

    public event Action? QuitRequested;

    public void ShowNotification(string title, string message) => Tray.ShowNotification(title, message);

    public void Dispose() => Tray.Dispose();

    private void OnMenuOpening(object sender, object e) => StartupItem.IsChecked = StartupRegistration.IsEnabled;

    private void OnStartupClick(object sender, RoutedEventArgs e) => StartupRegistration.IsEnabled = StartupItem.IsChecked;

    private void OnQuitClick(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();
}
