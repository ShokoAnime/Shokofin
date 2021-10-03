using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration;

namespace Shokofin
{

    public class UserDataSyncManager
    {
        /// <summary>
        /// Determines if we should push or pull the data.
        /// </summary>
        [Flags]
        public enum SyncDirection {
            /// <summary>
            /// Import data from Shoko.
            /// </summary>
            Import = 1,
            /// <summary>
            /// Export data to Shoko.
            /// </summary>
            Export = 2,
            /// <summary>
            /// Sync data with Shoko and only keep the latest data.
            /// <br/>
            /// This will conditionally import or export the data as needed.
            /// </summary>
            Sync = 3,
        }

        private readonly IUserDataManager UserDataManager;

        private readonly ILibraryManager LibraryManager;

        private readonly ILogger<UserDataSyncManager> Logger;

        private readonly ShokoAPIClient APIClient;

        private readonly IIdLookup Lookup;

        public UserDataSyncManager(IUserDataManager userDataManager, ILibraryManager libraryManager, ILogger<UserDataSyncManager> logger, ShokoAPIClient apiClient, IIdLookup lookup)
        {
            UserDataManager = userDataManager;
            LibraryManager = libraryManager;
            Logger = logger;
            APIClient = apiClient;
            Lookup = lookup;

            UserDataManager.UserDataSaved += OnUserDataSaved;
            LibraryManager.ItemAdded += OnItemAddedOrUpdated;
            LibraryManager.ItemUpdated += OnItemAddedOrUpdated;
        }

        public void Dispose()
        {
            UserDataManager.UserDataSaved -= OnUserDataSaved;
            LibraryManager.ItemAdded -= OnItemAddedOrUpdated;
            LibraryManager.ItemUpdated -= OnItemAddedOrUpdated;
        }

        private bool TryGetUserConfiguration(Guid userId, out UserConfiguration config)
        {
            config = Plugin.Instance.Configuration.UserList.FirstOrDefault(c => c.UserId == userId && c.EnableSynchronization);
            return config != null;
        }

        #region Export/Scrobble

        internal class SeesionMetadata {
            public Guid ItemId;
            public string FileId;
            public long Ticks;
            public bool SentPaused;
        }

        private readonly ConcurrentDictionary<Guid, SeesionMetadata> ActiveSessions = new ConcurrentDictionary<Guid, SeesionMetadata>();

        public async void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            if (e == null || e.Item == null || Guid.Equals(e.UserId, Guid.Empty) || e.UserData == null)
                return;

            if (e.SaveReason == UserDataSaveReason.UpdateUserRating) {
                OnUserRatingSaved(sender, e);
                return;
            }

            if (!(
                    (e.Item is Movie || e.Item is Episode) &&
                    TryGetUserConfiguration(e.UserId, out var userConfig) &&
                    Lookup.TryGetFileIdFor(e.Item, out var fileId)
                ))
                return;

            var itemId = e.Item.Id;
            var userData = e.UserData;
            var config = Plugin.Instance.Configuration;
            bool success = false;
            switch (e.SaveReason) {
                case UserDataSaveReason.PlaybackStart:
                case UserDataSaveReason.PlaybackProgress: {
                    if (!userConfig.SyncUserDataUnderPlayback)
                        return;

                    // If a session can't be found or created then throw an error.
                    if (!ActiveSessions.TryGetValue(e.UserId, out var sessionInfo) &&
                        !ActiveSessions.TryAdd(e.UserId, sessionInfo = new SeesionMetadata { ItemId = Guid.Empty, FileId = null, Ticks = 0, SentPaused = false }))
                        throw new Exception("Unable to create session data.");

                    // The active video changed, so send a start event.
                    if (!Guid.Equals(sessionInfo.ItemId, itemId)) {
                        sessionInfo.ItemId = e.Item.Id;
                        sessionInfo.FileId = fileId;
                        sessionInfo.Ticks = userData.PlaybackPositionTicks;
                        sessionInfo.SentPaused = false;

                        Logger.LogInformation("Playback has started. (File={FileId})", fileId);
                        success = await APIClient.ScrobbleFile(fileId, "play", sessionInfo.Ticks, userConfig.Token).ConfigureAwait(false);
                    }
                    // We received an event, but the position didn't change, so the playback is most likely paused.
                    else if (userData.PlaybackPositionTicks == sessionInfo.Ticks) {
                        if (sessionInfo.SentPaused)
                            return;

                        sessionInfo.SentPaused = true;

                        Logger.LogInformation("Playback was paused. (File={FileId})", fileId);
                        success = await APIClient.ScrobbleFile(fileId, "pause", sessionInfo.Ticks, userConfig.Token).ConfigureAwait(false);
                    }
                    // The playback was resumed.
                    else if (sessionInfo.SentPaused) {
                        sessionInfo.Ticks = userData.PlaybackPositionTicks;
                        sessionInfo.SentPaused = false;

                        Logger.LogInformation("Playback was resumed. (File={FileId})", fileId);
                        success = await APIClient.ScrobbleFile(fileId, "resume", sessionInfo.Ticks, userConfig.Token).ConfigureAwait(false);
                    }
                    // Scrobble.
                    else {
                        sessionInfo.Ticks = userData.PlaybackPositionTicks;

                        Logger.LogInformation("Scrobbled during playback. (File={FileId})", fileId);
                        success = await APIClient.ScrobbleFile(fileId, "scrobble", sessionInfo.Ticks, userConfig.Token).ConfigureAwait(false);
                    }
                    break;
                }
                case UserDataSaveReason.PlaybackFinished:
                    if (!userConfig.SyncUserDataAfterPlayback)
                        return;

                    // Remove the session metadata if the watch session was ended.
                    if (userConfig.SyncUserDataUnderPlayback) {
                        if (ActiveSessions.TryGetValue(e.UserId, out var sessionInfo) && sessionInfo.ItemId == itemId && !ActiveSessions.TryRemove(e.UserId, out sessionInfo))
                            Logger.LogWarning("Unable to remove session metadata for last session. (File={FileId})", fileId);
                    }

                    Logger.LogInformation("Playback has ended. (File={FileId})", fileId);
                    success = await APIClient.ScrobbleFile(fileId, "stop", userData.PlaybackPositionTicks, userData.Played, userConfig.Token).ConfigureAwait(false);
                    break;
                case UserDataSaveReason.TogglePlayed:
                    Logger.LogInformation("Scrobbled when toggled. (File={FileId})", fileId);
                    success = await APIClient.ScrobbleFile(fileId, "toggle-played", userData.PlaybackPositionTicks, userData.Played, userConfig.Token).ConfigureAwait(false);
                    break;
            }
            if (success) {
                Logger.LogInformation("Successfully synced watch state with Shoko. (File={FileId})", fileId);
            }
            else {
                Logger.LogInformation("Failed to sync watch state with Shoko. (File={FileId})", fileId);
            }
        }

        // Updates to favotite state and/or user data.
        private void OnUserRatingSaved(object sender, UserDataSaveEventArgs e)
        {
            if (!TryGetUserConfiguration(e.UserId, out var userConfig))
                return;

            var userData = e.UserData;
            var config = Plugin.Instance.Configuration;
            switch (e.Item) {
                case Episode:
                case Movie: {
                    var video = e.Item as Video;
                    if (!Lookup.TryGetEpisodeIdFor(video, out var episodeId))
                        return;

                    SyncVideo(video, userConfig, userData, SyncDirection.Export, episodeId).ConfigureAwait(false);
                    break;
                }
                case Season season: {
                    if (!Lookup.TryGetSeriesIdFor(season, out var seriesId))
                        return;

                    SyncSeason(season, userConfig, userData, SyncDirection.Export, seriesId).ConfigureAwait(false);
                    break;
                }
                case Series series: {
                    if (!Lookup.TryGetSeriesIdFor(series, out var seriesId))
                        return;

                    SyncSeries(series, userConfig, userData, SyncDirection.Export, seriesId).ConfigureAwait(false);
                    break;
                }
            }
        }

        #endregion
        #region Import/Sync

        public async Task ScanAndSync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var enabledUsers = Plugin.Instance.Configuration.UserList.Where(c => c.EnableSynchronization).ToList();
            if (enabledUsers.Count == 0) {
                progress.Report(100);
                return;
            }

            var videos = LibraryManager.GetItemList(new InternalItemsQuery {
                MediaTypes = new[] { MediaType.Video },
                IsFolder = false,
                Recursive = true,
                DtoOptions = new DtoOptions(false) {
                    EnableImages = false
                },
                SourceTypes = new SourceType[] { SourceType.Library },
                HasChapterImages = false,
                IsVirtualItem = false,
            })
                .OfType<Video>()
                .ToList();

            var numComplete = 0;
            var numTotal = videos.Count * enabledUsers.Count;
            foreach (var video in videos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!(Lookup.TryGetFileIdFor(video, out var fileId) && Lookup.TryGetEpisodeIdFor(video, out var episodeId)))
                    continue;

                foreach (var userConfig in enabledUsers) {
                    await SyncVideo(video, userConfig, null, SyncDirection.Sync, fileId, episodeId).ConfigureAwait(false);

                    numComplete++;
                    double percent = numComplete;
                    percent /= numTotal;

                    progress.Report(percent * 100);
                }
            }
            progress.Report(100);
        }

        public void OnItemAddedOrUpdated(object sender, ItemChangeEventArgs e)
        {
            if (e == null || e.Item == null || e.Parent == null || !(e.UpdateReason.HasFlag(ItemUpdateType.MetadataImport) || e.UpdateReason.HasFlag(ItemUpdateType.MetadataDownload)))
                return;

            switch (e.Item) {
                case Video video: {
                    if (!(Lookup.TryGetFileIdFor(video, out var fileId) && Lookup.TryGetEpisodeIdFor(video, out var episodeId)))
                        return;

                    foreach (var userConfig in Plugin.Instance.Configuration.UserList) {
                        if (!userConfig.EnableSynchronization)
                            continue;

                        if (!userConfig.SyncUserDataOnImport)
                            continue;

                        SyncVideo(video, userConfig, null, SyncDirection.Import, fileId, episodeId).ConfigureAwait(false);
                    }
                    break;
                }
                case Season season: {
                    if (!Lookup.TryGetSeriesIdFor(season, out var seriesId))
                        return;

                    foreach (var userConfig in Plugin.Instance.Configuration.UserList) {
                        if (!userConfig.EnableSynchronization)
                            continue;

                        if (!userConfig.SyncUserDataOnImport)
                            continue;

                        SyncSeason(season, userConfig, null, SyncDirection.Import, seriesId).ConfigureAwait(false);
                    }
                    break;
                }
                case Series series: {
                    if (!Lookup.TryGetSeriesIdFor(series, out var seriesId))
                        return;

                    foreach (var userConfig in Plugin.Instance.Configuration.UserList) {
                        if (!userConfig.EnableSynchronization)
                            continue;

                        if (!userConfig.SyncUserDataOnImport)
                            continue;

                        SyncSeries(series, userConfig, null, SyncDirection.Import, seriesId).ConfigureAwait(false);
                    }
                    break;
                }
            }

        }

        #endregion

        private Task SyncSeries(Series series, UserConfiguration userConfig, UserItemData userData, SyncDirection direction, string seriesId)
        {
            // Try to load the user-data if it was not provided
            if (userData == null)
                userData = UserDataManager.GetUserData(userConfig.UserId, series);
            // Create some new user-data if none exists.
            if (userData == null)
                userData = new UserItemData {
                    UserId = userConfig.UserId,
                    Key = series.GetUserDataKeys()[0],
                };

            Logger.LogDebug("TODO; {SyncDirection} user data for Series {SeriesName}. (Series={SeriesId})", direction.ToString(), series.Name, seriesId);

            return Task.CompletedTask;
        }

        private Task SyncSeason(Season season, UserConfiguration userConfig, UserItemData userData, SyncDirection direction, string seriesId)
        {
            // Try to load the user-data if it was not provided
            if (userData == null)
                userData = UserDataManager.GetUserData(userConfig.UserId, season);
            // Create some new user-data if none exists.
            if (userData == null)
                userData = new UserItemData {
                    UserId = userConfig.UserId,
                    Key = season.GetUserDataKeys()[0],
                };

            Logger.LogDebug("TODO; {SyncDirection} user data for Season {SeasonNumber} in Series {SeriesName}. (Series={SeriesId})", direction.ToString(), season.IndexNumber, season.SeriesName, seriesId);

            return Task.CompletedTask;
        }

        private Task SyncVideo(Video video, UserConfiguration userConfig, UserItemData userData, SyncDirection direction, string episodeId)
        {
            // Try to load the user-data if it was not provided
            if (userData == null)
                userData = UserDataManager.GetUserData(userConfig.UserId, video);
            // Create some new user-data if none exists.
            if (userData == null)
                userData = new UserItemData {
                    UserId = userConfig.UserId,
                    Key = video.GetUserDataKeys()[0],
                    LastPlayedDate = null,
                };

            // var remoteUserData = await APIClient.GetFileUserData(fileId, userConfig.Token);
            // if (remoteUserData == null)
            //     return;

            Logger.LogDebug("TODO; {SyncDirection} user data for video {VideoName}. (Episode={EpisodeId})", direction.ToString(), video.Name, episodeId);

            return Task.CompletedTask;
        }

        private Task SyncVideo(Video video, UserConfiguration userConfig, UserItemData userData, SyncDirection direction, string fileId, string episodeId)
        {
            // Try to load the user-data if it was not provided
            if (userData == null)
                userData = UserDataManager.GetUserData(userConfig.UserId, video);
            // Create some new user-data if none exists.
            if (userData == null)
                userData = new UserItemData {
                    UserId = userConfig.UserId,
                    Key = video.GetUserDataKeys()[0],
                    LastPlayedDate = null,
                };

            // var remoteUserData = await APIClient.GetFileUserData(fileId, userConfig.Token);
            // if (remoteUserData == null)
            //     return;

            Logger.LogDebug("TODO; {SyncDirection} user data for video {VideoName}. (File={FileId},Episode={EpisodeId})", direction.ToString(), video.Name, fileId, episodeId);

            return Task.CompletedTask;
        }
    }
}
