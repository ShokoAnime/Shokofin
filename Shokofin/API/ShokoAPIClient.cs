using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API.Models;
using File = Shokofin.API.Models.File;

namespace Shokofin.API
{
    /// <summary>
    /// All API calls to Shoko needs to go through this gateway.
    /// </summary>
    public class ShokoAPIClient
    {
        private readonly HttpClient _httpClient;

        private readonly ILogger<ShokoAPIClient> Logger;

        public ShokoAPIClient(ILogger<ShokoAPIClient> logger)
        {
            _httpClient = (new HttpClient());
            Logger = logger;
        }

        private Task<ReturnType> CallApi<ReturnType>(string url, string apiKey = null)
            => CallApi<ReturnType>(url, HttpMethod.Get, apiKey);

        private async Task<ReturnType> CallApi<ReturnType>(string url, HttpMethod method, string apiKey = null)
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
                var remoteUrl = string.Concat(Plugin.Instance.Configuration.Host, url);
                using (var requestMessage = new HttpRequestMessage(method, remoteUrl)) {
                    requestMessage.Content = (new StringContent(""));
                    requestMessage.Headers.Add("apikey", apiKey);
                    var response = await _httpClient.SendAsync(requestMessage);
                    var responseStream = response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStreamAsync() : null;
                    return responseStream != null ? await JsonSerializer.DeserializeAsync<ReturnType>(responseStream) : default(ReturnType);
                }
            }
            catch (HttpRequestException)
            {
                return default(ReturnType);
            }
        }

        private Task<ReturnType> CallApi<Type, ReturnType>(string url, Type body, string apiKey = null)
            => CallApi<Type, ReturnType>(url, HttpMethod.Post, body, apiKey);

        private async Task<ReturnType> CallApi<Type, ReturnType>(string url, HttpMethod method, Type body, string apiKey = null)
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
                var remoteUrl = string.Concat(Plugin.Instance.Configuration.Host, url);
                using (var requestMessage = new HttpRequestMessage(method, remoteUrl)) {
                    requestMessage.Content = (new StringContent(JsonSerializer.Serialize<Type>(body), Encoding.UTF8, "application/json"));
                    requestMessage.Headers.Add("apikey", apiKey);
                    var response = await _httpClient.SendAsync(requestMessage);
                    var responseStream = response.StatusCode == HttpStatusCode.OK ? response.Content.ReadAsStreamAsync().Result : null;
                    return responseStream != null ? await JsonSerializer.DeserializeAsync<ReturnType>(responseStream) : default(ReturnType);
                }
            }
            catch (HttpRequestException)
            {
                return default(ReturnType);
            }
        }

        public async Task<ApiKey> GetApiKey(string username, string password)
        {
            var postData = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                {"user", username},
                {"pass", password},
                {"device", "Shoko Jellyfin Plugin (Shokofin)"},
            });
            var apiBaseUrl = Plugin.Instance.Configuration.Host;
            var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/auth", new StringContent(postData, Encoding.UTF8, "application/json"));
            if (response.StatusCode == HttpStatusCode.OK)
                return (await JsonSerializer.DeserializeAsync<ApiKey>(response.Content.ReadAsStreamAsync().Result));

            return null;
        }

        public Task<Episode> GetEpisode(string id)
        {
            return CallApi<Episode>($"/api/v3/Episode/{id}");
        }

        public Task<List<Episode>> GetEpisodesFromSeries(string seriesId)
        {
            return CallApi<List<Episode>>($"/api/v3/Series/{seriesId}/Episode?includeMissing=true");
        }

        public Task<List<Episode>> GetEpisodeFromFile(string id)
        {
            return CallApi<List<Episode>>($"/api/v3/File/{id}/Episode");
        }

        public Task<Episode.AniDB> GetEpisodeAniDb(string id)
        {
            return CallApi<Episode.AniDB>($"/api/v3/Episode/{id}/AniDB");
        }

        public Task<List<Episode.TvDB>> GetEpisodeTvDb(string id)
        {
            return CallApi<List<Episode.TvDB>>($"/api/v3/Episode/{id}/TvDB");
        }

        public Task<File> GetFile(string id)
        {
            return CallApi<File>($"/api/v3/File/{id}");
        }

        public Task<List<File.FileDetailed>> GetFileByPath(string filename)
        {
            return CallApi<List<File.FileDetailed>>($"/api/v3/File/PathEndsWith/{Uri.EscapeDataString(filename)}");
        }

        public Task<File.UserDataSummary> GetFileUserData(string fileId, string apiKey)
        {
            return CallApi<File.UserDataSummary>($"/api/v3/File/UserData");
        }

        public Task<bool> ScrobbleFile(string id, bool watched, string apiKey)
        {
            return CallApi<bool>($"/api/v3/File/{id}/Scrobble?watched={watched}", HttpMethod.Patch, apiKey);
        }

        public Task<bool> ScrobbleFile(string id, long progress, string apiKey)
        {
            return CallApi<bool>($"/api/v3/File/{id}/Scrobble?resumePosition={progress}", HttpMethod.Patch, apiKey);
        }

        public Task<bool> ScrobbleFile(string id, bool watched, long? progress, string apiKey)
        {
            return CallApi<bool>($"/api/v3/File/{id}/Scrobble?watched={watched}&resumePosition={progress ?? 0}", HttpMethod.Patch, apiKey);
        }

        public Task<Series> GetSeries(string id)
        {
            return CallApi<Series>($"/api/v3/Series/{id}");
        }

        public Task<Series> GetSeriesFromEpisode(string id)
        {
            return CallApi<Series>($"/api/v3/Episode/{id}/Series");
        }

        public Task<List<Series>> GetSeriesInGroup(string id)
        {
            return CallApi<List<Series>>($"/api/v3/Filter/0/Group/{id}/Series");
        }

        public Task<Series.AniDB> GetSeriesAniDB(string id)
        {
            return CallApi<Series.AniDB>($"/api/v3/Series/{id}/AniDB");
        }

        public Task<List<Series.TvDB>> GetSeriesTvDB(string id)
        {
            return CallApi<List<Series.TvDB>>($"/api/v3/Series/{id}/TvDB");
        }

        public Task<List<Role>> GetSeriesCast(string id)
        {
            return CallApi<List<Role>>($"/api/v3/Series/{id}/Cast");
        }

        public Task<List<Role>> GetSeriesCast(string id, Role.CreatorRoleType role)
        {
            return CallApi<List<Role>>($"/api/v3/Series/{id}/Cast?roleType={role.ToString()}");
        }

        public Task<Images> GetSeriesImages(string id)
        {
            return CallApi<Images>($"/api/v3/Series/{id}/Images");
        }

        public Task<List<Series>> GetSeriesPathEndsWith(string dirname)
        {
            return CallApi<List<Series>>($"/api/v3/Series/PathEndsWith/{Uri.EscapeDataString(dirname)}");
        }

        public Task<List<Tag>> GetSeriesTags(string id, int filter = 0)
        {
            return CallApi<List<Tag>>($"/api/v3/Series/{id}/Tags/{filter}?excludeDescriptions=true");
        }

        public Task<Group> GetGroup(string id)
        {
            return CallApi<Group>($"/api/v3/Group/{id}");
        }

        public Task<Group> GetGroupFromSeries(string id)
        {
            return CallApi<Group>($"/api/v3/Series/{id}/Group");
        }

        public Task<List<SeriesSearchResult>> SeriesSearch(string query)
        {
            return CallApi<List<SeriesSearchResult>>($"/api/v3/Series/Search/{Uri.EscapeDataString(query)}");
        }
    }
}
