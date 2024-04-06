using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Shokofin.Configuration;

public class MediaFolderConfiguration
{
    /// <summary>
    /// The jellyfin media folder id.
    /// </summary>
    public Guid MediaFolderId { get; set; }

    /// <summary>
    /// The shoko import folder id the jellyfin media folder is linked to.
    /// </summary>
    public int ImportFolderId { get; set; }

    /// <summary>
    /// The relative path from the root of the import folder the media folder is located at.
    /// </summary>
    public string ImportFolderRelativePath  { get; set; } = string.Empty;

    /// <summary>
    /// Indicates the Jellyfin Media Folder is mapped to a Shoko Import Folder.
    /// </summary>
    [XmlIgnore]
    [JsonInclude]
    public bool IsMapped => ImportFolderId != 0;

    /// <summary>
    /// Indicates that SignalR file events is enabled for the folder.
    /// </summary>
    public bool IsFileEventsEnabled { get; set; } = true;

    /// <summary>
    /// Enable or disable the virtual file system on a per-media-folder basis.
    /// </summary>
    public bool IsVirtualFileSystemEnabled { get; set; } = true;

    /// <summary>
    /// Enable or disable the library filterin on a per-media-folder basis. Do
    /// note that this will only take effect if the VFS is not used.
    /// </summary>
    public bool? IsLibraryFilteringEnabled { get; set; } = null;

    /// <summary>
    /// Check if a relative path within the import folder is potentially available in this media folder.
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public bool IsEnabledForPath(string relativePath)
        => string.IsNullOrEmpty(ImportFolderRelativePath) || relativePath.StartsWith(ImportFolderRelativePath + Path.DirectorySeparatorChar);
}