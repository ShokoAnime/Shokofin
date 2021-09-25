using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Common.Json;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        private readonly ShokoAPIClient APIClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebController"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        public WebController(IHttpClientFactory httpClientFactory, ShokoAPIClient apiClient)
        {
            APIClient = apiClient;
        }

        [HttpPost("GetApiKey")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult<ApiKey>> PostAsync([FromBody] ApiLoginRequest body)
        {
            try {
                var apiKey = await APIClient.GetApiKey(body.username, body.password).ConfigureAwait(false);
                if (apiKey == null)
                    return new StatusCodeResult(StatusCodes.Status401Unauthorized);
                return apiKey;
            }
            catch {
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }

    public class ApiLoginRequest {
        public string username { get; set; }
        public string password { get; set; }
    }
}