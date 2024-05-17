
using System;

namespace Shokofin.Utils;

public class DisposableAction : IDisposable
{
    private readonly Action DisposeAction;

    public DisposableAction(Action disposeAction)
    {
        DisposeAction = disposeAction;
    }

    public void Dispose()
        => DisposeAction();
}