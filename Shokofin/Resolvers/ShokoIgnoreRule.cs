using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.IO;

namespace Shokofin.Resolvers;
#pragma warning disable CS8766

public class ShokoIgnoreRule : IResolverIgnoreRule
{
    private readonly ShokoResolveManager ResolveManager;

    public ResolverPriority Priority => ResolverPriority.Plugin;

    public ShokoIgnoreRule(ShokoResolveManager resolveManager)
    {
        ResolveManager = resolveManager;
    }

    public bool ShouldIgnore(FileSystemMetadata fileInfo, BaseItem parent)
        => ResolveManager.ShouldFilterItem(parent as Folder, fileInfo)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
}
