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
public class VersionCheckTask(ILogger<VersionCheckTask> logger, ILibraryManager libraryManager, ShokoAPIClient apiClient) : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILogger<VersionCheckTask> _logger = logger;

    private readonly ILibraryManager _libraryManager = libraryManager;

    private readonly ShokoAPIClient _apiClient = apiClient;

    /// <inheritdoc />
    public string Name => "Check Server Version";

    /// <inheritdoc />
    public string Description => "Responsible for updating the known version of the remote Shoko Server instance at startup and set intervals.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoVersionCheck";

    /// <inheritdoc />
    public bool IsHidden => !Plugin.Instance.Configuration.ExpertMode;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => Plugin.Instance.Configuration.ExpertMode;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [
            new() {
                Type = TaskTriggerInfo.TriggerStartup,
            },
        ];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var updated = false;
        var version = await _apiClient.GetVersion();
        if (version != null && (
            Plugin.Instance.Configuration.ServerVersion == null ||
            !string.Equals(version.ToString(), Plugin.Instance.Configuration.ServerVersion.ToString())
        )) {
            _logger.LogInformation("Found new Shoko Server version; {version}", version);
            Plugin.Instance.Configuration.ServerVersion = version;
            updated = true;
        }

        var mediaFolders = Plugin.Instance.Configuration.MediaFolders.ToList();
        var importFolderNameMap = await Task
            .WhenAll(
                mediaFolders
                    .Select(m => m.ImportFolderId)
                    .Distinct()
                    .Except([0])
                    .Select(id => _apiClient.GetImportFolder(id))
                    .ToList()
            )
            .ContinueWith(task => task.Result.OfType<ImportFolder>().ToDictionary(i => i.Id, i => i.Name))
            .ConfigureAwait(false);
        foreach (var mediaFolderConfig in mediaFolders) {
            if (!importFolderNameMap.TryGetValue(mediaFolderConfig.ImportFolderId, out var importFolderName))
                importFolderName = null;

            if (mediaFolderConfig.LibraryId == Guid.Empty && _libraryManager.GetItemById(mediaFolderConfig.MediaFolderId) is Folder mediaFolder &&
                _libraryManager.GetVirtualFolders().FirstOrDefault(p => p.Locations.Contains(mediaFolder.Path)) is { } library &&
                Guid.TryParse(library.ItemId, out var libraryId)) {
                _logger.LogInformation("Found new library for media folder; {LibraryName} (Library={LibraryId},MediaFolder={MediaFolderPath})", library.Name, libraryId, mediaFolder.Path);
                mediaFolderConfig.LibraryId = libraryId;
                updated = true;
            }

            if (!string.Equals(mediaFolderConfig.ImportFolderName, importFolderName)) {
                _logger.LogInformation("Found new name for import folder; {name} (ImportFolder={ImportFolderId})", importFolderName, mediaFolderConfig.ImportFolderId);
                mediaFolderConfig.ImportFolderName = importFolderName;
                updated = true;
            }
        }
        if (updated) {
            Plugin.Instance.UpdateConfiguration();
        }
    }
}