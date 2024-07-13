using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Info;
using Shokofin.Collections;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Providers;

/// <summary>
/// The custom episode provider. Responsible for de-duplicating episodes.
/// </summary>
/// <remarks>
/// This needs to be it's own class because of internal Jellyfin shenanigans
/// about how a provider cannot also be a custom provider otherwise it won't
/// save the metadata.
/// </remarks>
public class CustomBoxSetProvider : ICustomMetadataProvider<BoxSet>
{
    public string Name => Plugin.MetadataProviderName;

    private readonly ILogger<CustomBoxSetProvider> Logger;

    private readonly ShokoAPIManager ApiManager;

    private readonly ILibraryManager LibraryManager;

    private readonly CollectionManager CollectionManager;

    public CustomBoxSetProvider(ILogger<CustomBoxSetProvider> logger, ShokoAPIManager apiManager, ILibraryManager libraryManager, CollectionManager collectionManager)
    {
        Logger = logger;
        ApiManager = apiManager;
        LibraryManager = libraryManager;
        CollectionManager = collectionManager;
    }

    public async Task<ItemUpdateType> FetchAsync(BoxSet collection, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        // Abort if the collection root is not made yet (which should never happen).
        var collectionRoot = await CollectionManager.GetCollectionsFolder(false);
        if (collectionRoot is null)
            return ItemUpdateType.None;

        // Try to read the shoko group id
        if (collection.TryGetProviderId(ShokoCollectionGroupId.Name, out var collectionId) || collection.Path.TryGetAttributeValue(ShokoCollectionGroupId.Name, out collectionId))
            using (Plugin.Instance.Tracker.Enter($"Providing custom info for Collection \"{collection.Name}\". (Path=\"{collection.Path}\",Collection=\"{collectionId}\")"))
                if (await EnsureGroupCollectionIsCorrect(collectionRoot, collection, collectionId, cancellationToken))
                    return ItemUpdateType.MetadataEdit;

        // Try to read the shoko series id
        if (collection.TryGetProviderId(ShokoCollectionSeriesId.Name, out var seriesId) || collection.Path.TryGetAttributeValue(ShokoCollectionSeriesId.Name, out seriesId))
            using (Plugin.Instance.Tracker.Enter($"Providing custom info for Collection \"{collection.Name}\". (Path=\"{collection.Path}\",Series=\"{seriesId}\")"))
                if (await EnsureSeriesCollectionIsCorrect(collection, seriesId, cancellationToken))
                    return ItemUpdateType.MetadataEdit;

        return ItemUpdateType.None;
    }

    private async Task<bool> EnsureSeriesCollectionIsCorrect(BoxSet collection, string seriesId, CancellationToken cancellationToken)
    {
        var seasonInfo = await ApiManager.GetSeasonInfoForSeries(seriesId);
        if (seasonInfo is null)
            return false;

        var updated = EnsureNoTmdbIdIsSet(collection);
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
            await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken);
            Logger.LogDebug("Fixed collection {CollectionName} (Series={SeriesId})", collection.Name, seriesId);
        }

        return updated;
    }

    private async Task<bool> EnsureGroupCollectionIsCorrect(Folder collectionRoot, BoxSet collection, string collectionId, CancellationToken cancellationToken)
    {
        var collectionInfo = await ApiManager.GetCollectionInfoForGroup(collectionId);
        if (collectionInfo is null)
            return false;

        var updated = EnsureNoTmdbIdIsSet(collection);
        var parent = collectionInfo.IsTopLevel ? collectionRoot : await GetCollectionByGroupId(collectionRoot, collectionInfo.ParentId);
        if (collection.ParentId != parent.Id) {
            collection.SetParent(parent);
            updated = true;
        }
        if (!string.Equals(collection.Name, collectionInfo.Name)) {
            collection.Name = collectionInfo.Name;
            updated = true;
        }
        if (updated) {
            await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken);
            Logger.LogDebug("Fixed collection {CollectionName} (Group={GroupId})", collection.Name, collectionId);
        }

        return updated;
    }

    private bool EnsureNoTmdbIdIsSet(BoxSet collection)
    {
        var willRemove = collection.HasProviderId(MetadataProvider.TmdbCollection);
        collection.SetProviderId(MetadataProvider.TmdbCollection.ToString(), null);
        return willRemove;
    }

    private async Task<BoxSet> GetCollectionByGroupId(Folder collectionRoot, string? collectionId)
    {
        if (string.IsNullOrEmpty(collectionId))
            throw new ArgumentNullException(nameof(collectionId));

        var collectionInfo = await ApiManager.GetCollectionInfoForGroup(collectionId) ??
            throw new Exception($"Unable to find collection info for the parent collection with id \"{collectionId}\"");

        var collection = GetCollectionByPath(collectionRoot, collectionInfo);
        if (collection is not null)
            return collection;

        var list = LibraryManager.GetItemList(new()
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },

            HasAnyProviderId = new() { { ShokoCollectionGroupId.Name, collectionId } },
            IsVirtualItem = false,
            Recursive = true,
        })
            .OfType<BoxSet>()
            .ToList();
        if (list.Count == 0) {
            throw new NullReferenceException("Unable to a find collection with the given group id.");
        }
        if (list.Count > 1) {
            throw new Exception("Found multiple collections with the same group id.");
        }
        return list[0]!;
    }

    private BoxSet? GetCollectionByPath(Folder collectionRoot, CollectionInfo collectionInfo)
    {
        var baseName = $"{collectionInfo.Name.ForceASCII()} [{ShokoCollectionGroupId.Name}={collectionInfo.Id}]";
        var folderName = BaseItem.FileSystem.GetValidFilename(baseName) + " [boxset]";
        var path = Path.Combine(collectionRoot.Path, folderName);
        return LibraryManager.FindByPath(path, true) as BoxSet;
    }

}
