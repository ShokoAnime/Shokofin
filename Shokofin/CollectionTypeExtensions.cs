
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;

namespace Shokofin;

public static class CollectionTypeExtensions
{
    public static CollectionType? ConvertToCollectionType(this CollectionTypeOptions? collectionType)
        => collectionType switch
        {
            CollectionTypeOptions.movies => CollectionType.movies,
            CollectionTypeOptions.tvshows => CollectionType.tvshows,
            CollectionTypeOptions.music => CollectionType.music,
            CollectionTypeOptions.musicvideos => CollectionType.musicvideos,
            CollectionTypeOptions.homevideos => CollectionType.homevideos,
            CollectionTypeOptions.boxsets => CollectionType.boxsets,
            CollectionTypeOptions.books => CollectionType.books,
            null => null,
            _ => CollectionType.unknown,
        };
}