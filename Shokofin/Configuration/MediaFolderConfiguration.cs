using System;
using System.ComponentModel.DataAnnotations;
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
    /// Only generate links in the VFS for files changed since the last check occurred.
    /// </summary>
    public bool IterativeVfsGeneration_Enabled { get; set; } = false;

    /// <summary>
    /// The last time the VFS was iteratively generated.
    /// </summary>
    /// <remarks>
    /// This will be null if the VFS has never been iteratively generated, or if
    /// a generation with <seealso cref="IterativeVfsGeneration_Enabled"/>
    /// disabled has occurred.
    /// </remarks>
    public DateTime? IterativeVfsGeneration_LastGeneratedAt { get; set; } = null;

    /// <summary>
    /// The current number of times the VFS has been iteratively generated.
    /// </summary>
    /// <remarks>
    /// Will be incremented by the system, but only if
    /// <seealso cref="MaxIterativeGenerationCount"/> is set to a value above 0.
    /// Otherwise the VFS will always be iteratively generated if
    /// <seealso cref="IterativeVfsGenerationEnabled"/> is enabled.
    /// </remarks>
    [Range(0, 100)]
    public int IterativeVfsGeneration_CurrentCount { get; set; } = 0;

    /// <summary>
    /// The maximum number of times the VFS should be iteratively generated
    /// before a full generation is performed. Set to a value above 0 to enable.
    /// </summary>
    [Range(0, 100)]
    public int IterativeVfsGeneration_MaxCount { get; set; } = 0;

    /// <summary>
    /// Force a full generation of the VFS on the next library generation.
    /// </summary>
    /// <remarks>
    /// This will be turned off once the next generation has started.
    /// </remarks>
    public bool IterativeVfsGeneration_ForceFullGenerationOnNextRefresh { get; set; } = false;

    /// <summary>
    /// Check if a relative path within the managed folder is potentially available in this media folder.
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public bool IsEnabledForPath(string relativePath)
        => string.IsNullOrEmpty(ManagedFolderRelativePath) || relativePath.StartsWith(ManagedFolderRelativePath + Path.DirectorySeparatorChar);
}
