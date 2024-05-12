using System;
using System.Threading.Tasks;

namespace Shokofin.Utils;

public class PropertyWatcher<T>
{
    private readonly Func<T> _valueGetter;

    private bool _continueMonitoring;

    public T LastKnownValue { get; private set; }

    public event EventHandler<T> OnValueChanged;

    public PropertyWatcher(Func<T> valueGetter)
    {
        _valueGetter = valueGetter;
        LastKnownValue = _valueGetter();
    }

    public void StartMonitoring(int delayInMilliseconds)
    {
        _continueMonitoring = true;
        LastKnownValue = _valueGetter();
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
        if (!LastKnownValue!.Equals(currentValue)) {
            OnValueChanged?.Invoke(null, currentValue);
            LastKnownValue = currentValue;
        }
    }
}
