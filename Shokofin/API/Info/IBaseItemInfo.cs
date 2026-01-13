using System;
using System.Collections.Generic;
using Shokofin.API.Models;

namespace Shokofin.API.Info;

/// <summary>
/// Information about a base item.
/// </summary>
public interface IBaseItemInfo {
    /// <summary>
    /// Unique identifier for the base item.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Preferred title according to title settings on the server for the base item type.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// List of all available titles for the base item.
    /// </summary>
    IReadOnlyList<Title> Titles { get; }

    /// <summary>
    /// Preferred overview according to description settings on the server.
    /// </summary>
    string? Overview { get; }

    /// <summary>
    /// List of all available overviews for the base item.
    /// </summary>
    IReadOnlyList<Text> Overviews { get; }

    /// <summary>
    /// Notes.
    /// </summary>
    IReadOnlyList<string> Notes { get => []; }

    /// <summary>
    /// Original language code for the base item if available.
    /// </summary>
    string? OriginalLanguageCode { get; }

    /// <summary>
    /// Date and time the base item was created.
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// Date and time the base item was last updated.
    /// </summary>
    DateTime LastUpdatedAt { get; }
}
