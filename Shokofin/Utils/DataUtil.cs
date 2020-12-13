using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shokofin.API;
using Shokofin.API.Models;
using Path = System.IO.Path;

namespace Shokofin.Utils
{
    public class DataUtil
    {
        public static float GetRating(Rating rating)
        {
            return rating == null ? 0 : (float) ((rating.Value * 10) / rating.MaxValue);
        }

        public static async Task<IEnumerable<PersonInfo>> GetPeople(string seriesId)
        {
            var list = new List<PersonInfo>();
            var roles = await ShokoAPI.GetSeriesCast(seriesId);
            foreach (var role in roles)
            {
                list.Add(new PersonInfo
                {
                    Type = PersonType.Actor,
                    Name = role.Staff.Name,
                    Role = role.Character.Name,
                    ImageUrl = role.Staff.Image?.ToURLString(),
                });
            }
            return list;
        }

        #region File Info

        public class FileInfo
        {
            public string ID;
            public File Shoko;
        }

        public static async Task<(string, FileInfo, EpisodeInfo, SeriesInfo, GroupInfo)> GetFileInfoByPath(string path, bool includeGroup = true)
        {
            // TODO: Check if it can be written in a better way. Parent directory + File Name
            var id = Path.Join(
                    Path.GetDirectoryName(path)?.Split(Path.DirectorySeparatorChar).LastOrDefault(),
                    Path.GetFileName(path));
            var result = await ShokoAPI.GetFileByPath(id);

            var file = result?.FirstOrDefault();
            if (file == null)
                return (id, null, null, null, null);

            var series = file?.SeriesIDs.FirstOrDefault();
            var seriesId = series?.SeriesID.ID.ToString();
            var episodes = series?.EpisodeIDs?.FirstOrDefault();
            var episodeId = episodes?.ID.ToString();
            var otherEpisodesCount =  series?.EpisodeIDs.Count() - 1 ?? 0;
            if (string.IsNullOrEmpty(seriesId) || string.IsNullOrEmpty(episodeId))
                return (id, null, null, null, null);

            var episodeInfo = await GetEpisodeInfo(episodeId);
            if (episodeInfo == null)
                return (id, null, null, null, null);

            var seriesInfo = await GetSeriesInfo(seriesId);
            if (episodeInfo == null)
                return (id, null, null, null, null);

            GroupInfo groupInfo = null;
            if (includeGroup)
            {
                groupInfo =  await GetGroupInfoForSeries(seriesId);
                if (groupInfo == null)
                    return (id, null, null, null, null);
            }

            var fileInfo = new FileInfo
            {
                ID = file.ID.ToString(),
                Shoko = file,
            };

            return (id, fileInfo, episodeInfo, seriesInfo, groupInfo);
        }

        public static async Task<(string, FileInfo, EpisodeInfo, SeriesInfo, GroupInfo)> GetFileInfoByID(string fileId, string id = null)
        {
            var file = await ShokoAPI.GetFile(fileId);
            if (file == null)
                return (id, null, null, null, null);
            var fileInfo = new FileInfo
            {
                ID = fileId,
                Shoko = file,
            };

            var episodes = await ShokoAPI.GetEpisodeFromFile(fileId);
            var episodeInfo = await CreateEpisodeInfo(episodes[0], null, episodes?.Count ?? 0 - 1);
            if (episodeInfo == null)
                return (id, null, null, null, null);

            var seriesInfo = await CreateSeriesInfo(await ShokoAPI.GetSeriesFromEpisode(episodeInfo.ID));
            if (seriesInfo == null)
                return (id, null, null, null, null);

            var groupInfo = await GetGroupInfoForSeries(seriesInfo.ID);
            if (groupInfo == null)
                return (id, null, null, null, null);

            return (id, fileInfo, episodeInfo, seriesInfo, groupInfo);
        }

        #endregion
        #region Episode Info

        public class EpisodeInfo
        {
            public string ID;
            public Episode Shoko;
            public Episode.AniDB AniDB;
            public Episode.TvDB TvDB;
            public int OtherEpisodesCount;
        }

        public static async Task<EpisodeInfo> GetEpisodeInfo(string episodeId, int otherEpisodesCount = 0)
        {
            var episode = await ShokoAPI.GetEpisode(episodeId);
            return await CreateEpisodeInfo(episode, episodeId, otherEpisodesCount);
        }

        public static async Task<EpisodeInfo> CreateEpisodeInfo(Episode episode, string episodeId = null, int otherEpisodesCount = 0)
        {
            if (episode == null)
                return null;
            if (string.IsNullOrEmpty(episodeId))
                episodeId = episode.IDs.ID.ToString();
            return new EpisodeInfo
            {
                ID = episodeId,
                Shoko = await ShokoAPI.GetEpisode(episodeId),
                AniDB = await ShokoAPI.GetEpisodeAniDb(episodeId),
                TvDB = (await ShokoAPI.GetEpisodeTvDb(episodeId))?.FirstOrDefault(),
                OtherEpisodesCount = otherEpisodesCount,
            };
        }

        #endregion
        #region Series Info

        public class SeriesInfo
        {
            public string ID;
            public Series Shoko;
            public Series.AniDB AniDB;
            public string TvDBID;
        }

        public static async Task<(string, SeriesInfo)> GetSeriesInfoByPath(string path)
        {
            var id = Path.DirectorySeparatorChar + path.Split(Path.DirectorySeparatorChar).Last();
            var result = await ShokoAPI.GetSeriesPathEndsWith(id);

            var seriesId = result?.FirstOrDefault()?.IDs?.ID.ToString();
            if (string.IsNullOrEmpty(seriesId))
                return (id, null);

            return (id, await GetSeriesInfo(seriesId));
        }

        public static async Task<SeriesInfo> GetSeriesInfoFromGroup(string groupId, int seasonNumber)
        {
            var groupInfo = await GetGroupInfo(groupId);
            if (groupInfo == null)
                return null;
            int seriesIndex = seasonNumber > 0 ? seasonNumber - 1 : seasonNumber;
            var index = groupInfo.DefaultSeriesIndex + seriesIndex;
            var seriesInfo = groupInfo.SeriesList[index];
            if (seriesInfo == null)
                return null;

            return seriesInfo;
        }

        public static async Task<SeriesInfo> GetSeriesInfo(string seriesId)
        {
            var series = await ShokoAPI.GetSeries(seriesId);
            return await CreateSeriesInfo(series, seriesId);
        }

        private static async Task<SeriesInfo> CreateSeriesInfo(Series series, string seriesId = null)
        {
            if (series == null)
                return null;
            if (string.IsNullOrEmpty(seriesId))
                seriesId = series.IDs.ID.ToString();
            return new SeriesInfo
            {
                ID = seriesId,
                Shoko = series,
                AniDB = await ShokoAPI.GetSeriesAniDb(seriesId),
                TvDBID = series.IDs.TvDB.Count > 0 ? series.IDs.TvDB.FirstOrDefault().ToString() : null,
            };
        }

        #endregion
        #region Group Info

        public class GroupInfo
        {
            public string ID;
            public List<SeriesInfo> SeriesList;
            public SeriesInfo DefaultSeries;
            public int DefaultSeriesIndex;
        }

        public static async Task<(string, GroupInfo)> GetGroupInfoByPath(string path)
        {
            var id = Path.DirectorySeparatorChar + path.Split(Path.DirectorySeparatorChar).Last();
            var result = await ShokoAPI.GetSeriesPathEndsWith(id);

            var seriesId = result?.FirstOrDefault()?.IDs?.ID.ToString();
            if (string.IsNullOrEmpty(seriesId))
                return (id, null);

            var groupInfo = await GetGroupInfoForSeries(seriesId);
            if (groupInfo == null)
                return (id, null);

            return (id, groupInfo);
        }

        public static async Task<GroupInfo> GetGroupInfo(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
                return null;

            var group = await ShokoAPI.GetGroup(groupId);
            return await CreateGroupInfo(group, groupId);
        }

        public static async Task<GroupInfo> GetGroupInfoForSeries(string seriesId)
        {
            var group = await ShokoAPI.GetGroupFromSeries(seriesId);
            return await CreateGroupInfo(group);
        }

        private static async Task<GroupInfo> CreateGroupInfo(Group group, string groupId = null)
        {
            if (group == null)
                return null;

            if (string.IsNullOrEmpty(groupId))
                groupId = group.IDs.ID.ToString();

            var seriesList = await ShokoAPI.GetSeriesInGroup(groupId)
                .ContinueWith(async task => await Task.WhenAll(task.Result.Select(s => CreateSeriesInfo(s)))).Unwrap()
                .ContinueWith(l => l.Result.ToList());
            if (seriesList == null || seriesList.Count == 0)
                return null;
            // Map
            int foundIndex = -1;
            int targetId = (group.IDs.DefaultSeries ?? 0);
            // Sort list
            var orderingType = Plugin.Instance.Configuration.SeasonOrdering;
            switch (orderingType)
            {
                case OrderingUtil.SeasonOrderType.Default:
                    break;
                case OrderingUtil.SeasonOrderType.ReleaseDate:
                    seriesList.OrderBy(s => s?.AniDB?.AirDate ?? System.DateTime.MaxValue);
                    break;
                // Should not be selectable unless a user fidles with DevTools in the browser to select the option.
                case OrderingUtil.SeasonOrderType.Chronological:
                    throw new System.Exception("Not implemented yet");
            }
            // Select the targeted id if a group spesify a default series.
            if (targetId != 0)
                foundIndex = seriesList.FindIndex(s => s.Shoko.IDs.ID == targetId);
            // Else select the default series as first-to-be-released.
            else switch (orderingType)
            {
                // The list is already sorted by release date, so just return the first index.
                case OrderingUtil.SeasonOrderType.ReleaseDate:
                    foundIndex = 0;
                    break;
                // We don't know how Shoko may have sorted it, so just find the earliest series
                case OrderingUtil.SeasonOrderType.Default:
                // We can't be sure that the the series in the list was _released_ chronologically, so find the earliest series, and use that as a base.
                case OrderingUtil.SeasonOrderType.Chronological: {
                    var earliestSeries = seriesList.Aggregate((cur, nxt) => (cur == null || (nxt?.AniDB.AirDate ?? System.DateTime.MaxValue) < (cur.AniDB.AirDate ?? System.DateTime.MaxValue)) ? nxt : cur);
                    foundIndex = seriesList.FindIndex(s => s == earliestSeries);
                    break;
                }
            }

            // Return if we can't get a base point for seasons.
            if (foundIndex == -1)
                return null;

            return new GroupInfo
            {
                ID = groupId,
                SeriesList = seriesList,
                DefaultSeries = seriesList[foundIndex],
                DefaultSeriesIndex = foundIndex,
            };
        }

        #endregion

        public static async Task<string[]> GetTags(string seriesId)
        {
            return (await ShokoAPI.GetSeriesTags(seriesId, DataUtil.GetTagFilter()))?.Select(tag => tag.Name).ToArray() ?? new string[0];
        }

        /// <summary>
        /// Get the tag filter
        /// </summary>
        /// <returns></returns>
        private static int GetTagFilter()
        {
            var config = Plugin.Instance.Configuration;
            var filter = 0;

            if (config.HideAniDbTags) filter = 1;
            if (config.HideArtStyleTags) filter |= (filter << 1);
            if (config.HideSourceTags) filter |= (filter << 2);
            if (config.HideMiscTags) filter |= (filter << 3);
            if (config.HidePlotTags) filter |= (filter << 4);

            return filter;
        }
    }
}
