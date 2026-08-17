using System;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class Image {
    /// <summary>
    ///   The unique image id to use when identifying the image upstream.
    /// </summary>
    private string UniqueImageId => UID.HasValue ? UID.Value.ToString("D") : $"{Source}/{Type}/{ID}";

    /// <summary>
    /// AniDB, TMDB, etc.
    /// </summary>
    public string Source { get; set; } = "AniDB";

    /// <summary>
    /// Poster, Banner, etc.
    /// </summary>
    public string Type { get; set; } = "None";

    /// <summary>
    /// The image's id.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int ID { get; set; } = 0;

    /// <summary>
    /// The image's GUID, for newer versions of Shoko.
    /// </summary>
    public Guid? UID { get; set; }

    /// <summary>
    /// True if the image is marked as the preferred for the given shoko image
    /// type. Only one preferred is possible for a given image type.
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
    /// Newer property for checking if the image is available.
    /// </summary>
    [JsonPropertyName("Available")]
    public bool? IsMaybeAvailable { get; set; }

    /// <summary>
    /// True if the image is available.
    /// </summary>
    [JsonIgnore]
    public virtual bool IsAvailable
        => IsMaybeAvailable ?? !string.IsNullOrEmpty(LocalPath);

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
        UID = image.UID;
        IsPreferred = image.IsPreferred;
        IsDisabled = image.IsDisabled;
        LanguageCode = image.LanguageCode;
        Width = image.Width;
        Height = image.Height;
        IsMaybeAvailable = image.IsAvailable;
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
        => new Uri(new Uri(internalUrl ? Plugin.Instance.BaseUrl : Web.ImageHostUrl.BaseUrl), $"{(internalUrl ? Plugin.Instance.BasePath : Web.ImageHostUrl.BasePath)}/Shokofin/Host/Image/{UniqueImageId}").ToString();
}
