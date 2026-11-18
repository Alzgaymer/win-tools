using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;
using WinTools.Core.Battery;
using WinTools.Platform.Battery;
using WinTools.Platform.Shell;

namespace WinTools.App.Features.Battery;

/// <summary>One notification-area icon per wireless peripheral, showing its battery percentage.</summary>
public sealed class BatteryTrayIcons : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, Entry> _entries = [];
    private readonly DispatcherQueueTimer _timer;
    private readonly XamlUICommand _clickCommand = new();
    private BatteryIconStyle _style = BatteryIconSettings.Style;
    private bool _refreshing;
    private bool _disposed;

    public BatteryTrayIcons(DispatcherQueue dispatcher)
    {
        _clickCommand.ExecuteRequested += (_, _) => ToggleRequested?.Invoke();
        _timer = dispatcher.CreateTimer();
        _timer.Interval = PollInterval;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public event Action? ToggleRequested;

    public BatteryIconStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            BatteryIconSettings.Style = value;
            Redraw();
        }
    }

    public async Task StartAsync()
    {
        _timer.Start();
        await RefreshAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        foreach (var entry in _entries.Values)
        {
            entry.Dispose();
        }
        _entries.Clear();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _disposed)
        {
            return;
        }
        _refreshing = true;
        try
        {
            var devices = await Task.Run(BatteryReader.ReadAllAsync);
            if (!_disposed)
            {
                Apply(devices);
                await PromoteNewIconsAsync();
            }
        }
        catch (Exception)
        {
            // Device I/O failures are transient (unplugged mid-read); the next tick retries.
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Apply(IReadOnlyList<BatteryDevice> devices)
    {
        foreach (var gone in _entries.Keys.Except(devices.Select(d => d.Id)).ToList())
        {
            _entries[gone].Dispose();
            _entries.Remove(gone);
        }

        foreach (var device in devices)
        {
            if (!_entries.TryGetValue(device.Id, out var entry))
            {
                var id = StableGuid(device.Id);
                entry = new Entry(CreateIcon(id), id);
                _entries[device.Id] = entry;
            }

            // A sleeping mouse or a headset that is off still has its receiver plugged in:
            // keep the last reading rather than flicker to "unknown".
            entry.Stale = device.Percent is null;
            entry.Percent = device.Percent ?? entry.Percent;
            entry.Device = device;
        }
        Redraw();
    }

    /// <summary>Re-renders icons whose content, style, taskbar theme or DPI changed since they were drawn.</summary>
    private void Redraw()
    {
        var palette = BatteryIconRenderer.CurrentPalette();
        var size = BatteryIconRenderer.CurrentSize();
        foreach (var entry in _entries.Values)
        {
            if (entry.Device is not { } device)
            {
                continue;
            }
            var state = new DrawnState(entry.Percent, device.IsCharging, entry.Stale, device.Name, _style, palette, size);
            if (entry.Drawn == state)
            {
                continue;
            }
            entry.Drawn = state;
            entry.Icon.ToolTipText = ToolTip(device, entry.Percent, entry.Stale);
            entry.SetImage(BatteryIconRenderer.Render(device.Kind, entry.Percent, device.IsCharging, entry.Stale, _style, palette, size));
        }
    }

    /// <summary>Explorer registers a new icon's settings shortly after it appears, so retry briefly.</summary>
    private async Task PromoteNewIconsAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var pending = _entries.Values.Where(e => !e.Promoted).ToList();
            if (pending.Count == 0 || _disposed)
            {
                return;
            }
            foreach (var entry in pending)
            {
                entry.Promoted = TrayIconPromotion.PromoteUnlessUserChose(entry.Id);
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private TaskbarIcon CreateIcon(Guid id)
    {
        var icon = new TaskbarIcon
        {
            Id = id,
            NoLeftClickDelay = true,
            LeftClickCommand = _clickCommand,
        };
        icon.ForceCreate(enablesEfficiencyMode: false);
        return icon;
    }

    private static string ToolTip(BatteryDevice device, int? percent, bool stale)
    {
        if (percent is null)
        {
            return $"{device.Name}: not responding";
        }
        var suffix = device.IsCharging ? " (charging)" : stale ? " (not responding, last known)" : "";
        return $"{device.Name}: {percent}%{suffix}";
    }

    /// <summary>
    /// Windows remembers per-icon settings (e.g. "always show") by GUID, and binds the GUID to the
    /// executable path, so the GUID must be stable per device and differ between install locations.
    /// </summary>
    private static Guid StableGuid(string deviceId)
    {
        var seed = $"WinTools.Battery|{deviceId}|{Environment.ProcessPath}".ToUpperInvariant();
        return new Guid(MD5.HashData(Encoding.UTF8.GetBytes(seed)));
    }

    private sealed record DrawnState(
        int? Percent, bool Charging, bool Stale, string Name, BatteryIconStyle Style, BatteryIconRenderer.Palette Palette, int Size);

    private sealed class Entry(TaskbarIcon icon, Guid id)
    {
        public TaskbarIcon Icon { get; } = icon;

        public Guid Id { get; } = id;

        public bool Promoted { get; set; }

        public BatteryDevice? Device { get; set; }

        public int? Percent { get; set; }

        public bool Stale { get; set; }

        public DrawnState? Drawn { get; set; }

        /// <summary>The tray copies the icon on assignment, so the previous HICON can be released.</summary>
        public void SetImage(Icon image)
        {
            var previous = Icon.Icon;
            Icon.Icon = image;
            previous?.Dispose();
        }

        public void Dispose()
        {
            var image = Icon.Icon;
            Icon.Dispose();
            image?.Dispose();
        }
    }
}
