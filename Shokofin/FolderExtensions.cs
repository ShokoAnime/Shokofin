using System.IO;
using MediaBrowser.Controller.Entities;

namespace Shokofin;

public static class FolderExtensions
{
    public static string GetVirtualRoot(this Folder libraryFolder)
        => Path.Join(Plugin.Instance.VirtualRoot, libraryFolder.Id.ToString());
}