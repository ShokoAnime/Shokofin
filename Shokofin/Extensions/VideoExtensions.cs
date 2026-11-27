#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace Shokofin.Extensions;

public static class VideoExtensions {
    public static IEnumerable<Video> GetLinkedAlternateVersions(this Video video)
        => BaseItem.LibraryManager.GetLinkedAlternateVersions(video);

    public static IEnumerable<Guid> GetLocalAlternateVersionIds(this Video video)
        => BaseItem.LibraryManager.GetLocalAlternateVersionIds(video);
}
#endif
