using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API.Models;
using Shokofin.API.Models.AniDB;
using Shokofin.API.Models.Shoko;
using Shokofin.API.Models.TMDB;
using Shokofin.Configuration;
using Shokofin.Extensions;
using Shokofin.Utils;

namespace Shokofin.API;

/// <summary>
/// All API calls to Shoko needs to go through this gateway.
/// </summary>
public class ShokoApiClient : IDisposable {
    private static readonly TimeSpan _requestWaitLogThreshold = TimeSpan.FromMilliseconds(50);

    private readonly ILogger<ShokoApiClient> _logger;

    private int _pageSize;

    private int _maxInFlightRequests;

    private readonly UsageTracker _tracker;

    private readonly HttpClient _httpClient;

    private SemaphoreSlim _requestLimiter;

    private readonly GuardedMemoryCache _cache;

    private bool _connectionUsable;

    private static bool HasPluginsExposed {
        get => Plugin.Instance.Configuration.HasPluginsExposed;
        set {
            Plugin.Instance.Configuration.HasPluginsExposed = value;
            Plugin.Instance.UpdateConfiguration();
        }
    }

    public ShokoApiClient(ILogger<ShokoApiClient> logger, UsageTracker tracker) {
        var config = Plugin.Instance.Configuration;

        _logger = logger;
        _tracker = tracker;
        _httpClient = new HttpClient {
            Timeout = TimeSpan.FromMinutes(10),
        };
        _pageSize = config.Debug.SeriesPageSize;
        _maxInFlightRequests = config.Debug.MaxInFlightRequests;
        _requestLimiter = new(_maxInFlightRequests, _maxInFlightRequests);
        _cache = new(
            logger,
            new() { ExpirationScanFrequency = config.Debug.ExpirationScanFrequency },
            new() { AbsoluteExpirationRelativeToNow = config.Debug.AbsoluteExpirationRelativeToNow }
        );
        _connectionUsable = Plugin.Instance.Configuration.IsConnectionUsable;

        Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
        _tracker.Stalled += OnTrackerStalled;
    }

    ~ShokoApiClient() {
        _tracker.Stalled -= OnTrackerStalled;
        Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
    }

    private void OnConfigurationChanged(object? sender, PluginConfiguration config) {
        var maxRequests = config.Debug.MaxInFlightRequests;
        if (maxRequests != _maxInFlightRequests) {
            _logger.LogInformation("Updating request limit to {MaxRequests}", maxRequests);
            _requestLimiter = new(maxRequests, maxRequests);
            _maxInFlightRequests = maxRequests;
        }

        if (config.Debug.SeriesPageSize != _pageSize) {
            _logger.LogInformation("Updating page size to {PageSize}", config.Debug.SeriesPageSize);
            _pageSize = config.Debug.SeriesPageSize;
        }

        var connectionUsable = config.IsConnectionUsable;
        if (_connectionUsable != connectionUsable) {
            _connectionUsable = connectionUsable;
            if (connectionUsable) {
                var hasPluginsExposed = Task.Run(() => CheckIfPluginsExposed()).GetAwaiter().GetResult();
                if (hasPluginsExposed != HasPluginsExposed) {
                    HasPluginsExposed = hasPluginsExposed;
                }
            }
        }
    }

    private void OnTrackerStalled(object? sender, EventArgs eventArgs) {
        if (Plugin.Instance.Configuration.Debug.AutoClearClientCache)
            Clear();
    }

    public void Clear() {
        _logger.LogDebug("Clearing data…");
        _cache.Clear();
    }

    public void Dispose() {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _cache.Dispose();
    }

    #region Base Implementation

    private async Task<ReturnType?> GetOrNull<ReturnType>(string url, string? apiKey = null, bool skipCache = false, CancellationToken cancellationToken = default) {
        try {
            return await Get<ReturnType>(url, HttpMethod.Get, apiKey, skipCache, cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException e) when (e.StatusCode == HttpStatusCode.NotFound) {
            return default;
        }
    }

    private Task<ReturnType> Get<ReturnType>(string url, string? apiKey = null, bool skipCache = false, CancellationToken cancellationToken = default)
        => Get<ReturnType>(url, HttpMethod.Get, apiKey, skipCache, cancellationToken);

    private async Task<ReturnType> Get<ReturnType>(string url, HttpMethod method, string? apiKey = null, bool skipCache = false, CancellationToken cancellationToken = default) {
        if (skipCache) {
            _logger.LogTrace("Creating raw object for {Method} {URL}", method, url);
            var response = await Get(url, method, apiKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                throw ApiException.FromResponse(response);
            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false) ??
                throw new ApiException(response.StatusCode, nameof(ShokoApiClient), "Unexpected null return value.");
            return value;
        }

        return await _cache.GetOrCreateAsync(
            $"apiKey={apiKey ?? "default"},method={method},body=null,url={url},object",
            (_) => _logger.LogTrace("Reusing object for {Method} {URL}", method, url),
            async () => {
                _logger.LogTrace("Creating cached object for {Method} {URL}", method, url);
                var response = await Get(url, method, apiKey).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw ApiException.FromResponse(response);
                var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false) ??
                    throw new ApiException(response.StatusCode, nameof(ShokoApiClient), "Unexpected null return value.");
                return value;
            }
        ).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> Get(string url, HttpMethod method, string? apiKey = null, bool skipApiKey = false, CancellationToken cancellationToken = default) {
        // Use the default key if no key was provided.
        apiKey ??= Plugin.Instance.Configuration.ApiKey;

        // Check if we have a key to use.
        if (string.IsNullOrEmpty(apiKey) && !skipApiKey)
            throw new HttpRequestException("Unable to call the API before an connection is established to Shoko Server!", null, HttpStatusCode.BadRequest);

        var version = Plugin.Instance.Configuration.ServerVersion;
        if (version == null) {
            version = await GetVersion().ConfigureAwait(false)
                ?? throw new HttpRequestException("Unable to call the API before an connection is established to Shoko Server!", null, HttpStatusCode.BadRequest);

            Plugin.Instance.Configuration.ServerVersion = version;
            Plugin.Instance.UpdateConfiguration();
        }

        var result = await _requestLimiter.WaitAsync(_requestWaitLogThreshold, cancellationToken).ConfigureAwait(false);
        if (!result) {
            _logger.LogTrace("Waiting for our turn to try {Method} {URL}", method, url);
            await _requestLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogTrace("Got our turn to try {Method} {URL}", method, url);
        }
        cancellationToken.ThrowIfCancellationRequested();
        try {
            _logger.LogTrace("Trying to {Method} {URL}", method, url);
            var remoteUrl = string.Concat(Plugin.Instance.Configuration.Url, url);

            using var requestMessage = new HttpRequestMessage(method, remoteUrl);
            requestMessage.Content = new StringContent(string.Empty);
            if (!string.IsNullOrEmpty(apiKey))
                requestMessage.Headers.Add("apikey", apiKey);
            var timeStart = DateTime.UtcNow;
            var response = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new HttpRequestException("Invalid or expired API Token. Please reconnect the plugin to Shoko Server by resetting the connection or deleting and re-adding the user in the plugin settings.", null, HttpStatusCode.Unauthorized);
            _logger.LogTrace("API returned response with status code {StatusCode} for {Method} {URL} in {Elapsed}", response.StatusCode, method, url, DateTime.UtcNow - timeStart);
            return response;
        }
        catch (HttpRequestException ex) {
            _logger.LogWarning(ex, "Unable to connect to complete the request to Shoko.");
            throw;
        }
        finally {
            _requestLimiter.Release();
        }
    }

    private Task<ReturnType> Post<Type, ReturnType>(string url, Type body, string? apiKey = null, bool skipCache = true, CancellationToken cancellationToken = default)
        => Post<Type, ReturnType>(url, HttpMethod.Post, body, apiKey, skipCache, cancellationToken);

    private async Task<ReturnType> Post<Type, ReturnType>(string url, HttpMethod method, Type body, string? apiKey = null, bool skipCache = true, CancellationToken cancellationToken = default) {
        var bodyHash = Convert.ToHexString(MD5.HashData(JsonSerializer.SerializeToUtf8Bytes(body)));
        if (skipCache) {
            _logger.LogTrace("Creating raw object for {Method} {URL} ({Hash})", method, url, bodyHash);
            var response = await Post(url, method, body, bodyHash, apiKey, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                throw ApiException.FromResponse(response);
            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false) ??
                throw new ApiException(response.StatusCode, nameof(ShokoApiClient), "Unexpected null return value.");
            return value;
        }

        return await _cache.GetOrCreateAsync(
            $"apiKey={apiKey ?? "default"},method={method},body={bodyHash},url={url},object",
            (_) => _logger.LogTrace("Reusing object for {Method} {URL} ({Hash})", method, url, bodyHash),
            async () => {
                _logger.LogTrace("Creating cached object for {Method} {URL} ({Hash})", method, url, bodyHash);
                var response = await Post(url, method, body, bodyHash, apiKey, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw ApiException.FromResponse(response);
                var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false) ??
                    throw new ApiException(response.StatusCode, nameof(ShokoApiClient), "Unexpected null return value.");
                return value;
            }
        ).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> Post<Type>(string url, HttpMethod method, Type body, string? bodyHash = null, string? apiKey = null, CancellationToken cancellationToken = default) {
        // Use the default key if no key was provided.
        apiKey ??= Plugin.Instance.Configuration.ApiKey;

        // Compute the hash if it hasn't been pre-computed.
        bodyHash ??= Convert.ToHexString(MD5.HashData(JsonSerializer.SerializeToUtf8Bytes(body)));

        // Check if we have a key to use.
        if (string.IsNullOrEmpty(apiKey))
            throw new HttpRequestException("Unable to call the API before an connection is established to Shoko Server!", null, HttpStatusCode.BadRequest);

        var version = Plugin.Instance.Configuration.ServerVersion;
        if (version == null) {
            version = await GetVersion().ConfigureAwait(false)
                ?? throw new HttpRequestException("Unable to call the API before an connection is established to Shoko Server!", null, HttpStatusCode.BadRequest);

            Plugin.Instance.Configuration.ServerVersion = version;
            Plugin.Instance.UpdateConfiguration();
        }

        var result = await _requestLimiter.WaitAsync(_requestWaitLogThreshold, cancellationToken).ConfigureAwait(false);
        if (!result) {
            _logger.LogTrace("Waiting for our turn to try {Method} {URL} with body {HashCode}", method, url, bodyHash);
            await _requestLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogTrace("Got our turn to try {Method} {URL} with body {HashCode}", method, url, bodyHash);
        }
        cancellationToken.ThrowIfCancellationRequested();
        try {
            _logger.LogTrace("Trying to {Method} {URL} with body {HashCode}", method, url, bodyHash);
            var remoteUrl = string.Concat(Plugin.Instance.Configuration.Url, url);

            if (method == HttpMethod.Get)
                throw new HttpRequestException("Get requests cannot contain a body.");

            if (method == HttpMethod.Head)
                throw new HttpRequestException("Head requests cannot contain a body.");

            using var requestMessage = new HttpRequestMessage(method, remoteUrl);
            requestMessage.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            requestMessage.Headers.Add("apikey", apiKey);
            var timeStart = DateTime.UtcNow;
            var response = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new HttpRequestException("Invalid or expired API Token. Please reconnect the plugin to Shoko Server by resetting the connection or deleting and re-adding the user in the plugin settings.", null, HttpStatusCode.Unauthorized);
            _logger.LogTrace("API returned response with status code {StatusCode} for {Method} {URL} with body {HashCode} in {Elapsed}", response.StatusCode, method, url, bodyHash, DateTime.UtcNow - timeStart);
            return response;
        }
        catch (HttpRequestException ex) {
            _logger.LogWarning(ex, "Unable to connect to complete the request to Shoko.");
            throw;
        }
        finally {
            _requestLimiter.Release();
        }
    }

    #endregion Base Implementation

    #region Authentication

    public async Task<ApiKey?> GetApiKey(string username, string password, bool forUser = false) {
        var version = Plugin.Instance.Configuration.ServerVersion;
        if (version == null) {
            version = await GetVersion().ConfigureAwait(false)
                ?? throw new HttpRequestException("Unable to connect to Shoko Server to read the version.", null, HttpStatusCode.BadGateway);

            Plugin.Instance.Configuration.ServerVersion = version;
            Plugin.Instance.UpdateConfiguration();
        }

        var postData = JsonSerializer.Serialize(new Dictionary<string, string> {
            {"user", username},
            {"pass", password},
            {"device", forUser ? "Shoko Jellyfin Plugin (Shokofin) - User Key" : "Shoko Jellyfin Plugin (Shokofin)"},
        });
        var apiBaseUrl = Plugin.Instance.Configuration.Url;
        var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/auth", new StringContent(postData, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<ApiKey>(stream).ConfigureAwait(false);
        return result;
    }

    #endregion

    #region Version

    public async Task<ComponentVersion?> GetVersion() {
        try {
            var apiBaseUrl = Plugin.Instance.Configuration.Url;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/v3/Init/Version", source.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK) {
                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var componentVersionSet = await JsonSerializer.DeserializeAsync<ComponentVersionSet>(stream).ConfigureAwait(false);
                return componentVersionSet?.Server;
            }
        }
        catch (Exception e) {
            _logger.LogTrace(e, "Unable to connect to Shoko Server to read the version. Exception; {e}", e.Message);
            return null;
        }

        return null;
    }

    public async Task<bool> CheckIfPluginsExposed(CancellationToken cancellationToken = default)
        => (await Get($"/api/v3/Plugin", HttpMethod.Get, cancellationToken: cancellationToken).ConfigureAwait(false)) is { StatusCode: HttpStatusCode.OK };

    public async Task<string?> GetWebPrefix(CancellationToken cancellationToken = default)
    {
        try {
            var settingsResponse = await Get("/api/v3/Settings", HttpMethod.Get, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (settingsResponse.StatusCode != HttpStatusCode.OK)
                return null;
            var settingsJson = await settingsResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var settings = JsonNode.Parse(settingsJson)!;
            var value = settings["Web"]?["WebUIPrefix"]?.GetValue<string>();
            if (value is null)
                return "webui";
            return value;
        }
        catch (HttpRequestException httpEx) when (httpEx.Message.Contains("Shoko Server")) {
            return null;
        }
    }

    #endregion

    #region Image

    public Task<HttpResponseMessage> GetImageAsync(ImageSource imageSource, ShokoImageType imageType, int imageId)
        => Get($"/api/v3/Image/{imageSource}/{imageType}/{imageId}", HttpMethod.Get, null, true);

    #endregion

    #region Managed Folder

    public async Task<ManagedFolder?> GetManagedFolder(int managedFolderId)
        => HasPluginsExposed
            ? await GetOrNull<ManagedFolder>($"/api/v3/ManagedFolder/{managedFolderId}").ConfigureAwait(false)
            : await GetOrNull<ManagedFolder>($"/api/v3/ImportFolder/{managedFolderId}").ConfigureAwait(false);

    public async Task<ListResult<File>> GetFilesInManagedFolder(int managedFolderId, string subPath, int page = 1)
        => HasPluginsExposed
            ? await GetOrNull<ListResult<File>>($"/api/v3/ManagedFolder/{managedFolderId}/File?pageSize=1000&page={page}&include=XRefs&folderPath={Uri.EscapeDataString(subPath)}").ConfigureAwait(false) ?? new()
            : await GetOrNull<ListResult<File>>($"/api/v3/ImportFolder/{managedFolderId}/File?pageSize=1000&page={page}&include=XRefs&folderPath={Uri.EscapeDataString(subPath)}").ConfigureAwait(false) ?? new();

    #endregion

    #region File

    public async Task<File?> GetFile(string fileId)
        => HasPluginsExposed
            ? await GetOrNull<File>($"/api/v3/File/{fileId}?include=XRefs,ReleaseInfo").ConfigureAwait(false)
            :  await GetOrNull<File>($"/api/v3/File/{fileId}?include=XRefs&includeDataFrom=AniDB").ConfigureAwait(false);

    public Task<File?> GetFileByEd2kAndFileSize(string ed2k, long fileSize)
        => GetOrNull<File>($"/api/v3/File/Hash/ED2K?hash={Uri.EscapeDataString(ed2k)}&size={fileSize}");

    public async Task<IReadOnlyList<File>> GetFileByPath(string relativePath)
        => HasPluginsExposed
            ? await Get<IReadOnlyList<File>>($"/api/v3/File/PathEndsWith?path={Uri.EscapeDataString(relativePath)}&include=XRefs,ReleaseInfo&limit=10").ConfigureAwait(false)
            : await Get<IReadOnlyList<File>>($"/api/v3/File/PathEndsWith?path={Uri.EscapeDataString(relativePath)}&include=XRefs&includeDataFrom=AniDB&limit=10").ConfigureAwait(false);

    #region File User Stats

    public async Task<File.UserStats?> GetFileUserStats(string fileId, string? apiKey = null) {
        try {
            return await Get<File.UserStats>($"/api/v3/File/{fileId}/UserStats", apiKey, true).ConfigureAwait(false);
        }
        catch (ApiException e) when (e.StatusCode is HttpStatusCode.NotFound) {
            // File user stats were not found.
            if (!e.Message.Contains("FileUserStats"))
                _logger.LogWarning("Unable to find user stats for a file that doesn't exist. (File={FileID})", fileId);
            return null;
        }
    }

    public Task<File.UserStats> PutFileUserStats(string fileId, File.UserStats userStats, string? apiKey = null)
        => Post<File.UserStats, File.UserStats>($"/api/v3/File/{fileId}/UserStats", HttpMethod.Put, userStats, apiKey);

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, bool watched, string apiKey)
        => await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&watched={watched}", HttpMethod.Patch, apiKey).ConfigureAwait(false) is { } response &&
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.NoContent;

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, long progress, string apiKey)
        => await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&resumePosition={progress}", HttpMethod.Patch, apiKey).ConfigureAwait(false) is { } response &&
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.NoContent;

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, long? progress, bool watched, string apiKey)
        => !progress.HasValue
            ? await ScrobbleFile(fileId, episodeId, eventName, watched, apiKey).ConfigureAwait(false)
            : await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&resumePosition={progress.Value}&watched={watched}", HttpMethod.Patch, apiKey).ConfigureAwait(false) is { } response &&
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.NoContent;

    #endregion

    #endregion

    #region Shoko Episode

    public Task<ShokoEpisode?> GetShokoEpisode(string episodeId)
        => _cache.GetOrCreateAsync(
            $"shoko-episode:{episodeId}",
            (_) => _logger.LogTrace("Reusing object for Shoko episode {EpisodeId}", episodeId),
            async () => {
                _logger.LogTrace("Trying to get Shoko episode {EpisodeId}", episodeId);
                var timeStart = DateTime.UtcNow;
                var episode = await GetOrNull<ShokoEpisode>($"/api/v3/Episode/{episodeId}?includeDataFrom=AniDB&includeXRefs=true", skipCache: true).ConfigureAwait(false);
                _logger.LogTrace("Got Shoko episode {EpisodeId} in {Time}", episodeId, DateTime.UtcNow - timeStart);
                return episode;
            }
        );

    public Task<IReadOnlyList<ShokoEpisode>> GetShokoEpisodesInShokoSeries(string seriesId)
        => _cache.GetOrCreateAsync<IReadOnlyList<ShokoEpisode>>(
            $"shoko-series-episodes:{seriesId}",
            (_) => _logger.LogTrace("Reusing object for Shoko episodes for shoko series {SeriesId}", seriesId),
            async () => {
                _logger.LogTrace("Trying to get Shoko episodes for shoko series {SeriesId}", seriesId);
                var timeStart = DateTime.UtcNow;
                var firstPage = await GetOrNull<ListResult<ShokoEpisode>>($"/api/v3/Series/{seriesId}/Episode?pageSize={_pageSize}&includeHidden=true&includeMissing=true&includeUnaired=true&includeDataFrom=AniDB&includeXRefs=true", skipCache: true).ConfigureAwait(false);
                if (firstPage is null)
                    return [];

                var pages = new List<IReadOnlyList<ShokoEpisode>>() { firstPage.List };
                if (_pageSize > 0 && firstPage.Total > _pageSize) {
                    var totalPages = (int)Math.Ceiling((float)firstPage.Total / firstPage.List.Count);
                    for (var page = 2; page <= totalPages; page++) {
                        var pageData = await Get<ListResult<ShokoEpisode>>($"/api/v3/Series/{seriesId}/Episode?pageSize={_pageSize}&includeHidden=true&includeMissing=true&includeUnaired=true&includeDataFrom=AniDB&includeXRefs=true&page={page}", skipCache: true).ConfigureAwait(false);
                        pages.Add(pageData.List);
                    }
                }

                foreach (var page in pages) {
                    foreach (var episode in page) {
                        _cache.Set($"shoko-episode:{episode.Id}", episode);
                    }
                }

                _logger.LogTrace("Got Shoko episodes for shoko series {SeriesId} in {Elapsed}", seriesId, DateTime.UtcNow - timeStart);
                return [..pages.SelectMany(x => x)];
            }
        );

    public async Task<IReadOnlyList<ShokoEpisode>> GetShokoEpisodesForTmdbEpisode(string tmdbEpisodeId)
        => await GetOrNull<IReadOnlyList<ShokoEpisode>>($"/api/v3/TMDB/Episode/{tmdbEpisodeId}/Shoko/Episode?includeDataFrom=AniDB&includeXRefs=true").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<ShokoEpisode>> GetShokoEpisodesForTmdbMovie(string tmdbMovieId)
        => await GetOrNull<IReadOnlyList<ShokoEpisode>>($"/api/v3/TMDB/Movie/{tmdbMovieId}/Shoko/Episode?includeDataFrom=AniDB&includeXRefs=true").ConfigureAwait(false) ?? [];

    public async Task<EpisodeImages?> GetImagesForShokoEpisode(string episodeId, CancellationToken cancellationToken = default) {
        var episodeImages = await GetOrNull<EpisodeImages>($"/api/v3/Episode/{episodeId}/Images", cancellationToken: cancellationToken).ConfigureAwait(false);
        if (episodeImages is null)
            return null;

        // If the episode has no 'movie' images, get the series images to compensate.
        if (episodeImages.Posters.Count is 0) {
            // Separate 'episode' thumbnails from series images if there are any.
            if (episodeImages is { Backdrops.Count: > 0, Thumbnails.Count: 0 })
            {
                episodeImages.Thumbnails = episodeImages.Backdrops;
                episodeImages.Backdrops = [];
            }

            var episode1 = await GetShokoEpisode(episodeId).ConfigureAwait(false);
            var seriesImages1 = await GetImagesForShokoSeries(episode1!.IDs.ParentSeries.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false) ?? new();

            episodeImages.Posters = seriesImages1.Posters;
            episodeImages.Logos = seriesImages1.Logos;
            episodeImages.Banners = seriesImages1.Banners;
            episodeImages.Backdrops = seriesImages1.Backdrops;
        }

        return episodeImages;
    }

    #endregion

    #region Shoko Series

    public Task<IReadOnlyList<int>> GetShokoSeriesIdsForFilter(string filter, bool skipCache = true, CancellationToken cancellationToken = default)
        => Post<JsonDocument, IReadOnlyList<int>>($"/api/v3/Filter/Preview/Series/OnlyIDs", HttpMethod.Post, JsonDocument.Parse(filter), skipCache: skipCache, cancellationToken: cancellationToken);

    public Task<ShokoSeries?> GetShokoSeries(string seriesId)
        => GetOrNull<ShokoSeries>($"/api/v3/Series/{seriesId}?includeDataFrom=AniDB");

    public Task<ShokoSeries?> GetShokoSeriesForAnidbAnime(string animeId)
        => GetOrNull<ShokoSeries>($"/api/v3/Series/AniDB/{animeId}/Series?includeDataFrom=AniDB");

    public Task<ShokoSeries?> GetShokoSeriesForShokoEpisode(string episodeId)
        => GetOrNull<ShokoSeries>($"/api/v3/Episode/{episodeId}/Series?includeDataFrom=AniDB");

    public async Task<IReadOnlyList<ShokoSeries>> GetShokoSeriesForDirectory(string directoryName)
        => await GetOrNull<IReadOnlyList<ShokoSeries>>($"/api/v3/Series/PathEndsWith/{Uri.EscapeDataString(directoryName)}").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<ShokoSeries>> GetShokoSeriesForTmdbMovie(string movieId)
        => await GetOrNull<IReadOnlyList<ShokoSeries>>($"/api/v3/TMDB/Movie/{movieId}/Shoko/Series?includeDataFrom=AniDB").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<ShokoSeries>> GetShokoSeriesForTmdbShow(string showId)
        => await GetOrNull<IReadOnlyList<ShokoSeries>>($"/api/v3/TMDB/Show/{showId}/Shoko/Series?includeDataFrom=AniDB").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<ShokoSeries>> GetShokoSeriesInGroup(string groupId, int filterId = 0, bool recursive = false)
        => await GetOrNull<IReadOnlyList<ShokoSeries>>($"/api/v3/Filter/{filterId}/Group/{groupId}/Series?recursive={recursive}&includeMissing=true&includeIgnored=false&includeDataFrom=AniDB").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<Role>> GetCastForShokoSeries(string seriesId)
        => await GetOrNull<IReadOnlyList<Role>>($"/api/v3/Series/{seriesId}/Cast?includeDataFrom=AniDB").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<Relation>> GetRelationsForShokoSeries(string seriesId)
        => await GetOrNull<IReadOnlyList<Relation>>($"/api/v3/Series/{seriesId}/Relations").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<Tag>> GetTagsForShokoSeries(string seriesId)
        => await GetOrNull<IReadOnlyList<Tag>>($"/api/v3/Series/{seriesId}/Tags?filter=0&excludeDescriptions=true").ConfigureAwait(false) ?? [];

    public Task<Images?> GetImagesForShokoSeries(string seriesId, CancellationToken cancellationToken = default)
        => GetOrNull<Images>($"/api/v3/Series/{seriesId}/Images", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<File>> GetFilesForShokoSeries(string seriesId)
        => HasPluginsExposed
            ? (await GetOrNull<ListResult<File>>($"/api/v3/Series/{seriesId}/File?pageSize=0&include=XRefs,ReleaseInfo", skipCache: true).ConfigureAwait(false))?.List ?? []
            : (await GetOrNull<ListResult<File>>($"/api/v3/Series/{seriesId}/File?pageSize=0&include=XRefs&includeDataFrom=AniDB", skipCache: true).ConfigureAwait(false))?.List ?? [];

    public async Task<IReadOnlyList<TmdbEpisodeCrossReference>> GetTmdbCrossReferencesForShokoSeries(string seriesId)
        => (await GetOrNull<ListResult<TmdbEpisodeCrossReference>>($"/api/v3/Series/{seriesId}/TMDB/Show/CrossReferences/Episode?pageSize=0").ConfigureAwait(false))?.List ?? [];

    #endregion

    #region Shoko Group

    public Task<ShokoGroup?> GetShokoGroup(string groupId)
        => GetOrNull<ShokoGroup>($"/api/v3/Group/{groupId}");

    public Task<ShokoGroup?> GetShokoGroupForShokoSeries(string seriesId)
        => GetOrNull<ShokoGroup>($"/api/v3/Series/{seriesId}/Group");

    public async Task<IReadOnlyList<ShokoGroup>> GetShokoGroupsInShokoGroup(string groupId)
        => await GetOrNull<IReadOnlyList<ShokoGroup>>($"/api/v3/Group/{groupId}/Group?includeEmpty=true").ConfigureAwait(false) ?? [];

    #endregion

    #region AniDB Anime

    public Task<ListResult<AnidbAnime>> GetAllAnidbAnime(string query = "", int page = 1, int pageSize = 100)
        => Get<ListResult<AnidbAnime>>($"/api/v3/Series/AniDB/Search?local=true&searchById=false&pageSize={pageSize}&page={page}&query={Uri.EscapeDataString(query)}");

    #endregion

    #region TMDB Episode

    public Task<TmdbEpisode?> GetTmdbEpisode(string episodeId, bool useDefaultOrdering = false)
        => _cache.GetOrCreateAsync(
            $"tmdb-episode:{episodeId}",
            (_) => _logger.LogTrace("Reusing object for TMDB episode {EpisodeId}", episodeId),
            async () => {
                _logger.LogTrace("Trying to get TMDB episode {EpisodeId}", episodeId);
                var timeStart = DateTime.UtcNow;
                var episode = await GetOrNull<TmdbEpisode>($"/api/v3/TMDB/Episode/{episodeId}?include=Titles,Overviews,Cast,Crew,Ordering,FileCrossReferences{(useDefaultOrdering ? "&alternateOrderingID=default" : "")}", skipCache: true);
                _logger.LogTrace("Got TMDB episode {EpisodeId} in {Time}", episodeId, DateTime.UtcNow - timeStart);
                return episode;
            }
        );

    public Task<IReadOnlyList<TmdbEpisode>> GetTmdbEpisodesInTmdbSeason(string seasonId)
        => _cache.GetOrCreateAsync<IReadOnlyList<TmdbEpisode>>(
            $"tmdb-season-episodes:{seasonId}",
            (_) => _logger.LogTrace("Reusing object for TMDB episodes for season {SeasonId}", seasonId),
            async () => {
                _logger.LogTrace("Trying to get TMDB episodes for season {SeasonId}", seasonId);
                var timeStart = DateTime.UtcNow;
                var firstPage = await GetOrNull<ListResult<TmdbEpisode>>($"/api/v3/TMDB/Season/{seasonId}/Episode?pageSize={_pageSize}&include=Titles,Overviews,Cast,Crew,Ordering,FileCrossReferences", skipCache: true).ConfigureAwait(false);
                if (firstPage is null)
                    return [];

                var pages = new List<IReadOnlyList<TmdbEpisode>>() { firstPage.List };
                if (_pageSize > 0 && firstPage.Total > _pageSize) {
                    var totalPages = (int)Math.Ceiling((float)firstPage.Total / firstPage.List.Count);
                    for (var page = 2; page <= totalPages; page++) {
                        var pageData = await Get<ListResult<TmdbEpisode>>($"/api/v3/TMDB/Season/{seasonId}/Episode?pageSize={_pageSize}&include=Titles,Overviews,Cast,Crew,Ordering,FileCrossReferences&page={page}", skipCache: true).ConfigureAwait(false);
                        pages.Add(pageData.List);
                    }
                }

                foreach (var page in pages) {
                    foreach (var episode in page) {
                        _cache.Set($"tmdb-episode:{episode.Id}", episode);
                    }
                }

                _logger.LogTrace("Got TMDB episodes for season {SeasonId} in {Elapsed}", seasonId, DateTime.UtcNow - timeStart);
                return [..pages.SelectMany(x => x)];
            }
        );

    public Task<IReadOnlyList<TmdbEpisode>> GetTmdbEpisodesInTmdbShow(string showId)
        => _cache.GetOrCreateAsync<IReadOnlyList<TmdbEpisode>>(
            $"tmdb-show-episodes:{showId}",
            (_) => _logger.LogTrace("Reusing object for TMDB episodes for show {ShowId}", showId),
            async () => {
                _logger.LogTrace("Trying to get TMDB episodes for show {ShowId}", showId);
                var timeStart = DateTime.UtcNow;
                var firstPage = await GetOrNull<ListResult<TmdbEpisode>>($"/api/v3/TMDB/Show/{showId}/Episode?pageSize={_pageSize}&include=Titles,Overviews,Cast,Crew,Ordering,FileCrossReferences", skipCache: true).ConfigureAwait(false);
                if (firstPage is null)
                    return [];

                var pages = new List<IReadOnlyList<TmdbEpisode>>() { firstPage.List };
                if (_pageSize > 0 && firstPage.Total > _pageSize) {
                    var totalPages = (int)Math.Ceiling((float)firstPage.Total / firstPage.List.Count);
                    for (var page = 2; page <= totalPages; page++) {
                        var pageData = await Get<ListResult<TmdbEpisode>>($"/api/v3/TMDB/Show/{showId}/Episode?pageSize={_pageSize}&include=Titles,Overviews,Cast,Crew,Ordering,FileCrossReferences&page={page}", skipCache: true).ConfigureAwait(false);
                        pages.Add(pageData.List);
                    }
                }

                foreach (var page in pages) {
                    foreach (var episode in page) {
                        _cache.Set($"tmdb-episode:{episode.Id}", episode);
                    }
                }

                _logger.LogTrace("Got TMDB episodes for show {ShowId} in {Elapsed}", showId, DateTime.UtcNow - timeStart);
                return [..pages.SelectMany(x => x)];
            }
        );

    public Task<EpisodeImages?> GetImagesForTmdbEpisode(string episodeId, CancellationToken cancellationToken = default)
        => GetOrNull<EpisodeImages>($"/api/v3/TMDB/Episode/{episodeId}/Images", cancellationToken: cancellationToken);

    #endregion

    #region TMDB Season

    public Task<TmdbSeason?> GetTmdbSeasonForTmdbEpisode(string episodeId)
        => GetOrNull<TmdbSeason>($"/api/v3/TMDB/Episode/{episodeId}/Season?include=Titles,Overviews,YearlySeasons");

    public Task<TmdbSeason?> GetTmdbSeason(string seasonId)
        => GetOrNull<TmdbSeason>($"/api/v3/TMDB/Season/{seasonId}?include=Titles,Overviews,YearlySeasons");

    public async Task<IReadOnlyList<TmdbSeason>> GetTmdbSeasonsInTmdbShow(string showId)
        => (await GetOrNull<ListResult<TmdbSeason>>($"/api/v3/TMDB/Show/{showId}/Season?pageSize=0&include=Titles,Overviews").ConfigureAwait(false))?.List ?? [];

    public Task<Images?> GetImagesForTmdbSeason(string seasonId, CancellationToken cancellationToken = default)
        => GetOrNull<Images>($"/api/v3/TMDB/Season/{seasonId}/Images", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<File>> GetFilesForTmdbSeason(string seasonId)
        => HasPluginsExposed
            ? (await GetOrNull<ListResult<File>>($"/api/v3/TMDB/Season/{seasonId}/File?pageSize=0&include=XRefs,ReleaseInfo", skipCache: true).ConfigureAwait(false))?.List ?? []
            : (await GetOrNull<ListResult<File>>($"/api/v3/TMDB/Season/{seasonId}/File?pageSize=0&include=XRefs&includeDataFrom=AniDB", skipCache: true).ConfigureAwait(false))?.List ?? [];

    #endregion

    #region TMDB Show

    public Task<TmdbShow?> GetTmdbShowForSeason(string seasonId)
        => GetOrNull<TmdbShow>($"/api/v3/TMDB/Season/{seasonId}/Show?include=Titles,Overviews,Keywords,Studios,ContentRatings,ProductionCountries");

    public Task<Images?> GetImagesForTmdbShow(string showId, CancellationToken cancellationToken = default)
        => GetOrNull<Images>($"/api/v3/TMDB/Show/{showId}/Images", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<TmdbEpisodeCrossReference>> GetTmdbCrossReferencesForTmdbShow(string showId)
        => (await GetOrNull<ListResult<TmdbEpisodeCrossReference>>($"/api/v3/TMDB/Show/{showId}/Episode/CrossReferences?pageSize=0").ConfigureAwait(false))?.List ?? [];

    #endregion

    #region TMDB Movie

    public Task<TmdbMovie?> GetTmdbMovie(string movieId)
        => GetOrNull<TmdbMovie>($"/api/v3/TMDB/Movie/{movieId}?include=Titles,Overviews,Keywords,Studios,ContentRatings,ProductionCountries,Cast,Crew,FileCrossReferences,YearlySeasons");

    public async Task<IReadOnlyList<TmdbMovie>> GetTmdbMoviesInMovieCollection(string collectionId)
        => await GetOrNull<IReadOnlyList<TmdbMovie>>($"/api/v3/TMDB/Movie/Collection/{collectionId}/Movie?include=Titles,Overviews,Keywords,Studios,ContentRatings,ProductionCountries,Cast,Crew,FileCrossReferences,YearlySeasons").ConfigureAwait(false) ?? [];

    public Task<EpisodeImages?> GetImagesForTmdbMovie(string movieId, CancellationToken cancellationToken = default)
        => GetOrNull<EpisodeImages>($"/api/v3/TMDB/Movie/{movieId}/Images", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<File>> GetFilesForTmdbMovie(string movieId)
        => HasPluginsExposed
            ? (await GetOrNull<ListResult<File>>($"/api/v3/TMDB/Movie/{movieId}/File?pageSize=0&include=XRefs,ReleaseInfo", skipCache: true).ConfigureAwait(false))?.List ?? []
            : (await GetOrNull<ListResult<File>>($"/api/v3/TMDB/Movie/{movieId}/File?pageSize=0&include=XRefs&includeDataFrom=AniDB", skipCache: true).ConfigureAwait(false))?.List ?? [];

    public async Task<IReadOnlyList<TmdbMovieCrossReference>> GetTmdbCrossReferencesForTmdbMovie(string showId)
        => await GetOrNull<IReadOnlyList<TmdbMovieCrossReference>>($"/api/v3/TMDB/Movie/{showId}/CrossReferences").ConfigureAwait(false) ?? [];

    #endregion

    #region TMDB Movie Collection

    public Task<TmdbMovieCollection?> GetTmdbMovieCollection(string collectionId)
        => GetOrNull<TmdbMovieCollection>($"/api/v3/TMDB/Movie/Collection/{collectionId}");

    public Task<Images?> GetImagesForTmdbMovieCollection(string collectionId, CancellationToken cancellationToken = default)
        => GetOrNull<Images>($"/api/v3/TMDB/Movie/Collection/{collectionId}/Images", cancellationToken: cancellationToken);

    #endregion

    #region Custom Tags

    /// <summary>
    /// Gets a list of all custom tags in Shoko.
    /// </summary>
    /// <returns>A list of custom tags.</returns>
    public async Task<IReadOnlyList<Tag>> GetCustomTags()
        => (await Get<ListResult<Tag>>($"/api/v3/Tag/User?pageSize=0").ConfigureAwait(false))?.List ?? [];

    private const string CustomTagByIdFilter = """
        {
            "ApplyAtSeriesLevel": true,
            "Expression": {
                "Type": "SetOverlaps",
                "Left": "CustomTagIDsSelector",
                "Parameter": [%tagIds%],
            }
        }
    """;

    public async Task<IReadOnlyList<int>> GetSeriesIdsWithCustomTag(IEnumerable<int> tagIds)
        => tagIds.Select(x => x.ToString()).ToList() is { Count: > 0 } tagIdList ? await GetShokoSeriesIdsForFilter(CustomTagByIdFilter.Replace("%tagIds%", $"\"{tagIdList.Join("\", \"")}\"")).ConfigureAwait(false) : [];

    /// <summary>
    /// Creates a custom tag in Shoko.
    /// </summary>
    /// <param name="name">The name of the tag.</param>
    /// <param name="description">The description of the tag.</param>
    /// <returns>The custom tag that was created.</returns>
    public Task<Tag> CreateCustomTag(string name, string? description = null)
        => Post<Dictionary<string, string?>, Tag>($"/api/v3/Tag/User", HttpMethod.Post, new() { { "name", name }, { "description", description } });

    /// <summary>
    /// Updates an existing custom tag in Shoko.
    /// </summary>
    /// <param name="tagId">The ID of the tag to update.</param>
    /// <param name="name">The new name of the tag.</param>
    /// <param name="description">The new description of the tag.</param>
    /// <returns>The custom tag that was updated.</returns>
    public Task<Tag> UpdateCustomTag(int tagId, string? name = null, string? description = null)
        => Post<Dictionary<string, string?>, Tag>($"/api/v3/Tag/User/{tagId}", HttpMethod.Put, new() { { "name", name }, { "description", description } });

    /// <summary>
    /// Removes a custom tag from Shoko.
    /// </summary>
    /// <param name="tagId">The ID of the tag to remove.</param>
    /// <returns><c>true</c> if the tag was removed; <c>false</c> otherwise.</returns>
    public async Task<bool> RemoveCustomTag(int tagId)
        => (await Get($"/api/v3/Tag/User/{tagId}", HttpMethod.Delete).ConfigureAwait(false)).StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;

    #region Custom Tags on Series

    /// <summary>
    /// Gets a list of custom tags for a Shoko Series.
    /// </summary>
    /// <param name="seriesId">The ID of the Shoko Series.</param>
    /// <returns>A list of custom tags.</returns>
    public async Task<IReadOnlyList<Tag>> GetCustomTagsForShokoSeries(int seriesId)
        => await GetOrNull<IReadOnlyList<Tag>>($"/api/v3/Series/{seriesId}/Tags/User?excludeDescriptions=true", skipCache: true).ConfigureAwait(false) ?? [];

    /// <summary>
    /// Adds a custom tag to a Shoko Series.
    /// </summary>
    /// <param name="seriesId">The ID of the Shoko Series.</param>
    /// <param name="tagId">The ID of the custom tag to add.</param>
    /// <returns><c>true</c> if the tag was added; <c>false</c> otherwise.</returns>
    public async Task<bool> AddCustomTagToShokoSeries(int seriesId, int tagId)
        => (await Post($"/api/v3/Series/{seriesId}/Tags/User", HttpMethod.Post, new Dictionary<string, int[]> { { "IDs", [tagId] } }).ConfigureAwait(false)).StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;

    /// <summary>
    /// Removes a custom tag from a Shoko Series.
    /// </summary>
    /// <param name="seriesId">The ID of the Shoko Series.</param>
    /// <param name="tagId">The ID of the custom tag to remove.</param>
    /// <returns><c>true</c> if the tag was removed; <c>false</c> otherwise.</returns>
    public async Task<bool> RemoveCustomTagFromShokoSeries(int seriesId, int tagId)
        => (await Post($"/api/v3/Series/{seriesId}/Tags/User", HttpMethod.Delete, new Dictionary<string, int[]> { { "IDs", [tagId] } }).ConfigureAwait(false)).StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;

    #endregion

    #endregion
}
