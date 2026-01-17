using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class AnidbEpisodeId : IExternalId {
    #region IExternalId Implementation

    string IExternalId.ProviderName => ProviderNames.Anidb;

    string IExternalId.Key => ProviderNames.Anidb;

    ExternalIdMediaType? IExternalId.Type => ExternalIdMediaType.Episode;

    public bool Supports(IHasProviderIds item) => item is Episode;

#if NET9_0
#else
    string? IExternalId.UrlFormatString => null;
#endif 

    #endregion
}
