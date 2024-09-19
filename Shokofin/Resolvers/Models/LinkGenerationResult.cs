
using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Shokofin.Resolvers.Models;

public class LinkGenerationResult
{
    private DateTime CreatedAt { get; init; } = DateTime.Now;

    public ConcurrentBag<string> Paths { get; init; } = [];

    public int Total =>
        TotalVideos + TotalSubtitles;

    public int Created =>
        CreatedVideos + CreatedSubtitles;

    public int Fixed =>
        FixedVideos + FixedSubtitles;

    public int Skipped =>
        SkippedVideos + SkippedSubtitles;

    public int Removed =>
        RemovedVideos + RemovedSubtitles + RemovedNfos;

    public int TotalVideos =>
        CreatedVideos + FixedVideos + SkippedVideos;

    public int CreatedVideos { get; set; }

    public int FixedVideos { get; set; }

    public int SkippedVideos { get; set; }

    public int RemovedVideos { get; set; }

    public int TotalSubtitles =>
        CreatedSubtitles + FixedSubtitles + SkippedSubtitles;

    public int CreatedSubtitles { get; set; }

    public int FixedSubtitles { get; set; }

    public int SkippedSubtitles { get; set; }

    public int RemovedSubtitles { get; set; }

    public int RemovedNfos { get; set; }

    public void Print(ILogger logger, string path)
    {
        var timeSpent = DateTime.Now - CreatedAt;
        logger.LogInformation(
            "Created {CreatedTotal} ({CreatedMedia},{CreatedSubtitles}), fixed {FixedTotal} ({FixedMedia},{FixedSubtitles}), skipped {SkippedTotal} ({SkippedMedia},{SkippedSubtitles}), and removed {RemovedTotal} ({RemovedMedia},{RemovedSubtitles},{RemovedNFO}) entries in folder at {Path} in {TimeSpan} (Total={Total})",
            Created,
            CreatedVideos,
            CreatedSubtitles,
            Fixed,
            FixedVideos,
            FixedSubtitles,
            Skipped,
            SkippedVideos,
            SkippedSubtitles,
            Removed,
            RemovedVideos,
            RemovedSubtitles,
            RemovedNfos,
            path,
            timeSpent,
            Total
        );
    }

    public static LinkGenerationResult operator +(LinkGenerationResult a, LinkGenerationResult b)
    {
        // Re-use the same instance so the parallel execution will share the same bag.
        var paths = a.Paths;
        foreach (var path in b.Paths)
            a.Paths.Add(path);

        return new()
        {
            CreatedAt = a.CreatedAt,
            Paths = paths,
            CreatedVideos = a.CreatedVideos + b.CreatedVideos,
            FixedVideos = a.FixedVideos + b.FixedVideos,
            SkippedVideos = a.SkippedVideos + b.SkippedVideos,
            RemovedVideos = a.RemovedVideos + b.RemovedVideos,
            CreatedSubtitles = a.CreatedSubtitles + b.CreatedSubtitles,
            FixedSubtitles = a.FixedSubtitles + b.FixedSubtitles,
            SkippedSubtitles = a.SkippedSubtitles + b.SkippedSubtitles,
            RemovedSubtitles = a.RemovedSubtitles + b.RemovedSubtitles,
            RemovedNfos = a.RemovedNfos + b.RemovedNfos,
        };
    }
}