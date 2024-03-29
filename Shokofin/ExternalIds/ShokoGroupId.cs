using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

#nullable enable
namespace Shokofin.ExternalIds;

public class ShokoGroupId : IExternalId
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