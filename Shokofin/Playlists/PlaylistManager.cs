using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Info;
using Shokofin.Configuration;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Playlists;

public class PlaylistManager(
    ILibraryManager _libraryManager,
    IPlaylistManager _playlist,
    ILogger<PlaylistManager> _logger,
    ShokoIdLookup _lookup,
    ShokoApiManager _apiManager,
    IUserManager _userManager
) {
    private static PlaylistConfiguration Config => Plugin.Instance.Configuration.Playlist;

    public async Task ReconstructPlaylists(IProgress<double> progress, CancellationToken cancellationToken) {
        try {
            if (_libraryManager.GetVirtualFolders().Count is 0) return;

            switch (Config.Grouping) {
                default:
                    await CleanupAll();
                    break;
                case Ordering.PlaylistCreationType.GroupWithMixedContent:
                case Ordering.PlaylistCreationType.Grouped:
                    await RecreatePlaylists(progress, cancellationToken);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
        }
    }

    private async Task RecreatePlaylists(IProgress<double> progress, CancellationToken cancellationToken) {
        var timeStarted = DateTime.Now;

        // Get all Jellyfin items with Shoko IDs
        var movies = GetMovies();
        var shows = GetShows();
        _logger.LogInformation("Reconstructing playlists for {MovieCount} movies and {ShowCount} series.", movies.Count, shows.Count);

        // Build movie -> Shoko data map
        var movieDict = new Dictionary<Movie, (FileInfo fileInfo, SeasonInfo seasonInfo, ShowInfo showInfo)>();
        foreach (var movie in movies) {
            if (!_lookup.TryGetEpisodeIdsFor(movie, out var episodeIds))
                continue;

            var (fileInfo, seasonInfo, showInfo) = await _apiManager.GetFileInfoByPath(movie.Path);
            if (fileInfo == null || seasonInfo == null || showInfo == null)
                continue;

            movieDict.Add(movie, (fileInfo, seasonInfo, showInfo));
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(10);

        // Build series -> ShowInfo map
        var showDict = new Dictionary<Series, ShowInfo>();
        foreach (var show in shows) {
            if (!_lookup.TryGetSeasonIdFor(show, out var seasonId))
                continue;

            var showInfo = await _apiManager.GetShowInfoBySeasonId(seasonId);
            if (showInfo == null)
                continue;

            showDict.Add(show, showInfo);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(20);

        // Build episode -> Jellyfin item lookup
        var episodes = _libraryManager.GetItemList(new() {
            IncludeItemTypes = [BaseItemKind.Episode],
            SourceTypes = [SourceType.Library],
            HasAnyProviderId = new() { { ShokoInternalId.Name, string.Empty } },
            IsVirtualItem = false,
            Recursive = true,
        })
            .Where(_lookup.IsEnabledForItem)
            .Cast<Episode>()
            .ToList();

        var episodeLookup = new Dictionary<string, List<Episode>>();
        foreach (var episode in episodes) {
            if (!_lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                continue;

            foreach (var episodeId in episodeIds) {
                if (!episodeLookup.TryGetValue(episodeId, out var list))
                    episodeLookup[episodeId] = list = [];
                list.Add(episode);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(30);

        // Collect group IDs from movies and shows
        var groupIds = new HashSet<string>();
        foreach (var (_, _, showInfo) in movieDict.Values) {
            if (!string.IsNullOrEmpty(showInfo.CollectionId))
                groupIds.Add(showInfo.CollectionId);
        }
        foreach (var showInfo in showDict.Values) {
            if (!string.IsNullOrEmpty(showInfo.CollectionId))
                groupIds.Add(showInfo.CollectionId);
        }

        // Resolve each group to its top-level group, since that's what the
        // playlist is generated for, and determine which ones qualify.
        var seenTopLevelIds = new HashSet<string>();
        var finalGroups = new Dictionary<string, CollectionInfo>();
        foreach (var groupId in groupIds) {
            var collectionInfo = await _apiManager.GetCollectionInfo(groupId);
            if (collectionInfo == null || !seenTopLevelIds.Add(collectionInfo.TopLevelId))
                continue;

            if (!collectionInfo.IsTopLevel) {
                collectionInfo = await _apiManager.GetCollectionInfo(collectionInfo.TopLevelId);
                if (collectionInfo == null)
                    continue;
            }

            if (!await GroupQualifies(collectionInfo))
                continue;

            finalGroups.Add(collectionInfo.Id, collectionInfo);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(40);

        // Get existing playlists indexed by group ID. Duplicates and legacy
        // playlists without the group ID in their path will be removed.
        var (existingPlaylists, toRemove) = GetGroupPlaylists();

        // Determine what to add, remove, check
        var toCreate = new List<(string GroupId, CollectionInfo Info, List<BaseItem> Items)>();
        var toCheck = new List<(Playlist Playlist, CollectionInfo Info, List<BaseItem> Items)>();
        foreach (var (groupId, info) in finalGroups) {
            var orderedItems = BuildOrderedItemList(info, movieDict, episodeLookup);
            if (orderedItems.Count < (Config.MinSizeOfTwo ? 2 : 1))
                continue;

            if (existingPlaylists.Remove(groupId, out var existingPlaylist))
                toCheck.Add((existingPlaylist, info, orderedItems));
            else
                toCreate.Add((groupId, info, orderedItems));
        }

        // Remaining playlists have no matching group -> remove
        toRemove.AddRange(existingPlaylists.Values);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(50);

        // Remove orphaned playlists
        foreach (var playlist in toRemove)
            RemovePlaylist(playlist);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(60);

        // Create new playlists
        #if NET9_0_OR_GREATER
        var users = _userManager.GetUsers().ToList();
        #else
        var users = _userManager.Users.ToList();
        #endif
        var firstUser = users.FirstOrDefault();
        if (firstUser == null) {
            _logger.LogWarning("No users found, skipping playlist creation.");
            return;
        }

        var created = 0;
        foreach (var (groupId, info, orderedItems) in toCreate) {
            // The group ID is embedded in the name so it ends up in the path of
            // the playlist, which is what we use to find the playlist again.
            var result = await _playlist.CreatePlaylist(new PlaylistCreationRequest {
                Name = $"{info.Title.ForceASCII()} [{ProviderNames.ShokoPlaylistForGroup}={groupId}]",
                ItemIdList = orderedItems.Select(item => item.Id).ToList(),
                MediaType = MediaType.Video,
                UserId = firstUser.Id,
                Public = true,
            });

            if (Guid.TryParse(result.Id, out var playlistId) && _libraryManager.GetItemById(playlistId) is Playlist playlist) {
                playlist.Name = info.Title;
                playlist.ProviderIds[ProviderNames.ShokoPlaylistForGroup] = groupId;
                await playlist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken);
            }

            created++;
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(80);

        // Update existing playlists
        var updated = 0;
        foreach (var (existingPlaylist, info, orderedItems) in toCheck) {
            var needsUpdate = false;

            if (!string.Equals(existingPlaylist.Name, info.Title)) {
                existingPlaylist.Name = info.Title;
                needsUpdate = true;
            }

            // Replace the items if anything was added, removed or re-ordered.
            var existingItemIds = existingPlaylist.GetLinkedChildrenInfos()
                .Select(tuple => tuple.Item2?.Id ?? Guid.Empty)
                .ToList();
            if (!existingItemIds.SequenceEqual(orderedItems.Select(item => item.Id))) {
                existingPlaylist.LinkedChildren = orderedItems.Select(LinkedChild.Create).ToArray();
                needsUpdate = true;
            }

            if (needsUpdate) {
                _logger.LogTrace("Updating playlist {PlaylistName} (Id={PlaylistId})", existingPlaylist.Name, existingPlaylist.Id);
                await existingPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken);
                updated++;
            }
        }

        progress.Report(100);

        _logger.LogInformation(
            "Created {CreatedCount}, updated {UpdatedCount}, removed {RemovedCount} playlists for {MovieCount} movies and {ShowCount} series in {TimeSpent}.",
            created,
            updated,
            toRemove.Count,
            movies.Count,
            shows.Count,
            DateTime.Now - timeStarted
        );
    }

    #region Group Helpers

    /// <summary>
    /// Determine if a group qualifies for playlist creation based on the current config.
    /// </summary>
    private async Task<bool> GroupQualifies(CollectionInfo info) {
        var config = Config;

        if (config.Grouping switch {
            Ordering.PlaylistCreationType.Grouped => CountItemsInCollection(info) > (config.MinSizeOfTwo ? 1 : 0),
            Ordering.PlaylistCreationType.GroupWithMixedContent => HasMixedContent(info) && CountItemsInCollection(info) > (config.MinSizeOfTwo ? 1 : 0),
            _ => false,
        }) {
            return await PassesTagFilter(info);
        }

        return false;
    }

    /// <summary>
    /// Check if the group passes the per-series tag filter, if active.
    /// </summary>
    private async Task<bool> PassesTagFilter(CollectionInfo info) {
        var filter = Config.TagFilter;
        if (filter is Ordering.PlaylistTagFilter.None)
            return true;

        var shokoSeriesIds = CollectShokoSeriesIds(info);
        if (shokoSeriesIds.Count == 0)
            return filter is Ordering.PlaylistTagFilter.AllSeriesTagged;

        var taggedCount = 0;
        foreach (var seriesId in shokoSeriesIds) {
            var seriesConfig = await _apiManager.GetInternalSeriesConfiguration(seriesId);
            if (seriesConfig.PlaylistInclude)
                taggedCount++;
        }

        return filter switch {
            Ordering.PlaylistTagFilter.AnySeriesTagged => taggedCount > 0,
            Ordering.PlaylistTagFilter.AllSeriesTagged => taggedCount == shokoSeriesIds.Count,
            _ => true,
        };
    }

    /// <summary>
    /// Collect all Shoko series IDs from a collection recursively.
    /// </summary>
    private static HashSet<string> CollectShokoSeriesIds(CollectionInfo info) {
        var ids = new HashSet<string>();

        foreach (var show in info.Shows)
            foreach (var season in show.SeasonList)
                foreach (var shokoSeries in season.ShokoSeries)
                    ids.Add(shokoSeries.ShokoSeriesId);

        foreach (var movie in info.Movies)
            foreach (var season in movie.SeasonList)
                foreach (var shokoSeries in season.ShokoSeries)
                    ids.Add(shokoSeries.ShokoSeriesId);

        foreach (var sub in info.SubCollections)
            ids.UnionWith(CollectShokoSeriesIds(sub));

        return ids;
    }

    /// <summary>
    /// Check if a collection has both TV shows and movies (recursively).
    /// </summary>
    private static bool HasMixedContent(CollectionInfo info)
        => HasShows(info) && HasMovies(info);

    private static bool HasShows(CollectionInfo info)
        => info.Shows.Count > 0 || info.SubCollections.Any(HasShows);

    private static bool HasMovies(CollectionInfo info)
        => info.Movies.Count > 0 || info.SubCollections.Any(HasMovies);

    /// <summary>
    /// Count total items (movies + episodes from shows) in a collection recursively.
    /// </summary>
    private static int CountItemsInCollection(CollectionInfo info) {
        var count = 0;

        foreach (var show in info.Shows)
            count += show.SeasonList.Sum(s => s.EpisodeList.Count + s.AlternateEpisodesList.Count);

        foreach (var movie in info.Movies)
            count += movie.DefaultSeason.EpisodeList.Count + movie.DefaultSeason.AlternateEpisodesList.Count;

        foreach (var sub in info.SubCollections)
            count += CountItemsInCollection(sub);

        return count;
    }

    /// <summary>
    /// Build a flat ordered list of Jellyfin items from a collection's shows and movies.
    /// </summary>
    private List<BaseItem> BuildOrderedItemList(
        CollectionInfo info,
        Dictionary<Movie, (FileInfo fileInfo, SeasonInfo seasonInfo, ShowInfo showInfo)> movieDict,
        Dictionary<string, List<Episode>> episodeLookup
    ) {
        var items = new List<(BaseItem Item, DateTime? Date)>();

        CollectItems(info, movieDict, episodeLookup, items);

        // Sort by date ascending; items without dates go at the end
        var ordered = items
            .OrderBy(t => t.Date ?? DateTime.MaxValue)
            .Select(t => t.Item)
            .DistinctBy(item => item.Id)
            .ToList();

        return ordered;
    }

    private void CollectItems(
        CollectionInfo info,
        Dictionary<Movie, (FileInfo fileInfo, SeasonInfo seasonInfo, ShowInfo showInfo)> movieDict,
        Dictionary<string, List<Episode>> episodeLookup,
        List<(BaseItem Item, DateTime? Date)> items
    ) {
        // Process movies
        foreach (var movieShow in info.Movies) {
            var season = movieShow.DefaultSeason;
            foreach (var episodeInfo in season.EpisodeList.Concat(season.AlternateEpisodesList)) {
                // Find matching Jellyfin movie
                foreach (var (movie, (fileInfo, _, _)) in movieDict) {
                    if (fileInfo.EpisodeList.Any(e => e.Episode.Id == episodeInfo.Id)) {
                        items.Add((movie, episodeInfo.AiredAt ?? movieShow.PremiereDate));
                        break;
                    }
                }
            }
        }

        // Process TV shows
        foreach (var show in info.Shows) {
            foreach (var season in show.SeasonList) {
                var episodeList = GetFilteredEpisodeList(season);
                foreach (var episodeInfo in episodeList) {
                    // Find matching Jellyfin episode
                    var primaryId = episodeInfo.Id;
                    if (episodeLookup.TryGetValue(primaryId, out var episodeList2)) {
                        foreach (var episode in episodeList2) {
                            items.Add((episode, episodeInfo.AiredAt));
                        }
                    }
                }
            }
        }

        // Process sub-collections recursively
        foreach (var sub in info.SubCollections)
            CollectItems(sub, movieDict, episodeLookup, items);
    }

    /// <summary>
    /// Get the filtered episode list based on specials placement config.
    /// </summary>
    private List<EpisodeInfo> GetFilteredEpisodeList(SeasonInfo season) {
        var config = Config;
        var list = new List<EpisodeInfo>();

        list.AddRange(season.EpisodeList);
        list.AddRange(season.AlternateEpisodesList);

        if (config.SpecialsPlacement != Ordering.SpecialOrderType.Excluded) {
            list.AddRange(season.SpecialsList);
        }

        return list;
    }

    #endregion

    #region Cleanup Helpers

    private Task CleanupAll() {
        foreach (var playlist in GetPlaylists())
            RemovePlaylist(playlist);
        return Task.CompletedTask;
    }

    private void RemovePlaylist(Playlist playlist) {
        _logger.LogTrace("Removing playlist {PlaylistName} (Id={PlaylistId})", playlist.Name, playlist.Id);
        _libraryManager.DeleteItem(playlist, new() { DeleteFileLocation = true, DeleteFromExternalProvider = false });
    }

    #endregion

    #region Getter Helpers

    private List<Movie> GetMovies()
        => _libraryManager.GetItemList(new() {
            IncludeItemTypes = [BaseItemKind.Movie],
            SourceTypes = [SourceType.Library],
            HasAnyProviderId = new() { { ShokoInternalId.Name, string.Empty } },
            IsVirtualItem = false,
            Recursive = true,
        })
            .Where(_lookup.IsEnabledForItem)
            .Cast<Movie>()
            .ToList();

    private List<Series> GetShows()
        => _libraryManager.GetItemList(new() {
            IncludeItemTypes = [BaseItemKind.Series],
            SourceTypes = [SourceType.Library],
            HasAnyProviderId = new() { { ShokoInternalId.Name, string.Empty } },
            IsVirtualItem = false,
            Recursive = true,
        })
            .Where(_lookup.IsEnabledForItem)
            .Cast<Series>()
            .ToList();

    /// <summary>
    /// Get all playlists managed by the plugin, including legacy playlists
    /// which only have the group ID stored as a provider ID.
    /// </summary>
    private List<Playlist> GetPlaylists()
        => _libraryManager.GetItemList(new() {
            IncludeItemTypes = [BaseItemKind.Playlist],
            SourceTypes = [SourceType.Library],
            IsVirtualItem = false,
            Recursive = true,
        })
            .OfType<Playlist>()
            .Where(x =>
                (!string.IsNullOrEmpty(x.Path) && x.Path.TryGetAttributeValue(ProviderNames.ShokoPlaylistForGroup, out _)) ||
                x.TryGetProviderId(ProviderNames.ShokoPlaylistForGroup, out _)
            )
            .ToList();

    /// <summary>
    /// Get the playlist for each group, identified by the group ID in the
    /// path. Duplicate and legacy playlists are returned separately.
    /// </summary>
    private (Dictionary<string, Playlist> Playlists, List<Playlist> Extras) GetGroupPlaylists() {
        var playlists = new Dictionary<string, Playlist>();
        var extras = new List<Playlist>();
        foreach (var playlist in GetPlaylists().OrderBy(x => x.DateCreated)) {
            if (!string.IsNullOrEmpty(playlist.Path) && playlist.Path.TryGetAttributeValue(ProviderNames.ShokoPlaylistForGroup, out var groupId) && playlists.TryAdd(groupId, playlist))
                continue;

            extras.Add(playlist);
        }

        return (playlists, extras);
    }

    #endregion
}
