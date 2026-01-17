using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class AnidbCreatorId : IExternalId {
    #region IExternalId Implementation

    string IExternalId.ProviderName => ProviderNames.Anidb;

    string IExternalId.Key => ProviderNames.Anidb;

    ExternalIdMediaType? IExternalId.Type => ExternalIdMediaType.Person;

    public bool Supports(IHasProviderIds item) => item is Person;

#if NET9_0
#else
    string? IExternalId.UrlFormatString => null;
#endif 

    #endregion
}
