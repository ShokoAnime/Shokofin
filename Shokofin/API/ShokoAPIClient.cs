using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API.Models;
using Shokofin.Utils;

namespace Shokofin.API;

/// <summary>
/// All API calls to Shoko needs to go through this gateway.
/// </summary>
public class ShokoAPIClient : IDisposable
{
    private readonly HttpClient _httpClient;

    private readonly ILogger<ShokoAPIClient> Logger;

    private static ComponentVersion? ServerVersion =>
        Plugin.Instance.Configuration.ServerVersion;

    private static readonly DateTime StableCutOffDate = DateTime.Parse("2023-12-16T00:00:00.000Z");

    private static bool UseOlderSeriesAndFileEndpoints =>
        ServerVersion != null && ((ServerVersion.ReleaseChannel == ReleaseChannel.Stable && ServerVersion.Version == new Version("4.2.2.0")) || (ServerVersion.ReleaseDate.HasValue && ServerVersion.ReleaseDate.Value < StableCutOffDate));

    private static readonly DateTime ImportFolderCutOffDate = DateTime.Parse("2024-03-28T00:00:00.000Z");

    private static bool UseOlderImportFolderFileEndpoints =>
        ServerVersion != null && ((ServerVersion.ReleaseChannel == ReleaseChannel.Stable && ServerVersion.Version == new Version("4.2.2.0")) || (ServerVersion.ReleaseDate.HasValue && ServerVersion.ReleaseDate.Value < ImportFolderCutOffDate));

    public static bool AllowEpisodeImages =>
        ServerVersion is { } serverVersion && serverVersion.Version > new Version("4.2.2.0");

    private readonly GuardedMemoryCache _cache;

    public ShokoAPIClient(ILogger<ShokoAPIClient> logger)
    {
        _httpClient = new HttpClient {
            Timeout = TimeSpan.FromMinutes(10),
        };
        Logger = logger;
        _cache = new(logger, new() { ExpirationScanFrequency = TimeSpan.FromMinutes(25) }, new() { SlidingExpiration = new(2, 30, 0) });
        Plugin.Instance.Tracker.Stalled += OnTrackerStalled;
    }

    ~ShokoAPIClient()
    {
        Plugin.Instance.Tracker.Stalled -= OnTrackerStalled;
    }

    private void OnTrackerStalled(object? sender, EventArgs eventArgs)
        => Clear();

    #region Base Implementation

    public void Clear()
    {
        Logger.LogDebug("Clearing data…");
        _cache.Clear();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _cache.Dispose();
    }

    private Task<ReturnType> Get<ReturnType>(string url, string? apiKey = null, bool skipCache = false)
        => Get<ReturnType>(url, HttpMethod.Get, apiKey, skipCache);

    private async Task<ReturnType> Get<ReturnType>(string url, HttpMethod method, string? apiKey = null, bool skipCache = false)
    {
        if (skipCache) {
            Logger.LogTrace("Creating object for {Method} {URL}", method, url);
            var response = await Get(url, method, apiKey).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                throw ApiException.FromResponse(response);
            var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            responseStream.Seek(0, System.IO.SeekOrigin.Begin);
            var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream).ConfigureAwait(false) ??
                throw new ApiException(response.StatusCode, nameof(ShokoAPIClient), "Unexpected null return value.");
            return value;
        }

        return await _cache.GetOrCreateAsync(
            $"apiKey={apiKey ?? "default"},method={method},url={url},object",
            (_) => Logger.LogTrace("Reusing object for {Method} {URL}", method, url),
            async () => {
                Logger.LogTrace("Creating object for {Method} {URL}", method, url);
                var response = await Get(url, method, apiKey).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw ApiException.FromResponse(response);
                var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                responseStream.Seek(0, System.IO.SeekOrigin.Begin);
                var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream).ConfigureAwait(false) ??
                    throw new ApiException(response.StatusCode, nameof(ShokoAPIClient), "Unexpected null return value.");
                return value;
            }
        );
    }

    private async Task<HttpResponseMessage> Get(string url, HttpMethod method, string? apiKey = null, bool skipApiKey = false)
    {
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

        try {
            Logger.LogTrace("Trying to {Method} {URL}", method, url);
            var remoteUrl = string.Concat(Plugin.Instance.Configuration.Url, url);

            using var requestMessage = new HttpRequestMessage(method, remoteUrl);
            requestMessage.Content = new StringContent(string.Empty);
            if (!string.IsNullOrEmpty(apiKey))
                requestMessage.Headers.Add("apikey", apiKey);
            var response = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new HttpRequestException("Invalid or expired API Token. Please reconnect the plugin to Shoko Server by resetting the connection or deleting and re-adding the user in the plugin settings.", null, HttpStatusCode.Unauthorized);
            Logger.LogTrace("API returned response with status code {StatusCode}", response.StatusCode);
            return response;
        }
        catch (HttpRequestException ex) {
            Logger.LogWarning(ex, "Unable to connect to complete the request to Shoko.");
            throw;
        }
    }

    private Task<ReturnType> Post<Type, ReturnType>(string url, Type body, string? apiKey = null)
        => Post<Type, ReturnType>(url, HttpMethod.Post, body, apiKey);

    private async Task<ReturnType> Post<Type, ReturnType>(string url, HttpMethod method, Type body, string? apiKey = null)
    {
        var response = await Post(url, method, body, apiKey).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            throw ApiException.FromResponse(response);
        var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream).ConfigureAwait(false) ??
            throw new ApiException(response.StatusCode, nameof(ShokoAPIClient), "Unexpected null return value.");
        return value;
    }

    private async Task<HttpResponseMessage> Post<Type>(string url, HttpMethod method, Type body, string? apiKey = null)
    {
        // Use the default key if no key was provided.
        apiKey ??= Plugin.Instance.Configuration.ApiKey;

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

        try {
            Logger.LogTrace("Trying to get {URL}", url);
            var remoteUrl = string.Concat(Plugin.Instance.Configuration.Url, url);

            if (method == HttpMethod.Get)
                throw new HttpRequestException("Get requests cannot contain a body.");

            if (method == HttpMethod.Head)
                throw new HttpRequestException("Head requests cannot contain a body.");

            using var requestMessage = new HttpRequestMessage(method, remoteUrl);
            requestMessage.Content = new StringContent(JsonSerializer.Serialize<Type>(body), Encoding.UTF8, "application/json");
            requestMessage.Headers.Add("apikey", apiKey);
            var response = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new HttpRequestException("Invalid or expired API Token. Please reconnect the plugin to Shoko Server by resetting the connection or deleting and re-adding the user in the plugin settings.", null, HttpStatusCode.Unauthorized);
            Logger.LogTrace("API returned response with status code {StatusCode}", response.StatusCode);
            return response;
        }
        catch (HttpRequestException ex) {
            Logger.LogWarning(ex, "Unable to connect to complete the request to Shoko.");
            throw;
        }
    }

    #endregion Base Implementation

    public async Task<ApiKey?> GetApiKey(string username, string password, bool forUser = false)
    {
        var version = Plugin.Instance.Configuration.ServerVersion;
        if (version == null) {
            version = await GetVersion().ConfigureAwait(false)
                ?? throw new HttpRequestException("Unable to connect to Shoko Server to read the version.", null, HttpStatusCode.BadGateway);

            Plugin.Instance.Configuration.ServerVersion = version;
            Plugin.Instance.UpdateConfiguration();
        }

        var postData = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            {"user", username},
            {"pass", password},
            {"device", forUser ? "Shoko Jellyfin Plugin (Shokofin) - User Key" : "Shoko Jellyfin Plugin (Shokofin)"},
        });
        var apiBaseUrl = Plugin.Instance.Configuration.Url;
        var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/auth", new StringContent(postData, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            return null;

        return await JsonSerializer.DeserializeAsync<ApiKey>(response.Content.ReadAsStreamAsync().Result).ConfigureAwait(false);
    }

    public async Task<ComponentVersion?> GetVersion()
    {
        try {
            var apiBaseUrl = Plugin.Instance.Configuration.Url;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/v3/Init/Version", source.Token);
            if (response.StatusCode == HttpStatusCode.OK) {
                var componentVersionSet = await JsonSerializer.DeserializeAsync<ComponentVersionSet>(response.Content.ReadAsStreamAsync().Result);
                return componentVersionSet?.Server;
            }
        }
        catch (Exception e) {
            Logger.LogTrace("Unable to connect to Shoko Server to read the version. Exception; {e}", e.Message);
            return null;
        }

        return null;
    }

    public Task<HttpResponseMessage> GetImageAsync(ImageSource imageSource, ImageType imageType, int imageId)
        => Get($"/api/v3/Image/{imageSource}/{imageType}/{imageId}", HttpMethod.Get, null, true);

    public async Task<ImportFolder?> GetImportFolder(int id)
    {
        try {
            return await Get<ImportFolder>($"/api/v3/ImportFolder/{id}");
        }
        catch (ApiException e) {
            if (e.StatusCode == HttpStatusCode.NotFound)
                return null;
            throw;
        }
    }

    public Task<File> GetFile(string id)
    {
        if (UseOlderSeriesAndFileEndpoints)
            return Get<File>($"/api/v3/File/{id}?includeXRefs=true&includeDataFrom=AniDB");

        return Get<File>($"/api/v3/File/{id}?include=XRefs&includeDataFrom=AniDB");
    }

    public Task<List<File>> GetFileByPath(string path)
    {
        return Get<List<File>>($"/api/v3/File/PathEndsWith?path={Uri.EscapeDataString(path)}&includeDataFrom=AniDB&limit=1");
    }

    public async Task<IReadOnlyList<File>> GetFilesForSeries(string seriesId)
    {
        if (UseOlderSeriesAndFileEndpoints)
            return await Get<List<File>>($"/api/v3/Series/{seriesId}/File?&includeXRefs=true&includeDataFrom=AniDB").ConfigureAwait(false);

        var listResult = await Get<ListResult<File>>($"/api/v3/Series/{seriesId}/File?pageSize=0&include=XRefs&includeDataFrom=AniDB").ConfigureAwait(false);
        return listResult.List;
    }

    public async Task<ListResult<File>> GetFilesForImportFolder(int importFolderId, string subPath, int page = 1)
    {
        if (UseOlderImportFolderFileEndpoints) {
            return await Get<ListResult<File>>($"/api/v3/ImportFolder/{importFolderId}/File?page={page}&pageSize=100&includeXRefs=true", skipCache: true).ConfigureAwait(false);
        }

        return await Get<ListResult<File>>($"/api/v3/ImportFolder/{importFolderId}/File?page={page}&folderPath={Uri.EscapeDataString(subPath)}&pageSize=1000&include=XRefs", skipCache: true).ConfigureAwait(false);
    }

    public async Task<File.UserStats?> GetFileUserStats(string fileId, string? apiKey = null)
    {
        try {
            return await Get<File.UserStats>($"/api/v3/File/{fileId}/UserStats", apiKey, true).ConfigureAwait(false);
        }
        catch (ApiException e) {
            // File user stats were not found.
            if (e.StatusCode == HttpStatusCode.NotFound) {
                if (!e.Message.Contains("FileUserStats"))
                    Logger.LogWarning("Unable to find user stats for a file that doesn't exist. (File={FileID})", fileId);
                return null;
            }
            throw;
        }
    }

    public Task<File.UserStats> PutFileUserStats(string fileId, File.UserStats userStats, string? apiKey = null)
    {
        return Post<File.UserStats, File.UserStats>($"/api/v3/File/{fileId}/UserStats", HttpMethod.Put, userStats, apiKey);
    }

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, bool watched, string apiKey)
    {
        var response = await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&watched={watched}", HttpMethod.Patch, apiKey).ConfigureAwait(false);
        return response != null && (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent);
    }

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, long progress, string apiKey)
    {
        var response = await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&resumePosition={Math.Round(new TimeSpan(progress).TotalMilliseconds)}", HttpMethod.Patch, apiKey).ConfigureAwait(false);
        return response != null && (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent);
    }

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, long? progress, bool watched, string apiKey)
    {
        if (!progress.HasValue)
            return await ScrobbleFile(fileId, episodeId, eventName, watched, apiKey).ConfigureAwait(false);
        var response = await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&resumePosition={Math.Round(new TimeSpan(progress.Value).TotalMilliseconds)}&watched={watched}", HttpMethod.Patch, apiKey).ConfigureAwait(false);
        return response != null && (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent);
    }

    public Task<Episode> GetEpisode(string id)
    {
        return Get<Episode>($"/api/v3/Episode/{id}?includeDataFrom=AniDB,TvDB&includeXRefs=true");
    }

    public async Task<EpisodeImages?> GetEpisodeImages(string id)
    {
        try {
            if (AllowEpisodeImages)
                return await Get<EpisodeImages>($"/api/v3/Episode/{id}/Images");
            var episode = await GetEpisode(id);
            return new() {
                Thumbnails = episode.TvDBEntityList.FirstOrDefault()?.Thumbnail is { } thumbnail ? [thumbnail] : [],
            };
        }
        catch (ApiException e) when (e.StatusCode == HttpStatusCode.NotFound) {
            return null;
        }
    }

    public Task<ListResult<Episode>> GetEpisodesFromSeries(string seriesId)
    {
        return Get<ListResult<Episode>>($"/api/v3/Series/{seriesId}/Episode?pageSize=0&includeHidden=true&includeMissing=true&includeDataFrom=AniDB,TvDB&includeXRefs=true");
    }

    public Task<Series> GetSeries(string id)
    {
        return Get<Series>($"/api/v3/Series/{id}?includeDataFrom=AniDB,TvDB");
    }

    public Task<Series> GetSeriesFromEpisode(string id)
    {
        return Get<Series>($"/api/v3/Episode/{id}/Series?includeDataFrom=AniDB,TvDB");
    }

    public Task<List<Series>> GetSeriesInGroup(string groupID, int filterID = 0, bool recursive = false)
    {
        return Get<List<Series>>($"/api/v3/Filter/{filterID}/Group/{groupID}/Series?recursive={recursive}&includeMissing=true&includeIgnored=false&includeDataFrom=AniDB,TvDB");
    }

    public Task<List<Role>> GetSeriesCast(string id)
    {
        return Get<List<Role>>($"/api/v3/Series/{id}/Cast");
    }

    public Task<List<Relation>> GetSeriesRelations(string id)
    {
        return Get<List<Relation>>($"/api/v3/Series/{id}/Relations");
    }

    public async Task<Images?> GetSeriesImages(string id)
    {
        try {
            return await Get<Images>($"/api/v3/Series/{id}/Images");
        }
        catch (ApiException e) when (e.StatusCode == HttpStatusCode.NotFound) {
            return null;
        }
    }

    public Task<List<Series>> GetSeriesPathEndsWith(string dirname)
    {
        return Get<List<Series>>($"/api/v3/Series/PathEndsWith/{Uri.EscapeDataString(dirname)}");
    }

    public Task<List<Tag>> GetSeriesTags(string id, ulong filter = 0)
    {
        return Get<List<Tag>>($"/api/v3/Series/{id}/Tags?filter={filter}&excludeDescriptions=true");
    }

    public Task<Group> GetGroup(string id)
    {
        return Get<Group>($"/api/v3/Group/{id}");
    }

    public Task<List<Group>> GetGroupsInGroup(string id)
    {
        return Get<List<Group>>($"/api/v3/Group/{id}/Group?includeEmpty=true");
    }

    public Task<Group> GetGroupFromSeries(string id)
    {
        return Get<Group>($"/api/v3/Series/{id}/Group");
    }
}
