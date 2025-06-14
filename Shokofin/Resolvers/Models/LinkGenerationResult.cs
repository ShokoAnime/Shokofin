
using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Shokofin.Resolvers.Models;

public class LinkGenerationResult {
    private DateTime CreatedAt { get; init; } = DateTime.Now;

    public ConcurrentBag<string> Paths { get; init; } = [];

    public ConcurrentBag<string> RemovedPaths { get; init; } = [];

    public int Total =>
        TotalVideos + TotalExternalFiles + TotalTrickplayDirectories;

    public int Created =>
        CreatedVideos + CreatedExternalFiles + CreatedTrickplayDirectories;

    public int Fixed =>
        FixedVideos + FixedExternalFiles + FixedTrickplayDirectories;

    public int Skipped =>
        SkippedVideos + SkippedExternalFiles + SkippedTrickplayDirectories;

    public int Removed =>
        RemovedVideos + RemovedExternalFiles + RemovedNfos + RemovedTrickplayDirectories;

    public int TotalVideos =>
        CreatedVideos + FixedVideos + SkippedVideos;

    public int CreatedVideos { get; set; }

    public int FixedVideos { get; set; }

    public int SkippedVideos { get; set; }

    public int RemovedVideos { get; set; }

    public int TotalExternalFiles =>
        CreatedExternalFiles + FixedExternalFiles + SkippedExternalFiles;

    public int CreatedExternalFiles { get; set; }

    public int FixedExternalFiles { get; set; }

    public int SkippedExternalFiles { get; set; }

    public int RemovedExternalFiles { get; set; }

    public int TotalTrickplayDirectories =>
        CreatedTrickplayDirectories + FixedTrickplayDirectories + SkippedTrickplayDirectories;

    public int CreatedTrickplayDirectories { get; set; }

    public int FixedTrickplayDirectories { get; set; }

    public int SkippedTrickplayDirectories { get; set; }

    public int RemovedTrickplayDirectories { get; set; }

    public int RemovedNfos { get; set; }

    public void Print(ILogger logger, string path) {
        var timeSpent = DateTime.Now - CreatedAt;
        logger.LogInformation(
            "Created {CreatedTotal} ({CreatedMedia},{CreatedExternal},{CreatedTrickplay}), fixed {FixedTotal} ({FixedMedia},{FixedExternal},{FixedTrickplay}), skipped {SkippedTotal} ({SkippedMedia},{SkippedExternal},{SkippedTrickplay}), and removed {RemovedTotal} ({RemovedMedia},{RemovedExternal},{RemovedTrickplay},{RemovedNFO}) entries in folder at {Path} in {TimeSpan} (Total={Total})",
            Created,
            CreatedVideos,
            CreatedExternalFiles,
            CreatedTrickplayDirectories,
            Fixed,
            FixedVideos,
            FixedExternalFiles,
            FixedTrickplayDirectories,
            Skipped,
            SkippedVideos,
            SkippedExternalFiles,
            SkippedTrickplayDirectories,
            Removed,
            RemovedVideos,
            RemovedExternalFiles,
            RemovedTrickplayDirectories,
            RemovedNfos,
            path,
            timeSpent,
            Total
        );
    }

    public static LinkGenerationResult operator +(LinkGenerationResult a, LinkGenerationResult b) {
        // Re-use the same instance so the parallel execution will share the same bag.
        var paths = a.Paths;
        foreach (var path in b.Paths)
            paths.Add(path);

        var removedPaths = a.RemovedPaths;
        foreach (var path in b.RemovedPaths)
            removedPaths.Add(path);

        return new() {
            CreatedAt = a.CreatedAt,
            Paths = paths,
            RemovedPaths = removedPaths,
            CreatedVideos = a.CreatedVideos + b.CreatedVideos,
            FixedVideos = a.FixedVideos + b.FixedVideos,
            SkippedVideos = a.SkippedVideos + b.SkippedVideos,
            RemovedVideos = a.RemovedVideos + b.RemovedVideos,
            CreatedExternalFiles = a.CreatedExternalFiles + b.CreatedExternalFiles,
            FixedExternalFiles = a.FixedExternalFiles + b.FixedExternalFiles,
            SkippedExternalFiles = a.SkippedExternalFiles + b.SkippedExternalFiles,
            RemovedExternalFiles = a.RemovedExternalFiles + b.RemovedExternalFiles,
            CreatedTrickplayDirectories = a.CreatedTrickplayDirectories + b.CreatedTrickplayDirectories,
            FixedTrickplayDirectories = a.FixedTrickplayDirectories + b.FixedTrickplayDirectories,
            SkippedTrickplayDirectories = a.SkippedTrickplayDirectories + b.SkippedTrickplayDirectories,
            RemovedTrickplayDirectories = a.RemovedTrickplayDirectories + b.RemovedTrickplayDirectories,
            RemovedNfos = a.RemovedNfos + b.RemovedNfos,
        };
    }
}