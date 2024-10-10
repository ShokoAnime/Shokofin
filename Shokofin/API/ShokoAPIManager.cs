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
    // Note: This regex will only get uglier with time.
    private static readonly Regex YearRegex = new(@"\s+\((?<year>\d{4})(?:dai [2-9] bu)?\)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogger<ShokoAPIManager> Logger;

    private readonly ShokoAPIClient APIClient;

    private readonly ILibraryManager LibraryManager;

    private readonly object MediaFolderListLock = new();

    private readonly List<Folder> MediaFolderList = [];

    private readonly ConcurrentDictionary<string, string> PathToSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> NameToSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, List<string>> PathToEpisodeIdsDictionary = new();

    private readonly ConcurrentDictionary<string, (string FileId, string SeriesId)> PathToFileIdAndSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> SeriesIdToDefaultSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, string?> SeriesIdToCollectionIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> EpisodeIdToSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, List<string>> FileAndSeriesIdToEpisodeIdDictionary = new();

    private readonly GuardedMemoryCache DataCache;

    public ShokoAPIManager(ILogger<ShokoAPIManager> logger, ShokoAPIClient apiClient, ILibraryManager libraryManager)
    {
        Logger = logger;
        APIClient = apiClient;
        LibraryManager = libraryManager;
        DataCache = new(logger, new() { ExpirationScanFrequency = TimeSpan.FromMinutes(25) }, new() { AbsoluteExpirationRelativeToNow = new(2, 30, 0) });
        Plugin.Instance.Tracker.Stalled += OnTrackerStalled;
    }

    ~ShokoAPIManager()
    {
        Plugin.Instance.Tracker.Stalled -= OnTrackerStalled;
    }

    private void OnTrackerStalled(object? sender, EventArgs eventArgs)
        => Clear();

    #region Ignore rule

    /// <summary>
    /// We'll let the ignore rule "scan" for the media folder, and populate our
    /// dictionary for later use, then we'll use said dictionary to lookup the
    /// media folder by path later in the ignore rule and when stripping the
    /// media folder from the path to get the relative path in
    /// <see cref="StripMediaFolder"/>.
    /// </summary>
    /// <param name="path">The path to find the media folder for.</param>
    /// <param name="parent">The parent folder of <paramref name="path"/>.
    /// </param>
    /// <returns>The media folder and partial string within said folder for
    /// <paramref name="path"/>.</returns>
    public (Folder mediaFolder, string partialPath) FindMediaFolder(string path, Folder parent)
    {
        Folder? mediaFolder = null;
        lock (MediaFolderListLock)
            mediaFolder = MediaFolderList.FirstOrDefault((folder) => path.StartsWith(folder.Path + Path.DirectorySeparatorChar));
        if (mediaFolder is not null)
            return (mediaFolder, path[mediaFolder.Path.Length..]);
        if (parent.GetTopParent() is not Folder topParent)
            throw new Exception($"Unable to find media folder for path \"{path}\"");
        lock (MediaFolderListLock)
            MediaFolderList.Add(topParent);
        return (topParent, path[topParent.Path.Length..]);
    }

    /// <summary>
    /// Strip the media folder from the full path, leaving only the partial
    /// path to use when searching Shoko for a match.
    /// </summary>
    /// <param name="fullPath">The full path to strip.</param>
    /// <returns>The partial path, void of the media folder.</returns>
    public string StripMediaFolder(string fullPath)
    {
        Folder? mediaFolder = null;
        lock (MediaFolderListLock)
            mediaFolder = MediaFolderList.FirstOrDefault((folder) => fullPath.StartsWith(folder.Path + Path.DirectorySeparatorChar));
        if (mediaFolder is not null)
            return fullPath[mediaFolder.Path.Length..];
        if (Path.GetDirectoryName(fullPath) is not string directoryPath || LibraryManager.FindByPath(directoryPath, true)?.GetTopParent() is not Folder topParent)
            return fullPath;
        lock (MediaFolderListLock)
            MediaFolderList.Add(topParent);
        return fullPath[topParent.Path.Length..];
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
        EpisodeIdToSeriesIdDictionary.Clear();
        FileAndSeriesIdToEpisodeIdDictionary.Clear();
        lock (MediaFolderListLock)
            MediaFolderList.Clear();
        PathToEpisodeIdsDictionary.Clear();
        PathToFileIdAndSeriesIdDictionary.Clear();
        PathToSeriesIdDictionary.Clear();
        NameToSeriesIdDictionary.Clear();
        SeriesIdToDefaultSeriesIdDictionary.Clear();
        SeriesIdToCollectionIdDictionary.Clear();
        DataCache.Clear();
        Logger.LogDebug("Cleanup complete.");
    }

    #endregion
    #region Tags, Genres, And Content Ratings

    public Task<IReadOnlyDictionary<string, ResolvedTag>> GetNamespacedTagsForSeries(string seriesId)
        => DataCache.GetOrCreateAsync(
            $"series-linked-tags:{seriesId}",
            async () => {
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
                                tagMap[parentKey] = list = [];
                            // Remove comment on tag name itself.
                            if (tag.Name.Contains(" - "))
                                tag.Name = tag.Name.Split(" - ").First().Trim();
                            else if (tag.Name.Contains("--"))
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
                                    tagMap[parentKey] = children = [];

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
                                tagMap[lastParentKey] = lastChildren = [];

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
                var allResolvedTags = rootTags
                    .Select(tag => new ResolvedTag(tag, null, getChildren))
                    .SelectMany(tag => tag.RecursiveNamespacedChildren.Values.Prepend(tag))
                    .ToDictionary(tag => tag.FullName);
                // We reassign the children because they may have been moved to a different namespace.
                foreach (var groupBy in allResolvedTags.Values.GroupBy(tag => tag.Namespace).OrderByDescending(pair => pair.Key)) {
                    if (!allResolvedTags.TryGetValue(groupBy.Key[..^1], out var nsTag))
                        continue;
                    nsTag.Children = groupBy.ToDictionary(childTag => childTag.Name);
                    nsTag.RecursiveNamespacedChildren = nsTag.Children.Values
                        .SelectMany(childTag => childTag.RecursiveNamespacedChildren.Values.Prepend(childTag))
                        .ToDictionary(childTag => childTag.FullName[nsTag.FullName.Length..]);
                }
                return allResolvedTags as IReadOnlyDictionary<string, ResolvedTag>;
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
            async () => {
                var pathSet = new HashSet<string>();
                var episodeIds = new HashSet<string>();
                foreach (var file in await APIClient.GetFilesForSeries(seriesId).ConfigureAwait(false)) {
                    if (file.CrossReferences.Count == 1 && file.CrossReferences[0] is { } xref && xref.Series.Shoko.HasValue && xref.Series.Shoko.ToString() == seriesId)
                        foreach (var fileLocation in file.Locations)
                            pathSet.Add((Path.GetDirectoryName(fileLocation.RelativePath) ?? string.Empty) + Path.DirectorySeparatorChar);
                    xref = file.CrossReferences.FirstOrDefault(xref => xref.Series.Shoko.HasValue && xref.Series.Shoko.ToString() == seriesId);
                    foreach (var episodeXRef in xref?.Episodes.Where(e => e.Shoko.HasValue) ?? [])
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
            AddFileLookupIds(path, fileId, seriesId, fileInfo.EpisodeList.Select(episode => episode.Id));

            // Return the result.
            return new(fileInfo, seasonInfo, showInfo);
        }

        throw new Exception($"Unable to determine the series to use for the file based on it's location because the file resides within a mixed folder with multiple AniDB anime in it. You will either have to fix your file structure or use the VFS to avoid this issue. (File={fileId})\nFile location; {path}");
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

    private static readonly EpisodeType[] EpisodePickOrder = [EpisodeType.Special, EpisodeType.Normal, EpisodeType.Other];

    private Task<FileInfo> CreateFileInfo(File file, string fileId, string seriesId)
        => DataCache.GetOrCreateAsync(
            $"file:{fileId}:{seriesId}",
            async () => {
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

    public bool TryGetFileIdForPath(string path, [NotNullWhen(true)] out string? fileId)
    {
        if (string.IsNullOrEmpty(path)) {
            fileId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (PathToFileIdAndSeriesIdDictionary.TryGetValue(path, out var pair)) {
            fileId = pair.FileId;
            return true;
        }

        // Slow path; getting the show from cache or remote and finding the default season's id.
        Logger.LogDebug("Trying to find file id using the slow path. (Path={FullPath})", path);
        try {
            if (GetFileInfoByPath(path).ConfigureAwait(false).GetAwaiter().GetResult() is { } tuple && tuple.Item1 is not null) {
                var (fileInfo, _, _) = tuple;
                fileId = fileInfo.Id;
                return true;
            }
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Encountered an error while trying to lookup the file id for {Path}", path);
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
            () => {
                Logger.LogTrace("Creating info object for episode {EpisodeName}. (Episode={EpisodeId})", episode.Name, episodeId);

                return new EpisodeInfo(episode);
            }
        );

    public bool TryGetEpisodeIdForPath(string path, [NotNullWhen(true)] out string? episodeId)
    {
        if (string.IsNullOrEmpty(path)) {
            episodeId = null;
            return false;
        }

        var result = TryGetEpisodeIdsForPath(path, out var episodeIds);
        episodeId = episodeIds?.FirstOrDefault();
        return result;
    }

    public bool TryGetEpisodeIdsForPath(string path, [NotNullWhen(true)] out List<string>? episodeIds)
    {
        if (string.IsNullOrEmpty(path)) {
            episodeIds = null;
            return false;
        }

        // Fast path; using the lookup.
        if (PathToEpisodeIdsDictionary.TryGetValue(path, out episodeIds))
            return true;

        // Slow path; getting the show from cache or remote and finding the default season's id.
        Logger.LogDebug("Trying to find episode ids using the slow path. (Path={FullPath})", path);
        if (GetFileInfoByPath(path).ConfigureAwait(false).GetAwaiter().GetResult() is { } tuple && tuple.Item1 is not null) {
            var (fileInfo, _, _) = tuple;
            episodeIds = fileInfo.EpisodeList.Select(episodeInfo => episodeInfo.Id).ToList();
            return episodeIds.Count is > 0;
        }

        episodeIds = null;
        return false;
    }

    public bool TryGetEpisodeIdsForFileId(string fileId, string seriesId, [NotNullWhen(true)] out List<string>? episodeIds)
    {
        if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(seriesId)) {
            episodeIds = null;
            return false;
        }

        // Fast path; using the lookup.
        if (FileAndSeriesIdToEpisodeIdDictionary.TryGetValue($"{fileId}:{seriesId}", out episodeIds))
            return true;

        // Slow path; getting the show from cache or remote and finding the default season's id.
        Logger.LogDebug("Trying to find episode ids using the slow path. (Series={SeriesId},File={FileId})", seriesId, fileId);
        if (GetFileInfo(fileId, seriesId).ConfigureAwait(false).GetAwaiter().GetResult() is { } fileInfo) {
            episodeIds = fileInfo.EpisodeList.Select(episodeInfo => episodeInfo.Id).ToList();
            return true;
        }

        episodeIds = null;
        return false;
    }

    public bool TryGetSeriesIdForEpisodeId(string episodeId, [NotNullWhen(true)] out string? seriesId)
    {
        if (string.IsNullOrEmpty(episodeId)) {
            seriesId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (EpisodeIdToSeriesIdDictionary.TryGetValue(episodeId, out seriesId))
            return true;

        // Slow path; asking the http client to get the series from remote to look up it's id.
        Logger.LogDebug("Trying to find episode ids using the slow path. (Episode={EpisodeId})", episodeId);
        try {
            var series = APIClient.GetSeriesFromEpisode(episodeId).ConfigureAwait(false).GetAwaiter().GetResult();
            seriesId = series.IDs.Shoko.ToString();
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound) {
            seriesId = null;
            return false;
        }
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

        Series series;
        try {
            series = await APIClient.GetSeries(seriesId).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound) {
            return null;
        }
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
            async () => {
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
            async () => {
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
            (tuple) => {
                var config = Plugin.Instance.Configuration;
                if (!config.EXPERIMENTAL_MergeSeasons)
                    return;

                if (!config.EXPERIMENTAL_MergeSeasonsTypes.Contains(GetCustomSeriesType(series.IDs.Shoko.ToString()).ConfigureAwait(false).GetAwaiter().GetResult() ?? series.AniDBEntity.Type))
                    return;

                if (series.AniDBEntity.AirDate is null)
                    return;

                Logger.LogTrace("Reusing existing series-to-season mapping for series. (Series={SeriesId},ExtraSeries={ExtraIds})", tuple.primaryId, tuple.extraIds);
            },
            async () => {
                var primaryId = series.IDs.Shoko.ToString();
                var extraIds = new List<string>();
                var config = Plugin.Instance.Configuration;
                if (!config.EXPERIMENTAL_MergeSeasons)
                    return (primaryId, extraIds);

                if (!config.EXPERIMENTAL_MergeSeasonsTypes.Contains(await GetCustomSeriesType(series.IDs.Shoko.ToString()) ?? series.AniDBEntity.Type))
                    return (primaryId, extraIds);

                if (series.AniDBEntity.AirDate is null)
                    return (primaryId, extraIds);

                Logger.LogTrace("Creating new series-to-season mapping for series. (Series={SeriesId})", primaryId);

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
                                if (string.Equals(adjustedMainTitle, prequelMainTitle, StringComparison.InvariantCultureIgnoreCase)) {
                                    (primaryId, extraIds) = await GetSeriesIdsForSeason(prequelSeries);
                                    goto breakPrequelWhileLoop;
                                }
                                continue;
                            }

                            var adjustedPrequelMainTitle = prequelMainTitle[..^prequelResult.Length];
                            if (string.Equals(adjustedMainTitle, adjustedPrequelMainTitle, StringComparison.InvariantCultureIgnoreCase)) {
                                currentDate = prequelDate;
                                currentRelations = await APIClient.GetSeriesRelations(prequelSeries.IDs.Shoko.ToString()).ConfigureAwait(false);
                                goto continuePrequelWhileLoop;
                            }
                        }
                        breakPrequelWhileLoop: break;
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

                Logger.LogTrace("Created new series-to-season mapping for series. (Series={SeriesId},ExtraSeries={ExtraIds})", primaryId, extraIds);

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

        // Fast path; using the lookup.
        if (PathToSeriesIdDictionary.TryGetValue(path, out seriesId))
            return true;

        // Slow path; getting the show from cache or remote and finding the season's series id.
        Logger.LogDebug("Trying to find the season's series id for {Path} using the slow path.", path);
        if (GetSeasonInfoByPath(path).ConfigureAwait(false).GetAwaiter().GetResult() is { } seasonInfo) {
            seriesId = seasonInfo.Id;
            return true;
        }

        seriesId = null;
        return false;
    }

    public bool TryGetDefaultSeriesIdForSeriesId(string seriesId, [NotNullWhen(true)] out string? defaultSeriesId)
    {
        if (string.IsNullOrEmpty(seriesId)) {
            defaultSeriesId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (SeriesIdToDefaultSeriesIdDictionary.TryGetValue(seriesId, out defaultSeriesId))
            return true;

        // Slow path; getting the show from cache or remote and finding the default season's id.
        Logger.LogDebug("Trying to find the default series id for series using the slow path. (Series={SeriesId})", seriesId);
        if (GetShowInfoForSeries(seriesId).ConfigureAwait(false).GetAwaiter().GetResult() is { } showInfo) {
            defaultSeriesId = showInfo.Id;
            return true;
        }

        defaultSeriesId = null;
        return false;
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
            }

            return primaryId;
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
            async () => {
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
            () => {
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
            async () => {
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
