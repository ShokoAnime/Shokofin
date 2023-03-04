using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API.Models;

#nullable enable
namespace Shokofin.API;

/// <summary>
/// All API calls to Shoko needs to go through this gateway.
/// </summary>
public class ShokoAPIClient : IDisposable
{
    private readonly HttpClient _httpClient;

    private readonly ILogger<ShokoAPIClient> Logger;

    public ShokoAPIClient(ILogger<ShokoAPIClient> logger)
    {
        _httpClient = (new HttpClient());
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        Logger = logger;
    }

    #region Base Implementation

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private Task<ReturnType> Get<ReturnType>(string url, string? apiKey = null)
        => Get<ReturnType>(url, HttpMethod.Get, apiKey);

    private async Task<ReturnType> Get<ReturnType>(string url, HttpMethod method, string? apiKey = null)
    {
        var response = await Get(url, method, apiKey);
        if (response.StatusCode != HttpStatusCode.OK)
            throw ApiException.FromResponse(response);
        var responseStream = await response.Content.ReadAsStreamAsync();
        var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream);
        if (value == null)
            throw new ApiException(response.StatusCode, nameof(ShokoAPIClient), "Unexpected null return value.");
        return value;
    }

    private async Task<HttpResponseMessage> Get(string url, HttpMethod method, string? apiKey = null)
    {
        // Use the default key if no key was provided.
        if (apiKey == null)
            apiKey = Plugin.Instance.Configuration.ApiKey;

        // Check if we have a key to use.
        if (string.IsNullOrEmpty(apiKey)) {
            _httpClient.DefaultRequestHeaders.Clear();
            throw new Exception("Unable to call the API before an connection is established to Shoko Server!");
        }

        try {
            Logger.LogTrace("Trying to get {URL}", url);
            var remoteUrl = string.Concat(Plugin.Instance.Configuration.Host, url);

            // Because Shoko Server don't support HEAD requests, we spoof it instead.
            if (method == HttpMethod.Head) {
                var real = await _httpClient.GetAsync(remoteUrl, HttpCompletionOption.ResponseHeadersRead);
                var fake = new HttpResponseMessage(real.StatusCode);
                fake.ReasonPhrase = real.ReasonPhrase;
                fake.RequestMessage = real.RequestMessage;
                if (fake.RequestMessage != null)
                    fake.RequestMessage.Method = HttpMethod.Head;
                fake.Version = real.Version;
                fake.Content = (new StringContent(String.Empty));
                fake.Content.Headers.Clear();
                foreach (var pair in real.Content.Headers) {
                    fake.Content.Headers.Add(pair.Key, pair.Value);
                }
                fake.Headers.Clear();
                foreach (var pair in real.Headers) {
                    fake.Headers.Add(pair.Key, pair.Value);
                }
                real.Dispose();
                return fake;
            }

            using (var requestMessage = new HttpRequestMessage(method, remoteUrl)) {
                requestMessage.Content = (new StringContent(""));
                requestMessage.Headers.Add("apikey", apiKey);
                var response = await _httpClient.SendAsync(requestMessage);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new HttpRequestException("Invalid or expired API Token. Please reconnect the plugin to Shoko Server by resetting the connection or deleting and re-adding the user in the plugin settings.", null, HttpStatusCode.Unauthorized);
                Logger.LogTrace("API returned response with status code {StatusCode}", response.StatusCode);
                return response;
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Unable to connect to complete the request to Shoko.");
            throw;
        }
    }

    private Task<ReturnType> Post<Type, ReturnType>(string url, Type body, string? apiKey = null)
        => Post<Type, ReturnType>(url, HttpMethod.Post, body, apiKey);

    private async Task<ReturnType> Post<Type, ReturnType>(string url, HttpMethod method, Type body, string? apiKey = null)
    {
        var response = await Post<Type>(url, method, body, apiKey);
        if (response.StatusCode != HttpStatusCode.OK)
            throw ApiException.FromResponse(response);
        var responseStream = await response.Content.ReadAsStreamAsync();
        var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream);
        if (value == null)
            throw new ApiException(response.StatusCode, nameof(ShokoAPIClient), "Unexpected null return value.");
        return value;
    }

    private async Task<HttpResponseMessage> Post<Type>(string url, HttpMethod method, Type body, string? apiKey = null)
    {
        // Use the default key if no key was provided.
        if (apiKey == null)
            apiKey = Plugin.Instance.Configuration.ApiKey;

        // Check if we have a key to use.
        if (string.IsNullOrEmpty(apiKey)) {
            _httpClient.DefaultRequestHeaders.Clear();
            throw new Exception("Unable to call the API before an connection is established to Shoko Server!");
        }

        try {
            Logger.LogTrace("Trying to get {URL}", url);
            var remoteUrl = string.Concat(Plugin.Instance.Configuration.Host, url);

            if (method == HttpMethod.Get)
                throw new HttpRequestException("Get requests cannot contain a body.");

            if (method == HttpMethod.Head)
                throw new HttpRequestException("Head requests cannot contain a body.");

            using (var requestMessage = new HttpRequestMessage(method, remoteUrl)) {
                requestMessage.Content = (new StringContent(JsonSerializer.Serialize<Type>(body), Encoding.UTF8, "application/json"));
                requestMessage.Headers.Add("apikey", apiKey);
                var response = await _httpClient.SendAsync(requestMessage);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new HttpRequestException("Invalid or expired API Token. Please reconnect the plugin to Shoko Server by resetting the connection or deleting and re-adding the user in the plugin settings.", null, HttpStatusCode.Unauthorized);
                Logger.LogTrace("API returned response with status code {StatusCode}", response.StatusCode);
                return response;
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Unable to connect to complete the request to Shoko.");
            throw;
        }
    }

    #endregion Base Implementation

    public async Task<ApiKey?> GetApiKey(string username, string password, bool forUser = false)
    {
        var postData = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            {"user", username},
            {"pass", password},
            {"device", forUser ? "Shoko Jellyfin Plugin (Shokofin) - User Key" : "Shoko Jellyfin Plugin (Shokofin)"},
        });
        var apiBaseUrl = Plugin.Instance.Configuration.Host;
        var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/auth", new StringContent(postData, Encoding.UTF8, "application/json"));
        if (response.StatusCode == HttpStatusCode.OK)
            return (await JsonSerializer.DeserializeAsync<ApiKey>(response.Content.ReadAsStreamAsync().Result));

        return null;
    }

    public Task<File> GetFile(string id)
    {
        return Get<File>($"/api/v3/File/{id}?includeXRefs=true&includeDataFrom=AniDB");
    }

    public Task<List<File>> GetFileByPath(string path)
    {
        return Get<List<File>>($"/api/v3/File/PathEndsWith?path={Uri.EscapeDataString(path)}&includeDataFrom=AniDB&limit=1");
    }

    public Task<List<File>> GetFilesForSeries(string seriesId)
    {
        return Get<List<File>>($"/api/v3/Series/{seriesId}/File?includeXRefs=true&includeDataFrom=AniDB");
    }

    public async Task<File.UserStats?> GetFileUserStats(string fileId, string? apiKey = null)
    {
        try
        {
            return await Get<File.UserStats>($"/api/v3/File/{fileId}/UserStats", apiKey);
        }
        catch (ApiException e)
        {
            // File user stats were not found.
            if (e.StatusCode == HttpStatusCode.NotFound && e.Message.Contains("FileUserStats"))
                return null;
            throw;
        }
    }

    public Task<File.UserStats> PutFileUserStats(string fileId, File.UserStats userStats, string? apiKey = null)
    {
        return Post<File.UserStats, File.UserStats>($"/api/v3/File/{fileId}/UserStats", HttpMethod.Put, userStats, apiKey);
    }

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, bool watched, string apiKey)
    {
        var response = await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&watched={watched}", HttpMethod.Patch, apiKey);
        return response != null && (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent);
    }

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, long progress, string apiKey)
    {
        var response = await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&resumePosition={Math.Round(new TimeSpan(progress).TotalMilliseconds)}", HttpMethod.Patch, apiKey);
        return response != null && (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent);
    }

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, long? progress, bool watched, string apiKey)
    {
        if (!progress.HasValue)
            return await ScrobbleFile(fileId, episodeId, eventName, watched, apiKey);
        var response = await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&resumePosition={Math.Round(new TimeSpan(progress.Value).TotalMilliseconds)}&watched={watched}", HttpMethod.Patch, apiKey);
        return response != null && (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent);
    }

    public Task<Episode> GetEpisode(string id)
    {
        return Get<Episode>($"/api/v3/Episode/{id}?includeDataFrom=AniDB,TvDB");
    }

    public Task<ListResult<Episode>> GetEpisodesFromSeries(string seriesId)
    {
        return Get<ListResult<Episode>>($"/api/v3/Series/{seriesId}/Episode?pageSize=0&includeMissing=true&includeDataFrom=AniDB,TvDB");
    }

    public Task<Series> GetSeries(string id)
    {
        return Get<Series>($"/api/v3/Series/{id}?includeDataFrom=AniDB,TvDB");
    }

    public Task<Series> GetSeriesFromEpisode(string id)
    {
        return Get<Series>($"/api/v3/Episode/{id}/Series?includeDataFrom=AniDB,TvDB");
    }

    public Task<List<Series>> GetSeriesInGroup(string groupID, int filterID = 0)
    {
        return Get<List<Series>>($"/api/v3/Filter/{filterID}/Group/{groupID}/Series?includeMissing=true&includeIgnored=false&includeDataFrom=AniDB,TvDB");
    }

    public Task<List<Role>> GetSeriesCast(string id)
    {
        return Get<List<Role>>($"/api/v3/Series/{id}/Cast");
    }

    public Task<Images> GetSeriesImages(string id)
    {
        return Get<Images>($"/api/v3/Series/{id}/Images");
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

    public Task<Group> GetGroupFromSeries(string id)
    {
        return Get<Group>($"/api/v3/Series/{id}/Group");
    }

    public Task<ListResult<Series.AniDB>> SeriesSearch(string query)
    {
        return Get<ListResult<Series.AniDB>>($"/api/v3/Series/AniDB/Search?query={Uri.EscapeDataString(query ?? "")}&local=true&includeTitles=true&pageSize=0");
    }
}
