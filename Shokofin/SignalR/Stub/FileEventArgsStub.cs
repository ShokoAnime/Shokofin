using System.Collections.Generic;
using System.Linq;
using Shokofin.SignalR.Interfaces;

using File = Shokofin.API.Models.File;

namespace Shokofin.SignalR.Models;

public class FileEventArgsStub : IFileEventArgs
{
    /// <inheritdoc/>
    public int FileId { get; private init; }

    /// <inheritdoc/>
    public int? FileLocationId { get; private init; }

    /// <inheritdoc/>
    public int ImportFolderId { get; private init; }

    /// <inheritdoc/>
    public string RelativePath { get; private init; }

    /// <inheritdoc/>
    public bool HasCrossReferences => true;

    /// <inheritdoc/>
    public List<IFileEventArgs.FileCrossReference> CrossReferences { get; private init; }

    public FileEventArgsStub(File.Location location, File file)
    {
        FileId = file.Id;
        ImportFolderId = location.ImportFolderId;
        RelativePath = location.RelativePath
            .Replace('/', System.IO.Path.DirectorySeparatorChar)
            .Replace('\\', System.IO.Path.DirectorySeparatorChar);
        if (RelativePath[0] != System.IO.Path.DirectorySeparatorChar)
            RelativePath = System.IO.Path.DirectorySeparatorChar + RelativePath;
        FileLocationId = location.Id;
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
