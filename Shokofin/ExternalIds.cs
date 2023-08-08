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
            => item is Series or BoxSet;

        public string ProviderName
            => "Shoko Group";

        public string Key
            => "Shoko Group";

        public ExternalIdMediaType? Type
            => null;

        public virtual string UrlFormatString
            => $"{Plugin.Instance.Configuration.PrettyHost}/webui/collection/group/{{0}}";
    }

    public class ShokoSeriesExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
            => item is Series or Season or Movie;

        public string ProviderName
            => "Shoko Series";

        public string Key
            => "Shoko Series";

        public ExternalIdMediaType? Type
            => null;

        public virtual string UrlFormatString
            => $"{Plugin.Instance.Configuration.PrettyHost}/webui/collection/series/{{0}}";
    }

    public class ShokoEpisodeExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
            => item is Episode or Movie;

        public string ProviderName
            => "Shoko Episode";

        public string Key
            => "Shoko Episode";

        public ExternalIdMediaType? Type
            => null;

        public virtual string UrlFormatString
            => $"{Plugin.Instance.Configuration.PrettyHost}/webui/redirect/episode/{{0}}";
    }

    public class ShokoFileExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
            => item is Episode or Movie;

        public string ProviderName
            => "Shoko File";

        public string Key
            => "Shoko File";

        public ExternalIdMediaType? Type
            => null;

        public virtual string UrlFormatString
            => null;
    }
}