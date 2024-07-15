
using System;
using MediaBrowser.Controller.Entities;
using Shokofin.API.Models;

namespace Shokofin.Sync;

public static class SyncExtensions
{
    public static File.UserStats ToFileUserStats(this UserItemData userData)
    {
        TimeSpan? resumePosition = new TimeSpan(userData.PlaybackPositionTicks);
        if (Math.Floor(resumePosition.Value.TotalMilliseconds) == 0d)
            resumePosition = null;
        var lastUpdated = userData.LastPlayedDate ?? DateTime.Now;
        return new File.UserStats
        {
            LastUpdatedAt = lastUpdated,
            LastWatchedAt = userData.Played ? lastUpdated : null,
            ResumePosition = resumePosition,
            WatchedCount = userData.PlayCount,
        };
    }

    public static bool CopyFrom(this UserItemData userData, UserItemData otherUserData)
    {
        var updated = false;

        if (!userData.Rating.HasValue && otherUserData.Rating.HasValue || userData.Rating.HasValue && otherUserData.Rating.HasValue && userData.Rating != otherUserData.Rating)
        {
            userData.Rating = otherUserData.Rating;
            updated = true;
        }

        if (userData.PlaybackPositionTicks != otherUserData.PlaybackPositionTicks)
        {
            userData.PlaybackPositionTicks = otherUserData.PlaybackPositionTicks;
            updated = true;
        }

        if (userData.PlayCount != otherUserData.PlayCount)
        {
            userData.PlayCount = otherUserData.PlayCount;
            updated = true;
        }

        if (!userData.IsFavorite != otherUserData.IsFavorite)
        {
            userData.IsFavorite = otherUserData.IsFavorite;
            updated = true;
        }

        if (!userData.LastPlayedDate.HasValue && otherUserData.LastPlayedDate.HasValue || userData.LastPlayedDate.HasValue && otherUserData.LastPlayedDate.HasValue && userData.LastPlayedDate < otherUserData.LastPlayedDate)
        {
            userData.LastPlayedDate = otherUserData.LastPlayedDate;
            updated = true;
        }

        if (userData.Played != otherUserData.Played)
        {
            userData.Played = otherUserData.Played;
            updated = true;
        }

        if (!userData.AudioStreamIndex.HasValue && otherUserData.AudioStreamIndex.HasValue || userData.AudioStreamIndex.HasValue && otherUserData.AudioStreamIndex.HasValue && userData.AudioStreamIndex != otherUserData.AudioStreamIndex)
        {
            userData.AudioStreamIndex = otherUserData.AudioStreamIndex;
            updated = true;
        }

        if (!userData.SubtitleStreamIndex.HasValue && otherUserData.SubtitleStreamIndex.HasValue || userData.SubtitleStreamIndex.HasValue && otherUserData.SubtitleStreamIndex.HasValue && userData.SubtitleStreamIndex != otherUserData.SubtitleStreamIndex)
        {
            userData.SubtitleStreamIndex = otherUserData.SubtitleStreamIndex;
            updated = true;
        }

        if (!userData.Likes.HasValue && otherUserData.Likes.HasValue || userData.Likes.HasValue && otherUserData.Likes.HasValue && userData.Likes != otherUserData.Likes)
        {
            userData.Likes = otherUserData.Likes;
            updated = true;
        }

        return updated;
    }


    public static UserItemData MergeWithFileUserStats(this UserItemData userData, File.UserStats userStats)
    {
        userData.Played = userStats.LastWatchedAt.HasValue;
        userData.PlayCount = userStats.WatchedCount;
        userData.PlaybackPositionTicks = userStats.ResumePosition?.Ticks ?? 0;
        userData.LastPlayedDate = userStats.ResumePosition.HasValue ? userStats.LastUpdatedAt : userStats.LastWatchedAt ?? userStats.LastUpdatedAt;
        return userData;
    }

    public static UserItemData ToUserData(this File.UserStats userStats, Video video, Guid userId)
    {
        return new UserItemData
        {
            UserId = userId,
            Key = video.GetUserDataKeys()[0],
            LastPlayedDate = null,
        }.MergeWithFileUserStats(userStats);
    }
}