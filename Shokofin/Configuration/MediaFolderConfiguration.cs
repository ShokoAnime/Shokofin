using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Shokofin.Configuration;

/// <summary>
/// Media folder configuration.
/// </summary>
[XmlType("MediaFolderConfiguration_V2")]
public class MediaFolderConfiguration {
    /// <summary>
    /// The jellyfin library id.
    /// </summary>
    public Guid LibraryId { get; set; }

    /// <summary>
    /// The library configuration.
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public LibraryConfiguration Library => Plugin.Instance.Configuration.Libraries.First(config => config.Id == LibraryId);

    /// <summary>
    /// The jellyfin media folder path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The shoko managed folder id the jellyfin media folder is linked to.
    /// </summary>
    public int ManagedFolderId { get; set; } = 0;

    /// <summary>
    /// The friendly name of the managed folder, if any. Stored only for showing
    /// in the settings page of the plugin… since it's very hard to get in
    /// there otherwise.
    /// </summary>
    public string? ManagedFolderName { get; set; } = null;

    /// <summary>
    /// The relative path from the root of the managed folder the media folder is located at.
    /// </summary>
    public string ManagedFolderRelativePath  { get; set; } = string.Empty;

    /// <summary>
    /// Indicates that the media folder mapping should be refreshed upon the next refresh.
    /// </summary>
    public bool NeedsRefresh { get; set; } = false;

    /// <summary>
    /// Indicates that the media folder should be ignored by the plugin, and be available as a media
    /// folder of the library if the VFS is enabled.
    /// </summary>
    public bool IsIgnored { get; set; } = false;

    /// <summary>
    /// Indicates the Jellyfin Media Folder is mapped to a Shoko Managed Folder.
    /// </summary>
    [XmlIgnore]
    [JsonInclude]
    public bool IsMapped => ManagedFolderId != 0;

    /// <summary>
    /// Check if a relative path within the managed folder is potentially available in this media folder.
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public bool IsEnabledForPath(string relativePath)
        => string.IsNullOrEmpty(ManagedFolderRelativePath) || relativePath.StartsWith(ManagedFolderRelativePath + System.IO.Path.DirectorySeparatorChar);

    /// <summary>
    /// Merge another media folder configuration into this one.
    /// </summary>
    /// <param name="other">The other media folder configuration to merge into this one.</param>
    public void MergeWith(MediaFolderConfiguration other) {
        ManagedFolderId = other.ManagedFolderId;
        ManagedFolderName = other.ManagedFolderName;
        ManagedFolderRelativePath = other.ManagedFolderRelativePath;
    }
}
