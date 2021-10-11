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
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration;

namespace Shokofin.Sync
{
    public class UserDataSyncManager
    {

        private readonly IUserDataManager UserDataManager;

        private readonly ILibraryManager LibraryManager;

        private readonly ISessionManager SessionManager;

        private readonly ILogger<UserDataSyncManager> Logger;

        private readonly ShokoAPIClient APIClient;

        private readonly IIdLookup Lookup;

        public UserDataSyncManager(IUserDataManager userDataManager, ILibraryManager libraryManager, ISessionManager sessionManager, ILogger<UserDataSyncManager> logger, ShokoAPIClient apiClient, IIdLookup lookup)
        {
            UserDataManager = userDataManager;
            LibraryManager = libraryManager;
            SessionManager = sessionManager;
            Logger = logger;
            APIClient = apiClient;
            Lookup = lookup;

            SessionManager.SessionStarted += OnSessionStarted;
            SessionManager.SessionEnded += OnSessionEnded;
            UserDataManager.UserDataSaved += OnUserDataSaved;
            LibraryManager.ItemAdded += OnItemAddedOrUpdated;
            LibraryManager.ItemUpdated += OnItemAddedOrUpdated;
        }

        public void Dispose()
        {
            SessionManager.SessionStarted -= OnSessionStarted;
            SessionManager.SessionEnded -= OnSessionEnded;
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
            public SessionInfo Session;
            public long Ticks;
            public bool SentPaused;
        }

        private readonly ConcurrentDictionary<Guid, SeesionMetadata> ActiveSessions = new ConcurrentDictionary<Guid, SeesionMetadata>();

        public void OnSessionStarted(object sender, SessionEventArgs e)
        {
            if (TryGetUserConfiguration(e.SessionInfo.UserId, out var userConfig) && userConfig.SyncUserDataUnderPlayback) {
                var sessionMetadata = new SeesionMetadata {
                    ItemId = Guid.Empty,
                    Session = e.SessionInfo,
                    FileId = null,
                    SentPaused = false,
                    Ticks = 0,
                };
                ActiveSessions.TryAdd(e.SessionInfo.UserId, sessionMetadata);
            }
            foreach (var user in e.SessionInfo.AdditionalUsers) {
                if (TryGetUserConfiguration(e.SessionInfo.UserId, out userConfig) && userConfig.SyncUserDataUnderPlayback) {
                    var sessionMetadata = new SeesionMetadata {
                        ItemId = Guid.Empty,
                        Session = e.SessionInfo,
                        FileId = null,
                        SentPaused = false,
                        Ticks = 0,
                    };
                    ActiveSessions.TryAdd(user.UserId, sessionMetadata);
                }
            }
        }

        public void OnSessionEnded(object sender, SessionEventArgs e)
        {
            ActiveSessions.TryRemove(e.SessionInfo.UserId, out var sessionMetadata);
            foreach (var user in e.SessionInfo.AdditionalUsers) {
                ActiveSessions.TryRemove(user.UserId, out sessionMetadata);
            }
        }

        public async void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            try {

                if (e == null || e.Item == null || Guid.Equals(e.UserId, Guid.Empty) || e.UserData == null)
                    return;

                if (e.SaveReason == UserDataSaveReason.UpdateUserRating) {
                    OnUserRatingSaved(sender, e);
                    return;
                }

                if (!(
                        (e.Item is Movie || e.Item is Episode) &&
                        TryGetUserConfiguration(e.UserId, out var userConfig) &&
                        Lookup.TryGetFileIdFor(e.Item, out var fileId) &&
                        Lookup.TryGetEpisodeIdFor(e.Item, out var episodeId)
                    ))
                    return;

                var itemId = e.Item.Id;
                var userData = e.UserData;
                var config = Plugin.Instance.Configuration;
                bool success = false;
                switch (e.SaveReason) {
                    // case UserDataSaveReason.PlaybackStart: // The progress event is sent at the same time, so this event is not needed.
                    case UserDataSaveReason.PlaybackProgress: {
                        // If a session can't be found or created then throw an error.
                        if (!ActiveSessions.TryGetValue(e.UserId, out var sessionMetadata))
                            return;

                        // The active video changed, so send a start event.
                        if (!Guid.Equals(sessionMetadata.ItemId, itemId)) {
                            sessionMetadata.ItemId = e.Item.Id;
                            sessionMetadata.FileId = fileId;
                            sessionMetadata.Ticks = userData.PlaybackPositionTicks;
                            sessionMetadata.SentPaused = false;

                            Logger.LogInformation("Playback has started. (File={FileId})", fileId);
                            success = await APIClient.ScrobbleFile(fileId, episodeId, "play", sessionMetadata.Ticks, userConfig.Token).ConfigureAwait(false);
                        }
                        else {
                            long ticks = sessionMetadata.Session.PlayState.PositionTicks ?? userData.PlaybackPositionTicks;
                            // We received an event, but the position didn't change, so the playback is most likely paused.
                            if (sessionMetadata.Session.PlayState?.IsPaused ?? false) {
                                if (sessionMetadata.SentPaused)
                                    return;

                                sessionMetadata.SentPaused = true;

                                Logger.LogInformation("Playback was paused. (File={FileId})", fileId);
                                success = await APIClient.ScrobbleFile(fileId, episodeId, "pause", sessionMetadata.Ticks, userConfig.Token).ConfigureAwait(false);
                            }
                            // The playback was resumed.
                            else if (sessionMetadata.SentPaused) {
                                sessionMetadata.Ticks = ticks;
                                sessionMetadata.SentPaused = false;

                                Logger.LogInformation("Playback was resumed. (File={FileId})", fileId);
                                success = await APIClient.ScrobbleFile(fileId, episodeId, "resume", sessionMetadata.Ticks, userConfig.Token).ConfigureAwait(false);
                            }
                            // Scrobble.
                            else {
                                sessionMetadata.Ticks = ticks;

                                Logger.LogInformation("Scrobbled during playback. (File={FileId})", fileId);
                                success = await APIClient.ScrobbleFile(fileId, episodeId, "scrobble", sessionMetadata.Ticks, userConfig.Token).ConfigureAwait(false);
                            }
                        }
                        break;
                    }
                    case UserDataSaveReason.PlaybackFinished: {
                        if (!userConfig.SyncUserDataAfterPlayback)
                            return;

                        if (ActiveSessions.TryGetValue(e.UserId, out var sessionMetadata) && sessionMetadata.ItemId == e.Item.Id) {
                            sessionMetadata.ItemId = Guid.Empty;
                            sessionMetadata.FileId = null;
                            sessionMetadata.Ticks = 0;
                            sessionMetadata.SentPaused = false;
                        }

                        Logger.LogInformation("Playback has ended. (File={FileId})", fileId);
                        success = await APIClient.ScrobbleFile(fileId, episodeId, "stop", userData.PlaybackPositionTicks, userData.Played, userConfig.Token).ConfigureAwait(false);
                        break;
                    }
                    case UserDataSaveReason.TogglePlayed:
                        Logger.LogInformation("Scrobbled when toggled. (File={FileId})", fileId);
                        success = await APIClient.ScrobbleFile(fileId, episodeId, "user-interaction", userData.PlaybackPositionTicks, userData.Played, userConfig.Token).ConfigureAwait(false);
                        break;
                }
                if (success) {
                    Logger.LogInformation("Successfully synced watch state with Shoko. (File={FileId})", fileId);
                }
                else {
                    Logger.LogInformation("Failed to sync watch state with Shoko. (File={FileId})", fileId);
                }
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Threw unexpectedly; {ErrorMessage}", ex.Message);
                return;
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

        public async Task ScanAndSync(SyncDirection direction, IProgress<double> progress, CancellationToken cancellationToken)
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
                    await SyncVideo(video, userConfig, null, direction, fileId, episodeId).ConfigureAwait(false);

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
