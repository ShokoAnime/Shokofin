using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Shokofin.Configuration;
using Sentry;
using System.Reflection;

#nullable enable
namespace Shokofin;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static string MetadataProviderName = "Shoko";

    public override string Name => "Shoko";

    public override Guid Id => Guid.Parse("5216ccbf-d24a-4eb3-8a7e-7da4230b7052");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        ConfigurationChanged += OnConfigChanged;
        RefreshSentry();
        IgnoredFileExtensions = this.Configuration.IgnoredFileExtensions.ToHashSet();
        IgnoredFolders = this.Configuration.IgnoredFolders.ToHashSet();
    }

    ~Plugin()
    {
        if (SentryReference != null) {
            SentrySdk.EndSession();
            SentryReference.Dispose();
            SentryReference = null;
        }
    }

    public void OnConfigChanged(object? sender, BasePluginConfiguration e)
    {
        if (!(e is PluginConfiguration config))
            return;
        RefreshSentry();
        IgnoredFileExtensions = config.IgnoredFileExtensions.ToHashSet();
        IgnoredFolders = config.IgnoredFolders.ToHashSet();
    }

    private void RefreshSentry()
    {
        if (IsSentryEnabled) {
            if (SentryReference != null && SentryConfiguration.DSN.StartsWith("https://")) {
                SentryReference = SentrySdk.Init(options => {
                    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
                    var environment = version.EndsWith(".0") ? "stable" : "dev";
                    var release = string.Join(".", version.Split(".").Take(3));
                    var revision = version.Split(".").Last();

                    // Assign the DSN key and release version.
                    options.Dsn = SentryConfiguration.DSN;
                    options.Environment = environment;
                    options.Release = release;
                    options.AutoSessionTracking = false;
                    
                    // Add the dev revision if we're not on stable.
                    if (environment != "stable")
                        options.DefaultTags.Add("release.revision", revision);
                });

                SentrySdk.StartSession();
            }
        }
        else {
            if (SentryReference != null) 
            {
                SentrySdk.EndSession();
                SentryReference.Dispose();
                SentryReference = null;
            }
        }
    }

    public bool IsSentryEnabled
    {
        get => Configuration.SentryEnabled ?? true;
    }

    public void CaptureException(Exception ex)
    {
        if (SentryReference == null)
            return;

        SentrySdk.CaptureException(ex);
    }

    private IDisposable? SentryReference { get; set; }

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
            }
        };
    }
}
