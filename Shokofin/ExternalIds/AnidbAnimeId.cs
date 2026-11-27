using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class AnidbAnimeId : IExternalId {
    #region IExternalId Implementation

    string IExternalId.ProviderName => ProviderNames.Anidb;

    string IExternalId.Key => ProviderNames.Anidb;

    ExternalIdMediaType? IExternalId.Type => ExternalIdMediaType.Series;

    public bool Supports(IHasProviderIds item) => item is Series or Season;

#if NET9_0_OR_GREATER
#else
    string? IExternalId.UrlFormatString => null;
#endif 

    #endregion
}
