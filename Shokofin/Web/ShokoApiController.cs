using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Mime;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;

namespace Shokofin.Web;

/// <summary>
/// Shoko API Host Web Controller.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ShokoApiController"/> class.
/// </remarks>
/// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
[ApiController]
[Route("Plugin/Shokofin/Host")]
[Produces(MediaTypeNames.Application.Json)]
public class ShokoApiController(ILogger<ShokoApiController> logger, ShokoAPIClient apiClient) : ControllerBase
{
    private readonly ILogger<ShokoApiController> Logger = logger;

    private readonly ShokoAPIClient APIClient = apiClient;

    /// <summary>
    /// Try to get the version of the server.
    /// </summary>
    /// <returns></returns>
    [HttpGet("Version")]
    public async Task<ActionResult<ComponentVersion>> GetVersionAsync()
    {
        try {
            Logger.LogDebug("Trying to get version from the remote Shoko server.");
            var version = await APIClient.GetVersion().ConfigureAwait(false);
            if (version == null) {
                Logger.LogDebug("Failed to get version from the remote Shoko server.");
                return StatusCode(StatusCodes.Status502BadGateway);
            }

            Logger.LogDebug("Successfully got version {Version} from the remote Shoko server. (Channel={Channel},Commit={Commit})", version.Version, version.ReleaseChannel, version.Commit?[0..7]);
            return version;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Failed to get version from the remote Shoko server. Exception; {ex}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("GetApiKey")]
    public async Task<ActionResult<ApiKey>> GetApiKeyAsync([FromBody] ApiLoginRequest body)
    {
        try {
            Logger.LogDebug("Trying to create an API-key for user {Username}.", body.Username);
            var apiKey = await APIClient.GetApiKey(body.Username, body.Password, body.UserKey).ConfigureAwait(false);
            if (apiKey == null) {
                Logger.LogDebug("Failed to create an API-key for user {Username} — invalid credentials received.", body.Username);
                return StatusCode(StatusCodes.Status401Unauthorized);
            }

            Logger.LogDebug("Successfully created an API-key for user {Username}.", body.Username);
            return apiKey;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Failed to create an API-key for user {Username} — unable to complete the request.", body.Username);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Simple forward to grab the image from Shoko Server.
    /// </summary>
    [ResponseCache(Duration = 3600 /* 1 hour in seconds */)]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    [ProducesResponseType(404)]
    [HttpGet("Image/{ImageSource}/{ImageType}/{ImageId}")]
    [HttpHead("Image/{ImageSource}/{ImageType}/{ImageId}")]
    public async Task<ActionResult> GetImageAsync([FromRoute] ImageSource imageSource, [FromRoute] ImageType imageType, [FromRoute, Range(1, int.MaxValue)] int imageId
    )
    {
        var response = await APIClient.GetImageAsync(imageSource, imageType, imageId);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound)
            return NotFound();
        if (response.StatusCode is not System.Net.HttpStatusCode.OK)
            return StatusCode((int)response.StatusCode);
        var stream = await response.Content.ReadAsStreamAsync();
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/ocelot-stream";
        return File(stream, contentType);
    }
}

public class ApiLoginRequest
{
    /// <summary>
    /// The username to submit to shoko.
    /// </summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The password to submit to shoko.
    /// </summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// If this is a user key.
    /// </summary>
    [JsonPropertyName("userKey")]
    public bool UserKey { get; set; } = false;
}