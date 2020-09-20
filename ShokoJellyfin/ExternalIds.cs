using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace ShokoJellyfin
{
    public class ShokoSeriesExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
            => item is Series || item is Movie || item is BoxSet;

        public string ProviderName
            => "Shoko Series";

        public string Key
            => "Shoko Series";

        public ExternalIdMediaType? Type
            => null;

        public string UrlFormatString
            => null;
    }

    public class ShokoEpisodeExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
            => item is Episode || item is Movie;

        public string ProviderName
            => "Shoko Episode";

        public string Key
            => "Shoko Episode";

        public ExternalIdMediaType? Type
            => null;

        public string UrlFormatString
            => null;
    }
}