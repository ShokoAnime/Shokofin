using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Shokofin.Configuration;
using Shokofin.Utils;

namespace Shokofin;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private static TimeSpan BaseUrlUpdateDelay => TimeSpan.FromMinutes(15);

    private readonly IServerConfigurationManager _configurationManager;

    private readonly ILogger<Plugin> Logger;

    /// <summary>
    /// The last time the base URL and base path was updated.
    /// </summary>
    private DateTime? LastBaseUrlUpdate = null;

    /// <summary>
    /// Cached base URL of the Jellyfin server, to avoid calculating it all the
    /// time.
    /// </summary>
    private string? CachedBaseUrl = null;

    /// <summary>
    /// Base URL where the Jellyfin server is running.
    /// </summary>
    public string BaseUrl
    {
        get
        {
            if (CachedBaseUrl is not null && LastBaseUrlUpdate is not null && DateTime.Now - LastBaseUrlUpdate < BaseUrlUpdateDelay)
                return CachedBaseUrl;

            lock(this) {
                LastBaseUrlUpdate = DateTime.Now;
                if (_configurationManager.GetNetworkConfiguration() is not { } networkOptions)
                {
                    CachedBaseUrl = "http://localhost:8096/";
                    CachedBasePath = string.Empty;
                    return CachedBaseUrl;
                }

                var protocol = networkOptions.RequireHttps && networkOptions.EnableHttps ? "https" : "http";
                var hostname = networkOptions.LocalNetworkAddresses.FirstOrDefault() is { } address && address is not "0.0.0.0" and not "::" ? address : "localhost";
                var port = networkOptions.RequireHttps && networkOptions.EnableHttps ? networkOptions.InternalHttpsPort : networkOptions.InternalHttpPort;
                var basePath = networkOptions.BaseUrl is { } baseUrl ? baseUrl : string.Empty;
                if (basePath.Length > 0 && basePath[0] == '/')
                    basePath = basePath[1..];
                CachedBaseUrl = new UriBuilder(protocol, hostname, port).ToString();
                CachedBasePath = basePath;
                return CachedBaseUrl;
            }
        }
    }

    /// <summary>
    /// Cached base path of the Jellyfin server, to avoid calculating it all the
    /// time.
    /// </summary>
    private string? CachedBasePath = null;

    /// <summary>
    /// Base path where the Jellyfin server is running on the domain.
    /// </summary>
    public string BasePath
    {
        get
        {
            if (CachedBasePath is not null && LastBaseUrlUpdate is not null && DateTime.Now - LastBaseUrlUpdate < BaseUrlUpdateDelay)
                return CachedBasePath;

            lock(this) {
                LastBaseUrlUpdate = DateTime.Now;
                if (_configurationManager.GetNetworkConfiguration() is not { } networkOptions)
                {
                    CachedBaseUrl = "http://localhost:8096/";
                    CachedBasePath = string.Empty;
                    return CachedBaseUrl;
                }

                var protocol = networkOptions.RequireHttps && networkOptions.EnableHttps ? "https" : "http";
                var hostname = networkOptions.LocalNetworkAddresses.FirstOrDefault() is { } address && address is not "0.0.0.0" and not "::" ? address : "localhost";
                var port = networkOptions.RequireHttps && networkOptions.EnableHttps ? networkOptions.InternalHttpsPort : networkOptions.InternalHttpPort;
                var basePath = networkOptions.BaseUrl is { } baseUrl ? baseUrl : string.Empty;
                if (basePath.Length > 0 && basePath[0] == '/')
                    basePath = basePath[1..];
                CachedBaseUrl = new UriBuilder(protocol, hostname, port).ToString();
                CachedBasePath = basePath;
                return CachedBasePath;
            }
        }
    }

    public const string MetadataProviderName = "Shoko";

    public override string Name => MetadataProviderName;

    public override Guid Id => Guid.Parse("5216ccbf-d24a-4eb3-8a7e-7da4230b7052");

    /// <summary>
    /// Indicates that we can create symbolic links.
    /// </summary>
    public readonly bool CanCreateSymbolicLinks;

    /// <summary>
    /// Usage tracker for automagically clearing the caches when nothing is using them.
    /// </summary>
    public readonly UsageTracker Tracker;

    /// <summary>
    /// "Virtual" File System Root Directory.
    /// </summary>
    public readonly string VirtualRoot;

    /// <summary>
    /// Gets or sets the event handler that is triggered when this configuration changes.
    /// </summary>
    public new event EventHandler<PluginConfiguration>? ConfigurationChanged;

    public Plugin(UsageTracker usageTracker, IServerConfigurationManager configurationManager, IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger) : base(applicationPaths, xmlSerializer)
    {
        _configurationManager = configurationManager;
        Instance = this;
        base.ConfigurationChanged += OnConfigChanged;
        VirtualRoot = Path.Join(applicationPaths.ProgramDataPath, "Shokofin", "VFS");
        Tracker = usageTracker;
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
        IgnoredFolders = Configuration.IgnoredFolders.ToHashSet();
        Tracker.UpdateTimeout(TimeSpan.FromSeconds(Configuration.UsageTracker_StalledTimeInSeconds));
        Logger.LogDebug("Virtual File System Location; {Path}", VirtualRoot);
        Logger.LogDebug("Can create symbolic links; {Value}", CanCreateSymbolicLinks);
    }

    public void UpdateConfiguration()
    {
        UpdateConfiguration(this.Configuration);
    }

    public void OnConfigChanged(object? sender, BasePluginConfiguration e)
    {
        if (e is not PluginConfiguration config)
            return;
        IgnoredFolders = config.IgnoredFolders.ToHashSet();
        Tracker.UpdateTimeout(TimeSpan.FromSeconds(Configuration.UsageTracker_StalledTimeInSeconds));
        ConfigurationChanged?.Invoke(sender, config);
    }

    public HashSet<string> IgnoredFolders;

#pragma warning disable 8618
    public static Plugin Instance { get; private set; }
#pragma warning restore 8618

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
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
        ];
    }
}
