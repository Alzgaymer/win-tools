using System.ComponentModel;

namespace WinTools.Core.Brightness;

/// <summary>
/// Bindable state for one physical monitor. Only user changes (the <see cref="Brightness"/>
/// setter) are written to the monitor; readings go through <see cref="SetFromMonitor"/>.
/// </summary>
public sealed class MonitorViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly PropertyChangedEventArgs BrightnessChanged = new(nameof(Brightness));
    private static readonly PropertyChangedEventArgs StatusTextChanged = new(nameof(StatusText));

    private readonly BrightnessWorker? _worker;
    private uint _brightness;

    public MonitorViewModel(nint handle, string name, bool supported, uint brightness, Action<uint> apply)
    {
        Handle = handle;
        Name = name;
        Supported = supported;
        _brightness = Clamp(brightness);
        _worker = supported ? new BrightnessWorker(apply) : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public nint Handle { get; }

    public string Name { get; }

    public bool Supported { get; }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (_worker is null || !Update(Clamp(value)))
            {
                return;
            }
            _worker.Submit(_brightness);
        }
    }

    public string StatusText => Supported ? $"{_brightness}%" : "DDC/CI not supported";

    public void SetFromMonitor(uint value) => Update(Clamp(value));

    public ValueTask DisposeAsync() => _worker?.DisposeAsync() ?? ValueTask.CompletedTask;

    private bool Update(uint value)
    {
        if (value == _brightness)
        {
            return false;
        }
        _brightness = value;
        PropertyChanged?.Invoke(this, BrightnessChanged);
        PropertyChanged?.Invoke(this, StatusTextChanged);
        return true;
    }

    private static uint Clamp(double value) => (uint)Math.Clamp(Math.Round(value), 1, 100);
}
