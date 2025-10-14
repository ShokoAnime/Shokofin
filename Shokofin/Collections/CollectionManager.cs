using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Info;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Collections;

public class CollectionManager(
    ILibraryManager _libraryManager,
    ICollectionManager _collection,
    ILogger<CollectionManager> _logger,
    ShokoIdLookup _lookup,
    ShokoApiManager _apiManager
) {
    private static int MinCollectionSize => Plugin.Instance.Configuration.CollectionMinSizeOfTwo ? 1 : 0;

    public Task<Folder?> GetCollectionsFolder(bool createIfNeeded)
        => _collection.GetCollectionsFolder(createIfNeeded);

    public async Task ReconstructCollections(IProgress<double> progress, CancellationToken cancellationToken) {
        try {
            // This check is to prevent creating the collections root if we don't have any libraries yet.
            if (_libraryManager.GetVirtualFolders().Count is 0) return;
            switch (Plugin.Instance.Configuration.CollectionGrouping) {
                default:
                    await CleanupAll(progress, cancellationToken).ConfigureAwait(false);
                    break;
                case Ordering.CollectionCreationType.Movies:
                    await ReconstructMovieSeriesCollections(progress, cancellationToken).ConfigureAwait(false);
                    break;
                case Ordering.CollectionCreationType.Shared:
                    await ReconstructSharedCollections(progress, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
        }
    }

    #region Movie Collections

    private async Task ReconstructMovieSeriesCollections(IProgress<double> progress, CancellationToken cancellationToken) {
        _logger.LogTrace("Ensuring collection root exists…");
        var collectionRoot = (await GetCollectionsFolder(true).ConfigureAwait(false))!;

        var timeStarted = DateTime.Now;

        _logger.LogTrace("Cleaning up movies and invalid collections…");

        // Clean up movies and unneeded group collections.
        await CleanupMovies().ConfigureAwait(false);
        CleanupGroupCollections();

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(10);

        // Get all movies to include in the collection.
        var movies = GetMovies();
        _logger.LogInformation("Reconstructing collections for {MovieCount} movies using Shoko Series.", movies.Count);

        // Create a tree-map of how it's supposed to be.
        var movieDict = new Dictionary<Movie, (FileInfo fileInfo, SeasonInfo seasonInfo, ShowInfo showInfo)>();
        foreach (var movie in movies) {
            if (!_lookup.TryGetEpisodeIdsFor(movie, out var episodeIds))
                continue;

            var (fileInfo, seasonInfo, showInfo) = await _apiManager.GetFileInfoByPath(movie.Path).ConfigureAwait(false);
            if (fileInfo == null || seasonInfo == null || showInfo == null)
                continue;

            movieDict.Add(movie, (fileInfo, seasonInfo, showInfo));
        }
        // Filter to only "seasons" with at least (`MinCollectionSize` + 1) movies in them.
        var seasonDict = movieDict.Values
            .Select(tuple => tuple.seasonInfo)
            .GroupBy(seasonInfo => seasonInfo.Id)
            .Where(groupBy => groupBy.Count() > MinCollectionSize)
            .ToDictionary(groupBy => groupBy.Key, groupBy => groupBy.First());

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(30);

        // Find out what to add, what to remove and what to check.
        var addedChildren = 0;
        var removedChildren = 0;
        var totalChildren = 0;
        var existingCollections = GetSeriesCollections();
        var childDict = existingCollections
            .Values
            .SelectMany(collectionList => collectionList)
            .ToDictionary(collection => collection.Id, collection => collection.Children.Concat(collection.GetLinkedChildren()).ToList());
        var parentDict = childDict
            .SelectMany(pair => pair.Value.Select(child => (childId: child.Id, parent: pair.Key)))
            .GroupBy(tuple => tuple.childId)
            .ToDictionary(groupBy => groupBy.Key, groupBy => groupBy.Select(tuple => tuple.parent).ToList());
        var toCheck = new Dictionary<string, BoxSet>();
        var toRemove = new Dictionary<Guid, BoxSet>();
        var toAdd = seasonDict.Keys
            .Where(groupId => !existingCollections.ContainsKey(groupId))
            .ToHashSet();
        foreach (var (seasonId, collectionList) in existingCollections) {
            if (seasonDict.ContainsKey(seasonId)) {
                toCheck.Add(seasonId, collectionList[0]);
                foreach (var collection in collectionList.Skip(1))
                    toRemove.Add(collection.Id, collection);
            }
            else {
                foreach (var collection in collectionList)
                    toRemove.Add(collection.Id, collection);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(50);

        // Remove unknown collections.
        foreach (var (id, collection) in toRemove) {
            // Remove the item from all parents.
            if (parentDict.TryGetValue(collection.Id, out var parents)) {
                foreach (var parentId in parents) {
                    if (!toRemove.ContainsKey(parentId) && collection.ParentId != parentId)
                        await _collection.RemoveFromCollectionAsync(parentId, [id]).ConfigureAwait(false);
                }
            }

            // Log how many children we will be removing.
            removedChildren += childDict[collection.Id].Count;

            // Remove the item.
            _libraryManager.DeleteItem(collection, new() { DeleteFileLocation = true, DeleteFromExternalProvider = false });
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(70);

        // Add the missing collections.
        foreach (var missingId in toAdd) {
            var seasonInfo = seasonDict[missingId];
            var collection = await _collection.CreateCollectionAsync(new() {
                Name = $"{seasonInfo.Title.ForceASCII()} [{ProviderNames.ShokoCollectionForSeries}={missingId}]",
                ProviderIds = new() { { ProviderNames.ShokoCollectionForSeries, missingId } },
            }).ConfigureAwait(false);

            childDict.Add(collection.Id, []);
            toCheck.Add(missingId, collection);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(80);

        // Check if the collection have the correct children, and add any
        // missing and remove any extras.
        var fixedCollections = 0;
        foreach (var (seasonId, collection) in toCheck) {
            // Edit the metadata to if needed.
            var updated = false;
            var seasonInfo = seasonDict[seasonId];
            var metadataLanguage = _libraryManager.GetLibraryOptions(collection)?.PreferredMetadataLanguage;
            var (displayName, alternateTitle) = TextUtility.GetCollectionTitles(seasonInfo, metadataLanguage);
            if (!string.Equals(collection.Name, displayName)) {
                collection.Name = displayName;
                updated = true;
            }
            if (!string.Equals(collection.OriginalTitle, alternateTitle)) {
                collection.OriginalTitle = alternateTitle;
                updated = true;
            }
            if (updated) {
                await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                fixedCollections++;
            }

            var actualChildren = childDict[collection.Id];
            var actualChildMovies = new List<Movie>();
            foreach (var child in actualChildren) switch (child) {
                case Movie movie:
                    actualChildMovies.Add(movie);
                    break;
            }

            var expectedMovies = seasonInfo.EpisodeList.Concat(seasonInfo.AlternateEpisodesList)
                .Select(episodeInfo => (episodeInfo, seasonInfo))
                .SelectMany(tuple => movieDict.Where(pair => pair.Value.seasonInfo.Id == tuple.seasonInfo.Id && pair.Value.fileInfo.EpisodeList.Any(episodeInfo => episodeInfo.Id == tuple.episodeInfo.Id)))
                .Select(pair => pair.Key)
                .ToList();
            var missingMovies = expectedMovies
                .Select(movie => movie.Id)
                .Except(actualChildMovies.Select(a => a.Id).ToHashSet())
                .ToList();
            var unwantedMovies = actualChildren
                .Except(actualChildMovies)
                .Select(movie => movie.Id)
                .ToList();
            if (missingMovies.Count > 0)
                await _collection.AddToCollectionAsync(collection.Id, missingMovies).ConfigureAwait(false);
            if (unwantedMovies.Count > 0)
                await _collection.RemoveFromCollectionAsync(collection.Id, unwantedMovies).ConfigureAwait(false);

            totalChildren += expectedMovies.Count;
            addedChildren += missingMovies.Count;
            removedChildren += unwantedMovies.Count;
        }

        progress.Report(100);

        _logger.LogInformation(
            "Created {AddedCount} ({AddedCollectionCount},{AddedChildCount}), fixed {FixedCount}, skipped {SkippedCount} ({SkippedCollectionCount},{SkippedChildCount}), and removed {RemovedCount} ({RemovedCollectionCount},{RemovedChildCount}) collections for {MovieCount} movies and using Shoko Series in {TimeSpent}. (Total={TotalCount})",
            toAdd.Count + addedChildren,
            toAdd.Count,
            addedChildren,
            fixedCollections -  toAdd.Count,
            toCheck.Count + totalChildren - toAdd.Count - addedChildren - (fixedCollections - toAdd.Count),
            toCheck.Count - toAdd.Count - (fixedCollections - toAdd.Count),
            totalChildren - addedChildren,
            toRemove.Count + removedChildren,
            toRemove.Count,
            removedChildren,
            movies.Count,
            DateTime.Now - timeStarted,
            toCheck.Count + totalChildren
        );
    }

    #endregion

    #region Shared Collections

    private async Task ReconstructSharedCollections(IProgress<double> progress, CancellationToken cancellationToken) {
        _logger.LogTrace("Ensuring collection root exists…");
        var collectionRoot = (await GetCollectionsFolder(true).ConfigureAwait(false))!;

        var timeStarted = DateTime.Now;

        _logger.LogTrace("Cleaning up movies and invalid collections…");

        // Clean up movies and unneeded series collections.
        await CleanupMovies().ConfigureAwait(false);
        CleanupSeriesCollections();

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(10);

        // Get all shows/movies to include in the collection.
        var movies = GetMovies();
        var shows = GetShows();
        _logger.LogInformation("Checking collections for {MovieCount} movies and {ShowCount} shows using Shoko Groups.", movies.Count, shows.Count);

        // Create a tree-map of how it's supposed to be.
        var movieDict = new Dictionary<Movie, (FileInfo fileInfo, SeasonInfo seasonInfo, ShowInfo showInfo)>();
        foreach (var movie in movies) {
            var (fileInfo, seasonInfo, showInfo) = await _apiManager.GetFileInfoByPath(movie.Path).ConfigureAwait(false);
            if (fileInfo == null || seasonInfo == null || showInfo == null)
                continue;

            movieDict.Add(movie, (fileInfo, seasonInfo, showInfo));
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(20);

        var showDict = new Dictionary<Series, ShowInfo>();
        foreach (var show in shows) {
            if (!_lookup.TryGetSeasonIdFor(show, out var seasonId))
                continue;

            var showInfo = await _apiManager.GetShowInfoBySeasonId(seasonId).ConfigureAwait(false);
            if (showInfo == null)
                continue;

            showDict.Add(show, showInfo);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(30);

        // Filter to only collections with at least (`MinCollectionSize` + 1) entries in them.
        var movieCollections = movieDict.Values
            .Select(tuple => tuple.showInfo.CollectionId)
            .Where(collectionId => !string.IsNullOrEmpty(collectionId))
            .ToList();
        var showCollections = showDict.Values
            .Select(showInfo => showInfo.CollectionId)
            .Where(collectionId => !string.IsNullOrEmpty(collectionId))
            .ToList();
        var groupsDict = await Task
            .WhenAll(
                movieCollections.Concat(showCollections)
                    .GroupBy(collectionId => collectionId)
                    .Select(groupBy =>
                        _apiManager.GetCollectionInfo(groupBy.Key!)
                            .ContinueWith(task => (collectionInfo: task.Result, count: groupBy.Count()))
                    )
            )
            .ContinueWith(task =>
                task.Result
                    .Where(tuple => tuple.collectionInfo != null)
                    .GroupBy(tuple => tuple.collectionInfo!.TopLevelId)
                    .Where(groupBy => groupBy.Sum(tuple => tuple.count) > MinCollectionSize)
                    .SelectMany(groupBy => groupBy)
                    .ToDictionary(c => c.collectionInfo!.Id, c => c.collectionInfo!)
            )
            .ConfigureAwait(false);
        var finalGroups = new Dictionary<string, CollectionInfo>();
        foreach (var initialGroup in groupsDict.Values) {
            var currentGroup = initialGroup;
            if (finalGroups.ContainsKey(currentGroup.Id))
                continue;

            finalGroups.Add(currentGroup.Id, currentGroup);
            if (currentGroup.IsTopLevel)
                continue;

            while (!currentGroup.IsTopLevel && !finalGroups.ContainsKey(currentGroup.ParentId!)) {
                currentGroup = await _apiManager.GetCollectionInfo(currentGroup.ParentId!).ConfigureAwait(false);
                if (currentGroup == null)
                    break;
                finalGroups.Add(currentGroup.Id, currentGroup);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(40);

        // Find out what to add, what to remove and what to check.
        var addedChildren = 0;
        var removedChildren = 0;
        var totalChildren = 0;
        var existingCollections = GetGroupCollections();
        var childDict = existingCollections
            .Values
            .SelectMany(collectionList => collectionList)
            .ToDictionary(collection => collection.Id, collection => collection.Children.Concat(collection.GetLinkedChildren()).ToList());
        var parentDict = childDict
            .SelectMany(pair => pair.Value.Select(child => (childId: child.Id, parent: pair.Key)))
            .GroupBy(tuple => tuple.childId)
            .ToDictionary(groupBy => groupBy.Key, groupBy => groupBy.Select(tuple => tuple.parent).ToList());
        var toCheck = new Dictionary<string, BoxSet>();
        var toRemove = new Dictionary<Guid, BoxSet>();
        var toAdd = finalGroups.Keys
            .Where(groupId => !existingCollections.ContainsKey(groupId))
            .ToList();
        foreach (var (groupId, collectionList) in existingCollections) {
            if (finalGroups.ContainsKey(groupId)) {
                toCheck.Add(groupId, collectionList[0]);
                foreach (var collection in collectionList.Skip(1))
                    toRemove.Add(collection.Id, collection);
            }
            else {
                foreach (var collection in collectionList)
                    toRemove.Add(collection.Id, collection);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(50);

        // Remove unknown collections.
        foreach (var (id, collection) in toRemove) {
            // Remove the item from all parents.
            if (parentDict.TryGetValue(collection.Id, out var parents)) {
                foreach (var parentId in parents) {
                    if (!toRemove.ContainsKey(parentId) && collection.ParentId != parentId)
                        await _collection.RemoveFromCollectionAsync(parentId, [id]).ConfigureAwait(false);
                }
            }

            // Log how many children we will be removing.
            removedChildren += childDict[collection.Id].Count;

            // Remove the item.
            _libraryManager.DeleteItem(collection, new() { DeleteFileLocation = true, DeleteFromExternalProvider = false });
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(70);

        // Add the missing collections.
        var addedCollections = toAdd.Count;
        while (toAdd.Count > 0) {
            // First add any top level ids, then gradually move down until all groups are added.
            var index = toAdd.FindIndex(id => finalGroups[id].IsTopLevel);
            if (index == -1)
                index = toAdd.FindIndex(id => toCheck.ContainsKey(finalGroups[id].ParentId!));
            if (index == -1)
                throw new IndexOutOfRangeException("Unable to find the parent to add.");

            var missingId = toAdd[index];
            var collectionInfo = finalGroups[missingId];
            var collection = await _collection.CreateCollectionAsync(new() {
                Name = $"{collectionInfo.Title.ForceASCII()} [{ProviderNames.ShokoCollectionForGroup}={missingId}]",
                ProviderIds = new() { { ProviderNames.ShokoCollectionForGroup, missingId } },
            }).ConfigureAwait(false);

            childDict.Add(collection.Id, []);
            toCheck.Add(missingId, collection);
            toAdd.RemoveAt(index);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(80);

        // Check if the collection have the correct children, and add any
        // missing and remove any extras.
        var fixedCollections = 0;
        foreach (var (groupId, collection) in toCheck) {
            // Edit the metadata to place the collection under the right parent and with the correct name.
            var collectionInfo = finalGroups[groupId];
            var updated = false;
            var parent = collectionInfo.IsTopLevel ? collectionRoot : toCheck[collectionInfo.ParentId!];
            if (collection.ParentId != parent.Id) {
                collection.SetParent(parent);
                updated = true;
            }
            if (!string.Equals(collection.Name, collectionInfo.Title)) {
                collection.Name = collectionInfo.Title;
                updated = true;
            }
            if (updated) {
                await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                fixedCollections++;
            }

            var actualChildren = childDict[collection.Id];
            var actualChildCollections = new List<BoxSet>();
            var actualChildSeries = new List<Series>();
            var actualChildMovies = new List<Movie>();
            foreach (var child in actualChildren) switch (child) {
                case BoxSet subCollection:
                    actualChildCollections.Add(subCollection);
                    break;
                case Series series:
                    actualChildSeries.Add(series);
                    break;
                case Movie movie:
                    actualChildMovies.Add(movie);
                    break;
            }

            var expectedCollections = collectionInfo.SubCollections
                .Select(subCollectionInfo => toCheck.TryGetValue(subCollectionInfo.Id, out var boxSet) ? boxSet : null)
                .OfType<BoxSet>()
                .ToList();
            var expectedShows = collectionInfo.Shows
                .SelectMany(showInfo => showDict.Where(pair => pair.Value.Id == showInfo.Id))
                .Select(pair => pair.Key)
                .ToList();
            var expectedMovies = collectionInfo.Movies
                .SelectMany(showInfo => showInfo.DefaultSeason.EpisodeList.Concat(showInfo.DefaultSeason.AlternateEpisodesList).Select(episodeInfo => (episodeInfo, seasonInfo: showInfo.DefaultSeason)))
                .SelectMany(tuple => movieDict.Where(pair => pair.Value.seasonInfo.Id == tuple.seasonInfo.Id && pair.Value.fileInfo.EpisodeList.Any(episodeInfo => episodeInfo.Id == tuple.episodeInfo.Id)))
                .Select(pair => pair.Key)
                .ToList();
            var missingCollections = expectedCollections
                .Select(show => show.Id)
                .Except(actualChildCollections.Select(a => a.Id).ToHashSet())
                .ToList();
            var missingShows = expectedShows
                .Select(show => show.Id)
                .Except(actualChildSeries.Select(a => a.Id).ToHashSet())
                .ToList();
            var missingMovies = expectedMovies
                .Select(movie => movie.Id)
                .Except(actualChildMovies.Select(a => a.Id).ToHashSet())
                .ToList();
            var missingChildren = missingCollections
                .Concat(missingShows)
                .Concat(missingMovies)
                .ToList();
            var unwantedChildren = actualChildren
                .Except(actualChildCollections)
                .Except(actualChildSeries)
                .Except(actualChildMovies)
                .Select(movie => movie.Id)
                .ToList();
            if (missingChildren.Count > 0)
                await _collection.AddToCollectionAsync(collection.Id, missingChildren).ConfigureAwait(false);
            if (unwantedChildren.Count > 0)
                await _collection.RemoveFromCollectionAsync(collection.Id, unwantedChildren).ConfigureAwait(false);

            totalChildren += expectedCollections.Count + expectedShows.Count + expectedMovies.Count;
            addedChildren += missingChildren.Count;
            removedChildren += unwantedChildren.Count;
        }

        progress.Report(100);

        _logger.LogInformation(
            "Created {AddedCount} ({AddedCollectionCount},{AddedChildCount}), fixed {FixedCount}, skipped {SkippedCount} ({SkippedCollectionCount},{SkippedChildCount}), and removed {RemovedCount} ({RemovedCollectionCount},{RemovedChildCount}) entities for {MovieCount} movies and {ShowCount} shows using Shoko Groups in {TimeSpent}. (Total={TotalCount})",
            addedCollections + addedChildren,
            addedCollections,
            addedChildren,
            fixedCollections - addedCollections,
            toCheck.Count + totalChildren - addedCollections - addedChildren - (fixedCollections - addedCollections),
            toCheck.Count - addedCollections - (fixedCollections - addedCollections),
            totalChildren - addedChildren,
            toRemove.Count + removedChildren,
            toRemove.Count,
            removedChildren,
            movies.Count,
            shows.Count,
            DateTime.Now - timeStarted,
            toCheck.Count + totalChildren
        );
    }

    #endregion

    #region Cleanup Helpers

    private async Task CleanupAll(IProgress<double> progress, CancellationToken cancellationToken) {
        await CleanupMovies().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        CleanupSeriesCollections();
        cancellationToken.ThrowIfCancellationRequested();

        CleanupGroupCollections();
        progress.Report(100d);
    }

    /// <summary>
    /// Check the movies with a shoko series id set, and remove the collection name from them.
    /// </summary>
    /// <returns>A task to await when it's done.</returns>
    private async Task CleanupMovies() {
        var movies = GetMovies();
        foreach (var movie in movies) {
            if (string.IsNullOrEmpty(movie.CollectionName))
                continue;

            if (!_lookup.TryGetEpisodeIdsFor(movie, out var episodeIds) ||
                !_apiManager.TryGetSeasonIdForEpisodeId(episodeIds[0], out var seasonId))
                continue;

            _logger.LogTrace("Removing movie {MovieName} from collection {CollectionName}. (Episode={EpisodeId},Season={SeasonId})", movie.Name, movie.CollectionName, episodeIds[0], seasonId);
            movie.CollectionName = string.Empty;
            await _libraryManager.UpdateItemAsync(movie, movie.GetParent(), ItemUpdateType.None, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void CleanupSeriesCollections() {
        var collectionDict = GetSeriesCollections();
        if (collectionDict.Count == 0)
            return;

        var collectionSet = collectionDict.Values
            .SelectMany(x => x.Select(y => y.Id))
            .Distinct()
            .Count();
        _logger.LogInformation("Going to remove {CollectionCount} collection items for {SeriesCount} Shoko Series", collectionSet, collectionDict.Count);

        foreach (var (seasonId, collectionList) in collectionDict)
            foreach (var collection in collectionList)
                RemoveCollection(collection, seasonId: seasonId);
    }

    private void CleanupGroupCollections() {
        var collectionDict = GetGroupCollections();
        if (collectionDict.Count == 0)
            return;

        var collectionSet = collectionDict.Values
            .SelectMany(x => x.Select(y => y.Id))
            .Distinct()
            .Count();
        _logger.LogInformation("Going to remove {CollectionCount} collection items for {GroupCount} Shoko Groups", collectionSet, collectionDict.Count);

        foreach (var (groupId, collectionList) in collectionDict)
            foreach (var collection in collectionList)
                RemoveCollection(collection, groupId: groupId);
    }

    private void RemoveCollection(BoxSet collection, string? seasonId = null, string? groupId = null) {
        var children = collection.Children.Concat(collection.GetLinkedChildren()).Select(x => x.Id).Distinct().Count();
        _logger.LogTrace("Removing collection {CollectionName} with {ChildCount} children. (Collection={CollectionId},Season={SeasonId},Group={GroupId})", collection.Name, children, collection.Id, seasonId, groupId);

        // Remove the item.
        _libraryManager.DeleteItem(collection, new() { DeleteFileLocation = true, DeleteFromExternalProvider = false });
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

    private Dictionary<string, IReadOnlyList<BoxSet>> GetSeriesCollections()
        => _libraryManager.GetItemList(new() {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            SourceTypes = [SourceType.Library],
            IsVirtualItem = false,
            Recursive = true,
        })
            .Cast<BoxSet>()
            .Select(x => x.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForSeries, out var seasonId) ? new { SeasonId = seasonId, BoxSet = x } : null)
            .WhereNotNull()
            .GroupBy(x => x.SeasonId, x => x.BoxSet)
            .ToDictionary(x => x.Key, x => x.ToList() as IReadOnlyList<BoxSet>);

    private Dictionary<string, IReadOnlyList<BoxSet>> GetGroupCollections()
        => _libraryManager.GetItemList(new() {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            SourceTypes = [SourceType.Library],
            IsVirtualItem = false,
            Recursive = true,
        })
            .Cast<BoxSet>()
            .Select(x => x.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForGroup, out var groupId) ? new { GroupId = groupId, BoxSet = x } : null)
            .WhereNotNull()
            .GroupBy(x => x.GroupId, x => x.BoxSet)
            .ToDictionary(x => x.Key, x => x.ToList() as IReadOnlyList<BoxSet>);

    #endregion
}