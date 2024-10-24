using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.ExternalIds;
using Shokofin.Providers;
using Shokofin.Sync;

namespace Shokofin.Tasks;

/// <summary>
/// Migrate user watch data for episodes store in Jellyfin to the newest id namespace.
/// </summary>
public class MigrateEpisodeUserDataTask(
    ILogger<MigrateEpisodeUserDataTask> logger,
    IUserDataManager userDataManager,
    IUserManager userManager,
    ILibraryManager libraryManager
) : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILogger<MigrateEpisodeUserDataTask> _logger = logger;

    private readonly IUserDataManager _userDataManager = userDataManager;

    private readonly IUserManager _userManager = userManager;

    private readonly ILibraryManager _libraryManager = libraryManager;

    /// <inheritdoc />
    public string Name => "Migrate Episode User Watch Data";

    /// <inheritdoc />
    public string Description => "Migrate user watch data for episodes store in Jellyfin to the newest id namespace.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoMigrateEpisodeUserDataTask";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var foundEpisodeCount = 0;
        var seriesDict = new Dictionary<string, (Series series, List<Episode> episodes)>();
        var users = _userManager.Users.ToList();
        var allEpisodes = _libraryManager.GetItemList(new InternalItemsQuery {
            IncludeItemTypes = [BaseItemKind.Episode],
            HasAnyProviderId = new Dictionary<string, string> { { ShokoFileId.Name, string.Empty } },
            IsFolder = false,
            Recursive = true,
            DtoOptions = new(false) {
                EnableImages = false
            },
            SourceTypes = [SourceType.Library],
            IsVirtualItem = false,
        })
            .OfType<Episode>()
            .ToList();
        _logger.LogDebug("Attempting to migrate user watch data across {EpisodeCount} episodes and {UserCount} users.", allEpisodes.Count, users.Count);
        foreach (var episode in allEpisodes) {
            cancellationToken.ThrowIfCancellationRequested();

            if (!episode.ParentIndexNumber.HasValue || !episode.IndexNumber.HasValue ||
                !episode.TryGetProviderId(ShokoFileId.Name, out var fileId) ||
                episode.Series is not Series series || !series.TryGetProviderId(ShokoSeriesId.Name, out var seriesId))
                continue;

            if (!seriesDict.TryGetValue(seriesId, out var tuple))
                seriesDict[seriesId] = tuple = (series, []);

            tuple.episodes.Add(episode);
            foundEpisodeCount++;
        }

        _logger.LogInformation("Found {SeriesCount} series and {EpisodeCount} episodes across {AllEpisodeCount} total episodes to search for user watch data to migrate.", seriesDict.Count, foundEpisodeCount, allEpisodes.Count);
        var savedCount = 0;
        var numComplete = 0;
        var numTotal = foundEpisodeCount * users.Count;
        var userDataDict = users.ToDictionary(user => user, user => (_userDataManager.GetAllUserData(user.Id).DistinctBy(data => data.Key).ToDictionary(data => data.Key), new List<UserItemData>()));
        var userDataToRemove = new List<UserItemData>();
        foreach (var (seriesId, (series, episodes)) in seriesDict) {
            cancellationToken.ThrowIfCancellationRequested();

            SeriesProvider.AddProviderIds(series, seriesId);
            var seriesUserKeys = series.GetUserDataKeys();
            // 10.9 post-4.1 id format
            var primaryKey = seriesUserKeys.First();
            var keysToSearch = seriesUserKeys.Skip(1)
                // 10.9 pre-4.1 id format
                .Prepend($"shoko://shoko-series={seriesId}")
                // 10.8 id format
                .Prepend($"INVALID-BUT-DO-NOT-TOUCH:{seriesId}")
                .ToList();
            _logger.LogTrace("Migrating user watch data for series {SeriesName}. (Series={SeriesId},Primary={PrimaryKey},Search={SearchKeys})", series.Name, seriesId, primaryKey, keysToSearch);
            foreach (var episode in episodes) {
                cancellationToken.ThrowIfCancellationRequested();

                if (!episode.TryGetProviderId(ShokoFileId.Name, out var fileId))
                    continue;

                var suffix = episode.ParentIndexNumber!.Value.ToString("000", CultureInfo.InvariantCulture) + episode.IndexNumber!.Value.ToString("000", CultureInfo.InvariantCulture);
                var videoUserDataKeys = (episode as Video).GetUserDataKeys();
                var episodeKeysToSearch = keysToSearch.Select(key => key + suffix).Prepend(primaryKey + suffix).Concat(videoUserDataKeys).ToList();
                _logger.LogTrace("Migrating user watch data for season {SeasonNumber}, episode {EpisodeNumber} - {EpisodeName}. (Series={SeriesId},File={FileId},Search={SearchKeys})", episode.ParentIndexNumber, episode.IndexNumber, episode.Name, seriesId, fileId, episodeKeysToSearch);
                foreach (var (user, (dataDict, dataList)) in userDataDict) {
                    var userData = _userDataManager.GetUserData(user, episode);
                    foreach (var searchKey in episodeKeysToSearch) {
                        if (!dataDict.TryGetValue(searchKey, out var searchUserData))
                            continue;

                        if (userData.CopyFrom(searchUserData)) {
                            _logger.LogInformation("Found user data to migrate. (Series={SeriesId},File={FileId},Search={SearchKeys},Key={SearchKey},User={UserId})", seriesId, fileId, episodeKeysToSearch, searchKey, user.Id);
                            dataList.Add(userData);
                            savedCount++;
                        }
                        break;
                    }

                    numComplete++;
                    double percent = numComplete;
                    percent /= numTotal;

                    progress.Report(percent * 100);
                }
            }
        }

        // Last attempt to cancel before we save all the changes.
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("Saving {UserDataCount} user watch data entries across {UserCount} users", savedCount, users.Count);
        foreach (var (user, (dataDict, dataList)) in userDataDict) {
            if (dataList.Count is 0)
                continue;

            _userDataManager.SaveAllUserData(user.Id, dataList.ToArray(), CancellationToken.None);
        }
        _logger.LogInformation("Saved {UserDataCount} user watch data entries across {UserCount} users", savedCount, users.Count);

        progress.Report(100);
        return Task.CompletedTask;
    }
}
