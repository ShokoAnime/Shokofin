using System;
using System.Linq;
using Jellyfin.Extensions;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.API.Models.AniDB;
using Shokofin.Extensions;
using ExtraType = MediaBrowser.Model.Entities.ExtraType;

namespace Shokofin.Utils;

public class Ordering {
    /// <summary>
    /// Library operation mode.
    /// </summary>
    public enum LibraryOperationMode {
        /// <summary>
        /// Will use the Virtual File System (VFS) on the library.
        /// </summary>
        VFS = 0,

        /// <summary>
        /// Will use legacy filtering in strict mode, which will only allow
        /// files/folders that are recognized and it knows should be part of the
        /// library.
        /// </summary>
        Strict = 1,

        /// <summary>
        /// Obsolete. Use <see cref="Strict"/> instead.
        /// </summary>
        /// TODO: Break this during the next major version of the plugin.
        Auto = Strict,

        /// <summary>
        /// Will use legacy filtering in lax mode, which will permit
        /// files/folders that are not recognized to exist in the library, but
        /// will filter out anything it knows should not be part of the library.
        /// </summary>
        Lax = 2,
    }

    /// <summary>
    /// Helps determine what the user wants to group into collections
    /// (AKA "box-sets").
    /// </summary>
    public enum CollectionCreationType {
        /// <summary>
        /// No grouping. All series will have their own entry.
        /// </summary>
        None = 0,

        /// <summary>
        /// Group movies into collections based on Shoko's series.
        /// </summary>
        Movies = 1,

        /// <summary>
        /// Group both movies and shows into collections based on Shoko's
        /// groups.
        /// </summary>
        Shared = 2,
    }

    /// <summary>
    /// Helps determine what the user wants to group into playlists.
    /// </summary>
    public enum PlaylistCreationType {
        /// <summary>
        /// No grouping. No playlists will be created.
        /// </summary>
        None = 0,

        /// <summary>
        /// Only create playlists for groups that contain both series and movies.
        /// </summary>
        GroupWithMixedContent = 1,

        /// <summary>
        /// Create playlists for all groups.
        /// </summary>
        Grouped = 2,
    }

    /// <summary>
    /// Determines how per-series playlist tags are evaluated when deciding
    /// whether to create a playlist for a group.
    /// </summary>
    public enum PlaylistTagFilter {
        /// <summary>
        /// Ignore per-series tags. Use the global grouping config as-is.
        /// </summary>
        None = 0,

        /// <summary>
        /// Only create a playlist for a group if at least one series in
        /// the group has the playlist include tag set.
        /// </summary>
        AnySeriesTagged = 1,

        /// <summary>
        /// Only create a playlist for a group if all series in the group
        /// have the playlist include tag set.
        /// </summary>
        AllSeriesTagged = 2,
    }

    /// <summary>
    /// Season or movie ordering when grouping series/box-sets using Shoko's groups.
    /// </summary>
    public enum OrderType {
        /// <summary>
        /// No ordering.
        /// </summary>
        None = -1,

        /// <summary>
        /// Let Shoko decide the order.
        /// </summary>
        Default = 0,

        /// <summary>
        /// Order seasons by release date.
        /// </summary>
        ReleaseDate = 1,

        /// <summary>
        /// Order seasons based on the chronological order of relations.
        /// </summary>
        Chronological = 2,

        /// <summary>
        /// Order seasons based on the chronological order of only direct relations.
        /// </summary>
        ChronologicalIgnoreIndirect = 3,
    }

    public enum SpecialOrderType {
        /// <summary>
        /// Only for use with the series settings.
        /// </summary>
        None = -1,

        /// <summary>
        /// Always exclude the specials from the season.
        /// </summary>
        Excluded = 1,

        /// <summary>
        /// Always place the specials after the normal episodes in the season.
        /// </summary>
        AfterSeason = 2,

        /// <summary>
        /// Obsolete. Use <see cref="Excluded" /> instead.
        /// </summary>
        /// TODO: Break this during the next major version of the plugin.
        Default = Excluded,

        /// <summary>
        /// Use a mix of <see cref="InBetweenSeasonByOtherData" /> and <see cref="InBetweenSeasonByAirDate" />.
        /// </summary>
        InBetweenSeasonMixed = 3,

        /// <summary>
        /// Place the specials in-between normal episodes based on when the episodes aired.
        /// </summary>
        InBetweenSeasonByAirDate = 4,

        /// <summary>
        /// Place the specials in-between normal episodes based upon data from TMDB.
        /// </summary>
        InBetweenSeasonByOtherData = 5,
    }

    /// <summary>
    /// Get index number for an episode in a series.
    /// </summary>
    /// <returns>Absolute index.</returns>
    public static int GetEpisodeNumber(ShowInfo showInfo, SeasonInfo seasonInfo, EpisodeInfo episodeInfo) {
        var index = 0;
        var offset = 0;
        if (seasonInfo.IsExtraEpisode(episodeInfo)) {
            var seasonIndex = showInfo.SeasonList.FindIndex(s => string.Equals(s.Id, seasonInfo.Id));
            if (seasonIndex == -1)
                throw new System.IndexOutOfRangeException($"Series is not part of the provided group. (MainSeason={showInfo.Id},Series={seasonInfo.Id},ExtraSeries={seasonInfo.ExtraIds},Episode={episodeInfo.Id})");
            index = seasonInfo.ExtrasList.FindIndex(e => string.Equals(e.Id, episodeInfo.Id));
            if (index == -1)
                throw new System.IndexOutOfRangeException($"Episode not in the filtered specials list. (MainSeason={showInfo.Id},Series={seasonInfo.Id},ExtraSeries={seasonInfo.ExtraIds},Episode={episodeInfo.Id})");
            offset = showInfo.SeasonList.GetRange(0, seasonIndex).Aggregate(0, (count, series) => count + series.ExtrasList.Count);
            return offset + index + 1;
        }

        if (showInfo.IsSpecial(episodeInfo)) {
            var seasonIndex = showInfo.SeasonList.FindIndex(s => string.Equals(s.Id, seasonInfo.Id));
            if (seasonIndex == -1)
                throw new System.IndexOutOfRangeException($"Series is not part of the provided group. (MainSeason={showInfo.Id},Series={seasonInfo.Id},ExtraSeries={seasonInfo.ExtraIds},Episode={episodeInfo.Id})");
            index = seasonInfo.SpecialsList.FindIndex(e => string.Equals(e.Id, episodeInfo.Id));
            if (index == -1)
                throw new System.IndexOutOfRangeException($"Episode not in the filtered specials list. (MainSeason={showInfo.Id},Series={seasonInfo.Id},ExtraSeries={seasonInfo.ExtraIds},Episode={episodeInfo.Id})");
            offset = showInfo.SeasonList.GetRange(0, seasonIndex).Aggregate(0, (count, series) => count + series.SpecialsList.Count);
            return offset + index + 1;
        }

        // All normal episodes will find their index in here.
        index = seasonInfo.EpisodeList.FindIndex(ep => ep.Id == episodeInfo.Id);
        if (index == -1)
            index = seasonInfo.AlternateEpisodesList.FindIndex(ep => ep.Id == episodeInfo.Id);

        // If we still cannot find the episode for whatever reason, then bail. I don't fudging know why, but I know it's not the plugin's fault.
        if (index == -1)
            throw new IndexOutOfRangeException($"Unable to find index to use for \"{episodeInfo.Title}\". (MainSeason=\"{showInfo.Id}\",Series=\"{seasonInfo.Id}\",ExtraSeries={(seasonInfo.ExtraIds.Count > 0 ? $"[\"{seasonInfo.ExtraIds.Join("\",\"")}\"]" : "[]")},Episode={episodeInfo.Id})");

        return index + 1;
    }

    public static (int?, int?, int?, bool) GetSpecialPlacement(ShowInfo showInfo, SeasonInfo seasonInfo, EpisodeInfo episodeInfo) {
        // Return early if we want to exclude them from the normal seasons.
        if (seasonInfo.SpecialsPlacement is SpecialOrderType.Excluded) {
            // Check if this should go in the specials season.
            return (null, null, null, showInfo.IsSpecial(episodeInfo));
        }

        // Abort if episode is not a TMDB special or AniDB special
        if (!showInfo.IsSpecial(episodeInfo))
            return (null, null, null, false);

        int seasonNumber = GetSeasonNumber(showInfo, seasonInfo, episodeInfo);
        int? airsBeforeEpisodeNumber = null;
        int? airsBeforeSeasonNumber = null;
        int? airsAfterSeasonNumber = null;
        switch (seasonInfo.SpecialsPlacement) {
            default:
                airsAfterSeasonNumber = seasonNumber;
                break;
            case SpecialOrderType.InBetweenSeasonMixed:
            case SpecialOrderType.InBetweenSeasonByAirDate:
                // Reset the order if we come from `SpecialOrderType.InBetweenSeasonMixed`.
                int? episodeNumber = null;
                if (seasonInfo.SpecialsBeforeEpisodes.Contains(episodeInfo.Id)) {
                    airsBeforeSeasonNumber = seasonNumber;
                    break;
                }

                if (seasonInfo.SpecialsAnchors.TryGetValue(episodeInfo.Id, out var previousEpisode))
                    episodeNumber = GetEpisodeNumber(showInfo, seasonInfo, previousEpisode);

                if (episodeNumber.HasValue && episodeNumber.Value < seasonInfo.EpisodeList.Count) {
                    airsBeforeEpisodeNumber = episodeNumber.Value + 1;
                    airsBeforeSeasonNumber = seasonNumber;
                }
                else {
                    airsAfterSeasonNumber = seasonNumber;
                }
                break;
            case SpecialOrderType.InBetweenSeasonByOtherData:
                break;
        }

        return (airsBeforeEpisodeNumber, airsBeforeSeasonNumber, airsAfterSeasonNumber, true);
    }

    /// <summary>
    /// Get season number for an episode in a series.
    /// </summary>
    /// <param name="showInfo"></param>
    /// <param name="seasonInfo"></param>
    /// <param name="episodeInfo"></param>
    /// <returns></returns>
    public static int GetSeasonNumber(ShowInfo showInfo, SeasonInfo seasonInfo, EpisodeInfo episodeInfo) {
        if (!showInfo.TryGetBaseSeasonNumberForSeasonInfo(seasonInfo, out var seasonNumber))
            return 0;

        if (seasonInfo.AlternateEpisodesList.Any(ep => ep.Id == episodeInfo.Id))
            return seasonNumber + 1;

        return seasonNumber;
    }

    /// <summary>
    /// Get the extra type for an episode.
    /// </summary>
    /// <param name="episode"></param>
    /// <returns></returns>
    public static ExtraType? GetExtraType(AnidbEpisode episode) {
        switch (episode.Type) {
            case EpisodeType.Episode:
                return null;
            case EpisodeType.Credits:
            case EpisodeType.OpeningSong:
            case EpisodeType.EndingSong:
                return ExtraType.ThemeVideo;
            case EpisodeType.Trailer:
                return ExtraType.Trailer;
            case EpisodeType.Other:
            case EpisodeType.Special: {
                var title = TextUtility.GetTitleForLanguage(episode.Titles, false, false, "en");
                if (string.IsNullOrEmpty(title))
                    return null;
                // Interview
                if (title.Contains("interview", StringComparison.OrdinalIgnoreCase))
                    return ExtraType.Interview;
                // Cinema/theatrical intro/outro
                if (
                    (title.StartsWith("cinema ", StringComparison.OrdinalIgnoreCase) || title.StartsWith("theatrical ", StringComparison.OrdinalIgnoreCase)) &&
                    (title.Contains("intro", StringComparison.OrdinalIgnoreCase) || title.Contains("outro", StringComparison.OrdinalIgnoreCase)) ||
                    title.Contains("manners movie", StringComparison.OrdinalIgnoreCase)
                )
                    return ExtraType.Clip;
                // Special endings for episodes
                if (title.StartsWith("episode", StringComparison.OrdinalIgnoreCase) && title.Contains("ending"))
                    return ExtraType.Clip;
                // Behind the Scenes
                if (title.Contains("behind the scenes", StringComparison.OrdinalIgnoreCase))
                    return ExtraType.BehindTheScenes;
                if (title.Contains("making of", StringComparison.OrdinalIgnoreCase))
                    return ExtraType.BehindTheScenes;
                if (title.Contains("music in", StringComparison.OrdinalIgnoreCase))
                    return ExtraType.BehindTheScenes;
                if (title.Contains("advance screening", StringComparison.OrdinalIgnoreCase))
                    return ExtraType.BehindTheScenes;
                if (title.Contains("premiere", StringComparison.OrdinalIgnoreCase))
                    return ExtraType.BehindTheScenes;
                if (title.Contains("talk show", StringComparison.OrdinalIgnoreCase))
                    return ExtraType.Featurette;
                return null;
            }
            default:
                return ExtraType.Unknown;
        }
    }
}
