using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Shokofin.API.Models;
using Shokofin.Configuration;

#nullable enable
namespace Shokofin;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string MetadataProviderName = "Shoko";

    public override string Name => MetadataProviderName;

    public override Guid Id => Guid.Parse("5216ccbf-d24a-4eb3-8a7e-7da4230b7052");

    /// <summary>
    /// "Virtual" File System Root Directory.
    /// </summary>
    public string VirtualRoot => Path.Combine(DataFolderPath, "VFS");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        ConfigurationChanged += OnConfigChanged;
        IgnoredFileExtensions = this.Configuration.IgnoredFileExtensions.ToHashSet();
        IgnoredFolders = this.Configuration.IgnoredFolders.ToHashSet();
    }

    public void OnConfigChanged(object? sender, BasePluginConfiguration e)
    {
        if (e is not PluginConfiguration config)
            return;
        IgnoredFileExtensions = config.IgnoredFileExtensions.ToHashSet();
        IgnoredFolders = config.IgnoredFolders.ToHashSet();
    }

    public HashSet<string> IgnoredFileExtensions;

    public HashSet<string> IgnoredFolders;

#pragma warning disable 8618
    public static Plugin Instance { get; private set; }
#pragma warning restore 8618

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
            },
        };
    }
}
