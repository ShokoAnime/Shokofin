using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration;
using Shokofin.ExternalIds;
using Shokofin.Providers;
using Shokofin.Utils;
using ImageType = MediaBrowser.Model.Entities.ImageType;

namespace Shokofin.Events;

public class MetadataRefreshService {
    private BoxSetProvider? _boxSetProvider = null;

    private CustomBoxSetProvider? _customBoxSetProvider = null;

    private MovieProvider? _movieProvider = null;

    private CustomMovieProvider? _customMovieProvider = null;

    private SeriesProvider? _seriesProvider = null;

    private CustomSeriesProvider? _customSeriesProvider = null;

    private SeasonProvider? _seasonProvider = null;

    private CustomSeasonProvider? _customSeasonProvider = null;

    private EpisodeProvider? _episodeProvider = null;

    private CustomEpisodeProvider? _customEpisodeProvider = null;

    private TrailerProvider? _trailerProvider = null;

    private VideoProvider? _videoProvider = null;

    private readonly ConcurrentDictionary<Guid, object> _refreshedItems = new();

    private readonly ILogger<MetadataRefreshService> _logger;

    private readonly IServerApplicationHost _applicationHost;

    private readonly ILibraryManager _libraryManager;

    private readonly IDirectoryService _directoryService;

    private readonly ShokoIdLookup _lookup;

    private readonly UsageTracker _usageTracker;

    public MetadataRefreshService(
        ILogger<MetadataRefreshService> logger,
        IServerApplicationHost applicationHost,
        ILibraryManager libraryManager,
        IDirectoryService directoryService,
        ShokoIdLookup lookup,
        UsageTracker usageTracker
    ) {
        _logger = logger;
        _applicationHost = applicationHost;
        _libraryManager = libraryManager;
        _directoryService = directoryService;
        _lookup = lookup;
        _usageTracker = usageTracker;

        _usageTracker.Stalled += OnStalled;
    }

    ~MetadataRefreshService() {
        _usageTracker.Stalled -= OnStalled;
    }

    private void OnStalled(object? sender, EventArgs eventArgs) {
        _refreshedItems.Clear();
    }

    public async Task<bool> RefreshCollection(BoxSet boxSet, MetadataRefreshField? refreshFields = null, CancellationToken cancellationToken = default) {
        if (!_refreshedItems.TryAdd(boxSet.Id, new()))
            return false;

        if (!_lookup.IsEnabledForItem(boxSet))
            return await LegacyRefreshMetadata(boxSet).ConfigureAwait(false);

        var updated = false;
        refreshFields ??= Plugin.Instance.Configuration.MetadataRefresh.Collection;
        if (refreshFields.Value.HasFlag(MetadataRefreshField.LegacyRefresh))
            updated = await LegacyRefreshMetadata(boxSet, refreshFields.Value.HasFlag(MetadataRefreshField.Images)).ConfigureAwait(false);

        if (refreshFields is not MetadataRefreshField.None and not MetadataRefreshField.LegacyRefresh and not (MetadataRefreshField.LegacyRefresh & MetadataRefreshField.Images)) {
            _boxSetProvider ??= _applicationHost.GetExports<BoxSetProvider>().First();
            var metadataResult = await _boxSetProvider.GetMetadata(new() {
                Name = boxSet.Name,
                Path = boxSet.Path,
                MetadataLanguage = boxSet.GetPreferredMetadataLanguage(),
                MetadataCountryCode = boxSet.GetPreferredMetadataCountryCode(),
                IsAutomated = true,
                ProviderIds = boxSet.ProviderIds.ToDictionary(),
            }, cancellationToken).ConfigureAwait(false);
            if (metadataResult is not { HasMetadata: true, Item: { } metadata })
                return updated;

            if (refreshFields.Value.HasFlag(MetadataRefreshField.OwnedItems)) {
                var extras = boxSet.ExtraIds
                    .Select(extraId => _libraryManager.GetItemById<Video>(extraId)!)
                    .Where(i => i is not null)
                    .ToArray();
                foreach (var extra in extras)
                    updated = await RefreshVideo(extra, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
            }

            _customBoxSetProvider ??= _applicationHost.GetExports<CustomBoxSetProvider>().First();
            updated = await RefreshBaseItem(boxSet, metadata, metadataResult, refreshFields.Value, _customBoxSetProvider, cancellationToken).ConfigureAwait(false) || updated;
        }

        return updated;
    }

    public async Task<bool> RefreshMovie(Movie movie, MetadataRefreshField? refreshFields = null, CancellationToken cancellationToken = default) {
        if (!_refreshedItems.TryAdd(movie.Id, new()))
            return false;

        if (!_lookup.IsEnabledForItem(movie))
            return await LegacyRefreshMetadata(movie).ConfigureAwait(false);

        var updated = false;
        refreshFields ??= Plugin.Instance.Configuration.MetadataRefresh.Movie;
        if (refreshFields.Value.HasFlag(MetadataRefreshField.LegacyRefresh))
            updated = await LegacyRefreshMetadata(movie, refreshFields.Value.HasFlag(MetadataRefreshField.Images)).ConfigureAwait(false) || updated;

        if (refreshFields is not MetadataRefreshField.None and not MetadataRefreshField.LegacyRefresh and not (MetadataRefreshField.LegacyRefresh & MetadataRefreshField.Images)) {
            _movieProvider ??= _applicationHost.GetExports<MovieProvider>().First();
            var metadataResult = await _movieProvider.GetMetadata(new() {
                Path = movie.Path,
                Name = movie.Name,
                MetadataLanguage = movie.GetPreferredMetadataLanguage(),
                MetadataCountryCode = movie.GetPreferredMetadataCountryCode(),
                IsAutomated = true,
            }, cancellationToken).ConfigureAwait(false);
            if (metadataResult is not { HasMetadata: true, Item: { } metadata })
                return updated;

            if (refreshFields.Value.HasFlag(MetadataRefreshField.OwnedItems)) {
                var extras = movie.ExtraIds
                    .Select(extraId => _libraryManager.GetItemById<Video>(extraId)!)
                    .Where(i => i is not null)
                    .ToArray();
                foreach (var extra in extras)
                    updated = await RefreshVideo(extra, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
            }

            _customMovieProvider ??= _applicationHost.GetExports<CustomMovieProvider>().First();
            updated = await RefreshBaseItem(movie, metadata, metadataResult, refreshFields.Value, _customMovieProvider, cancellationToken).ConfigureAwait(false) || updated;
            if (movie.LinkedAlternateVersions.Length > 0) {
                foreach (var part in movie.LinkedAlternateVersions) {
                    if (_libraryManager.FindByPath(part.Path, isFolder: false) is not Video video)
                        continue;

                    updated = await RefreshVideo(video, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
                }
            }
            if (movie.LocalAlternateVersions.Length > 0) {
                foreach (var part in movie.LocalAlternateVersions) {
                    if (_libraryManager.FindByPath(part, isFolder: false) is not Video video)
                        continue;

                    updated = await RefreshVideo(video, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
                }
            }
        }

        return updated;
    }

    public async Task<bool> RefreshSeries(Series series, MetadataRefreshField? refreshFields = null, CancellationToken cancellationToken = default) {
        if (!_refreshedItems.TryAdd(series.Id, new()))
            return false;

        if (!_lookup.IsEnabledForItem(series))
            return await LegacyRefreshMetadata(series).ConfigureAwait(false);

        var updated = false;
        refreshFields ??= Plugin.Instance.Configuration.MetadataRefresh.Series;
        if (!_lookup.IsEnabledForItem(series) || refreshFields.Value.HasFlag(MetadataRefreshField.LegacyRefresh))
            updated = await LegacyRefreshMetadata(series, refreshFields.Value.HasFlag(MetadataRefreshField.Images)).ConfigureAwait(false);

        if (refreshFields is not MetadataRefreshField.None and not MetadataRefreshField.LegacyRefresh and not (MetadataRefreshField.LegacyRefresh & MetadataRefreshField.Images)) {
            _seriesProvider ??= _applicationHost.GetExports<SeriesProvider>().First();
            var metadataResult = await _seriesProvider.GetMetadata(new() {
                Path = series.Path,
                Name = series.Name,
                MetadataLanguage = series.GetPreferredMetadataLanguage(),
                MetadataCountryCode = series.GetPreferredMetadataCountryCode(),
                IsAutomated = true,
            }, cancellationToken).ConfigureAwait(false);
            if (metadataResult is not { HasMetadata: true, Item: { } metadata })
                return updated;

            _customSeriesProvider ??= _applicationHost.GetExports<CustomSeriesProvider>().First();
            updated = await RefreshBaseItem(series, metadata, metadataResult, refreshFields.Value, _customSeriesProvider, cancellationToken).ConfigureAwait(false) || updated;

            if (refreshFields.Value.HasFlag(MetadataRefreshField.OwnedItems)) {
                var extras = series.ExtraIds
                    .Select(extraId => _libraryManager.GetItemById<Video>(extraId)!)
                    .Where(i => i is not null)
                    .ToArray();
                foreach (var extra in extras)
                    updated = await RefreshVideo(extra, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
            }

            if (refreshFields.Value.HasFlag(MetadataRefreshField.Recursive)) {
                foreach (var season in series.Children.OfType<Season>())
                    updated = await RefreshSeason(season, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
            }
        }

        return updated;
    }

    public async Task<bool> RefreshSeason(Season season, MetadataRefreshField? refreshFields = null, CancellationToken cancellationToken = default) {
        if (!_refreshedItems.TryAdd(season.Id, new()))
            return false;

        if (!_lookup.IsEnabledForItem(season))
            return await LegacyRefreshMetadata(season).ConfigureAwait(false);

        var updated = false;
        refreshFields ??= Plugin.Instance.Configuration.MetadataRefresh.Season;
        if (!_lookup.IsEnabledForItem(season) || refreshFields.Value.HasFlag(MetadataRefreshField.LegacyRefresh))
            updated = await LegacyRefreshMetadata(season, refreshFields.Value.HasFlag(MetadataRefreshField.Images)).ConfigureAwait(false);

        if (refreshFields is not MetadataRefreshField.None and not MetadataRefreshField.LegacyRefresh and not (MetadataRefreshField.LegacyRefresh & MetadataRefreshField.Images)) {
            if (season.Series is not { } series)
                return updated;

            _seasonProvider ??= _applicationHost.GetExports<SeasonProvider>().First();
            var metadataResult = await _seasonProvider.GetMetadata(new() {
                Path = season.Path,
                Name = season.Name,
                IndexNumber = season.IndexNumber,
                MetadataLanguage = season.GetPreferredMetadataLanguage(),
                MetadataCountryCode = season.GetPreferredMetadataCountryCode(),
                SeriesProviderIds = series.ProviderIds.ToDictionary(),
                IsAutomated = true,
            }, cancellationToken).ConfigureAwait(false);
            if (metadataResult is not { HasMetadata: true, Item: { } metadata })
                return updated;

            _customSeasonProvider ??= _applicationHost.GetExports<CustomSeasonProvider>().First();
            updated = await RefreshBaseItem(season, metadata, metadataResult, refreshFields.Value, _customSeasonProvider, cancellationToken).ConfigureAwait(false) || updated;

            if (refreshFields.Value.HasFlag(MetadataRefreshField.OwnedItems)) {
                var extras = season.ExtraIds
                    .Select(extraId => _libraryManager.GetItemById<Video>(extraId)!)
                    .Where(i => i is not null)
                    .ToArray();
                foreach (var extra in extras)
                    updated = await RefreshVideo(extra, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
            }

            if (refreshFields.Value.HasFlag(MetadataRefreshField.Recursive)) {
                foreach (var episode in season.Children.OfType<Episode>())
                    updated = await RefreshEpisode(episode, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
            }
        }

        return updated;
    }

    public async Task<bool> RefreshEpisode(Episode episode, MetadataRefreshField? refreshFields = null, CancellationToken cancellationToken = default) {
        if (!_refreshedItems.TryAdd(episode.Id, new()))
            return false;

        if (!_lookup.IsEnabledForItem(episode))
            return await LegacyRefreshMetadata(episode).ConfigureAwait(false);

        var updated = false;
        refreshFields ??= Plugin.Instance.Configuration.MetadataRefresh.Episode;
        if (!_lookup.IsEnabledForItem(episode) || refreshFields.Value.HasFlag(MetadataRefreshField.LegacyRefresh))
            updated = await LegacyRefreshMetadata(episode, refreshFields.Value.HasFlag(MetadataRefreshField.Images)).ConfigureAwait(false);

        if (refreshFields is not MetadataRefreshField.None and not MetadataRefreshField.LegacyRefresh and not (MetadataRefreshField.LegacyRefresh & MetadataRefreshField.Images)) {
            _episodeProvider ??= _applicationHost.GetExports<EpisodeProvider>().First();
            var metadataResult = await _episodeProvider.GetMetadata(new() {
                Path = episode.Path,
                Name = episode.Name,
                MetadataLanguage = episode.GetPreferredMetadataLanguage(),
                MetadataCountryCode = episode.GetPreferredMetadataCountryCode(),
                IsMissingEpisode = episode.IsMissingEpisode,
                IsAutomated = true,
            }, cancellationToken).ConfigureAwait(false);
            if (metadataResult is not { HasMetadata: true, Item: { } metadata })
                return updated;

            _customEpisodeProvider ??= _applicationHost.GetExports<CustomEpisodeProvider>().First();
            updated = await RefreshBaseItem(episode, metadata, metadataResult, refreshFields.Value, _customEpisodeProvider, cancellationToken).ConfigureAwait(false) || updated;
            if (episode.AdditionalParts.Length > 0) {
                foreach (var part in episode.AdditionalParts) {
                    if (_libraryManager.FindByPath(part, isFolder: false) is not Video video)
                        continue;

                    updated = await RefreshVideo(video, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
                }
            }

            if (refreshFields.Value.HasFlag(MetadataRefreshField.OwnedItems)) {
                var extras = episode.ExtraIds
                    .Select(extraId => _libraryManager.GetItemById<Video>(extraId)!)
                    .Where(i => i is not null)
                    .ToArray();
                foreach (var extra in extras)
                    updated = await RefreshVideo(extra, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
            }

            if (episode.LinkedAlternateVersions.Length > 0) {
                foreach (var part in episode.LinkedAlternateVersions) {
                    if (_libraryManager.FindByPath(part.Path, isFolder: false) is not Video video)
                        continue;

                    updated = await RefreshVideo(video, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
                }
            }

            if (episode.LocalAlternateVersions.Length > 0) {
                foreach (var part in episode.LocalAlternateVersions) {
                    if (_libraryManager.FindByPath(part, isFolder: false) is not Video video)
                        continue;

                    updated = await RefreshVideo(video, refreshFields.Value, cancellationToken).ConfigureAwait(false) || updated;
                }
            }
        }

        return updated;
    }

    public async Task<bool> RefreshVideo(Video video, MetadataRefreshField? refreshFields = null, CancellationToken cancellationToken = default) {
        if (!_refreshedItems.TryAdd(video.Id, new()))
            return false;

        if (!_lookup.IsEnabledForItem(video))
            return await LegacyRefreshMetadata(video).ConfigureAwait(false);

        var updated = false;
        refreshFields ??= Plugin.Instance.Configuration.MetadataRefresh.Video;
        if (!_lookup.IsEnabledForItem(video) || refreshFields.Value.HasFlag(MetadataRefreshField.LegacyRefresh))
            return await LegacyRefreshMetadata(video, refreshFields.Value.HasFlag(MetadataRefreshField.Images)).ConfigureAwait(false);

        if (refreshFields is not MetadataRefreshField.None and not MetadataRefreshField.LegacyRefresh and not (MetadataRefreshField.LegacyRefresh & MetadataRefreshField.Images)) {
            if (video is Trailer trailer) {
                _trailerProvider ??= _applicationHost.GetExports<TrailerProvider>().First();
                var metadataResult = await _trailerProvider.GetMetadata(new() {
                    Path = trailer.Path,
                    MetadataLanguage = trailer.GetPreferredMetadataLanguage(),
                    MetadataCountryCode = trailer.GetPreferredMetadataCountryCode(),
                    IsAutomated = true,
                }, cancellationToken).ConfigureAwait(false);
                if (metadataResult is not { HasMetadata: true, Item: { } metadata })
                    return updated;

                updated = await RefreshBaseItem(trailer, metadata, metadataResult, refreshFields.Value, cancellationToken: cancellationToken).ConfigureAwait(false) || updated;
            }
            else {
                _videoProvider ??= _applicationHost.GetExports<VideoProvider>().First();
                var metadataResult = await _videoProvider.GetMetadata(new() {
                    Path = video.Path,
                    MetadataLanguage = video.GetPreferredMetadataLanguage(),
                    MetadataCountryCode = video.GetPreferredMetadataCountryCode(),
                    IsAutomated = true,
                }, cancellationToken);
                if (metadataResult is not { HasMetadata: true, Item: { } metadata })
                    return updated;

                updated = await RefreshBaseItem(video, metadata, metadataResult, refreshFields.Value, cancellationToken: cancellationToken).ConfigureAwait(false) || updated;
            }
        }

        return updated;
    }

    private async Task<bool> RefreshBaseItem<T>(
        T item,
        T metadata,
        MetadataResult<T> metadataResult,
        MetadataRefreshField refreshFields,
        ICustomMetadataProvider<T>? customMetadataProvider = null,
        CancellationToken cancellationToken = default
    ) where T : BaseItem {
        var updatedFields = new List<string>();
        if (refreshFields.HasFlag(MetadataRefreshField.TitlesAndOverview)) {
            if (!item.LockedFields.Contains(MetadataField.Name) && !string.Equals(metadata.Name, item.Name, StringComparison.Ordinal)) {
                item.Name = metadata.Name;
                updatedFields.Add(nameof(BaseItem.Name));
            }

            if (!string.Equals(metadata.OriginalTitle, item.OriginalTitle, StringComparison.Ordinal)) {
                item.OriginalTitle = metadata.OriginalTitle;
                updatedFields.Add(nameof(BaseItem.OriginalTitle));
            }

            if (!item.LockedFields.Contains(MetadataField.Overview) && !string.Equals(metadata.Overview, item.Overview, StringComparison.Ordinal)) {
                item.Overview = metadata.Overview;
                updatedFields.Add(nameof(BaseItem.Overview));
            }
        }

        if (refreshFields.HasFlag(MetadataRefreshField.Dates)) {
            if (item.PremiereDate != metadata.PremiereDate) {
                item.PremiereDate = metadata.PremiereDate;
                updatedFields.Add(nameof(BaseItem.PremiereDate));
            }

            if (item.EndDate != metadata.EndDate) {
                item.EndDate = metadata.EndDate;
                updatedFields.Add(nameof(BaseItem.EndDate));
            }

            if (item.ProductionYear != metadata.ProductionYear) {
                item.ProductionYear = metadata.ProductionYear;
                updatedFields.Add(nameof(BaseItem.ProductionYear));
            }

            if (!item.LockedFields.Contains(MetadataField.Runtime) && metadata is Video { RunTimeTicks: > 0 } && item.RunTimeTicks != metadata.RunTimeTicks) {
                item.RunTimeTicks = metadata.RunTimeTicks;
                updatedFields.Add(nameof(BaseItem.RunTimeTicks));
            }
        }

        if (refreshFields.HasFlag(MetadataRefreshField.TagsAndGenres)) {
            if (!item.LockedFields.Contains(MetadataField.Tags) && ((item.Tags == null && metadata.Tags != null) || (item.Tags != null && metadata.Tags != null && !item.Tags.SequenceEqual(metadata.Tags)))) {
                item.Tags = metadata.Tags;
                updatedFields.Add(nameof(BaseItem.Tags));
            }

            if (!item.LockedFields.Contains(MetadataField.Genres) && ((item.Genres == null && metadata.Genres != null) || (item.Genres != null && metadata.Genres != null && !item.Genres.SequenceEqual(metadata.Genres)))) {
                item.Genres = metadata.Genres;
                updatedFields.Add(nameof(BaseItem.Genres));
            }
        }

        if (refreshFields.HasFlag(MetadataRefreshField.StudiosAndProductionLocations)) {
            if (!item.LockedFields.Contains(MetadataField.Studios) && ((item.Studios == null && metadata.Studios != null) || (item.Studios != null && metadata.Studios != null && !item.Studios.SequenceEqual(metadata.Studios)))) {
                item.Studios = metadata.Studios;
                updatedFields.Add(nameof(BaseItem.Studios));
            }

            if (!item.LockedFields.Contains(MetadataField.ProductionLocations) && (item.ProductionLocations == null && metadata.ProductionLocations != null || item.ProductionLocations != null && metadata.ProductionLocations != null && !item.ProductionLocations.SequenceEqual(metadata.ProductionLocations))) {
                item.ProductionLocations = metadata.ProductionLocations;
                updatedFields.Add(nameof(BaseItem.ProductionLocations));
            }
        }

        if (refreshFields.HasFlag(MetadataRefreshField.ContentRatings)) {
            if (!item.LockedFields.Contains(MetadataField.OfficialRating) && !string.Equals(metadata.OfficialRating, item.OfficialRating, StringComparison.Ordinal)) {
                item.OfficialRating = metadata.OfficialRating;
                updatedFields.Add(nameof(BaseItem.OfficialRating));
            }

            if ((item.CommunityRating == null && metadata.CommunityRating != null) || (item.CommunityRating != null && metadata.CommunityRating != null && item.CommunityRating != metadata.CommunityRating)) {
                item.CommunityRating = metadata.CommunityRating;
                updatedFields.Add(nameof(BaseItem.CommunityRating));
            }

            if (!string.Equals(metadata.CustomRating, item.CustomRating, StringComparison.Ordinal)) {
                item.CustomRating = metadata.CustomRating;
                updatedFields.Add(nameof(BaseItem.CustomRating));
            }
        }

        if (refreshFields.HasFlag(MetadataRefreshField.Images) || refreshFields.HasFlag(MetadataRefreshField.PreferredImages)) {
            if (refreshFields.HasFlag(MetadataRefreshField.Images)) {
                // TODO: Maybe switch from "legacy" refreshing of images to a custom method only using the Shoko provider?
                if (await LegacyRefreshImages(item).ConfigureAwait(false)) {
                    updatedFields.Add(nameof(item.ImageInfos));
                }
            }

            if (refreshFields.HasFlag(MetadataRefreshField.PreferredImages)) {
                // TODO: Reorder the images so our preferred image is placed first.
                // As for how. Idk. We don't have any anchors to attach to and use. Since the image infos are local file
                // system paths, while our preferred image is an id / remote url. We would need to maybe get the size of the preferred image
                // and compare that against the on-disk images to determine which is the preferred image, or similar. And if it's not in
                // the list, save it locally and add it to the list, the reorder it to appear first.
            }
        }

        if (refreshFields.HasFlag(MetadataRefreshField.CustomProvider) && customMetadataProvider is not null) {
            var updatedItemType = await customMetadataProvider.FetchAsync(
                item,
                new(_directoryService) { MetadataRefreshMode = MetadataRefreshMode.FullRefresh },
                cancellationToken
            ).ConfigureAwait(false);
            if (updatedItemType is not ItemUpdateType.None) {
                updatedFields.Add(nameof(MetadataRefreshField.CustomProvider));
            }
        }

        if (
            metadata.SupportsPeople &&
            refreshFields.HasFlag(MetadataRefreshField.CastAndCrew) &&
            !item.LockedFields.Contains(MetadataField.Cast) &&
            metadataResult.People.Count > 0
        ) {
            await _libraryManager.UpdatePeopleAsync(item, metadataResult.People, cancellationToken).ConfigureAwait(false);
            updatedFields.Add(nameof(MetadataRefreshField.CastAndCrew));
        }

        if (updatedFields.Count > 0) {
            item.DateLastRefreshed = DateTime.UtcNow;

#pragma warning disable CA2254 // Template should be a static expression
            _logger.LogDebug($"Updating fields for {item.GetBaseItemKind()} {{ItemName}} (Id={{Guid}},UpdatedFields={{UpdatedFieldList}})", item.Name, item.Id, updatedFields);

            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug($"Updated fields for {item.GetBaseItemKind()} {{ItemName}} (Id={{Guid}},UpdatedFields={{UpdatedFieldList}})", item.Name, item.Id, updatedFields);
#pragma warning restore CA2254 // Template should be a static expression
        }

        return updatedFields.Count > 0;
    }

    private async Task<bool> LegacyRefreshMetadata(BaseItem item, bool updateImages = false)
    {
        var updateType = await item.RefreshMetadata(new(_directoryService) {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = updateImages ? MetadataRefreshMode.FullRefresh : MetadataRefreshMode.None,
            ReplaceAllMetadata = true,
            ReplaceAllImages = updateImages,
            RemoveOldMetadata = true,
            ReplaceImages = updateImages ? Enum.GetValues<ImageType>().ToArray() : [],
            IsAutomated = true,
            EnableRemoteContentProbe = true,
        }, CancellationToken.None).ConfigureAwait(false);
        return updateType is not ItemUpdateType.None;
    }

    private async Task<bool> LegacyRefreshImages(BaseItem item) {
        var updateType = await item.RefreshMetadata(new(_directoryService) {
            MetadataRefreshMode = MetadataRefreshMode.None,
            ImageRefreshMode = MetadataRefreshMode.FullRefresh,
            ReplaceAllMetadata = false,
            ReplaceAllImages = true,
            RemoveOldMetadata = true,
            ReplaceImages = Enum.GetValues<ImageType>().ToArray(),
            IsAutomated = true,
            EnableRemoteContentProbe = true,
        }, CancellationToken.None).ConfigureAwait(false);
        return updateType is not ItemUpdateType.None;
    }

    public async Task AutoRefresh(IProgress<double>? progress = null, CancellationToken cancellationToken = default) {
        // Get all movies, series, seasons, and episodes and refresh each one based on the configuration's fields per item
        var config = Plugin.Instance.Configuration.MetadataRefresh;
        var movieList = GetMovies(config);
        var episodeList = GetEpisodes(config);
        var seasonList = episodeList
            .DistinctBy(ep => ep.SeasonId)
            .Select(ep => ep.Season)
            .Where(season => season is not null)
            .ToList();
        var seriesList = episodeList
            .DistinctBy(ep => ep.SeriesId)
            .Select(ep => ep.Series)
            .Where(series => series is not null)
            .ToList();

        var movieConfig = config.Movie & ~MetadataRefreshField.Recursive;
        foreach (var movie in movieList) {
            await RefreshMovie(movie, movieConfig, cancellationToken).ConfigureAwait(false);
        }

        var seriesConfig = config.Series & ~MetadataRefreshField.Recursive;
        foreach (var series in seriesList) {
            await RefreshSeries(series, seriesConfig, cancellationToken).ConfigureAwait(false);
        }

        var seasonConfig = config.Season & ~MetadataRefreshField.Recursive;
        foreach (var season in seasonList) {
            await RefreshSeason(season, seasonConfig, cancellationToken).ConfigureAwait(false);
        }

        var episodeConfig = config.Episode & ~MetadataRefreshField.Recursive;
        foreach (var episode in episodeList) {
            await RefreshEpisode(episode, episodeConfig, cancellationToken).ConfigureAwait(false);
        }
    }

    private List<Movie> GetMovies(MetadataRefreshConfiguration config)
        => _libraryManager.GetItemList(new() {
            IncludeItemTypes = [BaseItemKind.Movie],
            SourceTypes = [SourceType.Library],
            HasAnyProviderId = new() { { ProviderNames.ShokoFile, string.Empty } },
            IsVirtualItem = config.UpdateUnaired ? null : false,
            Recursive = true,
        })
            .Where(FilterBaseItem(config))
            .Cast<Movie>()
            .ToList();

    private List<Episode> GetEpisodes(MetadataRefreshConfiguration config)
        => _libraryManager.GetItemList(new() {
            IncludeItemTypes = [BaseItemKind.Episode],
            SourceTypes = [SourceType.Library],
            HasAnyProviderId = config.UpdateUnaired ? new() { { ShokoInternalId.Name, string.Empty } } : new() { { ProviderNames.ShokoFile, string.Empty } },
            IsVirtualItem = config.UpdateUnaired ? null : false,
            Recursive = true,
        })
            .Where(FilterBaseItem(config))
            .Cast<Episode>()
            .ToList();

    private Func<BaseItem, bool> FilterBaseItem(MetadataRefreshConfiguration config)
    {
        var updateUnaired = config.UpdateUnaired;
        var upperThreshold = config.UpdateUnaired ? (DateTime?)null : DateTime.UtcNow;
        var lowerThreshold = config.AutoRefreshRangeInDays > 0 ? DateTime.UtcNow.AddDays(-config.AutoRefreshRangeInDays) : (DateTime?)null;
        var minAge = config.AntiRefreshDeadZoneInHours > 0 ? DateTime.UtcNow.AddHours(-config.AntiRefreshDeadZoneInHours) : (DateTime?)null;
        var outOfSync = config.OutOfSyncInDays > 0 ? DateTime.UtcNow.AddDays(-config.OutOfSyncInDays) : (DateTime?)null;
        if (outOfSync.HasValue && minAge.HasValue && outOfSync < minAge) {
            minAge = null;
        }

        return item => {
            if (minAge is not null && item.DateLastRefreshed > minAge) {
                return false;
            }
            if (outOfSync is not null && item.DateLastRefreshed < outOfSync) {
                return !updateUnaired  && !item.IsVirtualItem;
            }

            return _lookup.IsEnabledForItem(item) && item.PremiereDate is { } premiereDate && premiereDate > lowerThreshold && (upperThreshold is null || premiereDate < upperThreshold);
        };
    }
}
