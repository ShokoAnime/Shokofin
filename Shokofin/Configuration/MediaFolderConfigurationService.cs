using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emby.Naming.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration.Models;

namespace Shokofin.Configuration;

public class MediaFolderConfigurationService
{
    private readonly ILogger<MediaFolderConfigurationService> Logger;

    private readonly ILibraryManager LibraryManager;

    private readonly IFileSystem FileSystem;

    private readonly ShokoAPIClient ApiClient;

    private readonly NamingOptions NamingOptions;

    private readonly Dictionary<Guid, string> MediaFolderChangeKeys = new();

    public event EventHandler<MediaConfigurationChangedEventArgs>? ConfigurationAdded;

    public event EventHandler<MediaConfigurationChangedEventArgs>? ConfigurationUpdated;

    public event EventHandler<MediaConfigurationChangedEventArgs>? ConfigurationRemoved;

    public MediaFolderConfigurationService(
        ILogger<MediaFolderConfigurationService> logger,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        ShokoAPIClient apiClient,
        NamingOptions namingOptions
    )
    {
        Logger = logger;
        LibraryManager = libraryManager;
        FileSystem = fileSystem;
        ApiClient = apiClient;
        NamingOptions = namingOptions;

        foreach (var mediaConfig in Plugin.Instance.Configuration.MediaFolders)
            MediaFolderChangeKeys[mediaConfig.MediaFolderId] = ConstructKey(mediaConfig);
        LibraryManager.ItemRemoved += OnLibraryManagerItemRemoved;
        Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
    }

    ~MediaFolderConfigurationService()
    {
        GC.SuppressFinalize(this);
        LibraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
        Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        MediaFolderChangeKeys.Clear();
    }

    #region Changes Tracking

    private static string ConstructKey(MediaFolderConfiguration config)
        => $"IsMapped={config.IsMapped},IsFileEventsEnabled={config.IsFileEventsEnabled},IsRefreshEventsEnabled={config.IsRefreshEventsEnabled},IsVirtualFileSystemEnabled={config.IsVirtualFileSystemEnabled},LibraryFilteringMode={config.LibraryFilteringMode}";

    private void OnConfigurationChanged(object? sender, PluginConfiguration config)
    {
        foreach (var mediaConfig in config.MediaFolders) {
            var currentKey = ConstructKey(mediaConfig);
            if (MediaFolderChangeKeys.TryGetValue(mediaConfig.MediaFolderId, out var previousKey) && previousKey != currentKey) {
                MediaFolderChangeKeys[mediaConfig.MediaFolderId] = currentKey;
                if (LibraryManager.GetItemById(mediaConfig.MediaFolderId) is not Folder mediaFolder)
                    continue;
                ConfigurationUpdated?.Invoke(sender, new(mediaConfig, mediaFolder));
            }
        }
    }

    private void OnLibraryManagerItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        var root = LibraryManager.RootFolder;
        if (e.Item != null && root != null && e.Item != root && e.Item is Folder folder && folder.ParentId == Guid.Empty  && !string.IsNullOrEmpty(folder.Path) && !folder.Path.StartsWith(root.Path)) {
            var mediaFolderConfig = Plugin.Instance.Configuration.MediaFolders.FirstOrDefault(c => c.MediaFolderId == folder.Id);
            if (mediaFolderConfig != null) {
                Logger.LogDebug(
                    "Removing stored configuration for folder at {Path} (ImportFolder={ImportFolderId},RelativePath={RelativePath})",
                    folder.Path,
                    mediaFolderConfig.ImportFolderId,
                    mediaFolderConfig.ImportFolderRelativePath
                );
                Plugin.Instance.Configuration.MediaFolders.Remove(mediaFolderConfig);
                Plugin.Instance.UpdateConfiguration();

                MediaFolderChangeKeys.Remove(folder.Id);
                ConfigurationRemoved?.Invoke(null, new(mediaFolderConfig, folder));
            }
        }
    }

    #endregion

    #region Media Folder Mapping

    public IReadOnlyList<(MediaFolderConfiguration config, Folder mediaFolder, string vfsPath)> GetAvailableMediaFolders(bool fileEvents = false, bool refreshEvents = false)
        => Plugin.Instance.Configuration.MediaFolders
            .Where(mediaFolder => mediaFolder.IsMapped && (!fileEvents || mediaFolder.IsFileEventsEnabled) && (!refreshEvents || mediaFolder.IsRefreshEventsEnabled))
            .Select(config => (config,  mediaFolder: LibraryManager.GetItemById(config.MediaFolderId) as Folder))
            .OfType<(MediaFolderConfiguration config, Folder mediaFolder)>()
            .Select(tuple => (tuple.config, tuple.mediaFolder, tuple.mediaFolder.GetVirtualRoot()))
            .ToList();

    public async Task<MediaFolderConfiguration> GetOrCreateConfigurationForMediaFolder(Folder mediaFolder)
    {
        var config = Plugin.Instance.Configuration;
        var mediaFolderConfig = config.MediaFolders.FirstOrDefault(c => c.MediaFolderId == mediaFolder.Id);
        if (mediaFolderConfig != null)
            return mediaFolderConfig;

        // Check if we should introduce the VFS for the media folder.
        mediaFolderConfig = new() {
            MediaFolderId = mediaFolder.Id,
            MediaFolderPath = mediaFolder.Path,
            IsFileEventsEnabled = config.SignalR_FileEvents,
            IsRefreshEventsEnabled = config.SignalR_RefreshEnabled,
            IsVirtualFileSystemEnabled = config.VirtualFileSystem,
            LibraryFilteringMode = config.LibraryFilteringMode,
        };

        var start = DateTime.UtcNow;
        var attempts = 0;
        var samplePaths = FileSystem.GetFilePaths(mediaFolder.Path, true)
            .Where(path => NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
            .Take(100)
            .ToList();

        Logger.LogDebug("Asking remote server if it knows any of the {Count} sampled files in {Path}.", samplePaths.Count > 100 ? 100 : samplePaths.Count, mediaFolder.Path);
        foreach (var path in samplePaths) {
            attempts++;
            var partialPath = path[mediaFolder.Path.Length..];
            var files = await ApiClient.GetFileByPath(partialPath).ConfigureAwait(false);
            var file = files.FirstOrDefault();
            if (file is null)
                continue;

            var fileId = file.Id.ToString();
            var fileLocations = file.Locations
                .Where(location => location.RelativePath.EndsWith(partialPath))
                .ToList();
            if (fileLocations.Count is 0)
                continue;

            var fileLocation = fileLocations[0];
            mediaFolderConfig.ImportFolderId = fileLocation.ImportFolderId;
            mediaFolderConfig.ImportFolderRelativePath = fileLocation.RelativePath[..^partialPath.Length];
            break;
        }

        try {
            var importFolder = await ApiClient.GetImportFolder(mediaFolderConfig.ImportFolderId);
            if (importFolder != null)
                mediaFolderConfig.ImportFolderName = importFolder.Name;
        }
        catch { }

        // Store and log the result.
        MediaFolderChangeKeys[mediaFolder.Id] = ConstructKey(mediaFolderConfig);
        config.MediaFolders.Add(mediaFolderConfig);
        Plugin.Instance.UpdateConfiguration(config);
        if (mediaFolderConfig.IsMapped) {
            Logger.LogInformation(
                "Found a match for media folder at {Path} in {TimeSpan} (ImportFolder={FolderId},RelativePath={RelativePath},MediaLibrary={Path},Attempts={Attempts})",
                mediaFolder.Path,
                DateTime.UtcNow - start,
                mediaFolderConfig.ImportFolderId,
                mediaFolderConfig.ImportFolderRelativePath,
                mediaFolder.Path,
                attempts
            );
        }
        else {
            Logger.LogWarning(
                "Failed to find a match for media folder at {Path} after {Amount} attempts in {TimeSpan}.",
                mediaFolder.Path,
                attempts,
                DateTime.UtcNow - start
            );
        }

        ConfigurationAdded?.Invoke(null, new(mediaFolderConfig, mediaFolder));

        return mediaFolderConfig;
    }

    #endregion
}