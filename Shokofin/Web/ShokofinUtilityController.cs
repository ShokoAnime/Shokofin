using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration;
using Shokofin.Resolvers;
using Shokofin.Utils;
using Shokofin.Web.Models;

namespace Shokofin.Web;

/// <summary>
/// Shoko Utility Web Controller.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ShokofinUtilityController"/> class.
/// </remarks>
[Authorize]
[ApiController]
[Route("Shokofin/Utility")]
[Produces(MediaTypeNames.Application.Json)]
public partial class ShokofinUtilityController(
    ILogger<ShokofinUtilityController> logger,
    ShokoApiClient apiClient,
    SeriesConfigurationService seriesConfigurationService,
    VirtualFileSystemService virtualFileSystemService
) : ControllerBase {
    private readonly ILogger<ShokofinUtilityController> Logger = logger;

    private readonly SeriesConfigurationService SeriesConfigurationService = seriesConfigurationService;

    private readonly VirtualFileSystemService VirtualFileSystemService = virtualFileSystemService;

    private readonly GuardedMemoryCache Cache = new(logger, new() { ExpirationScanFrequency = TimeSpan.FromMinutes(25) }, new() { SlidingExpiration = new(0, 30, 0) });

    /// <summary>
    /// Previews the VFS structure for the given library.
    /// </summary>
    /// <param name="libraryId">The id of the library to preview.</param>
    /// <returns>A <see cref="VfsLibraryPreview"/> or <see cref="ValidationProblemDetails"/> if the library is not found.</returns>
    [HttpPost("VFS/Library/{libraryId}/Preview")]
    public async Task<ActionResult<VfsLibraryPreview>> PreviewVFS(Guid libraryId) {
        var trackerId = Plugin.Instance.Tracker.Add("Preview VFS");
        try {
            var (filesBefore, filesAfter, virtualFolder, result, vfsPath) = await VirtualFileSystemService.PreviewChangesForLibrary(libraryId, HttpContext.RequestAborted).ConfigureAwait(false);
            if (virtualFolder is null)
                return NotFound("Unable to find library with the given id.");

            return new VfsLibraryPreview(filesBefore, filesAfter, virtualFolder, result, vfsPath);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    /// <summary>
    /// Retrieves a simple series list.
    /// </summary>
    /// <param name="query">Query to filter the list.</param>
    /// <returns>The series list.</returns>
    [HttpGet("Series")]
    public async Task<ActionResult<IReadOnlyList<SimpleSeries>>> GetSeriesList(
        [FromQuery] string? query = null
    ) {
        var list = await GetSeriesListInternal().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query))
        {
            if (AnidbIdRegex().Match(query) is { Success: true })
            {
                var anidbId = int.Parse(AnidbIdRegex().Match(query).Groups["animeId"].Value);
                return list
                    .Where(s => s.AnidbId == anidbId)
                    .ToList();
            }

            return list
                .Where(s =>
                    s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.DefaultTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                )
                .OrderByDescending(s => string.Equals(s.Title, query, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(s => string.Equals(s.DefaultTitle, query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(s => s.Title)
                .ThenBy(s => s.DefaultTitle)
                .ToList();
        }

        return new(list);
    }

    private Task<IReadOnlyList<SimpleSeries>> GetSeriesListInternal()
        => Cache.GetOrCreateAsync<IReadOnlyList<SimpleSeries>>("SeriesList", async () => {
            var simpleList = new List<SimpleSeries>();
            var trackerId = Plugin.Instance.Tracker.Add($"Get Simple Series List");
            try {
                const int PageSize = 100;
                var firstPage = await apiClient.GetAllAnidbAnime(pageSize: PageSize);
                foreach (var anime in firstPage.List) {
                    if (anime.ShokoId.HasValue)
                        simpleList.Add(new SimpleSeries() {
                            Id = anime.ShokoId.Value,
                            AnidbId = anime.Id,
                            Title = anime.Title,
                            DefaultTitle = anime.Titles?.FirstOrDefault(title => title.Type is API.Models.TitleType.Main)?.Value ?? anime.Title,
                        });
                }
                if (firstPage.Total > PageSize) {
                    var total = firstPage.Total;
                    var page = 2;
                    while (total > 0) {
                        var nextPage = await apiClient.GetAllAnidbAnime(page, PageSize);
                        foreach (var anime in nextPage.List) {
                            if (anime.ShokoId.HasValue)
                                simpleList.Add(new SimpleSeries() {
                                    Id = anime.ShokoId.Value,
                                    AnidbId = anime.Id,
                                    Title = anime.Title,
                                    DefaultTitle = anime.Titles?.FirstOrDefault(title => title.Type is API.Models.TitleType.Main)?.Value ?? anime.Title,
                                });
                        }
                        total -= PageSize;
                        page++;
                    }
                }
            }
            finally {
                Plugin.Instance.Tracker.Remove(trackerId);
            }

            return simpleList
                .OrderBy(s => s.AnidbId)
                .ToList();
        });

    [GeneratedRegex(@"^\s*a(?<animeId>\d+)\s*$")]
    private static partial Regex AnidbIdRegex();

    /// <summary>
    /// Retrieves the series configuration for the given series id.
    /// </summary>
    /// <param name="seriesId">Shoko series ID.</param>
    /// <returns>The series configuration, if found.</returns>
    [HttpGet("Series/{seriesId}/Configuration")]
    public async Task<ActionResult<SeriesConfiguration>> GetSeriesConfigurationForId(
        [FromRoute, Range(1, int.MaxValue)] int seriesId
    ) {
        var trackerId = Plugin.Instance.Tracker.Add($"Get Series Configuration for {seriesId}");
        try {
            var config = await SeriesConfigurationService.GetSeriesConfigurationForId(seriesId).ConfigureAwait(false);
            if (config is null)
                return NotFound("Unable to find series with the given id.");

            return config;
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    /// <summary>
    /// Updates the series configuration for the given series id.
    /// </summary>
    /// <param name="seriesId">Shoko series ID.</param>
    /// <param name="seriesConfiguration">The series configuration.</param>
    /// <returns>The updated series configuration.</returns>
    [HttpPost("Series/{seriesId}/Configuration")]
    public async Task<ActionResult<SeriesConfiguration>> UpdateSeriesConfigurationForId(
        [FromRoute, Range(1, int.MaxValue)] int seriesId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] NullableSeriesConfiguration seriesConfiguration
    ) {
        var trackerId = Plugin.Instance.Tracker.Add($"Update Series Configuration for {seriesId} (Add)");
        try {
            return await SeriesConfigurationService.UpdateSeriesConfigurationForId(seriesId, seriesConfiguration).ConfigureAwait(false);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    /// <summary>
    /// Updates the series configuration for the given series id.
    /// </summary>
    /// <param name="seriesId">Shoko series ID.</param>
    /// <param name="seriesConfiguration">The series configuration.</param>
    /// <returns>The updated series configuration.</returns>
    [HttpPut("Series/{seriesId}/Configuration")]
    public async Task<ActionResult<SeriesConfiguration>> UpdateSeriesConfigurationForId(
        [FromRoute, Range(1, int.MaxValue)] int seriesId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SeriesConfiguration seriesConfiguration
    ) {
        var trackerId = Plugin.Instance.Tracker.Add($"Update Series Configuration for {seriesId} (Replace)");
        try {
            return await SeriesConfigurationService.UpdateSeriesConfigurationForId(seriesId, seriesConfiguration).ConfigureAwait(false);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }
}
