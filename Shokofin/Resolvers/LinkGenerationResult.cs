
using System;
using System.Collections.Concurrent;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Shokofin.Resolvers;

public class LinkGenerationResult
{
    private DateTime CreatedAt { get; init; } = DateTime.Now;

    public ConcurrentBag<string> Paths { get; init; } = new();

    public int Total =>
        TotalVideos + TotalSubtitles + TotalNfos;

    public int Created =>
        CreatedVideos + CreatedSubtitles + CreatedNfos;

    public int Fixed =>
        FixedVideos + FixedSubtitles;

    public int Skipped =>
        SkippedVideos + SkippedSubtitles + SkippedNfos;

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

    public int TotalNfos =>
        CreatedNfos + SkippedNfos;

    public int CreatedNfos { get; set; }

    public int SkippedNfos { get; set; }

    public int RemovedNfos { get; set; }

    public void Print(Folder mediaFolder, ILogger logger)
    {
        var timeSpent = DateTime.Now - CreatedAt;
        var logLevel = Removed == 0 && Skipped == Total ? LogLevel.Debug : LogLevel.Information;
        logger.Log(
            logLevel,
            "Created {CreatedTotal} ({CreatedMedia},{CreatedSubtitles},{CreatedNFO}), fixed {FixedTotal} ({FixedMedia},{FixedSubtitles}), skipped {SkippedTotal} ({SkippedMedia},{SkippedSubtitles},{SkippedNFO}), and removed {RemovedTotal} ({RemovedMedia},{RemovedSubtitles},{RemovedNFO}) entries in media folder at {Path} in {TimeSpan} (Total={Total})",
            Created,
            CreatedVideos,
            CreatedSubtitles,
            CreatedNfos,
            Fixed,
            FixedVideos,
            FixedSubtitles,
            Skipped,
            SkippedVideos,
            SkippedSubtitles,
            SkippedNfos,
            Removed,
            RemovedVideos,
            RemovedSubtitles,
            RemovedNfos,
            mediaFolder.Path,
            timeSpent,
            Total
        );
    }

    public void MarkSkipped()
    {
        if (FixedSubtitles > 0 || CreatedSubtitles > 0) {
            SkippedSubtitles += FixedSubtitles + CreatedSubtitles;
            FixedSubtitles = CreatedSubtitles = 0;
        }
        if (FixedVideos > 0 || CreatedVideos > 0) {
            SkippedVideos += FixedVideos + CreatedVideos;
            FixedVideos = CreatedVideos = 0;
        }
        if (CreatedNfos > 0) {
            SkippedNfos += CreatedNfos;
            CreatedNfos = 0;
        }
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
            CreatedNfos = a.CreatedNfos + b.CreatedNfos,
            SkippedNfos = a.SkippedNfos + b.SkippedNfos,
            RemovedNfos = a.RemovedNfos + b.RemovedNfos,
        };
    }
}