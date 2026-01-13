using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using Shokofin.Configuration;

namespace Shokofin.Extensions;

public static class MediaFolderConfigurationExtensions {
    public static Folder GetFolderForPath(this string mediaFolderPath)
        => BaseItem.LibraryManager.FindByPath(mediaFolderPath, true) as Folder ??
            throw new Exception($"Unable to find folder by path \"{mediaFolderPath}\".");

    public static IReadOnlyList<(int managedFolderId, string managedFolderSubPath, IReadOnlyList<string> mediaFolderPaths)> ToManagedFolderList(this IEnumerable<MediaFolderConfiguration> mediaConfigs)
        => mediaConfigs
            .GroupBy(a => (a.ManagedFolderId, a.ManagedFolderRelativePath))
            .Select(g => (g.Key.ManagedFolderId, g.Key.ManagedFolderRelativePath, g.Select(a => a.Path).ToList() as IReadOnlyList<string>))
            .ToList();

    public static IReadOnlyList<(string managedFolderSubPath, bool vfsEnabled, IReadOnlyList<string> mediaFolderPaths)> ToManagedFolderList(this IEnumerable<MediaFolderConfiguration> mediaConfigs, int managedFolderId, string relativePath)
        => mediaConfigs
            .Where(a => a.ManagedFolderId == managedFolderId && a.IsEnabledForPath(relativePath))
            .GroupBy(a => (a.ManagedFolderId, a.ManagedFolderRelativePath, a.Library.IsVirtualFileSystemEnabled))
            .Select(g => (g.Key.ManagedFolderRelativePath, g.Key.IsVirtualFileSystemEnabled, g.Select(a => a.Path).ToList() as IReadOnlyList<string>))
            .ToList();
}
