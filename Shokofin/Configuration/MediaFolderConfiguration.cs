using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using MediaBrowser.Controller.Entities;

using LibraryOperationMode = Shokofin.Utils.Ordering.LibraryOperationMode;

namespace Shokofin.Configuration;

/// <summary>
/// Media folder configuration.
/// </summary>
public class MediaFolderConfiguration {
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
    public string? LibraryName => Guid.Empty == LibraryId  ? null : BaseItem.LibraryManager.GetItemById(LibraryId)?.Name;

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
    /// The shoko managed folder id the jellyfin media folder is linked to.
    /// </summary>
    [XmlElement("ImportFolderId")]
    public int ManagedFolderId { get; set; }

    /// <summary>
    /// The friendly name of the managed folder, if any. Stored only for showing
    /// in the settings page of the plugin… since it's very hard to get in
    /// there otherwise.
    /// </summary>
    [XmlElement("ImportFolderName")]
    public string? ManagedFolderName { get; set; }

    /// <summary>
    /// The relative path from the root of the managed folder the media folder is located at.
    /// </summary>
    [XmlElement("ImportFolderRelativePath")]
    public string ManagedFolderRelativePath  { get; set; } = string.Empty;

    /// <summary>
    /// Indicates the Jellyfin Media Folder is a virtual file system folder.
    /// </summary>
    [XmlIgnore]
    [JsonInclude]
    public bool IsVirtualRoot => ManagedFolderId < 0;

    /// <summary>
    /// Indicates the Jellyfin Media Folder is mapped to a Shoko Managed Folder.
    /// </summary>
    [XmlIgnore]
    [JsonInclude]
    public bool IsMapped => ManagedFolderId != 0;

    /// <summary>
    /// Indicates that SignalR file events is enabled for the folder.
    /// </summary>
    public bool IsFileEventsEnabled { get; set; } = true;

    /// <summary>
    /// Indicates that SignalR refresh events is enabled for the folder.
    /// </summary>
    public bool IsRefreshEventsEnabled { get; set; } = true;

    /// <summary>
    /// Shortcut to check if the virtual file system is enabled.
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public bool IsVirtualFileSystemEnabled => LibraryOperationMode is LibraryOperationMode.VFS;

    /// <summary>
    /// Legacy property used to upgrade to the new library operation mode if necessary.
    /// </summary>
    /// TODO: Break this during the next major version of the plugin.
    [XmlElement("IsVirtualFileSystemEnabled")]
    [JsonIgnore]
    public bool? LegacyVirtualFileSystemEnabled { get; set; }

    /// <summary>
    /// Determines how the plugin should operate on the selected library.
    /// </summary>
    [XmlElement("LibraryFilteringMode")]
    public LibraryOperationMode LibraryOperationMode { get; set; } = LibraryOperationMode.VFS;

    /// <summary>
    /// Check if a relative path within the managed folder is potentially available in this media folder.
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public bool IsEnabledForPath(string relativePath)
        => string.IsNullOrEmpty(ManagedFolderRelativePath) || relativePath.StartsWith(ManagedFolderRelativePath + Path.DirectorySeparatorChar);
}
