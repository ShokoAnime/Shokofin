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

        private Task<ReturnType> GetAsync<ReturnType>(string url, string apiKey = null)
            => GetAsync<ReturnType>(url, HttpMethod.Get, apiKey);

        private async Task<ReturnType> GetAsync<ReturnType>(string url, HttpMethod method, string apiKey = null)
        {
            var response = await GetAsync(url, method, apiKey);
            var responseStream = response.StatusCode == HttpStatusCode.OK ? response.Content.ReadAsStreamAsync().Result : null;
            return responseStream != null ? await JsonSerializer.DeserializeAsync<ReturnType>(responseStream) : default(ReturnType);
        }

        private async Task<HttpResponseMessage> GetAsync(string url, HttpMethod method, string apiKey = null)
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
                    return await _httpClient.SendAsync(requestMessage);
                }
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        private Task<ReturnType> PostAsync<Type, ReturnType>(string url, Type body, string apiKey = null)
            => PostAsync<Type, ReturnType>(url, HttpMethod.Post, body, apiKey);

        private async Task<ReturnType> PostAsync<Type, ReturnType>(string url, HttpMethod method, Type body, string apiKey = null)
        {
            var response = await PostAsync<Type>(url, method, body, apiKey);
            var responseStream = response.StatusCode == HttpStatusCode.OK ? response.Content.ReadAsStreamAsync().Result : null;
            return responseStream != null ? await JsonSerializer.DeserializeAsync<ReturnType>(responseStream) : default(ReturnType);
        }

        private async Task<HttpResponseMessage> PostAsync<Type>(string url, HttpMethod method, Type body, string apiKey = null)
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
                    return await _httpClient.SendAsync(requestMessage);
                }
            }
            catch (HttpRequestException)
            {
                return null;
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

        public bool CheckImage(string imagePath)
        {
            var response = GetAsync(imagePath, HttpMethod.Head).ConfigureAwait(false).GetAwaiter().GetResult();
            return response != null && response.StatusCode == HttpStatusCode.OK;
        }

        public Task<Episode> GetEpisode(string id)
        {
            return GetAsync<Episode>($"/api/v3/Episode/{id}");
        }

        public Task<List<Episode>> GetEpisodesFromSeries(string seriesId)
        {
            return GetAsync<List<Episode>>($"/api/v3/Series/{seriesId}/Episode?includeMissing=true");
        }

        public Task<List<Episode>> GetEpisodeFromFile(string id)
        {
            return GetAsync<List<Episode>>($"/api/v3/File/{id}/Episode");
        }

        public Task<Episode.AniDB> GetEpisodeAniDb(string id)
        {
            return GetAsync<Episode.AniDB>($"/api/v3/Episode/{id}/AniDB");
        }

        public Task<List<Episode.TvDB>> GetEpisodeTvDb(string id)
        {
            return GetAsync<List<Episode.TvDB>>($"/api/v3/Episode/{id}/TvDB");
        }

        public Task<File> GetFile(string id)
        {
            return GetAsync<File>($"/api/v3/File/{id}");
        }

        public Task<List<File.FileDetailed>> GetFileByPath(string filename)
        {
            return GetAsync<List<File.FileDetailed>>($"/api/v3/File/PathEndsWith/{Uri.EscapeDataString(filename)}");
        }

        public Task<File.UserDataSummary> GetFileUserData(string fileId, string apiKey)
        {
            return GetAsync<File.UserDataSummary>($"/api/v3/File/UserData");
        }

        public Task<bool> ScrobbleFile(string id, bool watched, string apiKey)
        {
            return GetAsync<bool>($"/api/v3/File/{id}/Scrobble?watched={watched}", HttpMethod.Patch, apiKey);
        }

        public Task<bool> ScrobbleFile(string id, long progress, string apiKey)
        {
            return GetAsync<bool>($"/api/v3/File/{id}/Scrobble?resumePosition={progress}", HttpMethod.Patch, apiKey);
        }

        public Task<bool> ScrobbleFile(string id, bool watched, long? progress, string apiKey)
        {
            return GetAsync<bool>($"/api/v3/File/{id}/Scrobble?watched={watched}&resumePosition={progress ?? 0}", HttpMethod.Patch, apiKey);
        }

        public Task<Series> GetSeries(string id)
        {
            return GetAsync<Series>($"/api/v3/Series/{id}");
        }

        public Task<Series> GetSeriesFromEpisode(string id)
        {
            return GetAsync<Series>($"/api/v3/Episode/{id}/Series");
        }

        public Task<List<Series>> GetSeriesInGroup(string id)
        {
            return GetAsync<List<Series>>($"/api/v3/Filter/0/Group/{id}/Series");
        }

        public Task<Series.AniDB> GetSeriesAniDB(string id)
        {
            return GetAsync<Series.AniDB>($"/api/v3/Series/{id}/AniDB");
        }

        public Task<List<Series.TvDB>> GetSeriesTvDB(string id)
        {
            return GetAsync<List<Series.TvDB>>($"/api/v3/Series/{id}/TvDB");
        }

        public Task<List<Role>> GetSeriesCast(string id)
        {
            return GetAsync<List<Role>>($"/api/v3/Series/{id}/Cast");
        }

        public Task<List<Role>> GetSeriesCast(string id, Role.CreatorRoleType role)
        {
            return GetAsync<List<Role>>($"/api/v3/Series/{id}/Cast?roleType={role.ToString()}");
        }

        public Task<Images> GetSeriesImages(string id)
        {
            return GetAsync<Images>($"/api/v3/Series/{id}/Images");
        }

        public Task<List<Series>> GetSeriesPathEndsWith(string dirname)
        {
            return GetAsync<List<Series>>($"/api/v3/Series/PathEndsWith/{Uri.EscapeDataString(dirname)}");
        }

        public Task<List<Tag>> GetSeriesTags(string id, int filter = 0)
        {
            return GetAsync<List<Tag>>($"/api/v3/Series/{id}/Tags/{filter}?excludeDescriptions=true");
        }

        public Task<Group> GetGroup(string id)
        {
            return GetAsync<Group>($"/api/v3/Group/{id}");
        }

        public Task<Group> GetGroupFromSeries(string id)
        {
            return GetAsync<Group>($"/api/v3/Series/{id}/Group");
        }

        public Task<List<SeriesSearchResult>> SeriesSearch(string query)
        {
            return GetAsync<List<SeriesSearchResult>>($"/api/v3/Series/Search/{Uri.EscapeDataString(query)}");
        }
    }
}
