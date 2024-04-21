using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Shokofin.Configuration;

namespace Shokofin;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string MetadataProviderName = "Shoko";

    public override string Name => MetadataProviderName;

    public override Guid Id => Guid.Parse("5216ccbf-d24a-4eb3-8a7e-7da4230b7052");

    /// <summary>
    /// Indicates that we can create symbolic links.
    /// </summary>
    public readonly bool CanCreateSymbolicLinks;

    private readonly ILogger<Plugin> Logger;

    /// <summary>
    /// "Virtual" File System Root Directory.
    /// </summary>
    public readonly string VirtualRoot;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        ConfigurationChanged += OnConfigChanged;
        IgnoredFolders = Configuration.IgnoredFolders.ToHashSet();
        VirtualRoot = Path.Join(applicationPaths.ProgramDataPath, "Shokofin", "VFS");
        Logger = logger;
        CanCreateSymbolicLinks = true;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            var target = Path.Join(Path.GetDirectoryName(VirtualRoot)!, "TestTarget.txt");
            var link = Path.Join(Path.GetDirectoryName(VirtualRoot)!, "TestLink.txt");
            try {
                if (!Directory.Exists(Path.GetDirectoryName(VirtualRoot)!))
                    Directory.CreateDirectory(Path.GetDirectoryName(VirtualRoot)!);
                File.WriteAllText(target, string.Empty);
                File.CreateSymbolicLink(link, target);
            }
            catch {
                CanCreateSymbolicLinks = false;
            }
            finally {
                if (File.Exists(link))
                    File.Delete(link);
                if (File.Exists(target))
                    File.Delete(target);
            }
        }
        Logger.LogDebug("Virtual File System Location; {Path}", VirtualRoot);
        Logger.LogDebug("Can create symbolic links; {Value}", CanCreateSymbolicLinks);
    }

    public void OnConfigChanged(object? sender, BasePluginConfiguration e)
    {
        if (e is not PluginConfiguration config)
            return;
        IgnoredFolders = config.IgnoredFolders.ToHashSet();
    }

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
