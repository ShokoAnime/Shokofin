
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Shokofin
{
    /// <inheritdoc />
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<Shokofin.API.ShokoAPIClient>();
            serviceCollection.AddSingleton<Shokofin.API.ShokoAPIManager>();
            serviceCollection.AddSingleton<IIdLookup, IdLookup>();
            serviceCollection.AddSingleton<Shokofin.Sync.UserDataSyncManager>();
        }
    }
}