using MediaBrowser.Model.Entities;
using Shokofin.API.Models;

namespace Shokofin.Utils
{
    public class OrderingUtil
    {

        /// <summary>
        /// Group series or movie box-sets
        /// </summary>
        public enum SeriesOrBoxSetGroupType
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
        public enum SeasonAndMovieOrderType
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

        /// <summary>
        /// Get index number for a movie in a box-set.
        /// </summary>
        /// <returns>Absoute index.</returns>
        public static int GetMovieIndexNumber(DataUtil.GroupInfo group, DataUtil.SeriesInfo series, DataUtil.EpisodeInfo episode)
        {
            switch (Plugin.Instance.Configuration.BoxSetGrouping)
            {
                default:
                case SeriesOrBoxSetGroupType.Default:
                    return 1;
                case SeriesOrBoxSetGroupType.ShokoSeries:
                    return episode.AniDB.EpisodeNumber;
                case SeriesOrBoxSetGroupType.ShokoGroup:
                {
                    int offset = 0;
                    foreach (DataUtil.SeriesInfo s in group.SeriesList)
                    {
                        var sizes = s.Shoko.Sizes.Total;
                        if (s != series)
                        {
                            if (episode.AniDB.Type == "Special")
                            {
                                var index = series.FilteredSpecialEpisodesList.FindIndex(e => string.Equals(e.ID, episode.ID));
                                if (index == -1)
                                    throw new System.IndexOutOfRangeException("Episode not in filtered list");
                                return offset - (index + 1);
                            }
                            switch (episode.AniDB.Type)
                            {
                                case "Normal":
                                    // offset += 0;
                                    break;
                                case "Parody":
                                    offset += sizes?.Episodes ?? 0;
                                    goto case "Normal";
                                case "Other":
                                    offset += sizes?.Parodies ?? 0;
                                    goto case "Parody";
                            }
                            return offset + episode.AniDB.EpisodeNumber;
                        }
                        else
                        {
                            if (episode.AniDB.Type == "Special") {
                                offset -= series.FilteredSpecialEpisodesList.Count;
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
        public static int GetIndexNumber(DataUtil.SeriesInfo series, DataUtil.EpisodeInfo episode)
        {
            switch (Plugin.Instance.Configuration.SeriesGrouping)
            {
                default:
                case SeriesOrBoxSetGroupType.Default:
                    return episode.AniDB.EpisodeNumber;
                case SeriesOrBoxSetGroupType.MergeFriendly:
                {
                    var epNum = episode?.TvDB.Number ?? 0;
                    if (epNum == 0)
                        goto case SeriesOrBoxSetGroupType.Default;
                    return epNum;
                }
                case SeriesOrBoxSetGroupType.ShokoGroup:
                {
                    if (episode.AniDB.Type == "Special")
                    {
                        var index = series.FilteredSpecialEpisodesList.FindIndex(e => string.Equals(e.ID, episode.ID));
                        if (index == -1)
                            throw new System.IndexOutOfRangeException("Episode not in filtered list");
                        return -(index + 1);
                    }
                    int offset = 0;
                    var sizes = series.Shoko.Sizes.Total;
                    switch (episode.AniDB.Type)
                    {
                        case "Normal":
                            break;
                        case "Special":
                            offset += sizes?.Episodes ?? 0;
                            break; // goto case "Normal";
                        case "Other":
                            offset += sizes?.Specials ?? 0;
                            goto case "Special";
                        case "Parody":
                            offset += sizes?.Others ?? 0;
                            goto case "Other";
                    }
                    return offset + episode.AniDB.EpisodeNumber;
                }
            }
        }

        /// <summary>
        /// Get season number for an episode in a series.
        /// </summary>
        /// <param name="group"></param>
        /// <param name="series"></param>
        /// <param name="episode"></param>
        /// <returns></returns>
        public static int GetSeasonNumber(DataUtil.GroupInfo group, DataUtil.SeriesInfo series, DataUtil.EpisodeInfo episode)
        {
            switch (Plugin.Instance.Configuration.SeriesGrouping)
            {
                default:
                case SeriesOrBoxSetGroupType.Default:
                    switch (episode.AniDB.Type)
                    {
                        case "Normal":
                            return 1;
                        case "Special":
                            return 0;
                        default:
                            return 98;
                    }
                case SeriesOrBoxSetGroupType.MergeFriendly: {
                    var seasonNumber = episode?.TvDB?.Season;
                    if (seasonNumber == null)
                        goto case SeriesOrBoxSetGroupType.Default;
                    return seasonNumber ?? 1;
                }
                case SeriesOrBoxSetGroupType.ShokoGroup: {
                    var id = series.ID;
                    if (series == group.DefaultSeries)
                        return 1;
                    var index = group.SeriesList.FindIndex(s => s.ID == id);
                    if (index == -1)
                        goto case SeriesOrBoxSetGroupType.Default;
                    var value = index - group.DefaultSeriesIndex;
                    return value < 0 ? value : value + 1;
                }
            }
        }

        public static ExtraType? GetExtraType(Episode.AniDB episode)
        {
            switch (episode.Type)
            {
                case "Normal":
                case "Other":
                    return null;
                case "ThemeSong":
                    return ExtraType.ThemeVideo;
                case "Trailer":
                    return ExtraType.Trailer;
                case "Special": {
                    var title = TextUtil.GetTitleByLanguages(episode.Titles, "en") ?? "";
                    // Interview
                    if (title.Contains("interview", System.StringComparison.OrdinalIgnoreCase))
                        return ExtraType.Interview;
                    // Cinema intro/outro
                    if (title.StartsWith("cinema ", System.StringComparison.OrdinalIgnoreCase) &&
                    (title.Contains("intro", System.StringComparison.OrdinalIgnoreCase) || title.Contains("outro", System.StringComparison.OrdinalIgnoreCase)))
                        return ExtraType.Clip;
                    return null;
                }
                default:
                    return ExtraType.Unknown;
            }
        }
    }
}
