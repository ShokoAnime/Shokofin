
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