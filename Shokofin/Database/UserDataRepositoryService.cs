using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;

#if NET9_0_OR_GREATER
using System.Linq;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
#else
using Jellyfin.Data.Entities;
using Microsoft.Data.Sqlite;
#endif

namespace Shokofin.Database;

public class UserDataRepositoryService
{
#if NET9_0_OR_GREATER
    private readonly IDbContextFactory<JellyfinDbContext> _dbContextFactory;

    /// <summary>
    /// The sentinel GUID Jellyfin uses as a placeholder for detached UserData rows.
    /// Must match BaseItemRepository.PlaceholderId.
    /// </summary>
    private static readonly Guid PlaceholderId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public UserDataRepositoryService(
        IDbContextFactory<JellyfinDbContext> dbContextFactory) {
        _dbContextFactory = dbContextFactory;
    }
#else
    private readonly string _dbPath;

    public UserDataRepositoryService(
        IServerConfigurationManager configManager) {
        _dbPath = System.IO.Path.Combine(configManager.ApplicationPaths.DataPath, "library.db");
    }
#endif

    /// <summary>
    /// Returns the subset of the given item ids that already exist in Jellyfin's database. Used by
    /// the migration to avoid writing user data rows whose ItemId has no backing BaseItem yet.
    /// </summary>
    public HashSet<Guid> GetExistingItemIds(IEnumerable<Guid> itemIds) {
#if NET9_0_OR_GREATER
        var ids = itemIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new HashSet<Guid>();

        using var context = _dbContextFactory.CreateDbContext();
        return context.BaseItems
            .WhereOneOrMany(ids, e => e.Id)
            .Select(e => e.Id)
            .ToHashSet();
#else
        // Jellyfin 10.10's UserDatas table has no per-item foreign keys, so every destination is valid.
        return new HashSet<Guid>();
#endif
    }

    public UserItemData? GetUserDataByKey(string key, User user) {
#if NET9_0_OR_GREATER
        using var context = _dbContextFactory.CreateDbContext();
        var entry = context.UserData
            .AsNoTracking()
            .FirstOrDefault(e => e.CustomDataKey == key && e.UserId == user.Id);
        if (entry is null)
            return null;

        return new UserItemData {
            Key = entry.CustomDataKey,
            Rating = entry.Rating,
            Played = entry.Played,
            PlayCount = entry.PlayCount,
            IsFavorite = entry.IsFavorite,
            PlaybackPositionTicks = entry.PlaybackPositionTicks,
            LastPlayedDate = entry.LastPlayedDate,
            AudioStreamIndex = entry.AudioStreamIndex,
            SubtitleStreamIndex = entry.SubtitleStreamIndex,
            Likes = entry.Likes,
        };
#else
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT key,userId,rating,played,playCount,isFavorite,playbackPositionTicks,lastPlayedDate," +
            "AudioStreamIndex,SubtitleStreamIndex " +
            "FROM UserDatas WHERE key=@key AND userId=@userId";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@userId", user.InternalId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new UserItemData {
            Key = reader.IsDBNull(0) ? key : reader.GetString(0),
            Rating = reader.IsDBNull(2) ? null : reader.GetDouble(2),
            Played = reader.GetBoolean(3),
            PlayCount = reader.GetInt32(4),
            IsFavorite = reader.GetBoolean(5),
            PlaybackPositionTicks = reader.GetInt64(6),
            LastPlayedDate = reader.IsDBNull(7) ? null : DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc),
            AudioStreamIndex = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            SubtitleStreamIndex = reader.IsDBNull(9) ? null : reader.GetInt32(9),
        };
#endif
    }

    public void SaveUserDataForNewKey(string key, UserItemData data, User user, Guid newItemId, HashSet<Guid> existingItemIds) {
#if NET9_0_OR_GREATER
        using var context = _dbContextFactory.CreateDbContext();

        // The destination item may not exist yet: migration runs during VFS generation, before
        // Jellyfin creates the new BaseItem. Writing a row with a dangling ItemId would violate the
        // UserData.ItemId -> BaseItems.Id foreign key, so route it to the placeholder instead;
        // Jellyfin's ReattachUserDataAsync moves it onto the item once the item is first refreshed.
        var routedToPlaceholder = !existingItemIds.Contains(newItemId);
        var targetItemId = routedToPlaceholder ? PlaceholderId : newItemId;

        // If a row already exists for this (ItemId, UserId, CustomDataKey) composite key,
        // update it in place. Otherwise Jellyfin's ReattachUserDataAsync will attempt to
        // move placeholder rows to the same key and hit a UNIQUE CONSTRAINT violation.
        var existing = context.UserData
            .FirstOrDefault(e => e.ItemId == targetItemId && e.UserId == user.Id && e.CustomDataKey == key);

        if (existing is not null) {
            existing.Rating = data.Rating;
            existing.PlaybackPositionTicks = data.PlaybackPositionTicks;
            existing.PlayCount = data.PlayCount;
            existing.IsFavorite = data.IsFavorite;
            existing.LastPlayedDate = data.LastPlayedDate;
            existing.Played = data.Played;
            existing.AudioStreamIndex = data.AudioStreamIndex;
            existing.SubtitleStreamIndex = data.SubtitleStreamIndex;
            existing.Likes = data.Likes;
        }
        else {
            // Delete any placeholder row that shares the same (UserId, CustomDataKey).
            // Without this, Jellyfin's ReattachUserDataAsync will attempt to move the
            // placeholder row to this ItemId and hit a UNIQUE CONSTRAINT violation
            // because our row already occupies that (ItemId, UserId, CustomDataKey) slot.
            // This mirrors what Jellyfin itself does for the tombstone path in
            // BaseItemRepository (commit 482271c / PR #14475).
            context.UserData
                .Where(e => e.ItemId == PlaceholderId && e.UserId == user.Id && e.CustomDataKey == key)
                .ExecuteDelete();

            var entry = new UserData {
                CustomDataKey = key,
                ItemId = targetItemId,
                UserId = user.Id,
                Item = null!,
                User = null!,
                RetentionDate = routedToPlaceholder ? DateTime.UtcNow : null,
                Rating = data.Rating,
                PlaybackPositionTicks = data.PlaybackPositionTicks,
                PlayCount = data.PlayCount,
                IsFavorite = data.IsFavorite,
                LastPlayedDate = data.LastPlayedDate,
                Played = data.Played,
                AudioStreamIndex = data.AudioStreamIndex,
                SubtitleStreamIndex = data.SubtitleStreamIndex,
                Likes = data.Likes,
            };
            context.UserData.Add(entry);
        }
        context.SaveChanges();
#else
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT OR REPLACE INTO UserDatas " +
            "(key,userId,rating,played,playCount,isFavorite,playbackPositionTicks,lastPlayedDate,AudioStreamIndex,SubtitleStreamIndex) " +
            "VALUES (@key,@userId,@rating,@played,@playCount,@isFavorite,@playbackPositionTicks,@lastPlayedDate,@audioStreamIndex,@subtitleStreamIndex)";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@userId", user.InternalId);
        cmd.Parameters.AddWithValue("@rating", data.Rating.HasValue ? (object)data.Rating.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@played", data.Played);
        cmd.Parameters.AddWithValue("@playCount", data.PlayCount);
        cmd.Parameters.AddWithValue("@isFavorite", data.IsFavorite);
        cmd.Parameters.AddWithValue("@playbackPositionTicks", data.PlaybackPositionTicks);
        cmd.Parameters.AddWithValue("@lastPlayedDate", data.LastPlayedDate.HasValue ? (object)data.LastPlayedDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
        cmd.Parameters.AddWithValue("@audioStreamIndex", data.AudioStreamIndex.HasValue ? (object)data.AudioStreamIndex.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@subtitleStreamIndex", data.SubtitleStreamIndex.HasValue ? (object)data.SubtitleStreamIndex.Value : DBNull.Value);
        cmd.ExecuteNonQuery();
#endif
    }

    public void DeleteUserDataByKey(string key, User user, Guid oldItemId) {
#if NET9_0_OR_GREATER
        using var context = _dbContextFactory.CreateDbContext();
        context.UserData
            .Where(e => (e.ItemId == oldItemId || e.ItemId == PlaceholderId) && e.UserId == user.Id && e.CustomDataKey == key)
            .ExecuteDelete();
#else
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM UserDatas WHERE key=@key AND userId=@userId";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@userId", user.InternalId);
        cmd.ExecuteNonQuery();
#endif
    }

    /// <summary>
    /// Removes any detached placeholder UserData row for a given user and key set. The sync
    /// import calls this before saving so Jellyfin's ReattachUserDataAsync does not later move
    /// a placeholder row onto an item that already holds the shared key, which would violate
    /// the composite (ItemId, UserId, CustomDataKey) unique constraint.
    /// </summary>
    public void DeleteUserDataByKeyForPlaceholder(IEnumerable<string> keys, User user) {
#if NET9_0_OR_GREATER
        var keyArray = keys.ToArray();
        if (keyArray.Length == 0)
            return;

        using var context = _dbContextFactory.CreateDbContext();
        context.UserData
            .Where(e => e.ItemId == PlaceholderId && e.UserId == user.Id && keyArray.Contains(e.CustomDataKey))
            .ExecuteDelete();
#else
        // Jellyfin 10.10's UserDatas schema keys on (key, userId) with no per-item placeholder
        // rows, so this UNIQUE constraint cannot occur there.
#endif
    }
}
