using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;

namespace Shokofin.Web
{

    /// <summary>
    /// Pushbullet notifications controller.
    /// </summary>
    [ApiController]
    [Route("Plugin/Shokofin")]
    [Produces(MediaTypeNames.Application.Json)]
    public class WebController : ControllerBase
    {
        private readonly ILogger<WebController> Logger;

        private readonly ShokoAPIClient APIClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebController"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        public WebController(ILogger<WebController> logger, ShokoAPIClient apiClient)
        {
            Logger = logger;
            APIClient = apiClient;
        }

        [HttpPost("GetApiKey")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult<ApiKey>> PostAsync([FromBody] ApiLoginRequest body)
        {
            try {
                Logger.LogDebug("Trying to create an API-key for user {Username}.", body.username);
                var apiKey = await APIClient.GetApiKey(body.username, body.password, body.userKey).ConfigureAwait(false);
                if (apiKey == null) {
                    Logger.LogDebug("Failed to create an API-key for user {Username} — invalid credentials received.", body.username);
                    return new StatusCodeResult(StatusCodes.Status401Unauthorized);
                }

                Logger.LogDebug("Successfully created an API-key for user {Username}.", body.username);
                return apiKey;
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Failed to create an API-key for user {Username} — unable to complete the request.", body.username);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }

    public class ApiLoginRequest {
        public string username { get; set; }
        public string password { get; set; }
        public bool userKey { get; set; }
    }
}