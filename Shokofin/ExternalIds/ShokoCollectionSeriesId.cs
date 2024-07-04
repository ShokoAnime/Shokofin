using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class ShokoCollectionSeriesId : IExternalId
{
    public const string Name = "ShokoCollectionSeries";

    public bool Supports(IHasProviderIds item)
        => item is BoxSet;

    public string ProviderName
        => "Shoko Series";

    public string Key
        => Name;

    public ExternalIdMediaType? Type
        => null;

    public virtual string UrlFormatString
        => $"{Plugin.Instance.Configuration.PrettyUrl}/webui/collection/series/{{0}}";
}