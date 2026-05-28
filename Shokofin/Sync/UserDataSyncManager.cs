using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
using Shokofin.Extensions;
using Shokofin.Resolvers;

using UserStats = Shokofin.API.Models.File.UserStats;

namespace Shokofin.Sync;

public class UserDataSyncManager {

    private readonly IUserDataManager UserDataManager;

    private readonly IUserManager UserManager;

    private readonly ILibraryManager LibraryManager;

    private readonly ISessionManager SessionManager;

    private readonly ILogger<UserDataSyncManager> Logger;

    private readonly VirtualFileSystemService VfsService;

    private readonly ShokoApiClient ApiClient;

    private readonly ShokoIdLookup Lookup;

    public UserDataSyncManager(IUserDataManager userDataManager, IUserManager userManager, ILibraryManager libraryManager, ISessionManager sessionManager, ILogger<UserDataSyncManager> logger, VirtualFileSystemService vfsService, ShokoApiClient apiClient, ShokoIdLookup lookup) {
        UserDataManager = userDataManager;
        UserManager = userManager;
        LibraryManager = libraryManager;
        SessionManager = sessionManager;
        Logger = logger;
        VfsService = vfsService;
        ApiClient = apiClient;
        Lookup = lookup;

        SessionManager.SessionStarted += OnSessionStarted;
        SessionManager.SessionEnded += OnSessionEnded;
        SessionManager.PlaybackStart += OnPlaybackStart;
        SessionManager.PlaybackStopped += OnPlaybackStopped;
        UserDataManager.UserDataSaved += OnUserDataSaved;
        LibraryManager.ItemAdded += OnItemAddedOrUpdated;
        LibraryManager.ItemUpdated += OnItemAddedOrUpdated;
    }

    public void Dispose() {
        SessionManager.SessionStarted -= OnSessionStarted;
        SessionManager.SessionEnded -= OnSessionEnded;
        SessionManager.PlaybackStart -= OnPlaybackStart;
        SessionManager.PlaybackStopped -= OnPlaybackStopped;
        UserDataManager.UserDataSaved -= OnUserDataSaved;
        LibraryManager.ItemAdded -= OnItemAddedOrUpdated;
        LibraryManager.ItemUpdated -= OnItemAddedOrUpdated;
    }

    private static bool TryGetUserConfiguration(Guid userId, [NotNullWhen(true)] out UserConfiguration? config) {
        config = Plugin.Instance.Configuration.UserList.FirstOrDefault(c => c.UserId == userId && c.EnableSynchronization);
        return config != null;
    }

    #region Export/Scrobble

    internal class SessionMetadata {
        private readonly ILogger Logger;

        /// <summary>
        /// The current session Id.
        /// </summary>
        public string SessionId;

        /// <summary>
        /// The current user Id we're tracking for the session.
        /// </summary>
        public Guid UserId;

        /// <summary>
        /// The currently active item Id. Used to detect when the item changes.
        /// </summary>
        public Guid ActiveItemId;

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

        public SessionMetadata(ILogger logger, SessionInfo sessionInfo, Guid userId) {
            Logger = logger;
            SessionId = sessionInfo.Id;
            UserId = userId;
            ItemId = Guid.Empty;
            FileId = null;
            Session = sessionInfo;
            PlaybackTicks = 0;
            InitialPlaybackTicks = 0;
            ScrobbleTicks = 0;
            IsPaused = false;
            SkipEventCount = 0;
        }

        public bool  ShouldSendEvent(bool isPauseOrResumeEvent = false) {
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

    private readonly ConcurrentDictionary<string, SessionMetadata> ActiveSessions = new();

    private IEnumerable<SessionMetadata> GetSessionsForSessionId(string sessionId) {
        foreach (var session in ActiveSessions.Values) {
            if (session.SessionId == sessionId) {
                yield return session;
            }
        }
    }

    private bool TryGetSessionByUserId(Guid userId, Guid itemId, [NotNullWhen(true)] out SessionMetadata? session) {
        foreach (var metadata in ActiveSessions.Values) {
            if (metadata.UserId == userId && (metadata.ActiveItemId == itemId || metadata.ItemId == itemId)) {
                session = metadata;
                return true;
            }
        }

        session = null;
        return false;
    }

    public void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e) {
        foreach (var sessionMetadata in GetSessionsForSessionId(e.Session.Id))
            sessionMetadata.ActiveItemId = e.Item.Id;
    }

    public void OnPlaybackStopped(object? sender, PlaybackProgressEventArgs e) {
        foreach (var sessionMetadata in GetSessionsForSessionId(e.Session.Id))
            sessionMetadata.ActiveItemId = Guid.Empty;
    }

    public void OnSessionStarted(object? sender, SessionEventArgs e) {
        if (TryGetUserConfiguration(e.SessionInfo.UserId, out var userConfig) && userConfig.EnableSynchronization && (userConfig.SyncUserDataUnderPlayback || userConfig.SyncUserDataAfterPlayback)) {
            var sessionMetadata = new SessionMetadata(Logger, e.SessionInfo, e.SessionInfo.UserId);
            var key = $"{e.SessionInfo.Id}:{e.SessionInfo.UserId}";
            ActiveSessions.TryAdd(key, sessionMetadata);
        }
        foreach (var user in e.SessionInfo.AdditionalUsers) {
            if (TryGetUserConfiguration(e.SessionInfo.UserId, out userConfig) && userConfig.EnableSynchronization && (userConfig.SyncUserDataUnderPlayback || userConfig.SyncUserDataAfterPlayback)) {
                var sessionMetadata = new SessionMetadata(Logger, e.SessionInfo, user.UserId);
                var key = $"{e.SessionInfo.Id}:{user.UserId}";
                ActiveSessions.TryAdd(key, sessionMetadata);
            }
        }
    }

    public void OnSessionEnded(object? sender, SessionEventArgs e) {
        foreach (var session in GetSessionsForSessionId(e.SessionInfo.Id).ToArray()) {
            ActiveSessions.TryRemove($"{e.SessionInfo.Id}:{session.UserId}", out _);
        }
    }

    public async void OnUserDataSaved(object? sender, UserDataSaveEventArgs e) {
        try {

            if (e == null || e.Item == null || Guid.Empty == e.UserId || e.UserData == null || !Lookup.IsEnabledForItem(e.Item))
                return;

            if (e.SaveReason == UserDataSaveReason.UpdateUserRating) {
                OnUserRatingSaved(sender, e);
                return;
            }

            if (!(
                    (e.Item is Movie || e.Item is Episode) &&
                    TryGetUserConfiguration(e.UserId, out var userConfig) &&
                    userConfig.EnableSynchronization &&
                    (userConfig.SyncRestrictedVideos || e.Item.CustomRating != "XXX") &&
                    Lookup.TryGetFileAndSeriesIdFor(e.Item, out var fileId, out var seriesId) &&
                    await ApiClient.GetFile(fileId) is { } file &&
                    file.CrossReferences.FirstOrDefault(xref0 => xref0.Series.Shoko.HasValue && xref0.Series.Shoko.Value.ToString() == seriesId && xref0.Episodes.Any(xref1 => xref1.Shoko.HasValue)) is { } xref
                ))
                return;

            var episodeId = xref.Episodes.First(xref => xref.Shoko.HasValue).Shoko!.Value.ToString();
            var itemId = e.Item.Id;
            var userData = e.UserData;
            var config = Plugin.Instance.Configuration;
            bool? success = null;
            switch (e.SaveReason) {
                case UserDataSaveReason.PlaybackStart:
                case UserDataSaveReason.PlaybackProgress: {
                    // If a session can't be found or created then throw an error.
                    if (!TryGetSessionByUserId(e.UserId, itemId, out var sessionMetadata))
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
                            success = await ApiClient.ScrobbleFile(fileId, episodeId, "play", sessionMetadata.InitialPlaybackTicks, userConfig.Token);
                        }
                    }
                    else {
                        var isPaused = sessionMetadata.Session.PlayState.IsPaused;
                        var ticks = sessionMetadata.Session.PlayState.PositionTicks ?? userData.PlaybackPositionTicks;
                        // We received an event, but the position didn't change, so the playback is most likely paused.
                        if (isPaused) {
                            if (sessionMetadata.IsPaused)
                                return;

                            sessionMetadata.IsPaused = true;

                            Logger.LogInformation("Playback was paused. (File={FileId})", fileId);
                            if (sessionMetadata.ShouldSendEvent(true) && userConfig.SyncUserDataUnderPlayback)
                                success = await ApiClient.ScrobbleFile(fileId, episodeId, "pause", sessionMetadata.PlaybackTicks, userConfig.Token);
                        }
                        // The playback was resumed.
                        else if (sessionMetadata.IsPaused) {
                            sessionMetadata.PlaybackTicks = ticks;
                            sessionMetadata.ScrobbleTicks = 0;
                            sessionMetadata.IsPaused = false;

                            Logger.LogInformation("Playback was resumed. (File={FileId})", fileId);
                            if (sessionMetadata.ShouldSendEvent(true) && userConfig.SyncUserDataUnderPlayback)
                                success = await ApiClient.ScrobbleFile(fileId, episodeId, "resume", sessionMetadata.PlaybackTicks, userConfig.Token);
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
                                    success = await ApiClient.ScrobbleFile(fileId, episodeId, "play", sessionMetadata.InitialPlaybackTicks, userConfig.Token);
                                }
                                if (userConfig.SyncUserDataUnderPlaybackLive)
                                    success = await ApiClient.ScrobbleFile(fileId, episodeId, "scrobble", sessionMetadata.PlaybackTicks, userConfig.Token);
                            }
                        }
                    }
                    break;
                }
                case UserDataSaveReason.PlaybackFinished: {
                    if (!(userConfig.SyncUserDataAfterPlayback || userConfig.SyncUserDataUnderPlayback))
                        return;

                    var shouldSendEvent = true;
                    if (TryGetSessionByUserId(e.UserId, e.Item.Id, out var sessionMetadata) && sessionMetadata.ItemId == e.Item.Id) {
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
                            success = await ApiClient.ScrobbleFile(fileId, episodeId, "stop", userData.PlaybackPositionTicks, userConfig.Token);
                        else
                            success = await ApiClient.ScrobbleFile(fileId, episodeId, "stop", userData.PlaybackPositionTicks, userData.Played, userConfig.Token);
                    break;
                }
                case UserDataSaveReason.TogglePlayed:
                    Logger.LogInformation("Scrobbled when toggled. (File={FileId})", fileId);
                    if (!userData.Played && userData.PlaybackPositionTicks > 0)
                        success = await ApiClient.ScrobbleFile(fileId, episodeId, "user-interaction", userData.PlaybackPositionTicks, userConfig.Token);
                    else
                        success = await ApiClient.ScrobbleFile(fileId, episodeId, "user-interaction", userData.PlaybackPositionTicks, userData.Played, userConfig.Token);
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
    private void OnUserRatingSaved(object? sender, UserDataSaveEventArgs e) {
        if (!TryGetUserConfiguration(e.UserId, out var userConfig))
            return;

        var userData = e.UserData;
        switch (e.Item) {
            case Episode:
            case Movie: {
                if (e.Item is not Video video || !Lookup.TryGetEpisodeIdsFor(video, out var episodeIds))
                    return;

                SyncVideo(video, userConfig!, userData, SyncDirection.Export, episodeIds[0]);
                break;
            }
            case Season season: {
                if (!Lookup.TryGetSeasonIdFor(season, out var seasonId))
                    return;

                SyncSeason(season, userConfig!, userData, SyncDirection.Export, seasonId);
                break;
            }
            case Series series: {
                if (!Lookup.TryGetSeasonIdFor(series, out var seasonId))
                    return;

                SyncSeries(series, userConfig!, userData, SyncDirection.Export, seasonId);
                break;
            }
        }
    }

    #endregion

    #region Import/Sync

    public async Task ScanAndSync(SyncDirection direction, IProgress<double> progress, CancellationToken cancellationToken) {
        var enabledUsers = Plugin.Instance.Configuration.UserList.Where(c => c.EnableSynchronization).ToList();
        if (enabledUsers.Count == 0) {
            progress.Report(100);
            return;
        }

        var videos = LibraryManager.GetItemList(new InternalItemsQuery {
            MediaTypes = [MediaType.Video],
            IsFolder = false,
            Recursive = true,
            DtoOptions = new DtoOptions(false) {
                EnableImages = false
            },
            SourceTypes = [SourceType.Library],
            IsVirtualItem = false,
        })
            .OfType<Video>()
            .ToList();

        var numComplete = 0;
        var numTotal = videos.Count * enabledUsers.Count;
        foreach (var video in videos) {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Lookup.IsEnabledForItem(video) || !Lookup.TryGetFileAndSeriesIdFor(video, out var fileId, out var seriesId))
                continue;

            foreach (var userConfig in enabledUsers) {
                await SyncVideo(video, userConfig, direction, fileId, seriesId);

                numComplete++;
                double percent = numComplete;
                percent /= numTotal;

                progress.Report(percent * 100);
            }
        }
        progress.Report(100);
    }

    public void OnItemAddedOrUpdated(object? sender, ItemChangeEventArgs e) {
        if (e == null || e.Item == null || e.Parent == null || !(e.UpdateReason.HasFlag(ItemUpdateType.MetadataImport) || e.UpdateReason.HasFlag(ItemUpdateType.MetadataDownload)))
            return;

        switch (e.Item) {
            case Video video: {
                if (!Lookup.IsEnabledForItem(video) || !Lookup.TryGetFileAndSeriesIdFor(video, out var fileId, out var seriesId))
                    return;

                var path = video is Episode ep ? ep.Series?.Path : video is Movie mv ? mv.ContainingFolderPath : video.Path;
                if (VfsService.TryGetCurrentLibraryGenerationMode(path, out var iterativeGeneration, out var wasGenerated) && iterativeGeneration && !wasGenerated) {
                    Logger.LogTrace("Skipped video during iterative generation. (Path={Path})", video.Path);
                    return;
                }

                foreach (var userConfig in Plugin.Instance.Configuration.UserList) {
                    if (!userConfig.EnableSynchronization)
                        continue;

                    if (!userConfig.SyncUserDataOnImport)
                        continue;

                    SyncVideo(video, userConfig, SyncDirection.Import, fileId, seriesId);
                }
                break;
            }
            case Season season: {
                if (!season.IndexNumber.HasValue || season.IndexNumber.Value == 0)
                    return;

                if (!Lookup.IsEnabledForItem(season) || !Lookup.TryGetSeasonIdFor(season, out var seasonId))
                    return;

                if (season.Series is not { } series) {
                    Logger.LogTrace("Skipping import user data for season; Unable to find series for season to use. (Season={SeasonId})", seasonId);
                    return;
                }

                if (VfsService.TryGetCurrentLibraryGenerationMode(series.Path, out var iterativeGeneration, out var wasGenerated) && iterativeGeneration && !wasGenerated) {
                    Logger.LogTrace("Skipped season during iterative generation. (Season={SeasonId})", seasonId);
                    return;
                }

                if (seasonId[0] is IdPrefix.TmdbShow or IdPrefix.TmdbMovie) {
                    Logger.LogTrace("Skipping import user data for season {SeasonNumber} in series {SeriesName}; Season is not a Shoko Series. (Season={SeasonId})", season.IndexNumber, series.Name, seasonId);
                    return;
                }

                foreach (var userConfig in Plugin.Instance.Configuration.UserList) {
                    if (!userConfig.EnableSynchronization)
                        continue;

                    if (!userConfig.SyncUserDataOnImport)
                        continue;

                    SyncSeason(season, userConfig, null, SyncDirection.Import, seasonId);
                }
                break;
            }
            case Series series: {
                if (!Lookup.IsEnabledForItem(series) || !Lookup.TryGetSeasonIdFor(series, out var mainSeasonId))
                    return;

                if (VfsService.TryGetCurrentLibraryGenerationMode(series.Path, out var iterativeGeneration, out var wasGenerated) && iterativeGeneration && !wasGenerated) {
                    Logger.LogTrace("Skipped series during iterative generation. (MainSeason={SeasonId})", mainSeasonId);
                    return;
                }

                if (mainSeasonId[0] is IdPrefix.TmdbShow or IdPrefix.TmdbMovie) {
                    Logger.LogTrace("Skipping import user data for Series {SeriesName}; Series is not a Shoko Series. (MainSeason={SeasonId})", series.Name, mainSeasonId);
                    return;
                }

                foreach (var userConfig in Plugin.Instance.Configuration.UserList) {
                    if (!userConfig.EnableSynchronization)
                        continue;

                    if (!userConfig.SyncUserDataOnImport)
                        continue;

                    SyncSeries(series, userConfig, null, SyncDirection.Import, mainSeasonId);
                }
                break;
            }
        }

    }

    #endregion

    private Task SyncSeries(Series series, UserConfiguration userConfig, UserItemData? userData, SyncDirection direction, string seasonId) {
        var user = UserManager.GetUserById(userConfig.UserId);
        if (user == null) {
            return Task.CompletedTask;
        }
        // Try to load the user-data if it was not provided
        userData ??= UserDataManager.GetUserData(user, series);
        // Create some new user-data if none exists.
        userData ??= new UserItemData {
                Key = series.GetUserDataKeys()[0],
            };

        Logger.LogDebug("TODO; {SyncDirection} user data for Series {SeriesName}. (Series={SeriesId})", direction.ToString(), series.Name, seasonId);

        return Task.CompletedTask;
    }

    private Task SyncSeason(Season season, UserConfiguration userConfig, UserItemData? userData, SyncDirection direction, string seasonId) {
        var user = UserManager.GetUserById(userConfig.UserId);
        if (user == null) {
            return Task.CompletedTask;
        }
        // Try to load the user-data if it was not provided
        userData ??= UserDataManager.GetUserData(user, season);
        // Create some new user-data if none exists.
        userData ??= new UserItemData {
                Key = season.GetUserDataKeys()[0],
            };

        Logger.LogDebug("TODO; {SyncDirection} user data for Season {SeasonNumber} in Series {SeriesName}. (Series={SeriesId})", direction.ToString(), season.IndexNumber, season.SeriesName, seasonId);

        return Task.CompletedTask;
    }

    private Task SyncVideo(Video video, UserConfiguration userConfig, UserItemData? userData, SyncDirection direction, string episodeId) {
        var user = UserManager.GetUserById(userConfig.UserId);
        if (user == null) {
            return Task.CompletedTask;
        }
        if (!userConfig.SyncRestrictedVideos && video.CustomRating == "XXX") {
            Logger.LogTrace("Skipped {SyncDirection} user data for video {VideoName}. (Episode={EpisodeId})", direction.ToString(), video.Name, episodeId);
            return Task.CompletedTask;
        }
        // Try to load the user-data if it was not provided
        userData ??= UserDataManager.GetUserData(user, video);
        // Create some new user-data if none exists.
        userData ??= new UserItemData {
                Key = video.GetUserDataKeys()[0],
                LastPlayedDate = null,
            };

        // var remoteUserData = await APIClient.GetFileUserData(fileId, userConfig.Token);
        // if (remoteUserData == null)
        //     return;

        Logger.LogDebug("TODO; {SyncDirection} user data for video {VideoName}. (Episode={EpisodeId})", direction.ToString(), video.Name, episodeId);

        return Task.CompletedTask;
    }

    private async Task SyncVideo(Video video, UserConfiguration userConfig, SyncDirection direction, string fileId, string seriesId) {
        try {
            var user = UserManager.GetUserById(userConfig.UserId);
            if (user is null || (!userConfig.SyncRestrictedVideos && video.CustomRating == "XXX")) {
                Logger.LogTrace("Skipped {SyncDirection} user data for video {VideoName}. (User={UserId},File={FileId},Series={SeriesId})", direction.ToString(), userConfig.UserId, video.Name, fileId, seriesId);
                return;
            }

            var localUserStats = UserDataManager.GetUserData(user, video);
            var remoteUserStats = await ApiClient.GetFileUserStats(fileId, userConfig.Token);
            bool isInSync = UserDataEqualsFileUserStats(localUserStats, remoteUserStats);
            Logger.LogInformation("{SyncDirection} user data for video {VideoName}. (User={UserId},File={FileId},Series={SeriesId},Local={HaveLocal},Remote={HaveRemote},InSync={IsInSync})", direction.ToString(), video.Name, userConfig.UserId, fileId, seriesId, localUserStats != null, remoteUserStats != null, isInSync);
            if (isInSync)
                return;

            switch (direction) {
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
                        remoteUserStats = await ApiClient.PutFileUserStats(fileId, remoteUserStats, userConfig.Token);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Series={SeriesId})", SyncDirection.Export.ToString(), video.Name, userConfig.UserId, fileId, seriesId);
                    }
                    else if (localUserStats.LastPlayedDate.HasValue && localUserStats.LastPlayedDate.Value > remoteUserStats.LastUpdatedAt) {
                        remoteUserStats = localUserStats.ToFileUserStats();
                        remoteUserStats = await ApiClient.PutFileUserStats(fileId, remoteUserStats, userConfig.Token);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Series={SeriesId})", SyncDirection.Export.ToString(), video.Name, userConfig.UserId, fileId, seriesId);
                    }
                    break;
                case SyncDirection.Import:
                    // Abort since there are no remote stats to import.
                    if (remoteUserStats == null)
                        break;
                    // Create a new local stats entry if there is no local entry.
                    if (localUserStats == null) {
                        UserDataManager.SaveUserData(user, video, localUserStats = remoteUserStats.ToUserData(video), UserDataSaveReason.Import, CancellationToken.None);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Series={SeriesId})", SyncDirection.Import.ToString(), video.Name, userConfig.UserId, fileId, seriesId);
                    }
                    // Else merge the remote stats into the local stats entry.
                    else if (!localUserStats.LastPlayedDate.HasValue || remoteUserStats.LastUpdatedAt > localUserStats.LastPlayedDate.Value) {
                        UserDataManager.SaveUserData(user, video, localUserStats.MergeWithFileUserStats(remoteUserStats), UserDataSaveReason.Import, CancellationToken.None);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Series={SeriesId})", SyncDirection.Import.ToString(), video.Name, userConfig.UserId, fileId, seriesId);
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
                        remoteUserStats = await ApiClient.PutFileUserStats(fileId, remoteUserStats, userConfig.Token);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Series={SeriesId})", SyncDirection.Export.ToString(), video.Name, userConfig.UserId, fileId, seriesId);
                    }
                    // Else import if the remote state is fresher then the local state.
                    else if (localUserStats.LastPlayedDate.Value < remoteUserStats.LastUpdatedAt) {
                        UserDataManager.SaveUserData(user, video, localUserStats.MergeWithFileUserStats(remoteUserStats), UserDataSaveReason.Import, CancellationToken.None);
                        Logger.LogDebug("{SyncDirection} user data for video {VideoName} successful. (User={UserId},File={FileId},Series={SeriesId})", SyncDirection.Import.ToString(), video.Name, userConfig.UserId, fileId, seriesId);
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
    private static bool UserDataEqualsFileUserStats(UserItemData? localUserData, UserStats? remoteUserStats) {
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
