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

#nullable enable
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
        try
        {
            switch (Plugin.Instance.Configuration.CollectionGrouping)
            {
                default:
                    break;
                case Ordering.CollectionCreationType.ShokoSeries:
                    await ReconstructMovieSeriesCollections(progress, cancellationToken);
                    break;
                case Ordering.CollectionCreationType.ShokoGroup:
                    await ReconstructSharedCollections(progress, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
        }
    }

    private async Task ReconstructMovieSeriesCollections(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Get all movies

        // Clean up the movies
        await CleanupMovies();

        await CleanupGroupCollections();

        var movies = GetMovies();
        Logger.LogInformation("Reconstructing collections for {MovieCount} movies using Shoko Series.", movies.Count);

        // create a tree-map of how it's supposed to be.
        var config = Plugin.Instance.Configuration;
        var movieDict = new Dictionary<Movie, (FileInfo, SeasonInfo, ShowInfo)>();
        foreach (var movie in movies)
        {
            if (!Lookup.TryGetEpisodeIdsFor(movie, out var episodeIds))
                continue;

            var (fileInfo, seasonInfo, showInfo) = await ApiManager.GetFileInfoByPath(movie.Path);
            if (fileInfo == null || seasonInfo == null || showInfo == null)
                continue;

            movieDict.Add(movie, (fileInfo, seasonInfo, showInfo));
        }

        var seriesDict = movieDict.Values
            .Select(tuple => tuple.Item2)
            .DistinctBy(seasonInfo => seasonInfo.Id)
            .ToDictionary(seasonInfo => seasonInfo.Id);
        var groupsDict = await Task
            .WhenAll(
                seriesDict.Values
                    .Select(seasonInfo => seasonInfo.Shoko.IDs.ParentGroup.ToString())
                    .Distinct()
                    .Select(groupId => ApiManager.GetCollectionInfoForGroup(groupId))
            )
            .ContinueWith(task => task.Result.ToDictionary(x => x!.Id, x => x!));

        var finalGroups = new Dictionary<string, CollectionInfo>();
        foreach (var initialGroup in groupsDict.Values)
        {
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

        var existingCollections = GetSeriesCollections();
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

        // Add the missing collections.
        foreach (var missingId in toAdd)
        {
            var collectionInfo = finalGroups[missingId];
            var collection = await Collection.CreateCollectionAsync(new() {
                Name = collectionInfo.Name,
                ProviderIds = new() { { ShokoGroupId.Name, missingId } },
            });
            toCheck.Add(missingId, collection);
        }

        // Check the collections.
        foreach (var (groupId, collection) in toCheck)
        {
            var collectionInfo = finalGroups[groupId];
            // Check if the collection have the correct children
            
        }
    }

    private async Task ReconstructMovieGroupCollections(IProgress<double> progress, CancellationToken cancellationToken)
    {

        // Clean up the movies
        await CleanupMovies();

        await CleanupSeriesCollections();

        var movies = GetMovies();
        Logger.LogInformation("Reconstructing collections for {MovieCount} movies using Shoko Groups.", movies.Count);

        // create a tree-map of how it's supposed to be.

        // create a tree-map of how it's supposed to be.
        var config = Plugin.Instance.Configuration;
        var movieDict = new Dictionary<Movie, (FileInfo, SeasonInfo, ShowInfo)>();
        foreach (var movie in movies)
        {
            if (!Lookup.TryGetEpisodeIdsFor(movie, out var episodeIds))
                continue;

            var (fileInfo, seasonInfo, showInfo) = await ApiManager.GetFileInfoByPath(movie.Path);
            if (fileInfo == null || seasonInfo == null || showInfo == null)
                continue;

            movieDict.Add(movie, (fileInfo, seasonInfo, showInfo));
        }

        var seriesDict = movieDict.Values
            .Select(tuple => tuple.Item2)
            .DistinctBy(seasonInfo => seasonInfo.Id)
            .ToDictionary(seasonInfo => seasonInfo.Id);
        var groupsDict = await Task
            .WhenAll(
                seriesDict.Values
                    .Select(seasonInfo => seasonInfo.Shoko.IDs.ParentGroup.ToString())
                    .Distinct()
                    .Select(groupId => ApiManager.GetCollectionInfoForGroup(groupId))
            )
            .ContinueWith(task => task.Result.ToDictionary(x => x!.Id, x => x!));

        var finalGroups = new Dictionary<string, CollectionInfo>();
        foreach (var initialGroup in groupsDict.Values)
        {
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

        var existingCollections = GetGroupCollections();
        var toCheck = new Dictionary<string, BoxSet>();
        var toRemove = new Dictionary<Guid, (string GroupId, BoxSet Collection)>();
        var toAdd = finalGroups.Keys
            .Where(groupId => !existingCollections.ContainsKey(groupId))
            .ToHashSet();
        var idToGuidDict = new Dictionary<string, Guid>();

        foreach (var (groupId, collectionList) in existingCollections) {
            if (finalGroups.ContainsKey(groupId)) {
                idToGuidDict.Add(groupId, collectionList[0].Id);
                toCheck.Add(groupId, collectionList[0]);
                foreach (var collection in collectionList.Skip(1))
                    toRemove.Add(collection.Id, (groupId, collection));
            }
            else {
                foreach (var collection in collectionList)
                    toRemove.Add(collection.Id, (groupId, collection));
            }
        }

        var toRemoveSet = toRemove.Keys.ToHashSet();
        foreach (var (id, (groupId, boxSet)) in toRemove)
            await RemoveCollection(boxSet, toRemoveSet, groupId: groupId);

        // Add the missing collections.
        foreach (var missingId in toAdd)
        {
            var collectionInfo = finalGroups[missingId];
            var collection = await Collection.CreateCollectionAsync(new() {
                Name = collectionInfo.Name,
                ProviderIds = new() { { ShokoGroupId.Name, missingId } },
            });
            toCheck.Add(missingId, collection);
        }

        // Check the collections.
        foreach (var (groupId, collection) in toCheck)
        {
            var collectionInfo = finalGroups[groupId];
            // Check if the collection have the correct children
            
        }
    }

    private async Task ReconstructSharedCollections(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Get all movies

        // Clean up the movies
        await CleanupMovies();

        await CleanupSeriesCollections();

        // Get all shows
        var movies = GetMovies();
        var shows = GetShows();
        Logger.LogInformation("Reconstructing collections for {MovieCount} movies and {ShowCount} shows using Shoko Groups.", movies.Count, shows.Count);

        // create a tree-map of how it's supposed to be.

        var collections = GetSeriesCollections();

        // check which nodes are correct, which nodes is not correct, and which are missing.

        // fix the nodes that are not correct.

        // add the missing nodes.
    }

    private async Task CleanupMovies()
    {
        // Check the movies with a shoko series id set, and remove the collection name from them.
        var movies = GetMovies();
        foreach (var movie in movies)
        {
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
        var collectionMap = collectionDict.Values
            .SelectMany(x => x.Select(y => y.Id))
            .ToHashSet();

        Logger.LogInformation("Going to remove {CollectionCount} collection items for {SeriesCount} Shoko Series", collectionMap.Count, collectionDict.Count);


        foreach (var (seriesId, collectionList) in collectionDict)
            foreach (var collection in collectionList)
                await RemoveCollection(collection, collectionMap, seriesId: seriesId);
    }

    private async Task CleanupGroupCollections()
    {

        var collectionDict = GetGroupCollections();
        var collectionMap = collectionDict.Values
            .SelectMany(x => x.Select(y => y.Id))
            .ToHashSet();

        Logger.LogInformation("Going to remove {CollectionCount} collection items for {GroupCount} Shoko Groups", collectionMap.Count, collectionDict.Count);

        foreach (var (groupId, collectionList) in collectionDict)
            foreach (var collection in collectionList)
                await RemoveCollection(collection, collectionMap, groupId: groupId);
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
            HasAnyProviderId = new Dictionary<string, string> { { ShokoFileId.Name, "" } },
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
            HasAnyProviderId = new Dictionary<string, string> { { ShokoSeriesId.Name, "" } },
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
            HasAnyProviderId = new Dictionary<string, string> { { ShokoSeriesId.Name, "" } },
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
            
            HasAnyProviderId = new Dictionary<string, string> { { ShokoGroupId.Name, "" } },
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