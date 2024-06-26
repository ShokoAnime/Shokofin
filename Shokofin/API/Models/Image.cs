using System;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class Image
{
    /// <summary>
    /// AniDB, TvDB, TMDB, etc.
    /// </summary>
    public ImageSource Source { get; set; } = ImageSource.AniDB;

    /// <summary>
    /// Poster, Banner, etc.
    /// </summary>
    public ImageType Type { get; set; } = ImageType.Poster;

    /// <summary>
    /// The image's id. Usually an int, but in the case of <see cref="ImageType.Static"/> resources
    /// then it is the resource name.
    /// </summary>
    public string ID { get; set; } = string.Empty;


    /// <summary>
    /// True if the image is marked as the default for the given <see cref="ImageType"/>.
    /// Only one default is possible for a given <see cref="ImageType"/>.
    /// </summary>
    [JsonPropertyName("Preferred")]
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// True if the image has been disabled. You must explicitly ask for these, for obvious reasons.
    /// </summary>
    [JsonPropertyName("Disabled")]
    public bool IsDisabled { get; set; } = false;

    /// <summary>
    /// Width of the image, if available.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Height of the image, if available.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// The relative path from the image base directory if the image is present
    /// on the server.
    /// </summary>
    [JsonPropertyName("RelativeFilepath")]
    public string? LocalPath { get; set; }

    /// <summary>
    /// True if the image is available.
    /// </summary>
    [JsonIgnore]
    public virtual bool IsAvailable
        => !string.IsNullOrEmpty(LocalPath);

    /// <summary>
    /// Get an URL to both download the image on the backend and preview it for
    /// the clients.
    /// </summary>
    /// <remarks>
    /// May or may not work 100% depending on how the servers and clients are
    /// set up, but better than nothing.
    /// </remarks>
    /// <returns>The image URL</returns>
    public string ToURLString()
        => new Uri(new Uri(Web.ImageHostUrl.Value), $"/Plugin/Shokofin/Host/Image/{Source}/{Type}/{ID}").ToString();
}

/// <summary>
/// Image source.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageSource
{
    /// <summary>
    ///
    /// </summary>
    AniDB = 1,

    /// <summary>
    ///
    /// </summary>
    TvDB = 2,

    /// <summary>
    ///
    /// </summary>
    TMDB = 3,

    /// <summary>
    ///
    /// </summary>
    Shoko = 100
}

/// <summary>
/// Image type.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageType
{
    /// <summary>
    ///
    /// </summary>
    Poster = 1,

    /// <summary>
    ///
    /// </summary>
    Banner = 2,

    /// <summary>
    ///
    /// </summary>
    Thumb = 3,

    /// <summary>
    ///
    /// </summary>
    Fanart = 4,

    /// <summary>
    ///
    /// </summary>
    Character = 5,

    /// <summary>
    ///
    /// </summary>
    Staff = 6,

    /// <summary>
    /// Static resources are only valid if the <see cref="Image.Source"/> is set to <see cref="ImageSource.Shoko"/>.
    /// </summary>
    Static = 100
}