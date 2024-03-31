
#nullable enable
using System.Threading.Tasks;
using MediaBrowser.Controller.Plugins;

namespace Shokofin.SignalR;

public class SignalREntryPoint : IServerEntryPoint
{
    private readonly SignalRConnectionManager ConnectionManager;

    public SignalREntryPoint(SignalRConnectionManager connectionManager) => ConnectionManager = connectionManager;

    public void Dispose()
        => ConnectionManager.Dispose();

    public Task RunAsync()
        => ConnectionManager.RunAsync();
}