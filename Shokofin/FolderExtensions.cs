using System.IO;
using MediaBrowser.Controller.Entities;

namespace Shokofin;

public static class FolderExtensions
{
    public static string GetVirtualRoot(this Folder mediaFolder)
        => Path.Join(Plugin.Instance.VirtualRoot, mediaFolder.Id.ToString());
}