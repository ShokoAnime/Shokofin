using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using MediaBrowser.Controller.Entities;

using LibraryFilteringMode = Shokofin.Utils.Ordering.LibraryFilteringMode;

namespace Shokofin.Configuration;

public class MediaFolderConfiguration
{
    /// <summary>
    /// The jellyfin library id.
    /// </summary>
    public Guid LibraryId { get; set; }

    /// <summary>
    /// The Jellyfin library's name. Only for displaying on the plugin
    /// configuration page.
    /// </summary>
    [XmlIgnore]
    [JsonInclude]
    public string? LibraryName => LibraryId == Guid.Empty ? null : BaseItem.LibraryManager.GetItemById(LibraryId)?.Name;

    /// <summary>
    /// The jellyfin media folder id.
    /// </summary>
    public Guid MediaFolderId { get; set; }

    /// <summary>
    /// The jellyfin media folder path. Stored only for showing in the settings
    /// page of the plugin… since it's very hard to get in there otherwise.
    /// </summary>
    public string MediaFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// The shoko import folder id the jellyfin media folder is linked to.
    /// </summary>
    public int ImportFolderId { get; set; }

    /// <summary>
    /// The friendly name of the import folder, if any. Stored only for showing
    /// in the settings page of the plugin… since it's very hard to get in
    /// there otherwise.
    /// </summary>
    public string? ImportFolderName { get; set; }

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
    /// Indicates that SignalR refresh events is enabled for the folder.
    /// </summary>
    public bool IsRefreshEventsEnabled { get; set; } = true;

    /// <summary>
    /// Enable or disable the virtual file system on a per-media-folder basis.
    /// </summary>
    public bool IsVirtualFileSystemEnabled { get; set; } = true;

    /// <summary>
    /// Enable or disable the library filtering on a per-media-folder basis. Do
    /// note that this will only take effect if the VFS is not used.
    /// </summary>
    public LibraryFilteringMode LibraryFilteringMode { get; set; } = LibraryFilteringMode.Auto;

    /// <summary>
    /// Check if a relative path within the import folder is potentially available in this media folder.
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public bool IsEnabledForPath(string relativePath)
        => string.IsNullOrEmpty(ImportFolderRelativePath) || relativePath.StartsWith(ImportFolderRelativePath + Path.DirectorySeparatorChar);
}