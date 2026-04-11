using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Shokofin.Web;

public class VfsActionFilter : IAsyncActionFilter {
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) {
        var executedResult = await next().ConfigureAwait(false);
        if (
            executedResult.Result is ObjectResult result1 &&
            result1.Value is BaseItemDto { MediaSources.Length: > 0 } dto &&
            dto.Path.StartsWith(Plugin.Instance.VirtualRoot) &&
            Plugin.Instance.Configuration.VFS_UseSourceFileAsVersionIdentifier
        ) {
            foreach (var mediaSource in dto.MediaSources) {
                var path = File.ResolveLinkTarget(mediaSource.Path, returnFinalTarget: false)?.FullName ?? mediaSource.Path;
                var fileName = Path.GetFileNameWithoutExtension(path);
                mediaSource.Name = fileName;
            }
        }
        if (
            executedResult.Result is PhysicalFileResult result &&
            result.FileName.StartsWith(Plugin.Instance.VirtualRoot) &&
            File.Exists(result.FileName) &&
            File.ResolveLinkTarget(result.FileName, returnFinalTarget: true) is { } linkTargetInfo &&
            linkTargetInfo.Exists
        ) {
            context.Result = executedResult.Result = new PhysicalFileResult(linkTargetInfo.FullName, result.ContentType) {
                EnableRangeProcessing = result.EnableRangeProcessing,
                EntityTag = result.EntityTag,
                FileDownloadName = result.FileDownloadName,
                FileName = linkTargetInfo.FullName,
                LastModified = result.LastModified,
            };
        }
    }
}
