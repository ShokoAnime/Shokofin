using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Entities;
using Shokofin.Resolvers.Models;

namespace Shokofin.Web.Models;

public class VfsLibraryPreview(HashSet<string> filesBefore, HashSet<string> filesAfter, VirtualFolderInfo virtualFolder, LinkGenerationResult? result, string vfsPath)
{
    public string LibraryId = virtualFolder.ItemId;

    public string LibraryName { get; } = virtualFolder.Name;

    public string CollectionType { get; } = virtualFolder.CollectionType.ConvertToCollectionType()?.ToString() ?? "-";

    public string VfsRoot { get; } = Plugin.Instance.VirtualRoot;

    public bool IsSuccess = result is not null;

    public IReadOnlyList<string> FilesBeforeChanges { get; } = filesBefore
        .Select(path => path.Replace(vfsPath, string.Empty).Replace(Path.DirectorySeparatorChar, '/'))
        .OrderBy(path => path)
        .ToList();

    public IReadOnlyList<string> FilesAfterChanges { get; } = filesAfter
        .Select(path => path.Replace(vfsPath, string.Empty).Replace(Path.DirectorySeparatorChar, '/'))
        .OrderBy(path => path)
        .ToList();

    public VfsLibraryPreviewStats Stats { get; } = new(result);

    public class VfsLibraryPreviewStats(LinkGenerationResult? result)
    {
        public int Total { get; } = result?.Total ?? 0;

        public int Created { get; } = result?.Created ?? 0;

        public int Fixed { get; } = result?.Fixed ?? 0;

        public int Skipped { get; } = result?.Skipped ?? 0;

        public int Removed { get; } = result?.Removed ?? 0;

        public int TotalVideos { get; } = result?.TotalVideos ?? 0;

        public int CreatedVideos { get; } = result?.CreatedVideos ?? 0;

        public int FixedVideos { get; } = result?.FixedVideos ?? 0;

        public int SkippedVideos { get; } = result?.SkippedVideos ?? 0;

        public int RemovedVideos { get; } = result?.RemovedVideos ?? 0;

        public int TotalSubtitles { get; } = result?.TotalSubtitles ?? 0;

        public int CreatedSubtitles { get; } = result?.CreatedSubtitles ?? 0;

        public int FixedSubtitles { get; } = result?.FixedSubtitles ?? 0;

        public int SkippedSubtitles { get; } = result?.SkippedSubtitles ?? 0;

        public int RemovedSubtitles { get; } = result?.RemovedSubtitles ?? 0;

        public int RemovedNfos { get; } = result?.RemovedNfos ?? 0;
    }
}