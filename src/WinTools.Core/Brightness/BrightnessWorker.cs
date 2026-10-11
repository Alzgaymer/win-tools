using System.Threading.Channels;

namespace WinTools.Core.Brightness;

/// <summary>
/// Applies brightness values on a background task. While a slow DDC/CI write is in
/// progress only the most recent submitted value is kept, so dragging a slider never
/// queues up a backlog of stale writes.
/// </summary>
public sealed class BrightnessWorker : IAsyncDisposable
{
    private readonly Channel<uint> _values = Channel.CreateBounded<uint>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private readonly Action<uint> _apply;
    private readonly Task _loop;

    public BrightnessWorker(Action<uint> apply)
    {
        _apply = apply;
        _loop = Task.Run(RunAsync);
    }

    public void Submit(uint value) => _values.Writer.TryWrite(value);

    public async ValueTask DisposeAsync()
    {
        _values.Writer.TryComplete();
        await _loop.ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        await foreach (var value in _values.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                _apply(value);
            }
            catch
            {
                // A failed DDC/CI write is not fatal; the next submitted value retries.
            }
        }
    }
}
