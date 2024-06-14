using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using Path = System.IO.Path;
using Regex = System.Text.RegularExpressions.Regex;
using RegexOptions = System.Text.RegularExpressions.RegexOptions;

namespace Shokofin.API;

public class ShokoAPIManager : IDisposable
{
    private static readonly Regex YearRegex = new(@"\s+\((?<year>\d{4})\)\s*$", RegexOptions.Compiled);

    private readonly ILogger<ShokoAPIManager> Logger;

    private readonly ShokoAPIClient APIClient;

    private readonly ILibraryManager LibraryManager;

    private readonly object MediaFolderListLock = new();

    private readonly List<Folder> MediaFolderList = new();

    private readonly ConcurrentDictionary<string, string> PathToSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> NameToSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, List<string>> PathToEpisodeIdsDictionary = new();

    private readonly ConcurrentDictionary<string, (string FileId, string SeriesId)> PathToFileIdAndSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> SeriesIdToPathDictionary = new();

    private readonly ConcurrentDictionary<string, string> SeriesIdToDefaultSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, string?> SeriesIdToCollectionIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> EpisodeIdToEpisodePathDictionary = new();

    private readonly ConcurrentDictionary<string, string> EpisodeIdToSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, List<string>> FileAndSeriesIdToEpisodeIdDictionary = new();

    public ShokoAPIManager(ILogger<ShokoAPIManager> logger, ShokoAPIClient apiClient, ILibraryManager libraryManager)
    {
        Logger = logger;
        APIClient = apiClient;
        LibraryManager = libraryManager;
        DataCache = new(logger, TimeSpan.FromMinutes(15), new() { ExpirationScanFrequency = TimeSpan.FromMinutes(25) }, new() { AbsoluteExpirationRelativeToNow = new(2, 30, 0) });
    }

    public bool IsCacheStalled => DataCache.IsStalled;

    private readonly GuardedMemoryCache DataCache;

    #region Ignore rule

    public (Folder mediaFolder, string partialPath) FindMediaFolder(string path, Folder parent, Folder root)
    {
        Folder? mediaFolder = null;
        if (path.StartsWith(Plugin.Instance.VirtualRoot + Path.DirectorySeparatorChar)) {
            var mediaFolderId = Guid.Parse(path[(Plugin.Instance.VirtualRoot.Length + 1)..].Split(Path.DirectorySeparatorChar).First());
            mediaFolder = LibraryManager.GetItemById(mediaFolderId) as Folder;
            if (mediaFolder != null) {
                var mediaRootVirtualPath = mediaFolder.GetVirtualRoot();
                return (mediaFolder, path[mediaRootVirtualPath.Length..]);
            }
            return (root, path);
        }
        lock (MediaFolderListLock) {
            mediaFolder = MediaFolderList.FirstOrDefault((folder) => path.StartsWith(folder.Path + Path.DirectorySeparatorChar));
        }
        if (mediaFolder != null) {
            return (mediaFolder, path[mediaFolder.Path.Length..]);
        }

        // Look for the root folder for the current item.
        mediaFolder = parent;
        while (!mediaFolder.ParentId.Equals(root.Id)) {
            if (mediaFolder.GetParent() == null) {
                break;
            }
            mediaFolder = (Folder)mediaFolder.GetParent();
        }

        lock (MediaFolderListLock) {
            MediaFolderList.Add(mediaFolder);
        }
        return (mediaFolder, path[mediaFolder.Path.Length..]);
    }

    public string StripMediaFolder(string fullPath)
    {
        Folder? mediaFolder = null;
        lock (MediaFolderListLock) {
            mediaFolder = MediaFolderList.FirstOrDefault((folder) => fullPath.StartsWith(folder.Path + Path.DirectorySeparatorChar));
        }
        if (mediaFolder != null) {
            return fullPath[mediaFolder.Path.Length..];
        }

        // Try to get the media folder by loading the parent and navigating upwards till we reach the root.
        var directoryPath = System.IO.Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directoryPath)) {
            return fullPath;
        }

        mediaFolder = (LibraryManager.FindByPath(directoryPath, true) as Folder);
        if (mediaFolder == null || string.IsNullOrEmpty(mediaFolder?.Path)) {
            return fullPath;
        }

        // Look for the root folder for the current item.
        var root = LibraryManager.RootFolder;
        while (!mediaFolder.ParentId.Equals(root.Id)) {
            if (mediaFolder.GetParent() == null) {
                break;
            }
            mediaFolder = (Folder)mediaFolder.GetParent();
        }

        lock (MediaFolderListLock) {
            MediaFolderList.Add(mediaFolder);
        }
        return fullPath[mediaFolder.Path.Length..];
    }

    #endregion
    #region Clear

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Clear();
    }

    public void Clear()
    {
        Logger.LogDebug("Clearing data…");
        EpisodeIdToEpisodePathDictionary.Clear();
        EpisodeIdToSeriesIdDictionary.Clear();
        FileAndSeriesIdToEpisodeIdDictionary.Clear();
        lock (MediaFolderListLock) {
            MediaFolderList.Clear();
        }
        PathToEpisodeIdsDictionary.Clear();
        PathToFileIdAndSeriesIdDictionary.Clear();
        PathToSeriesIdDictionary.Clear();
        NameToSeriesIdDictionary.Clear();
        SeriesIdToDefaultSeriesIdDictionary.Clear();
        SeriesIdToCollectionIdDictionary.Clear();
        SeriesIdToPathDictionary.Clear();
        DataCache.Clear();
        Logger.LogDebug("Cleanup complete.");
    }

    #endregion
    #region Tags, Genres, And Content Ratings

    public Task<IReadOnlyDictionary<string, ResolvedTag>> GetNamespacedTagsForSeries(string seriesId)
        => DataCache.GetOrCreateAsync(
            $"series-linked-tags:{seriesId}",
            async (_) => {
                var nextUserTagId = 1;
                var hasCustomTags = false;
                var rootTags = new List<Tag>();
                var tagMap = new Dictionary<string, List<Tag>>();
                var tags = (await APIClient.GetSeriesTags(seriesId).ConfigureAwait(false))
                    .OrderBy(tag => tag.Source)
                    .ThenBy(tag => tag.Source == "User" ? tag.Name.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length : 0)
                    .ToList();
                foreach (var tag in tags) {
                    if (Plugin.Instance.Configuration.HideUnverifiedTags && tag.IsVerified.HasValue && !tag.IsVerified.Value)
                        continue;

                    switch (tag.Source) {
                        case "AniDB": {
                            var parentKey = $"{tag.Source}:{tag.ParentId ?? 0}";
                            if (!tag.ParentId.HasValue) {
                                rootTags.Add(tag);
                                continue;
                            }
                            if (!tagMap.TryGetValue(parentKey, out var list))
                                tagMap[parentKey] = list = new();
                            // Remove comment on tag name itself.
                            if (tag.Name.Contains("--"))
                                tag.Name = tag.Name.Split("--").First().Trim();
                            list.Add(tag);
                            break;
                        }
                        case "User": {
                            if (!hasCustomTags) {
                                rootTags.Add(new() {
                                    Id = 0,
                                    Name = "custom user tags",
                                    Description = string.Empty,
                                    IsVerified = true,
                                    IsGlobalSpoiler = false,
                                    IsLocalSpoiler = false,
                                    LastUpdated = DateTime.UnixEpoch,
                                    Source = "Shokofin",
                                });
                                hasCustomTags = true;
                            }
                            var parentNames = tag.Name.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                            tag.Name = parentNames.Last();
                            parentNames.RemoveAt(parentNames.Count - 1);
                            var customTagsRoot = rootTags.First(tag => tag.Source == "Shokofin" && tag.Id == 0);
                            var lastParentTag = customTagsRoot;
                            while (parentNames.Count > 0) {
                                // Take the first element from the list.
                                if (!parentNames.TryRemoveAt(0, out var name))
                                    break;

                                // Make sure the parent's children exists in our map.
                                var parentKey = $"Shokofin:{lastParentTag.Id}";
                                if (!tagMap!.TryGetValue(parentKey, out var children))
                                    tagMap[parentKey] = children = new();

                                // Add the child tag to the parent's children if needed.
                                var childTag = children.Find(t => string.Equals(name, t.Name, StringComparison.InvariantCultureIgnoreCase));
                                if (childTag is null)
                                    children.Add(childTag = new() {
                                        Id = nextUserTagId++,
                                        ParentId = lastParentTag.Id,
                                        Name = name.ToLowerInvariant(),
                                        IsVerified = true,
                                        Description = string.Empty,
                                        IsGlobalSpoiler = false,
                                        IsLocalSpoiler = false,
                                        LastUpdated = customTagsRoot.LastUpdated,
                                        Source = "Shokofin",
                                    });

                                // Switch to the child tag for the next parent name.
                                lastParentTag = childTag;
                            };

                            // Same as above, but for the last parent, be it the root or any other layer.
                            var lastParentKey = $"Shokofin:{lastParentTag.Id}";
                            if (!tagMap!.TryGetValue(lastParentKey, out var lastChildren))
                                tagMap[lastParentKey] = lastChildren = new();

                            if (!lastChildren.Any(childTag => string.Equals(childTag.Name, tag.Name, StringComparison.InvariantCultureIgnoreCase)))
                                lastChildren.Add(new() {
                                    Id = nextUserTagId++,
                                    ParentId = lastParentTag.Id,
                                    Name = tag.Name,
                                    Description = tag.Description,
                                    IsVerified = tag.IsVerified,
                                    IsGlobalSpoiler = tag.IsGlobalSpoiler,
                                    IsLocalSpoiler = tag.IsLocalSpoiler,
                                    Weight = tag.Weight,
                                    LastUpdated = tag.LastUpdated,
                                    Source = "Shokofin",
                                });
                            break;
                        }
                    }
                }
                List<Tag>? getChildren(string source, int id) => tagMap.TryGetValue($"{source}:{id}", out var list) ? list : null;
                return rootTags
                    .Select(tag => new ResolvedTag(tag, null, getChildren))
                    .SelectMany(tag => tag.RecursiveNamespacedChildren.Values.Prepend(tag))
                    .OrderBy(tag => tag.FullName)
                    .ToDictionary(childTag => childTag.FullName) as IReadOnlyDictionary<string, ResolvedTag>;
            }
        );

    private async Task<string[]> GetTagsForSeries(string seriesId)
    {
        var tags = await GetNamespacedTagsForSeries(seriesId);
        return TagFilter.FilterTags(tags);
    }

    private async Task<string[]> GetGenresForSeries(string seriesId)
    {
        var tags = await GetNamespacedTagsForSeries(seriesId);
        return TagFilter.FilterGenres(tags);
    }

    private async Task<string[]> GetProductionLocations(string seriesId)
    {
        var tags = await GetNamespacedTagsForSeries(seriesId);
        return TagFilter.GetProductionCountriesFromTags(tags);
    }

    private async Task<string?> GetAssumedContentRating(string seriesId)
    {
        var tags = await GetNamespacedTagsForSeries(seriesId);
        return ContentRating.GetTagBasedContentRating(tags);
    }

    private async Task<SeriesType?> GetCustomSeriesType(string seriesId)
    {
        var tags = await GetNamespacedTagsForSeries(seriesId);
        if (tags.TryGetValue("/custom user tags/series type", out var seriesTypeTag) &&
            seriesTypeTag.Children.Count is > 1 &&
            Enum.TryParse<SeriesType>(NormalizeCustomSeriesType(seriesTypeTag.Children.Keys.First()), out var seriesType) &&
            seriesType is not SeriesType.Unknown
        )
            return seriesType;
        return null;
    }

    private static string NormalizeCustomSeriesType(string seriesType)
    {
        seriesType = seriesType.ToLowerInvariant().Replace(" ", "");
        if (seriesType[^1] == 's')
          seriesType = seriesType[..^1];
        return seriesType;
    }

    #endregion
    #region Path Set And Local Episode IDs

    public async Task<List<(File file, string seriesId)>> GetFilesForSeason(SeasonInfo seasonInfo)
    {
        // TODO: Optimise/cache this better now that we do it per season.
        var list = (await APIClient.GetFilesForSeries(seasonInfo.Id)).Select(file => (file, seriesId: seasonInfo.Id)).ToList();
        foreach (var extraId in seasonInfo.ExtraIds)
            list.AddRange((await APIClient.GetFilesForSeries(extraId)).Select(file => (file, seriesId: extraId)));
        return list;
    }

    /// <summary>
    /// Get a set of paths that are unique to the series and don't belong to
    /// any other series.
    /// </summary>
    /// <param name="seriesId">Shoko series id.</param>
    /// <returns>Unique path set for the series</returns>
    public async Task<HashSet<string>> GetPathSetForSeries(string seriesId, IEnumerable<string> extraIds)
    {
        // TODO: Optimise/cache this better now that we do it per season.
        var (pathSet, _) = await GetPathSetAndLocalEpisodeIdsForSeries(seriesId).ConfigureAwait(false);
        foreach (var extraId in extraIds)
            foreach (var path in await GetPathSetAndLocalEpisodeIdsForSeries(extraId).ContinueWith(task => task.Result.Item1).ConfigureAwait(false))
                pathSet.Add(path);
        return pathSet;
    }

    /// <summary>
    /// Get a set of local episode ids for the series.
    /// </summary>
    /// <param name="seriesId">Shoko series id.</param>
    /// <returns>Local episode ids for the series</returns>
    public async Task<HashSet<string>> GetLocalEpisodeIdsForSeason(SeasonInfo seasonInfo)
    {
        // TODO: Optimise/cache this better now that we do it per season.
        var (_, episodeIds) = await GetPathSetAndLocalEpisodeIdsForSeries(seasonInfo.Id).ConfigureAwait(false);
        foreach (var extraId in seasonInfo.ExtraIds)
            foreach (var episodeId in await GetPathSetAndLocalEpisodeIdsForSeries(extraId).ContinueWith(task => task.Result.Item2).ConfigureAwait(false))
                episodeIds.Add(episodeId);
        return episodeIds;
    }

    // Set up both at the same time.
    private Task<(HashSet<string>, HashSet<string>)> GetPathSetAndLocalEpisodeIdsForSeries(string seriesId)
        => DataCache.GetOrCreateAsync(
            $"series-path-set-and-episode-ids:${seriesId}",
            async (_) => {
                var pathSet = new HashSet<string>();
                var episodeIds = new HashSet<string>();
                foreach (var file in await APIClient.GetFilesForSeries(seriesId).ConfigureAwait(false)) {
                    if (file.CrossReferences.Count == 1)
                        foreach (var fileLocation in file.Locations)
                            pathSet.Add((Path.GetDirectoryName(fileLocation.RelativePath) ?? string.Empty) + Path.DirectorySeparatorChar);
                    var xref = file.CrossReferences.First(xref => xref.Series.Shoko.HasValue && xref.Series.Shoko.ToString() == seriesId);
                    foreach (var episodeXRef in xref.Episodes.Where(e => e.Shoko.HasValue))
                        episodeIds.Add(episodeXRef.Shoko!.Value.ToString());
                }

                return (pathSet, episodeIds);
            },
            new()
        );

    #endregion
    #region File Info

    internal void AddFileLookupIds(string path, string fileId, string seriesId, IEnumerable<string> episodeIds)
    {
        PathToFileIdAndSeriesIdDictionary.TryAdd(path, (fileId, seriesId));
        PathToEpisodeIdsDictionary.TryAdd(path, episodeIds.ToList());
    }

    public async Task<(FileInfo?, SeasonInfo?, ShowInfo?)> GetFileInfoByPath(string path)
    {
        // Use pointer for fast lookup.
        if (PathToFileIdAndSeriesIdDictionary.ContainsKey(path)) {
            var (fI, sI) = PathToFileIdAndSeriesIdDictionary[path];
            var fileInfo = await GetFileInfo(fI, sI).ConfigureAwait(false);
            if (fileInfo == null)
                return (null, null, null);

            var seasonInfo = await GetSeasonInfoForSeries(sI).ConfigureAwait(false);
            if (seasonInfo == null)
                return (null, null, null);

            var showInfo = await GetShowInfoForSeries(sI).ConfigureAwait(false);
            if (showInfo == null)
                return (null, null, null);

            return new(fileInfo, seasonInfo, showInfo);
        }

        // Fast-path for VFS.
        if (path.StartsWith(Plugin.Instance.VirtualRoot + Path.DirectorySeparatorChar)) {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!fileName.TryGetAttributeValue(ShokoSeriesId.Name, out var sI) || !int.TryParse(sI, out _))
                return (null, null, null);
            if (!fileName.TryGetAttributeValue(ShokoFileId.Name, out var fI) || !int.TryParse(fI, out _))
                return (null, null, null);

            var fileInfo = await GetFileInfo(fI, sI).ConfigureAwait(false);
            if (fileInfo == null)
                return (null, null, null);

            var seasonInfo = await GetSeasonInfoForSeries(sI).ConfigureAwait(false);
            if (seasonInfo == null)
                return (null, null, null);

            var showInfo = await GetShowInfoForSeries(sI).ConfigureAwait(false);
            if (showInfo == null)
                return (null, null, null);

            AddFileLookupIds(path, fI, sI, fileInfo.EpisodeList.Select(episode => episode.Id));
            return (fileInfo, seasonInfo, showInfo);
        }

        // Strip the path and search for a match.
        var partialPath = StripMediaFolder(path);
        var result = await APIClient.GetFileByPath(partialPath).ConfigureAwait(false);
        Logger.LogDebug("Looking for a match for {Path}", partialPath);

        // Check if we found a match.
        var file = result?.FirstOrDefault();
        if (file == null || file.CrossReferences.Count == 0) {
            Logger.LogTrace("Found no match for {Path}", partialPath);
            return (null, null, null);
        }

        // Find the file locations matching the given path.
        var fileId = file.Id.ToString();
        var fileLocations = file.Locations
            .Where(location => location.RelativePath.EndsWith(partialPath))
            .ToList();
        Logger.LogTrace("Found a file match for {Path} (File={FileId})", partialPath, file.Id.ToString());
        if (fileLocations.Count != 1) {
            if (fileLocations.Count == 0)
                throw new Exception($"I have no idea how this happened, but the path gave a file that doesn't have a matching file location. See you in #support. (File={fileId})");

            Logger.LogWarning("Multiple locations matched the path, picking the first location. (File={FileId})", fileId);
        }

        // Find the correct series based on the path.
        var selectedPath = (Path.GetDirectoryName(fileLocations.First().RelativePath) ?? string.Empty) + Path.DirectorySeparatorChar;
        foreach (var seriesXRef in file.CrossReferences.Where(xref => xref.Series.Shoko.HasValue && xref.Episodes.All(e => e.Shoko.HasValue))) {
            var seriesId = seriesXRef.Series.Shoko!.Value.ToString();

            // Check if the file is in the series folder.
            var (primaryId, extraIds) = await GetSeriesIdsForSeason(seriesId);
            var pathSet = await GetPathSetForSeries(primaryId, extraIds).ConfigureAwait(false);
            if (!pathSet.Contains(selectedPath))
                continue;

            // Find the season info.
            var seasonInfo = await GetSeasonInfoForSeries(primaryId).ConfigureAwait(false);
            if (seasonInfo == null)
                return (null, null, null);

            // Find the show info.
            var showInfo =  await GetShowInfoForSeries(primaryId).ConfigureAwait(false);
            if (showInfo == null || showInfo.SeasonList.Count == 0)
                return (null, null, null);

            // Find the file info for the series.
            var fileInfo = await CreateFileInfo(file, fileId, seriesId).ConfigureAwait(false);

            // Add pointers for faster lookup.
            foreach (var episodeInfo in fileInfo.EpisodeList)
                EpisodeIdToEpisodePathDictionary.TryAdd(episodeInfo.Id, path);

            // Add pointers for faster lookup.
            AddFileLookupIds(path, fileId, seriesId, fileInfo.EpisodeList.Select(episode => episode.Id));

            // Return the result.
            return new(fileInfo, seasonInfo, showInfo);
        }

        throw new Exception($"Unable to find the series to use for the file. (File={fileId})");
    }

    public async Task<FileInfo?> GetFileInfo(string fileId, string seriesId)
    {
        if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(seriesId))
            return null;

        var cacheKey = $"file:{fileId}:{seriesId}";
        if (DataCache.TryGetValue<FileInfo>(cacheKey, out var fileInfo))
            return fileInfo;

        // Gracefully return if we can't find the file.
        File file;
        try {
            file = await APIClient.GetFile(fileId).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound) {
            return null;
        }

        return await CreateFileInfo(file, fileId, seriesId).ConfigureAwait(false);
    }

    private static readonly EpisodeType[] EpisodePickOrder = { EpisodeType.Special, EpisodeType.Normal, EpisodeType.Other };

    private Task<FileInfo> CreateFileInfo(File file, string fileId, string seriesId)
        => DataCache.GetOrCreateAsync(
            $"file:{fileId}:{seriesId}",
            async (_) => {
                Logger.LogTrace("Creating info object for file. (File={FileId},Series={SeriesId})", fileId, seriesId);

                // Find the cross-references for the selected series.
                var seriesXRef = file.CrossReferences.Where(xref => xref.Series.Shoko.HasValue && xref.Episodes.All(e => e.Shoko.HasValue))
                    .FirstOrDefault(xref => xref.Series.Shoko!.Value.ToString() == seriesId) ??
                    throw new Exception($"Unable to find any cross-references for the specified series for the file. (File={fileId},Series={seriesId})");

                // Find a list of the episode info for each episode linked to the file for the series.
                var episodeList = new List<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)>();
                foreach (var episodeXRef in seriesXRef.Episodes) {
                    var episodeId = episodeXRef.Shoko!.Value.ToString();
                    var episodeInfo = await GetEpisodeInfo(episodeId).ConfigureAwait(false) ??
                        throw new Exception($"Unable to find episode cross-reference for the specified series and episode for the file. (File={fileId},Episode={episodeId},Series={seriesId})");
                    if (episodeInfo.Shoko.IsHidden) {
                        Logger.LogDebug("Skipped hidden episode linked to file. (File={FileId},Episode={EpisodeId},Series={SeriesId})", fileId, episodeId, seriesId);
                        continue;
                    }
                    episodeList.Add((episodeInfo, episodeXRef, episodeId));
                }

                // Group and order the episodes.
                var groupedEpisodeLists = episodeList
                    .GroupBy(tuple => (type: tuple.Episode.AniDB.Type, group: tuple.CrossReference.Percentage?.Group ?? 1))
                    .OrderByDescending(a => Array.IndexOf(EpisodePickOrder, a.Key.type))
                    .ThenBy(a => a.Key.group)
                    .Select(epList => epList.OrderBy(tuple => tuple.Episode.AniDB.EpisodeNumber).ToList())
                    .ToList();

                var fileInfo = new FileInfo(file, groupedEpisodeLists, seriesId);

                FileAndSeriesIdToEpisodeIdDictionary[$"{fileId}:{seriesId}"] = episodeList.Select(episode => episode.Id).ToList();
                return fileInfo;
            }
        );

    public bool TryGetFileIdForPath(string path, out string? fileId)
    {
        if (!string.IsNullOrEmpty(path) && PathToFileIdAndSeriesIdDictionary.TryGetValue(path, out var pair)) {
            fileId = pair.FileId;
            return true;
        }

        fileId = null;
        return false;
    }

    #endregion
    #region Episode Info

    public async Task<EpisodeInfo?> GetEpisodeInfo(string episodeId)
    {
        if (string.IsNullOrEmpty(episodeId))
            return null;

        var key = $"episode:{episodeId}";
        if (DataCache.TryGetValue<EpisodeInfo>(key, out var episodeInfo))
            return episodeInfo;

        var episode = await APIClient.GetEpisode(episodeId).ConfigureAwait(false);
        return CreateEpisodeInfo(episode, episodeId);
    }

    private EpisodeInfo CreateEpisodeInfo(Episode episode, string episodeId)
        => DataCache.GetOrCreate(
            $"episode:{episodeId}",
            (cachedEntry) => {
                Logger.LogTrace("Creating info object for episode {EpisodeName}. (Episode={EpisodeId})", episode.Name, episodeId);

                return new EpisodeInfo(episode);
            }
        );

    public bool TryGetEpisodeIdForPath(string path, out string? episodeId)
    {
        if (string.IsNullOrEmpty(path)) {
            episodeId = null;
            return false;
        }
        var result = PathToEpisodeIdsDictionary.TryGetValue(path, out var episodeIds);
        episodeId = episodeIds?.FirstOrDefault();
        return result;
    }

    public bool TryGetEpisodeIdsForPath(string path, out List<string>? episodeIds)
    {
        if (string.IsNullOrEmpty(path)) {
            episodeIds = null;
            return false;
        }
        return PathToEpisodeIdsDictionary.TryGetValue(path, out episodeIds);
    }

    public bool TryGetEpisodeIdsForFileId(string fileId, string seriesId, out List<string>? episodeIds)
    {
        if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(seriesId)) {
            episodeIds = null;
            return false;
        }
        return FileAndSeriesIdToEpisodeIdDictionary.TryGetValue($"{fileId}:{seriesId}", out episodeIds);
    }

    public bool TryGetEpisodePathForId(string episodeId, out string? path)
    {
        if (string.IsNullOrEmpty(episodeId)) {
            path = null;
            return false;
        }
        return EpisodeIdToEpisodePathDictionary.TryGetValue(episodeId, out path);
    }

    public bool TryGetSeriesIdForEpisodeId(string episodeId, out string? seriesId)
    {
        if (string.IsNullOrEmpty(episodeId)) {
            seriesId = null;
            return false;
        }
        return EpisodeIdToSeriesIdDictionary.TryGetValue(episodeId, out seriesId);
    }

    #endregion
    #region Season Info

    public async Task<SeasonInfo?> GetSeasonInfoByPath(string path)
    {
        var seriesId = await GetSeriesIdForPath(path).ConfigureAwait(false);
        if (string.IsNullOrEmpty(seriesId))
            return null;

        var series = await APIClient.GetSeries(seriesId).ConfigureAwait(false);
        return await CreateSeasonInfo(series).ConfigureAwait(false);
    }

    public async Task<SeasonInfo?> GetSeasonInfoForSeries(string seriesId)
    {
        if (string.IsNullOrEmpty(seriesId))
            return null;

        var series = await APIClient.GetSeries(seriesId).ConfigureAwait(false);
        return await CreateSeasonInfo(series).ConfigureAwait(false);
    }

    public async Task<SeasonInfo?> GetSeasonInfoForEpisode(string episodeId)
    {
        if (!EpisodeIdToSeriesIdDictionary.TryGetValue(episodeId, out var seriesId)) {
            var series = await APIClient.GetSeriesFromEpisode(episodeId).ConfigureAwait(false);
            if (series == null)
                return null;
            seriesId = series.IDs.Shoko.ToString();
            return await CreateSeasonInfo(series).ConfigureAwait(false);
        }

        return await GetSeasonInfoForSeries(seriesId).ConfigureAwait(false);
    }

    private async Task<SeasonInfo> CreateSeasonInfo(Series series)
    {
        var (seriesId, extraIds) = await GetSeriesIdsForSeason(series);
        return await DataCache.GetOrCreateAsync(
            $"season:{seriesId}",
            (seasonInfo) => Logger.LogTrace("Reusing info object for season {SeriesName}. (Series={SeriesId})", seasonInfo.Shoko.Name, seriesId),
            async (cachedEntry) => {
                // We updated the "primary" series id for the merge group, so fetch the new series details from the client cache.
                if (!string.Equals(series.IDs.Shoko.ToString(), seriesId, StringComparison.Ordinal))
                    series = await APIClient.GetSeries(seriesId);

                Logger.LogTrace("Creating info object for season {SeriesName}. (Series={SeriesId},ExtraSeries={ExtraIds})", series.Name, seriesId, extraIds);

                var customSeriesType = await GetCustomSeriesType(seriesId).ConfigureAwait(false);
                var contentRating = await GetAssumedContentRating(seriesId).ConfigureAwait(false);
                var (earliestImportedAt, lastImportedAt) = await GetEarliestImportedAtForSeries(seriesId).ConfigureAwait(false);
                var episodes = (await Task.WhenAll(
                    extraIds.Prepend(seriesId)
                        .Select(id => APIClient.GetEpisodesFromSeries(id).ContinueWith(task => task.Result.List.Select(e => CreateEpisodeInfo(e, e.IDs.Shoko.ToString()))))
                ).ConfigureAwait(false))
                    .SelectMany(list => list)
                    .OrderBy(episode => episode.AniDB.AirDate)
                    .ToList();

                SeasonInfo seasonInfo;
                if (extraIds.Count > 0) {
                    var detailsIds = extraIds.Prepend(seriesId).ToList();

                    // Create the tasks.
                    var castTasks = detailsIds.Select(id => APIClient.GetSeriesCast(id));
                    var relationsTasks = detailsIds.Select(id => APIClient.GetSeriesRelations(id));
                    var genresTasks = detailsIds.Select(id => GetGenresForSeries(id));
                    var tagsTasks = detailsIds.Select(id => GetTagsForSeries(id));
                    var productionLocationsTasks = detailsIds.Select(id => GetProductionLocations(id));

                    // Await the tasks in order.
                    var cast = (await Task.WhenAll(castTasks))
                        .SelectMany(c => c)
                        .Distinct()
                        .ToList();
                    var relations = (await Task.WhenAll(relationsTasks))
                        .SelectMany(r => r)
                        .Where(r => r.RelatedIDs.Shoko.HasValue && !detailsIds.Contains(r.RelatedIDs.Shoko.Value.ToString()))
                        .ToList();
                    var genres = (await Task.WhenAll(genresTasks))
                        .SelectMany(g => g)
                        .OrderBy(g => g)
                        .Distinct()
                        .ToArray();
                    var tags = (await Task.WhenAll(tagsTasks))
                        .SelectMany(t => t)
                        .OrderBy(t => t)
                        .Distinct()
                        .ToArray();
                    var productionLocations = (await Task.WhenAll(genresTasks))
                        .SelectMany(g => g)
                        .OrderBy(g => g)
                        .Distinct()
                        .ToArray();

                    // Create the season info using the merged details.
                    seasonInfo = new SeasonInfo(series, customSeriesType, extraIds, earliestImportedAt, lastImportedAt, episodes, cast, relations, genres, tags, productionLocations, contentRating);
                } else {
                    var cast = await APIClient.GetSeriesCast(seriesId).ConfigureAwait(false);
                    var relations = await APIClient.GetSeriesRelations(seriesId).ConfigureAwait(false);
                    var genres = await GetGenresForSeries(seriesId).ConfigureAwait(false);
                    var tags = await GetTagsForSeries(seriesId).ConfigureAwait(false);
                    var productionLocations = await GetProductionLocations(seriesId).ConfigureAwait(false);
                    seasonInfo = new SeasonInfo(series, customSeriesType, extraIds, earliestImportedAt, lastImportedAt, episodes, cast, relations, genres, tags, productionLocations, contentRating);
                }

                foreach (var episode in episodes)
                    EpisodeIdToSeriesIdDictionary.TryAdd(episode.Id, seriesId);
                return seasonInfo;
            }
        );
    }

    private Task<(DateTime?, DateTime?)> GetEarliestImportedAtForSeries(string seriesId)
        => DataCache.GetOrCreateAsync<(DateTime?, DateTime?)>(
            $"series-earliest-imported-at:${seriesId}",
            async (_) => {
                var files = await APIClient.GetFilesForSeries(seriesId).ConfigureAwait(false);
                if (!files.Any(f => f.ImportedAt.HasValue))
                    return (null, null);
                return (
                    files.Any(f => f.ImportedAt.HasValue)
                        ? files.Where(f => f.ImportedAt.HasValue).Select(f => f.ImportedAt!.Value).Min()
                        : files.Select(f => f.CreatedAt).Min(),
                    files.Any(f => f.ImportedAt.HasValue)
                        ? files.Where(f => f.ImportedAt.HasValue).Select(f => f.ImportedAt!.Value).Max()
                        : files.Select(f => f.CreatedAt).Max()
                );
            },
            new()
        );

    public async Task<(string primaryId, List<string> extraIds)> GetSeriesIdsForSeason(string seriesId)
        => await GetSeriesIdsForSeason(await APIClient.GetSeries(seriesId));

    private Task<(string primaryId, List<string> extraIds)> GetSeriesIdsForSeason(Series series)
        => DataCache.GetOrCreateAsync(
            $"season-series-ids:{series.IDs.Shoko}",
            (tuple) => Logger.LogTrace(""),
            async (cacheEntry) => {
                var primaryId = series.IDs.Shoko.ToString();
                var extraIds = new List<string>();
                var config = Plugin.Instance.Configuration;
                if (!config.EXPERIMENTAL_MergeSeasons)
                    return (primaryId, extraIds);

                if (!config.EXPERIMENTAL_MergeSeasonsTypes.Contains(await GetCustomSeriesType(series.IDs.Shoko.ToString()) ?? series.AniDBEntity.Type))
                    return (primaryId, extraIds);

                if (series.AniDBEntity.AirDate is null)
                    return (primaryId, extraIds);

                // We potentially have a "follow-up" season candidate, so look for the "primary" season candidate, then jump into that. 
                var relations = await APIClient.GetSeriesRelations(primaryId).ConfigureAwait(false);
                var mainTitle = series.AniDBEntity.Titles.First(title => title.Type == TitleType.Main).Value;
                var result = YearRegex.Match(mainTitle);
                var maxDaysThreshold = config.EXPERIMENTAL_MergeSeasonsMergeWindowInDays;
                if (result.Success)
                {
                    var adjustedMainTitle = mainTitle[..^result.Length];
                    var currentDate = series.AniDBEntity.AirDate.Value;
                    var currentRelations = relations;
                    while (currentRelations.Count > 0) {
                        foreach (var prequelRelation in currentRelations.Where(relation => relation.Type == RelationType.Prequel && relation.RelatedIDs.Shoko.HasValue)) {
                            var prequelSeries = await APIClient.GetSeries(prequelRelation.RelatedIDs.Shoko!.Value.ToString());
                            if (prequelSeries.IDs.ParentGroup != series.IDs.ParentGroup)
                                continue;

                            if (!config.EXPERIMENTAL_MergeSeasonsTypes.Contains(await GetCustomSeriesType(prequelSeries.IDs.Shoko.ToString()) ?? prequelSeries.AniDBEntity.Type))
                                continue;

                            if (prequelSeries.AniDBEntity.AirDate is null)
                                continue;

                            var prequelDate = prequelSeries.AniDBEntity.AirDate.Value;
                            if (prequelDate > currentDate)
                                continue;

                            if (maxDaysThreshold > 0) {
                                var deltaDays = (int)Math.Floor((currentDate - prequelDate).TotalDays);
                                if (deltaDays > maxDaysThreshold)
                                    continue;
                            }

                            var prequelMainTitle = prequelSeries.AniDBEntity.Titles.First(title => title.Type == TitleType.Main).Value;
                            var prequelResult = YearRegex.Match(prequelMainTitle);
                            if (!prequelResult.Success) {
                                if (string.Equals(adjustedMainTitle, prequelMainTitle, StringComparison.InvariantCultureIgnoreCase))
                                    return await GetSeriesIdsForSeason(prequelSeries);
                                continue;
                            }

                            var adjustedPrequelMainTitle = prequelMainTitle[..^prequelResult.Length];
                            if (string.Equals(adjustedMainTitle, adjustedPrequelMainTitle, StringComparison.InvariantCultureIgnoreCase)) {
                                currentDate = prequelDate;
                                currentRelations = await APIClient.GetSeriesRelations(prequelSeries.IDs.Shoko.ToString()).ConfigureAwait(false);
                                goto continuePrequelWhileLoop;
                            }
                        }
                        break;
                        continuePrequelWhileLoop: continue;
                    }
                }
                // We potentially have a "primary" season candidate, so look for any "follow-up" season candidates.
                else {
                    var currentDate = series.AniDBEntity.AirDate.Value;
                    var adjustedMainTitle = mainTitle;
                    var currentRelations = relations;
                    while (currentRelations.Count > 0) {
                        foreach (var sequelRelation in currentRelations.Where(relation => relation.Type == RelationType.Sequel && relation.RelatedIDs.Shoko.HasValue)) {
                            var sequelSeries = await APIClient.GetSeries(sequelRelation.RelatedIDs.Shoko!.Value.ToString());
                            if (sequelSeries.IDs.ParentGroup != series.IDs.ParentGroup)
                                continue;

                            if (!config.EXPERIMENTAL_MergeSeasonsTypes.Contains(await GetCustomSeriesType(sequelSeries.IDs.Shoko.ToString()) ?? sequelSeries.AniDBEntity.Type))
                                continue;

                            if (sequelSeries.AniDBEntity.AirDate is null)
                                continue;

                            var sequelDate = sequelSeries.AniDBEntity.AirDate.Value;
                            if (sequelDate < currentDate)
                                continue;

                            if (maxDaysThreshold > 0) {
                                var deltaDays = (int)Math.Floor((sequelDate - currentDate).TotalDays);
                                if (deltaDays > maxDaysThreshold)
                                    continue;
                            }

                            var sequelMainTitle = sequelSeries.AniDBEntity.Titles.First(title => title.Type == TitleType.Main).Value;
                            var sequelResult = YearRegex.Match(sequelMainTitle);
                            if (!sequelResult.Success)
                                continue;

                            var adjustedSequelMainTitle = sequelMainTitle[..^sequelResult.Length];
                            if (string.Equals(adjustedMainTitle, adjustedSequelMainTitle, StringComparison.InvariantCultureIgnoreCase)) {
                                extraIds.Add(sequelSeries.IDs.Shoko.ToString());
                                currentDate = sequelDate;
                                currentRelations = await APIClient.GetSeriesRelations(sequelSeries.IDs.Shoko.ToString()).ConfigureAwait(false);
                                goto continueSequelWhileLoop;
                            }
                        }
                        break;
                        continueSequelWhileLoop: continue;
                    }
                }

                return (primaryId, extraIds);
            }
        );

    #endregion
    #region Series Helpers

    public bool TryGetSeriesIdForPath(string path, [NotNullWhen(true)] out string? seriesId)
    {
        if (string.IsNullOrEmpty(path)) {
            seriesId = null;
            return false;
        }
        return PathToSeriesIdDictionary.TryGetValue(path, out seriesId);
    }

    public bool TryGetSeriesPathForId(string seriesId, [NotNullWhen(true)] out string? path)
    {
        if (string.IsNullOrEmpty(seriesId)) {
            path = null;
            return false;
        }
        return SeriesIdToPathDictionary.TryGetValue(seriesId, out path);
    }

    public bool TryGetDefaultSeriesIdForSeriesId(string seriesId, [NotNullWhen(true)] out string? defaultSeriesId)
    {
        if (string.IsNullOrEmpty(seriesId)) {
            defaultSeriesId = null;
            return false;
        }
        return SeriesIdToDefaultSeriesIdDictionary.TryGetValue(seriesId, out defaultSeriesId);
    }

    private async Task<string?> GetSeriesIdForPath(string path)
    {
        // Reuse cached value.
        if (PathToSeriesIdDictionary.TryGetValue(path, out var seriesId))
            return seriesId;

        // Fast-path for VFS.
        if (path.StartsWith(Plugin.Instance.VirtualRoot + Path.DirectorySeparatorChar)) {
            if (!Path.GetFileName(path).TryGetAttributeValue(ShokoSeriesId.Name, out seriesId) || !int.TryParse(seriesId, out _))
                return null;

            PathToSeriesIdDictionary[path] = seriesId;
            SeriesIdToPathDictionary.TryAdd(seriesId, path);

            return seriesId;
        }

        var partialPath = StripMediaFolder(path);
        Logger.LogDebug("Looking for shoko series matching path {Path}", partialPath);
        var result = await APIClient.GetSeriesPathEndsWith(partialPath).ConfigureAwait(false);
        Logger.LogTrace("Found {Count} matches for path {Path}", result.Count, partialPath);

        // Return the first match where the series unique paths partially match
        // the input path.
        foreach (var series in result) {
            seriesId  = series.IDs.Shoko.ToString();
            var (primaryId, extraIds) = await GetSeriesIdsForSeason(seriesId);
            var pathSet = await GetPathSetForSeries(primaryId, extraIds).ConfigureAwait(false);
            foreach (var uniquePath in pathSet) {
                // Remove the trailing slash before matching.
                if (!uniquePath[..^1].EndsWith(partialPath))
                    continue;

                PathToSeriesIdDictionary[path] = primaryId;
                SeriesIdToPathDictionary.TryAdd(primaryId, path);

                return primaryId;
            }
        }

        // In the edge case for series with only files with multiple
        // cross-references we just return the first match.
        return result.FirstOrDefault()?.IDs.Shoko.ToString();
    }

    #endregion
    #region Show Info

    public async Task<ShowInfo?> GetShowInfoByPath(string path)
    {
        if (!PathToSeriesIdDictionary.TryGetValue(path, out var seriesId)) {
            seriesId = await GetSeriesIdForPath(path).ConfigureAwait(false);
            if (string.IsNullOrEmpty(seriesId))
                return null;
        }

        return await GetShowInfoForSeries(seriesId).ConfigureAwait(false);
    }

    public async Task<ShowInfo?> GetShowInfoForEpisode(string episodeId)
    {
        if (string.IsNullOrEmpty(episodeId))
            return null;

        if (EpisodeIdToSeriesIdDictionary.TryGetValue(episodeId, out var seriesId))
            return await GetShowInfoForSeries(seriesId).ConfigureAwait(false);

        var series = await APIClient.GetSeriesFromEpisode(episodeId).ConfigureAwait(false);
        if (series == null)
            return null;

        seriesId = series.IDs.Shoko.ToString();
        return await GetShowInfoForSeries(seriesId).ConfigureAwait(false);
    }

    public async Task<ShowInfo?> GetShowInfoForSeries(string seriesId)
    {
        if (string.IsNullOrEmpty(seriesId))
            return null;

        var group = await APIClient.GetGroupFromSeries(seriesId).ConfigureAwait(false);
        if (group == null)
            return null;

        var seasonInfo = await GetSeasonInfoForSeries(seriesId).ConfigureAwait(false);
        if (seasonInfo == null)
            return null;

        // Create a standalone group if grouping is disabled and/or for each series in a group with sub-groups.
        if (!Plugin.Instance.Configuration.UseGroupsForShows || group.Sizes.SubGroups > 0)
            return GetOrCreateShowInfoForSeasonInfo(seasonInfo);

        // If we found a movie, and we're assigning movies as stand-alone shows, and we didn't create a stand-alone show
        // above, then attach the stand-alone show to the parent group of the group that might other
        if (seasonInfo.Type == SeriesType.Movie && Plugin.Instance.Configuration.SeparateMovies)
            return GetOrCreateShowInfoForSeasonInfo(seasonInfo, group.Size > 0 ? group.IDs.ParentGroup?.ToString() : null);

        return await CreateShowInfoForGroup(group, group.IDs.Shoko.ToString()).ConfigureAwait(false);
    }

    private Task<ShowInfo?> CreateShowInfoForGroup(Group group, string groupId)
        => DataCache.GetOrCreateAsync(
            $"show:by-group-id:{groupId}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {GroupName}. (Group={GroupId})", showInfo?.Name, groupId),
            async (cachedEntry) => {
                Logger.LogTrace("Creating info object for show {GroupName}. (Group={GroupId})", group.Name, groupId);

                var seriesInGroup = await APIClient.GetSeriesInGroup(groupId).ConfigureAwait(false);
                var seasonList = (await Task.WhenAll(seriesInGroup.Select(CreateSeasonInfo)).ConfigureAwait(false))
                    .DistinctBy(seasonInfo => seasonInfo.Id)
                    .ToList();

                var length = seasonList.Count;
                if (Plugin.Instance.Configuration.SeparateMovies) {
                    seasonList = seasonList.Where(s => s.Type != SeriesType.Movie).ToList();

                    // Return early if no series matched the filter or if the list was empty.
                    if (seasonList.Count == 0) {
                        Logger.LogWarning("Creating an empty show info for filter! (Group={GroupId})", groupId);

                        return null;
                    }
                }

                var showInfo = new ShowInfo(group, seasonList, Logger, length != seasonList.Count);

                foreach (var seasonInfo in seasonList) {
                    SeriesIdToDefaultSeriesIdDictionary[seasonInfo.Id] = showInfo.Id;
                    if (!string.IsNullOrEmpty(showInfo.CollectionId))
                        SeriesIdToCollectionIdDictionary[seasonInfo.Id] = showInfo.CollectionId;
                }

                return showInfo;
            }
        );


    private ShowInfo GetOrCreateShowInfoForSeasonInfo(SeasonInfo seasonInfo, string? collectionId = null)
        => DataCache.GetOrCreate(
            $"show:by-series-id:{seasonInfo.Id}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {GroupName}. (Series={SeriesId})", showInfo.Name, seasonInfo.Id),
            (cachedEntry) => {
                Logger.LogTrace("Creating info object for show {SeriesName}. (Series={SeriesId})", seasonInfo.Shoko.Name, seasonInfo.Id);

                var showInfo = new ShowInfo(seasonInfo, collectionId);
                SeriesIdToDefaultSeriesIdDictionary[seasonInfo.Id] = showInfo.Id;
                if (!string.IsNullOrEmpty(showInfo.CollectionId))
                    SeriesIdToCollectionIdDictionary[seasonInfo.Id] = showInfo.CollectionId;
                return showInfo;
            }
        );

    #endregion
    #region Collection Info

    public async Task<CollectionInfo?> GetCollectionInfoForGroup(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return null;

        if (DataCache.TryGetValue<CollectionInfo>($"collection:by-group-id:{groupId}", out var collectionInfo)) {
            Logger.LogTrace("Reusing info object for collection {GroupName}. (Group={GroupId})", collectionInfo.Name, groupId);
            return collectionInfo;
        }

        var group = await APIClient.GetGroup(groupId).ConfigureAwait(false);
        return await CreateCollectionInfo(group, groupId).ConfigureAwait(false);
    }

    public async Task<CollectionInfo?> GetCollectionInfoForSeries(string seriesId)
    {
        if (string.IsNullOrEmpty(seriesId))
            return null;

        if (SeriesIdToCollectionIdDictionary.TryGetValue(seriesId, out var groupId)) {
            if (string.IsNullOrEmpty(groupId))
                return null;

            return await GetCollectionInfoForGroup(groupId).ConfigureAwait(false);
        }

        var group = await APIClient.GetGroupFromSeries(seriesId).ConfigureAwait(false);
        if (group == null)
            return null;

        return await CreateCollectionInfo(group, group.IDs.Shoko.ToString()).ConfigureAwait(false);
    }

    private Task<CollectionInfo> CreateCollectionInfo(Group group, string groupId)
        => DataCache.GetOrCreateAsync(
            $"collection:by-group-id:{groupId}",
            (collectionInfo) => Logger.LogTrace("Reusing info object for collection {GroupName}. (Group={GroupId})", collectionInfo.Name, groupId),
            async (cachedEntry) => {
                Logger.LogTrace("Creating info object for collection {GroupName}. (Group={GroupId})", group.Name, groupId);
                Logger.LogTrace("Fetching show info objects for collection {GroupName}. (Group={GroupId})", group.Name, groupId);

                var showGroupIds = new HashSet<string>();
                var collectionIds = new HashSet<string>();
                var showDict = new Dictionary<string, ShowInfo>();
                foreach (var series in await APIClient.GetSeriesInGroup(groupId, recursive: true).ConfigureAwait(false)) {
                    var showInfo = await GetShowInfoForSeries(series.IDs.Shoko.ToString()).ConfigureAwait(false);
                    if (showInfo == null)
                        continue;

                    if (!string.IsNullOrEmpty(showInfo.GroupId))
                        showGroupIds.Add(showInfo.GroupId);

                    if (string.IsNullOrEmpty(showInfo.CollectionId))
                        continue;

                    collectionIds.Add(showInfo.CollectionId);
                    if (showInfo.CollectionId == groupId)
                        showDict.TryAdd(showInfo.Id, showInfo);
                }

                var groupList = new List<CollectionInfo>();
                if (group.Sizes.SubGroups > 0) {
                    Logger.LogTrace("Fetching sub-collection info objects for collection {GroupName}. (Group={GroupId})", group.Name, groupId);
                    foreach (var subGroup in await APIClient.GetGroupsInGroup(groupId).ConfigureAwait(false)) {
                        if (showGroupIds.Contains(subGroup.IDs.Shoko.ToString()) && !collectionIds.Contains(subGroup.IDs.Shoko.ToString()))
                            continue;
                        var subCollectionInfo = await CreateCollectionInfo(subGroup, subGroup.IDs.Shoko.ToString()).ConfigureAwait(false);
                        if (subCollectionInfo.Shoko.Sizes.Files > 0)
                            groupList.Add(subCollectionInfo);
                    }
                }

                Logger.LogTrace("Finalizing info object for collection {GroupName}. (Group={GroupId})", group.Name, groupId);
                var showList = showDict.Values.ToList();
                var collectionInfo = new CollectionInfo(group, showList, groupList);
                return collectionInfo;
            }
        );

    #endregion
}
