using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Shokofin.Web;

/// <summary>
/// Responsible for tracking the base url we need for the next set of images
/// to-be presented to a client.
/// </summary>
public class ImageHostUrl : IAsyncActionFilter
{
    /// <summary>
    /// The current image host base url to use.
    /// </summary>
    public static string BaseUrl { get; private set; } = "http://localhost:8096/";

    /// <summary>
    /// The current image host base path to use.
    /// </summary>
    public static string BasePath { get; private set; } = "/";

    private readonly object LockObj = new();

    private static Regex RemoteImagesRegex = new(@"/Items/(?<itemId>[0-9a-fA-F]{32})/RemoteImages$", RegexOptions.Compiled);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var uriBuilder = new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? (request.Scheme == "https" ? 443 : 80), $"{request.PathBase}{request.Path}", request.QueryString.HasValue ? request.QueryString.Value : null);
        var result = RemoteImagesRegex.Match(uriBuilder.Path);
        if (result.Success) {
            var path = result.Length == uriBuilder.Path.Length ? "" : uriBuilder.Path[..^result.Length];
            uriBuilder.Path = "";
            uriBuilder.Query = "";
            var uri = uriBuilder.ToString();
            lock (LockObj) {
                if (!string.Equals(uri, BaseUrl))
                    BaseUrl = uri;
                if (!string.Equals(path, BasePath))
                    BasePath = path;
            }
        }
        await next();
    }
}
