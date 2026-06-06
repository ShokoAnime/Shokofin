namespace Shokofin.Database;

public record PathChangeInfo(
    string OldPath,
    string NewPath,
    string CompositeKey,
    bool IsMovie,
    string ShowId,
    string NewShowId,
    string? EpisodeId,
    int Season,
    int Episode,
    string FileId,
    string FilenameSeriesId
);
