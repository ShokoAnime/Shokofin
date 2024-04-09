
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Shokofin.SignalR;

public class SignalREntryPoint : IHostedService
{
    private readonly SignalRConnectionManager ConnectionManager;

    public SignalREntryPoint(SignalRConnectionManager connectionManager) => ConnectionManager = connectionManager;

    public Task StopAsync(CancellationToken cancellationToken)
        => ConnectionManager.StopAsync();

    public Task StartAsync(CancellationToken cancellationToken)
        => ConnectionManager.RunAsync();
}
