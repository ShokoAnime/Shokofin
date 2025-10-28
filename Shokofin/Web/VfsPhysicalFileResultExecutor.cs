using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Shokofin.Web;

public class VfsPhysicalFileResultExecutor(ILoggerFactory loggerFactory) : PhysicalFileResultExecutor(loggerFactory) {
    public override Task ExecuteAsync(ActionContext context, PhysicalFileResult result) {
        if (
            result.FileName.StartsWith(Plugin.Instance.VirtualRoot) &&
            File.ResolveLinkTarget(result.FileName, returnFinalTarget: false) is { } linkTargetInfo
        )
            result = new(linkTargetInfo.FullName, result.ContentType) {
                EnableRangeProcessing = result.EnableRangeProcessing,
                EntityTag = result.EntityTag,
                FileDownloadName = result.FileDownloadName,
                FileName = linkTargetInfo.FullName,
                LastModified = result.LastModified,
            };
        return base.ExecuteAsync(context, result);
    }
}
