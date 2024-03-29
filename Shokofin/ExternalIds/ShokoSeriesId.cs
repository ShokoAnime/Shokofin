using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

#nullable enable
namespace Shokofin.ExternalIds;

public class ShokoSeriesId : IExternalId
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