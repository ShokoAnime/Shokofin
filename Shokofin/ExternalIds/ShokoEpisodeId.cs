using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

#nullable enable
namespace Shokofin.ExternalIds;

public class ShokoEpisodeId : IExternalId
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