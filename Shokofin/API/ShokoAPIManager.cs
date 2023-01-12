using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.Utils;

using CultureInfo = System.Globalization.CultureInfo;
using ItemLookupInfo = MediaBrowser.Controller.Providers.ItemLookupInfo;
using Path = System.IO.Path;

#nullable enable
namespace Shokofin.API;

public class ShokoAPIManager
{
    private readonly ILogger<ShokoAPIManager> Logger;

    private readonly ShokoAPIClient APIClient;

    private readonly ILibraryManager LibraryManager;

    private readonly List<Folder> MediaFolderList = new();

    private readonly ConcurrentDictionary<string, string> PathToSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, List<string>> PathToEpisodeIdsDictionary = new();

    private readonly ConcurrentDictionary<string, (string, string)> PathToFileIdAndSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> SeriesIdToPathDictionary = new();

    private readonly ConcurrentDictionary<string, string> SeriesIdToGroupIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> EpisodeIdToEpisodePathDictionary = new();

    private readonly ConcurrentDictionary<string, string> EpisodeIdToSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, List<string>> FileIdToEpisodeIdDictionary = new();

    public ShokoAPIManager(ILogger<ShokoAPIManager> logger, ShokoAPIClient apiClient, ILibraryManager libraryManager)
    {
        Logger = logger;
        APIClient = apiClient;
        LibraryManager = libraryManager;
    }

    private IMemoryCache DataCache = new MemoryCache(new MemoryCacheOptions() {
        ExpirationScanFrequency = ExpirationScanFrequency,
    });

    private static readonly System.TimeSpan ExpirationScanFrequency = new System.TimeSpan(0, 25, 0);

    private static readonly System.TimeSpan DefaultTimeSpan = new System.TimeSpan(1, 30, 0);

    #region Ignore rule

    public Folder FindMediaFolder(string path)
    {
        var mediaFolder = MediaFolderList.FirstOrDefault((folder) => path.StartsWith(folder.Path + Path.DirectorySeparatorChar));
        if (mediaFolder == null) {
            var parent = (Folder?)LibraryManager.FindByPath(Path.GetDirectoryName(path), true);
            if (parent == null)
                throw new Exception($"Unable to find parent folder for \"{path}\"");

            mediaFolder = FindMediaFolder(path, parent, LibraryManager.RootFolder);
        }

        return mediaFolder;
    }

    public Folder FindMediaFolder(string path, Folder parent, Folder root)
    {
        var mediaFolder = MediaFolderList.FirstOrDefault((folder) => path.StartsWith(folder.Path + Path.DirectorySeparatorChar));
        // Look for the root folder for the current item.
        if (mediaFolder != null) {
            return mediaFolder;
        }

        mediaFolder = parent;
        while (!mediaFolder.ParentId.Equals(root.Id)) {
            if (mediaFolder.GetParent() == null) {
                break;
            }
            mediaFolder = (Folder)mediaFolder.GetParent();
        }

        MediaFolderList.Add(mediaFolder);
        return mediaFolder;
    }

    public string StripMediaFolder(string fullPath)
    {
        var mediaFolder = MediaFolderList.FirstOrDefault((folder) => fullPath.StartsWith(folder.Path + Path.DirectorySeparatorChar));
        if (mediaFolder != null) {
            return fullPath.Substring(mediaFolder.Path.Length);
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

        MediaFolderList.Add(mediaFolder);
        return fullPath.Substring(mediaFolder.Path.Length);
    }

    public bool IsInMixedLibrary(ItemLookupInfo info)
    {
        var mediaFolder = FindMediaFolder(info.Path);
        var type = LibraryManager.GetInheritedContentType(mediaFolder);
        return !string.IsNullOrEmpty(type) && type == "mixed";
    }

    #endregion
    #region Clear

    public void Clear()
    {
        Logger.LogDebug("Clearing data.");
        DataCache.Dispose();
        EpisodeIdToEpisodePathDictionary.Clear();
        EpisodeIdToSeriesIdDictionary.Clear();
        FileIdToEpisodeIdDictionary.Clear();
        MediaFolderList.Clear();
        PathToEpisodeIdsDictionary.Clear();
        PathToFileIdAndSeriesIdDictionary.Clear();
        PathToSeriesIdDictionary.Clear();
        SeriesIdToGroupIdDictionary.Clear();
        SeriesIdToPathDictionary.Clear();
        DataCache = (new MemoryCache((new MemoryCacheOptions() {
            ExpirationScanFrequency = ExpirationScanFrequency,
        })));
    }

    #endregion
    #region Tags And Genres

    private async Task<string[]> GetTagsForSeries(string seriesId)
    {
        return (await APIClient.GetSeriesTags(seriesId, GetTagFilter()))?.Select(SelectTagName).ToArray() ?? new string[0];
    }

    /// <summary>
    /// Get the tag filter
    /// </summary>
    /// <returns></returns>
    private ulong GetTagFilter()
    {
        var config = Plugin.Instance.Configuration;
        ulong filter = 132L; // We exclude genres and source by default

        if (config.HideAniDbTags) filter |= (1 << 0);
        if (config.HideArtStyleTags) filter |= (1 << 1);
        if (config.HideMiscTags) filter |= (1 << 3);
        if (config.HidePlotTags) filter |= (1 << 4);
        if (config.HideSettingTags) filter |= (1 << 5);
        if (config.HideProgrammingTags) filter |= (1 << 6);

        return filter;
    }

    public async Task<string[]> GetGenresForSeries(string seriesId)
    {
        // The following magic number is the filter value to allow only genres in the returned list.
        var genreSet = (await APIClient.GetSeriesTags(seriesId, 2147483776))?.Select(SelectTagName).ToHashSet() ?? new();
        var sourceGenre = await GetSourceGenre(seriesId);
        genreSet.Add(sourceGenre);
        return genreSet.ToArray();
    }

    private async Task<string> GetSourceGenre(string seriesId)
    {
        // The following magic number is the filter value to allow only the source type in the returned list.
        return(await APIClient.GetSeriesTags(seriesId, 2147483652))?.FirstOrDefault()?.Name?.ToLowerInvariant() switch {
            "american derived" => "Adapted From Western Media",
            "cartoon" => "Adapted From Western Media",
            "comic book" => "Adapted From Western Media",
            "4-koma" => "Adapted From A Manga",
            "manga" => "Adapted From A Manga",
            "4-koma manga" => "Adapted From A Manga",
            "manhua" => "Adapted From A Manhua",
            "manhwa" => "Adapted from a Manhwa",
            "movie" => "Adapted From A Movie",
            "novel" => "Adapted From A Light/Web Novel",
            "rpg" => "Adapted From A Video Game",
            "action game" => "Adapted From A Video Game",
            "game" => "Adapted From A Video Game",
            "erotic game" => "Adapted From An Eroge",
            "korean drama" => "Adapted From A Korean Drama",
            "television programme" => "Adapted From A Live-Action Show",
            "visual novel" => "Adapted From A Visual Novel",
            "fan-made" => "Fan-Made",
            "remake" => "Remake",
            "radio programme" => "Radio Programme",
            "biographical film" => "Original Work",
            "original work" => "Original Work",
            "new" => "Original Work",
            "ultra jump" => "Original Work",
            _ => "Original Work",
        };
    }

    private string SelectTagName(Tag tag)
    {
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tag.Name);
    }

    #endregion
    #region Path Set And Local Episode IDs

    /// <summary>
    /// Get a set of paths that are unique to the series and don't belong to
    /// any other series.
    /// </summary>
    /// <param name="seriesId">Shoko series id.</param>
    /// <returns>Unique path set for the series</returns>
    public async Task<HashSet<string>> GetPathSetForSeries(string seriesId)
    {
        var (pathSet, _episodeIds) = await GetPathSetAndLocalEpisodeIdsForSeries(seriesId);
        return pathSet;
    }

    /// <summary>
    /// Get a set of local episode ids for the series.
    /// </summary>
    /// <param name="seriesId">Shoko series id.</param>
    /// <returns>Local episode ids for the series</returns>
    public HashSet<string> GetLocalEpisodeIdsForSeries(string seriesId)
    {
        var (_pathSet, episodeIds) = GetPathSetAndLocalEpisodeIdsForSeries(seriesId)
            .GetAwaiter()
            .GetResult();
        return episodeIds;
    }

    // Set up both at the same time.
    private async Task<(HashSet<string>, HashSet<string>)> GetPathSetAndLocalEpisodeIdsForSeries(string seriesId)
    {
        var key =$"series-path-set-and-episode-ids:${seriesId}";
        if (DataCache.TryGetValue<(HashSet<string>, HashSet<string>)>(key, out var cached))
            return cached;

        var pathSet = new HashSet<string>();
        var episodeIds = new HashSet<string>();
        foreach (var file in await APIClient.GetFilesForSeries(seriesId)) {
            if (file.CrossReferences.Count == 1)
                foreach (var fileLocation in file.Locations)
                    pathSet.Add((Path.GetDirectoryName(fileLocation.Path) ?? "") + Path.DirectorySeparatorChar);
            var xref = file.CrossReferences.First(xref => xref.Series.Shoko.ToString() == seriesId);
            foreach (var episodeXRef in xref.Episodes)
                episodeIds.Add(episodeXRef.Shoko.ToString());
        }

        DataCache.Set(key, (pathSet, episodeIds), DefaultTimeSpan);
        return (pathSet, episodeIds);
    }

    #endregion
    #region File Info

    public async Task<(FileInfo?, SeriesInfo?, GroupInfo?)> GetFileInfoByPath(string path, Ordering.GroupFilterType? filterGroupByType)
    {
        // Use pointer for fast lookup.
        if (PathToFileIdAndSeriesIdDictionary.ContainsKey(path)) {
            var (fI, sI) = PathToFileIdAndSeriesIdDictionary[path];
            var fileInfo = await GetFileInfo(fI, sI);
            var seriesInfo = await GetSeriesInfo(sI);
            var groupInfo = filterGroupByType.HasValue ? await GetGroupInfoForSeries(sI, filterGroupByType.Value) : null;
            return new(fileInfo, seriesInfo, groupInfo);
        }

        // Strip the path and search for a match.
        var partialPath = StripMediaFolder(path);
        Logger.LogDebug("Looking for file matching {Path}", partialPath);
        var result = await APIClient.GetFileByPath(partialPath);
        Logger.LogTrace("Found result with {Count} matches for {Path}", result?.Count ?? 0, partialPath);

        // Check if we found a match.
        var file = result?.FirstOrDefault();
        if (file == null || file.CrossReferences.Count == 0)
            return (null, null, null);

        // Find the file locations matching the given path.
        var fileId = file.Id.ToString();
        var fileLocations = file.Locations
            .Where(location => location.Path.EndsWith(partialPath))
            .ToList();
        if (fileLocations.Count != 1) {
            if (fileLocations.Count == 0)
                throw new Exception($"I have no idea how this happened, but the path gave a file that doesn't have a matching file location. See you in #support. (File={fileId})");

            Logger.LogWarning("Multiple locations matched the path, picking the first location. (File={FileId})", fileId);
        }

        // Find the correct series based on the path.
        var selectedPath = (Path.GetDirectoryName(fileLocations.First().Path) ?? "") + Path.DirectorySeparatorChar;
        foreach (var seriesXRef in file.CrossReferences) {
            var seriesId = seriesXRef.Series.Shoko.ToString();

            // Check if the file is in the series folder.
            var pathSet = await GetPathSetForSeries(seriesId);
            if (!pathSet.Contains(selectedPath))
                continue;

            // Find the group info.
            GroupInfo? groupInfo = null;
            if (filterGroupByType.HasValue) {
                groupInfo =  await GetGroupInfoForSeries(seriesId, filterGroupByType.Value);
                if (groupInfo == null)
                    return (null, null, null);
            }

            // Find the series info.
            var seriesInfo = await GetSeriesInfo(seriesId);
            if (seriesInfo == null)
                return (null, null, null);

            // Find the file info for the series.
            var fileInfo = await CreateFileInfo(file, fileId, seriesId);

            // Add pointers for faster lookup.
            foreach (var episodeInfo in fileInfo.EpisodeList)
                EpisodeIdToEpisodePathDictionary.TryAdd(episodeInfo.Id, path);

            // Add pointers for faster lookup.
            PathToFileIdAndSeriesIdDictionary.TryAdd(path, (fileId, seriesId));
            PathToEpisodeIdsDictionary.TryAdd(path, fileInfo.EpisodeList.Select(episode => episode.Id).ToList());

            // Return the result.
            return new(fileInfo, seriesInfo, groupInfo);
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

        var file = await APIClient.GetFile(fileId);
        return await CreateFileInfo(file, fileId, seriesId);
    }

    private async Task<FileInfo> CreateFileInfo(File file, string fileId, string seriesId)
    {
        var cacheKey = $"file:{fileId}:{seriesId}";
        if (DataCache.TryGetValue<FileInfo>(cacheKey, out var fileInfo))
            return fileInfo;

        Logger.LogTrace("Creating info object for file. (File={FileId},Series={SeriesId})", fileId, seriesId);

        // Find the cross-references for the selected series.
        var seriesXRef = file.CrossReferences.FirstOrDefault(xref => xref.Series.Shoko.ToString() == seriesId);
        if (seriesXRef == null)
            throw new Exception($"Unable to find any cross-references for the specified series for the file. (File={fileId},Series={seriesId})");

        // Find a list of the episode info for each episode linked to the file for the series.
        var episodeList = new List<EpisodeInfo>();
        foreach (var episodeXRef in seriesXRef.Episodes) {
            var episodeId = episodeXRef.Shoko.ToString();
            var episodeInfo = await GetEpisodeInfo(episodeId);
            if (episodeInfo == null)
                throw new Exception($"Unable to find episode cross-reference for the specified series and episode for the file. (File={fileId},Episode={episodeId},Series={seriesId})");
            episodeList.Add(episodeInfo);
        }

        // Order the episodes.
        episodeList = episodeList
            .OrderBy(episode => episode.AniDB.Type)
            .ThenBy(episode => episode.AniDB.EpisodeNumber)
            .ToList();

        fileInfo = new FileInfo(file, episodeList, seriesId);

        DataCache.Set<FileInfo>(cacheKey, fileInfo, DefaultTimeSpan);
        FileIdToEpisodeIdDictionary.TryAdd(fileId, episodeList.Select(episode => episode.Id).ToList());
        return fileInfo;
    }

    public bool TryGetFileIdForPath(string path, out string? fileId)
    {
        if (!string.IsNullOrEmpty(path) && PathToFileIdAndSeriesIdDictionary.TryGetValue(path, out var pair)) {
            fileId = pair.Item1;
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

        var episode = await APIClient.GetEpisode(episodeId);
        return CreateEpisodeInfo(episode, episodeId);
    }

    private EpisodeInfo CreateEpisodeInfo(Episode episode, string episodeId)
    {
        var cacheKey = $"episode:{episodeId}";
        if (DataCache.TryGetValue<EpisodeInfo>(cacheKey, out var episodeInfo))
            return episodeInfo;

        Logger.LogTrace("Creating info object for episode {EpisodeName}. (Episode={EpisodeId})", episode.Name, episodeId);

        episodeInfo = new EpisodeInfo(episode);

        DataCache.Set<EpisodeInfo>(cacheKey, episodeInfo, DefaultTimeSpan);
        return episodeInfo;
    }

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

    public bool TryGetEpisodeIdsForFileId(string fileId, out List<string>? episodeIds)
    {
        if (string.IsNullOrEmpty(fileId)) {
            episodeIds = null;
            return false;
        }
        return FileIdToEpisodeIdDictionary.TryGetValue(fileId, out episodeIds);
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
    #region Series Info

    public async Task<SeriesInfo?> GetSeriesInfoByPath(string path)
    {
        var seriesId = await GetSeriesIdForPath(path);
        if (string.IsNullOrEmpty(seriesId))
            return null;

        var key = $"series:{seriesId}";
        if (DataCache.TryGetValue<SeriesInfo>(key, out var seriesInfo)) {
            Logger.LogTrace("Reusing info object for series {SeriesName}. (Series={SeriesId})", seriesInfo.Shoko.Name, seriesId);
            return seriesInfo;
        }

        var series = await APIClient.GetSeries(seriesId);
        return await CreateSeriesInfo(series, seriesId);
    }

    public async Task<SeriesInfo?> GetSeriesInfo(string seriesId)
    {
        if (string.IsNullOrEmpty(seriesId))
            return null;

        var cachedKey = $"series:{seriesId}";
        if (DataCache.TryGetValue<SeriesInfo>(cachedKey, out var seriesInfo)) {
            Logger.LogTrace("Reusing info object for series {SeriesName}. (Series={SeriesId})", seriesInfo.Shoko.Name, seriesId);
            return seriesInfo;
        }

        var series = await APIClient.GetSeries(seriesId);
        return await CreateSeriesInfo(series, seriesId);
    }

    public async Task<SeriesInfo?> GetSeriesInfoForEpisode(string episodeId)
    {
        if (!EpisodeIdToSeriesIdDictionary.TryGetValue(episodeId, out var seriesId)) {
            var series = await APIClient.GetSeriesFromEpisode(episodeId);
            if (series == null)
                return null;
            seriesId = series.IDs.Shoko.ToString();
        }

        return await GetSeriesInfo(seriesId);
    }

    private async Task<SeriesInfo> CreateSeriesInfo(Series series, string seriesId)
    {
        var cacheKey = $"series:{seriesId}";
        if (DataCache.TryGetValue<SeriesInfo>(cacheKey, out var seriesInfo)) {
            Logger.LogTrace("Reusing info object for series {SeriesName}. (Series={SeriesId})", seriesInfo.Shoko.Name, seriesId);
            return seriesInfo;
        }

        Logger.LogTrace("Creating info object for series {SeriesName}. (Series={SeriesId})", series.Name, seriesId);

        var episodes = (await APIClient.GetEpisodesFromSeries(seriesId) ?? new())
            .Select(e => CreateEpisodeInfo(e, e.IDs.Shoko.ToString()))
            .OrderBy(e => e.AniDB.AirDate)
            .ToList();
        var cast = await APIClient.GetSeriesCast(seriesId);
        var genres = await GetGenresForSeries(seriesId);
        var tags = await GetTagsForSeries(seriesId);

        seriesInfo = new SeriesInfo(series, episodes, cast, genres, tags);

        foreach (var episode in episodes)
            EpisodeIdToSeriesIdDictionary[episode.Id] = seriesId;
        DataCache.Set<SeriesInfo>(cacheKey, seriesInfo, DefaultTimeSpan);
        return seriesInfo;
    }

    public bool TryGetSeriesIdForPath(string path, out string? seriesId)
    {
        if (string.IsNullOrEmpty(path)) {
            seriesId = null;
            return false;
        }
        return PathToSeriesIdDictionary.TryGetValue(path, out seriesId);
    }

    public bool TryGetSeriesPathForId(string seriesId, out string? path)
    {
        if (string.IsNullOrEmpty(seriesId)) {
            path = null;
            return false;
        }
        return SeriesIdToPathDictionary.TryGetValue(seriesId, out path);
    }

    public bool TryGetGroupIdForSeriesId(string seriesId, out string? groupId)
    {
        if (string.IsNullOrEmpty(seriesId)) {
            groupId = null;
            return false;
        }
        return SeriesIdToGroupIdDictionary.TryGetValue(seriesId, out groupId);
    }

    private async Task<string?> GetSeriesIdForPath(string path)
    {
        // Reuse cached value.
        if (PathToSeriesIdDictionary.TryGetValue(path, out var seriesId))
            return seriesId;

        var partialPath = StripMediaFolder(path);
        Logger.LogDebug("Looking for series matching {Path}", partialPath);
        var result = await APIClient.GetSeriesPathEndsWith(partialPath);
        Logger.LogTrace("Found result with {Count} matches for {Path}", result.Count, partialPath);

        // Return the first match where the series unique paths partially match
        // the input path.
        foreach (var series in result)
        {
            seriesId  = series.IDs.Shoko.ToString();
            var pathSet = await GetPathSetForSeries(seriesId);
            foreach (var uniquePath in pathSet)
            {
                // Remove the trailing slash before matching.
                if (!uniquePath[..^1].EndsWith(partialPath))
                    continue;

                PathToSeriesIdDictionary[path] = seriesId;
                SeriesIdToPathDictionary.TryAdd(seriesId, path);

                return seriesId;
            }
        }

        // In the edge case for series with only files with multiple
        // cross-references we just return the first match.
        return result.FirstOrDefault()?.IDs.Shoko.ToString();
    }

    #endregion
    #region Group Info

    public async Task<GroupInfo?> GetGroupInfoByPath(string path, Ordering.GroupFilterType filterByType = Ordering.GroupFilterType.Default)
    {
        if (PathToSeriesIdDictionary.TryGetValue(path, out var seriesId)) {
            if (SeriesIdToGroupIdDictionary.TryGetValue(seriesId, out var groupId)) {
                if (DataCache.TryGetValue<GroupInfo>($"group:{filterByType}:{groupId}", out var groupInfo)) {
                    Logger.LogTrace("Reusing info object for group {GroupName}. (Series={seriesId},Group={GroupId})", groupInfo.Shoko.Name, seriesId, groupId);
                    return groupInfo;
                }

                return await GetGroupInfo(groupId, filterByType);
            }
        }
        else
        {
            seriesId = await GetSeriesIdForPath(path);
            if (string.IsNullOrEmpty(seriesId))
                return null;
        }

        return await GetGroupInfoForSeries(seriesId, filterByType);
    }

    public async Task<GroupInfo?> GetGroupInfo(string groupId, Ordering.GroupFilterType filterByType = Ordering.GroupFilterType.Default)
    {
        if (string.IsNullOrEmpty(groupId))
            return null;

        if (DataCache.TryGetValue<GroupInfo>($"group:{filterByType}:{groupId}", out var groupInfo)) {
            Logger.LogTrace("Reusing info object for group {GroupName}. (Group={GroupId})", groupInfo.Shoko.Name, groupId);
            return groupInfo;
        }

        var group = await APIClient.GetGroup(groupId);
        return await CreateGroupInfo(group, groupId, filterByType);
    }

    public async Task<GroupInfo?> GetGroupInfoForSeries(string seriesId, Ordering.GroupFilterType filterByType = Ordering.GroupFilterType.Default)
    {
        if (string.IsNullOrEmpty(seriesId))
            return null;

        if (!SeriesIdToGroupIdDictionary.TryGetValue(seriesId, out var groupId)) {
            var group = await APIClient.GetGroupFromSeries(seriesId);
            if (group == null)
                return null;

            groupId = group.IDs.Shoko.ToString();
            if (DataCache.TryGetValue<GroupInfo>($"group:{filterByType}:{groupId}", out var groupInfo)) {
                Logger.LogTrace("Reusing info object for group {GroupName}. (Series={SeriesId},Group={GroupId})", groupInfo.Shoko.Name, seriesId, groupId);   
                return groupInfo;
            }

            return await CreateGroupInfo(group, groupId, filterByType);
        }

        return await GetGroupInfo(groupId, filterByType);
    }

    private async Task<GroupInfo> CreateGroupInfo(Group group, string groupId, Ordering.GroupFilterType filterByType)
    {
        var cacheKey = $"group:{filterByType}:{groupId}";
        if (DataCache.TryGetValue<GroupInfo>(cacheKey, out var groupInfo)) {
            Logger.LogTrace("Reusing info object for group {GroupName}. (Group={GroupId})", groupInfo.Shoko.Name, groupId);   
            return groupInfo;
        }

        Logger.LogTrace("Creating info object for group {GroupName}. (Group={GroupId})", group.Name, groupId);

        var seriesList = (await APIClient.GetSeriesInGroup(groupId)
            .ContinueWith(task => Task.WhenAll(task.Result.Select(s => CreateSeriesInfo(s, s.IDs.Shoko.ToString()))))
            .Unwrap())
            .Where(s => s != null)
            .ToList();

        // Return early if no series matched the filter or if the list was empty.
        if (seriesList.Count == 0) {
            Logger.LogWarning("Creating an empty group info for filter {Filter}! (Group={GroupId})", filterByType.ToString(), groupId);

            groupInfo = new GroupInfo(group);

            DataCache.Set<GroupInfo>(cacheKey, groupInfo, DefaultTimeSpan);
            return groupInfo;
        }

        groupInfo = new GroupInfo(group, seriesList, filterByType);

        foreach (var series in seriesList)
            SeriesIdToGroupIdDictionary[series.Id] = groupId;
        DataCache.Set<GroupInfo>(cacheKey, groupInfo, DefaultTimeSpan);
        return groupInfo;
    }

    #endregion
    #region Post Process Library Changes

    public Task PostProcess(IProgress<double> progress, CancellationToken token)
    {
        Clear();
        return Task.CompletedTask;
    }

    #endregion
}
