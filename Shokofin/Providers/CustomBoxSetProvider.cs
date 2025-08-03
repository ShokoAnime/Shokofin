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
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Providers;
#pragma warning disable IDE0059
#pragma warning disable IDE0290

/// <summary>
/// The custom episode provider. Responsible for de-duplicating episodes.
/// </summary>
/// <remarks>
/// This needs to be it's own class because of internal Jellyfin shenanigans
/// about how a provider cannot also be a custom provider otherwise it won't
/// save the metadata.
/// </remarks>
public class CustomBoxSetProvider(ILogger<CustomBoxSetProvider> _logger, ShokoApiManager _apiManager, ILibraryManager _libraryManager, CollectionManager _collectionManager)
    : IHasItemChangeMonitor, ICustomMetadataProvider<BoxSet> {
    public string Name => Plugin.MetadataProviderName;

    public bool HasChanged(BaseItem item, IDirectoryService directoryService) {
        // We're only interested in box sets.
        if (item is not BoxSet collection)
            return false;

        // Try to read the shoko group id.
        if (collection.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForGroup, out var collectionId))
            return true;

        // Try to read the shoko series id.
        if (collection.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForSeries, out var seasonId))
            return true;

        return false;
    }

    public async Task<ItemUpdateType> FetchAsync(BoxSet collection, MetadataRefreshOptions options, CancellationToken cancellationToken) {
        // Abort if the collection root is not made yet (which should never happen).
        var collectionRoot = await _collectionManager.GetCollectionsFolder(false).ConfigureAwait(false);
        if (collectionRoot is null)
            return ItemUpdateType.None;

        // Try to read the shoko group id.
        if (collection.TryGetProviderId(ProviderNames.ShokoCollectionForGroup, out var collectionId) || collection.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForGroup, out collectionId))
            using (Plugin.Instance.Tracker.Enter($"Providing custom info for Collection \"{collection.Name}\". (Path=\"{collection.Path}\",Collection=\"{collectionId}\")"))
                if (await EnsureGroupCollectionIsCorrect(collectionRoot, collection, collectionId, cancellationToken).ConfigureAwait(false))
                    return ItemUpdateType.MetadataEdit;

        // Try to read the shoko series id.
        if (collection.TryGetProviderId(ProviderNames.ShokoCollectionForSeries, out var seasonId) || collection.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForSeries, out seasonId))
            using (Plugin.Instance.Tracker.Enter($"Providing custom info for Collection \"{collection.Name}\". (Path=\"{collection.Path}\",Season=\"{seasonId}\")"))
                if (await EnsureSeriesCollectionIsCorrect(collection, seasonId, cancellationToken).ConfigureAwait(false))
                    return ItemUpdateType.MetadataEdit;

        return ItemUpdateType.None;
    }

    private async Task<bool> EnsureSeriesCollectionIsCorrect(BoxSet collection, string seasonId, CancellationToken cancellationToken) {
        var seasonInfo = await _apiManager.GetSeasonInfo(seasonId).ConfigureAwait(false);
        if (seasonInfo is null)
            return false;

        var updated = EnsureNoTmdbIdIsSet(collection);
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
            _logger.LogDebug("Fixed collection {CollectionName} (Season={SeasonId})", collection.Name, seasonId);
        }

        return updated;
    }

    private async Task<bool> EnsureGroupCollectionIsCorrect(Folder collectionRoot, BoxSet collection, string collectionId, CancellationToken cancellationToken) {
        var collectionInfo = await _apiManager.GetCollectionInfo(collectionId).ConfigureAwait(false);
        if (collectionInfo is null)
            return false;

        var updated = EnsureNoTmdbIdIsSet(collection);
        var parent = collectionInfo.IsTopLevel ? collectionRoot : await GetCollectionByCollectionId(collectionRoot, collectionInfo.ParentId).ConfigureAwait(false);
        var (displayTitle, alternateTitle) = TextUtility.GetCollectionTitles(collectionInfo, collection.GetPreferredMetadataLanguage());
        displayTitle ??= collectionInfo.Title;
        if (collection.ParentId != parent.Id) {
            collection.SetParent(parent);
            updated = true;
        }
        if (!string.Equals(collection.Name, displayTitle)) {
            collection.Name = displayTitle;
            updated = true;
        }
        if (!string.Equals(collection.OriginalTitle, alternateTitle)) {
            collection.OriginalTitle = alternateTitle;
            updated = true;
        }
        if (updated) {
            await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Fixed collection {CollectionName} (Collection={CollectionId})", collection.Name, collectionId);
        }

        return updated;
    }

    private async Task<BoxSet> GetCollectionByCollectionId(Folder collectionRoot, string? collectionId) {
        if (string.IsNullOrEmpty(collectionId))
            throw new ArgumentNullException(nameof(collectionId));

        var collectionInfo = await _apiManager.GetCollectionInfo(collectionId).ConfigureAwait(false) ??
            throw new Exception($"Unable to find collection info for the parent collection with id \"{collectionId}\"");

        var collection = GetCollectionByPath(collectionRoot, collectionInfo);
        if (collection is not null)
            return collection;

        var list = _libraryManager.GetItemList(new() {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            HasAnyProviderId = new() { { ProviderNames.ShokoCollectionForGroup, collectionId } },
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

    private BoxSet? GetCollectionByPath(Folder collectionRoot, CollectionInfo collectionInfo) {
        var baseName = $"{collectionInfo.Title.ForceASCII()} [{ProviderNames.ShokoCollectionForGroup}={collectionInfo.Id}]";
        var folderName = BaseItem.FileSystem.GetValidFilename(baseName) + " [boxset]";
        var path = Path.Combine(collectionRoot.Path, folderName);
        return _libraryManager.FindByPath(path, true) as BoxSet;
    }

    private static bool EnsureNoTmdbIdIsSet(BoxSet collection) {
        var willRemove = collection.HasProviderId(MetadataProvider.TmdbCollection);
        collection.ProviderIds.Remove(MetadataProvider.TmdbCollection.ToString());
        return willRemove;
    }
}
