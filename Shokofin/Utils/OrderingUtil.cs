using System.Linq;
using Shokofin.API.Info;
using Shokofin.API.Models;

using ExtraType = MediaBrowser.Model.Entities.ExtraType;

namespace Shokofin.Utils
{
    public class Ordering
    {
        public enum GroupFilterType {
            Default = 0,
            Movies = 1,
            Others = 2,
        }

        /// <summary>
        /// Group series or movie box-sets
        /// </summary>
        public enum GroupType
        {
            /// <summary>
            /// No grouping. All series will have their own entry.
            /// </summary>
            Default = 0,

            /// <summary>
            /// Don't group, but make series merge-friendly by using the season numbers from TvDB.
            /// </summary>
            MergeFriendly = 1,

            /// <summary>
            /// Group seris based on Shoko's default group filter.
            /// </summary>
            ShokoGroup = 2,

            /// <summary>
            /// Group movies based on Shoko's series.
            /// </summary>
            ShokoSeries = 3,
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
            /// Use a mix of <see cref="Shokofin.Utils.Ordering.SpecialOrderType.InBetweenSeasonByOtherData" /> and <see cref="Shokofin.Utils.Ordering.SpecialOrderType.InBetweenSeasonByAirDate" />.
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
        /// Get index number for a movie in a box-set.
        /// </summary>
        /// <returns>Absoute index.</returns>
        public static int GetMovieIndexNumber(GroupInfo group, SeriesInfo series, EpisodeInfo episode)
        {
            switch (Plugin.Instance.Configuration.BoxSetGrouping) {
                default:
                case GroupType.Default:
                    return 1;
                case GroupType.ShokoSeries:
                    return episode.AniDB.EpisodeNumber;
                case GroupType.ShokoGroup: {
                    int offset = 0;
                    foreach (SeriesInfo s in group.SeriesList) {
                        var sizes = s.Shoko.Sizes.Total;
                        if (s != series) {
                            if (episode.AniDB.Type == EpisodeType.Special) {
                                var index = series.SpecialsList.FindIndex(e => string.Equals(e.Id, episode.Id));
                                if (index == -1)
                                    throw new System.IndexOutOfRangeException($"Episode not in the filtered specials list. (Group={group.Id},Series={series.Id},Episode={episode.Id})");
                                return offset - (index + 1);
                            }
                            switch (episode.AniDB.Type) {
                                case EpisodeType.Normal:
                                    // offset += 0; // it's not needed, so it's just here as a comment instead.
                                    break;
                                case EpisodeType.Special:
                                    offset += sizes?.Episodes ?? 0;
                                    goto case EpisodeType.Normal;
                                case EpisodeType.Unknown:
                                    offset += sizes?.Specials ?? 0;
                                    goto case EpisodeType.Special;
                                // Add them to the bottom of the list if we didn't filter them out properly.
                                case EpisodeType.Parody:
                                    offset += sizes?.Others ?? 0;
                                    goto case EpisodeType.Unknown;
                                case EpisodeType.OpeningSong:
                                    offset += sizes?.Parodies ?? 0;
                                    goto case EpisodeType.Parody;
                                case EpisodeType.Trailer:
                                    offset += sizes?.Credits ?? 0;
                                    goto case EpisodeType.OpeningSong;
                                default:
                                    offset += sizes?.Trailers ?? 0;
                                    goto case EpisodeType.Trailer;
                            }
                            return offset + episode.AniDB.EpisodeNumber;
                        }
                        else {
                            if (episode.AniDB.Type == EpisodeType.Special) {
                                offset -= series.SpecialsList.Count;
                            }
                            offset += (sizes?.Episodes ?? 0) + (sizes?.Parodies ?? 0) + (sizes?.Others ?? 0);
                        }
                    }
                    break;
                }
            }
            return 0;
        }

        /// <summary>
        /// Get index number for an episode in a series.
        /// </summary>
        /// <returns>Absolute index.</returns>
        public static int GetEpisodeNumber(GroupInfo group, SeriesInfo series, EpisodeInfo episode)
        {
            switch (Plugin.Instance.Configuration.SeriesGrouping)
            {
                default:
                case GroupType.Default:
                    if (episode.AniDB.Type == EpisodeType.Special) {
                        var index = series.SpecialsList.FindIndex(e => string.Equals(e.Id, episode.Id));
                        if (index == -1)
                            throw new System.IndexOutOfRangeException($"Episode not in the filtered specials list. (Series={series.Id},Episode={episode.Id})");
                        return (index + 1);
                    }
                    return episode.AniDB.EpisodeNumber;
                case GroupType.MergeFriendly: {
                    var episodeNumber = episode?.TvDB?.Number;
                    if (episodeNumber.HasValue)
                        return episodeNumber.Value;
                    goto case GroupType.Default;
                }
                case GroupType.ShokoGroup: {
                    int offset = 0;
                    if (episode.AniDB.Type == EpisodeType.Special) {
                        var seriesIndex = group.SeriesList.FindIndex(s => string.Equals(s.Id, series.Id));
                        if (seriesIndex == -1)
                            throw new System.IndexOutOfRangeException($"Series is not part of the provided group. (Group={group.Id},Series={series.Id},Episode={episode.Id})");
                        var index = series.SpecialsList.FindIndex(e => string.Equals(e.Id, episode.Id));
                        if (index == -1)
                            throw new System.IndexOutOfRangeException($"Episode not in the filtered specials list. (Group={group.Id},Series={series.Id},Episode={episode.Id})");
                        offset = group.SeriesList.GetRange(0, seriesIndex).Aggregate(0, (count, series) => count + series.SpecialsList.Count);
                        return offset + (index + 1);
                    }
                    var sizes = series.Shoko.Sizes.Total;
                    switch (episode.AniDB.Type) {
                        case EpisodeType.Other:
                        case EpisodeType.Unknown:
                        case EpisodeType.Normal:
                            // offset += 0; // it's not needed, so it's just here as a comment instead.
                            break;
                        // Add them to the bottom of the list if we didn't filter them out properly.
                        case EpisodeType.Parody:
                            offset += sizes?.Episodes ?? 0;
                            goto case EpisodeType.Normal;
                        case EpisodeType.OpeningSong:
                            offset += sizes?.Parodies ?? 0;
                            goto case EpisodeType.Parody;
                        case EpisodeType.Trailer:
                            offset += sizes?.Credits ?? 0;
                            goto case EpisodeType.OpeningSong;
                        default:
                            offset += sizes?.Trailers ?? 0;
                            goto case EpisodeType.Trailer;
                    }
                    return offset + episode.AniDB.EpisodeNumber;
                }
            }
        }

        public static (int?, int?, int?) GetSpecialPlacement(GroupInfo group, SeriesInfo series, EpisodeInfo episode)
        {
            var order = Plugin.Instance.Configuration.SpecialsPlacement;
            if (order == SpecialOrderType.Excluded)
                return (null, null, null);

            // Abort if episode is not a TvDB special or AniDB special
            var allowOtherData = order == SpecialOrderType.InBetweenSeasonByOtherData || order == SpecialOrderType.InBetweenSeasonMixed;
            if (allowOtherData  ? !(episode?.TvDB?.Season == 0 || episode.AniDB.Type == EpisodeType.Special) : episode.AniDB.Type != EpisodeType.Special)
                return (null, null, null);

            int? episodeNumber = null;
            int seasonNumber = GetSeasonNumber(group, series, episode);
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
                    if (series.SpesialsAnchors.TryGetValue(episode, out var previousEpisode))
                        episodeNumber = GetEpisodeNumber(group, series, previousEpisode);

                    if (episodeNumber.HasValue && episodeNumber.Value < series.EpisodeList.Count) {
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
                    if (episode.TvDB == null) {
                        if (order == SpecialOrderType.InBetweenSeasonMixed) goto byAirdate;
                        airsAfterSeasonNumber = seasonNumber;
                        break;
                    }

                    episodeNumber = episode.TvDB.AirsBeforeEpisode;
                    if (!episodeNumber.HasValue) {
                        if (episode.TvDB.AirsAfterSeason.HasValue) {
                            airsAfterSeasonNumber = seasonNumber;
                            break;
                        }

                        if (order == SpecialOrderType.InBetweenSeasonMixed) goto byAirdate;
                        airsAfterSeasonNumber = seasonNumber;
                        break;
                    }

                    var nextEpisode = series.EpisodeList.FirstOrDefault(e => e.TvDB != null && e.TvDB.Season == seasonNumber && e.TvDB.Number == episodeNumber);
                    if (nextEpisode != null) {
                        airsBeforeEpisodeNumber = GetEpisodeNumber(group, series, nextEpisode);
                        airsBeforeSeasonNumber = seasonNumber;
                        break;
                    }

                    if (order == SpecialOrderType.InBetweenSeasonMixed) goto byAirdate;
                    airsAfterSeasonNumber = seasonNumber;
                    break;
            }

            return (airsBeforeEpisodeNumber, airsBeforeSeasonNumber, airsAfterSeasonNumber);
        }

        /// <summary>
        /// Get season number for an episode in a series.
        /// </summary>
        /// <param name="group"></param>
        /// <param name="series"></param>
        /// <param name="episode"></param>
        /// <returns></returns>
        public static int GetSeasonNumber(GroupInfo group, SeriesInfo series, EpisodeInfo episode)
        {
            switch (Plugin.Instance.Configuration.SeriesGrouping) {
                default:
                case GroupType.Default:
                    switch (episode.AniDB.Type) {
                        case EpisodeType.Normal:
                            return 1;
                        case EpisodeType.Special:
                            return 0;
                        case EpisodeType.Unknown:
                            return 123;
                        case EpisodeType.Other:
                            return 124;
                        case EpisodeType.Trailer:
                            return 125;
                        case EpisodeType.ThemeSong:
                            return 126;
                        default:
                            return 127;
                    }
                case GroupType.MergeFriendly: {
                    int? seasonNumber = null;
                    if (episode.TvDB != null) {
                        if (episode.TvDB.Season == 0)
                            seasonNumber = episode.TvDB.AirsAfterSeason ?? episode.TvDB.AirsBeforeSeason ?? 1;
                        else
                            seasonNumber = episode.TvDB.Season;
                    }
                    if (!seasonNumber.HasValue)
                        goto case GroupType.Default;
                    return seasonNumber.Value;
                }
                case GroupType.ShokoGroup: {
                    if (!group.SeasonNumberBaseDictionary.TryGetValue(series, out var seasonNumber))
                        throw new System.IndexOutOfRangeException($"Series is not part of the provided group. (Group={group.Id},Series={series.Id})");


                    var offset = 0;
                    switch (episode.AniDB.Type) {
                        default:
                            break;
                        case EpisodeType.Unknown: {
                            offset = 1;
                            break;
                        }
                        case EpisodeType.Other: {
                            offset = series.AlternateEpisodesList.Count > 0 ? 2 : 1;
                            break;
                        }
                    }

                    return seasonNumber + (seasonNumber < 0 ? -offset : offset);
                }
            }
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
                case EpisodeType.Unknown:
                    return null;
                case EpisodeType.ThemeSong:
                case EpisodeType.OpeningSong:
                case EpisodeType.EndingSong:
                    return ExtraType.ThemeVideo;
                case EpisodeType.Trailer:
                    return ExtraType.Trailer;
                case EpisodeType.Special: {
                    var title = Text.GetTitleByLanguages(episode.Titles, "en");
                    if (string.IsNullOrEmpty(title))
                        return null;
                    // Interview
                    if (title.Contains("interview", System.StringComparison.OrdinalIgnoreCase))
                        return ExtraType.Interview;
                    // Cinema intro/outro
                    if (title.StartsWith("cinema ", System.StringComparison.OrdinalIgnoreCase) &&
                    (title.Contains("intro", System.StringComparison.OrdinalIgnoreCase) || title.Contains("outro", System.StringComparison.OrdinalIgnoreCase)))
                        return ExtraType.Clip;
                    // Music videos
                    if (title.Contains("music video", System.StringComparison.OrdinalIgnoreCase))
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
}
