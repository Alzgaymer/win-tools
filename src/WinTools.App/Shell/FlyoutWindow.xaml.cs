using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;
using WinTools.Core.Shell;
using WinTools.Platform.Shell;

namespace WinTools.App.Shell;

public sealed partial class FlyoutWindow : Window
{
    // Used until Body has been laid out once (first show).
    private const double FallbackContentHeightDip = 160;

    private readonly ReopenGuard _reopenGuard = new(TimeProvider.System);
    private bool _closingForQuit;

    public FlyoutWindow()
    {
        InitializeComponent();
        Hwnd = WindowNative.GetWindowHandle(this);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += OnClosing;
        Activated += OnActivated;
        Body.SizeChanged += (_, _) => PlaceOnScreen();

        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
        }
        else
        {
            Root.Background = (Brush)Application.Current.Resources["WinToolsFallbackBackgroundBrush"];
        }
    }

    public nint Hwnd { get; }

    public void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            HideFlyout();
        }
        else if (!_reopenGuard.ShouldSuppressShow())
        {
            ShowFlyout();
        }
    }

    public void ShowFlyout()
    {
        PlaceOnScreen();
        AppWindow.Show();
        Activate();
        WindowFocus.BringToFront(Hwnd);
        _ = BrightnessSection.RefreshAsync();
    }

    public void HideFlyout() => AppWindow.Hide();

    public void CloseForQuit()
    {
        _closingForQuit = true;
        Close();
    }

    private void PlaceOnScreen()
    {
        var area = DisplayArea.Primary.WorkArea;
        var contentHeight = Body.ActualHeight > 0 ? Body.ActualHeight : FallbackContentHeightDip;
        var rect = FlyoutPlacement.BottomRight(
            new PixelRect(area.X, area.Y, area.Width, area.Height),
            DisplayScale.ForPrimaryMonitor(),
            contentHeight);
        AppWindow.MoveAndResize(new RectInt32(rect.X, rect.Y, rect.Width, rect.Height));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && AppWindow.IsVisible)
        {
            _reopenGuard.NoteAutoHidden();
            HideFlyout();
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closingForQuit)
        {
            return;
        }
        args.Cancel = true;
        HideFlyout();
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        HideFlyout();
    }
}
