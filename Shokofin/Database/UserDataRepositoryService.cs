using System;
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

    public void SaveUserDataForNewKey(string key, UserItemData data, User user, Guid newItemId) {
#if NET9_0_OR_GREATER
        using var context = _dbContextFactory.CreateDbContext();

        // If a row already exists for this (ItemId, UserId, CustomDataKey) composite key,
        // update it in place. Otherwise Jellyfin's ReattachUserDataAsync will attempt to
        // move placeholder rows to the same key and hit a UNIQUE CONSTRAINT violation.
        var existing = context.UserData
            .FirstOrDefault(e => e.ItemId == newItemId && e.UserId == user.Id && e.CustomDataKey == key);

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
                ItemId = newItemId,
                UserId = user.Id,
                Item = null!,
                User = null!,
                RetentionDate = null,
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
            .Where(e => e.ItemId == oldItemId && e.UserId == user.Id && e.CustomDataKey == key)
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
}
