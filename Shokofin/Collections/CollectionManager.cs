using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Info;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using Directory = System.IO.Directory;
using Path = System.IO.Path;

namespace Shokofin.Collections;

public class CollectionManager
{
    private readonly IApplicationPaths ApplicationPaths;

    private readonly ILibraryManager LibraryManager;

    private readonly IFileSystem FileSystem;

    private readonly ICollectionManager Collection;
    
    private readonly ILocalizationManager LocalizationManager;

    private readonly ILogger<CollectionManager> Logger;

    private readonly IIdLookup Lookup;

    private readonly ShokoAPIManager ApiManager;

    private static int MinCollectionSize => Plugin.Instance.Configuration.CollectionMinSizeOfTwo ? 1 : 0;

    public CollectionManager(
        IApplicationPaths applicationPaths,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        ICollectionManager collectionManager,
        ILocalizationManager localizationManager,
        ILogger<CollectionManager> logger,
        IIdLookup lookup,
        ShokoAPIManager apiManager
    )
    {
        ApplicationPaths = applicationPaths;
        LibraryManager = libraryManager;
        FileSystem = fileSystem;
        Collection = collectionManager;
        LocalizationManager = localizationManager;
        Logger = logger;
        Lookup = lookup;
        ApiManager = apiManager;
    }

    // TODO: Replace this temp. impl. with the native impl on 10.9 after the migration.
    public async Task<Folder?> GetCollectionsFolder(bool createIfNeeded)
    {
        var path = Path.Combine(ApplicationPaths.DataPath, "collections");
        var collectionRoot = LibraryManager
            .RootFolder
            .Children
            .OfType<Folder>()
            .Where(i => FileSystem.AreEqual(path, i.Path) || FileSystem.ContainsSubPath(i.Path, path))
            .FirstOrDefault();
        if (collectionRoot is not null)
            return collectionRoot;

        if (!createIfNeeded)
            return null;

        Directory.CreateDirectory(path);

        var libraryOptions = new LibraryOptions {
            PathInfos = new[] { new MediaPathInfo(path) },
            EnableRealtimeMonitor = false,
            SaveLocalMetadata = true
        };

        var name = LocalizationManager.GetLocalizedString("Collections");

        await LibraryManager.AddVirtualFolder(name, CollectionTypeOptions.BoxSets, libraryOptions, true)
            .ConfigureAwait(false);

        return LibraryManager
            .RootFolder
            .Children
            .OfType<Folder>()
            .Where(i => FileSystem.AreEqual(path, i.Path) || FileSystem.ContainsSubPath(i.Path, path))
            .FirstOrDefault();
    }

    public async Task ReconstructCollections(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try {
            switch (Plugin.Instance.Configuration.CollectionGrouping)
            {
                default:
                    await CleanupAll(progress, cancellationToken);
                    break;
                case Ordering.CollectionCreationType.Movies:
                    await ReconstructMovieSeriesCollections(progress, cancellationToken);
                    break;
                case Ordering.CollectionCreationType.Shared:
                    await ReconstructSharedCollections(progress, cancellationToken);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
        }
    }

    #region Movie Collections

    private async Task ReconstructMovieSeriesCollections(IProgress<double> progress, CancellationToken cancellationToken)
    {
        Logger.LogTrace("Ensuring collection root exists…");
        var collectionRoot = (await GetCollectionsFolder(true).ConfigureAwait(false))!;

        var timeStarted = DateTime.Now;

        Logger.LogTrace("Cleaning up movies and invalid collections…");

        // Clean up movies and unneeded group collections.
        await CleanupMovies().ConfigureAwait(false);
        CleanupGroupCollections();

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

            var (fileInfo, seasonInfo, showInfo) = await ApiManager.GetFileInfoByPath(movie.Path).ConfigureAwait(false);
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
        foreach (var (seriesId, collectionList) in existingCollections) {
            if (seasonDict.ContainsKey(seriesId)) {
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
        foreach (var (id, collection) in toRemove) {
            // Remove the item from all parents.
            if (parentDict.TryGetValue(collection.Id, out var parents)) {
                foreach (var parentId in parents) {
                    if (!toRemove.ContainsKey(parentId) && collection.ParentId != parentId)
                        await Collection.RemoveFromCollectionAsync(parentId, new[] { id }).ConfigureAwait(false);
                }
            }

            // Log how many children we will be removing.
            removedChildren += childDict[collection.Id].Count;

            // Remove the item.
            LibraryManager.DeleteItem(collection, new() { DeleteFileLocation = true, DeleteFromExternalProvider = false });
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(70);

        // Add the missing collections.
        foreach (var missingId in toAdd) {
            var seasonInfo = seasonDict[missingId];
            var collection = await Collection.CreateCollectionAsync(new() {
                Name = $"{seasonInfo.Shoko.Name.ForceASCII()} [{ShokoCollectionSeriesId.Name}={missingId}]",
                ProviderIds = new() { { ShokoCollectionSeriesId.Name, missingId } },
            }).ConfigureAwait(false);

            childDict.Add(collection.Id, new());
            toCheck.Add(missingId, collection);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(80);

        // Check if the collection have the correct children, and add any
        // missing and remove any extras.
        var fixedCollections = 0;
        foreach (var (seriesId, collection) in toCheck)
        {
            // Edit the metadata to if needed.
            var updated = false;
            var seasonInfo = seasonDict[seriesId];
            var metadataLanguage = LibraryManager.GetLibraryOptions(collection)?.PreferredMetadataLanguage;
            var (displayName, alternateTitle) = Text.GetSeasonTitles(seasonInfo, metadataLanguage);
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
                await Collection.AddToCollectionAsync(collection.Id, missingMovies).ConfigureAwait(false);
            if (unwantedMovies.Count > 0)
                await Collection.RemoveFromCollectionAsync(collection.Id, unwantedMovies).ConfigureAwait(false);

            totalChildren += expectedMovies.Count;
            addedChildren += missingMovies.Count;
            removedChildren += unwantedMovies.Count;
        }

        progress.Report(100);

        Logger.LogInformation(
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

    private async Task ReconstructSharedCollections(IProgress<double> progress, CancellationToken cancellationToken)
    {
        Logger.LogTrace("Ensuring collection root exists…");
        var collectionRoot = (await GetCollectionsFolder(true).ConfigureAwait(false))!;

        var timeStarted = DateTime.Now;

        Logger.LogTrace("Cleaning up movies and invalid collections…");

        // Clean up movies and unneeded series collections.
        await CleanupMovies().ConfigureAwait(false);
        CleanupSeriesCollections();

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(10);

        // Get all shows/movies to include in the collection.
        var movies = GetMovies();
        var shows = GetShows();
        Logger.LogInformation("Checking collections for {MovieCount} movies and {ShowCount} shows using Shoko Groups.", movies.Count, shows.Count);

        // Create a tree-map of how it's supposed to be.
        var movieDict = new Dictionary<Movie, (FileInfo fileInfo, SeasonInfo seasonInfo, ShowInfo showInfo)>();
        foreach (var movie in movies) {
            var (fileInfo, seasonInfo, showInfo) = await ApiManager.GetFileInfoByPath(movie.Path).ConfigureAwait(false);
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

            var showInfo = await ApiManager.GetShowInfoForSeries(seriesId).ConfigureAwait(false);
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
                        ApiManager.GetCollectionInfoForGroup(groupBy.Key!)
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

            while (!currentGroup.IsTopLevel && !finalGroups.ContainsKey(currentGroup.ParentId!))
            {
                currentGroup = await ApiManager.GetCollectionInfoForGroup(currentGroup.ParentId!).ConfigureAwait(false);
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
                        await Collection.RemoveFromCollectionAsync(parentId, new[] { id }).ConfigureAwait(false);
                }
            }

            // Log how many children we will be removing.
            removedChildren += childDict[collection.Id].Count;

            // Remove the item.
            LibraryManager.DeleteItem(collection, new() { DeleteFileLocation = true, DeleteFromExternalProvider = false });
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
            var collection = await Collection.CreateCollectionAsync(new() {
                Name = $"{collectionInfo.Name.ForceASCII()} [{ShokoCollectionGroupId.Name}={missingId}]",
                ProviderIds = new() { { ShokoCollectionGroupId.Name, missingId } },
            }).ConfigureAwait(false);

            childDict.Add(collection.Id, new());
            toCheck.Add(missingId, collection);
            toAdd.RemoveAt(index);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(80);

        // Check if the collection have the correct children, and add any
        // missing and remove any extras.
        var fixedCollections = 0;
        foreach (var (groupId, collection) in toCheck)
        {
            // Edit the metadata to place the collection under the right parent and with the correct name.
            var collectionInfo = finalGroups[groupId];
            var updated = false;
            var parent = collectionInfo.IsTopLevel ? collectionRoot : toCheck[collectionInfo.ParentId!];
            if (collection.ParentId != parent.Id) {
                collection.SetParent(parent);
                updated = true;
            }
            if (!string.Equals(collection.Name, collectionInfo.Name)) {
                collection.Name = collectionInfo.Name;
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
                .Where(showInfo => !showInfo.IsMovieCollection)
                .SelectMany(showInfo => showDict.Where(pair => pair.Value.Id == showInfo.Id))
                .Select(pair => pair.Key)
                .ToList();
            var expectedMovies = collectionInfo.Shows
                .Where(showInfo => showInfo.IsMovieCollection)
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
                await Collection.AddToCollectionAsync(collection.Id, missingChildren).ConfigureAwait(false);
            if (unwantedChildren.Count > 0)
                await Collection.RemoveFromCollectionAsync(collection.Id, unwantedChildren).ConfigureAwait(false);

            totalChildren += expectedCollections.Count + expectedShows.Count + expectedMovies.Count;
            addedChildren += missingChildren.Count;
            removedChildren += unwantedChildren.Count;
        }

        progress.Report(100);

        Logger.LogInformation(
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

    private async Task CleanupAll(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await CleanupMovies();
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

    private void CleanupSeriesCollections()
    {
        var collectionDict = GetSeriesCollections();
        if (collectionDict.Count == 0)
            return;

        var collectionSet = collectionDict.Values
            .SelectMany(x => x.Select(y => y.Id))
            .Distinct()
            .Count();
        Logger.LogInformation("Going to remove {CollectionCount} collection items for {SeriesCount} Shoko Series", collectionSet, collectionDict.Count);

        foreach (var (seriesId, collectionList) in collectionDict)
            foreach (var collection in collectionList)
                RemoveCollection(collection, seriesId: seriesId);
    }

    private void CleanupGroupCollections()
    {
        var collectionDict = GetGroupCollections();
        if (collectionDict.Count == 0)
            return;

        var collectionSet = collectionDict.Values
            .SelectMany(x => x.Select(y => y.Id))
            .Distinct()
            .Count();
        Logger.LogInformation("Going to remove {CollectionCount} collection items for {GroupCount} Shoko Groups", collectionSet, collectionDict.Count);

        foreach (var (groupId, collectionList) in collectionDict)
            foreach (var collection in collectionList)
                RemoveCollection(collection, groupId: groupId);
    }

    private void RemoveCollection(BoxSet collection, string? seriesId = null, string? groupId = null)
    {
        var children = collection.Children.Concat(collection.GetLinkedChildren()).Select(x => x.Id).Distinct().Count();
        Logger.LogTrace("Removing collection {CollectionName} with {ChildCount} children. (Collection={CollectionId},Series={SeriesId},Group={GroupId})", collection.Name, children, collection.Id, seriesId, groupId);

        // Remove the item.
        LibraryManager.DeleteItem(collection, new() { DeleteFileLocation = true, DeleteFromExternalProvider = false });
    }

    #endregion

    #region Getter Helpers

    private List<Movie> GetMovies()
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

    private List<Series> GetShows()
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

    private Dictionary<string, IReadOnlyList<BoxSet>> GetSeriesCollections()
    {
        return LibraryManager.GetItemList(new()
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            HasAnyProviderId = new Dictionary<string, string> { { ShokoCollectionSeriesId.Name, string.Empty } },
            IsVirtualItem = false,
            Recursive = true,
        })
            .Cast<BoxSet>()
            .Select(x => x.ProviderIds.TryGetValue(ShokoCollectionSeriesId.Name, out var seriesId) && !string.IsNullOrEmpty(seriesId) ? new { SeriesId = seriesId, BoxSet = x } : null)
            .Where(x => x != null)
            .GroupBy(x => x!.SeriesId, x => x!.BoxSet)
            .ToDictionary(x => x.Key, x => x.ToList() as IReadOnlyList<BoxSet>);
    }

    private Dictionary<string, IReadOnlyList<BoxSet>> GetGroupCollections()
    {
        return LibraryManager.GetItemList(new()
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },

            HasAnyProviderId = new Dictionary<string, string> { { ShokoCollectionGroupId.Name, string.Empty } },
            IsVirtualItem = false,
            Recursive = true,
        })
            .Cast<BoxSet>()
            .Select(x => x.ProviderIds.TryGetValue(ShokoCollectionGroupId.Name, out var groupId) && !string.IsNullOrEmpty(groupId) ? new { GroupId = groupId, BoxSet = x } : null)
            .Where(x => x != null)
            .GroupBy(x => x!.GroupId, x => x!.BoxSet)
            .ToDictionary(x => x.Key, x => x.ToList() as IReadOnlyList<BoxSet>);
    }

    #endregion
}