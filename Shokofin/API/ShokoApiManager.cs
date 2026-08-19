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
using Shokofin.API.Info.AniDB;
using Shokofin.API.Info.Shoko;
using Shokofin.API.Info.TMDB;
using Shokofin.API.Models;
using Shokofin.API.Models.Shoko;
using Shokofin.API.Models.TMDB;
using Shokofin.Configuration;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using ContentRating = Shokofin.Utils.ContentRating;
using Path = System.IO.Path;
using Regex = System.Text.RegularExpressions.Regex;
using RegexOptions = System.Text.RegularExpressions.RegexOptions;

namespace Shokofin.API;

public partial class ShokoApiManager : IDisposable {
    // Note: This regex will only get uglier with time.
    [System.Text.RegularExpressions.GeneratedRegex(@"\s+\((?<year>\d{4})(?: dai [2-9] (?:bu|cour))?\)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex YearRegex();

    private readonly ILogger<ShokoApiManager> Logger;

    private readonly ShokoApiClient ApiClient;

    private readonly ILibraryManager LibraryManager;

    private readonly UsageTracker UsageTracker;

    private readonly GuardedMemoryCache DataCache;

    private readonly object MediaFolderListLock = new();

    private readonly List<Folder> MediaFolderList = [];

    private readonly ConcurrentDictionary<string, string> PathToSeasonIdDictionary = new();

    private readonly ConcurrentDictionary<string, List<string>> PathToEpisodeIdsDictionary = new();

    private readonly ConcurrentDictionary<string, (string FileId, string SeriesId)> PathToFileIdAndSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> SeasonIdToShowIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> EpisodeIdToSeasonIdDictionary = new();

    private readonly ConcurrentDictionary<string, List<string>> FileAndSeasonIdToEpisodeIdDictionary = new();

    public ShokoApiManager(ILogger<ShokoApiManager> logger, ShokoApiClient apiClient, ILibraryManager libraryManager, UsageTracker usageTracker) {
        Logger = logger;
        ApiClient = apiClient;
        LibraryManager = libraryManager;
        UsageTracker = usageTracker;
        DataCache = new(
            logger,
            new() { ExpirationScanFrequency = Plugin.Instance.Configuration.Debug.ExpirationScanFrequency },
            new() { AbsoluteExpirationRelativeToNow = Plugin.Instance.Configuration.Debug.AbsoluteExpirationRelativeToNow }
        );
        UsageTracker.Stalled += OnTrackerStalled;
    }

    ~ShokoApiManager() {
        UsageTracker.Stalled -= OnTrackerStalled;
    }

    private void OnTrackerStalled(object? sender, EventArgs eventArgs) {
        if (Plugin.Instance.Configuration.Debug.AutoClearManagerCache)
            Clear();
    }

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
    public (Folder mediaFolder, string partialPath) FindMediaFolder(string path, Folder parent) {
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
    public string StripMediaFolder(string fullPath) {
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

    public void Dispose() {
        GC.SuppressFinalize(this);
        Clear();
    }

    public void Clear() {
        Logger.LogDebug("Clearing data…");
        EpisodeIdToSeasonIdDictionary.Clear();
        FileAndSeasonIdToEpisodeIdDictionary.Clear();
        lock (MediaFolderListLock)
            MediaFolderList.Clear();
        PathToEpisodeIdsDictionary.Clear();
        PathToFileIdAndSeriesIdDictionary.Clear();
        PathToSeasonIdDictionary.Clear();
        SeasonIdToShowIdDictionary.Clear();
        DataCache.Clear();
        Logger.LogDebug("Cleanup complete.");
    }

    #endregion

    #region Series Settings

    internal Task<SeriesConfiguration> GetInternalSeriesConfiguration(string id)
        => DataCache.GetOrCreateAsync($"series-settings-raw:{id}", async () => {
            var tags = await GetNamespacedTagsForSeries(id);
            var seriesSettings = new SeriesConfiguration() {
                Type = SeriesType.None,
                StructureType = SeriesStructureType.None,
                SeasonOrdering = Ordering.OrderType.None,
                SpecialsPlacement = Ordering.SpecialOrderType.None,
                SeasonMergingBehavior = SeasonMergingBehavior.None,
                EpisodeConversion = SeriesEpisodeConversion.None,
                OrderByAirdate = false,
            };
            if (tags.TryGetValue("/custom user tags/series type", out var seriesTypeTag) &&
                seriesTypeTag.Children.Count is >= 1 &&
                Enum.TryParse<SeriesType>(NormalizeCustomSeriesType(seriesTypeTag.Children.Keys.First()), ignoreCase: true, out var seriesType) &&
                seriesType is not SeriesType.None
            )
                seriesSettings.Type = seriesType;

            if (!tags.TryGetValue("/custom user tags/shokofin", out var customTags))
                return seriesSettings;

            tags = customTags.RecursiveNamespacedChildren;
            if (tags.ContainsKey("/structure/anidb"))
                seriesSettings.StructureType = SeriesStructureType.AniDB_Anime;
            else if (tags.ContainsKey("/structure/shoko"))
                seriesSettings.StructureType = SeriesStructureType.Shoko_Groups;
            else if (tags.ContainsKey("/structure/tmdb"))
                seriesSettings.StructureType = SeriesStructureType.TMDB_SeriesAndMovies;

            if (tags.ContainsKey("/season ordering/default"))
                seriesSettings.SeasonOrdering = Ordering.OrderType.Default;
            else if (tags.ContainsKey("/season ordering/release"))
                seriesSettings.SeasonOrdering = Ordering.OrderType.ReleaseDate;
            else if (tags.ContainsKey("/season ordering/chronological"))
                seriesSettings.SeasonOrdering = Ordering.OrderType.Chronological;
            else if (tags.ContainsKey("/season ordering/simplified chronological"))
                seriesSettings.SeasonOrdering = Ordering.OrderType.ChronologicalIgnoreIndirect;

            if (tags.ContainsKey("/specials placement/excluded"))
                seriesSettings.SpecialsPlacement = Ordering.SpecialOrderType.Excluded;
            else if (tags.ContainsKey("/specials placement/after season"))
                seriesSettings.SpecialsPlacement = Ordering.SpecialOrderType.AfterSeason;
            else if (tags.ContainsKey("/specials placement/mixed"))
                seriesSettings.SpecialsPlacement = Ordering.SpecialOrderType.InBetweenSeasonMixed;
            else if (tags.ContainsKey("/specials placement/air date"))
                seriesSettings.SpecialsPlacement = Ordering.SpecialOrderType.InBetweenSeasonByAirDate;
            else if (tags.ContainsKey("/specials placement/tmdb"))
                seriesSettings.SpecialsPlacement = Ordering.SpecialOrderType.InBetweenSeasonByOtherData;

            if (tags.ContainsKey("/merge/none")) {
                seriesSettings.SeasonMergingBehavior = SeasonMergingBehavior.NoMerge;
            }
            else {
                if (tags.ContainsKey("/merge/forward"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeForward;
                if (tags.ContainsKey("/merge/backward"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeBackward;

                if (tags.ContainsKey("/merge/main story"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeWithMainStory;

                if (tags.ContainsKey("/merge/group/a/source"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeGroupASource;
                else if (tags.ContainsKey("/merge/group/a/target"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeGroupATarget;

                if (tags.ContainsKey("/merge/group/b/source"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeGroupBSource;
                else if (tags.ContainsKey("/merge/group/b/target"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeGroupBTarget;

                if (tags.ContainsKey("/merge/group/c/source"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeGroupCSource;
                else if (tags.ContainsKey("/merge/group/c/target"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeGroupCTarget;

                if (tags.ContainsKey("/merge/group/d/source"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeGroupDSource;
                else if (tags.ContainsKey("/merge/group/d/target"))
                    seriesSettings.SeasonMergingBehavior |= SeasonMergingBehavior.MergeGroupDTarget;
            }

            if (tags.ContainsKey("/episodes as specials"))
                seriesSettings.EpisodeConversion = SeriesEpisodeConversion.EpisodesAsSpecials;
            else if (tags.ContainsKey("/specials as episodes"))
                seriesSettings.EpisodeConversion = SeriesEpisodeConversion.SpecialsAsEpisodes;
            else if (tags.ContainsKey("/specials as extra featurettes"))
                seriesSettings.EpisodeConversion = SeriesEpisodeConversion.SpecialsAsExtraFeaturettes;

            if (tags.ContainsKey("/order by airdate"))
                seriesSettings.OrderByAirdate = true;

            if (tags.ContainsKey("/include in playlists"))
                seriesSettings.PlaylistInclude = true;

            return seriesSettings;
        });

    private Task<SeriesConfiguration> GetSeriesConfiguration(string id)
        => DataCache.GetOrCreateAsync($"series-configuration:{id}", async () => {
            var seriesSettings = await GetInternalSeriesConfiguration(id);
            var config = Plugin.Instance.Configuration;
            if (seriesSettings.Type is SeriesType.None) {
                var series = await ApiClient.GetShokoSeries(id);
                seriesSettings.Type = series?.AniDB.Type ?? SeriesType.Other;
            }
            if (seriesSettings.StructureType is SeriesStructureType.None) {
                seriesSettings.StructureType = config.DefaultLibraryStructure;
            }
            if (seriesSettings.SeasonOrdering is Ordering.OrderType.None) {
                seriesSettings.SeasonOrdering = config.DefaultSeasonOrdering;
            }
            if (seriesSettings.SpecialsPlacement is Ordering.SpecialOrderType.None) {
                seriesSettings.SpecialsPlacement = config.DefaultSpecialsPlacement;
            }
            if (seriesSettings.SeasonMergingBehavior is SeasonMergingBehavior.None) {
                seriesSettings.SeasonMergingBehavior = config.SeasonMerging_DefaultBehavior;
            }
            if (config.MovieSpecialsAsExtraFeaturettes && seriesSettings.Type is SeriesType.Movie) {
                seriesSettings.EpisodeConversion = SeriesEpisodeConversion.SpecialsAsExtraFeaturettes;
            }
            return seriesSettings;
        });

    private static string NormalizeCustomSeriesType(string seriesType) {
        seriesType = seriesType.ToLowerInvariant().Replace(" ", "");
        if (seriesType[^1] == 's')
          seriesType = seriesType[..^1];
        return seriesType;
    }

    #endregion

    #region Tags, Genres, And Content Ratings

    public Task<IReadOnlyDictionary<string, ResolvedTag>> GetNamespacedTagsForSeries(string seriesId)
        => DataCache.GetOrCreateAsync<IReadOnlyDictionary<string, ResolvedTag>>(
            $"series-linked-tags:{seriesId}",
            async () => {
                var nextUserTagId = 1;
                var hasCustomTags = false;
                var rootTags = new List<Tag>();
                var tagMap = new Dictionary<string, List<Tag>>();
                var tags = (await ApiClient.GetTagsForShokoSeries(seriesId))
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
                    .ToDictionary(tag => tag.FullName, StringComparer.InvariantCultureIgnoreCase);
                // We reassign the children because they may have been moved to a different namespace.
                foreach (var groupBy in allResolvedTags.Values.GroupBy(tag => tag.Namespace).OrderByDescending(pair => pair.Key)) {
                    if (!allResolvedTags.TryGetValue(groupBy.Key[..^1], out var nsTag))
                        continue;
                    nsTag.Children = groupBy.ToDictionary(childTag => childTag.Name, StringComparer.InvariantCultureIgnoreCase);
                    nsTag.RecursiveNamespacedChildren = nsTag.Children.Values
                        .SelectMany(childTag => childTag.RecursiveNamespacedChildren.Values.Prepend(childTag))
                        .ToDictionary(childTag => childTag.FullName[nsTag.FullName.Length..], StringComparer.InvariantCultureIgnoreCase);
                }
                return allResolvedTags;
            }
        );

    private async Task<string[]> GetTagsForSeries(string seriesId) {
        var tags = await GetNamespacedTagsForSeries(seriesId);
        return TagFilter.FilterTags(tags);
    }

    private async Task<string[]> GetGenresForSeries(string seriesId) {
        var tags = await GetNamespacedTagsForSeries(seriesId);
        return TagFilter.FilterGenres(tags);
    }

    private async Task<string[]> GetProductionLocations(string seriesId) {
        var tags = await GetNamespacedTagsForSeries(seriesId);
        return TagFilter.GetProductionCountriesFromTags(tags);
    }

    private async Task<string?> GetAssumedContentRating(string seriesId) {
        var tags = await GetNamespacedTagsForSeries(seriesId);
        return ContentRating.GetTagBasedContentRating(tags);
    }

    #endregion

    #region Path Set And Local Episode IDs

    /// <summary>
    /// Get a set of paths that are unique to the series and don't belong to
    /// any other series.
    /// </summary>
    /// <param name="seriesId">Shoko series id.</param>
    /// <returns>Unique path set for the series</returns>
    public Task<HashSet<string>> GetPathSetForSeries(string seriesId)
        => DataCache.GetOrCreateAsync(
                $"series-path-set:${seriesId}",
                async () => {
                    var pathSet = new HashSet<string>();
                    foreach (var file in await ApiClient.GetFilesForShokoSeries(seriesId)) {
                        if (file.CrossReferences.Count == 1 && file.CrossReferences[0] is { } xref && xref.Series.Shoko.HasValue && xref.Series.Shoko.ToString() == seriesId)
                            foreach (var fileLocation in file.Locations)
                                pathSet.Add((Path.GetDirectoryName(fileLocation.RelativePath) ?? string.Empty) + Path.DirectorySeparatorChar);
                    }

                    return pathSet;
                }
            );

    /// <summary>
    /// Get a set of local episode ids for the series.
    /// </summary>
    /// <param name="seasonInfo">Season info.</param>
    /// <returns>Local episode ids for the series</returns>
    public Task<HashSet<string>> GetLocalEpisodeIdsForSeason(SeasonInfo seasonInfo)
        => DataCache.GetOrCreateAsync(
            $"season-episode-ids:${seasonInfo.Id}",
            async () => {
                var episodeIds = new HashSet<string>();
                foreach (var seasonId in new HashSet<string>([seasonInfo.Id, ..seasonInfo.ExtraIds])) {
                    switch (seasonId[0]) {
                        case IdPrefix.TmdbShow: {
                            var files = await ApiClient.GetFilesForTmdbSeason(seasonId[1..]);
                            var episodes = await ApiClient.GetTmdbEpisodesInTmdbSeason(seasonId[1..]);
                            foreach (var episode in episodes) {
                                if (files.Any(file => file.CrossReferences.Any(fileXref => fileXref.Episodes.Any(episodeXref => episodeXref.TMDB.Episode.Contains(episode.Id)))))
                                    episodeIds.Add(IdPrefix.TmdbShow + episode.Id.ToString());
                            }
                            break;
                        }

                        case IdPrefix.TmdbMovie: {
                            var files = await ApiClient.GetFilesForTmdbMovie(seasonId[1..]);
                            if (files.Count > 0)
                                episodeIds.Add(seasonId);
                            break;
                        }

                        case IdPrefix.TmdbMovieCollection: {
                            var movies = (await ApiClient.GetTmdbMoviesInMovieCollection(seasonId[1..]))
                                .Select(m => m.Id)
                                .ToHashSet();
                            foreach (var episodeInfo in seasonInfo.EpisodeList) {
                                var episodeFiles = await ApiClient.GetFilesForTmdbMovie(episodeInfo.Id[1..]);
                                var movieId = int.Parse(episodeInfo.Id[1..]);
                                foreach (var file in episodeFiles) {
                                    if (file.CrossReferences.FirstOrDefault(x => x.Series.Shoko.HasValue && x.Episodes.Any(e => e.Shoko.HasValue && e.TMDB.Movie.Contains(movieId))) is not { } xref)
                                        continue;

                                    episodeIds.Add(IdPrefix.TmdbMovie + movieId.ToString());
                                }
                            }
                            break;
                        }

                        default: {
                            var files = await ApiClient.GetFilesForShokoSeries(seasonId);
                            foreach (var file in files) {
                                var xref = file.CrossReferences.FirstOrDefault(xref => xref.Series.Shoko.HasValue && xref.Series.Shoko.ToString() == seasonId);
                                foreach (var episodeXRef in xref?.Episodes.Where(e => e.Shoko.HasValue) ?? [])
                                    episodeIds.Add(episodeXRef.Shoko!.Value.ToString());
                            }
                            break;
                        }
                    }
                }

                return episodeIds;
            },
            new()
        );

    #endregion

    #region File Info

    internal void AddFileLookupIds(string path, string fileId, string seriesId, IEnumerable<string> episodeIds) {
        PathToFileIdAndSeriesIdDictionary.TryAdd(path, (fileId, seriesId));
        PathToEpisodeIdsDictionary.TryAdd(path, [.. episodeIds]);
    }

    public async Task<(FileInfo?, SeasonInfo?, ShowInfo?)> GetFileInfoByPath(string path) {
        // Use pointer for fast lookup.
        if (PathToFileIdAndSeriesIdDictionary.TryGetValue(path, out (string FileId, string SeriesId) tuple)) {
            var (fI, sI) = tuple;
            var fileInfo = await GetFileInfo(fI, sI);
            if (fileInfo == null || fileInfo.EpisodeList.Count is 0)
                return (null, null, null);

            var selectedSeasonId = fileInfo.EpisodeList[0].Episode.SeasonId;
            var seasonInfo = await GetSeasonInfo(selectedSeasonId);
            if (seasonInfo == null)
                return (null, null, null);

            var showInfo = await GetShowInfoBySeasonId(selectedSeasonId);
            if (showInfo == null)
                return (null, null, null);

            return new(fileInfo, seasonInfo, showInfo);
        }

        // Fast-path for VFS.
        if (path.StartsWith(Plugin.Instance.VirtualRoot + Path.DirectorySeparatorChar)) {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!fileName.TryGetAttributeValue(ProviderNames.ShokoSeries, out var sI) || !int.TryParse(sI, out _))
                return (null, null, null);
            if (!fileName.TryGetAttributeValue(ProviderNames.ShokoFile, out var fI) || !int.TryParse(fI, out _))
                return (null, null, null);

            var fileInfo = await GetFileInfo(fI, sI);
            if (fileInfo == null || fileInfo.EpisodeList.Count is 0)
                return (null, null, null);

            var selectedSeasonId = fileInfo.EpisodeList[0].Episode.SeasonId;
            var seasonInfo = await GetSeasonInfo(selectedSeasonId);
            if (seasonInfo == null)
                return (null, null, null);

            var showInfo = await GetShowInfoBySeasonId(selectedSeasonId);
            if (showInfo == null)
                return (null, null, null);

            AddFileLookupIds(path, fI, sI, fileInfo.EpisodeList.Select(episode => episode.Id));
            return (fileInfo, seasonInfo, showInfo);
        }

        // Strip the path and search for a match.
        var partialPath = StripMediaFolder(path);
        var result = await ApiClient.GetFileByPath(partialPath);
        Logger.LogDebug("Looking for a match for {Path}", partialPath);

        // Check if we found a match.
        var file = result is { Count: > 0 } ? result[0] : null;
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
            var pathSet = await GetPathSetForSeries(seriesId);
            if (!pathSet.Contains(selectedPath))
                continue;

            // Find the file info for the series.
            var fileInfo = await CreateFileInfo(file, fileId, seriesId);
            if (fileInfo.EpisodeList.Count is 0)
                return (null, null, null);

            var seasonId = fileInfo.EpisodeList[0].Episode.SeasonId;
            var seasonInfo = await GetSeasonInfo(seasonId);
            if (seasonInfo == null)
                return (null, null, null);

            var showInfo = await GetShowInfoBySeasonId(seasonId);
            if (showInfo == null)
                return (null, null, null);

            // Add pointers for faster lookup.
            AddFileLookupIds(path, fileId, seriesId, fileInfo.EpisodeList.Select(episode => episode.Id));

            // Return the result.
            return new(fileInfo, seasonInfo, showInfo);
        }

        throw new Exception($"Unable to determine the series to use for the file based on it's location because the file resides within a mixed folder with multiple AniDB anime in it. You will either have to fix your file structure or use the VFS to avoid this issue. (File={fileId})\nFile location; {path}");
    }

    public async Task<FileInfo?> GetFileInfo(string fileId, string seriesId) {
        if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(seriesId))
            return null;

        var cacheKey = $"file:{fileId}:{seriesId}";
        if (DataCache.TryGetValue<FileInfo>(cacheKey, out var fileInfo))
            return fileInfo;

        if (await ApiClient.GetFile(fileId) is not { } file)
            return null;

        return await CreateFileInfo(file, fileId, seriesId);
    }

    private static readonly EpisodeType[] EpisodePickOrder = [EpisodeType.Special, EpisodeType.Episode, EpisodeType.Other];

    private Task<FileInfo> CreateFileInfo(File file, string fileId, string seriesId)
        => DataCache.GetOrCreateAsync(
            $"file:{fileId}:{seriesId}",
            async () => {
                Logger.LogTrace("Creating info object for file. (File={FileId},Series={SeriesId})", fileId, seriesId);

                // Find the cross-references for the selected series.
                var seriesConfig = await GetSeriesConfiguration(seriesId);
                var seriesXRef = file.CrossReferences
                    .Where(xref => xref.Series.Shoko.HasValue && xref.Episodes.All(e => e.Shoko.HasValue))
                    .FirstOrDefault(xref => xref.Series.Shoko!.Value.ToString() == seriesId) ??
                    throw new Exception($"Unable to find any cross-references for the specified series for the file. (File={fileId},Series={seriesId})");

                // Find a list of the episode info for each episode linked to the file for the series.
                var episodeList = new List<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)>();
                foreach (var episodeXRef in seriesXRef.Episodes) {
                    var episodeId = episodeXRef.Shoko!.Value.ToString();
                    if (await ApiClient.GetShokoEpisode(episodeId) is not { } episode) {
                        Logger.LogDebug("Skipped unknown episode linked to file. (File={FileId},Episode={EpisodeId},Series={SeriesId})", fileId, episodeId, seriesId);
                        continue;
                    }

                    if (episode.IsHidden) {
                        Logger.LogDebug("Skipped hidden episode linked to file. (File={FileId},Episode={EpisodeId},Series={SeriesId})", fileId, episodeId, seriesId);
                        continue;
                    }

                    if (seriesConfig.StructureType is SeriesStructureType.TMDB_SeriesAndMovies) {
                        var tmdbEpisodes = await Task.WhenAll(episodeXRef.TMDB.Episode.Select(id => GetEpisodeInfo(IdPrefix.TmdbShow + id.ToString())));
                        foreach (var tmdbEpisode in tmdbEpisodes) {
                            if (tmdbEpisode == null)
                                continue;
                            episodeList.Add((tmdbEpisode, episodeXRef, tmdbEpisode.Id));
                        }

                        var tmdbMovies = await Task.WhenAll(episodeXRef.TMDB.Movie.Select(id => GetEpisodeInfo(IdPrefix.TmdbMovie + id.ToString())));
                        foreach (var tmdbMovie in tmdbMovies) {
                            if (tmdbMovie == null)
                                continue;
                            episodeList.Add((tmdbMovie, episodeXRef, tmdbMovie.Id));
                        }
                        continue;
                    }
                    var episodeInfo = await GetEpisodeInfo(episodeId) ??
                        throw new Exception($"Unable to find episode cross-reference for the specified series and episode for the file. (File={fileId},Episode={episodeId},Series={seriesId})");
                    episodeList.Add((episodeInfo, episodeXRef, episodeId));
                }

                // Distinct the list in case the shoko episodes are linked to the same tmdb episode(s)/movie(s).
                if (seriesConfig.StructureType is SeriesStructureType.TMDB_SeriesAndMovies) {
                    episodeList = [.. episodeList.DistinctBy(tuple => tuple.Id)];
                }

                // Group and order the episodes, then select the first group to use.
                var groupedEpisodeLists = episodeList
                    .GroupBy(tuple => (type: tuple.Episode.Type, group: tuple.CrossReference.Percentage.Group, isStandalone: tuple.Episode.IsStandalone))
                    .OrderByDescending(a => Array.IndexOf(EpisodePickOrder, a.Key.type))
                    .ThenBy(a => a.Key.group)
                    .ThenByDescending(a => a.Key.isStandalone)
                    .Select(epList => epList.OrderBy(tuple => tuple.Episode.SeasonNumber).ThenBy(tuple => tuple.Episode.EpisodeNumber).ToList() as IReadOnlyList<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)> ?? [])
                    .ToList();
                var selectedEpisodeList = groupedEpisodeLists.FirstOrDefault() ?? [];
                var fileInfo = new FileInfo(file, seriesId, selectedEpisodeList);

                FileAndSeasonIdToEpisodeIdDictionary[$"{fileId}:{seriesId}"] = [.. episodeList.Select(episode => episode.Id)];

                return fileInfo;
            }
        );

    public bool TryGetFileAndSeriesIdForPath(string path, [NotNullWhen(true)] out string? fileId, [NotNullWhen(true)] out string? seriesId) {
        if (string.IsNullOrEmpty(path)) {
            fileId = null;
            seriesId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (PathToFileIdAndSeriesIdDictionary.TryGetValue(path, out var pair)) {
            fileId = pair.FileId;
            seriesId = pair.SeriesId;
            return true;
        }

        // Slow path; getting the show from cache or remote and finding the default season's id.
        Logger.LogDebug("Trying to find file id using the slow path. (Path={FullPath})", path);
        try {
            if (Task.Run(() => GetFileInfoByPath(path)).GetAwaiter().GetResult() is { } tuple && tuple.Item1 is not null) {
                var (fileInfo, _, _) = tuple;
                fileId = fileInfo.Id;
                seriesId = fileInfo.SeriesId;
                return true;
            }
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Encountered an error while trying to lookup the file id for path. (Path={Path})", path);
        }

        fileId = null;
        seriesId = null;
        return false;
    }

    #endregion

    #region Episode Info

    public async Task<EpisodeInfo?> GetEpisodeInfo(string episodeId) {
        if (string.IsNullOrEmpty(episodeId))
            return null;

        if (DataCache.TryGetValue<EpisodeInfo>($"episode:{episodeId}", out var episodeInfo))
            return episodeInfo;

        switch (episodeId[0]) {
            case IdPrefix.TmdbShow:
                if (await ApiClient.GetTmdbEpisode(episodeId[1..]) is not { } tmdbEpisode)
                    return null;

                if (await ApiClient.GetTmdbShowForSeason(tmdbEpisode.SeasonId) is not { } tmdbShow)
                    return null;

                return await CreateEpisodeInfo(tmdbEpisode, tmdbShow);

            case IdPrefix.TmdbMovie:
                if (await ApiClient.GetTmdbMovie(episodeId[1..]) is not { } tmdbMovie)
                    return null;

                return await CreateEpisodeInfo(tmdbMovie);

            default:
                if (await ApiClient.GetShokoEpisode(episodeId) is not { } shokoEpisode)
                    return null;

                return await CreateEpisodeInfo(shokoEpisode);
        }
    }

    private Task<EpisodeInfo> CreateEpisodeInfo(TmdbMovie movie)
        => DataCache.GetOrCreateAsync(
            $"episode:{IdPrefix.TmdbMovie}{movie.Id}",
            async () => {
                Logger.LogTrace("Creating info object for episode {EpisodeName}. (Source=TMDB,Movie={MovieId})", movie.Title, movie.Id);

                var episodeList = await ApiClient.GetShokoEpisodesForTmdbMovie(movie.Id.ToString());
                var anidbEpisodes = episodeList
                    .Select(shokoEpisode => shokoEpisode.AniDB.ToInfo())
                    .ToArray();
                var shokoEpisodes = episodeList
                    .Select(shokoEpisode => shokoEpisode.ToInfo())
                    .ToArray();
                return new EpisodeInfo(ApiClient, movie, shokoEpisodes, anidbEpisodes);
            }
        );

    private Task<EpisodeInfo> CreateEpisodeInfo(TmdbEpisode episode, TmdbShow show)
        => DataCache.GetOrCreateAsync(
            $"episode:{IdPrefix.TmdbShow}{episode.Id}",
            async () => {
                Logger.LogTrace("Creating info object for episode {EpisodeName}. (Source=TMDB,Episode={EpisodeId})", episode.Title, episode.Id);

                var episodeList = await ApiClient.GetShokoEpisodesForTmdbEpisode(episode.Id.ToString());
                var anidbEpisodes = episodeList
                    .Select(shokoEpisode => shokoEpisode.AniDB.ToInfo())
                    .ToArray();
                var shokoEpisodes = episodeList
                    .Select(shokoEpisode => shokoEpisode.ToInfo())
                    .ToArray();
                return new EpisodeInfo(ApiClient, episode, show, shokoEpisodes, anidbEpisodes);
            }
        );

    private Task<EpisodeInfo> CreateEpisodeInfo(ShokoEpisode episode)
        => DataCache.GetOrCreateAsync(
            $"episode:{episode.Id}",
            async () => {
                Logger.LogTrace("Creating info object for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", episode.Name, episode.Id);

                var (cast, genres, tags, productionLocations, contentRating) = await GetExtraEpisodeDetailsForShokoSeries(episode.IDs.ParentSeries.ToString());

                ITmdbEntity? tmdbEntity = null;
                ITmdbParentEntity? tmdbParentEntity = null;
                var tmdbMovies = new List<TmdbMovieInfo>();
                var tmdbEpisodes = new List<TmdbEpisodeInfo>();
                foreach (var tmdbMovieId in episode.IDs.TMDB.Movie) {
                    Logger.LogTrace("Trying to find TMDB movie {MovieId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbMovieId, episode.Name, episode.Id);
                    if (await ApiClient.GetTmdbMovie(tmdbMovieId.ToString()) is not { } tmdbMovie) {
                        Logger.LogTrace("Did not find TMDB movie {MovieId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbMovieId, episode.Name, episode.Id);
                        continue;
                    }

                    tmdbMovies.Add(tmdbMovie.ToInfo());
                    tmdbEntity ??= tmdbMovie;
                    Logger.LogTrace("Found TMDB movie {MovieId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbMovieId, episode.Name, episode.Id);

                }

                foreach (var tmdbEpisodeId in episode.IDs.TMDB.Episode) {
                    Logger.LogTrace("Trying to find TMDB episode {EpisodeId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbEpisodeId, episode.Name, episode.Id);
                    if (await ApiClient.GetTmdbEpisode(tmdbEpisodeId.ToString(), useDefaultOrdering: true) is not { } tmdbEpisode) {
                        Logger.LogTrace("Did not find TMDB episode {EpisodeId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbEpisodeId, episode.Name, episode.Id);
                        continue;
                    }

                    tmdbEpisodes.Add(tmdbEpisode.ToInfo());
                    tmdbEntity ??= tmdbEpisode;
                    Logger.LogTrace("Found TMDB episode {EpisodeId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbEpisodeId, episode.Name, episode.Id);

                    if (await ApiClient.GetTmdbShowForSeason(tmdbEpisode.SeasonId) is { } tmdbShow) {
                        tmdbParentEntity = tmdbShow;
                        Logger.LogTrace("Found TMDB show {ShowId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbShow.Id, episode.Name, episode.Id);
                    }

                }

                return new EpisodeInfo(ApiClient, episode, cast, [.. genres], [.. tags], productionLocations, contentRating, [.. tmdbMovies], [.. tmdbEpisodes], tmdbEntity, tmdbParentEntity);
            }
        );

    private Task<(IReadOnlyList<Role>, string[], string[], string[], string?)> GetExtraEpisodeDetailsForShokoSeries(string seriesId)
        => DataCache.GetOrCreateAsync(
            $"series-episode-details:{seriesId}",
            async () => {
                var cast = await ApiClient.GetCastForShokoSeries(seriesId);
                var genres = await GetGenresForSeries(seriesId);
                var tags = await GetTagsForSeries(seriesId);
                var productionLocations = await GetProductionLocations(seriesId);
                var contentRating = await GetAssumedContentRating(seriesId);
                return (cast, genres, tags, productionLocations, contentRating);
            }
        );

    #endregion

    #region Episode Id Helpers

    public bool TryGetEpisodeIdsForPath(string path, [NotNullWhen(true)] out List<string>? episodeIds) {
        if (string.IsNullOrEmpty(path)) {
            episodeIds = null;
            return false;
        }

        // Fast path; using the lookup.
        if (PathToEpisodeIdsDictionary.TryGetValue(path, out episodeIds))
            return true;

        // Slow path; getting the show from cache or remote and finding the default season's id.
        Logger.LogDebug("Trying to find episode ids using the slow path. (Path={FullPath})", path);
        try {
            if (Task.Run(() => GetFileInfoByPath(path)).GetAwaiter().GetResult() is { } tuple && tuple.Item1 is not null) {
                var (fileInfo, _, _) = tuple;
                episodeIds = [.. fileInfo.EpisodeList.Select(episodeInfo => episodeInfo.Id)];
                return episodeIds.Count is > 0;
            }
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Encountered an error while trying to lookup the episode id for path. (Path={Path})", path);
        }

        episodeIds = null;
        return false;
    }

    public bool TryGetEpisodeIdsForFileId(string fileId, string seriesId, [NotNullWhen(true)] out List<string>? episodeIds) {
        if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(seriesId)) {
            episodeIds = null;
            return false;
        }

        // Fast path; using the lookup.
        if (FileAndSeasonIdToEpisodeIdDictionary.TryGetValue($"{fileId}:{seriesId}", out episodeIds))
            return true;

        Logger.LogDebug("Trying to find episode ids using the slow path. (Series={SeriesId},File={FileId})", seriesId, fileId);
        try {
            // Slow path; getting the show from cache or remote and finding the default season's id.
            if (Task.Run(() => GetFileInfo(fileId, seriesId)).GetAwaiter().GetResult() is { } fileInfo) {
                episodeIds = [.. fileInfo.EpisodeList.Select(episodeInfo => episodeInfo.Id)];
                return true;
            }
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Encountered an error while trying to lookup the episode ids for file and series ids. (Series={SeriesId},File={FileId})", fileId, seriesId);
        }

        episodeIds = null;
        return false;
    }

    #endregion

    #region Season Info

    public async Task<SeasonInfo?> GetSeasonInfo(string seasonId) {
        if (string.IsNullOrEmpty(seasonId))
            return null;

        if (DataCache.TryGetValue<SeasonInfo>($"season:{seasonId}", out var seasonInfo))
            return seasonInfo;

        switch (seasonId[0]) {
            case IdPrefix.TmdbShow:
                if (await ApiClient.GetTmdbSeason(seasonId[1..]) is not { } tmdbSeason)
                    return null;

                if (await ApiClient.GetTmdbShowForSeason(tmdbSeason.Id) is not { } tmdbShow)
                    return null;

                return await CreateSeasonInfo(tmdbSeason, tmdbShow);

            case IdPrefix.TmdbMovie:
                if (await ApiClient.GetTmdbMovie(seasonId[1..]) is not { } tmdbMovie)
                    return null;

                return await CreateSeasonInfo(tmdbMovie);

            case IdPrefix.TmdbMovieCollection:
                if (await ApiClient.GetTmdbMovieCollection(seasonId[1..]) is not { } tmdbMovieCollection)
                    return null;

                return await CreateSeasonInfo(tmdbMovieCollection);

            default:
                if (await ApiClient.GetShokoSeries(seasonId) is not { } shokoSeries)
                    return null;

                return await CreateSeasonInfo(shokoSeries);
        }
    }

    public async Task<SeasonInfo?> GetSeasonInfoByPath(string path) {
        if (!PathToSeasonIdDictionary.TryGetValue(path, out var seasonId)) {
            seasonId = await GetSeasonIdForPath(path);
            if (string.IsNullOrEmpty(seasonId))
                return null;
        }

        return await GetSeasonInfo(seasonId);
    }

    public async Task<SeasonInfo?> GetSeasonInfoForEpisode(string episodeId) {
        if (string.IsNullOrEmpty(episodeId))
            return null;

        if (EpisodeIdToSeasonIdDictionary.TryGetValue(episodeId, out var seasonId))
            return await GetSeasonInfo(seasonId);

        switch (episodeId[0]) {
            case IdPrefix.TmdbShow:
                if (await ApiClient.GetTmdbSeasonForTmdbEpisode(episodeId[1..]) is not { } tmdbSeason)
                    return null;

                if (await ApiClient.GetTmdbShowForSeason(tmdbSeason.Id) is not { } tmdbShow)
                    return null;

                return await CreateSeasonInfo(tmdbSeason, tmdbShow);

            case IdPrefix.TmdbMovie:
                if (await ApiClient.GetTmdbMovie(episodeId[1..]) is not { } tmdbMovie)
                    return null;

                var episodeInfo = await CreateEpisodeInfo(tmdbMovie);
                return await GetSeasonInfo(episodeInfo.SeasonId);

            default:
                if (await ApiClient.GetShokoSeriesForShokoEpisode(episodeId) is not { } shokoSeries)
                    return null;

                return await CreateSeasonInfo(shokoSeries);
        }
    }

    public Task<IReadOnlyList<SeasonInfo>> GetSeasonInfosForShokoSeries(string seriesId)
        => DataCache.GetOrCreateAsync<IReadOnlyList<SeasonInfo>>(
            $"seasons-by-series-id:{seriesId}",
            (seasons) => Logger.LogTrace("Reusing info objects for seasons. (Series={SeriesId})", seriesId),
            async () => {
                Logger.LogTrace("Creating info objects for seasons for series {SeriesName}. (Series={SeriesId})", seriesId, seriesId);
                if (await ApiClient.GetShokoSeries(seriesId) is not { } series)
                    return [];

                var seriesConfig = await GetSeriesConfiguration(seriesId);
                if (seriesConfig.StructureType is SeriesStructureType.TMDB_SeriesAndMovies) {
                    var seasons = new List<SeasonInfo>();
                    var episodeXrefs = await ApiClient.GetTmdbCrossReferencesForShokoSeries(seriesId);
                    var showIds = episodeXrefs
                        .GroupBy(x => x.TmdbShowId)
                        .OrderByDescending(x => x.Count())
                        .Select(x => x.Key)
                        .Except([0])
                        .ToList();
                    foreach (var showId in showIds) {
                        var episodes = (await ApiClient.GetTmdbEpisodesInTmdbShow(showId.ToString())).ToDictionary(e => e.Id);
                        var seasonIds = episodeXrefs
                            .Where(x => x.TmdbShowId == showId)
                            .GroupBy(x => episodes.TryGetValue(x.TmdbEpisodeId, out var e) ? e.SeasonId : string.Empty)
                            .OrderByDescending(x => x.Count())
                            .Select(x => x.Key)
                            .Except([string.Empty])
                            .ToList();
                        foreach (var seasonId in seasonIds) {
                            if (await GetSeasonInfo(IdPrefix.TmdbShow + seasonId) is not { } seasonInfo)
                                continue;

                            seasons.Add(seasonInfo);
                        }
                    }
                    foreach (var movieId in series.IDs.TMDB.Movie) {
                        if (await GetSeasonInfo(IdPrefix.TmdbMovie + movieId.ToString()) is not { } seasonInfo)
                            continue;

                        seasons.Add(seasonInfo);
                    }

                    return seasons;
                }
                else {
                    if (await GetSeasonInfo(seriesId) is not { } seasonInfo)
                        return [];

                    return [seasonInfo];
                }
            }
        );

    private Task<SeasonInfo> CreateSeasonInfo(TmdbMovie tmdbMovie)
        => DataCache.GetOrCreateAsync(
            $"season:{IdPrefix.TmdbMovie}{tmdbMovie.Id}",
            (seasonInfo) => Logger.LogTrace("Reusing info object for season {SeasonTitle}. (Source=TMDB,Movie={MovieId})", seasonInfo.Title, tmdbMovie.Id),
            async () => {
                Logger.LogTrace("Creating info object for season {SeasonTitle}. (Source=TMDB,Movie={MovieId})", tmdbMovie.Title, tmdbMovie.Id);

                var episodeInfo = await CreateEpisodeInfo(tmdbMovie);
                var animeIds = (await ApiClient.GetTmdbCrossReferencesForTmdbMovie(tmdbMovie.Id.ToString()))
                    .GroupBy(x => x.AnidbAnimeId)
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.Key)
                    .Except([0])
                    .ToList();
                var (topLevelShokoGroupId, anidbAnime, shokoSeries) = await GetGroupIdsForAnidbAnime(animeIds, $"TMDB movie \"{tmdbMovie.Title}\"", $"Movie=\"{tmdbMovie.Id}\"");
                return new SeasonInfo(ApiClient, tmdbMovie, episodeInfo, topLevelShokoGroupId, anidbAnime, shokoSeries);
            });

    private Task<SeasonInfo> CreateSeasonInfo(TmdbMovieCollection tmdbMovieCollection)
        => DataCache.GetOrCreateAsync(
            $"season:{IdPrefix.TmdbMovieCollection}{tmdbMovieCollection.Id}",
            (seasonInfo) => Logger.LogTrace("Reusing info object for season {SeasonTitle}. (Source=TMDB,MovieCollection={MovieId})", seasonInfo.Title, tmdbMovieCollection.Id),
            async () => {
                Logger.LogTrace("Creating info object for season {SeasonTitle}. (Source=TMDB,MovieCollection={MovieId})", tmdbMovieCollection.Title, tmdbMovieCollection.Id);

                var moviesInCollection = await ApiClient.GetTmdbMoviesInMovieCollection(tmdbMovieCollection.Id.ToString());
                var episodeInfos = await Task.WhenAll(moviesInCollection.Select(tmdbMovie => CreateEpisodeInfo(tmdbMovie)));
                var animeIds = (await Task.WhenAll(moviesInCollection.Select(tmdbMovie => ApiClient.GetTmdbCrossReferencesForTmdbMovie(tmdbMovie.Id.ToString()))))
                    .SelectMany(x => x)
                    .GroupBy(x => x.AnidbAnimeId)
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.Key)
                    .Except([0])
                    .ToList();
                var (topLevelShokoGroupId, anidbAnime, shokoSeries) = await GetGroupIdsForAnidbAnime(animeIds, $"TMDB movie collection \"{tmdbMovieCollection.Title}\"", $"MovieCollection=\"{tmdbMovieCollection.Id}\"");
                return new SeasonInfo(ApiClient, tmdbMovieCollection, moviesInCollection, episodeInfos, topLevelShokoGroupId, anidbAnime, shokoSeries);
            });

    private Task<SeasonInfo> CreateSeasonInfo(TmdbSeason tmdbSeason, TmdbShow tmdbShow)
        => DataCache.GetOrCreateAsync(
            $"season:{IdPrefix.TmdbShow}{tmdbSeason.Id}",
            (seasonInfo) => Logger.LogTrace("Reusing info object for season {SeasonTitle}. (Source=TMDB,Season={SeasonId},Show={ShowId})", seasonInfo.Title, tmdbSeason.Id, tmdbSeason.ShowId),
            async () => {
                Logger.LogTrace("Creating info object for season {SeasonTitle}. (Source=TMDB,Season={SeasonId},Show={ShowId})", tmdbSeason.Title, tmdbSeason.Id, tmdbSeason.ShowId);

                var tmdbEpisodes = (await ApiClient.GetTmdbEpisodesInTmdbSeason(tmdbSeason.Id))
                    .ToDictionary(e => e.Id);
                var episodeInfos = await Task.WhenAll(tmdbEpisodes.Values.Select(tmdbEpisode => CreateEpisodeInfo(tmdbEpisode, tmdbShow)));
                var animeIds = (await ApiClient.GetTmdbCrossReferencesForTmdbShow(tmdbSeason.ShowId.ToString()))
                    .Where(x => tmdbEpisodes.TryGetValue(x.TmdbEpisodeId, out var tmdbEpisode) && tmdbEpisode.SeasonId == tmdbSeason.Id)
                    .GroupBy(x => x.AnidbAnimeId)
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.Key)
                    .Except([0])
                    .ToList();
                var (topLevelShokoGroupId, anidbAnime, shokoSeries) = await GetGroupIdsForAnidbAnime(animeIds, $"season {tmdbSeason.SeasonNumber} in TMDB show \"{tmdbShow.Title}\"", $"Season=\"{tmdbSeason.Id}\",Show=\"{tmdbSeason.ShowId}\"");
                return new SeasonInfo(ApiClient, tmdbSeason, tmdbShow, episodeInfos, topLevelShokoGroupId, anidbAnime, shokoSeries);
            });

#pragma warning disable CA2254 // Template should be a static method
    private async Task<(string? topLevelShokoGroupId, AnidbAnimeInfo[] anidbAnime, ShokoSeriesInfo[] shokoSeries)> GetGroupIdsForAnidbAnime(IReadOnlyList<int> animeIds, string entryName, string entryId) {
        Logger.LogTrace($"Found {{AnidbAnimeCount}} AniDB anime for {entryName} to pick a Shoko Group to use. (Anime={{AnimeIds}},{entryId})", animeIds.Count, animeIds);

        if (animeIds.Count is 0)
            return (null, [], []);

        string? topLevelShokoGroupId = null;
        var anidbAnimeList = new List<AnidbAnimeInfo>();
        var shokoSeriesList = new List<ShokoSeriesInfo>();
        foreach (var animeId in animeIds) {
            if (await ApiClient.GetShokoSeriesForAnidbAnime(animeId.ToString()) is not { } shokoSeries)
                continue;

            anidbAnimeList.Add(new() {
                AnidbAnimeId = animeId.ToString(),
            });

            shokoSeriesList.Add(new() {
                ShokoSeriesId = shokoSeries.Id,
                ShokoGroupId = shokoSeries.IDs.ParentGroup.ToString(),
                TopLevelShokoGroupId = shokoSeries.IDs.TopLevelGroup.ToString(),
            });

            Logger.LogTrace($"Found Shoko series to use for {entryName}. (Anime={{AnimeId}},Series={{SeriesId}},Group={{GroupId}},{entryId})", animeId, shokoSeries.Id, shokoSeries.IDs.ParentGroup.ToString());
        }

        var topLevelGroupIdCount = shokoSeriesList
            .GroupBy(x => x.TopLevelShokoGroupId)
            .OrderByDescending(x => x.Count())
            .Select(x => x.Key)
            .ToList();
        if (topLevelGroupIdCount.Count is 1) {
            topLevelShokoGroupId = topLevelGroupIdCount[0];
            Logger.LogDebug($"Multiple Shoko groups in the same top-level groups linked to {entryName}. (Anime={{AnimeIds}},TopLevelGroup={{TopLevelGroupId}},{entryId})", animeIds, topLevelShokoGroupId);
        }
        else if (shokoSeriesList.Count is > 0) {
            Logger.LogDebug($"Multiple Shoko groups in multiple top-level groups linked to {entryName}. (Anime={{AnimeIds}},{entryId})", animeIds);
        }
        else {
            Logger.LogDebug($"Could not find Shoko group for {entryName}. ({entryId})");
        }

        return (topLevelShokoGroupId, [..anidbAnimeList], [..shokoSeriesList]);
    }
#pragma warning restore CA2254 // Template should be a static method

    private async Task<SeasonInfo> CreateSeasonInfo(ShokoSeries series) {
        var (primaryId, extraIds) = await GetSeriesIdsForSeason(series);
        return await DataCache.GetOrCreateAsync(
            $"season:{primaryId}",
            (seasonInfo) => Logger.LogTrace("Reusing info object for season {SeasonTitle}. (Source=Shoko,Series={SeriesId},ExtraSeries={ExtraIds})", seasonInfo.Title, primaryId, extraIds),
            async () => {
                // We updated the "primary" series id for the merge group, so fetch the new series details from the client cache.
                if (!string.Equals(series.Id, primaryId, StringComparison.Ordinal))
                    series = await ApiClient.GetShokoSeries(primaryId)
                        ?? throw new InvalidOperationException("Could not find series with id " + primaryId);

                Logger.LogTrace("Creating info object for season {SeasonTitle}. (Source=Shoko,Series={SeriesId},ExtraSeries={ExtraIds})", series.Name, primaryId, extraIds);

                var episodes = (await Task.WhenAll(
                    extraIds.Prepend(primaryId)
                        .Select(id => ApiClient.GetShokoEpisodesInShokoSeries(id)
                            .ContinueWith(task => Task.WhenAll(task.Result.Select(CreateEpisodeInfo)))
                            .Unwrap()
                        )
                ))
                    .SelectMany(list => list)
                    .ToList();

                ITmdbEntity? tmdbEntity = null;
                List<TmdbSeasonInfo> tmdbSeasons = [];
                if (series.IDs.TMDB.Show.Count > 0 || series.IDs.TMDB.Movie.Count > 0) {
                    if (series.IDs.TMDB.Show.Count > 0) {
                        Logger.LogTrace("Found {TmdbShowCount} TMDB shows for Shoko Series {SeriesTitle} to pick a season to use. (Series={SeriesId})", series.IDs.TMDB.Show.Count, series.Name, primaryId);

                        var episodeXrefs = await ApiClient.GetTmdbCrossReferencesForShokoSeries(primaryId);
                        var showIds = episodeXrefs
                            .GroupBy(x => x.TmdbShowId)
                            .OrderByDescending(x => x.Count())
                            .Select(x => x.Key)
                            .Except([0])
                            .ToList();
                        foreach (var showId in showIds) {
                            var tmdbEpisodes = (await ApiClient.GetTmdbEpisodesInTmdbShow(showId.ToString())).ToDictionary(e => e.Id);
                            var seasonIds = episodeXrefs
                                .Where(x => x.TmdbShowId == showId)
                                .GroupBy(x => tmdbEpisodes.TryGetValue(x.TmdbEpisodeId, out var e) ? e.SeasonId : string.Empty)
                                .OrderByDescending(x => x.Count())
                                .ExceptBy([string.Empty], x => x.Key)
                                .ToDictionary(x => x.Key, x => x.Count());

                            Logger.LogTrace("Found {TmdbSeasonCount} TMDB seasons to potentially use. (Series={SeriesId},Show={ShowId})", seasonIds.Count, primaryId, showId);

                            var fullyMatchedSeasons = 0;
                            foreach (var (seasonId, matchedEpisodeCount) in seasonIds) {
                                if (await ApiClient.GetTmdbSeason(seasonId) is { } tmdbSeason0) {
                                    if (tmdbSeason0.SeasonNumber is 0) {
                                        Logger.LogTrace("Found season zero for Shoko Series {SeriesTitle}. Skipping season match. (Series={SeriesId},Season={SeasonId},Show={ShowId})", series.Name, primaryId, tmdbSeason0.Id, tmdbSeason0.ShowId);
                                        continue;
                                    }

                                    tmdbSeasons.Add(tmdbSeason0.ToInfo());
                                    tmdbEntity ??= tmdbSeason0;
                                    Logger.LogTrace("Found TMDB season {TmdbSeasonTitle} for Shoko Series {SeriesTitle}. (Series={SeriesId},Season={SeasonId},Show={ShowId})", tmdbSeason0.Title, series.Name, primaryId, tmdbSeason0.Id, tmdbSeason0.ShowId);

                                    // If the Shoko Series is fully matched to more than one TMDB season that's not season zero, then switch to using the show instead.
                                    if (tmdbSeason0.EpisodeCount == matchedEpisodeCount) {
                                        fullyMatchedSeasons++;
                                    }
                                }
                            }
                            if (tmdbEntity is TmdbSeason tmdbSeason1 && tmdbSeason1.ShowId == showId && fullyMatchedSeasons > 1) {
                                if (await ApiClient.GetTmdbShowForSeason(tmdbSeason1.Id) is { } tmdbShow) {
                                    tmdbEntity = tmdbShow;
                                    Logger.LogTrace("Found multiple TMDB seasons for Shoko Series {SeriesTitle}, so switched to show {ShowName} instead. (Series={SeriesId})", series.Name, tmdbShow.Title, primaryId);
                                }
                            }
                        }
                    }

                    if (tmdbEntity is null && series.IDs.TMDB.Movie.Count > 0) {
                        Logger.LogTrace("Found {TmdbMovieCount} TMDB movies for Shoko Series {SeriesTitle} to pick a movie collection to use. (Series={SeriesId})", series.IDs.TMDB.Movie.Count, series.Name, primaryId);

                        var collectionIds = new List<int>();
                        foreach (var movieId in series.IDs.TMDB.Movie) {
                            if (await ApiClient.GetTmdbMovie(movieId.ToString()) is not { } tmdbMovie ||
                                !tmdbMovie.CollectionId.HasValue)
                                continue;

                            collectionIds.Add(tmdbMovie.CollectionId.Value);
                        }

                        collectionIds = [.. collectionIds
                            .GroupBy(x => x)
                            .OrderByDescending(x => x.Count())
                            .Select(x => x.Key)];
                        foreach (var collectionId in collectionIds) {
                            if (await ApiClient.GetTmdbMovieCollection(collectionId.ToString()) is { } tmdbCollection) {
                                tmdbEntity = tmdbCollection;
                                Logger.LogTrace("Found TMDB movie collection {TmdbCollectionTitle} for Shoko Series {SeriesTitle}. (Series={SeriesId},Collection={CollectionId})", tmdbCollection.Title, series.Name, primaryId, tmdbCollection.Id);
                                break;
                            }
                        }
                    }

                    if (tmdbEntity is null)
                        Logger.LogTrace("Could not find TMDB entity to use for Shoko Series {SeriesTitle}. (Series={SeriesId})", series.Name, primaryId);
                }

                SeasonInfo seasonInfo;
                if (extraIds.Count > 0) {
                    var detailsIds = extraIds.Prepend(primaryId).ToList();

                    // Create the tasks.
                    var relationsTasks = detailsIds.Select(id => ApiClient.GetRelationsForShokoSeries(id));
                    var seriesConfigurationsTasks = detailsIds.Select(id => GetSeriesConfiguration(id));

                    // Await the tasks in order.
                    var relations = (await Task.WhenAll(relationsTasks))
                        .SelectMany(r => r)
                        .Where(r => r.RelatedIDs.Shoko.HasValue && !detailsIds.Contains(r.RelatedIDs.Shoko.Value.ToString()))
                        .ToList();
                    var seriesConfigurations = (await Task.WhenAll(seriesConfigurationsTasks))
                        .Select((t, i) => (t, i))
                        .ToDictionary(t => detailsIds[t.i], (t) => t.t);

                    // Create the season info using the merged details.
                    seasonInfo = new SeasonInfo(ApiClient, series, extraIds, episodes, relations, tmdbEntity, seriesConfigurations, [.. tmdbSeasons]);
                }
                else {
                    var relations = await ApiClient.GetRelationsForShokoSeries(primaryId);
                    var seriesConfigurations = new Dictionary<string, SeriesConfiguration>() { { primaryId, await GetSeriesConfiguration(primaryId) },
                    };
                    seasonInfo = new SeasonInfo(ApiClient, series, extraIds, episodes, relations, tmdbEntity, seriesConfigurations, [.. tmdbSeasons]);
                }

                foreach (var episode in episodes)
                    EpisodeIdToSeasonIdDictionary.TryAdd(episode.Id, primaryId);

                return seasonInfo;
            }
        );
    }

    #endregion

    #region Series Merging

    public async Task<(string primaryId, List<string> extraIds)> GetSeriesIdsForShokoSeries(string seriesId) {
        if (await ApiClient.GetShokoSeries(seriesId) is not { } shokoSeries)
            return (seriesId, []);

        return await GetSeriesIdsForSeason(shokoSeries);
    }

    private Task<(string primaryId, List<string> extraIds)> GetSeriesIdsForSeason(ShokoSeries series)
        => DataCache.GetOrCreateAsync(
            $"season-series-ids:{series.Id}",
            (tuple) => {
                var config = Plugin.Instance.Configuration;
                if (!config.SeasonMerging_Enabled)
                    return;

                Logger.LogTrace("Reusing existing series-to-season mapping for series. (Series={SeriesId},ExtraSeries={ExtraIds})", tuple.primaryId, tuple.extraIds);
            },
            async () => {
                var primaryId = series.Id;
                var extraIds = new List<string>();
                var config = Plugin.Instance.Configuration;
                if (!config.SeasonMerging_Enabled)
                    return (primaryId, extraIds);

                Logger.LogTrace("Creating new series-to-season mapping for series. (Series={SeriesId})", primaryId);

                var seriesConfig = await GetSeriesConfiguration(series.Id);
                if (seriesConfig.SeasonMergingBehavior is SeasonMergingBehavior.NoMerge)
                    return (primaryId, extraIds);

                if (seriesConfig.StructureType is not SeriesStructureType.Shoko_Groups)
                    return (primaryId, extraIds);

                if (seriesConfig.SeasonMergingBehavior is SeasonMergingBehavior.None && !config.SeasonMerging_SeriesTypes.Contains(seriesConfig.Type))
                    return (primaryId, extraIds);

                if (series.AniDB.AirDate is null)
                    return (primaryId, extraIds);

                // We potentially have a "follow-up" season candidate, so look for the "primary" season candidate, then jump into that.
                var relations = await ApiClient.GetRelationsForShokoSeries(primaryId);
                var mainTitle = series.AniDB.Titles.First(title => title.Type == TitleType.Main).Value;
                var maxDaysThreshold = config.SeasonMerging_MergeWindowInDays;
                var adjustedMainTitle = AdjustMainTitle(mainTitle) ?? mainTitle;
                var currentSeries = series;
                var currentDate = currentSeries.AniDB.AirDate.Value;
                var currentRelations = relations;
                var currentConfig = seriesConfig;
                var groupId = currentSeries.IDs.ParentGroup;
                while (currentRelations.Count > 0) {
                    foreach (
                        var prequelRelation in currentRelations
                            .Where(relation => relation.Type is RelationType.Prequel or RelationType.MainStory && relation.RelatedIDs.Shoko.HasValue)
                            .OrderBy(relation => relation.Type is RelationType.Prequel)
                            .ThenBy(relation => relation.Type)
                            .ThenBy(relation => relation.RelatedIDs.AniDB)
                    ) {
                        if (await ApiClient.GetShokoSeries(prequelRelation.RelatedIDs.Shoko!.Value.ToString()) is not { } prequelSeries)
                            continue;

                        if (prequelSeries.IDs.ParentGroup != groupId)
                            continue;

                        var prequelConfig = await GetSeriesConfiguration(prequelSeries.Id);
                        if (prequelConfig.SeasonMergingBehavior is SeasonMergingBehavior.NoMerge)
                            continue;

                        if (prequelConfig.StructureType is not SeriesStructureType.Shoko_Groups)
                            continue;

                        if (prequelConfig.SeasonMergingBehavior is SeasonMergingBehavior.None && !config.SeasonMerging_SeriesTypes.Contains(prequelConfig.Type))
                            continue;

                        if (prequelSeries.AniDB.AirDate is not { } prequelDate)
                            continue;

                        var mergeOverride = (
                            prequelRelation.Type is RelationType.Prequel && (currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeBackward) || prequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeForward))
                        ) || (
                            prequelRelation.Type is RelationType.MainStory && currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeWithMainStory)
                        ) || (
                            currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupASource) && prequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupATarget)
                        ) || (
                            currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupBSource) && prequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupBTarget)
                        ) || (
                            currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupCSource) && prequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupCTarget)
                        ) || (
                            currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupDSource) && prequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupDTarget)
                        );
                        if (!mergeOverride) {
                            if (prequelRelation.Type is RelationType.Prequel && prequelDate > currentDate)
                                continue;

                            if (maxDaysThreshold > 0) {
                                var deltaDays = (int)Math.Floor((currentDate - prequelDate).TotalDays);
                                if (deltaDays > maxDaysThreshold)
                                    continue;
                            }
                        }

                        var prequelMainTitle = prequelSeries.AniDB.Titles.First(title => title.Type == TitleType.Main).Value;
                        var adjustedPrequelMainTitle = AdjustMainTitle(prequelMainTitle);
                        if (mergeOverride) {
                            adjustedMainTitle = adjustedPrequelMainTitle ?? prequelMainTitle;
                            currentSeries = prequelSeries;
                            currentDate = prequelDate;
                            currentRelations = await ApiClient.GetRelationsForShokoSeries(prequelSeries.Id);
                            currentConfig = prequelConfig;
                            goto continuePrequelWhileLoop;
                        }

                        // We only want to merge main/side stories if the override is set.
                        if (prequelRelation.Type is RelationType.MainStory)
                            continue;

                        if (string.IsNullOrEmpty(adjustedPrequelMainTitle)) {
                            if (string.Equals(adjustedMainTitle, prequelMainTitle, StringComparison.InvariantCultureIgnoreCase)) {
                                currentSeries = prequelSeries;
                                currentDate = prequelDate;
                                currentRelations = await ApiClient.GetRelationsForShokoSeries(prequelSeries.Id);
                                currentConfig = prequelConfig;
                                goto breakPrequelWhileLoop;
                            }
                            continue;
                        }

                        if (string.Equals(adjustedMainTitle, adjustedPrequelMainTitle, StringComparison.InvariantCultureIgnoreCase)) {
                            currentSeries = prequelSeries;
                            currentDate = prequelDate;
                            currentRelations = await ApiClient.GetRelationsForShokoSeries(prequelSeries.Id);
                            currentConfig = prequelConfig;
                            goto continuePrequelWhileLoop;
                        }
                    }
                    breakPrequelWhileLoop: break;
                    continuePrequelWhileLoop: continue;
                }

                // If an earlier candidate was found, use its IDs instead. We re-run the method to
                // allow it to cache the IDs once for the forward search and re-use them across all
                // other seasons that perform a backward search.
                if (currentSeries != series) {
                    (primaryId, extraIds) = await GetSeriesIdsForSeason(currentSeries);

                    // I don't want to duplicate the logging here and to use an else branch with
                    // more indention for the while loop, so using goto instead.
                    goto logAndReturn;
                }

                var storyStack = new Stack<(string adjustedMainTitle, DateTime currentDate, SeriesConfiguration currentConfig, IReadOnlyList<Relation> currentRelations, int relationOffset)>([
                    (adjustedMainTitle, currentDate, currentConfig, currentRelations, 0)
                ]);
                while (storyStack.Count > 0) {
                    (adjustedMainTitle, currentDate, currentConfig, currentRelations, var relationOffset) = storyStack.Pop();
                    while (currentRelations.Count > 0) {
                        foreach (
                            var sequelRelation in currentRelations
                                .Where(relation => relation.Type is RelationType.Sequel or RelationType.SideStory && relation.RelatedIDs.Shoko.HasValue)
                                .OrderBy(relation => relation.Type is RelationType.Sequel)
                                .ThenBy(relation => relation.Type)
                                .ThenBy(relation => relation.RelatedIDs.AniDB)
                                .Skip(relationOffset)
                        ) {
                            relationOffset++;
                            if (await ApiClient.GetShokoSeries(sequelRelation.RelatedIDs.Shoko!.Value.ToString()) is not { } sequelSeries)
                                continue;

                            if (sequelSeries.IDs.ParentGroup != groupId)
                                continue;

                            var sequelConfig = await GetSeriesConfiguration(sequelSeries.Id);
                            if (sequelConfig.SeasonMergingBehavior is SeasonMergingBehavior.NoMerge)
                                continue;

                            if (sequelConfig.StructureType is not SeriesStructureType.Shoko_Groups)
                                continue;

                            if (sequelConfig.SeasonMergingBehavior is SeasonMergingBehavior.None && !config.SeasonMerging_SeriesTypes.Contains(sequelConfig.Type))
                                continue;

                            if (sequelSeries.AniDB.AirDate is not { } sequelDate)
                                continue;

                            // Fix for older servers with mismatching relations between the series causing weird behavior.
                            if (sequelRelation.Type is RelationType.SideStory) {
                                var sequelRelations = await ApiClient.GetRelationsForShokoSeries(sequelSeries.Id);
                                sequelRelations = sequelRelations
                                    .Where(relation => relation.Type is RelationType.MainStory && relation.RelatedIDs.Shoko.HasValue)
                                    .OrderBy(relation => relation.RelatedIDs.AniDB)
                                    .ToList();
                                if (sequelRelations.Count == 0 || sequelRelations[0].RelatedIDs.AniDB != sequelRelation.IDs.AniDB)
                                    continue;
                            }

                            var mergeOverride = (
                                sequelRelation.Type is RelationType.Sequel && (currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeForward) || sequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeBackward))
                            ) || (
                                sequelRelation.Type is RelationType.SideStory && sequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeWithMainStory)
                            ) || (
                                currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupATarget) && sequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupATarget)
                            ) || (
                                currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupBTarget) && sequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupBTarget)
                            ) || (
                                currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupCTarget) && sequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupCTarget)
                            ) || (
                                currentConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupDTarget) && sequelConfig.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupDTarget)
                            );
                            if (!mergeOverride) {
                                if (sequelRelation.Type is RelationType.Sequel && sequelDate < currentDate)
                                    continue;

                                if (maxDaysThreshold > 0) {
                                    var deltaDays = (int)Math.Floor((sequelDate - currentDate).TotalDays);
                                    if (deltaDays > maxDaysThreshold)
                                        continue;
                                }
                            }

                            var sequelMainTitle = sequelSeries.AniDB.Titles.First(title => title.Type == TitleType.Main).Value;
                            var adjustedSequelMainTitle = AdjustMainTitle(sequelMainTitle);
                            if (mergeOverride) {
                                // If we're about to enter a side tangent, so push the main story on the stack at the next relation index.
                                if (sequelRelation.Type is not RelationType.Sequel)
                                    storyStack.Push((adjustedMainTitle, currentDate, currentConfig, currentRelations, relationOffset));

                                // Re-focus on the sequel when overriding.
                                adjustedMainTitle = adjustedSequelMainTitle ?? sequelMainTitle;
                                extraIds.Add(sequelSeries.Id);
                                currentDate = sequelDate;
                                currentRelations = await ApiClient.GetRelationsForShokoSeries(sequelSeries.Id);
                                currentConfig = sequelConfig;
                                goto continueSequelWhileLoop;
                            }

                            // We only want to merge main/side stories if the override is set.
                            if (sequelRelation.Type is RelationType.SideStory)
                                continue;

                            if (string.IsNullOrEmpty(adjustedSequelMainTitle))
                                continue;

                            if (string.Equals(adjustedMainTitle, adjustedSequelMainTitle, StringComparison.InvariantCultureIgnoreCase)) {
                                extraIds.Add(sequelSeries.Id);
                                currentDate = sequelDate;
                                currentRelations = await ApiClient.GetRelationsForShokoSeries(sequelSeries.Id);
                                currentConfig = sequelConfig;
                                goto continueSequelWhileLoop;
                            }
                        }
                        break;
                        continueSequelWhileLoop: continue;
                    }
                }

                logAndReturn:
                Logger.LogTrace("Created new series-to-season mapping for series. (Series={SeriesId},Primary={PrimaryId},ExtraSeries={ExtraIds})", series.Id, primaryId, extraIds);

                return (primaryId, extraIds);
            }
        );

    private string? AdjustMainTitle(string title)
        => YearRegex().Match(title) is { Success: true } result
            ? title[..^result.Length]
            : null;

    #endregion

    #region Season Id Helpers

    public bool TryGetSeasonIdForPath(string path, [NotNullWhen(true)] out string? seasonId) {
        if (string.IsNullOrEmpty(path)) {
            seasonId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (PathToSeasonIdDictionary.TryGetValue(path, out seasonId))
            return true;

        // Slow path; getting the show from cache or remote and finding the season's series id.
        Logger.LogDebug("Trying to find the season's series id for {Path} using the slow path.", path);
        try {
            if (Task.Run(() => GetSeasonInfoByPath(path)).GetAwaiter().GetResult() is { } seasonInfo) {
                seasonId = seasonInfo.Id;
                return true;
            }
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Encountered an error while trying to lookup the season id for path. (Path={Path})", path);
        }

        seasonId = null;
        return false;
    }

    public bool TryGetSeasonIdForEpisodeId(string episodeId, [NotNullWhen(true)] out string? seasonId) {
        if (string.IsNullOrEmpty(episodeId)) {
            seasonId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (EpisodeIdToSeasonIdDictionary.TryGetValue(episodeId, out seasonId))
            return true;

        // Slow path; asking the http client to get the series from remote to look up it's id.
        Logger.LogDebug("Trying to find episode ids using the slow path. (Episode={EpisodeId})", episodeId);
        try {
            switch (episodeId[0]) {
                case IdPrefix.TmdbShow:
                    if (Task.Run(() => ApiClient.GetTmdbSeasonForTmdbEpisode(episodeId[1..])).GetAwaiter().GetResult() is not { } tmdbSeason) {
                        seasonId = null;
                        return false;
                    }

                    seasonId = IdPrefix.TmdbShow + tmdbSeason.Id;
                    return true;

                case IdPrefix.TmdbMovie:
                    seasonId = episodeId;
                    return true;

                default:
                    if (Task.Run(() => ApiClient.GetShokoSeriesForShokoEpisode(episodeId)).GetAwaiter().GetResult() is not { } series) {
                        seasonId = null;
                        return false;
                    }

                    seasonId = series.Id;
                    return true;
            }
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Encountered an error while trying to lookup the season id for episode id. (Episode={EpisodeId})", episodeId);
        }

        seasonId = null;
        return false;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Season (?<seasonNumber>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex SeasonNameRegex();

    private async Task<string?> GetSeasonIdForPath(string path) {
        // Reuse cached value.
        if (PathToSeasonIdDictionary.TryGetValue(path, out var seasonId))
            return seasonId;

        // Fast-path for VFS.
        if (path.StartsWith(Plugin.Instance.VirtualRoot + Path.DirectorySeparatorChar)) {
            var fileName = Path.GetFileName(path);
            var seasonNumberResult = SeasonNameRegex().Match(fileName);
            if (seasonNumberResult.Success)
                fileName = Path.GetFileName(Path.GetDirectoryName(path)!);

            if (!fileName.TryGetAttributeValue(ProviderNames.ShokoSeries, out seasonId))
                return null;

            if (seasonNumberResult.Success) {
                var seasonNumber = int.Parse(seasonNumberResult.Groups["seasonNumber"].Value);
                var showInfo = await GetShowInfoBySeasonId(seasonId);
                if (showInfo == null)
                    return null;

                var seasonInfo = showInfo.GetSeasonInfoBySeasonNumber(seasonNumber);
                if (seasonInfo == null)
                    return null;

                seasonId = seasonInfo.Id;
            }

            PathToSeasonIdDictionary[path] = seasonId;
            return seasonId;
        }

        var partialPath = StripMediaFolder(path);
        Logger.LogDebug("Looking for shoko series matching path {Path}", partialPath);
        var result = await ApiClient.GetShokoSeriesForDirectory(partialPath);
        Logger.LogTrace("Found {Count} matches for path {Path}", result.Count, partialPath);

        // Return the first match where the series unique paths partially match
        // the input path.
        foreach (var series in result) {
            if (await GetSeasonInfosForShokoSeries(series.Id) is not { Count: > 0 } seasonInfoList)
                continue;

            var seasonInfo = seasonInfoList[0];
            var pathSet = await GetPathSetForSeries(series.Id);
            foreach (var uniquePath in pathSet) {
                // Remove the trailing slash before matching.
                if (!uniquePath[..^1].EndsWith(partialPath))
                    goto continueForeach;

                PathToSeasonIdDictionary[path] = seasonInfo.Id;
            }

            return seasonInfo.Id;
            continueForeach: continue;
        }

        // In the edge case for series with only files with multiple
        // cross-references we just return the first match.
        if (result.Count > 0) {
            if (await GetSeasonInfosForShokoSeries(result[0].Id) is not { Count: > 0 } seasonInfoList)
                return null;

            return seasonInfoList[0].Id;
        }

        return null;
    }

    #endregion

    #region Show Info

    public async Task<ShowInfo?> GetShowInfoByPath(string path) {
        if (!PathToSeasonIdDictionary.TryGetValue(path, out var seasonId)) {
            seasonId = await GetSeasonIdForPath(path);
            if (string.IsNullOrEmpty(seasonId))
                return null;
        }

        return await GetShowInfoBySeasonId(seasonId);
    }

    public async Task<IReadOnlyList<ShowInfo>> GetShowInfosForShokoSeries(string seriesId) {
        if (await GetSeasonInfosForShokoSeries(seriesId) is not { Count: > 0 } seasonInfoList)
            return [];

        var showInfoList = await Task.WhenAll(seasonInfoList.Select(seasonInfo => GetShowInfoBySeasonId(seasonInfo.Id)));
        return showInfoList
            .WhereNotNull()
            .DistinctBy(showInfo => showInfo.Id)
            .ToList();
    }

    public async Task<ShowInfo?> GetMainShokoSeriesShowInfo(string? seriesId) {
        if (string.IsNullOrEmpty(seriesId))
            return null;

        var group = await ApiClient.GetShokoGroupForShokoSeries(seriesId);
        if (group == null || group.IDs.MainSeries <= 0)
            return await GetShowInfoBySeasonId(seriesId);

        return await GetShowInfoBySeasonId(group.IDs.MainSeries.ToString());
    }

    public async Task<ShowInfo?> GetShowInfoBySeasonId(string seasonId) {
        if (string.IsNullOrEmpty(seasonId))
            return null;

        if (seasonId[0] is IdPrefix.TmdbShow) {
            if (await ApiClient.GetTmdbShowForSeason(seasonId[1..]) is not { } tmdbShow)
                return null;

            return await CreateShowInfo(tmdbShow);
        }
        else if (seasonId[0] is IdPrefix.TmdbMovie) {
            if (await ApiClient.GetTmdbMovie(seasonId[1..]) is not { } tmdbMovie)
                return null;

            if (!tmdbMovie.CollectionId.HasValue)
                return await CreateShowInfoForTmdbMovie(tmdbMovie);

            if (await ApiClient.GetTmdbMovieCollection(tmdbMovie.CollectionId.Value.ToString()) is not { } tmdbMovieCollection)
                return await CreateShowInfoForTmdbMovie(tmdbMovie);

            return await CreateShowInfoForTmdbMovieCollection(tmdbMovieCollection);
        }
        else if (seasonId[0] is IdPrefix.TmdbMovieCollection) {
            if (await ApiClient.GetTmdbMovieCollection(seasonId[1..]) is not { } tmdbMovieCollection)
                return null;

            return await CreateShowInfoForTmdbMovieCollection(tmdbMovieCollection, true);
        }

        var seasonInfo = await GetSeasonInfo(seasonId);
        if (seasonInfo == null)
            return null;

        // Create a standalone group if grouping is disabled and/or for each series in a group with sub-groups.
        var seriesConfig = await GetSeriesConfiguration(seasonId);
        if (seriesConfig.StructureType is not SeriesStructureType.Shoko_Groups)
            return await CreateShowInfoForShokoSeries(seasonInfo);

        var group = await ApiClient.GetShokoGroupForShokoSeries(seasonId);
        if (group == null)
            return null;

        // Create a standalone group if grouping is disabled and/or for each series in a group with sub-groups.
        if (group.Sizes.SubGroups > 0)
            return await CreateShowInfoForShokoSeries(seasonInfo);

        // If we found a movie, and we're assigning movies as stand-alone shows, and we didn't create a stand-alone show
        // above, then attach the stand-alone show to the parent group of the group that might otherwise
        // contain the movie.
        if (seasonInfo.Type == SeriesType.Movie && Plugin.Instance.Configuration.SeparateMovies)
            return await CreateShowInfoForShokoSeries(seasonInfo, group.Size > 0 ? group.IDs.ParentGroup?.ToString() : null);

        return await CreateShowInfoForShokoGroup(group, group.Id);
    }

    private Task<ShowInfo> CreateShowInfo(TmdbShow tmdbShow)
        => DataCache.GetOrCreateAsync(
            $"show:by-tmdb-show-id:{tmdbShow.Id}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {ShowName}. (Source=TMDB,Show={ShowId})", showInfo?.Title, tmdbShow.Id),
            async () => {
                Logger.LogTrace("Creating info object for show {ShowName}. (Source=TMDB,Show={ShowId})", tmdbShow.Title, tmdbShow.Id);
                var seasonsInShow = await ApiClient.GetTmdbSeasonsInTmdbShow(tmdbShow.Id.ToString());
                var seasonList = new List<SeasonInfo>();
                foreach (var season in seasonsInShow) {
                    // Since this is taxing on the upstream, do it 1 season at a time.
                    var seasonInfo = await CreateSeasonInfo(season, tmdbShow);
                    seasonList.Add(seasonInfo);
                }
                var showInfo = new ShowInfo(ApiClient, tmdbShow, seasonList);

                foreach (var seasonInfo in seasonList)
                    SeasonIdToShowIdDictionary[seasonInfo.Id] = showInfo.Id;

                return showInfo;
            }
    );

    private Task<ShowInfo> CreateShowInfoForTmdbMovieCollection(TmdbMovieCollection tmdbMovieCollection, bool singleSeasonMode = false)
        => DataCache.GetOrCreateAsync(
            $"show:by-tmdb-movie-collection-id:{tmdbMovieCollection.Id}:{singleSeasonMode}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {ShowName}. (Source=TMDB,MovieCollection={MovieCollectionId},SingleSeasonMode={SingleSeasonMode})", showInfo?.Title, tmdbMovieCollection.Id, singleSeasonMode),
            async () => {
                Logger.LogTrace("Creating info object for show {ShowName}. (Source=TMDB,MovieCollection={MovieCollectionId},SingleSeasonMode={SingleSeasonMode})", tmdbMovieCollection.Title, tmdbMovieCollection.Id, singleSeasonMode);

                var moviesInCollection = await ApiClient.GetTmdbMoviesInMovieCollection(tmdbMovieCollection.Id.ToString());
                var seasonList = singleSeasonMode
                    ? [await CreateSeasonInfo(tmdbMovieCollection)]
                    : await Task.WhenAll(moviesInCollection.Select(CreateSeasonInfo));
                var showInfo = new ShowInfo(ApiClient, tmdbMovieCollection, seasonList);

                foreach (var seasonInfo in seasonList)
                    SeasonIdToShowIdDictionary[seasonInfo.Id] = showInfo.Id;

                return showInfo;
            }
    );

    private Task<ShowInfo> CreateShowInfoForTmdbMovie(TmdbMovie tmdbMovie)
        => DataCache.GetOrCreateAsync(
            $"show:by-tmdb-movie-id:{tmdbMovie.Id}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {ShowName}. (Source=TMDB,Movie={MovieId})", showInfo?.Title, tmdbMovie.Id),
            async () => {
                Logger.LogTrace("Creating info object for show {ShowName}. (Source=TMDB,Movie={MovieId})", tmdbMovie.Title, tmdbMovie.Id);

                var seasonInfo = await CreateSeasonInfo(tmdbMovie);
                var showInfo = new ShowInfo(ApiClient, tmdbMovie, seasonInfo);

                SeasonIdToShowIdDictionary[seasonInfo.Id] = showInfo.Id;

                return showInfo;
            }
    );

    private Task<ShowInfo?> CreateShowInfoForShokoGroup(ShokoGroup group, string groupId)
        => DataCache.GetOrCreateAsync(
            $"show:by-group-id:{groupId}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {GroupName}. (Source=Shoko,Group={GroupId})", showInfo?.Title, groupId),
            async () => {
                Logger.LogTrace("Creating info object for show {GroupName}. (Source=Shoko,Group={GroupId})", group.Name, groupId);

                var seriesInGroup = await ApiClient.GetShokoSeriesInGroup(groupId);
                var seasonList = (await Task.WhenAll(seriesInGroup.Select(CreateSeasonInfo)))
                    .DistinctBy(seasonInfo => seasonInfo.Id)
                    .ToList();
                var length = seasonList.Count;

                seasonList = [.. seasonList.Where(s => s.StructureType is SeriesStructureType.Shoko_Groups)];

                if (Plugin.Instance.Configuration.SeparateMovies)
                    seasonList = [.. seasonList.Where(s => s.Type is not SeriesType.Movie)];

                // Return early if no series matched the filter or if the list was empty.
                if (seasonList.Count == 0) {
                    Logger.LogWarning("Creating an empty show info for filter! (Source=Shoko,Group={GroupId})", groupId);

                    return null;
                }

                var tmdbEntities = new List<ITmdbEntity>();
                foreach (var seasonInfo in seasonList) {
                    foreach (var tmdbInfo in seasonInfo.TmdbSeasons) {
                        Logger.LogTrace("Fetching TMDB show for Shoko Series {SeriesName}. (Series={SeriesId},Show={ShowId})", seasonInfo.Title, seasonInfo.Id, tmdbInfo.TmdbSeasonId);

                        if (await ApiClient.GetTmdbShowForSeason(tmdbInfo.TmdbSeasonId) is not { } tmdbShow) {
                            Logger.LogTrace("Failed to fetch TMDB show for Shoko Series {SeriesName}. (Series={SeriesId},Show={ShowId})", seasonInfo.Title, seasonInfo.Id, tmdbInfo.TmdbSeasonId);
                            continue;
                        }

                        tmdbEntities.Add(tmdbShow);
                    }

                    foreach (var tmdbInfo in seasonInfo.TmdbMovies.DistinctBy(tmdbMovie => tmdbMovie.TmdbMovieId)) {
                        if (string.IsNullOrEmpty(tmdbInfo.TmdbMovieCollectionId))
                            continue;

                        Logger.LogTrace("Fetching TMDB movie collection for Shoko Series {SeriesName}. (Series={SeriesId},Show={ShowId})", seasonInfo.Title, seasonInfo.Id, tmdbInfo.TmdbMovieCollectionId);

                        if (await ApiClient.GetTmdbMovieCollection(tmdbInfo.TmdbMovieCollectionId) is not { } tmdbMovieCollection) {
                            Logger.LogTrace("Failed to fetch TMDB movie collection for Shoko Series {SeriesName}. (Series={SeriesId},Show={ShowId})", seasonInfo.Title, seasonInfo.Id, tmdbInfo.TmdbMovieCollectionId);
                            continue;
                        }

                        tmdbEntities.Add(tmdbMovieCollection);
                    }
                }
                var tmdbEntity = tmdbEntities
                    .GroupBy(x => (x.Kind, x.Id))
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.First())
                    .FirstOrDefault();
                if (tmdbEntity is not null)
                    Logger.LogTrace("Found TMDB show for group {GroupName}. (Group={GroupId},Show={ShowId})", group.Name, groupId, tmdbEntity.Id);

                var showInfo = new ShowInfo(ApiClient, Logger, group, seasonList, tmdbEntity, length != seasonList.Count);

                foreach (var seasonInfo in seasonList)
                    SeasonIdToShowIdDictionary[seasonInfo.Id] = showInfo.Id;

                return showInfo;
            }
        );

    private Task<ShowInfo> CreateShowInfoForShokoSeries(SeasonInfo seasonInfo, string? collectionId = null)
        => DataCache.GetOrCreateAsync(
            $"show:by-series-id:{seasonInfo.Id}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {GroupName}. (Source=Shoko,Series={SeriesId})", showInfo.Title, seasonInfo.Id),
            async () => {
                Logger.LogTrace("Creating info object for show {SeriesName}. (Source=Shoko,Series={SeriesId})", seasonInfo.Title, seasonInfo.Id);

                TmdbShow? tmdbShow = null;
                if (seasonInfo.TmdbSeasons.DistinctBy(tmdbSeason => tmdbSeason.TmdbShowId).Count() is 1) {
                    tmdbShow = await ApiClient.GetTmdbShowForSeason(seasonInfo.TmdbSeasons[0].TmdbSeasonId);
                }
                var showInfo = new ShowInfo(ApiClient, seasonInfo, tmdbShow, collectionId);

                SeasonIdToShowIdDictionary[seasonInfo.Id] = showInfo.Id;

                return showInfo;
            }
        );

    #endregion

    #region Show Id Helper

    public bool TryGetShowIdForSeasonId(string seasonId, [NotNullWhen(true)] out string? showId) {
        if (string.IsNullOrEmpty(seasonId)) {
            showId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (SeasonIdToShowIdDictionary.TryGetValue(seasonId, out showId))
            return true;

        // Slow path; getting the show from cache or remote and finding the show id.
        Logger.LogDebug("Trying to find the show id for season using the slow path. (Season={SeasonId})", seasonId);
        try {
            if (Task.Run(() => GetShowInfoBySeasonId(seasonId)).GetAwaiter().GetResult() is { } showInfo) {
                showId = showInfo.Id;
                return true;
            }
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Encountered an error while trying to lookup the show id for season id. (Season={SeasonId})", seasonId);
        }

        showId = null;
        return false;
    }

    #endregion

    #region Collection Info

    public async Task<CollectionInfo?> GetCollectionInfo(string collectionId) {
        if (string.IsNullOrEmpty(collectionId))
            return null;

        if (DataCache.TryGetValue<CollectionInfo>($"collection:{collectionId}", out var collectionInfo)) {
            Logger.LogTrace("Reusing info object for collection {GroupName}. (Group={GroupId})", collectionInfo.Title, collectionId);
            return collectionInfo;
        }

        if (await ApiClient.GetShokoGroup(collectionId) is not { } group)
            return null;

        return await CreateCollectionInfo(group, collectionId);
    }

    private Task<CollectionInfo> CreateCollectionInfo(ShokoGroup group, string groupId)
        => DataCache.GetOrCreateAsync(
            $"collection:{groupId}",
            (collectionInfo) => Logger.LogTrace("Reusing info object for collection {GroupName}. (Group={GroupId})", collectionInfo.Title, groupId),
            async () => {
                Logger.LogTrace("Creating info object for collection {GroupName}. (Group={GroupId})", group.Name, groupId);
                Logger.LogTrace("Fetching show info objects for collection {GroupName}. (Group={GroupId})", group.Name, groupId);

                var showGroupIds = new HashSet<string>();
                var collectionIds = new HashSet<string>();
                var showDict = new Dictionary<string, ShowInfo>();
                foreach (var series in await ApiClient.GetShokoSeriesInGroup(groupId, recursive: true)) {
                    foreach (var showInfo in await GetShowInfosForShokoSeries(series.Id)) {
                        if (showInfo == null)
                            continue;

                        foreach (var shokoInfo in showInfo.ShokoSeries)
                            showGroupIds.Add(shokoInfo.ShokoGroupId);

                        if (string.IsNullOrEmpty(showInfo.CollectionId))
                            continue;

                        collectionIds.Add(showInfo.CollectionId);
                        if (showInfo.CollectionId == groupId)
                            showDict.TryAdd(showInfo.Id, showInfo);
                    }
                }

                var groupList = new List<CollectionInfo>();
                if (group.Sizes.SubGroups > 0) {
                    Logger.LogTrace("Fetching sub-collection info objects for collection {GroupName}. (Group={GroupId})", group.Name, groupId);
                    foreach (var subGroup in await ApiClient.GetShokoGroupsInShokoGroup(groupId)) {
                        if (showGroupIds.Contains(subGroup.Id) && !collectionIds.Contains(subGroup.Id))
                            continue;

                        var subCollectionInfo = await CreateCollectionInfo(subGroup, subGroup.Id);
                        if (subCollectionInfo.FileCount > 0)
                            groupList.Add(subCollectionInfo);
                    }
                }

                var showInfoList = await GetShowInfosForShokoSeries(group.IDs.MainSeries.ToString());
                var mainShowId = showInfoList is { Count: > 0 } ? showInfoList[0].Id : null;
                var showList = showDict.Values.ToList();
                if (
                    await ApiClient.GetShokoSeries(group.IDs.MainSeries.ToString()) is { } mainSeries &&
                    group.Name == mainSeries.Name &&
                    group.Description == mainSeries.AniDB.Description
                ) {
                    Logger.LogTrace("Finalizing info object for collection {GroupName}. (MainSeries={MainSeriesId},Group={GroupId})", group.Name, mainSeries.IDs.Shoko.ToString(), groupId);
                    return new CollectionInfo(group, mainSeries, mainShowId, showList, groupList);
                }

                Logger.LogTrace("Finalizing info object for collection {GroupName}. (Group={GroupId})", group.Name, groupId);
                return new CollectionInfo(group, mainShowId, showList, groupList);
            }
        );

    #endregion
}
