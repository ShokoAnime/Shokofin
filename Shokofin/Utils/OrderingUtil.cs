using MediaBrowser.Model.Entities;
using Shokofin.API.Models;

namespace Shokofin.Utils
{
    public class OrderingUtil
    {

        /// <summary>
        /// Group series as
        /// </summary>
        public enum SeriesGroupType
        {
            /// <summary>
            /// No grouping. All series will have their own entry.
            /// </summary>
            Default = 0,
            /// <summary>
            /// Don't group, but make series merge-friendly by using the season numbers from TvDB.
            /// </summary>
            TvDB = 1,
            /// <summary>
            /// Group seris based on Shoko's default group filter.
            /// </summary>
            ShokoGroup = 2,
        }

        /// <summary>
        /// Season ordering when grouping series using Shoko's groups.
        /// </summary>
        public enum SeasonOrderType
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
        /// Get index number for an episode in a series.
        /// </summary>
        /// <returns>Absolute index.</returns>
        public static int GetIndexNumber(DataUtil.SeriesInfo series, DataUtil.EpisodeInfo episode)
        {
            switch (Plugin.Instance.Configuration.SeriesGrouping)
            {
                default:
                case SeriesGroupType.Default:
                    return episode.AniDB.EpisodeNumber;
                case SeriesGroupType.TvDB: {
                    var epNum = episode?.TvDB.Number ?? 0;
                    if (epNum == 0)
                        goto case SeriesGroupType.Default;
                    return epNum;
                }
                case SeriesGroupType.ShokoGroup: {
                    int offset = 0;
                    var sizes = series.Shoko.Sizes;
                    switch (episode.AniDB.Type)
                    {
                        case "Normal":
                            break;
                        case "Special":
                            offset += sizes.Total.Episodes;
                            break; // goto case "Normal";
                        case "Other":
                            offset += sizes.Total?.Specials ?? 0;
                            goto case "Special";
                        case "Parody":
                            offset += sizes.Total?.Others ?? 0;
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
                case SeriesGroupType.Default:
                    switch (episode.AniDB.Type)
                    {
                        case "Normal":
                            return 1;
                        case "Special":
                            return 0;
                        default:
                            return 98;
                    }
                case SeriesGroupType.TvDB: {
                    var seasonNumber = episode?.TvDB?.Season;
                    if (seasonNumber == null)
                        goto case SeriesGroupType.Default;
                    return seasonNumber ?? 1;
                }
                case SeriesGroupType.ShokoGroup: {
                    var id = series.ID;
                    if (series == group.DefaultSeries)
                        return 1;
                    var index = group.SeriesList.FindIndex(s => s.ID == id);
                    if (index == -1)
                        goto case SeriesGroupType.Default;
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
