using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Info;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Collections;

public class CollectionManager
{
    private readonly ILibraryManager LibraryManager;

    private readonly ICollectionManager Collection;

    private readonly ILogger<CollectionManager> Logger;

    private readonly IIdLookup Lookup;

    private readonly ShokoAPIManager ApiManager;

    public CollectionManager(ILibraryManager libraryManager, ICollectionManager collectionManager, ILogger<CollectionManager> logger, IIdLookup lookup, ShokoAPIManager apiManager)
    {
        LibraryManager = libraryManager;
        Collection = collectionManager;
        Logger = logger;
        Lookup = lookup;
        ApiManager = apiManager;
    }

    public async Task ReconstructCollections(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try {
            switch (Plugin.Instance.Configuration.CollectionGrouping)
            {
                default:
                    break;
                case Ordering.CollectionCreationType.Movies:
                    await ReconstructMovieSeriesCollections(progress, cancellationToken);
                    break;
                case Ordering.CollectionCreationType.Shared:
                    await ReconstructSharedCollections(progress, cancellationToken);
                    break;
            }
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
        }
    }

    private async Task ReconstructMovieSeriesCollections(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Clean up movies and unneeded group collections.
        await CleanupMovies();
        await CleanupGroupCollections();

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(10);

        // Get all movies to include in the collection.
        var movies = GetMovies();
        Logger.LogInformation("Reconstructing collections for {MovieCount} movies using Shoko Series.", movies.Count);

        // Create a tree-map of how it's supposed to be.
        var movieDict = new Dictionary<Movie, (FileInfo fileInfo, SeasonInfo seasonInfo, ShowInfo showInfo)>();
        foreach (var movie in movies) {
            if (!Lookup.TryGetEpisodeIdsFor(movie, out var episodeIds))
                continue;

            var (fileInfo, seasonInfo, showInfo) = await ApiManager.GetFileInfoByPath(movie.Path);
            if (fileInfo == null || seasonInfo == null || showInfo == null)
                continue;

            movieDict.Add(movie, (fileInfo, seasonInfo, showInfo));
        }
        var seasonDict = movieDict.Values
            .Select(tuple => tuple.seasonInfo)
            .DistinctBy(seasonInfo => seasonInfo.Id)
            .ToDictionary(seasonInfo => seasonInfo.Id);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(30);

        // Find out what to add, what to remove and what to check.
        var existingCollections = GetSeriesCollections();
        var toCheck = new Dictionary<string, BoxSet>();
        var toRemove = new Dictionary<Guid, BoxSet>();
        var toAdd = seasonDict.Keys
            .Where(groupId => !existingCollections.ContainsKey(groupId))
            .ToHashSet();
        var idToGuidDict = new Dictionary<string, Guid>();

        foreach (var (seriesId, collectionList) in existingCollections) {
            if (seasonDict.ContainsKey(seriesId)) {
                idToGuidDict.Add(seriesId, collectionList[0].Id);
                toCheck.Add(seriesId, collectionList[0]);
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
        foreach (var (id, boxSet) in toRemove) {
            // Remove the item from all parents.
            foreach (var parent in boxSet.GetParents().OfType<BoxSet>()) {
                if (toRemove.ContainsKey(parent.Id))
                    continue;
                await Collection.RemoveFromCollectionAsync(parent.Id, new[] { id });
            }

            // Remove all children
            var children = boxSet.GetChildren(null, true, new()).Select(x => x.Id);
            await Collection.RemoveFromCollectionAsync(id, children);

            // Remove the item.
            LibraryManager.DeleteItem(boxSet, new() { DeleteFileLocation = false, DeleteFromExternalProvider = false });
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(70);

        // Add the missing collections.
        foreach (var missingId in toAdd) {
            var seasonInfo = seasonDict[missingId];
            var (displayName, _) = Text.GetSeasonTitles(seasonInfo, "en");
            var collection = await Collection.CreateCollectionAsync(new() {
                Name = displayName,
                ProviderIds = new() { { ShokoSeriesId.Name, missingId } },
            });
            toCheck.Add(missingId, collection);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(80);

        // Check if the collection have the correct children, and add any
        // missing and remove any extras.
        foreach (var (seriesId, collection) in toCheck)
        {
            var actualChildren = collection.Children.ToList();
            var actualChildMovies = new List<Movie>();
            foreach (var child in actualChildren) switch (child) {
                case Movie movie:
                    actualChildMovies.Add(movie);
                    break;
            }

            var seasonInfo = seasonDict[seriesId];
            var expectedMovies = seasonInfo.EpisodeList.Concat(seasonInfo.AlternateEpisodesList)
                .Select(episodeInfo => (episodeInfo, seasonInfo))
                .SelectMany(tuple => movieDict.Where(pair => pair.Value.seasonInfo.Id == tuple.seasonInfo.Id && pair.Value.fileInfo.EpisodeList.Any(episodeInfo => episodeInfo.Id == tuple.episodeInfo.Id)))
                .Select(pair => pair.Key)
                .ToList();
            var missingMovies = expectedMovies
                .Select(movie => movie.Id)
                .Except(actualChildMovies.Select(a => a.Id).ToHashSet())
                .ToList();
            var childrenToRemove = actualChildren
                .Except(actualChildMovies)
                .Select(movie => movie.Id)
                .ToList();
            await Collection.AddToCollectionAsync(collection.Id, missingMovies);
            await Collection.RemoveFromCollectionAsync(collection.Id, childrenToRemove);
        }

        progress.Report(100);
    }

    private async Task ReconstructSharedCollections(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Get all movies

        // Clean up movies and unneeded series collections.
        await CleanupMovies();
        await CleanupSeriesCollections();

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(10);

        // Get all shows/movies to include in the collection.
        var movies = GetMovies();
        var shows = GetShows();
        Logger.LogInformation("Reconstructing collections for {MovieCount} movies and {ShowCount} shows using Shoko Groups.", movies.Count, shows.Count);

        // Create a tree-map of how it's supposed to be.
        var movieDict = new Dictionary<Movie, (FileInfo fileInfo, SeasonInfo seasonInfo, ShowInfo showInfo)>();
        foreach (var movie in movies) {
            if (!Lookup.TryGetEpisodeIdsFor(movie, out var episodeIds))
                continue;

            var (fileInfo, seasonInfo, showInfo) = await ApiManager.GetFileInfoByPath(movie.Path);
            if (fileInfo == null || seasonInfo == null || showInfo == null)
                continue;

            movieDict.Add(movie, (fileInfo, seasonInfo, showInfo));
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(20);

        var showDict = new Dictionary<Series, ShowInfo>();
        foreach (var show in shows) {
            if (!Lookup.TryGetSeriesIdFor(show, out var seriesId))
                continue;

            var showInfo = await ApiManager.GetShowInfoForSeries(seriesId);
            if (showInfo == null)
                continue;

            showDict.Add(show, showInfo);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(30);

        var groupsDict = await Task
            .WhenAll(
                movieDict.Values
                    .Select(tuple => tuple.seasonInfo)
                    .DistinctBy(seasonInfo => seasonInfo.Id)
                    .Select(seasonInfo => seasonInfo.Shoko.IDs.ParentGroup.ToString())
                    .Concat(showDict.Values.Select(showInfo => showInfo.CollectionId).Where(collectionId => !string.IsNullOrEmpty(collectionId)).OfType<string>())
                    .Distinct()
                    .Select(groupId => ApiManager.GetCollectionInfoForGroup(groupId))
            )
            .ContinueWith(task => task.Result.ToDictionary(x => x!.Id, x => x!));
        var finalGroups = new Dictionary<string, CollectionInfo>();
        foreach (var initialGroup in groupsDict.Values) {
            var currentGroup = initialGroup;
            if (finalGroups.ContainsKey(currentGroup.Id))
                continue;

            finalGroups.Add(currentGroup.Id, currentGroup);
            if (currentGroup.IsTopLevel)
                continue;

            while (!currentGroup.IsTopLevel && !finalGroups.ContainsKey(currentGroup.ParentId!))
            {
                currentGroup = await ApiManager.GetCollectionInfoForGroup(currentGroup.ParentId!);
                if (currentGroup == null)
                    break;
                finalGroups.Add(currentGroup.Id, currentGroup);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(40);

        // Find out what to add, what to remove and what to check.
        var existingCollections = GetGroupCollections();
        var toCheck = new Dictionary<string, BoxSet>();
        var toRemove = new Dictionary<Guid, BoxSet>();
        var toAdd = finalGroups.Keys
            .Where(groupId => !existingCollections.ContainsKey(groupId))
            .ToHashSet();
        var idToGuidDict = new Dictionary<string, Guid>();

        foreach (var (groupId, collectionList) in existingCollections) {
            if (finalGroups.ContainsKey(groupId)) {
                idToGuidDict.Add(groupId, collectionList[0].Id);
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
        foreach (var (id, boxSet) in toRemove) {
            // Remove the item from all parents.
            foreach (var parent in boxSet.GetParents().OfType<BoxSet>()) {
                if (toRemove.ContainsKey(parent.Id))
                    continue;
                await Collection.RemoveFromCollectionAsync(parent.Id, new[] { id });
            }

            // Remove all children
            var children = boxSet.GetChildren(null, true, new()).Select(x => x.Id);
            await Collection.RemoveFromCollectionAsync(id, children);

            // Remove the item.
            LibraryManager.DeleteItem(boxSet, new() { DeleteFileLocation = false, DeleteFromExternalProvider = false });
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(70);

        // Add the missing collections.
        foreach (var missingId in toAdd) {
            var collectionInfo = finalGroups[missingId];
            var collection = await Collection.CreateCollectionAsync(new() {
                Name = collectionInfo.Name,
                ProviderIds = new() { { ShokoGroupId.Name, missingId } },
            });
            toCheck.Add(missingId, collection);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(80);

        // Check if the collection have the correct children, and add any
        // missing and remove any extras.
        foreach (var (groupId, collection) in toCheck)
        {
            var actualChildren = collection.Children.ToList();
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

            var collectionInfo = finalGroups[groupId];
            var expectedCollections = collectionInfo.SubCollections
                .Select(subCollectionInfo => toCheck.TryGetValue(subCollectionInfo.Id, out var boxSet) ? boxSet : null)
                .OfType<BoxSet>()
                .ToList();
            var missingCollections = expectedCollections
                .Select(show => show.Id)
                .Except(actualChildCollections.Select(a => a.Id).ToHashSet())
                .ToList();
            var expectedShows = collectionInfo.Shows
                .Where(showInfo => !showInfo.IsMovieCollection)
                .SelectMany(showInfo => showDict.Where(pair => pair.Value.Id == showInfo.Id))
                .Select(pair => pair.Key)
                .ToList();
            var missingShows = expectedShows
                .Select(show => show.Id)
                .Except(actualChildSeries.Select(a => a.Id).ToHashSet())
                .ToList();
            var expectedMovies = collectionInfo.Shows
                .Where(showInfo => showInfo.IsMovieCollection)
                .SelectMany(showInfo => showInfo.DefaultSeason.EpisodeList.Concat(showInfo.DefaultSeason.AlternateEpisodesList).Select(episodeInfo => (episodeInfo, seasonInfo: showInfo.DefaultSeason)))
                .SelectMany(tuple => movieDict.Where(pair => pair.Value.seasonInfo.Id == tuple.seasonInfo.Id && pair.Value.fileInfo.EpisodeList.Any(episodeInfo => episodeInfo.Id == tuple.episodeInfo.Id)))
                .Select(pair => pair.Key)
                .ToList();
            var missingMovies = expectedMovies
                .Select(movie => movie.Id)
                .Except(actualChildMovies.Select(a => a.Id).ToHashSet())
                .ToList();
            var childrenToRemove = actualChildren
                .Except(actualChildCollections)
                .Except(actualChildSeries)
                .Except(actualChildMovies)
                .Select(movie => movie.Id)
                .ToList();
            await Collection.AddToCollectionAsync(collection.Id, missingCollections.Concat(missingShows).Concat(missingMovies));
            await Collection.RemoveFromCollectionAsync(collection.Id, childrenToRemove);
        }

        progress.Report(100);
    }

    /// <summary>
    /// Check the movies with a shoko series id set, and remove the collection name from them.
    /// </summary>
    /// <returns>A task to await when it's done.</returns>
    private async Task CleanupMovies()
    {
        var movies = GetMovies();
        foreach (var movie in movies) {
            if (string.IsNullOrEmpty(movie.CollectionName))
                continue;

            if (!Lookup.TryGetEpisodeIdFor(movie, out var episodeId) ||
                !Lookup.TryGetSeriesIdFor(movie, out var seriesId))
                continue;

            Logger.LogTrace("Removing movie {MovieName} from collection {CollectionName}. (Episode={EpisodeId},Series={SeriesId})", movie.Name, movie.CollectionName, episodeId, seriesId);
            movie.CollectionName = string.Empty;
            await LibraryManager.UpdateItemAsync(movie, movie.GetParent(), ItemUpdateType.None, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task CleanupSeriesCollections()
    {
        var collectionDict = GetSeriesCollections();
        var collectionSet = collectionDict.Values
            .SelectMany(x => x.Select(y => y.Id))
            .ToHashSet();

        if (collectionDict.Count == 0)
            return;

        Logger.LogInformation("Going to remove {CollectionCount} collection items for {SeriesCount} Shoko Series", collectionSet.Count, collectionDict.Count);

        foreach (var (seriesId, collectionList) in collectionDict)
            foreach (var collection in collectionList)
                await RemoveCollection(collection, collectionSet, seriesId: seriesId);
    }

    private async Task CleanupGroupCollections()
    {

        var collectionDict = GetGroupCollections();
        var collectionSet = collectionDict.Values
            .SelectMany(x => x.Select(y => y.Id))
            .ToHashSet();

        if (collectionDict.Count == 0)
            return;

        Logger.LogInformation("Going to remove {CollectionCount} collection items for {GroupCount} Shoko Groups", collectionSet.Count, collectionDict.Count);

        foreach (var (groupId, collectionList) in collectionDict)
            foreach (var collection in collectionList)
                await RemoveCollection(collection, collectionSet, groupId: groupId);
    }

    private async Task RemoveCollection(BoxSet boxSet, ISet<Guid> allBoxSets, string? seriesId = null, string? groupId = null)
    {
        var parents = boxSet.GetParents().OfType<BoxSet>().ToList();
        var children = boxSet.GetChildren(null, true, new()).Select(x => x.Id).ToList();
        Logger.LogTrace("Removing collection {CollectionName} with {ParentCount} parents and {ChildCount} children. (Collection={CollectionId},Series={SeriesId},Group={GroupId})", boxSet.Name, parents.Count, children.Count, boxSet.Id, seriesId, groupId);

        // Remove the item from all parents.
        foreach (var parent in parents) {
            if (allBoxSets.Contains(parent.Id))
                continue;
            await Collection.RemoveFromCollectionAsync(parent.Id, new[] { boxSet.Id });
        }

        // Remove all children
        await Collection.RemoveFromCollectionAsync(boxSet.Id, children);

        // Remove the item.
        LibraryManager.DeleteItem(boxSet, new() { DeleteFileLocation = false, DeleteFromExternalProvider = false });
    }

    private IReadOnlyList<Movie> GetMovies()
    {
        return LibraryManager.GetItemList(new()
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            HasAnyProviderId = new Dictionary<string, string> { { ShokoFileId.Name, string.Empty } },
            IsVirtualItem = false,
            Recursive = true,
        })
            .Where(Lookup.IsEnabledForItem)
            .Cast<Movie>()
            .ToList();
    }

    private IReadOnlyList<Series> GetShows()
    {
        return LibraryManager.GetItemList(new()
        {
            IncludeItemTypes = new[] { BaseItemKind.Series },
            HasAnyProviderId = new Dictionary<string, string> { { ShokoSeriesId.Name, string.Empty } },
            IsVirtualItem = false,
            Recursive = true,
        })
            .Where(Lookup.IsEnabledForItem)
            .Cast<Series>()
            .ToList();
    }

    private IReadOnlyDictionary<string, IReadOnlyList<BoxSet>> GetSeriesCollections()
    {
        return LibraryManager.GetItemList(new()
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            HasAnyProviderId = new Dictionary<string, string> { { ShokoSeriesId.Name, string.Empty } },
            IsVirtualItem = false,
            Recursive = true,
        })
            .Cast<BoxSet>()
            .Select(x => x.ProviderIds.TryGetValue(ShokoSeriesId.Name, out var seriesId) && !string.IsNullOrEmpty(seriesId) ? new { SeriesId = seriesId, BoxSet = x } : null)
            .Where(x => x != null)
            .GroupBy(x => x!.SeriesId, x => x!.BoxSet)
            .ToDictionary(x => x.Key, x => x.ToList() as IReadOnlyList<BoxSet>);
    }

    private IReadOnlyDictionary<string, IReadOnlyList<BoxSet>> GetGroupCollections()
    {
        return LibraryManager.GetItemList(new()
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },

            HasAnyProviderId = new Dictionary<string, string> { { ShokoGroupId.Name, string.Empty } },
            IsVirtualItem = false,
            Recursive = true,
        })
            .Cast<BoxSet>()
            .Select(x => x.ProviderIds.TryGetValue(ShokoGroupId.Name, out var groupId) && !string.IsNullOrEmpty(groupId) ? new { GroupId = groupId, BoxSet = x } : null)
            .Where(x => x != null)
            .GroupBy(x => x!.GroupId, x => x!.BoxSet)
            .ToDictionary(x => x.Key, x => x.ToList() as IReadOnlyList<BoxSet>);
    }
}