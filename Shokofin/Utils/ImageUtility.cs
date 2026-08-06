using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Shokofin.API.Info;
using Shokofin.Configuration;

namespace Shokofin.Utils;

public static class ImageUtility {
    #region Episode

    public static async Task<IReadOnlyCollection<RemoteImageInfo>> GetEpisodeImages(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string? metadataLanguage, bool displayMode, CancellationToken cancellationToken) {
        var images = await episodeInfo.GetImages(cancellationToken);
        var originLanguages = TextUtility.GuessOriginLanguage(seasonInfo);
        var config = seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Image.AnidbEpisode.Enabled ? (
                Plugin.Instance.Configuration.Image.AnidbEpisode
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Image.TmdbEpisode.Enabled ? (
                Plugin.Instance.Configuration.Image.TmdbEpisode
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
            _ => Plugin.Instance.Configuration.Image.ShokoEpisode.Enabled ? (
                Plugin.Instance.Configuration.Image.ShokoEpisode
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
        };

        return [..ProcessEpisodeImages(images, metadataLanguage, originLanguages, displayMode, config).DistinctBy(image => image.Url)];
    }

    #endregion

    #region Season

    public static async Task<IReadOnlyCollection<RemoteImageInfo>> GetSeasonImages(SeasonInfo seasonInfo, string? metadataLanguage, bool displayMode, CancellationToken cancellationToken) {
        var images = await seasonInfo.GetImages(cancellationToken);
        var originLanguages = TextUtility.GuessOriginLanguage(seasonInfo);
        var config = seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Image.AnidbSeason.Enabled ? (
                Plugin.Instance.Configuration.Image.AnidbSeason
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Image.TmdbSeason.Enabled ? (
                Plugin.Instance.Configuration.Image.TmdbSeason
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
            _ => Plugin.Instance.Configuration.Image.ShokoSeason.Enabled ? (
                Plugin.Instance.Configuration.Image.ShokoSeason
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
        };

        return [..ProcessSeriesImages(images, metadataLanguage, originLanguages, displayMode, config).DistinctBy(image => image.Url)];
    }

    #endregion

    #region Show

    public static async Task<IReadOnlyCollection<RemoteImageInfo>> GetShowImages(ShowInfo showInfo, string? metadataLanguage, bool displayMode, CancellationToken cancellationToken) {
        var imagesList = new List<API.Models.Images> { await showInfo.GetImages(cancellationToken) };

        // Also attach any images linked to the "seasons" if it's not a standalone series.
        if (!showInfo.IsStandalone) {
            foreach (var seasonInfo in showInfo.SeasonList) {
                imagesList.Add(await seasonInfo.GetImages(cancellationToken));
            }
        }

        var images = CombineImages(imagesList);
        var originLanguages = TextUtility.GuessOriginLanguage(showInfo.DefaultSeason);
        var config = showInfo.DefaultSeason.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Image.AnidbAnime.Enabled ? (
                Plugin.Instance.Configuration.Image.AnidbAnime
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Image.TmdbShow.Enabled ? (
                Plugin.Instance.Configuration.Image.TmdbShow
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
            _ => Plugin.Instance.Configuration.Image.ShokoSeries.Enabled ? (
                Plugin.Instance.Configuration.Image.ShokoSeries
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
        };

        return [..ProcessSeriesImages(images, metadataLanguage, originLanguages, displayMode, config).DistinctBy(image => image.Url)];
    }

    private static API.Models.Images CombineImages(IEnumerable<API.Models.Images> imagesList) {
        var images = new API.Models.Images();

        var ignorePreferred = false;
        foreach (var otherImages in imagesList) {
            images.Posters.AddRange(otherImages.Posters.Select(image => image.IsPreferred && ignorePreferred ? new(image) { IsPreferred = false } : image));
            images.Backdrops.AddRange(otherImages.Backdrops.Select(image => image.IsPreferred && ignorePreferred ? new(image) { IsPreferred = false } : image));
            images.Banners.AddRange(otherImages.Banners.Select(image => image.IsPreferred && ignorePreferred ? new(image) { IsPreferred = false } : image));
            images.Logos.AddRange(otherImages.Logos.Select(image => image.IsPreferred && ignorePreferred ? new(image) { IsPreferred = false } : image));
            ignorePreferred = true;
        }

        return images;
    }

    #endregion

    #region Movie

    public static async Task<IReadOnlyCollection<RemoteImageInfo>> GetMovieImages(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string? metadataLanguage, bool displayMode, CancellationToken cancellationToken) {
        var images = await episodeInfo.GetImages(cancellationToken);
        var originLanguages = TextUtility.GuessOriginLanguage(seasonInfo);
        var config = seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Image.AnidbSeason.Enabled ? (
                Plugin.Instance.Configuration.Image.AnidbSeason
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Image.TmdbSeason.Enabled ? (
                Plugin.Instance.Configuration.Image.TmdbSeason
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
            _ => Plugin.Instance.Configuration.Image.ShokoSeason.Enabled ? (
                Plugin.Instance.Configuration.Image.ShokoSeason
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
        };

        return [..ProcessSeriesImages(images, metadataLanguage, originLanguages, displayMode, config).DistinctBy(image => image.Url)];
    }

    #endregion

    #region Collection

    public static async Task<IReadOnlyCollection<RemoteImageInfo>> GetCollectionImages(SeasonInfo seasonInfo, string? metadataLanguage, bool displayMode, CancellationToken cancellationToken) {
        var images = await seasonInfo.GetImages(cancellationToken);
        var originLanguages = TextUtility.GuessOriginLanguage(seasonInfo);
        var config = seasonInfo.StructureType switch {
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Image.TmdbCollection.Enabled ? (
                Plugin.Instance.Configuration.Image.TmdbCollection
            ) : (
                Plugin.Instance.Configuration.Image.Default
            ),
            _ => Plugin.Instance.Configuration.Image.ShokoCollection.Enabled ? (
                Plugin.Instance.Configuration.Image.ShokoCollection
            ) : (
                Plugin.Instance.Configuration.Image.Default
            )
        };

        return [..ProcessSeriesImages(images, metadataLanguage, originLanguages, displayMode, config).DistinctBy(image => image.Url)];
    }

    public static async Task<IReadOnlyCollection<RemoteImageInfo>> GetCollectionImages(ShowInfo showInfo, string? metadataLanguage, bool displayMode, CancellationToken cancellationToken) {
        var images = await showInfo.GetImages(cancellationToken);
        var originLanguages = TextUtility.GuessOriginLanguage(showInfo.DefaultSeason);
        var config =  Plugin.Instance.Configuration.Image.ShokoCollection.Enabled ? (
            Plugin.Instance.Configuration.Image.ShokoCollection
        ) : (
            Plugin.Instance.Configuration.Image.Default
        );

        return [..ProcessSeriesImages(images, metadataLanguage, originLanguages, displayMode, config).DistinctBy(image => image.Url)];
    }

    #endregion

    #region Process Images

    private const int Over9K = 9001;

    private static IEnumerable<RemoteImageInfo> ProcessEpisodeImages(API.Models.EpisodeImages images, string? metadataLanguage, string[] originLanguages, bool displayMode, ImageConfiguration config) {
        // Set to english if not set to match Jellyfin's internal logic.
        if (string.IsNullOrWhiteSpace(metadataLanguage))
            metadataLanguage = "en";

        var thumbnails = images.Thumbnails.Count > 0 ? images.Thumbnails : images.Backdrops;
        foreach (var image in ProcessImages(thumbnails, ImageType.Primary, metadataLanguage, originLanguages, displayMode, config, config.GetOrderedBackdropTypes()))
            yield return image;
    }

    private static IEnumerable<RemoteImageInfo> ProcessSeriesImages(API.Models.Images images, string? metadataLanguage, string[] originLanguages, bool displayMode, ImageConfiguration config) {
        // Set to english if not set to match Jellyfin's internal logic.
        if (string.IsNullOrWhiteSpace(metadataLanguage))
            metadataLanguage = "en";

        foreach (var image in ProcessImages(images.Posters, ImageType.Primary, metadataLanguage, originLanguages, displayMode, config, config.GetOrderedPosterTypes()))
            yield return image;
        foreach (var image in ProcessImages(images.Logos, ImageType.Logo, metadataLanguage, originLanguages, displayMode, config, config.GetOrderedLogoTypes()))
            yield return image;
        foreach (var image in ProcessImages(images.Banners, ImageType.Banner, metadataLanguage, originLanguages, displayMode, config, config.GetOrderedBackdropTypes()))
            yield return image;
        foreach (var image in ProcessImages(images.Backdrops, ImageType.Backdrop, metadataLanguage, originLanguages, displayMode, config, config.GetOrderedBackdropTypes()))
            yield return image;
    }

    private static IEnumerable<RemoteImageInfo> ProcessImages(IReadOnlyList<API.Models.Image> images, ImageType imageType, string metadataLanguage, string[] originLanguages, bool displayMode, ImageConfiguration config,  IReadOnlyList<ImageLanguageType> orderedTypes) {
        var filteredImages = images
            .Select(image => (image, type: GetTypeForImage(image, metadataLanguage, originLanguages)));
        if (!displayMode && orderedTypes.Count > 0)
            filteredImages = filteredImages
                .Where(tuple => config.UsePreferred && tuple.image.IsPreferred || orderedTypes.Contains(tuple.type));
        var orderedImages = filteredImages
            .OrderByDescending(tuple => !config.UsePreferred || tuple.image.IsPreferred)
            .ThenBy(tuple => orderedTypes.IndexOf(tuple.type))
            .ThenByDescending(tuple => config.UseCommunityRating
                ? (tuple.image.CommunityRating?.ToFloat(10) ?? 0, tuple.image.CommunityRating?.Votes ?? 0)
                : (0, 0)
            )
            .ToList();

        // Ensure we have at least 1 poster available if we filtered the list and we're looking for a primary image.
        if (
            !displayMode &&
            orderedImages.Count == 0 &&
            orderedTypes.Count > 0 &&
            imageType is ImageType.Primary &&
            images.Any(image => image is { Source: "AniDB", Type: API.Models.ShokoImageType.Poster, IsAvailable: true })
        )
            orderedImages = [(images.First(image => image is { Source: "AniDB", Type: API.Models.ShokoImageType.Poster, IsAvailable: true }), ImageLanguageType.None)];

        var index = orderedImages.Count - 1;
        var useDimensions = config.UseDimensions;
        foreach (var (image, _) in orderedImages) {
            var remoteImage = SelectImage(image, imageType, metadataLanguage, displayMode, useDimensions, index);
            if (remoteImage is not null)
                yield return remoteImage;

            index--;
        }
    }

    private static ImageLanguageType GetTypeForImage(API.Models.Image image, string metadataLanguage, string[] originLanguages) {
        if (string.IsNullOrEmpty(image.LanguageCode))
            return ImageLanguageType.None;

        if (string.Equals(image.LanguageCode, metadataLanguage, StringComparison.OrdinalIgnoreCase))
            return ImageLanguageType.Metadata;

        if (originLanguages.Contains(image.LanguageCode, StringComparison.OrdinalIgnoreCase))
            return ImageLanguageType.Original;

        if (string.Equals(image.LanguageCode, "en", StringComparison.OrdinalIgnoreCase) || string.Equals(image.LanguageCode, "eng", StringComparison.OrdinalIgnoreCase))
            return ImageLanguageType.English;

        return ImageLanguageType.Unknown;
    }

    private static RemoteImageInfo? SelectImage(API.Models.Image? image, ImageType imageType, string? metadataLanguage, bool displayMode, bool useDimensions, int index) {
        if (image is not { IsAvailable: true })
            return null;

        var remoteImage = new RemoteImageInfo {
            ProviderName = $"{image.Source.ToString().Replace("TMDB", "TheMovieDb")} ({Plugin.MetadataProviderName})",
            Type = imageType,
            Url = image.ToURLString(),
        };

        if (displayMode) {
            remoteImage.Width = image.Width;
            remoteImage.Height = image.Height;
            remoteImage.Language = image.LanguageCode;
            if (image.CommunityRating is { } rating) {
                remoteImage.CommunityRating = rating.ToFloat(10);
                remoteImage.VoteCount = rating.Votes;
                remoteImage.RatingType = RatingType.Score;
            }
        }
        else {
            remoteImage.Width = useDimensions ? image.Width : null;
            remoteImage.Height = useDimensions ? image.Height : null;
            remoteImage.Language = metadataLanguage;
            remoteImage.CommunityRating = Over9K + index;
            remoteImage.VoteCount = Over9K + index;
            remoteImage.RatingType = RatingType.Score;
        }

        return remoteImage;
    }

    #endregion
}