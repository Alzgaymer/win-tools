using System.Collections.Concurrent;
using WinTools.Core.Brightness;

namespace WinTools.Core.Tests.Brightness;

public class BrightnessWorkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Submit_AppliesValue()
    {
        var applied = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var worker = new BrightnessWorker(v => applied.TrySetResult(v));

        worker.Submit(42);

        Assert.Equal(42u, await applied.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Submit_WhileApplyIsSlow_KeepsOnlyLatestValue()
    {
        var applied = new ConcurrentQueue<uint>();
        var firstCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gate = new ManualResetEventSlim(); // not a Task: xUnit1031 forbids blocking on tasks in tests
        var latestApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var worker = new BrightnessWorker(v =>
        {
            if (applied.IsEmpty)
            {
                firstCallStarted.TrySetResult();
                gate.Wait(Timeout);
            }
            applied.Enqueue(v);
            if (v == 50) latestApplied.TrySetResult();
        });

        worker.Submit(1);
        await firstCallStarted.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        for (uint v = 2; v <= 50; v++) worker.Submit(v);
        gate.Set();
        await latestApplied.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(new uint[] { 1, 50 }, applied.ToArray());
    }

    [Fact]
    public async Task Apply_Throws_WorkerKeepsRunning()
    {
        var firstCallDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondApplied = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        await using var worker = new BrightnessWorker(v =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstCallDone.TrySetResult();
                throw new InvalidOperationException("DDC/CI write failed");
            }
            secondApplied.TrySetResult(v);
        });

        worker.Submit(1);
        await firstCallDone.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        worker.Submit(2);

        Assert.Equal(2u, await secondApplied.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Submit_AfterDispose_IsIgnored()
    {
        var applied = new ConcurrentQueue<uint>();
        var worker = new BrightnessWorker(applied.Enqueue);

        await worker.DisposeAsync();
        worker.Submit(7);

        Assert.Empty(applied);
    }
}
