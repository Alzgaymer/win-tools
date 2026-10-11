using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinTools.Core.Brightness;
using WinTools.Platform.Brightness;

namespace WinTools.App.Features.Brightness;

public sealed partial class BrightnessPanel : UserControl
{
    // Serializes load/refresh/rebuild/release so a display change never races a refresh.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DispatcherQueueTimer _rebuildTimer;

    public BrightnessPanel()
    {
        InitializeComponent();
        _rebuildTimer = DispatcherQueue.CreateTimer();
        _rebuildTimer.Interval = TimeSpan.FromSeconds(1);
        _rebuildTimer.IsRepeating = false;
        _rebuildTimer.Tick += async (_, _) => await RebuildAsync();
    }

    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];

    public Task LoadAsync() => Serialized(LoadCoreAsync);

    /// <summary>Re-reads brightness from supported monitors without writing anything back.</summary>
    public Task RefreshAsync() => Serialized(async () =>
    {
        var supported = Monitors.Where(m => m.Supported).ToArray();
        var readings = await Task.Run(() => supported.Select(m => PhysicalMonitors.TryGetBrightness(m.Handle)).ToArray());
        for (var i = 0; i < supported.Length; i++)
        {
            if (readings[i] is { } value)
            {
                supported[i].SetFromMonitor(value);
            }
        }
    });

    public Task RebuildAsync() => Serialized(async () =>
    {
        await ReleaseCoreAsync();
        await LoadCoreAsync();
    });

    /// <summary>Debounced rebuild: a burst of display-change messages causes one rebuild 1 s after the last.</summary>
    public void ScheduleRebuild()
    {
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    public Task ReleaseAsync()
    {
        _rebuildTimer.Stop();
        return Serialized(ReleaseCoreAsync);
    }

    private async Task LoadCoreAsync()
    {
        var found = await Task.Run(PhysicalMonitors.Enumerate);
        foreach (var monitor in found)
        {
            var handle = monitor.Handle;
            Monitors.Add(new MonitorViewModel(
                handle, monitor.Name, monitor.Supported, monitor.Brightness,
                value => PhysicalMonitors.SetBrightness(handle, value)));
        }
        EmptyText.Visibility = Monitors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ReleaseCoreAsync()
    {
        var old = Monitors.ToArray();
        Monitors.Clear();
        // Workers must finish their in-flight writes before the handles are destroyed.
        foreach (var monitor in old)
        {
            await monitor.DisposeAsync();
        }
        var handles = old.Select(m => m.Handle).ToArray();
        await Task.Run(() => PhysicalMonitors.Destroy(handles));
    }

    private async Task Serialized(Func<Task> action)
    {
        await _gate.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _gate.Release();
        }
    }
}
