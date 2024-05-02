using System;
using System.Linq;
using Shokofin.API.Info;
using Shokofin.API.Models;

using ExtraType = MediaBrowser.Model.Entities.ExtraType;

namespace Shokofin.Utils;

public class Ordering
{
    /// <summary>
    /// Library filtering mode.
    /// </summary>
    public enum LibraryFilteringMode
    {
        /// <summary>
        /// Will use either <see cref="Strict"/> or <see cref="Lax"/> depending
        /// on which metadata providers are enabled for the library.
        /// </summary>
        Auto = 0,
        /// <summary>
        /// Will only allow files/folders that are recognised and it knows
        /// should be part of the library.
        /// </summary>
        Strict = 1,
        /// <summary>
        /// Will premit files/folders that are not recognised to exist in the
        /// library, but will filter out anything it knows should not be part of
        /// the library.
        /// </summary>
        Lax = 2,
        /// <summary>
        /// Use at your own risk. And also don't complain about the results.
        /// </summary>
        Disabled = 3,
    }

    /// <summary>
    /// Group series or movie box-sets
    /// </summary>
    public enum CollectionCreationType
    {
        /// <summary>
        /// No grouping. All series will have their own entry.
        /// </summary>
        None = 0,

        /// <summary>
        /// Group movies based on Shoko's series.
        /// </summary>
        ShokoSeries = 1,

        /// <summary>
        /// Group both movies and shows into collections based on shoko's
        /// groups.
        /// </summary>
        ShokoGroup = 2,
    }

    /// <summary>
    /// Season or movie ordering when grouping series/box-sets using Shoko's groups.
    /// </summary>
    public enum OrderType
    {
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
        /// Use the default for the type.
        /// </summary>
        Default = 0,

        /// <summary>
        /// Always exclude the specials from the season.
        /// </summary>
        Excluded = 1,

        /// <summary>
        /// Always place the specials after the normal episodes in the season.
        /// </summary>
        AfterSeason = 2,

        /// <summary>
        /// Use a mix of <see cref="InBetweenSeasonByOtherData" /> and <see cref="InBetweenSeasonByAirDate" />.
        /// </summary>
        InBetweenSeasonMixed = 3,

        /// <summary>
        /// Place the specials in-between normal episodes based on the time the episodes aired.
        /// </summary>
        InBetweenSeasonByAirDate = 4,

        /// <summary>
        /// Place the specials in-between normal episodes based upon the data from TvDB or TMDB.
        /// </summary>
        InBetweenSeasonByOtherData = 5,
    }

    /// <summary>
    /// Get index number for an episode in a series.
    /// </summary>
    /// <returns>Absolute index.</returns>
    public static int GetEpisodeNumber(ShowInfo showInfo, SeasonInfo seasonInfo, EpisodeInfo episodeInfo)
    {
        var index = 0;
        var offset = 0;
        if (episodeInfo.ExtraType != null) {
            var seasonIndex = showInfo.SeasonList.FindIndex(s => string.Equals(s.Id, seasonInfo.Id));
            if (seasonIndex == -1)
                throw new System.IndexOutOfRangeException($"Series is not part of the provided group. (Group={showInfo.GroupId},Series={seasonInfo.Id},Episode={episodeInfo.Id})");
            index = seasonInfo.ExtrasList.FindIndex(e => string.Equals(e.Id, episodeInfo.Id));
            if (index == -1)
                throw new System.IndexOutOfRangeException($"Episode not in the filtered specials list. (Group={showInfo.GroupId},Series={seasonInfo.Id},Episode={episodeInfo.Id})");
            offset = showInfo.SeasonList.GetRange(0, seasonIndex).Aggregate(0, (count, series) => count + series.ExtrasList.Count);
            return offset + index + 1;
        }

        if (showInfo.IsSpecial(episodeInfo)) {
            var seasonIndex = showInfo.SeasonList.FindIndex(s => string.Equals(s.Id, seasonInfo.Id));
            if (seasonIndex == -1)
                throw new System.IndexOutOfRangeException($"Series is not part of the provided group. (Group={showInfo.GroupId},Series={seasonInfo.Id},Episode={episodeInfo.Id})");
            index = seasonInfo.SpecialsList.FindIndex(e => string.Equals(e.Id, episodeInfo.Id));
            if (index == -1)
                throw new System.IndexOutOfRangeException($"Episode not in the filtered specials list. (Group={showInfo.GroupId},Series={seasonInfo.Id},Episode={episodeInfo.Id})");
            offset = showInfo.SeasonList.GetRange(0, seasonIndex).Aggregate(0, (count, series) => count + series.SpecialsList.Count);
            return offset + index + 1;
        }

        // All normal episodes will find their index in here.
        index = seasonInfo.EpisodeList.FindIndex(ep => ep.Id == episodeInfo.Id);
        if (index == -1)
            index = seasonInfo.AlternateEpisodesList.FindIndex(ep => ep.Id == episodeInfo.Id);

        // Extras that show up in the season because _somebody_ decided to disable filtering.
        if (index == -1) {
            offset += seasonInfo.EpisodeList.Count > 0 ? seasonInfo.EpisodeList.Count : seasonInfo.AlternateEpisodesList.Count;
            index = seasonInfo.ExtrasList.FindIndex(ep => ep.Id == episodeInfo.Id);
        }

        // All other episodes that show up in the season because _somebody_ decided to disable filtering.
        if (index == -1) {
            offset += seasonInfo.ExtrasList.Count;
            index = seasonInfo.LeftoverList.FindIndex(ep => ep.Id == episodeInfo.Id);
        }

        // If we still cannot find the episode for whatever reason, then bail. I don't fudging know why, but I know it's not the plugin's fault.
        if (index == -1)
            throw new IndexOutOfRangeException($"Unable to find index to use for \"{episodeInfo.Shoko.Name}\". (Episode={episodeInfo.Id},Series={seasonInfo.Id})");

        return index + 1;
    }

    public static (int?, int?, int?, bool) GetSpecialPlacement(ShowInfo showInfo, SeasonInfo seasonInfo, EpisodeInfo episodeInfo)
    {
        var order = Plugin.Instance.Configuration.SpecialsPlacement;

        // Return early if we want to exclude them from the normal seasons.
        if (order == SpecialOrderType.Excluded) {
            // Check if this should go in the specials season.
            return (null, null, null, showInfo.IsSpecial(episodeInfo));
        }

        // Abort if episode is not a TvDB special or AniDB special
        if (!showInfo.IsSpecial(episodeInfo))
            return (null, null, null, false);

        int? episodeNumber = null;
        int seasonNumber = GetSeasonNumber(showInfo, seasonInfo, episodeInfo);
        int? airsBeforeEpisodeNumber = null;
        int? airsBeforeSeasonNumber = null;
        int? airsAfterSeasonNumber = null;
        switch (order) {
            default:
                airsAfterSeasonNumber = seasonNumber;
                break;
            case SpecialOrderType.InBetweenSeasonByAirDate:
                byAirdate:
                // Reset the order if we come from `SpecialOrderType.InBetweenSeasonMixed`.
                episodeNumber = null;
                if (seasonInfo.SpecialsAnchors.TryGetValue(episodeInfo, out var previousEpisode))
                    episodeNumber = GetEpisodeNumber(showInfo, seasonInfo, previousEpisode);

                if (episodeNumber.HasValue && episodeNumber.Value < seasonInfo.EpisodeList.Count) {
                    airsBeforeEpisodeNumber = episodeNumber.Value + 1;
                    airsBeforeSeasonNumber = seasonNumber;
                }
                else {
                    airsAfterSeasonNumber = seasonNumber;
                }
                break;
            case SpecialOrderType.InBetweenSeasonMixed:
            case SpecialOrderType.InBetweenSeasonByOtherData:
                // We need to have TvDB/TMDB data in the first place to do this method.
                if (episodeInfo.TvDB == null) {
                    if (order == SpecialOrderType.InBetweenSeasonMixed) goto byAirdate;
                    break;
                }

                episodeNumber = episodeInfo.TvDB.AirsBeforeEpisode;
                if (!episodeNumber.HasValue) {
                    if (episodeInfo.TvDB.AirsBeforeSeason.HasValue) {
                        airsBeforeSeasonNumber = seasonNumber;
                        break;
                    }

                    if (order == SpecialOrderType.InBetweenSeasonMixed) goto byAirdate;
                    airsAfterSeasonNumber = seasonNumber;
                    break;
                }

                var nextEpisode = seasonInfo.EpisodeList.FirstOrDefault(e => e.TvDB != null && e.TvDB.SeasonNumber == seasonNumber && e.TvDB.EpisodeNumber == episodeNumber);
                if (nextEpisode != null) {
                    airsBeforeEpisodeNumber = GetEpisodeNumber(showInfo, seasonInfo, nextEpisode);
                    airsBeforeSeasonNumber = seasonNumber;
                    break;
                }

                if (order == SpecialOrderType.InBetweenSeasonMixed) goto byAirdate;
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
    public static int GetSeasonNumber(ShowInfo showInfo, SeasonInfo seasonInfo, EpisodeInfo episodeInfo)
    {
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
    public static ExtraType? GetExtraType(Episode.AniDB episode)
    {
        switch (episode.Type)
        {
            case EpisodeType.Normal:
            case EpisodeType.Other:
                return null;
            case EpisodeType.ThemeSong:
            case EpisodeType.OpeningSong:
            case EpisodeType.EndingSong:
                return ExtraType.ThemeVideo;
            case EpisodeType.Trailer:
                return ExtraType.Trailer;
            case EpisodeType.Special: {
                var title = Text.GetTitlesForLanguage(episode.Titles, false, "en");
                if (string.IsNullOrEmpty(title))
                    return null;
                // Interview
                if (title.Contains("interview", System.StringComparison.OrdinalIgnoreCase))
                    return ExtraType.Interview;
                // Cinema intro/outro
                if (title.StartsWith("cinema ", System.StringComparison.OrdinalIgnoreCase) &&
                (title.Contains("intro", System.StringComparison.OrdinalIgnoreCase) || title.Contains("outro", System.StringComparison.OrdinalIgnoreCase)))
                    return ExtraType.Clip;
                // Behind the Scenes
                if (title.Contains("making of", System.StringComparison.CurrentCultureIgnoreCase))
                    return ExtraType.BehindTheScenes;
                if (title.Contains("music in", System.StringComparison.CurrentCultureIgnoreCase))
                    return ExtraType.BehindTheScenes;
                if (title.Contains("advance screening", System.StringComparison.CurrentCultureIgnoreCase))
                    return ExtraType.BehindTheScenes;
                return null;
            }
            default:
                return ExtraType.Unknown;
        }
    }
}
