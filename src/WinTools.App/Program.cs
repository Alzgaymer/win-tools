using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace WinTools.App;

public static class Program
{
    private const string InstanceKey = "WinTools";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectedToRunningInstance())
        {
            return 0;
        }

        Application.Start(callbackParams =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(startHidden: args.Contains("--startup", StringComparer.OrdinalIgnoreCase));
        });
        return 0;
    }

    /// <summary>
    /// If WinTools is already running, hands this launch to it (it shows its flyout) and returns true.
    /// </summary>
    private static bool RedirectedToRunningInstance()
    {
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (main.IsCurrent)
        {
            return false;
        }
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        // The redirect must not run on the STA thread; do it on the thread pool and wait.
        Task.Run(() => main.RedirectActivationToAsync(args).AsTask()).GetAwaiter().GetResult();
        return true;
    }
}
