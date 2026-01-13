using System;
using System.Collections.Generic;
using System.Linq;
using Shokofin.API.Models;
using Shokofin.API.Models.Shoko;
using Shokofin.Utils;

namespace Shokofin.API.Info;

public class CollectionInfo(ShokoGroup group, string? mainSeasonId, List<ShowInfo> shows, List<CollectionInfo> subCollections) : IBaseItemInfo {
    /// <summary>
    /// Collection Identifier.
    /// </summary>
    public string Id { get; init; } = group.Id;

    /// <summary>
    /// Parent Collection Identifier, if any.
    /// </summary>
    public string? ParentId { get; init; } = group.IDs.ParentGroup?.ToString();

    /// <summary>
    /// Top Level Collection Identifier. Will refer to itself if it's a top level collection.
    /// </summary>
    public string TopLevelId { get; init; } = group.IDs.TopLevelGroup.ToString();

    /// <summary>
    /// Main show's main season identifier.
    /// </summary>
    public string? MainSeasonId { get; init; } = mainSeasonId;

    /// <summary>
    /// True if the collection is a top level collection.
    /// </summary>
    public bool IsTopLevel { get; init; } = group.IDs.TopLevelGroup == group.IDs.Shoko;

    /// <summary>
    /// Collection Name.
    /// </summary>
    public string Title { get; init; } = group.Name;

    public IReadOnlyList<Title> Titles { get; init; } = [];

    /// <summary>
    /// Collection Description.
    /// </summary>
    public string Overview { get; init; } = group.Description;

    public IReadOnlyList<Text> Overviews { get; init; } = [];

    public IReadOnlyList<string> Notes { get; init; } = [];

    public string? OriginalLanguageCode => null;

    public DateTime CreatedAt { get; init; }

    public DateTime LastUpdatedAt { get; init; }

    /// <summary>
    /// Number of files across all shows and movies in the collection and all sub-collections.
    /// </summary>
    public int FileCount { get; init; } = group.Sizes.Files;

    /// <summary>
    /// Shows in the collection and not in any sub-collections.
    /// </summary>
    public IReadOnlyList<ShowInfo> Shows { get; init; } = shows
        .Where(showInfo => !showInfo.IsMovieCollection)
        .ToList();

    /// <summary>
    /// Movies in the collection and not in any sub-collections.
    /// </summary>
    public IReadOnlyList<ShowInfo> Movies { get; init; } = shows
        .Where(showInfo => showInfo.IsMovieCollection)
        .ToList();

    /// <summary>
    /// Sub-collections of the collection.
    /// </summary>
    public IReadOnlyList<CollectionInfo> SubCollections { get; init; } = subCollections;

    public CollectionInfo(ShokoGroup group, ShokoSeries series, string? mainSeasonId, List<ShowInfo> shows, List<CollectionInfo> subCollections) : this(group, mainSeasonId, shows, subCollections)
    {
        Title = series.Name;
        Titles = series.AniDB.Titles;
        Overview = series.Description == series.AniDB.Description
            ? TextUtility.SanitizeAnidbDescription(series.Description)
            : series.Description;
        Overviews = [
            new() {
                IsDefault = true,
                IsPreferred = true,
                LanguageCode = "en",
                Source = "AniDB",
                Value = TextUtility.SanitizeAnidbDescription(series.AniDB.Description, out var notes),
            },
        ];
        Notes = notes;
        CreatedAt = group.CreatedAt;
        LastUpdatedAt = group.LastUpdatedAt;
    }
}
