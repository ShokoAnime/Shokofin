using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Shokofin.API;
using Shokofin.Providers;
using Shokofin.Utils;

namespace Shokofin

{
    public interface IIdLookup
    {
        #region Base Item

        /// <summary>
        /// Check if the plugin is enabled for <see cref="MediaBrowser.Controller.Entities.BaseItem" >the item</see>.
        /// </summary>
        /// <param name="item">The <see cref="MediaBrowser.Controller.Entities.BaseItem" /> to check.</param>
        /// <returns>True if the plugin is enabled for the <see cref="MediaBrowser.Controller.Entities.BaseItem" /></returns>
        bool IsEnabledForItem(BaseItem item);

        #endregion
        #region Group Id

        bool TryGetGroupIdFromSeriesId(string seriesId, out string groupId);

        #endregion
        #region Series Id

        bool TryGetSeriesIdFor(string path, out string seriesId);

        bool TryGetSeriesIdFromEpisodeId(string episodeId, out string seriesId);

        /// <summary>
        /// Try to get the Shoko Series Id for the <see cref="MediaBrowser.Controller.Entities.TV.Series" />.
        /// </summary>
        /// <param name="series">The <see cref="MediaBrowser.Controller.Entities.TV.Series" /> to check for.</param>
        /// <param name="seriesId">The variable to put the id in.</param>
        /// <returns>True if it successfully retrived the id for the <see cref="MediaBrowser.Controller.Entities.TV.Series" />.</returns>
        bool TryGetSeriesIdFor(Series series, out string seriesId);

        /// <summary>
        /// Try to get the Shoko Series Id for the <see cref="MediaBrowser.Controller.Entities.TV.Season" />.
        /// </summary>
        /// <param name="season">The <see cref="MediaBrowser.Controller.Entities.TV.Season" /> to check for.</param>
        /// <param name="seriesId">The variable to put the id in.</param>
        /// <returns>True if it successfully retrived the id for the <see cref="MediaBrowser.Controller.Entities.TV.Season" />.</returns>
        bool TryGetSeriesIdFor(Season season, out string seriesId);

        /// <summary>
        /// Try to get the Shoko Series Id for the <see cref="MediaBrowser.Controller.Entities.TV.Season" />.
        /// </summary>
        /// <param name="season">The <see cref="MediaBrowser.Controller.Entities.TV.Season" /> to check for.</param>
        /// <param name="seriesId">The variable to put the id in.</param>
        /// <returns>True if it successfully retrived the id for the <see cref="MediaBrowser.Controller.Entities.TV.Season" />.</returns>
        bool TryGetSeriesIdFor(BoxSet boxSet, out string seriesId);

        /// <summary>
        /// Try to get the Shoko Series Id for the <see cref="MediaBrowser.Controller.Entities.TV.Season" />.
        /// </summary>
        /// <param name="season">The <see cref="MediaBrowser.Controller.Entities.TV.Season" /> to check for.</param>
        /// <param name="seriesId">The variable to put the id in.</param>
        /// <returns>True if it successfully retrived the id for the <see cref="MediaBrowser.Controller.Entities.TV.Season" />.</returns>
        bool TryGetSeriesIdFor(Movie movie, out string seriesId);

        #endregion
        #region Series Path

        bool TryGetPathForSeriesId(string seriesId, out string path);

        #endregion
        #region Episode Id

        bool TryGetEpisodeIdFor(string path, out string episodeId);

        bool TryGetEpisodeIdFor(BaseItem item, out string episodeId);

        #endregion
        #region Episode Path

        bool TryGetPathForEpisodeId(string episodeId, out string path);

        #endregion
        #region File Id

        bool TryGetFileIdFor(BaseItem item, out string fileId);

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

        public bool IsEnabledForItem(BaseItem item)
        {
            var reItem = item switch {
                Series s => s,
                Season s => s.Series,
                Episode e => e.Series,
                _ => item,
            };
            if (reItem == null)
                return false;
            var libraryOptions = LibraryManager.GetLibraryOptions(reItem);
            return libraryOptions != null &&
                libraryOptions.TypeOptions.Any(o => o.Type == nameof (Series) && o.MetadataFetchers.Contains(Plugin.MetadataProviderName));
        }

        #endregion
        #region Group Id

        public bool TryGetGroupIdFromSeriesId(string seriesId, out string groupId)
        {
            return ApiManager.TryGetGroupIdForSeriesId(seriesId, out groupId);
        }

        #endregion
        #region Series Id

        public bool TryGetSeriesIdFor(string path, out string seriesId)
        {
            return ApiManager.TryGetSeriesIdForPath(path, out seriesId);
        }

        public bool TryGetSeriesIdFromEpisodeId(string episodeId, out string seriesId)
        {
            return ApiManager.TryGetSeriesIdForEpisodeId(episodeId, out seriesId);
        }

        public bool TryGetSeriesIdFor(Series series, out string seriesId)
        {
            if (series.ProviderIds.TryGetValue("Shoko Series", out seriesId) && !string.IsNullOrEmpty(seriesId)) {
                return true;
            }

            if (TryGetSeriesIdFor(series.Path, out seriesId)) {
                // Set the "Shoko Group" and "Shoko Series" provider ids for the series, since it haven't been set again. It doesn't matter if it's not saved to the database, since we only need it while running the following code.
                if (TryGetGroupIdFromSeriesId(seriesId, out var groupId)) {
                    var filterByType = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                    var groupInfo = ApiManager.GetGroupInfoSync(groupId, filterByType);
                    seriesId = groupInfo.DefaultSeries.Id;

                    SeriesProvider.AddProviderIds(series, seriesId, groupInfo.Id);
                }
                // Same as above, but only set the "Shoko Series" id.
                else {
                    SeriesProvider.AddProviderIds(series, seriesId);
                }
                // Make sure the presentation unique is not cached, so we won't reuse the cache key.
                series.PresentationUniqueKey = null;
                return true;
            }

            return false;
        }

        public bool TryGetSeriesIdFor(Season season, out string seriesId)
        {
            if (!season.IndexNumber.HasValue) {
                seriesId = null;
                return false;
            }
            return TryGetSeriesIdFor(season.Series, out seriesId);
        }

        public bool TryGetSeriesIdFor(Movie movie, out string seriesId)
        {
            if (movie.ProviderIds.TryGetValue("Shoko Series", out seriesId) && !string.IsNullOrEmpty(seriesId)) {
                return true;
            }

            if (TryGetEpisodeIdFor(movie.Path, out var episodeId) && TryGetSeriesIdFromEpisodeId(episodeId, out seriesId)) {
                return true;
            }

            return false;
        }

        public bool TryGetSeriesIdFor(BoxSet boxSet, out string seriesId)
        {
            if (boxSet.ProviderIds.TryGetValue("Shoko Series", out seriesId) && !string.IsNullOrEmpty(seriesId)) {
                return true;
            }

            if (TryGetSeriesIdFor(boxSet.Path, out seriesId)) {
                if (TryGetGroupIdFromSeriesId(seriesId, out var groupId)) {
                    var filterByType = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                    var groupInfo = ApiManager.GetGroupInfoSync(groupId, filterByType);
                    seriesId = groupInfo.DefaultSeries.Id;
                }
                return true;
            }

            return false;
        }

        #endregion
        #region Series Path

        public bool TryGetPathForSeriesId(string seriesId, out string path)
        {
            return ApiManager.TryGetSeriesPathForId(seriesId, out path);
        }

        #endregion
        #region Episode Id

        public bool TryGetEpisodeIdFor(string path, out string episodeId)
        {
            return ApiManager.TryGetEpisodeIdForPath(path, out episodeId);
        }

        public bool TryGetEpisodeIdFor(BaseItem item, out string episodeId)
        {
            // This will account for virtual episodes and existing episodes
            if (item.ProviderIds.TryGetValue("Shoko Episode", out episodeId) && !string.IsNullOrEmpty(episodeId)) {
                return true;
            }

            // This will account for new episodes that haven't received their first metadata update yet.
            if (TryGetEpisodeIdFor(item.Path, out episodeId)) {
                return true;
            }

            return false;
        }

        #endregion
        #region Episode Path

        public bool TryGetPathForEpisodeId(string episodeId, out string path)
        {
            return ApiManager.TryGetEpisodePathForId(episodeId, out path);
        }

        #endregion
        #region File Id

        public bool TryGetFileIdFor(BaseItem episode, out string fileId)
        {
            if (episode.ProviderIds.TryGetValue("Shoko File", out fileId))
                return true;

            return ApiManager.TryGetFileIdForPath(episode.Path, out fileId, out var episodeCount);
        }

        #endregion
    }
}