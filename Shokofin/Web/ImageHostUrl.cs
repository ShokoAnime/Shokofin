using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Shokofin.Web;

/// <summary>
/// Responsible for tracking the base url we need for the next set of images
/// to-be presented to a client.
/// </summary>
public class ImageHostUrl : IAsyncActionFilter {
    /// <summary>
    /// The internal base url. Will be null if the base url haven't been used
    /// yet.
    /// </summary>
    private static string? InternalBaseUrl { get; set; } = null;

    /// <summary>
    /// The current image host base url to use.
    /// </summary>
    public static string BaseUrl { get => InternalBaseUrl ??= Plugin.Instance.BaseUrl; }

    /// <summary>
    /// The internal base path. Will be null if the base path haven't been used
    /// yet.
    /// </summary>
    private static string? InternalBasePath { get; set; } = null;

    /// <summary>
    /// The current image host base path to use.
    /// </summary>
    public static string BasePath { get => InternalBasePath ??= Plugin.Instance.BasePath; }

    private static Guid? _currentItemId;

    public static Guid? CurrentItemId {
        get {
            lock (LockObj) {
                return _currentItemId;
            }
        }
    }

    private static readonly object LockObj = new();

    private static readonly Regex RemoteImagesRegex = new(@"/Items/(?<itemId>[0-9a-fA-F]{32})/RemoteImages$", RegexOptions.Compiled);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) {
        var request = context.HttpContext.Request;
        var uriBuilder = new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? (request.Scheme == "https" ? 443 : 80), $"{request.PathBase}{request.Path}", request.QueryString.HasValue ? request.QueryString.Value : null);
        var result = RemoteImagesRegex.Match(uriBuilder.Path);
        var itemId = Guid.Empty;
        if (result.Success) {
            itemId = Guid.Parse(result.Groups["itemId"].Value);
            var path = result.Length == uriBuilder.Path.Length ? "" : uriBuilder.Path[..^result.Length];
            uriBuilder.Path = "";
            uriBuilder.Query = "";
            var uri = uriBuilder.ToString();
            lock (LockObj) {
                _currentItemId = itemId;
                if (!string.Equals(uri, InternalBaseUrl))
                    InternalBaseUrl = uri;
                if (!string.Equals(path, InternalBasePath))
                    InternalBasePath = path;
            }
        }

        try {
            await next();
        }
        finally {
            if (Guid.Empty != itemId && _currentItemId == itemId) {
                lock (LockObj) {
                    if (Guid.Empty != itemId && _currentItemId == itemId) {
                        _currentItemId = null;
                    }
                }
            }
        }
    }
}
