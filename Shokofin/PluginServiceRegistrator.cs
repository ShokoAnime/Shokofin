using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Shokofin;

/// <inheritdoc />
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<Utils.LibraryScanWatcher>();
        serviceCollection.AddSingleton<API.ShokoAPIClient>();
        serviceCollection.AddSingleton<API.ShokoAPIManager>();
        serviceCollection.AddSingleton<Configuration.MediaFolderConfigurationService>();
        serviceCollection.AddSingleton<IIdLookup, IdLookup>();
        serviceCollection.AddSingleton<Sync.UserDataSyncManager>();
        serviceCollection.AddSingleton<MergeVersions.MergeVersionsManager>();
        serviceCollection.AddSingleton<Collections.CollectionManager>();
        serviceCollection.AddSingleton<Resolvers.VirtualFileSystemService>();
        serviceCollection.AddSingleton<Events.EventDispatchService>();
        serviceCollection.AddSingleton<SignalR.SignalRConnectionManager>();
        serviceCollection.AddHostedService<SignalR.SignalREntryPoint>();
        serviceCollection.AddControllers(options => options.Filters.Add<Web.ImageHostUrl>());
    }
}
