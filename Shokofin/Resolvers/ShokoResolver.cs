using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.IO;

#nullable enable
namespace Shokofin.Resolvers;
#pragma warning disable CS8766

public class ShokoResolver : IItemResolver, IMultiItemResolver, IResolverIgnoreRule
{
    private readonly ShokoResolveManager ResolveManager;

    public ResolverPriority Priority => ResolverPriority.Plugin;

    public ShokoResolver(ShokoResolveManager resolveManager)
    {
        ResolveManager = resolveManager;
    }

    public bool ShouldIgnore(FileSystemMetadata fileInfo, BaseItem parent)
        => ResolveManager.ShouldFilterItem(parent as Folder, fileInfo)
            .GetAwaiter()
            .GetResult();

    public BaseItem? ResolvePath(ItemResolveArgs args)
        => ResolveManager.ResolveSingle(args.Parent, args.CollectionType, args.FileInfo);

    public MultiItemResolverResult? ResolveMultiple(Folder parent, List<FileSystemMetadata> files, string collectionType, IDirectoryService directoryService)
        => ResolveManager.ResolveMultiple(parent, collectionType, files);
}
