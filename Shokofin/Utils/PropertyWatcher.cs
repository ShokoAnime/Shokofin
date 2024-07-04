using System;
using System.Threading.Tasks;

namespace Shokofin.Utils;

public class PropertyWatcher<T>
{
    private readonly Func<T> _valueGetter;

    private bool _continueMonitoring;

    public T Value { get; private set; }

    public event EventHandler<T>? ValueChanged;

    public PropertyWatcher(Func<T> valueGetter)
    {
        _valueGetter = valueGetter;
        Value = _valueGetter();
    }

    public void StartMonitoring(int delayInSeconds)
    {
        var delayInMilliseconds = delayInSeconds * 1000;
        _continueMonitoring = true;
        Value = _valueGetter();
        Task.Run(async () => {
            while (_continueMonitoring) {
                await Task.Delay(delayInMilliseconds);
                CheckForChange();
            }
        });
    }

    public void StopMonitoring()
    {
        _continueMonitoring = false;
    }

    private void CheckForChange()
    {
        var currentValue = _valueGetter()!;
        if (!Value!.Equals(currentValue)) {
            ValueChanged?.Invoke(null, currentValue);
            Value = currentValue;
        }
    }
}
