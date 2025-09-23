using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Providers;

namespace Shokofin.API;

/// <summary>
/// Looks up Shoko ids for items.
/// </summary>
public class ShokoIdLookup(ShokoApiManager _apiManager, ILibraryManager _libraryManager) {
    #region Base Item

    private static readonly HashSet<string> AllowedTypes = [nameof(Series), nameof(Season), nameof(Episode), nameof(Movie)];

    /// <summary>
    /// Check if the plugin is enabled for <see cref="BaseItem" >the item</see>.
    /// </summary>
    /// <param name="item">The <see cref="BaseItem" /> to check.</param>
    /// <returns>True if the plugin is enabled for the <see cref="BaseItem" /></returns>
    public bool IsEnabledForItem(BaseItem item) {
        var reItem = item switch {
            Series s => s,
            Season s => s.Series,
            Episode e => e.Series,
            _ => item,
        };
        if (reItem == null) {
            return false;
        }

        var libraryOptions = _libraryManager.GetLibraryOptions(reItem);
        if (libraryOptions == null) {
            return false;
        }

        return IsEnabledForLibraryOptions(libraryOptions);
    }

    /// <summary>
    /// Check if the plugin is enabled for <see cref="LibraryOptions" >the library options</see>.
    /// </summary>
    /// <param name="libraryOptions">The <see cref="LibraryOptions" /> to check.</param>
    /// <param name="isSoleProvider">True if the plugin is the only metadata provider enabled for the item.</param>
    /// <returns>True if the plugin is enabled for the <see cref="LibraryOptions" /></returns>
    public bool IsEnabledForLibraryOptions(LibraryOptions libraryOptions) {
        var isEnabled = false;
        foreach (var options in libraryOptions.TypeOptions) {
            if (!AllowedTypes.Contains(options.Type))
                continue;
            var isEnabledForType = options.MetadataFetchers.Contains(Plugin.MetadataProviderName);
            if (isEnabledForType) {
                if (!isEnabled)
                    isEnabled = true;
            }
        }
        return isEnabled;
    }

    #endregion

    #region Season Id

    /// <summary>
    /// Try to get the season id for the given path.
    /// </summary>
    /// <param name="path">The path to check for.</param>
    /// <param name="seasonId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="BaseItem" />.</returns>
    public bool TryGetSeasonIdFor(string path, [NotNullWhen(true)] out string? seasonId) {
        if (_apiManager.TryGetSeasonIdForPath(path, out seasonId))
            return true;

        seasonId = null;
        return false;
    }

    /// <summary>
    /// Try to get the season id from the given episode id.
    /// </summary>
    /// <param name="episodeId">The episode id to check for.</param>
    /// <param name="seasonId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="BaseItem" />.</returns>
    public bool TryGetSeasonIdFromEpisodeId(string episodeId, [NotNullWhen(true)] out string? seasonId) {
        if (_apiManager.TryGetSeasonIdForEpisodeId(episodeId, out seasonId))
            return true;

        seasonId = null;
        return false;
    }

    /// <summary>
    /// Try to get the main season id for the <see cref="Series" />.
    /// </summary>
    /// <param name="series">The <see cref="Series" /> to check for.</param>
    /// <param name="seasonId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="Series" />.</returns>
    public bool TryGetSeasonIdFor(Series series, [NotNullWhen(true)] out string? seasonId) {
        if (series.TryGetSeasonId(out seasonId))
            return true;

        if (TryGetSeasonIdFor(series.Path, out seasonId)) {
            if (_apiManager.TryGetShowIdForSeasonId(seasonId, out var mainSeasonId))
                series.SetProviderId(ShokoInternalId.Name, mainSeasonId);
            else
                series.SetProviderId(ShokoInternalId.Name, seasonId);
            // Make sure the presentation unique is not cached, so we won't reuse the cache key.
            // This is for series-merging in a non-VFS based library.
            series.PresentationUniqueKey = null;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Try to get the season id for the <see cref="Season" />.
    /// </summary>
    /// <param name="season">The <see cref="Season" /> to check for.</param>
    /// <param name="seasonId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="Season" />.</returns>
    public bool TryGetSeasonIdFor(Season season, [NotNullWhen(true)] out string? seasonId) {
        if (season.TryGetSeasonId(out seasonId))
            return true;

        return TryGetSeasonIdFor(season.Path, out seasonId);
    }

    /// <summary>
    /// Try to get the season id for the <see cref="Movie" />.
    /// </summary>
    /// <param name="season">The <see cref="Movie" /> to check for.</param>
    /// <param name="seasonId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="Movie" />.</returns>
    public bool TryGetSeasonIdFor(Movie movie, [NotNullWhen(true)] out string? seasonId) {
        if (TryGetSeasonIdFor(movie.Path, out var episodeId) && TryGetSeasonIdFromEpisodeId(episodeId, out seasonId))
            return true;

        seasonId = null;
        return false;
    }

    #endregion

    #region Episode Id

    /// <summary>
    /// Try to get the episode ids for the given path.
    /// </summary>
    /// <param name="path">The path to check for.</param>
    /// <param name="episodeIds">The variable to put the ids in.</param>
    /// <returns>True if it successfully retrieved the ids for the <see cref="BaseItem" />.</returns>
    public bool TryGetEpisodeIdsFor(string path, [NotNullWhen(true)] out List<string>? episodeIds) {
        if (_apiManager.TryGetEpisodeIdsForPath(path, out episodeIds))
            return true;

        episodeIds = null;
        return false;
    }

    /// <summary>
    /// Try to get the episode ids for the given <see cref="BaseItem" />.
    /// </summary>
    /// <param name="item">The <see cref="BaseItem" /> to check for.</param>
    /// <param name="episodeIds">The variable to put the ids in.</param>
    /// <returns>True if it successfully retrieved the ids for the <see cref="BaseItem" />.</returns>
    public bool TryGetEpisodeIdsFor(BaseItem item, [NotNullWhen(true)] out List<string>? episodeIds) {
        // This will account for existing episodes.
        if (item.TryGetFileAndSeriesId(out var fileId, out var seasonId) && _apiManager.TryGetEpisodeIdsForFileId(fileId, seasonId, out episodeIds))
            return true;

        // This will account for new episodes that haven't received their first metadata update yet.
        if (TryGetEpisodeIdsFor(item.Path, out episodeIds))
            return true;

        // This will account for "missing" episodes.
        if (item.TryGetEpisodeIds(out episodeIds))
            return true;

        episodeIds = null;
        return false;
    }

    #endregion

    #region File Id

    /// <summary>
    /// Try to get the file id for the given <see cref="BaseItem" />.
    /// </summary>
    /// <param name="video">The <see cref="BaseItem" /> to check for.</param>
    /// <param name="fileId">The variable to put the id in.</param>
    /// <param name="seriesId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="BaseItem" />.</returns>
    public bool TryGetFileAndSeriesIdFor(BaseItem video, [NotNullWhen(true)] out string? fileId, [NotNullWhen(true)] out string? seriesId) {
        if (video.TryGetFileAndSeriesId(out fileId, out seriesId))
            return true;

        if (_apiManager.TryGetFileAndSeriesIdForPath(video.Path, out fileId, out seriesId))
            return true;

        fileId = null;
        seriesId = null;
        return false;
    }

    #endregion
}
