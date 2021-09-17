using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Shokofin.API.Models;
using File = Shokofin.API.Models.File;

namespace Shokofin.API
{
    /// <summary>
    /// All API calls to Shoko needs to go through this gateway.
    /// </summary>
    internal class ShokoAPI
    {
        private static readonly HttpClient _httpClient;

        static ShokoAPI()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("apikey", Plugin.Instance.Configuration.ApiKey);
        }

        private static async Task<Stream> CallApi(string url, string requestType = "GET")
        {
            if (!(await CheckApiKey())) return null;

            try
            {
                var apiBaseUrl = Plugin.Instance.Configuration.Host;
                switch (requestType)
                {
                    case "PATCH":
                    case "POST":
                        var response = await _httpClient.PostAsync($"{apiBaseUrl}{url}", new StringContent(""));
                        return response.StatusCode == HttpStatusCode.OK ? response.Content.ReadAsStreamAsync().Result : null;
                    default:
                        return await _httpClient.GetStreamAsync($"{apiBaseUrl}{url}");
                }
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        private static async Task<bool> CheckApiKey()
        {
            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.ApiKey)) return true;

            var apikey = (await GetApiKey())?.apikey;
            if (string.IsNullOrEmpty(apikey)) return false;
            Plugin.Instance.Configuration.ApiKey = apikey;
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("apikey", apikey);
            return true;
        }

        private static async Task<ApiKey> GetApiKey()
        {
            var postData = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                {"user", Plugin.Instance.Configuration.Username},
                {"pass", Plugin.Instance.Configuration.Password},
                {"device", "Shoko Jellyfin Plugin"}
            });
            var apiBaseUrl = Plugin.Instance.Configuration.Host;
            var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/auth", new StringContent(postData, Encoding.UTF8, "application/json"));
            if (response.StatusCode == HttpStatusCode.OK)
                return await JsonSerializer.DeserializeAsync<ApiKey>(response.Content.ReadAsStreamAsync().Result);

            return null;
        }

        public static async Task<Episode> GetEpisode(string id)
        {
            var responseStream = await CallApi($"/api/v3/Episode/{id}");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<Episode>(responseStream) : null;
        }

        public static async Task<List<Episode>> GetEpisodesFromSeries(string seriesId)
        {
            var responseStream = await CallApi($"/api/v3/Series/{seriesId}/Episode?includeMissing=true");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<List<Episode>>(responseStream) : null;
        }

        public static async Task<List<Episode>> GetEpisodeFromFile(string id)
        {
            var responseStream = await CallApi($"/api/v3/File/{id}/Episode");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<List<Episode>>(responseStream) : null;
        }

        public static async Task<Episode.AniDB> GetEpisodeAniDb(string id)
        {
            var responseStream = await CallApi($"/api/v3/Episode/{id}/AniDB");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<Episode.AniDB>(responseStream) : null;
        }

        public static async Task<IEnumerable<Episode.TvDB>> GetEpisodeTvDb(string id)
        {
            var responseStream = await CallApi($"/api/v3/Episode/{id}/TvDB");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<IEnumerable<Episode.TvDB>>(responseStream) : null;
        }

        public static async Task<File> GetFile(string id)
        {
            var responseStream = await CallApi($"/api/v3/File/{id}");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<File>(responseStream) : null;
        }

        public static async Task<bool> ScrobbleFile(string id, bool watched, long? progress)
        {
            var responseStream = await CallApi($"/api/v3/File/{id}/Scrobble?watched={watched}&resumePosition={progress ?? 0}", "PATCH");
            return responseStream != null;
        }

        public static async Task<IEnumerable<File.FileDetailed>> GetFileByPath(string filename)
        {
            var responseStream = await CallApi($"/api/v3/File/PathEndsWith/{Uri.EscapeDataString(filename)}");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<IEnumerable<File.FileDetailed>>(responseStream) : null;
        }

        public static async Task<Series> GetSeries(string id)
        {
            var responseStream = await CallApi($"/api/v3/Series/{id}");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<Series>(responseStream) : null;
        }

        public static async Task<Series> GetSeriesFromEpisode(string id)
        {
            var responseStream = await CallApi($"/api/v3/Episode/{id}/Series");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<Series>(responseStream) : null;
        }

        public static async Task<List<Series>> GetSeriesInGroup(string id)
        {
            var responseStream = await CallApi($"/api/v3/Filter/0/Group/{id}/Series");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<List<Series>>(responseStream) : null;
        }

        public static async Task<Series.AniDB> GetSeriesAniDB(string id)
        {
            var responseStream = await CallApi($"/api/v3/Series/{id}/AniDB");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<Series.AniDB>(responseStream) : null;
        }

        public static async Task<IEnumerable<Series.TvDB>> GetSeriesTvDB(string id)
        {
            var responseStream = await CallApi($"/api/v3/Series/{id}/TvDB");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<IEnumerable<Series.TvDB>>(responseStream) : null;
        }

        public static async Task<IEnumerable<Role>> GetSeriesCast(string id)
        {
            var responseStream = await CallApi($"/api/v3/Series/{id}/Cast");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<IEnumerable<Role>>(responseStream) : null;
        }

        public static async Task<Images> GetSeriesImages(string id)
        {
            var responseStream = await CallApi($"/api/v3/Series/{id}/Images");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<Images>(responseStream) : null;
        }

        public static async Task<IEnumerable<Series>> GetSeriesPathEndsWith(string dirname)
        {
            var responseStream = await CallApi($"/api/v3/Series/PathEndsWith/{Uri.EscapeDataString(dirname)}");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<IEnumerable<Series>>(responseStream) : null;
        }

        public static async Task<IEnumerable<Tag>> GetSeriesTags(string id, int filter = 0)
        {
            var responseStream = await CallApi($"/api/v3/Series/{id}/Tags/{filter}?excludeDescriptions=true");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<IEnumerable<Tag>>(responseStream) : null;
        }

        public static async Task<Group> GetGroup(string id)
        {
            var responseStream = await CallApi($"/api/v3/Group/{id}");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<Group>(responseStream) : null;
        }

        public static async Task<Group> GetGroupFromSeries(string id)
        {
            var responseStream = await CallApi($"/api/v3/Series/{id}/Group");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<Group>(responseStream) : null;
        }

        public static async Task<IEnumerable<SeriesSearchResult>> SeriesSearch(string query)
        {
            var responseStream = await CallApi($"/api/v3/Series/Search/{Uri.EscapeDataString(query)}");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<IEnumerable<SeriesSearchResult>>(responseStream) : null;
        }

        public static async Task<IEnumerable<SeriesSearchResult>> SeriesStartsWith(string query)
        {
            var responseStream = await CallApi($"/api/v3/Series/StartsWith/{Uri.EscapeDataString(query)}");
            return responseStream != null ? await JsonSerializer.DeserializeAsync<IEnumerable<SeriesSearchResult>>(responseStream) : null;
        }
    }
}
