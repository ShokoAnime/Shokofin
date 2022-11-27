using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.Utils;

using CultureInfo = System.Globalization.CultureInfo;
using Path = System.IO.Path;

namespace Shokofin.API
{
    public class ShokoAPIManager
    {
        private readonly ILogger<ShokoAPIManager> Logger;

        private readonly ShokoAPIClient APIClient;

        private readonly ILibraryManager LibraryManager;

        private readonly List<Folder> MediaFolderList = new List<Folder>();

        private readonly ConcurrentDictionary<string, string> SeriesPathToIdDictionary = new ConcurrentDictionary<string, string>();

        private readonly ConcurrentDictionary<string, string> SeriesIdToPathDictionary = new ConcurrentDictionary<string, string>();

        private readonly ConcurrentDictionary<string, string> SeriesIdToGroupIdDictionary = new ConcurrentDictionary<string, string>();

        private readonly ConcurrentDictionary<string, string> EpisodePathToEpisodeIdDictionary = new ConcurrentDictionary<string, string>();

        private readonly ConcurrentDictionary<string, string> EpisodeIdToEpisodePathDictionary = new ConcurrentDictionary<string, string>();

        private readonly ConcurrentDictionary<string, string> EpisodeIdToSeriesIdDictionary = new ConcurrentDictionary<string, string>();

        private readonly ConcurrentDictionary<string, (string, int, string, string)> FilePathToFileIdAndEpisodeCountDictionary = new ConcurrentDictionary<string,  (string, int, string, string)>();

        private readonly ConcurrentDictionary<string, string> FileIdToEpisodeIdDictionary = new ConcurrentDictionary<string, string>();

        public ShokoAPIManager(ILogger<ShokoAPIManager> logger, ShokoAPIClient apiClient, ILibraryManager libraryManager)
        {
            Logger = logger;
            APIClient = apiClient;
            LibraryManager = libraryManager;
        }

        private static IMemoryCache DataCache = new MemoryCache(new MemoryCacheOptions() {
            ExpirationScanFrequency = ExpirationScanFrequency,
        });

        private static readonly System.TimeSpan ExpirationScanFrequency = new System.TimeSpan(0, 25, 0);

        private static readonly System.TimeSpan DefaultTimeSpan = new System.TimeSpan(1, 0, 0);

        #region Ignore rule

        public Folder FindMediaFolder(string path, Folder parent, Folder root)
        {
            var mediaFolder = MediaFolderList.FirstOrDefault((folder) => path.StartsWith(folder.Path + Path.DirectorySeparatorChar));
            // Look for the root folder for the current item.
            if (mediaFolder != null) {
                return mediaFolder;
            }
            mediaFolder = parent;
            while (!mediaFolder.ParentId.Equals(root.Id)) {
                if (mediaFolder.GetParent() == null) {
                    break;
                }
                mediaFolder = (Folder)mediaFolder.GetParent();
            }
            MediaFolderList.Add(mediaFolder);
            return mediaFolder;
        }

        public string StripMediaFolder(string fullPath)
        {
            var mediaFolder = MediaFolderList.FirstOrDefault((folder) => fullPath.StartsWith(folder.Path + Path.DirectorySeparatorChar));
            if (mediaFolder != null) {
                return fullPath.Substring(mediaFolder.Path.Length);
            }
            // Try to get the media folder by loading the parent and navigating upwards till we reach the root.
            var directoryPath = System.IO.Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directoryPath)) {
                return fullPath;
            }
            mediaFolder = (LibraryManager.FindByPath(directoryPath, true) as Folder);
            if (mediaFolder == null || string.IsNullOrEmpty(mediaFolder?.Path)) {
                return fullPath;
            }
            // Look for the root folder for the current item.
            var root = LibraryManager.RootFolder;
            while (!mediaFolder.ParentId.Equals(root.Id)) {
                if (mediaFolder.GetParent() == null) {
                    break;
                }
                mediaFolder = (Folder)mediaFolder.GetParent();
            }
            MediaFolderList.Add(mediaFolder);
            return fullPath.Substring(mediaFolder.Path.Length);
        }

        #endregion
        #region Clear

        public void Clear()
        {
            Logger.LogDebug("Clearing data.");
            DataCache.Dispose();
            MediaFolderList.Clear();
            FileIdToEpisodeIdDictionary.Clear();
            FilePathToFileIdAndEpisodeCountDictionary.Clear();
            EpisodeIdToSeriesIdDictionary.Clear();
            EpisodePathToEpisodeIdDictionary.Clear();
            EpisodeIdToEpisodePathDictionary.Clear();
            SeriesPathToIdDictionary.Clear();
            SeriesIdToPathDictionary.Clear();
            SeriesIdToGroupIdDictionary.Clear();
            DataCache = (new MemoryCache((new MemoryCacheOptions() {
                ExpirationScanFrequency = ExpirationScanFrequency,
            })));
        }

        #endregion
        #region People

        private string GetImagePath(Image image)
        {
            return image != null && image.IsAvailable ? image.ToURLString() : null;
        }

        private PersonInfo RoleToPersonInfo(Role role)
        {
            switch (role.RoleName) {
                    default:
                        return null;
                    case Role.CreatorRoleType.Director:
                        return new PersonInfo {
                            Type = PersonType.Director,
                            Name = role.Staff.Name,
                            Role = role.RoleDetails,
                            ImageUrl = GetImagePath(role.Staff.Image),
                        };
                    case Role.CreatorRoleType.Producer:
                        return new PersonInfo {
                            Type = PersonType.Producer,
                            Name = role.Staff.Name,
                            Role = role.RoleDetails,
                            ImageUrl = GetImagePath(role.Staff.Image),
                        };
                    case Role.CreatorRoleType.Music:
                        return new PersonInfo {
                            Type = PersonType.Lyricist,
                            Name = role.Staff.Name,
                            Role = role.RoleDetails,
                            ImageUrl = GetImagePath(role.Staff.Image),
                        };
                    case Role.CreatorRoleType.SourceWork:
                        return new PersonInfo {
                            Type = PersonType.Writer,
                            Name = role.Staff.Name,
                            Role = role.RoleDetails,
                            ImageUrl = GetImagePath(role.Staff.Image),
                        };
                    case Role.CreatorRoleType.SeriesComposer:
                        return new PersonInfo {
                            Type = PersonType.Composer,
                            Name = role.Staff.Name,
                            ImageUrl = GetImagePath(role.Staff.Image),
                        };
                    case Role.CreatorRoleType.Seiyuu:
                        return new PersonInfo {
                            Type = PersonType.Actor,
                            Name = role.Staff.Name,
                            Role = role.Character.Name,
                            ImageUrl = GetImagePath(role.Staff.Image),
                        };
                }
        }

        #endregion
        #region Tags

        private async Task<string[]> GetTags(string seriesId)
        {
            return (await APIClient.GetSeriesTags(seriesId, GetTagFilter()))?.Select(SelectTagName).ToArray() ?? new string[0];
        }

        /// <summary>
        /// Get the tag filter
        /// </summary>
        /// <returns></returns>
        private ulong GetTagFilter()
        {
            var config = Plugin.Instance.Configuration;
            ulong filter = 132L; // We exclude genres and source by default

            if (config.HideAniDbTags) filter |= (1 << 0);
            if (config.HideArtStyleTags) filter |= (1 << 1);
            if (config.HideMiscTags) filter |= (1 << 3);
            if (config.HidePlotTags) filter |= (1 << 4);
            if (config.HideSettingTags) filter |= (1 << 5);
            if (config.HideProgrammingTags) filter |= (1 << 6);

            return filter;
        }

        #endregion
        #region Genres

        public async Task<string[]> GetGenresForSeries(string seriesId)
        {
            // The following magic number is the filter value to allow only genres in the returned list.
            var set = (await APIClient.GetSeriesTags(seriesId, 2147483776))?.Select(SelectTagName).ToHashSet() ?? new();
            set.Add(await GetSourceGenre(seriesId));
            return set.ToArray();
        }

        private async Task<string> GetSourceGenre(string seriesId)
        {
            return(await APIClient.GetSeriesTags(seriesId, 2147483652))?.FirstOrDefault()?.Name?.ToLowerInvariant() switch {
                "american derived" => "Adapted From Western Media",
                "cartoon" => "Adapted From Western Media",
                "comic book" => "Adapted From Western Media",
                "4-koma" => "Adapted From A Manga",
                "manga" => "Adapted From A Manga",
                "4-koma manga" => "Adapted From A Manga",
                "manhua" => "Adapted From A Manhua",
                "manhwa" => "Adapted from a Manhwa",
                "movie" => "Adapted From A Movie",
                "novel" => "Adapted From A Light/Web Novel",
                "rpg" => "Adapted From A Video Game",
                "action game" => "Adapted From A Video Game",
                "game" => "Adapted From A Video Game",
                "erotic game" => "Adapted From An Eroge",
                "korean drama" => "Adapted From A Korean Drama",
                "television programme" => "Adapted From A Live-Action Show",
                "visual novel" => "Adapted From A Visual Novel",
                "fan-made" => "Fan-Made",
                "remake" => "Remake",
                "radio programme" => "Original Work",
                "biographical film" => "Original Work",
                "original work" => "Original Work",
                "new" => "Original Work",
                "ultra jump" => "Original Work",
                _ => "Original Work",
            };
        }

        private string SelectTagName(Tag tag)
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tag.Name);
        }

        #endregion
        #region Studios

        public async Task<string[]> GetStudiosForSeries(string seriesId)
        {
            var cast = await APIClient.GetSeriesCast(seriesId, Role.CreatorRoleType.Studio);
            // * NOTE: Shoko Server version <4.1.2 don't support filtered cast, nor other role types besides Role.CreatorRoleType.Seiyuu.
            if (cast.Any(p => p.RoleName != Role.CreatorRoleType.Studio))
                return new string[0];
            return cast.Select(p => p.Staff.Name).ToArray();
        }

        #endregion
        #region File Info

        public (FileInfo, EpisodeInfo, SeriesInfo, GroupInfo) GetFileInfoByPathSync(string path, Ordering.GroupFilterType? filterGroupByType)
        {
            if (FilePathToFileIdAndEpisodeCountDictionary.ContainsKey(path)) {
                var (fileId, extraEpisodesCount, episodeId, seriesId) = FilePathToFileIdAndEpisodeCountDictionary[path];
                return (GetFileInfoSync(fileId, extraEpisodesCount), GetEpisodeInfoSync(episodeId), GetSeriesInfoSync(seriesId), filterGroupByType.HasValue ? GetGroupInfoForSeriesSync(seriesId, filterGroupByType.Value) : null);
            }

            return GetFileInfoByPath(path, filterGroupByType).GetAwaiter().GetResult();
        }

        public async Task<(FileInfo, EpisodeInfo, SeriesInfo, GroupInfo)> GetFileInfoByPath(string path, Ordering.GroupFilterType? filterGroupByType)
        {
            if (FilePathToFileIdAndEpisodeCountDictionary.ContainsKey(path)) {
                var (fI, eC, eI, sI) = FilePathToFileIdAndEpisodeCountDictionary[path];
                return (GetFileInfoSync(fI, eC), GetEpisodeInfoSync(eI), GetSeriesInfoSync(sI), filterGroupByType.HasValue ? GetGroupInfoForSeriesSync(sI, filterGroupByType.Value) : null);
            }

            var partialPath = StripMediaFolder(path);
            Logger.LogDebug("Looking for file matching {Path}", partialPath);
            var result = await APIClient.GetFileByPath(partialPath);
            Logger.LogTrace("Found result with {Count} matches for {Path}", result?.Count ?? 0, partialPath);

            var file = result?.FirstOrDefault();
            if (file == null)
                return (null, null, null, null);

            var series = file?.SeriesIDs?.FirstOrDefault();
            var seriesId = series?.SeriesID.ID.ToString();
            var episodes = series?.EpisodeIDs?.FirstOrDefault();
            var episodeId = episodes?.ID.ToString();
            if (string.IsNullOrEmpty(seriesId) || string.IsNullOrEmpty(episodeId))
                return (null, null, null, null);

            GroupInfo groupInfo = null;
            if (filterGroupByType.HasValue) {
                groupInfo =  await GetGroupInfoForSeries(seriesId, filterGroupByType.Value);
                if (groupInfo == null)
                    return (null, null, null, null);
            }

            var seriesInfo = await GetSeriesInfo(seriesId);
            if (seriesInfo == null)
                return (null, null, null, null);

            var episodeInfo = await GetEpisodeInfo(episodeId);
            if (episodeInfo == null)
                return (null, null, null, null);

            var fileId = file.ID.ToString();
            var episodeCount = series?.EpisodeIDs?.Count ?? 0;
            var fileInfo = CreateFileInfo(file, fileId, episodeCount);

            // Add pointers for faster lookup.
            EpisodePathToEpisodeIdDictionary.TryAdd(path, episodeId);
            EpisodeIdToEpisodePathDictionary.TryAdd(episodeId, path);
            FilePathToFileIdAndEpisodeCountDictionary.TryAdd(path, (fileId, episodeCount, episodeId, seriesId));
            return (fileInfo, episodeInfo, seriesInfo, groupInfo);
        }

        public FileInfo GetFileInfoSync(string fileId, int episodeCount = 0)
        {
            if (string.IsNullOrEmpty(fileId))
                return null;

            var cacheKey = $"file:{fileId}:{episodeCount}";
            FileInfo info = null;
            if (DataCache.TryGetValue<FileInfo>(cacheKey, out info))
                return info;

            var file = APIClient.GetFile(fileId).GetAwaiter().GetResult();
            return CreateFileInfo(file, fileId, episodeCount);
        }

        public async Task<FileInfo> GetFileInfo(string fileId, int episodeCount = 0)
        {
            if (string.IsNullOrEmpty(fileId))
                return null;

            var cacheKey = $"file:{fileId}:{episodeCount}";
            FileInfo info = null;
            if (DataCache.TryGetValue<FileInfo>(cacheKey, out info))
                return info;

            var file = await APIClient.GetFile(fileId);
            return CreateFileInfo(file, fileId, episodeCount);
        }

        private FileInfo CreateFileInfo(File file, string fileId = null, int episodeCount = 0)
        {
            if (file == null)
                return null;

            if (string.IsNullOrEmpty(fileId))
                fileId = file.ID.ToString();

            var cacheKey = $"file:{fileId}:{episodeCount}";
            FileInfo info = null;
            if (DataCache.TryGetValue<FileInfo>(cacheKey, out info))
                return info;

            Logger.LogTrace("Creating info object for file. (File={FileId})", fileId);
            info = new FileInfo
            {
                Id = fileId,
                Shoko = file,
                ExtraEpisodesCount = episodeCount - 1,
            };
            DataCache.Set<FileInfo>(cacheKey, info, DefaultTimeSpan);
            return info;
        }

        public bool TryGetFileIdForPath(string path, out string fileId, out int episodeCount)
        {
            if (!string.IsNullOrEmpty(path) && FilePathToFileIdAndEpisodeCountDictionary.TryGetValue(path, out var pair)) {
                fileId = pair.Item1;
                episodeCount = pair.Item2;
                return true;
            }

            fileId = null;
            episodeCount = 0;
            return false;
        }

        #endregion
        #region Episode Info

        public EpisodeInfo GetEpisodeInfoSync(string episodeId)
        {
            if (string.IsNullOrEmpty(episodeId))
                return null;
            if (DataCache.TryGetValue<EpisodeInfo>($"episode:{episodeId}", out var info))
                return info;
            return GetEpisodeInfo(episodeId).GetAwaiter().GetResult();
        }

        public async Task<EpisodeInfo> GetEpisodeInfo(string episodeId)
        {
            if (string.IsNullOrEmpty(episodeId))
                return null;
            if (DataCache.TryGetValue<EpisodeInfo>($"episode:{episodeId}", out var info))
                return info;
            var episode = await APIClient.GetEpisode(episodeId);
            return await CreateEpisodeInfo(episode, episodeId);
        }

        private async Task<EpisodeInfo> CreateEpisodeInfo(Episode episode, string episodeId = null)
        {
            if (episode == null)
                return null;
            if (string.IsNullOrEmpty(episodeId))
                episodeId = episode.IDs.ID.ToString();
            var cacheKey = $"episode:{episodeId}";
            EpisodeInfo info = null;
            if (DataCache.TryGetValue<EpisodeInfo>(cacheKey, out info))
                return info;
            Logger.LogTrace("Creating info object for episode {EpisodeName}. (Episode={EpisodeId})", episode.Name, episodeId);
            var aniDB = (await APIClient.GetEpisodeAniDb(episodeId));
            info = new EpisodeInfo
            {
                Id = episodeId,
                ExtraType = Ordering.GetExtraType(aniDB),
                Shoko = episode,
                AniDB = aniDB,
                TvDB = ((await APIClient.GetEpisodeTvDb(episodeId))?.FirstOrDefault()),
            };
            DataCache.Set<EpisodeInfo>(cacheKey, info, DefaultTimeSpan);
            return info;
        }

        public bool TryGetEpisodeIdForPath(string path, out string episodeId)
        {
            if (string.IsNullOrEmpty(path)) {
                episodeId = null;
                return false;
            }
            return EpisodePathToEpisodeIdDictionary.TryGetValue(path, out episodeId);
        }

        public bool TryGetEpisodePathForId(string episodeId, out string path)
        {
            if (string.IsNullOrEmpty(episodeId)) {
                path = null;
                return false;
            }
            return EpisodeIdToEpisodePathDictionary.TryGetValue(episodeId, out path);
        }

        public bool TryGetSeriesIdForEpisodeId(string episodeId, out string seriesId)
        {
            return EpisodeIdToSeriesIdDictionary.TryGetValue(episodeId, out seriesId);
        }

        #endregion
        #region Series Info

        public SeriesInfo GetSeriesInfoByPathSync(string path)
        {
            if (SeriesPathToIdDictionary.ContainsKey(path))
            {
                var seriesId = SeriesPathToIdDictionary[path];
                if (DataCache.TryGetValue<SeriesInfo>( $"series:{seriesId}", out var info))
                    return info;
                return GetSeriesInfo(seriesId).GetAwaiter().GetResult();
            }
            return GetSeriesInfoByPath(path).GetAwaiter().GetResult();
        }

        public async Task<SeriesInfo> GetSeriesInfoByPath(string path)
        {
            var partialPath = StripMediaFolder(path);
            Logger.LogDebug("Looking for series matching {Path}", partialPath);
            string seriesId;
            if (!SeriesPathToIdDictionary.TryGetValue(path, out seriesId))
            {
                var result = await APIClient.GetSeriesPathEndsWith(partialPath);
                Logger.LogTrace("Found result with {Count} matches for {Path}", result?.Count ?? 0, partialPath);
                seriesId = result?.FirstOrDefault()?.IDs?.ID.ToString();

                if (string.IsNullOrEmpty(seriesId))
                    return null;

                SeriesPathToIdDictionary[path] = seriesId;
                SeriesIdToPathDictionary.TryAdd(seriesId, path);
            }

            if (DataCache.TryGetValue<SeriesInfo>( $"series:{seriesId}", out var info))
                return info;

            var series = await APIClient.GetSeries(seriesId);
            return await CreateSeriesInfo(series, seriesId);
        }

        public async Task<SeriesInfo> GetSeriesInfoFromGroup(string groupId, int seasonNumber, Ordering.GroupFilterType filterByType = Ordering.GroupFilterType.Default)
        {
            var groupInfo = await GetGroupInfo(groupId, filterByType);
            if (groupInfo == null)
                return null;
            return groupInfo.GetSeriesInfoBySeasonNumber(seasonNumber);
        }
        public SeriesInfo GetSeriesInfoSync(string seriesId)
        {
            if (string.IsNullOrEmpty(seriesId))
                return null;
            if (DataCache.TryGetValue<SeriesInfo>( $"series:{seriesId}", out var info))
                return info;
            var series = APIClient.GetSeries(seriesId).GetAwaiter().GetResult();
            return CreateSeriesInfo(series, seriesId).GetAwaiter().GetResult();
        }

        public async Task<SeriesInfo> GetSeriesInfo(string seriesId)
        {
            if (string.IsNullOrEmpty(seriesId))
                return null;
            if (DataCache.TryGetValue<SeriesInfo>( $"series:{seriesId}", out var info))
                return info;
            var series = await APIClient.GetSeries(seriesId);
            return await CreateSeriesInfo(series, seriesId);
        }

        public SeriesInfo GetSeriesInfoForEpisodeSync(string episodeId)
        {
            if (EpisodeIdToSeriesIdDictionary.ContainsKey(episodeId)) {
                var seriesId = EpisodeIdToSeriesIdDictionary[episodeId];
                if (DataCache.TryGetValue<SeriesInfo>($"series:{seriesId}", out var info))
                    return info;

                return GetSeriesInfo(seriesId).GetAwaiter().GetResult();
            }

            return GetSeriesInfoForEpisode(episodeId).GetAwaiter().GetResult();
        }

        public async Task<SeriesInfo> GetSeriesInfoForEpisode(string episodeId)
        {
            string seriesId;
            if (EpisodeIdToSeriesIdDictionary.ContainsKey(episodeId)) {
                seriesId = EpisodeIdToSeriesIdDictionary[episodeId];
            }
            else {
                var series = await APIClient.GetSeriesFromEpisode(episodeId);
                if (series == null)
                    return null;
                seriesId = series.IDs.ID.ToString();
            }

            return await GetSeriesInfo(seriesId);
        }

        public bool TryGetSeriesIdForPath(string path, out string seriesId)
        {
            if (string.IsNullOrEmpty(path)) {
                seriesId = null;
                return false;
            }
            return SeriesPathToIdDictionary.TryGetValue(path, out seriesId);
        }

        public bool TryGetSeriesPathForId(string seriesId, out string path)
        {
            if (string.IsNullOrEmpty(seriesId)) {
                path = null;
                return false;
            }
            return SeriesIdToPathDictionary.TryGetValue(seriesId, out path);
        }

        public bool TryGetGroupIdForSeriesId(string seriesId, out string groupId)
        {
            return SeriesIdToGroupIdDictionary.TryGetValue(seriesId, out groupId);
        }

        private async Task<SeriesInfo> CreateSeriesInfo(Series series, string seriesId = null)
        {
            if (series == null)
                return null;

            if (string.IsNullOrEmpty(seriesId))
                seriesId = series.IDs.ID.ToString();

            SeriesInfo info = null;
            var cacheKey = $"series:{seriesId}";
            if (DataCache.TryGetValue<SeriesInfo>(cacheKey, out info))
                return info;
            Logger.LogTrace("Creating info object for series {SeriesName}. (Series={SeriesId})", series.Name, seriesId);

            var aniDb = await APIClient.GetSeriesAniDB(seriesId);
            var tvDbId = series.IDs.TvDB?.FirstOrDefault();
            var tags = await GetTags(seriesId);
            var genres = await GetGenresForSeries(seriesId);
            var cast = await APIClient.GetSeriesCast(seriesId);

            var studios = cast.Where(r => r.RoleName == Role.CreatorRoleType.Studio).Select(r => r.Staff.Name).ToArray();
            var staff = cast.Select(RoleToPersonInfo).OfType<PersonInfo>().ToArray();
            var specialsAnchorDictionary = new Dictionary<EpisodeInfo, EpisodeInfo>();
            var specialsList = new List<EpisodeInfo>();
            var episodesList = new List<EpisodeInfo>();
            var extrasList = new List<EpisodeInfo>();
            var altEpisodesList = new List<EpisodeInfo>();
            var othersList = new List<EpisodeInfo>();

            // The episode list is ordered by air date
            var allEpisodesList = APIClient.GetEpisodesFromSeries(seriesId)
                .ContinueWith(task => Task.WhenAll(task.Result.Select(e => CreateEpisodeInfo(e))))
                .Unwrap()
                .GetAwaiter()
                .GetResult()
                .Where(e => e != null && e.Shoko != null && e.AniDB != null)
                .OrderBy(e => e.AniDB.AirDate)
                .ToList();

            // Iterate over the episodes once and store some values for later use.
            for (int index = 0, lastNormalEpisode = 0; index < allEpisodesList.Count; index++) {
                var episode = allEpisodesList[index];
                EpisodeIdToSeriesIdDictionary[episode.Id] = seriesId;
                switch (episode.AniDB.Type) {
                    case EpisodeType.Normal:
                        episodesList.Add(episode);
                        lastNormalEpisode = index;
                        break;
                    case EpisodeType.Other:
                        othersList.Add(episode);
                        break;
                    case EpisodeType.Unknown:
                        altEpisodesList.Add(episode);
                        break;
                    default:
                        if (episode.ExtraType != null)
                            extrasList.Add(episode);
                        else if (episode.AniDB.Type == EpisodeType.Special) {
                            specialsList.Add(episode);
                            var previousEpisode = allEpisodesList
                                .GetRange(lastNormalEpisode, index - lastNormalEpisode)
                                .FirstOrDefault(e => e.AniDB.Type == EpisodeType.Normal);
                            if (previousEpisode != null)
                                specialsAnchorDictionary[episode] = previousEpisode;
                        }
                        break;
                }
            }

            // While the filtered specials list is ordered by episode number
            specialsList = specialsList
                .OrderBy(e => e.AniDB.EpisodeNumber)
                .ToList();

            info = new SeriesInfo {
                Id = seriesId,
                Shoko = series,
                AniDB = aniDb,
                TvDBId = tvDbId != 0 ? tvDbId.ToString() : null,
                TvDB = tvDbId != 0 ? (await APIClient.GetSeriesTvDB(seriesId)).FirstOrDefault() : null,
                Tags = tags,
                Genres = genres,
                Studios = studios,
                Staff = staff,
                RawEpisodeList = allEpisodesList,
                EpisodeList = episodesList,
                AlternateEpisodesList = altEpisodesList,
                OthersList = othersList,
                ExtrasList = extrasList,
                SpesialsAnchors = specialsAnchorDictionary,
                SpecialsList = specialsList,
            };

            DataCache.Set<SeriesInfo>(cacheKey, info, DefaultTimeSpan);
            return info;
        }

        #endregion
        #region Group Info

        public GroupInfo GetGroupInfoByPathSync(string path, Ordering.GroupFilterType filterByType = Ordering.GroupFilterType.Default)
        {
            return GetGroupInfoByPath(path, filterByType).GetAwaiter().GetResult();
        }

        public async Task<GroupInfo> GetGroupInfoByPath(string path, Ordering.GroupFilterType filterByType = Ordering.GroupFilterType.Default)
        {
            var partialPath = StripMediaFolder(path);
            Logger.LogDebug("Looking for group matching {Path}", partialPath);

            string seriesId;
            if (SeriesPathToIdDictionary.TryGetValue(path, out seriesId))
            {
                if (SeriesIdToGroupIdDictionary.TryGetValue(seriesId, out var groupId)) {
                    if (DataCache.TryGetValue<GroupInfo>($"group:{filterByType}:{groupId}", out var info))
                        return info;

                    return await GetGroupInfo(groupId, filterByType);
                }
            }
            else
            {
                var result = await APIClient.GetSeriesPathEndsWith(partialPath);
                Logger.LogTrace("Found result with {Count} matches for {Path}", result?.Count ?? 0, partialPath);
                seriesId = result?.FirstOrDefault()?.IDs?.ID.ToString();

                if (string.IsNullOrEmpty(seriesId))
                    return null;

                SeriesPathToIdDictionary[path] = seriesId;
                SeriesIdToPathDictionary.TryAdd(seriesId, path);
            }

            return await GetGroupInfoForSeries(seriesId, filterByType);
        }

        public GroupInfo GetGroupInfoSync(string groupId, Ordering.GroupFilterType filterByType = Ordering.GroupFilterType.Default)
        {
            if (!string.IsNullOrEmpty(groupId) && DataCache.TryGetValue<GroupInfo>($"group:{filterByType}:{groupId}", out var info))
                return info;

            return GetGroupInfo(groupId, filterByType).GetAwaiter().GetResult();
        }

        public async Task<GroupInfo> GetGroupInfo(string groupId, Ordering.GroupFilterType filterByType = Ordering.GroupFilterType.Default)
        {
            if (string.IsNullOrEmpty(groupId))
                return null;

            if (DataCache.TryGetValue<GroupInfo>($"group:{filterByType}:{groupId}", out var info))
                return info;

            var group = await APIClient.GetGroup(groupId);
            return await CreateGroupInfo(group, groupId, filterByType);
        }

        public GroupInfo GetGroupInfoForSeriesSync(string seriesId, Ordering.GroupFilterType filterByType = Ordering.GroupFilterType.Default)
        {
            if (string.IsNullOrEmpty(seriesId))
                return null;

            if (SeriesIdToGroupIdDictionary.TryGetValue(seriesId, out var groupId)) {
                if (DataCache.TryGetValue<GroupInfo>($"group:{filterByType}:{groupId}", out var info))
                    return info;

                return GetGroupInfo(groupId, filterByType).GetAwaiter().GetResult();
            }

            return GetGroupInfoForSeries(seriesId, filterByType).GetAwaiter().GetResult();
        }

        public async Task<GroupInfo> GetGroupInfoForSeries(string seriesId, Ordering.GroupFilterType filterByType = Ordering.GroupFilterType.Default)
        {
            if (string.IsNullOrEmpty(seriesId))
                return null;

            if (!SeriesIdToGroupIdDictionary.TryGetValue(seriesId, out var groupId)) {
                var group = await APIClient.GetGroupFromSeries(seriesId);
                if (group == null)
                    return null;

                groupId = group.IDs.ID.ToString();
                if (DataCache.TryGetValue<GroupInfo>($"group:{filterByType}:{groupId}", out var groupInfo))
                    return groupInfo;

                return await CreateGroupInfo(group, groupId, filterByType);
            }

            return await GetGroupInfo(groupId, filterByType);
        }

        private async Task<GroupInfo> CreateGroupInfo(Group group, string groupId, Ordering.GroupFilterType filterByType)
        {
            if (group == null)
                return null;

            if (string.IsNullOrEmpty(groupId))
                groupId = group.IDs.ID.ToString();

            var cacheKey = $"group:{filterByType}:{groupId}";
            GroupInfo groupInfo = null;
            if (DataCache.TryGetValue<GroupInfo>(cacheKey, out groupInfo))
                return groupInfo;
            Logger.LogTrace("Creating info object for group {GroupName}. (Group={GroupId})", group.Name, groupId);

            var seriesList = (await APIClient.GetSeriesInGroup(groupId)
                .ContinueWith(task => Task.WhenAll(task.Result.Select(s => CreateSeriesInfo(s))))
                .Unwrap())
                .Where(s => s != null)
                .ToList();
            if (seriesList != null && seriesList.Count > 0)  switch (filterByType) {
                default:
                    break;
                case Ordering.GroupFilterType.Movies:
                    seriesList = seriesList.Where(s => s.AniDB.Type == SeriesType.Movie).ToList();
                    break;
                case Ordering.GroupFilterType.Others:
                    seriesList = seriesList.Where(s => s.AniDB.Type != SeriesType.Movie).ToList();
                    break;
            }

            // Return ealty if no series matched the filter or if the list was empty.
            if (seriesList == null || seriesList.Count == 0) {
                Logger.LogWarning("Creating an empty group info for filter {Filter}! (Group={GroupId})", filterByType.ToString(), groupId);
                groupInfo = new GroupInfo {
                    Id = groupId,
                    Shoko = group,
                    Tags = new string[0],
                    Genres = new string[0],
                    Studios = new string[0],
                    SeriesList = (seriesList ?? new List<SeriesInfo>()),
                    SeasonNumberBaseDictionary = (new Dictionary<SeriesInfo, int>()),
                    SeasonOrderDictionary = (new Dictionary<int, SeriesInfo>()),
                    DefaultSeries = null,
                    DefaultSeriesIndex = -1,
                };
                DataCache.Set<GroupInfo>(cacheKey, groupInfo, DefaultTimeSpan);
                return groupInfo;
            }

            // Order series list
            var orderingType = filterByType == Ordering.GroupFilterType.Movies ? Plugin.Instance.Configuration.MovieOrdering : Plugin.Instance.Configuration.SeasonOrdering;
            switch (orderingType) {
                case Ordering.OrderType.Default:
                    break;
                case Ordering.OrderType.ReleaseDate:
                    seriesList = seriesList.OrderBy(s => s?.AniDB?.AirDate ?? System.DateTime.MaxValue).ToList();
                    break;
                // Should not be selectable unless a user fiddles with DevTools in the browser to select the option.
                case Ordering.OrderType.Chronological:
                    throw new System.Exception("Not implemented yet");
            }

            // Select the targeted id if a group spesify a default series.
            int foundIndex = -1;
            int targetId = (group.IDs.DefaultSeries ?? 0);
            if (targetId != 0)
                foundIndex = seriesList.FindIndex(s => s.Shoko.IDs.ID == targetId);
            // Else select the default series as first-to-be-released.
            else switch (orderingType) {
                // The list is already sorted by release date, so just return the first index.
                case Ordering.OrderType.ReleaseDate:
                    foundIndex = 0;
                    break;
                // We don't know how Shoko may have sorted it, so just find the earliest series
                case Ordering.OrderType.Default:
                // We can't be sure that the the series in the list was _released_ chronologically, so find the earliest series, and use that as a base.
                case Ordering.OrderType.Chronological: {
                    var earliestSeries = seriesList.Aggregate((cur, nxt) => (cur == null || (nxt?.AniDB.AirDate ?? System.DateTime.MaxValue) < (cur.AniDB.AirDate ?? System.DateTime.MaxValue)) ? nxt : cur);
                    foundIndex = seriesList.FindIndex(s => s == earliestSeries);
                    break;
                }
            }

            // Throw if we can't get a base point for seasons.
            if (foundIndex == -1)
                throw new System.Exception("Unable to get a base-point for seasions withing the group");

            var seasonOrderDictionary = new Dictionary<int, SeriesInfo>();
            var seasonNumberBaseDictionary = new Dictionary<SeriesInfo, int>();
            var positiveSeasonNumber = 1;
            var negativeSeasonNumber = -1;
            foreach (var (seriesInfo, index) in seriesList.Select((s, i) => (s, i))) {
                int seasonNumber;
                var offset = 0;
                if (seriesInfo.AlternateEpisodesList.Count > 0)
                    offset++;
                if (seriesInfo.OthersList.Count > 0)
                    offset++;

                // Series before the default series get a negative season number
                if (index < foundIndex) {
                    seasonNumber = negativeSeasonNumber;
                    negativeSeasonNumber -= offset + 1;
                }
                else {
                    seasonNumber = positiveSeasonNumber;
                    positiveSeasonNumber += offset + 1;
                }

                seasonNumberBaseDictionary.Add(seriesInfo, seasonNumber);
                seasonOrderDictionary.Add(seasonNumber, seriesInfo);
                for (var i = 0; i < offset; i++)
                    seasonOrderDictionary.Add(seasonNumber + (index < foundIndex ? -(i + 1) :  (i + 1)), seriesInfo);
            }

            groupInfo = new GroupInfo {
                Id = groupId,
                Shoko = group,
                Tags = seriesList.SelectMany(s => s.Tags).Distinct().ToArray(),
                Genres = seriesList.SelectMany(s => s.Genres).Distinct().ToArray(),
                Studios = seriesList.SelectMany(s => s.Studios).Distinct().ToArray(),
                SeriesList = seriesList,
                SeasonNumberBaseDictionary = seasonNumberBaseDictionary,
                SeasonOrderDictionary = seasonOrderDictionary,
                DefaultSeries = seriesList[foundIndex],
                DefaultSeriesIndex = foundIndex,
            };
            foreach (var series in seriesList)
                SeriesIdToGroupIdDictionary[series.Id] = groupId;
            DataCache.Set<GroupInfo>(cacheKey, groupInfo, DefaultTimeSpan);
            return groupInfo;
        }

        #endregion
        #region Post Process Library Changes

        public Task PostProcess(IProgress<double> progress, CancellationToken token)
        {
            Clear();
            return Task.CompletedTask;
        }

        #endregion
    }
}
