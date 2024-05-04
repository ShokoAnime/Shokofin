
using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Plugins;

namespace Shokofin.SignalR;

public class SignalREntryPoint : IServerEntryPoint
{
    private readonly SignalRConnectionManager ConnectionManager;

    public SignalREntryPoint(SignalRConnectionManager connectionManager) => ConnectionManager = connectionManager;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        ConnectionManager.StopAsync()
            .GetAwaiter()
            .GetResult();
    }

    public Task RunAsync()
        => ConnectionManager.RunAsync();
}