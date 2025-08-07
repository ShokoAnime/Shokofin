using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;

namespace Shokofin.Tasks;

/// <summary>
/// Responsible for updating the known version of the remote Shoko Server
/// instance at startup and set intervals.
/// </summary>
public class VersionCheckTask(ILogger<VersionCheckTask> _logger, ILibraryManager _libraryManager, ShokoApiClient _apiClient) : IScheduledTask, IConfigurableScheduledTask {
    /// <inheritdoc />
    public string Name => "Check Server Version";

    /// <inheritdoc />
    public string Description => "Responsible for updating the known version of the remote Shoko Server instance at startup and set intervals.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoVersionCheck";

    /// <inheritdoc />
    public bool IsHidden => !Plugin.Instance.Configuration.AdvancedMode;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => Plugin.Instance.Configuration.AdvancedMode;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [
            new() {
                Type = TaskTriggerInfo.TriggerStartup,
            },
        ];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) {
        try {
            var updated = false;
            var version = await _apiClient.GetVersion().ConfigureAwait(false);
            if (version != null && (
                Plugin.Instance.Configuration.ServerVersion == null ||
                !string.Equals(version.ToString(), Plugin.Instance.Configuration.ServerVersion.ToString())
            )) {
                _logger.LogDebug("Found new Shoko Server version; {version}", version);
                Plugin.Instance.Configuration.ServerVersion = version;
                updated = true;
            }

            if (string.IsNullOrEmpty(Plugin.Instance.Configuration.ApiKey))
                return;


            var prefix = await _apiClient.GetWebPrefix().ConfigureAwait(false);
            if (prefix != null && (
                Plugin.Instance.Configuration.WebPrefix == null ||
                !string.Equals(prefix, Plugin.Instance.Configuration.WebPrefix)
            )) {
                _logger.LogDebug("Found new Shoko Server web prefix; {prefix}", prefix);
                Plugin.Instance.Configuration.WebPrefix = prefix;
                updated = true;
            }

            var hasPluginsExposed = await _apiClient.CheckIfPluginsExposed(cancellationToken).ConfigureAwait(false);
            if (Plugin.Instance.Configuration.HasPluginsExposed != hasPluginsExposed) {
                _logger.LogDebug("Plugin based API; {hasPluginsExposed}", hasPluginsExposed);
                Plugin.Instance.Configuration.HasPluginsExposed = hasPluginsExposed;
                updated = true;
            }

            var mediaFolders = Plugin.Instance.Configuration.MediaFolders.ToList();
            var managedFolderNameMap = await Task
                .WhenAll(
                    mediaFolders
                        .Select(m => m.ManagedFolderId)
                        .Distinct()
                        .Except([0, -1])
                        .Select(id => _apiClient.GetManagedFolder(id))
                        .ToList()
                )
                .ContinueWith(task => task.Result.OfType<ManagedFolder>().ToDictionary(i => i.Id, i => i.Name))
                .ConfigureAwait(false);
            foreach (var mediaFolderConfig in mediaFolders) {
                if (mediaFolderConfig.IsVirtualRoot)
                    continue;

                if (!managedFolderNameMap.TryGetValue(mediaFolderConfig.ManagedFolderId, out var managedFolderName))
                    managedFolderName = null;

                if (Guid.Empty == mediaFolderConfig.LibraryId && _libraryManager.GetItemById(mediaFolderConfig.MediaFolderId) is Folder mediaFolder &&
                    _libraryManager.GetVirtualFolders().FirstOrDefault(p => p.Locations.Contains(mediaFolder.Path)) is { } library &&
                    Guid.TryParse(library.ItemId, out var libraryId)) {
                    _logger.LogDebug("Found new library for media folder; {LibraryName} (Library={LibraryId},MediaFolder={MediaFolderPath})", library.Name, libraryId, mediaFolder.Path);
                    mediaFolderConfig.LibraryId = libraryId;
                    updated = true;
                }

                if (!string.IsNullOrEmpty(managedFolderName) && !string.Equals(mediaFolderConfig.ManagedFolderName, managedFolderName)) {
                    _logger.LogDebug("Found new name for managed folder; {name} (ManagedFolder={ManagedFolderId})", managedFolderName, mediaFolderConfig.ManagedFolderId);
                    mediaFolderConfig.ManagedFolderName = managedFolderName;
                    updated = true;
                }
            }
            if (updated) {
                Plugin.Instance.UpdateConfiguration();
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error while checking Shoko Server version.");
        }
    }
}
