using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class ShokoSeriesId : IExternalId
{
    public const string Name = "Shoko Series";

    public bool Supports(IHasProviderIds item)
        => item is Series or Season or Episode or Movie;

    public string ProviderName
        => Name;

    public string Key
        => Name;

    public ExternalIdMediaType? Type
        => null;

    public virtual string UrlFormatString
        => $"{Plugin.Instance.Configuration.PrettyUrl}/webui/collection/series/{{0}}";
}