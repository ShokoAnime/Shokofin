using System.Collections.Generic;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.IO;

namespace Shokofin.Resolvers;
#pragma warning disable CS8766

public class ShokoResolver : IItemResolver, IMultiItemResolver
{
    private readonly ShokoResolveManager ResolveManager;

    public ResolverPriority Priority => ResolverPriority.Plugin;

    public ShokoResolver(ShokoResolveManager resolveManager)
    {
        ResolveManager = resolveManager;
    }

    public BaseItem? ResolvePath(ItemResolveArgs args)
        => ResolveManager.ResolveSingle(args.Parent, args.CollectionType, args.FileInfo)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

    public MultiItemResolverResult? ResolveMultiple(Folder parent, List<FileSystemMetadata> files, CollectionType? collectionType, IDirectoryService directoryService)
        => ResolveManager.ResolveMultiple(parent, collectionType, files)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
}
