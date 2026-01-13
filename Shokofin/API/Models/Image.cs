using System;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class Image {
    /// <summary>
    /// AniDB, TMDB, etc.
    /// </summary>
    public ImageSource Source { get; set; } = ImageSource.AniDB;

    /// <summary>
    /// Poster, Banner, etc.
    /// </summary>
    public ShokoImageType Type { get; set; } = ShokoImageType.Poster;

    /// <summary>
    /// The image's id.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int ID { get; set; } = 0;

    /// <summary>
    /// True if the image is marked as the preferred for the given
    /// <see cref="ShokoImageType"/>. Only one preferred is possible for a given
    /// <see cref="ShokoImageType"/>.
    /// </summary>
    [JsonPropertyName("Preferred")]
    public bool IsPreferred { get; set; } = false;

    /// <summary>
    /// True if the image has been disabled. You must explicitly ask for these,
    /// for hopefully obvious reasons.
    /// </summary>
    [JsonPropertyName("Disabled")]
    public bool IsDisabled { get; set; } = false;

    /// <summary>
    /// The language code for the image, if available.
    /// </summary>
    public string? LanguageCode { get; set; } = null;

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
    /// Community rating for the image, if available.
    /// </summary>
    public Rating? CommunityRating { get; set; }

    /// <summary>
    /// Json deserialization constructor.
    /// </summary>
    public Image() { }

    /// <summary>
    /// Copy constructor.
    /// </summary>
    public Image(Image image) : this() {
        Source = image.Source;
        Type = image.Type;
        ID = image.ID;
        IsPreferred = image.IsPreferred;
        IsDisabled = image.IsDisabled;
        LanguageCode = image.LanguageCode;
        Width = image.Width;
        Height = image.Height;
        LocalPath = image.LocalPath;
        CommunityRating = image.CommunityRating is { } rating ? new(rating) : null;
    }

    /// <summary>
    /// Get an URL to both download the image on the backend and preview it for
    /// the clients.
    /// </summary>
    /// <remarks>
    /// May or may not work 100% depending on how the servers and clients are
    /// set up, but better than nothing.
    /// </remarks>
    /// <returns>The image URL</returns>
    public string ToURLString(bool internalUrl = false)
        => new Uri(new Uri(internalUrl ? Plugin.Instance.BaseUrl : Web.ImageHostUrl.BaseUrl), $"{(internalUrl ? Plugin.Instance.BasePath : Web.ImageHostUrl.BasePath)}/Shokofin/Host/Image/{Source}/{Type}/{ID}").ToString();
}

/// <summary>
/// Image source.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageSource {
    /// <summary>
    ///
    /// </summary>
    AniDB = 1,

    /// <summary>
    ///
    /// </summary>
    TMDB = 2,

    /// <summary>
    ///
    /// </summary>
    Shoko = 100,
}

/// <summary>
/// Image type.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShokoImageType {
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
    Thumbnail = Thumb,

    /// <summary>
    ///
    /// </summary>
    Fanart = 4,

    /// <summary>
    ///
    /// </summary>
    Backdrop = Fanart,

    /// <summary>
    ///
    /// </summary>
    Character = 5,

    /// <summary>
    ///
    /// </summary>
    Staff = 6,

    /// <summary>
    /// Clear-text logo.
    /// </summary>
    Logo = 7,
}
