using System.Collections.Concurrent;
using System.ComponentModel;
using WinTools.Core.Brightness;

namespace WinTools.Core.Tests.Brightness;

public class MonitorViewModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SetBrightness_FromUser_SubmitsToMonitor()
    {
        var applied = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var vm = new MonitorViewModel(1, "DELL", supported: true, brightness: 30, v => applied.TrySetResult(v));

        vm.Brightness = 70;

        Assert.Equal(70u, await applied.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.Equal("70%", vm.StatusText);
    }

    [Fact]
    public async Task SetFromMonitor_DoesNotWriteBrightness()
    {
        var applied = new ConcurrentQueue<uint>();
        var vm = new MonitorViewModel(1, "DELL", supported: true, brightness: 30, applied.Enqueue);

        vm.SetFromMonitor(55);
        await vm.DisposeAsync(); // drains the worker, so any submitted value would have been applied

        Assert.Empty(applied);
        Assert.Equal(55d, vm.Brightness);
        Assert.Equal("55%", vm.StatusText);
    }

    [Fact]
    public async Task SetFromMonitor_RaisesPropertyChanged()
    {
        await using var vm = new MonitorViewModel(1, "DELL", supported: true, brightness: 30, _ => { });
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SetFromMonitor(40);

        Assert.Contains(nameof(MonitorViewModel.Brightness), changed);
        Assert.Contains(nameof(MonitorViewModel.StatusText), changed);
    }

    [Theory]
    [InlineData(0u, 1d)]
    [InlineData(250u, 100d)]
    [InlineData(100u, 100d)]
    public async Task Brightness_IsClampedTo1Through100(uint reading, double expected)
    {
        await using var vm = new MonitorViewModel(1, "DELL", supported: true, brightness: reading, _ => { });

        Assert.Equal(expected, vm.Brightness);
    }

    [Fact]
    public async Task SetBrightness_SameRoundedValue_DoesNotSubmitOrNotify()
    {
        var applied = new ConcurrentQueue<uint>();
        var vm = new MonitorViewModel(1, "DELL", supported: true, brightness: 30, applied.Enqueue);
        var notified = false;
        vm.PropertyChanged += (_, _) => notified = true;

        vm.Brightness = 30.2;
        await vm.DisposeAsync();

        Assert.False(notified);
        Assert.Empty(applied);
    }

    [Fact]
    public async Task Unsupported_ShowsNotSupported_AndIgnoresUserInput()
    {
        var applied = new ConcurrentQueue<uint>();
        var vm = new MonitorViewModel(1, "Monitor 2", supported: false, brightness: 0, applied.Enqueue);

        vm.Brightness = 80;
        await vm.DisposeAsync();

        Assert.Equal("DDC/CI not supported", vm.StatusText);
        Assert.Empty(applied);
    }
}
