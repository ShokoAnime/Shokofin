using System.Collections.Generic;
using System.Linq;
using Shokofin.API.Models;
using Shokofin.Events.Interfaces;

namespace Shokofin.Events.Stub;

public class FileEventArgsStub : IFileEventArgs {
    /// <inheritdoc/>
    public int FileId { get; private init; }

    /// <inheritdoc/>
    public int? FileLocationId { get; private init; }

    /// <inheritdoc/>
    public int ManagedFolderId { get; private init; }

    /// <inheritdoc/>
    public string RelativePath { get; private init; }

    /// <inheritdoc/>
    public bool HasCrossReferences => true;

    /// <inheritdoc/>
    public List<IFileEventArgs.FileCrossReference> CrossReferences { get; private init; }

    public FileEventArgsStub(int fileId, int? fileLocationId, int managedFolderId, string relativePath, IEnumerable<IFileEventArgs.FileCrossReference> xrefs) {
        FileId = fileId;
        FileLocationId = fileLocationId;
        ManagedFolderId = managedFolderId;
        RelativePath = relativePath;
        CrossReferences = xrefs.ToList();
    }

    public FileEventArgsStub(File.Location location, File file) {
        FileId = file.Id;
        FileLocationId = location.Id;
        ManagedFolderId = location.ManagedFolderId;
        RelativePath = location.RelativePath;
        CrossReferences = file.CrossReferences
            .SelectMany(xref => xref.Episodes.Select(episodeXref => new IFileEventArgs.FileCrossReference() {
                AnidbEpisodeId = episodeXref.AniDB,
                AnidbAnimeId = xref.Series.AniDB,
                ShokoEpisodeId = episodeXref.Shoko,
                ShokoSeriesId = xref.Series.Shoko,
            }))
            .ToList();
    }
}
