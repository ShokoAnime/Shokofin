using System;
using System.Net.Http;
using System.Net.Mime;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;

#nullable enable
namespace Shokofin.Web;

/// <summary>
/// Pushbullet notifications controller.
/// </summary>
[ApiController]
[Route("Plugin/Shokofin")]
[Produces(MediaTypeNames.Application.Json)]
public class ShokoApiController : ControllerBase
{
    private readonly ILogger<ShokoApiController> Logger;

    private readonly ShokoAPIClient APIClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShokoApiController"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    public ShokoApiController(ILogger<ShokoApiController> logger, ShokoAPIClient apiClient)
    {
        Logger = logger;
        APIClient = apiClient;
    }

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
    public async Task<ActionResult<ApiKey>> PostAsync([FromBody] ApiLoginRequest body)
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