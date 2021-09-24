using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Shokofin.Configuration;

namespace Shokofin
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static string MetadataProviderName = "Shoko";

        public override string Name => "Shoko";

        public override Guid Id => Guid.Parse("5216ccbf-d24a-4eb3-8a7e-7da4230b7052");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
                },
                new PluginPageInfo
                {
                    Name = "ShokoController.js",
                    EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configController.js",
                }
            };
        }
    }
}
