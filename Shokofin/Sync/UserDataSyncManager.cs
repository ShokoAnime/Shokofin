using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
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

using UserStats = Shokofin.API.Models.File.UserStats;

namespace Shokofin.Sync;

public class UserDataSyncManager
{

    private readonly IUserDataManager UserDataManager;

    private readonly IUserManager UserManager;

    private readonly ILibraryManager LibraryManager;

    private readonly ISessionManager SessionManager;

    private readonly ILogger<UserDataSyncManager> Logger;

    private readonly ShokoAPIClient APIClient;

    private readonly IIdLookup Lookup;

    public UserDataSyncManager(IUserDataManager userDataManager, IUserManager userManager, ILibraryManager libraryManager, ISessionManager sessionManager, ILogger<UserDataSyncManager> logger, ShokoAPIClient apiClient, IIdLookup lookup)
    {
        UserDataManager = userDataManager;
        UserManager = userManager;
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

    private static bool TryGetUserConfiguration(Guid userId, out UserConfiguration? config)
    {
        config = Plugin.Instance.Configuration.UserList.FirstOrDefault(c => c.UserId == userId && c.EnableSynchronization);
        return config != null;
    }

    #region Export/Scrobble

    internal class SessionMetadata {
        private readonly ILogger Logger;

        /// <summary>
        /// The video Id.
        /// </summary>
        public Guid ItemId;

        /// <summary>
        /// The shoko file id for the current item, if any.
        /// </summary>
        public string? FileId;

        /// <summary>
        /// The jellyfin native watch session.
        /// </summary>
        public SessionInfo Session;

        /// <summary>
        /// Current playback ticks.
        /// </summary>
        public long PlaybackTicks;

        /// <summary>
        /// Playback ticks at the start of playback. Needed for the "start" event.
        /// </summary>
        public long InitialPlaybackTicks;

        /// <summary>
        /// How many scrobble events we have done. Used to track when to sync
        /// live progress back to shoko.
        /// </summary>
        public byte ScrobbleTicks;

        /// <summary>
        /// Indicates that we've reacted to the pause event of the video
        /// already. This is to track when to send pause/resume events.
        /// </summary>
        public bool IsPaused;

        /// <summary>
        /// Indicates we've already sent the start event.
        /// </summary>
        public bool SentStartEvent;

        /// <summary>
        /// The amount of events we have to skip before before we start sending
        /// the events.
        /// </summary>
        public int SkipEventCount;

        public SessionMetadata(ILogger logger, SessionInfo sessionInfo)
        {
            Logger = logger;
            ItemId = Guid.Empty;
            FileId = null;
            Session = sessionInfo;
            PlaybackTicks = 0;
            InitialPlaybackTicks = 0;
            ScrobbleTicks = 0;
            IsPaused = false;
            SkipEventCount = 0;
        }

        public bool  ShouldSendEvent(bool isPauseOrResumeEvent = false)
        {
            if (SkipEventCount == 0)
                return true;

            if (!isPauseOrResumeEvent && SkipEventCount > 0)
                SkipEventCount--;

            var shouldSend = SkipEventCount == 0;
            if (!shouldSend)
                Logger.LogDebug("Scrobble event was skipped. (File={FileId})", FileId);

            return shouldSend;
        }
    }

    private readonly ConcurrentDictionary<Guid, SessionMetadata> ActiveSessions = new();

    public void OnSessionStarted(object? sender, SessionEventArgs e)
    {
        if (TryGetUserConfiguration(e.SessionInfo.UserId, out var userConfig) && (userConfig!.SyncUserDataUnderPlayback || userConfig.SyncUserDataAfterPlayback)) {
            var sessionMetadata = new SessionMetadata(Logger, e.SessionInfo);
            ActiveSessions.TryAdd(e.SessionInfo.UserId, sessionMetadata);
        }
        foreach (var user in e.SessionInfo.AdditionalUsers) {
            if (TryGetUserConfiguration(e.SessionInfo.UserId, out userConfig) && (userConfig!.SyncUserDataUnderPlayback || userConfig.SyncUserDataAfterPlayback)) {
                var sessionMetadata = new SessionMetadata(Logger, e.SessionInfo);
                ActiveSessions.TryAdd(user.UserId, sessionMetadata);
            }
        }
    }

    public void OnSessionEnded(object? sender, SessionEventArgs e)
    {
        ActiveSessions.TryRemove(e.SessionInfo.UserId, out _);
        foreach (var user in e.SessionInfo.AdditionalUsers) {
            ActiveSessions.TryRemove(user.UserId, out _);
        }
    }

    public async void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
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
                    Lookup.TryGetEpisodeIdFor(e.Item, out var episodeId) &&
                    (userConfig!.SyncRestrictedVideos || e.Item.CustomRating != "XXX")
                ))
                return;

            var itemId = e.Item.Id;
            var userData = e.UserData;
            var config = Plugin.Instance.Configuration;
            bool? success = null;
            switch (e.SaveReason) {
                case UserDataSaveReason.PlaybackStart:
                case UserDataSaveReason.PlaybackProgress: {
                    // If a session can't be found or created then throw an error.
                    if (!ActiveSessions.TryGetValue(e.UserId, out var sessionMetadata))
                        return;

                    // The active video changed, so send a start event.
                    if (sessionMetadata.ItemId != itemId) {
                        sessionMetadata.ItemId = e.Item.Id;
                        sessionMetadata.FileId = fileId;
                        sessionMetadata.PlaybackTicks = userData.PlaybackPositionTicks;
                        sessionMetadata.InitialPlaybackTicks = userData.PlaybackPositionTicks;
                        sessionMetadata.ScrobbleTicks = 0;
                        sessionMetadata.IsPaused = false;
                        sessionMetadata.SentStartEvent = false;
                        sessionMetadata.SkipEventCount = userConfig.SyncUserDataInitialSkipEventCount;

                        Logger.LogInformation("Playback has started. (File={FileId})", fileId);
                        if (sessionMetadata.ShouldSendEvent() && userConfig.SyncUserDataUnderPlayback) {
                            sessionMetadata.SentStartEvent = true;
                            success = await APIClient.ScrobbleFile(fileId, episodeId, "play", sessionMetadata.InitialPlaybackTicks, userConfig.Token).ConfigureAwait(false);
                        }
                    }
                    else {
                        var isPaused = sessionMetadata.Session.PlayState?.IsPaused ?? false;
                        var ticks = sessionMetadata.Session.PlayState?.PositionTicks ?? userData.PlaybackPositionTicks;
                        // We received an event, but the position didn't change, so the playback is most likely paused.
                        if (isPaused) {
                            if (sessionMetadata.IsPaused)
                                return;

                            sessionMetadata.IsPaused = true;

                            Logger.LogInformation("Playback was paused. (File={FileId})", fileId);
                            if (sessionMetadata.ShouldSendEvent(true) && userConfig.SyncUserDataUnderPlayback)
                                success = await APIClient.ScrobbleFile(fileId, episodeId, "pause", sessionMetadata.PlaybackTicks, userConfig.Token).ConfigureAwait(false);
                        }
                        // The playback was resumed.
                        else if (sessionMetadata.IsPaused) {
                            sessionMetadata.PlaybackTicks = ticks;
                            sessionMetadata.ScrobbleTicks = 0;
                            sessionMetadata.IsPaused = false;

                            Logger.LogInformation("Playback was resumed. (File={FileId})", fileId);
                            if (sessionMetadata.ShouldSendEvent(true) && userConfig.SyncUserDataUnderPlayback)
                                success = await APIClient.ScrobbleFile(fileId, episodeId, "resume", sessionMetadata.PlaybackTicks, userConfig.Token).ConfigureAwait(false);
                        }
                        // Live scrobbling.
                        else  {
                            var deltaTicks = Math.Abs(ticks - sessionMetadata.PlaybackTicks);
                            sessionMetadata.PlaybackTicks = ticks;
                            if (deltaTicks == 0 || deltaTicks < userConfig.SyncUserDataUnderPlaybackLiveThreshold &&
                                ++sessionMetadata.ScrobbleTicks < userConfig.SyncUserDataUnderPlaybackAtEveryXTicks)
                                return;

                            var logLevel = userConfig.SyncUserDataUnderPlaybackLive ? LogLevel.Information : LogLevel.Debug;
                            Logger.Log(logLevel, "Playback is running. (File={FileId})", fileId);
                            sessionMetadata.ScrobbleTicks = 0;
                            if (sessionMetadata.ShouldSendEvent() && userConfig.SyncUserDataUnderPlayback) {
                                if (!sessionMetadata.SentStartEvent) {
                                    sessionMetadata.SentStartEvent = true;
                                    success = await APIClient.ScrobbleFile(fileId, episodeId, "play", sessionMetadata.InitialPlaybackTicks, userConfig.Token).ConfigureAwait(false);
                                }
                                if (userConfig.SyncUserDataUnderPlaybackLive)
                                    success = await APIClient.ScrobbleFile(fileId, episodeId, "scrobble", sessionMetadata.PlaybackTicks, userConfig.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    break;
                }
                case UserDataSaveReason.PlaybackFinished: {
                    if (!(userConfig.SyncUserDataAfterPlayback || userConfig.SyncUserDataUnderPlayback))
                        return;

                    var shouldSendEvent = true;
                    if (ActiveSessions.TryGetValue(e.UserId, out var sessionMetadata) && sessionMetadata.ItemId == e.Item.Id) {
                        shouldSendEvent = sessionMetadata.ShouldSendEvent(true);

                        sessionMetadata.ItemId = Guid.Empty;
                        sessionMetadata.FileId = null;
                        sessionMetadata.PlaybackTicks = 0;
                        sessionMetadata.InitialPlaybackTicks = 0;
                        sessionMetadata.ScrobbleTicks = 0;
                        sessionMetadata.IsPaused = false;
                        sessionMetadata.SentStartEvent = false;
                        sessionMetadata.SkipEventCount = -1;
                    }

                    Logger.LogInformation("Playback has ended. (File={FileId})", fileId);
                    if (shouldSendEvent)
                        if (!userData.Played && userData.PlaybackPositionTicks > 0)
                            success = await APIClient.ScrobbleFile(fileId, episodeId, "stop", userData.PlaybackPositionTicks, userConfig.Token).ConfigureAwait(false);
                        else
                            success = await APIClient.ScrobbleFile(fileId, episodeId, "stop", userData.PlaybackPositionTicks, userData.Played, userConfig.Token).ConfigureAwait(false);
                    break;
                }
                case UserDataSaveReason.TogglePlayed:
                    Logger.LogInformation("Scrobbled when toggled. (File={FileId})", fileId);
                    if (!userData.Played && userData.PlaybackPositionTicks > 0)
                        success = await APIClient.ScrobbleFile(fileId, episodeId, "user-interaction", userData.PlaybackPositionTicks, userConfig.Token).ConfigureAwait(false);
                    else
                        success = await APIClient.ScrobbleFile(fileId, episodeId, "user-interaction", userData.PlaybackPositionTicks, userData.Played, userConfig.Token).ConfigureAwait(false);
                    break;
                default:
                    success = null;
                    break;
            }
            if (success.HasValue) {
                if (success.Value) {
                    Logger.LogInformation("Successfully synced watch state with Shoko. (File={FileId})", fileId);
                }
                else {
                    Logger.LogInformation("Failed to sync watch state with Shoko. (File={FileId})", fileId);
                }
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized) {
            if (TryGetUserConfiguration(e.UserId, out var userConfig))
                Logger.LogError(ex, "{Message} (Username={Username},Id={UserId})", ex.Message, UserManager.GetUserById(userConfig!.UserId)?.Username, userConfig.UserId);
            return;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {ErrorMessage}", ex.Message);
            return;
        }
    }

    // Updates to favorite state and/or user data.
    private void OnUserRatingSaved(object? sender, UserDataSaveEventArgs e)
    {
        if (!TryGetUserConfiguration(e.UserId, out var userConfig))
            return;

        var userData = e.UserData;
        switch (e.Item) {
            case Episode:
            case Movie: {
                if (e.Item is not Video video || !Lookup.TryGetEpisodeIdFor(video, out var episodeId))
                    return;

                SyncVideo(video, userConfig!, userData, SyncDirection.Export, episodeId).ConfigureAwait(false);
                break;
            }
            case Season season: {
                if (!Lookup.TryGetSeriesIdFor(season, out var seriesId))
                    return;

                SyncSeason(season, userConfig!, userData, SyncDirection.Export, seriesId).ConfigureAwait(false);
                break;
            }
            case Series series: {
                if (!Lookup.TryGetSeriesIdFor(series, out var seriesId))
                    return;

                SyncSeries(series, userConfig!, userData, SyncDirection.Export, seriesId).ConfigureAwait(false);
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
            IsVirtualItem = false,
        })
            .OfType<Video>()
            .ToList();

        var numComplete = 0;
        var numTotal = videos.Count * enabledUsers.Count;
        foreach (var video in videos) {
            cancellationToken.ThrowIfCancellationRequested();

            if (!(Lookup.TryGetFileIdFor(video, out var fileId) && Lookup.TryGetEpisodeIdFor(video, out var episodeId)))
                continue;

            foreach (var userConfig in enabledUsers) {
                await SyncVideo(video, userConfig, direction, fileId, episodeId).ConfigureAwait(false);

                numComplete++;
                double percent = numComplete;
                percent /= numTotal;

                progress.Report(percent * 100);
            }
        }
        progress.Report(100);
    }

    public void OnItemAddedOrUpdated(object? sender, ItemChangeEventArgs e)
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

                    SyncVideo(video, userConfig, SyncDirection.Import, fileId, episodeId).ConfigureAwait(false);
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

    private Task SyncSeries(Series series, UserConfiguration userConfig, UserItemData? userData, SyncDirection direction, string seriesId)
    {
        // Try to load the user-data if it was not provided
        userData ??= UserDataManager.GetUserData(userConfig.UserId, series);
        // Create some new user-data if none exists.
        userData ??= new UserItemData {
                UserId = userConfig.UserId,
                Key = series.GetUserDataKeys()[0],
            };

        Logger.LogDebug("TODO; {SyncDirection} user data for Series {SeriesName}. (Series={SeriesId})", direction.ToString(), series.Name, seriesId);

        return Task.CompletedTask;
    }

    private Task SyncSeason(Season season, UserConfiguration userConfig, UserItemData? userData, SyncDirection direction, string seriesId)
    {
        // Try to load the user-data if it was not provided
        userData ??= UserDataManager.GetUserData(userConfig.UserId, season);
        // Create some new user-data if none exists.
        userData ??= new UserItemData {
                UserId = userConfig.UserId,
                Key = season.GetUserDataKeys()[0],
            };

        Logger.LogDebug("TODO; {SyncDirection} user data for Season {SeasonNumber} in Series {SeriesName}. (Series={SeriesId})", direction.ToString(), season.IndexNumber, season.SeriesName, seriesId);

        return Task.CompletedTask;
    }

    private Task SyncVideo(Video video, UserConfiguration userConfig, UserItemData? userData, SyncDirection direction, string episodeId)
    {
        if (!userConfig.SyncRestrictedVideos && video.CustomRating == "XXX") {
            Logger.LogTrace("Skipped {SyncDirection} user data for video {VideoName}. (Episode={EpisodeId})", direction.ToString(), video.Name, episodeId);
            return Task.CompletedTask;
        }
        // Try to load the user-data if it was not provided
        userData ??= UserDataManager.GetUserData(userConfig.UserId, video);
        // Create some new user-data if none exists.
        userData ??= new UserItemData {
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

    private async Task SyncVideo(Video video, UserConfiguration userConfig, SyncDirection direction, string fileId, string episodeId)
    {
        try {
            if (!userConfig.SyncRestrictedVideos && video.CustomRating == "XXX") {
                Logger.LogTrace("Skipped {SyncDirection} user data for video {VideoName}. (File={FileId},Episode={EpisodeId})", direction.ToString(), video.Name, fileId, episodeId);
                return;
            }
            var localUserStats = UserDataManager.GetUserData(userConfig.UserId, video);
            var remoteUserStats = await APIClient.GetFileUserStats(fileId, userConfig.Token);
            bool isInSync = UserDataEqualsFileUserStats(localUserStats, remoteUserStats);
            Logger.LogInformation("{SyncDirection} user data for video {VideoName}. (User={UserId},File={FileId},Episode={EpisodeId},Local={HaveLocal},Remote={HaveRemote},InSync={IsInSync})", direction.ToString(), video.Name, userConfig.UserId, fileId, episodeId, localUserStats != null, remoteUserStats != null, isInSync);
            if (isInSync)
                return;

            switch (direction)
            {
                case SyncDirection.Export:
                    // Abort since there are no local stats to export.
                    if (localUserStats == null)
                        break;
                    // Export the local stats if there is no remote stats or if the local stats are newer.
                    if (remoteUserStats == null) {
                        remoteUserStats = localUserStats.ToFileUserStats();
                        // Don't sync if the local state is considered empty and there is no remote state.
                        if (remoteUserStats.IsEmpty)
                            break;
                        remoteUserStats = await APIClient.PutFileUserStats(fileId, remoteUserStats, userConfig.Token);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Episode={EpisodeId})", SyncDirection.Export.ToString(), video.Name, userConfig.UserId, fileId, episodeId);
                    }
                    else if (localUserStats.LastPlayedDate.HasValue && localUserStats.LastPlayedDate.Value > remoteUserStats.LastUpdatedAt) {
                        remoteUserStats = localUserStats.ToFileUserStats();
                        remoteUserStats = await APIClient.PutFileUserStats(fileId, remoteUserStats, userConfig.Token);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Episode={EpisodeId})", SyncDirection.Export.ToString(), video.Name, userConfig.UserId, fileId, episodeId);
                    }
                    break;
                case SyncDirection.Import:
                    // Abort since there are no remote stats to import.
                    if (remoteUserStats == null)
                        break;
                    // Create a new local stats entry if there is no local entry.
                    if (localUserStats == null) {
                        UserDataManager.SaveUserData(userConfig.UserId, video, localUserStats = remoteUserStats.ToUserData(video, userConfig.UserId), UserDataSaveReason.Import, CancellationToken.None);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Episode={EpisodeId})", SyncDirection.Import.ToString(), video.Name, userConfig.UserId, fileId, episodeId);
                    }
                    // Else merge the remote stats into the local stats entry.
                    else if (!localUserStats.LastPlayedDate.HasValue || remoteUserStats.LastUpdatedAt > localUserStats.LastPlayedDate.Value) {
                        UserDataManager.SaveUserData(userConfig.UserId, video, localUserStats.MergeWithFileUserStats(remoteUserStats), UserDataSaveReason.Import, CancellationToken.None);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Episode={EpisodeId})", SyncDirection.Import.ToString(), video.Name, userConfig.UserId, fileId, episodeId);
                    }
                    break;
                default:
                case SyncDirection.Sync: {
                    // Export if there is local stats but no remote stats.
                    if (localUserStats == null && remoteUserStats != null)
                        goto case SyncDirection.Import;

                    // Try to import of there is no local stats ubt there are remote stats.
                    else if (remoteUserStats == null && localUserStats != null)
                        goto case SyncDirection.Export;

                    // Abort if there are no local or remote stats.
                    else if (remoteUserStats == null && localUserStats == null)
                        break;

                    // Try to import if we're unable to read the last played timestamp.
                    if (!localUserStats!.LastPlayedDate.HasValue)
                        goto case SyncDirection.Import;

                    // Abort if the stats are in sync.
                    if (isInSync || localUserStats.LastPlayedDate.Value == remoteUserStats!.LastUpdatedAt)
                        break;

                    // Export if the local state is fresher then the remote state.
                    if (localUserStats.LastPlayedDate.Value > remoteUserStats.LastUpdatedAt) {
                        remoteUserStats = localUserStats.ToFileUserStats();
                        remoteUserStats = await APIClient.PutFileUserStats(fileId, remoteUserStats, userConfig.Token);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Episode={EpisodeId})", SyncDirection.Export.ToString(), video.Name, userConfig.UserId, fileId, episodeId);
                    }
                    // Else import if the remote state is fresher then the local state.
                    else if (localUserStats.LastPlayedDate.Value < remoteUserStats.LastUpdatedAt) {
                        UserDataManager.SaveUserData(userConfig.UserId, video, localUserStats.MergeWithFileUserStats(remoteUserStats), UserDataSaveReason.Import, CancellationToken.None);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Episode={EpisodeId})", SyncDirection.Import.ToString(), video.Name, userConfig.UserId, fileId, episodeId);
                    }
                    break;
                }
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized) {
            Logger.LogError(ex, "{Message} (Username={Username},Id={UserId})", ex.Message, UserManager.GetUserById(userConfig.UserId)?.Username, userConfig.UserId);
            throw;
        }
    }

    /// <summary>
    /// Checks if the local user data and the remote user stats are in sync.
    /// </summary>
    /// <param name="localUserData">The local user data</param>
    /// <param name="remoteUserStats">The remote user stats.</param>
    /// <returns>True if they are not in sync.</returns>
    private static bool UserDataEqualsFileUserStats(UserItemData? localUserData, UserStats? remoteUserStats)
    {
        if (remoteUserStats == null && localUserData == null)
            return true;

        if (localUserData == null)
            return false;

        var localUserStats = localUserData.ToFileUserStats();
        if (remoteUserStats == null)
            return localUserStats.IsEmpty;

        if (localUserStats.IsEmpty && remoteUserStats.IsEmpty)
            return true;

        if (localUserStats.ResumePosition != remoteUserStats.ResumePosition)
            return false;

        if (localUserStats.WatchedCount != remoteUserStats.WatchedCount)
            return false;

        var played = remoteUserStats.LastWatchedAt.HasValue;
        if (localUserData.Played != played)
            return false;

        if (localUserStats.LastUpdatedAt != remoteUserStats.LastUpdatedAt)
            return false;

        return true;
    }
}
