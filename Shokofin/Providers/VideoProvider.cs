using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Providers;

public class VideoProvider(IHttpClientFactory _httpClientFactory, ILogger<VideoProvider> _logger, ShokoApiManager _apiManager)
    : IRemoteMetadataProvider<Video, ItemLookupInfo>, IHasOrder {
    public string Name => Plugin.MetadataProviderName;

    // Always run first, so we can react to the VFS entries.
    public int Order => -1;

    public async Task<MetadataResult<Video>> GetMetadata(ItemLookupInfo info, CancellationToken cancellationToken) {
        var result = new MetadataResult<Video>();
        if (string.IsNullOrEmpty(info.Path) || !info.Path.StartsWith(Plugin.Instance.VirtualRoot + Path.DirectorySeparatorChar)) {
            return result;
        }

        var trackerId = Plugin.Instance.Tracker.Add($"Providing info for Video \"{info.Name}\". (Path=\"{info.Path}\")");
        try {
            var (fileInfo, seasonInfo, showInfo) = await _apiManager.GetFileInfoByPath(info.Path).ConfigureAwait(false);
            var episodeInfo = fileInfo is { EpisodeList.Count: > 0 } ? fileInfo.EpisodeList[0].Episode : null;
            if (fileInfo == null || episodeInfo == null || seasonInfo == null || showInfo == null) {
                _logger.LogWarning("Unable to find episode info for path {Path}", info.Path);
                return result;
            }

            var (displayTitle, alternateTitle) = TextUtility.GetEpisodeTitles(episodeInfo, seasonInfo, info.MetadataLanguage);
            if (string.IsNullOrEmpty(displayTitle))
                displayTitle = episodeInfo.Title;

            var description = TextUtility.GetEpisodeDescription(episodeInfo, seasonInfo, info.MetadataLanguage);
            result.Item = new() {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                PremiereDate = episodeInfo.AiredAt,
                ProductionYear = episodeInfo.AiredAt?.Year ?? seasonInfo.PremiereDate?.Year,
                Overview = description,
                CommunityRating = episodeInfo.CommunityRating.Value > 0 ? episodeInfo.CommunityRating.ToFloat(10) : 0,
            };
            _logger.LogInformation("Found video {EpisodeName} (File={FileId},Episode={EpisodeId},Season={SeasonId},ExtraSeasons={ExtraIds})", result.Item.Name, fileInfo.Id, episodeInfo.Id, seasonInfo.Id, seasonInfo.ExtraIds);

            result.Item.SetProviderId(ShokoInternalId.Name, fileInfo.InternalId);
            result.Item.SetProviderId(ProviderNames.Shoko, ShokoExternalUrlHandler.GetFileInfoUrls(fileInfo));
            result.Item.SetProviderId(ProviderNames.ShokoFile, fileInfo.Id);
            result.Item.SetProviderId(ProviderNames.ShokoEpisode, episodeInfo.Id);
            result.Item.SetProviderId(ProviderNames.ShokoSeries, fileInfo.SeriesId);

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in episodeInfo.Staff)
                result.AddPerson(person);

            return result;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Threw unexpectedly while refreshing {Path}; {Message}", info.Path, ex.Message);
            return new MetadataResult<Video>();
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(ItemLookupInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>([]);

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
