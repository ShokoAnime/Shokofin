using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using Path = System.IO.Path;

#nullable enable
namespace Shokofin.API;

public class ShokoAPIManager : IDisposable
{
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
    }

    private GuardedMemoryCache DataCache = new(new MemoryCacheOptions() {
        ExpirationScanFrequency = ExpirationScanFrequency,
    });

    private static readonly TimeSpan ExpirationScanFrequency = new(0, 25, 0);

    private static readonly TimeSpan DefaultTimeSpan = new(2, 30, 0);

    #region Ignore rule

    public static string GetVirtualRootForMediaFolder(Folder mediaFolder)
        => Path.Combine(Plugin.Instance.VirtualRoot, mediaFolder.Id.ToString());

    public (Folder mediaFolder, string partialPath) FindMediaFolder(string path, Folder parent, Folder root)
    {
        Folder? mediaFolder = null;
        if (path.StartsWith(Plugin.Instance.VirtualRoot + Path.DirectorySeparatorChar)) {
            var mediaFolderId = Guid.Parse(path[(Plugin.Instance.VirtualRoot.Length + 1)..].Split(Path.DirectorySeparatorChar).First());
            mediaFolder = LibraryManager.GetItemById(mediaFolderId) as Folder;
            if (mediaFolder != null) {
                var mediaRootVirtualPath = GetVirtualRootForMediaFolder(mediaFolder);
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
        Clear(false);
    }

    public void Clear(bool restore = true)
    {
        Logger.LogDebug("Clearing data…");
        DataCache.Dispose();
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
        if (restore) {
            Logger.LogDebug("Initialising new cache…");
            DataCache = new(new MemoryCacheOptions() {
                ExpirationScanFrequency = ExpirationScanFrequency,
            });
        }
        Logger.LogDebug("Cleanup complete.");
    }

    #endregion
    #region Tags And Genres

    private async Task<string[]> GetTagsForSeries(string seriesId)
    {
        return (await APIClient.GetSeriesTags(seriesId, GetTagFilter()).ConfigureAwait(false))
            .Where(KeepTag)
            .Select(SelectTagName)
            .ToArray();
    }

    /// <summary>
    /// Get the tag filter
    /// </summary>
    /// <returns></returns>
    private static ulong GetTagFilter()
    {
        var config = Plugin.Instance.Configuration;
        ulong filter = 132L; // We exclude genres and source by default

        if (config.HideAniDbTags) filter |= 1 << 0;
        if (config.HideArtStyleTags) filter |= 1 << 1;
        if (config.HideMiscTags) filter |= 1 << 3;
        if (config.HideSettingTags) filter |= 1 << 5;
        if (config.HideProgrammingTags) filter |= 1 << 6;

        return filter;
    }

    public async Task<string[]> GetGenresForSeries(string seriesId)
    {
        // The following magic number is the filter value to allow only genres in the returned list.
        var genreSet = (await APIClient.GetSeriesTags(seriesId, 2147483776).ConfigureAwait(false))
            .Select(SelectTagName)
            .ToHashSet();
        var sourceGenre = await GetSourceGenre(seriesId).ConfigureAwait(false);
        genreSet.Add(sourceGenre);
        return genreSet.ToArray();
    }

    private async Task<string> GetSourceGenre(string seriesId)
    {
        // The following magic number is the filter value to allow only the source type in the returned list.
        return(await APIClient.GetSeriesTags(seriesId, 2147483652).ConfigureAwait(false))?.FirstOrDefault()?.Name?.ToLowerInvariant() switch {
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

    private bool KeepTag(Tag tag)
    {
        // Filter out unverified tags.
        if (Plugin.Instance.Configuration.HideUnverifiedTags && tag.IsVerified.HasValue && !tag.IsVerified.Value)
            return false;

        // Filter out any and all spoiler tags.
        if (Plugin.Instance.Configuration.HidePlotTags && (tag.IsLocalSpoiler ?? tag.IsSpoiler))
            return false;

        return true;
    }

    private string SelectTagName(Tag tag)
    {
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tag.Name);
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
        var (pathSet, _) = await GetPathSetAndLocalEpisodeIdsForSeries(seriesId).ConfigureAwait(false);
        return pathSet;
    }

    /// <summary>
    /// Get a set of local episode ids for the series.
    /// </summary>
    /// <param name="seriesId">Shoko series id.</param>
    /// <returns>Local episode ids for the series</returns>
    public HashSet<string> GetLocalEpisodeIdsForSeries(string seriesId)
    {
        var (_, episodeIds) = GetPathSetAndLocalEpisodeIdsForSeries(seriesId)
            .ConfigureAwait(false)
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
        foreach (var file in await APIClient.GetFilesForSeries(seriesId).ConfigureAwait(false)) {
            if (file.CrossReferences.Count == 1)
                foreach (var fileLocation in file.Locations)
                    pathSet.Add((Path.GetDirectoryName(fileLocation.Path) ?? string.Empty) + Path.DirectorySeparatorChar);
            var xref = file.CrossReferences.First(xref => xref.Series.Shoko.ToString() == seriesId);
            foreach (var episodeXRef in xref.Episodes)
                episodeIds.Add(episodeXRef.Shoko.ToString());
        }

        DataCache.Set(key, (pathSet, episodeIds), DefaultTimeSpan);
        return (pathSet, episodeIds);
    }

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
        if (file == null || file.CrossReferences.Count == 0)
        {
            Logger.LogTrace("Found no match for {Path}", partialPath);
            return (null, null, null);
        }

        // Find the file locations matching the given path.
        var fileId = file.Id.ToString();
        var fileLocations = file.Locations
            .Where(location => location.Path.EndsWith(partialPath))
            .ToList();
        Logger.LogTrace("Found a file match for {Path} (File={FileId})", partialPath, file.Id.ToString());
        if (fileLocations.Count != 1) {
            if (fileLocations.Count == 0)
                throw new Exception($"I have no idea how this happened, but the path gave a file that doesn't have a matching file location. See you in #support. (File={fileId})");

            Logger.LogWarning("Multiple locations matched the path, picking the first location. (File={FileId})", fileId);
        }

        // Find the correct series based on the path.
        var selectedPath = (Path.GetDirectoryName(fileLocations.First().Path) ?? string.Empty) + Path.DirectorySeparatorChar;
        foreach (var seriesXRef in file.CrossReferences) {
            var seriesId = seriesXRef.Series.Shoko.ToString();

            // Check if the file is in the series folder.
            var pathSet = await GetPathSetForSeries(seriesId).ConfigureAwait(false);
            if (!pathSet.Contains(selectedPath))
                continue;

            // Find the season info.
            var seasonInfo = await GetSeasonInfoForSeries(seriesId).ConfigureAwait(false);
            if (seasonInfo == null)
                return (null, null, null);

            // Find the show info.
            var showInfo =  await GetShowInfoForSeries(seriesId).ConfigureAwait(false);
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

        var file = await APIClient.GetFile(fileId).ConfigureAwait(false);
        return await CreateFileInfo(file, fileId, seriesId).ConfigureAwait(false);
    }

    private static readonly EpisodeType[] EpisodePickOrder = { EpisodeType.Special, EpisodeType.Normal, EpisodeType.Other };

    private Task<FileInfo> CreateFileInfo(File file, string fileId, string seriesId)
        => DataCache.GetOrCreateAsync(
            $"file:{fileId}:{seriesId}",
            async (_) => {
                Logger.LogTrace("Creating info object for file. (File={FileId},Series={SeriesId})", fileId, seriesId);

                // Find the cross-references for the selected series.
                var seriesXRef = file.CrossReferences.FirstOrDefault(xref => xref.Series.Shoko.ToString() == seriesId) ??
                    throw new Exception($"Unable to find any cross-references for the specified series for the file. (File={fileId},Series={seriesId})");

                // Find a list of the episode info for each episode linked to the file for the series.
                var episodeList = new List<EpisodeInfo>();
                foreach (var episodeXRef in seriesXRef.Episodes) {
                    var episodeId = episodeXRef.Shoko.ToString();
                    var episodeInfo = await GetEpisodeInfo(episodeId).ConfigureAwait(false) ??
                        throw new Exception($"Unable to find episode cross-reference for the specified series and episode for the file. (File={fileId},Episode={episodeId},Series={seriesId})");
                    if (episodeInfo.Shoko.IsHidden) {
                        Logger.LogDebug("Skipped hidden episode linked to file. (File={FileId},Episode={EpisodeId},Series={SeriesId})", fileId, episodeId, seriesId);
                        continue;
                    }
                    episodeList.Add(episodeInfo);
                }

                // Group and order the episodes.
                var groupedEpisodeLists = episodeList
                    .GroupBy(episode => episode.AniDB.Type)
                    .OrderByDescending(a => Array.IndexOf(EpisodePickOrder, a.Key))
                    .Select(epList => epList.OrderBy(episode => episode.AniDB.EpisodeNumber).ToList())
                    .ToList();

                var fileInfo = new FileInfo(file, groupedEpisodeLists, seriesId);

                FileAndSeriesIdToEpisodeIdDictionary[$"{fileId}:{seriesId}"] = episodeList.Select(episode => episode.Id).ToList();
                return fileInfo;
            },
            new() {
                AbsoluteExpirationRelativeToNow = DefaultTimeSpan,
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
            },
            new() {
                AbsoluteExpirationRelativeToNow = DefaultTimeSpan,
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

        var key = $"season:{seriesId}";
        if (DataCache.TryGetValue<SeasonInfo>(key, out var seasonInfo)) {
            Logger.LogTrace("Reusing info object for season {SeriesName}. (Series={SeriesId})", seasonInfo.Shoko.Name, seriesId);
            return seasonInfo;
        }

        var series = await APIClient.GetSeries(seriesId).ConfigureAwait(false);
        return await CreateSeasonInfo(series, seriesId).ConfigureAwait(false);
    }

    public async Task<SeasonInfo?> GetSeasonInfoForSeries(string seriesId)
    {
        if (string.IsNullOrEmpty(seriesId))
            return null;

        var cachedKey = $"season:{seriesId}";
        if (DataCache.TryGetValue<SeasonInfo>(cachedKey, out var seasonInfo)) {
            Logger.LogTrace("Reusing info object for season {SeriesName}. (Series={SeriesId})", seasonInfo.Shoko.Name, seriesId);
            return seasonInfo;
        }

        var series = await APIClient.GetSeries(seriesId).ConfigureAwait(false);
        return await CreateSeasonInfo(series, seriesId).ConfigureAwait(false);
    }

    public async Task<SeasonInfo?> GetSeasonInfoForEpisode(string episodeId)
    {
        if (!EpisodeIdToSeriesIdDictionary.TryGetValue(episodeId, out var seriesId)) {
            var series = await APIClient.GetSeriesFromEpisode(episodeId).ConfigureAwait(false);
            if (series == null)
                return null;
            seriesId = series.IDs.Shoko.ToString();
            return await CreateSeasonInfo(series, seriesId).ConfigureAwait(false);
        }

        return await GetSeasonInfoForSeries(seriesId).ConfigureAwait(false);
    }

    private Task<SeasonInfo> CreateSeasonInfo(Series series, string seriesId)
        => DataCache.GetOrCreateAsync(
            $"season:{seriesId}",
            (seasonInfo) => Logger.LogTrace("Reusing info object for season {SeriesName}. (Series={SeriesId})", seasonInfo.Shoko.Name, seriesId),
            async (cachedEntry) => {
                Logger.LogTrace("Creating info object for season {SeriesName}. (Series={SeriesId})", series.Name, seriesId);

                var episodes = (await APIClient.GetEpisodesFromSeries(seriesId).ConfigureAwait(false) ?? new()).List
                    .Select(e => CreateEpisodeInfo(e, e.IDs.Shoko.ToString()))
                    .Where(e => !e.Shoko.IsHidden)
                    .OrderBy(e => e.AniDB.AirDate)
                    .ToList();
                var cast = await APIClient.GetSeriesCast(seriesId).ConfigureAwait(false);
                var relations = await APIClient.GetSeriesRelations(seriesId).ConfigureAwait(false);
                var genres = await GetGenresForSeries(seriesId).ConfigureAwait(false);
                var tags = await GetTagsForSeries(seriesId).ConfigureAwait(false);

                var seasonInfo = new SeasonInfo(series, episodes, cast, relations, genres, tags);

                foreach (var episode in episodes)
                    EpisodeIdToSeriesIdDictionary.TryAdd(episode.Id, seriesId);
                return seasonInfo;
            },
            new() {
                AbsoluteExpirationRelativeToNow = DefaultTimeSpan,
            }
        );

    #endregion
    #region Series Helpers

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

    public bool TryGetDefaultSeriesIdForSeriesId(string seriesId, out string? defaultSeriesId)
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
        foreach (var series in result)
        {
            seriesId  = series.IDs.Shoko.ToString();
            var pathSet = await GetPathSetForSeries(seriesId).ConfigureAwait(false);
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
        EpisodeIdToSeriesIdDictionary.TryAdd(episodeId, seriesId);
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

        // If we found a movie, and we're assiging movies as stand-alone shows, and we didn't create a stand-alone show
        // above, then attach the stand-alone show to the parent group of the group that might other
        if (seasonInfo.Type == SeriesType.Movie && Plugin.Instance.Configuration.SeparateMovies)
            return GetOrCreateShowInfoForSeasonInfo(seasonInfo, group.Size > 0 ? group.IDs.ParentGroup.ToString() : null);

        return await CreateShowInfoForGroup(group, group.IDs.Shoko.ToString()).ConfigureAwait(false);
    }

    private Task<ShowInfo?> CreateShowInfoForGroup(Group group, string groupId)
        => DataCache.GetOrCreateAsync(
            $"show:by-group-id:{groupId}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {GroupName}. (Group={GroupId})", showInfo?.Name, groupId),
            async (cachedEntry) => {
                Logger.LogTrace("Creating info object for show {GroupName}. (Group={GroupId})", group.Name, groupId);

                var seriesInGroup = await APIClient.GetSeriesInGroup(groupId).ConfigureAwait(false);
                var seasonList = (await Task.WhenAll(seriesInGroup.Select(s => CreateSeasonInfo(s, s.IDs.Shoko.ToString()))).ConfigureAwait(false)).ToList();

                var length = seasonList.Count;
                if (Plugin.Instance.Configuration.SeparateMovies) {
                    seasonList = seasonList.Where(s => s.Type != SeriesType.Movie).ToList();

                    // Return early if no series matched the filter or if the list was empty.
                    if (seasonList.Count == 0) {
                        Logger.LogWarning("Creating an empty show info for filter! (Group={GroupId})", groupId);

                        cachedEntry.AbsoluteExpirationRelativeToNow = DefaultTimeSpan;
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
            },
            new() {
                AbsoluteExpirationRelativeToNow = DefaultTimeSpan,
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
            },
            new() {
                AbsoluteExpirationRelativeToNow = DefaultTimeSpan,
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

                        groupList.Add(subCollectionInfo);
                    }
                }

                Logger.LogTrace("Finalising info object for collection {GroupName}. (Group={GroupId})", group.Name, groupId);
                var showList = showDict.Values.ToList();
                var collectionInfo = new CollectionInfo(group, showList, groupList);
                return collectionInfo;
            },
            new() {
                AbsoluteExpirationRelativeToNow = DefaultTimeSpan,
            }
        );

    #endregion
}
