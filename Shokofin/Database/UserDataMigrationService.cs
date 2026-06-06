using System;
using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Shokofin.Extensions;
using Shokofin.ExternalIds;

namespace Shokofin.Database;

public class UserDataMigrationService(
    UserDataRepositoryService repository,
    IUserManager userManager,
    ILibraryManager libraryManager,
    ILogger<UserDataMigrationService> logger)
{
    private readonly UserDataRepositoryService _repository = repository;
    private readonly IUserManager _userManager = userManager;
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly ILogger<UserDataMigrationService> _logger = logger;

    public void MigratePaths(Dictionary<string, PathChangeInfo> pathChangeMap) {
        if (pathChangeMap.Count == 0)
            return;

        var totalCount = pathChangeMap.Count;
        var seriesMigrated = 0;
        var movieMigrated = 0;
        foreach (var (_, info) in pathChangeMap) {
            if (info.IsMovie) {
                if (MigrateMovie(info))
                    movieMigrated++;
            }
            else if (MigrateSeriesEpisode(info)) {
                seriesMigrated++;
            }
        }

        if (seriesMigrated > 0)
            _logger.LogInformation("Migrated user data for {Count} series episodes (out of {Total} changes)", seriesMigrated, totalCount);

        if (movieMigrated > 0)
            _logger.LogInformation("Migrated user data for {Count} movies (out of {Total} changes)", movieMigrated, totalCount);
    }

    private bool MigrateSeriesEpisode(PathChangeInfo info) {
        var oldKey = ShokoInternalId.SeriesNamespace + info.ShowId +
            info.Season.ToString("000", CultureInfo.InvariantCulture) +
            info.Episode.ToString("000", CultureInfo.InvariantCulture);

        var newKey = ShokoInternalId.SeriesNamespace + info.NewShowId +
            info.Season.ToString("000", CultureInfo.InvariantCulture) +
            info.Episode.ToString("000", CultureInfo.InvariantCulture);

        if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
            return false;

        var oldItemId = _libraryManager.GetNewItemId(info.OldPath, typeof(Episode));
        var newItemId = _libraryManager.GetNewItemId(info.NewPath, typeof(Episode));

        return MigratePerUser(oldKey, newKey, oldItemId, newItemId);
    }

    private bool MigrateMovie(PathChangeInfo info) {
        var oldItem = _libraryManager.FindByPath(info.OldPath, false);
        if (oldItem is null)
            return false;

        var oldKey = oldItem.GetUserDataKeys()[0];
        var oldItemId = _libraryManager.GetNewItemId(info.OldPath, typeof(Movie));
        var newItemId = _libraryManager.GetNewItemId(info.NewPath, typeof(Movie));

        // If key is TMDB/IMDB it's stable — no migration needed.
        // Only migrate if key is the GUID fallback (derived from path).
        if (!string.Equals(oldKey, oldItemId.ToString(), StringComparison.OrdinalIgnoreCase))
            return false;

        var newKey = newItemId.ToString();
        if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
            return false;

        return MigratePerUser(oldKey, newKey, oldItemId, newItemId);
    }

    private bool MigratePerUser(string oldKey, string newKey, Guid oldItemId, Guid newItemId) {
        var anyMigrated = false;
        foreach (var user in _userManager.Users) {
            var oldData = _repository.GetUserDataByKey(oldKey, user);
            if (oldData is null)
                continue;

            var newData = new UserItemData { Key = newKey };
            newData.CopyFrom(oldData);

            _repository.SaveUserDataForNewKey(newKey, newData, user, newItemId);
            _repository.DeleteUserDataByKey(oldKey, user, oldItemId);

            _logger.LogDebug(
                "Migrated user data for {UserName} from key \"{OldKey}\" to \"{NewKey}\" (OldId={OldId},NewId={NewId})",
                user.Username,
                oldKey,
                newKey,
                oldItemId,
                newItemId);

            anyMigrated = true;
        }
        return anyMigrated;
    }
}
