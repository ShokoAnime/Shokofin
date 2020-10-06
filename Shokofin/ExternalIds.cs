using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin
{
    public class ShokoGroupExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
            => item is Series;

        public string ProviderName
            => "Shoko Group";

        public string Key
            => "Shoko Group";

        public ExternalIdMediaType? Type
            => null;

        public string UrlFormatString
            => null;
    }

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

    public class ShokoFileExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
            => item is Episode || item is Movie;

        public string ProviderName
            => "Shoko File";

        public string Key
            => "Shoko File";

        public ExternalIdMediaType? Type
            => null;

        public string UrlFormatString
            => null;
    }
}