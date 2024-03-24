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
using Sentry;

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
            if (SentryReference == null && SentryConfiguration.DSN.StartsWith("https://")) {
                SentryReference = SentrySdk.Init(options => {
                    var release = Assembly.GetAssembly(typeof(Plugin))?.GetName().Version?.ToString() ?? "1.0.0.0";
                    var environment = release.EndsWith(".0") ? "stable" : "dev";

                    // Cut off the build number for stable releases.
                    if (environment == "stable")
                        release = release[..^2];

                    // Assign the DSN key and release version.
                    options.Dsn = SentryConfiguration.DSN;
                    options.Environment = environment;
                    options.Release = release;
                    options.AutoSessionTracking = false;

                    // Additional tags for easier filtering in Sentry.
                    var jellyfinRelease = Assembly.GetAssembly(typeof(Jellyfin.Data.Entities.Preference))?.GetName().Version?.ToString() ?? "0.0.0.0";
                    options.DefaultTags.Add("jellyfin.release", jellyfinRelease);

                    // Disable auto-exception captures.
                    options.DisableUnobservedTaskExceptionCapture();
                    options.DisableAppDomainUnhandledExceptionCapture();
                    options.CaptureFailedRequests = false;

                    // Filter exceptions.
                    options.AddExceptionFilter(new SentryExceptionFilter(ex =>
                    {
                        switch (ex) {
                            // Ignore any and all http request exceptions and
                            // task cancellation exceptions.
                            case TaskCanceledException:
                            case HttpRequestException:
                                return true;

                            case ApiException apiEx:
                                // Server is not ready to accept requests yet.
                                if (ex.Message.Contains("The Server is not running."))
                                    return true;
                                break;
                        }

                        // If we need more filtering in the future then add them
                        // above this comment.

                        return false;
                    }));
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
            },
        };
    }

    /// <summary>
    /// An IException filter class to convert a function to a filter. It's weird
    /// they don't have a method that just accepts a pure function and converts
    /// it internally, but oh well. ¯\_(ツ)_/¯
    /// </summary>
    private class SentryExceptionFilter : Sentry.Extensibility.IExceptionFilter
    {
        private Func<Exception, bool> _action;

        public SentryExceptionFilter(Func<Exception, bool> action)
        {
            _action = action;
        }

        public bool Filter(Exception ex) =>
            _action(ex);
    }
}
