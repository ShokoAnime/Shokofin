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
    private string? _virtualRoot;

    /// <summary>
    /// "Virtual" File System Root Directory.
    /// </summary>
    public string VirtualRoot
    {
        get
        {
            var virtualRoot = _virtualRoot ??= Configuration.VFS_Location switch {
                VirtualRootLocation.Custom => VirtualRoot_Custom ?? VirtualRoot_Default,
                VirtualRootLocation.Cache => VirtualRoot_Cache,
                VirtualRootLocation.Default or _ => VirtualRoot_Default,
            };
            if (!Directory.Exists(virtualRoot))
                Directory.CreateDirectory(virtualRoot);

            return virtualRoot;
        }
    }

    public string VirtualRoot_Default => Path.Join(ApplicationPaths.ProgramDataPath, Name);
    
    public string VirtualRoot_Cache => Path.Join(ApplicationPaths.CachePath, Name);

    public string? VirtualRoot_Custom => string.IsNullOrWhiteSpace(Configuration.VFS_CustomLocation) ? null : Path.Combine(ApplicationPaths.ProgramDataPath, Configuration.VFS_CustomLocation);

    /// <summary>
    /// Gets or sets the event handler that is triggered when this configuration changes.
    /// </summary>
    public new event EventHandler<PluginConfiguration>? ConfigurationChanged;

    public Plugin(UsageTracker usageTracker, IServerConfigurationManager configurationManager, IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger) : base(applicationPaths, xmlSerializer)
    {
        var configExists = File.Exists(ConfigurationFilePath);
        _configurationManager = configurationManager;
        Tracker = usageTracker;
        Logger = logger;
        CanCreateSymbolicLinks = true;
        Instance = this;

        base.ConfigurationChanged += OnConfigChanged;

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

        FixupConfiguration(Configuration);

        IgnoredFolders = Configuration.IgnoredFolders.ToHashSet();
        Tracker.UpdateTimeout(TimeSpan.FromSeconds(Configuration.UsageTracker_StalledTimeInSeconds));

        Logger.LogDebug("Virtual File System Root Directory; {Path}", VirtualRoot);
        Logger.LogDebug("Can create symbolic links; {Value}", CanCreateSymbolicLinks);

        // Disable VFS if we can't create symbolic links on Windows and no configuration exists.
        if (!configExists && !CanCreateSymbolicLinks) {
            Configuration.VFS_Enabled = false;
            SaveConfiguration();
        }
    }

    public void UpdateConfiguration()
    {
        UpdateConfiguration(this.Configuration);
    }

    public void OnConfigChanged(object? sender, BasePluginConfiguration e)
    {
        if (e is not PluginConfiguration config)
            return;

        FixupConfiguration(config);

        IgnoredFolders = config.IgnoredFolders.ToHashSet();
        Tracker.UpdateTimeout(TimeSpan.FromSeconds(config.UsageTracker_StalledTimeInSeconds));

        // Reset the cached VFS root directory in case it has changed.
        _virtualRoot = null;

        ConfigurationChanged?.Invoke(sender, config);
    }

    public void FixupConfiguration(PluginConfiguration config)
    {
        // Fix-up faulty configuration.
        var changed = false;
        if (string.IsNullOrWhiteSpace(config.VFS_CustomLocation) && config.VFS_CustomLocation is not null) {
            config.VFS_CustomLocation = null;
            changed = true;
        }
        if (config.DescriptionSourceOrder.Length != Enum.GetValues<Text.DescriptionProvider>().Length) {
            var current = config.DescriptionSourceOrder;
            config.DescriptionSourceOrder = Enum.GetValues<Text.DescriptionProvider>()
                .OrderBy(x => Array.IndexOf(current, x) == -1 ? int.MaxValue : Array.IndexOf(current, x))
                .ToArray();
            changed = true;
        }
        if (changed)
            SaveConfiguration(config);
    }

    public HashSet<string> IgnoredFolders;

#pragma warning disable 8618
    public static Plugin Instance { get; private set; }
#pragma warning restore 8618

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            // HTML
            new PluginPageInfo
            {
                Name = "Shoko.Settings",
                EmbeddedResourcePath = $"{GetType().Namespace}.Pages.Settings.html",
                EnableInMainMenu = Configuration.Misc_ShowInMenu,
                DisplayName = "Shoko - Settings",
                MenuSection = "Shoko",
            },
            new PluginPageInfo
            {
                Name = "Shoko.Utilities.Dummy",
                EmbeddedResourcePath = $"{GetType().Namespace}.Pages.Dummy.html",
                DisplayName = "Shoko - Dummy",
                MenuSection = "Shoko",
            },

            // JS
            new PluginPageInfo
            {
                Name = "Shoko.Common.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Pages.Scripts.Common.js",
            },
            new PluginPageInfo
            {
                Name = "Shoko.Settings.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Pages.Scripts.Settings.js",
            },
            new PluginPageInfo
            {
                Name = "Shoko.Utilities.Dummy.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Pages.Scripts.Dummy.js",
            },
        ];
    }
}
