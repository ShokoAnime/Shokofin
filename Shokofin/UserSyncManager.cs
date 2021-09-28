using System;
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
    public class UserSyncManager
    {
        private readonly IUserDataManager UserDataManager;

        private readonly ILibraryManager LibraryManager;

        private readonly ILogger<UserSyncManager> Logger;

        private readonly ShokoAPIClient APIClient;

        private readonly IIdLookup Lookup;

        public UserSyncManager(IUserDataManager userDataManager, ILibraryManager libraryManager, ILogger<UserSyncManager> logger, ShokoAPIClient apiClient, IIdLookup lookup)
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
            config = Plugin.Instance.Configuration.UserList.FirstOrDefault(c => c.UserId == userId);
            return config != null;
        }

        #region Export

        public void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
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
                    Lookup.TryGetFileIdFor(e.Item, out var fileId) &&
                    Lookup.TryGetEpisodeIdFor(e.Item, out var episodeId)
                ))
                return;

            var config = Plugin.Instance.Configuration;
            switch (e.SaveReason) {
                case UserDataSaveReason.PlaybackStart:
                case UserDataSaveReason.PlaybackProgress:
                    if (!config.SyncUserDataUnderPlayback || !userConfig.EnableSynchronization)
                        return;
                    SyncVideo(userConfig, e.Item as Video, fileId, episodeId).ConfigureAwait(false);
                    break;
                case UserDataSaveReason.PlaybackFinished:
                    if (!config.SyncUserDataAfterPlayback || !userConfig.EnableSynchronization)
                        return;
                    SyncVideo(userConfig, e.Item as Video, fileId, episodeId).ConfigureAwait(false);
                    break;
                case UserDataSaveReason.TogglePlayed:
                    SyncVideo(userConfig, e.Item as Video, fileId, episodeId).ConfigureAwait(false);
                    break;
            }
        }

        private void OnUserRatingSaved(object sender, UserDataSaveEventArgs e)
        {
            // TODO: Sync user ratings.
            Logger.LogDebug("Sync user rating for {ItemName}.", e.Item.Name);
        }

        #endregion
        #region Import

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
                    await SyncVideo(userConfig, video, fileId, episodeId).ConfigureAwait(false);

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

            if (!(e.Item is Video video))
                return;
            
            if (!(Lookup.TryGetFileIdFor(video, out var fileId) && Lookup.TryGetEpisodeIdFor(video, out var episodeId)))
                return;

            foreach (var userConfig in Plugin.Instance.Configuration.UserList) {
                if (!userConfig.EnableSynchronization)
                    continue;

                SyncVideo(userConfig, video, fileId, episodeId).ConfigureAwait(false);
            }
        }

        #endregion

        private async Task SyncVideo(UserConfiguration userConfig, Video item, string fileId, string episodeId)
        {
            // var remoteUserData = await APIClient.GetFileUserData(fileId, userConfig.Token);
            // if (remoteUserData == null)
            //     return;

            var userData = UserDataManager.GetUserData(userConfig.UserId, item);
            if (userData == null) 
                userData = new UserItemData {
                    UserId = userConfig.UserId,
                    LastPlayedDate = null,
                };

            // TODO: Check what needs to be done, e.g. update JF, update SS, or nothing.
            Logger.LogDebug("Sync user data for {ItemName}.", item.Name);
        }
    }
}
