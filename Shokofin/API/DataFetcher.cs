using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Shokofin.API.Info;
using Shokofin.API.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Path = System.IO.Path;

namespace Shokofin.API
{
    public class DataFetcher
    {
        private static readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions() {
            ExpirationScanFrequency = new System.TimeSpan(0, 3, 0),
        });

        private static readonly System.TimeSpan DefaultTimeSpan = new System.TimeSpan(0, 5, 0);

        #region People

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

        #endregion
        #region Tags

        public static async Task<string[]> GetTags(string seriesId)
        {
            return (await ShokoAPI.GetSeriesTags(seriesId, DataFetcher.GetTagFilter()))?.Select(tag => tag.Name).ToArray() ?? new string[0];
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

        #endregion
        #region File Info

        public static (FileInfo, EpisodeInfo, SeriesInfo, GroupInfo) GetFileInfoByPathSync(string path, bool includeGroup = true, bool onlyMovies = false)
        {
            return GetFileInfoByPath(path, includeGroup, onlyMovies).GetAwaiter().GetResult();
        }

        public static async Task<(FileInfo, EpisodeInfo, SeriesInfo, GroupInfo)> GetFileInfoByPath(string path, bool includeGroup = true, bool onlyMovies = false)
        {
            // TODO: Check if it can be written in a better way. Parent directory + File Name
            var id = Path.Join(
                    Path.GetDirectoryName(path)?.Split(Path.DirectorySeparatorChar).LastOrDefault(),
                    Path.GetFileName(path));
            var result = await ShokoAPI.GetFileByPath(id);

            var file = result?.FirstOrDefault();
            if (file == null)
                return (null, null, null, null);

            var series = file?.SeriesIDs.FirstOrDefault();
            var seriesId = series?.SeriesID.ID.ToString();
            var episodes = series?.EpisodeIDs?.FirstOrDefault();
            var episodeId = episodes?.ID.ToString();
            if (string.IsNullOrEmpty(seriesId) || string.IsNullOrEmpty(episodeId))
                return (null, null, null, null);

            GroupInfo groupInfo = null;
            if (includeGroup)
            {
                groupInfo =  await GetGroupInfoForSeries(seriesId, onlyMovies);
                if (groupInfo == null)
                    return (null, null, null, null);
            }

            var seriesInfo = await GetSeriesInfo(seriesId);
            if (seriesInfo == null)
                return (null, null, null, null);

            var episodeInfo = await GetEpisodeInfo(episodeId);
            if (episodeInfo == null)
                return (null, null, null, null);

            var fileInfo = CreateFileInfo(file, file.ID.ToString(), series?.EpisodeIDs?.Count ?? 0);

            return (fileInfo, episodeInfo, seriesInfo, groupInfo);
        }

        public async Task<FileInfo> GetFileInfo(string fileId)
        {
            var file = await ShokoAPI.GetFile(fileId);
            if (file == null)
                return null;
            return CreateFileInfo(file);
        }

        private static FileInfo CreateFileInfo(File file, string fileId = null, int episodeCount = 0)
        {
            if (file == null)
                return null;
            if (string.IsNullOrEmpty(fileId))
                fileId = file.ID.ToString();
            var cacheKey = $"file:{fileId}:{episodeCount}";
            FileInfo info = null;
            if (_cache.TryGetValue<FileInfo>(cacheKey, out info))
                return info;
            info = new FileInfo
            {
                ID = fileId,
                Shoko = file,

            };
            _cache.Set<FileInfo>(cacheKey, info, DefaultTimeSpan);
            return info;
        }

        #endregion
        #region Episode Info

        public static async Task<EpisodeInfo> GetEpisodeInfo(string episodeId)
        {
            if (string.IsNullOrEmpty(episodeId))
                return null;
            if (_cache.TryGetValue<EpisodeInfo>($"episode:{episodeId}", out var info))
                return info;
            var episode = await ShokoAPI.GetEpisode(episodeId);
            return await CreateEpisodeInfo(episode, episodeId);
        }

        private static async Task<EpisodeInfo> CreateEpisodeInfo(Episode episode, string episodeId = null)
        {
            if (episode == null)
                return null;
            if (string.IsNullOrEmpty(episodeId))
                episodeId = episode.IDs.ID.ToString();
            var cacheKey = $"episode:{episodeId}";
            EpisodeInfo info = null;
            if (_cache.TryGetValue<EpisodeInfo>(cacheKey, out info))
                return info;
            info = new EpisodeInfo
            {
                ID = episodeId,
                Shoko = (await ShokoAPI.GetEpisode(episodeId)),
                AniDB = (await ShokoAPI.GetEpisodeAniDb(episodeId)),
                TvDB = ((await ShokoAPI.GetEpisodeTvDb(episodeId))?.FirstOrDefault()),
            };
            _cache.Set<EpisodeInfo>(cacheKey, info, DefaultTimeSpan);
            return info;
        }

        #endregion
        #region Series Info
        public static SeriesInfo GetSeriesInfoByPathSync(string path)
        {
            return GetSeriesInfoByPath(path).GetAwaiter().GetResult();
        }

        public static async Task<SeriesInfo> GetSeriesInfoByPath(string path)
        {
            var id = Path.DirectorySeparatorChar + path.Split(Path.DirectorySeparatorChar).Last();
            var result = await ShokoAPI.GetSeriesPathEndsWith(id);

            var seriesId = result?.FirstOrDefault()?.IDs?.ID.ToString();
            if (string.IsNullOrEmpty(seriesId))
                return null;

            return await GetSeriesInfo(seriesId);
        }

        public static async Task<SeriesInfo> GetSeriesInfoFromGroup(string groupId, int seasonNumber)
        {
            var groupInfo = await GetGroupInfo(groupId, false);
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
            if (string.IsNullOrEmpty(seriesId))
                return null;
            if (_cache.TryGetValue<SeriesInfo>( $"series:{seriesId}", out var info))
                return info;
            var series = await ShokoAPI.GetSeries(seriesId);
            return await CreateSeriesInfo(series, seriesId);
        }

        private static async Task<SeriesInfo> CreateSeriesInfo(Series series, string seriesId = null)
        {
            if (series == null)
                return null;

            if (string.IsNullOrEmpty(seriesId))
                seriesId = series.IDs.ID.ToString();

            SeriesInfo info = null;
            var cacheKey = $"series:{seriesId}";
            if (_cache.TryGetValue<SeriesInfo>(cacheKey, out info))
                return info;

            var aniDb = await ShokoAPI.GetSeriesAniDb(seriesId);
            var episodeList = await ShokoAPI.GetEpisodesFromSeries(seriesId)
                .ContinueWith(async task => await Task.WhenAll(task.Result.Select(e => CreateEpisodeInfo(e)))).Unwrap()
                .ContinueWith(l => l.Result.Where(s => s != null).ToList());
            var filteredSpecialEpisodesList = episodeList.Where(e => e.AniDB.Type == EpisodeType.Special && Shokofin.Utils.Ordering.GetExtraType(e.AniDB) != null).ToList();
            info = new SeriesInfo
            {
                ID = seriesId,
                Shoko = series,
                AniDB = aniDb,
                TvDBID = series.IDs.TvDB.Count > 0 ? series.IDs.TvDB.FirstOrDefault().ToString() : null,
                EpisodeList = episodeList,
                FilteredSpecialEpisodesList = filteredSpecialEpisodesList,
            };
            _cache.Set<SeriesInfo>(cacheKey, info, DefaultTimeSpan);
            return info;
        }

        #endregion
        #region Group Info

        public static GroupInfo GetGroupInfoByPathSync(string path, bool onlyMovies = false)
        {
            return GetGroupInfoByPath(path, onlyMovies).GetAwaiter().GetResult();
        }

        public static async Task<GroupInfo> GetGroupInfoByPath(string path, bool onlyMovies = false)
        {
            var id = Path.DirectorySeparatorChar + path.Split(Path.DirectorySeparatorChar).Last();
            var result = await ShokoAPI.GetSeriesPathEndsWith(id);

            var seriesId = result?.FirstOrDefault()?.IDs?.ID.ToString();
            if (string.IsNullOrEmpty(seriesId))
                return null;

            var groupInfo = await GetGroupInfoForSeries(seriesId, onlyMovies);
            if (groupInfo == null)
                return null;

            return groupInfo;
        }

        public static async Task<GroupInfo> GetGroupInfo(string groupId, bool onlyMovies = false)
        {
            if (string.IsNullOrEmpty(groupId))
                return null;
            if (_cache.TryGetValue<GroupInfo>($"group:{(onlyMovies ? "movies" : "all")}:{groupId}", out var info))
                return info;
            var group = await ShokoAPI.GetGroup(groupId);
            return await CreateGroupInfo(group, groupId, onlyMovies);
        }

        public static async Task<GroupInfo> GetGroupInfoForSeries(string seriesId, bool onlyMovies = false)
        {
            // TODO: Find a way to remove the double requests for group info.
            var group = await ShokoAPI.GetGroupFromSeries(seriesId);
            if (group == null)
                return null;
            var groupId = group.IDs.ID.ToString();
            GroupInfo info = null;
            var cacheKey = $"group-by-series:{(onlyMovies ? "movies" : "all")}:{seriesId}";
            if (_cache.TryGetValue<GroupInfo>(cacheKey, out info))
                return info;
            info = await GetGroupInfo(groupId, onlyMovies);
            _cache.Set<GroupInfo>(cacheKey, info, DefaultTimeSpan);
            return info;
        }

        private static async Task<GroupInfo> CreateGroupInfo(Group group, string groupId, bool onlyMovies)
        {
            if (group == null)
                return null;

            if (string.IsNullOrEmpty(groupId))
                groupId = group.IDs.ID.ToString();

            var cacheKey = $"group:{(onlyMovies ? "movies" : "all")}:{groupId}";
            GroupInfo info = null;
            if (_cache.TryGetValue<GroupInfo>(cacheKey, out info))
                return info;

            var seriesList = await ShokoAPI.GetSeriesInGroup(groupId)
                .ContinueWith(async task => await Task.WhenAll(task.Result.Select(s => CreateSeriesInfo(s)))).Unwrap()
                .ContinueWith(l => l.Result.Where(s => s != null).ToList());
            if (onlyMovies && seriesList != null && seriesList.Count > 0)
                seriesList = seriesList.Where(s => s.AniDB.Type == SeriesType.Movie).ToList();
            if (seriesList == null || seriesList.Count == 0)
                return null;
            // Map
            int foundIndex = -1;
            int targetId = (group.IDs.DefaultSeries ?? 0);
            // Sort list
            var orderingType = onlyMovies ? Plugin.Instance.Configuration.MovieOrdering : Plugin.Instance.Configuration.SeasonOrdering;
            switch (orderingType)
            {
                case Shokofin.Utils.Ordering.SeasonAndMovieOrderType.Default:
                    break;
                case Shokofin.Utils.Ordering.SeasonAndMovieOrderType.ReleaseDate:
                    seriesList = seriesList.OrderBy(s => s?.AniDB?.AirDate ?? System.DateTime.MaxValue).ToList();
                    break;
                // Should not be selectable unless a user fidles with DevTools in the browser to select the option.
                case Shokofin.Utils.Ordering.SeasonAndMovieOrderType.Chronological:
                    throw new System.Exception("Not implemented yet");
            }
            // Select the targeted id if a group spesify a default series.
            if (targetId != 0)
                foundIndex = seriesList.FindIndex(s => s.Shoko.IDs.ID == targetId);
            // Else select the default series as first-to-be-released.
            else switch (orderingType)
            {
                // The list is already sorted by release date, so just return the first index.
                case Shokofin.Utils.Ordering.SeasonAndMovieOrderType.ReleaseDate:
                    foundIndex = 0;
                    break;
                // We don't know how Shoko may have sorted it, so just find the earliest series
                case Shokofin.Utils.Ordering.SeasonAndMovieOrderType.Default:
                // We can't be sure that the the series in the list was _released_ chronologically, so find the earliest series, and use that as a base.
                case Shokofin.Utils.Ordering.SeasonAndMovieOrderType.Chronological: {
                    var earliestSeries = seriesList.Aggregate((cur, nxt) => (cur == null || (nxt?.AniDB.AirDate ?? System.DateTime.MaxValue) < (cur.AniDB.AirDate ?? System.DateTime.MaxValue)) ? nxt : cur);
                    foundIndex = seriesList.FindIndex(s => s == earliestSeries);
                    break;
                }
            }

            // Throw if we can't get a base point for seasons.
            if (foundIndex == -1)
                throw new System.Exception("Unable to get a base-point for seasions withing the group");

            info = new GroupInfo
            {
                ID = groupId,
                Shoko = group,
                SeriesList = seriesList,
                DefaultSeries = seriesList[foundIndex],
                DefaultSeriesIndex = foundIndex,
            };
            _cache.Set<GroupInfo>(cacheKey, info, DefaultTimeSpan);
            return info;
        }

        #endregion
    }
}
