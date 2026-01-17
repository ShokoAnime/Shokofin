using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class ShokoInternalId : IExternalId {
    public static string Name => MetadataProvider.Custom.ToString();

    public const string SeriesNamespace = "shoko://series/";

    public const string EpisodeNamespace = "shoko://episode/";

    public const string FileNamespace = "shoko://file/";

    #region IExternalId Implementation

    string IExternalId.ProviderName => Name;

    string IExternalId.Key => Name;

    ExternalIdMediaType? IExternalId.Type => null;

    bool IExternalId.Supports(IHasProviderIds item) => item is BoxSet or Series or Season or Video;

#if NET9_0
#else
    string? IExternalId.UrlFormatString => null;
#endif 

    #endregion
}
