using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Providers;

namespace Shokofin;

public interface IIdLookup
{
    #region Base Item

    /// <summary>
    /// Check if the plugin is enabled for <see cref="MediaBrowser.Controller.Entities.BaseItem" >the item</see>.
    /// </summary>
    /// <param name="item">The <see cref="MediaBrowser.Controller.Entities.BaseItem" /> to check.</param>
    /// <returns>True if the plugin is enabled for the <see cref="MediaBrowser.Controller.Entities.BaseItem" /></returns>
    bool IsEnabledForItem(BaseItem item);

    /// <summary>
    /// Check if the plugin is enabled for <see cref="MediaBrowser.Controller.Entities.BaseItem" >the item</see>.
    /// </summary>
    /// <param name="item">The <see cref="MediaBrowser.Controller.Entities.BaseItem" /> to check.</param>
    /// <param name="isSoleProvider">True if the plugin is the only metadata provider enabled for the item.</param>
    /// <returns>True if the plugin is enabled for the <see cref="MediaBrowser.Controller.Entities.BaseItem" /></returns>
    bool IsEnabledForItem(BaseItem item, out bool isSoleProvider);

    #endregion
    #region Series Id

    bool TryGetSeriesIdFor(string path, [NotNullWhen(true)] out string? seriesId);

    bool TryGetSeriesIdFromEpisodeId(string episodeId, [NotNullWhen(true)] out string? seriesId);

    /// <summary>
    /// Try to get the Shoko Series Id for the <see cref="MediaBrowser.Controller.Entities.TV.Series" />.
    /// </summary>
    /// <param name="series">The <see cref="MediaBrowser.Controller.Entities.TV.Series" /> to check for.</param>
    /// <param name="seriesId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="MediaBrowser.Controller.Entities.TV.Series" />.</returns>
    bool TryGetSeriesIdFor(Series series, [NotNullWhen(true)] out string? seriesId);

    /// <summary>
    /// Try to get the Shoko Series Id for the <see cref="MediaBrowser.Controller.Entities.TV.Season" />.
    /// </summary>
    /// <param name="season">The <see cref="MediaBrowser.Controller.Entities.TV.Season" /> to check for.</param>
    /// <param name="seriesId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="MediaBrowser.Controller.Entities.TV.Season" />.</returns>
    bool TryGetSeriesIdFor(Season season, [NotNullWhen(true)] out string? seriesId);

    /// <summary>
    /// Try to get the Shoko Series Id for the <see cref="MediaBrowser.Controller.Entities.TV.Season" />.
    /// </summary>
    /// <param name="season">The <see cref="MediaBrowser.Controller.Entities.TV.Season" /> to check for.</param>
    /// <param name="seriesId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="MediaBrowser.Controller.Entities.TV.Season" />.</returns>
    bool TryGetSeriesIdFor(Movie movie, [NotNullWhen(true)] out string? seriesId);

    #endregion
    #region Series Path

    bool TryGetPathForSeriesId(string seriesId, [NotNullWhen(true)] out string? path);

    #endregion
    #region Episode Id

    bool TryGetEpisodeIdFor(string path, [NotNullWhen(true)] out string? episodeId);

    bool TryGetEpisodeIdFor(BaseItem item, [NotNullWhen(true)] out string? episodeId);

    bool TryGetEpisodeIdsFor(string path, [NotNullWhen(true)] out List<string>? episodeIds);

    bool TryGetEpisodeIdsFor(BaseItem item, [NotNullWhen(true)] out List<string>? episodeIds);

    #endregion
    #region Episode Path

    bool TryGetPathForEpisodeId(string episodeId, [NotNullWhen(true)] out string? path);

    #endregion
    #region File Id

    bool TryGetFileIdFor(BaseItem item, [NotNullWhen(true)] out string? fileId);

    #endregion
}

public class IdLookup : IIdLookup
{
    private readonly ShokoAPIManager ApiManager;

    private readonly ILibraryManager LibraryManager;

    public IdLookup(ShokoAPIManager apiManager, ILibraryManager libraryManager)
    {
        ApiManager = apiManager;
        LibraryManager = libraryManager;
    }

    #region Base Item

    private readonly HashSet<string> AllowedTypes = new() { nameof(Series), nameof(Season), nameof(Episode), nameof(Movie) };

    public bool IsEnabledForItem(BaseItem item) =>
        IsEnabledForItem(item, out var _);

    public bool IsEnabledForItem(BaseItem item, out bool isSoleProvider)
    {
        var reItem = item switch {
            Series s => s,
            Season s => s.Series,
            Episode e => e.Series,
            _ => item,
        };
        if (reItem == null) {
            isSoleProvider = false;
            return false;
        }

        var libraryOptions = LibraryManager.GetLibraryOptions(reItem);
        if (libraryOptions == null) {
            isSoleProvider = false;
            return false;
        }

        var isEnabled = false;
        isSoleProvider = true;
        foreach (var options in libraryOptions.TypeOptions) {
            if (!AllowedTypes.Contains(options.Type))
                continue;
            var isEnabledForType = options.MetadataFetchers.Contains(Plugin.MetadataProviderName);
            if (isEnabledForType) {
                if (!isEnabled)
                    isEnabled = true;
                if (options.MetadataFetchers.Length > 1 && isSoleProvider)
                    isSoleProvider = false;
            }
        }
        return isEnabled;
    }

    #endregion
    #region Series Id

    public bool TryGetSeriesIdFor(string path, [NotNullWhen(true)] out string? seriesId)
    {
        if (ApiManager.TryGetSeriesIdForPath(path, out seriesId!))
            return true;

        seriesId = string.Empty;
        return false;
    }

    public bool TryGetSeriesIdFromEpisodeId(string episodeId, [NotNullWhen(true)] out string? seriesId)
    {
        if (ApiManager.TryGetSeriesIdForEpisodeId(episodeId, out seriesId!))
            return true;

        seriesId = string.Empty;
        return false;
    }

    public bool TryGetSeriesIdFor(Series series, [NotNullWhen(true)] out string? seriesId)
    {
        if (series.ProviderIds.TryGetValue(ShokoSeriesId.Name, out seriesId!) && !string.IsNullOrEmpty(seriesId)) {
            return true;
        }

        if (TryGetSeriesIdFor(series.Path, out seriesId)) {
            // Set the ShokoGroupId.Name and ShokoSeriesId.Name provider ids for the series, since it haven't been set again. It doesn't matter if it's not saved to the database, since we only need it while running the following code.
            if (ApiManager.TryGetDefaultSeriesIdForSeriesId(seriesId, out var defaultSeriesId)) {
                SeriesProvider.AddProviderIds(series, defaultSeriesId);
            }
            // Same as above, but only set the ShokoSeriesId.Name id.
            else {
                SeriesProvider.AddProviderIds(series, seriesId);
            }
            // Make sure the presentation unique is not cached, so we won't reuse the cache key.
            series.PresentationUniqueKey = null;
            return true;
        }

        return false;
    }

    public bool TryGetSeriesIdFor(Season season, [NotNullWhen(true)] out string? seriesId)
    {
        if (season.ProviderIds.TryGetValue(ShokoSeriesId.Name, out seriesId) && !string.IsNullOrEmpty(seriesId))
            return true;

        return TryGetSeriesIdFor(season.Path, out seriesId);
    }

    public bool TryGetSeriesIdFor(Movie movie, [NotNullWhen(true)] out string? seriesId)
    {
        if (movie.ProviderIds.TryGetValue(ShokoSeriesId.Name, out seriesId!) && !string.IsNullOrEmpty(seriesId))
            return true;

        if (TryGetEpisodeIdFor(movie.Path, out var episodeId) && TryGetSeriesIdFromEpisodeId(episodeId, out seriesId))
            return true;

        return false;
    }

    #endregion
    #region Series Path

    public bool TryGetPathForSeriesId(string seriesId, [NotNullWhen(true)] out string? path)
    {
        if (ApiManager.TryGetSeriesPathForId(seriesId, out path!))
            return true;

        path = string.Empty;
        return false;
    }

    #endregion
    #region Episode Id

    public bool TryGetEpisodeIdFor(string path, [NotNullWhen(true)] out string? episodeId)
    {
        if (ApiManager.TryGetEpisodeIdForPath(path, out episodeId!))
            return true;

        episodeId = string.Empty;
        return false;
    }

    public bool TryGetEpisodeIdFor(BaseItem item, [NotNullWhen(true)] out string? episodeId)
    {
        // This will account for virtual episodes and existing episodes
        if (item.ProviderIds.TryGetValue(ShokoEpisodeId.Name, out episodeId!) && !string.IsNullOrEmpty(episodeId)) {
            return true;
        }

        // This will account for new episodes that haven't received their first metadata update yet.
        if (TryGetEpisodeIdFor(item.Path, out episodeId)) {
            return true;
        }

        return false;
    }

    public bool TryGetEpisodeIdsFor(string path, [NotNullWhen(true)] out List<string>? episodeIds)
    {
        if (ApiManager.TryGetEpisodeIdsForPath(path, out episodeIds!))
            return true;

        episodeIds = new();
        return false;
    }

    public bool TryGetEpisodeIdsFor(BaseItem item, [NotNullWhen(true)] out List<string>? episodeIds)
    {
        // This will account for virtual episodes and existing episodes
        if (item.ProviderIds.TryGetValue(ShokoFileId.Name, out var fileId) && item.ProviderIds.TryGetValue(ShokoSeriesId.Name, out var seriesId) && ApiManager.TryGetEpisodeIdsForFileId(fileId, seriesId, out episodeIds!)) {
            return true;
        }

        // This will account for new episodes that haven't received their first metadata update yet.
        if (TryGetEpisodeIdsFor(item.Path, out episodeIds)) {
            return true;
        }

        return false;
    }

    #endregion
    #region Episode Path

    public bool TryGetPathForEpisodeId(string episodeId, [NotNullWhen(true)] out string? path)
    {
        if (ApiManager.TryGetEpisodePathForId(episodeId, out path!))
            return true;

        path = string.Empty;
        return false;
    }

    #endregion
    #region File Id

    public bool TryGetFileIdFor(BaseItem episode, [NotNullWhen(true)] out string? fileId)
    {
        if (episode.ProviderIds.TryGetValue(ShokoFileId.Name, out fileId!))
            return true;

        if (ApiManager.TryGetFileIdForPath(episode.Path, out fileId!))
            return true;

        fileId = string.Empty;
        return false;
    }

    #endregion
}