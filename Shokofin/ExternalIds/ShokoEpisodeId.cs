using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

#nullable enable
namespace Shokofin.ExternalIds;

public class ShokoEpisodeId : IExternalId
{
    public const string Name = "Shoko Episode";

    public bool Supports(IHasProviderIds item)
        => item is Episode or Movie;

    public string ProviderName
        => Name;

    public string Key
        => Name;

    public ExternalIdMediaType? Type
        => null;

    public virtual string UrlFormatString
        => $"{Plugin.Instance.Configuration.PrettyUrl}/webui/redirect/episode/{{0}}";
}