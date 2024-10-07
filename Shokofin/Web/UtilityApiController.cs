using System;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Shokofin.Configuration;
using Shokofin.Resolvers;
using Shokofin.Web.Models;

namespace Shokofin.Web;

/// <summary>
/// Shoko Utility Web Controller.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="UtilityApiController"/> class.
/// </remarks>
[ApiController]
[Route("Plugin/Shokofin/Utility")]
[Produces(MediaTypeNames.Application.Json)]
public class UtilityApiController(ILogger<UtilityApiController> logger, MediaFolderConfigurationService mediaFolderConfigurationService, VirtualFileSystemService virtualFileSystemService) : ControllerBase
{
    private readonly ILogger<UtilityApiController> Logger = logger;

    private readonly MediaFolderConfigurationService ConfigurationService = mediaFolderConfigurationService;

    private readonly VirtualFileSystemService VirtualFileSystemService = virtualFileSystemService;

    /// <summary>
    /// Previews the VFS structure for the given library.
    /// </summary>
    /// <param name="libraryId">The id of the library to preview.</param>
    /// <returns>A <see cref="VfsLibraryPreview"/> or <see cref="ValidationProblemDetails"/> if the library is not found.</returns>
    [HttpPost("VFS/Library/{libraryId}/Preview")]
    public async Task<ActionResult<VfsLibraryPreview>> PreviewVFS(Guid libraryId)
    {
        var trackerId = Plugin.Instance.Tracker.Add("Preview VFS");
        try {
            var (filesBefore, filesAfter, virtualFolder, result, vfsPath) = await VirtualFileSystemService.PreviewChangesForLibrary(libraryId);
            if (virtualFolder is null)
                return NotFound("Unable to find library with the given id.");

            return new VfsLibraryPreview(filesBefore, filesAfter, virtualFolder, result, vfsPath);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }
}