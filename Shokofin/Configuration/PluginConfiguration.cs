using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;
using Shokofin.API.Models;

using CollectionCreationType = Shokofin.Utils.Ordering.CollectionCreationType;
using DescriptionProvider = Shokofin.Utils.Text.DescriptionProvider;
using LibraryFilteringMode = Shokofin.Utils.Ordering.LibraryFilteringMode;
using OrderType = Shokofin.Utils.Ordering.OrderType;
using SpecialOrderType = Shokofin.Utils.Ordering.SpecialOrderType;
using TitleProvider = Shokofin.Utils.Text.TitleProvider;

namespace Shokofin.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    #region Connection

#pragma warning disable CA1822
    /// <summary>
    /// Helper for the web ui to show the windows only warning, and to disable
    /// the VFS by default if we cannot create symbolic links.
    /// </summary>
    [XmlIgnore, JsonInclude]
    public bool CanCreateSymbolicLinks => Plugin.Instance.CanCreateSymbolicLinks;
#pragma warning restore CA1822

    /// <summary>
    /// The URL for where to connect to shoko internally.
    /// And externally if no <seealso cref="PublicUrl"/> is set.
    /// </summary>
    [XmlElement("Host")]
    public string Url { get; set; }

    /// <summary>
    /// The last known server version. This is used for keeping compatibility
    /// with multiple versions of the server.
    /// </summary>
    [XmlElement("HostVersion")]
    public ComponentVersion? ServerVersion { get; set; }

    [XmlElement("PublicHost")]
    public string PublicUrl { get; set; }

    [JsonIgnore]
    public virtual string PrettyUrl
        => string.IsNullOrEmpty(PublicUrl) ? Url : PublicUrl;

    /// <summary>
    /// The last known user name we used to try and connect to the server.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// The API key used to authenticate our requests to the server.
    /// This will be an empty string if we're not authenticated yet.
    /// </summary>
    public string ApiKey { get; set; }

    #endregion

    #region Plugin Interoperability

    /// <summary>
    /// Add AniDB ids to entries that support it. This is best to use when you
    /// don't use shoko groups.
    /// </summary>
    public bool AddAniDBId { get; set; }

    /// <summary>
    /// Add TMDb ids to entries that support it.
    /// </summary>
    public bool AddTMDBId { get; set; }

    #endregion

    #region Metadata

    /// <summary>
    /// Determines if we use the overridden settings for how the main title is fetched for entries.
    /// </summary>
    public bool TitleMainOverride { get; set; }

    /// <summary>
    /// Determines how we'll be selecting our main title for entries.
    /// </summary>
    public TitleProvider[] TitleMainList { get; set; }

    /// <summary>
    /// The order of which we will be selecting our main title for entries.
    /// </summary>
    public TitleProvider[] TitleMainOrder { get; set; }

    /// <summary>
    /// Determines if we use the overridden settings for how the alternate title is fetched for entries.
    /// </summary>
    public bool TitleAlternateOverride { get; set; }

    /// <summary>
    /// Determines how we'll be selecting our alternate title for entries.
    /// </summary>
    public TitleProvider[] TitleAlternateList { get; set; }

    /// <summary>
    /// The order of which we will be selecting our alternate title for entries.
    /// </summary>
    public TitleProvider[] TitleAlternateOrder { get; set; }

    /// <summary>
    /// Allow choosing any title in the selected language if no official
    /// title is available.
    /// </summary>
    public bool TitleAllowAny { get; set; }

    /// <summary>
    /// This will combine the titles for multi episodes entries into a single
    /// title, instead of just showing the title for the first episode.
    /// </summary>
    public bool TitleAddForMultipleEpisodes { get; set; }

    /// <summary>
    /// Mark any episode that is not considered a normal season episode with a
    /// prefix and number.
    /// </summary>
    public bool MarkSpecialsWhenGrouped { get; set; }

   /// <summary>
   /// Determines if we use the overridden settings for how descriptions are fetched for entries.
   /// </summary>
    public bool DescriptionSourceOverride { get; set; }

    /// <summary>
    /// The collection of providers for descriptions. Replaces the former `DescriptionSource`.
    /// </summary>
    public DescriptionProvider[] DescriptionSourceList { get; set; }

    /// <summary>
    /// The prioritization order of source providers for description sources.
    /// </summary>
    public DescriptionProvider[] DescriptionSourceOrder { get; set; }

    /// <summary>
    /// Clean up links within the AniDB description for entries.
    /// </summary>
    public bool SynopsisCleanLinks { get; set; }

    /// <summary>
    /// Clean up misc. lines within the AniDB description for entries.
    /// </summary>
    public bool SynopsisCleanMiscLines { get; set; }

    /// <summary>
    /// Remove the "summary" preface text in the AniDB description for entries.
    /// </summary>
    public bool SynopsisRemoveSummary { get; set; }

    /// <summary>
    /// Collapse up multiple empty lines into a single line in the AniDB
    /// description for entries.
    /// </summary>
    public bool SynopsisCleanMultiEmptyLines { get; set; }

    #endregion

    #region Tags

    public bool HideArtStyleTags { get; set; }

    public bool HideMiscTags { get; set; }

    public bool HidePlotTags { get; set; }

    public bool HideAniDbTags { get; set; }

    public bool HideSettingTags { get; set; }

    public bool HideProgrammingTags { get; set; }

    public bool HideUnverifiedTags { get; set; }

    #endregion

    #region User

    /// <summary>
    /// User configuration.
    /// </summary>
    public List<UserConfiguration> UserList { get; set; }

    #endregion

    #region Library

    /// <summary>
    /// Use Shoko Groups to group Shoko Series together to create the show entries.
    /// </summary>
    public bool UseGroupsForShows { get; set; }

    /// <summary>
    /// Separate movies out of show type libraries.
    /// </summary>
    public bool SeparateMovies { get; set; }

    /// <summary>
    /// Append all specials in AniDB movie series as special features for
    /// the movies.
    /// </summary>
    public bool MovieSpecialsAsExtraFeaturettes { get; set; }

    /// <summary>
    /// Add trailers to entities within the VFS. Trailers within the trailers
    /// directory when not using the VFS are not affected by this option.
    /// </summary>
    public bool AddTrailers { get; set; }

    /// <summary>
    /// Add all credits as theme videos to entities with in the VFS. In a
    /// non-VFS library they will just be filtered out since we can't properly
    /// support them as Jellyfin native features.
    /// </summary>
    public bool AddCreditsAsThemeVideos { get; set; }

    /// <summary>
    /// Add all credits as special features to entities with in the VFS. In a
    /// non-VFS library they will just be filtered out since we can't properly
    /// support them as Jellyfin native features.
    /// </summary>
    public bool AddCreditsAsSpecialFeatures { get; set; }

    /// <summary>
    /// Determines how collections are made.
    /// </summary>
    public CollectionCreationType CollectionGrouping { get; set; }

    /// <summary>
    /// Add a minimum requirement of two entries with the same collection id
    /// before creating a collection for them.
    /// </summary>
    public bool CollectionMinSizeOfTwo { get; set; }

    /// <summary>
    /// Determines how seasons are ordered within a show.
    /// </summary>
    public OrderType SeasonOrdering { get; set; }

    /// <summary>
    /// Determines how specials are placed within seasons, if at all.
    /// </summary>
    public SpecialOrderType SpecialsPlacement { get; set; }

    /// <summary>
    /// Add missing season and episode entries so the user can see at a glance
    /// what is missing, and so the "Upcoming" section of the library works as
    /// intended.
    /// </summary>
    public bool AddMissingMetadata { get; set; }

    public string[] IgnoredFolders { get; set; }

    #endregion

    #region Media Folder

    /// <summary>
    /// Enable/disable the VFS for new media-folders/libraries.
    /// </summary>
    public bool VirtualFileSystem { get; set; }

    /// <summary>
    /// Number of threads to concurrently generate links for the VFS.
    /// </summary>
    public int VirtualFileSystemThreads { get; set; }

    /// <summary>
    /// Enable/disable the filtering for new media-folders/libraries.
    /// </summary>
        [XmlElement("LibraryFiltering")]
    public LibraryFilteringMode LibraryFilteringMode { get; set; }

    /// <summary>
    /// Reaction time to when a library scan starts/ends, because they don't
    /// expose it as an event, so we need to poll instead.
    /// </summary>
    [Range(1, 10)]
    public int LibraryScanReactionTimeInSeconds { get; set; }

    /// <summary>
    /// Per media folder configuration.
    /// </summary>
    public List<MediaFolderConfiguration> MediaFolders { get; set; }

    #endregion

    #region SignalR

    /// <summary>
    /// Enable the SignalR events from Shoko.
    /// </summary>
    public bool SignalR_AutoConnectEnabled { get; set; }

    /// <summary>
    /// Reconnect intervals if the the stream gets disconnected.
    /// </summary>
    public int[] SignalR_AutoReconnectInSeconds { get; set; }

    /// <summary>
    /// Will automatically refresh entries if metadata is updated in Shoko.
    /// </summary>
    public bool SignalR_RefreshEnabled { get; set; }

    /// <summary>
    /// Will notify Jellyfin about files that have been added/updated/removed
    /// in shoko.
    /// </summary>
    public bool SignalR_FileEvents { get; set; }

    #endregion

    #region Experimental features

    /// <summary>
    /// Automagically merge alternate versions after a library scan.
    /// </summary>
    public bool EXPERIMENTAL_AutoMergeVersions { get; set; }

    /// <summary>
    /// Split all movies up before merging them back together.
    /// </summary>
    public bool EXPERIMENTAL_SplitThenMergeMovies { get; set; }

    /// <summary>
    /// Split all episodes up before merging them back together.
    /// </summary>
    public bool EXPERIMENTAL_SplitThenMergeEpisodes { get; set; }

    /// <summary>
    /// Coming soon™.
    /// </summary>
    public bool EXPERIMENTAL_MergeSeasons { get; set; }

    #endregion

    public PluginConfiguration()
    {
        Url = "http://127.0.0.1:8111";
        ServerVersion = null;
        PublicUrl = string.Empty;
        Username = "Default";
        ApiKey = string.Empty;
        HideArtStyleTags = false;
        HideMiscTags = false;
        HidePlotTags = true;
        HideAniDbTags = true;
        HideSettingTags = false;
        HideProgrammingTags = true;
        HideUnverifiedTags = true;
        TitleAddForMultipleEpisodes = true;
        SynopsisCleanLinks = true;
        SynopsisCleanMiscLines = true;
        SynopsisRemoveSummary = true;
        SynopsisCleanMultiEmptyLines = true;
        AddAniDBId = true;
        AddTMDBId = true;
        TitleMainOverride = false;
        TitleMainList = new[] { 
            TitleProvider.Shoko_Default,
        };
        TitleMainOrder = new[] { 
            TitleProvider.Shoko_Default,
            TitleProvider.AniDB_Default,
            TitleProvider.AniDB_LibraryLanguage,
            TitleProvider.AniDB_CountryOfOrigin,
            TitleProvider.TMDB_Default,
            TitleProvider.TMDB_LibraryLanguage,
            TitleProvider.TMDB_CountryOfOrigin,
        };
        TitleAlternateOverride = false;
        TitleAlternateList = new[] {
            TitleProvider.AniDB_CountryOfOrigin
        };
        TitleAlternateOrder = TitleMainOrder.ToArray();
        TitleAllowAny = true;
        DescriptionSourceOverride = false;
        DescriptionSourceList = new[] {
            DescriptionProvider.AniDB,
            DescriptionProvider.TvDB,
            DescriptionProvider.TMDB,
        };
        DescriptionSourceOrder = new[] {
            DescriptionProvider.AniDB,
            DescriptionProvider.TvDB,
            DescriptionProvider.TMDB,
        };
        VirtualFileSystem = CanCreateSymbolicLinks;
        VirtualFileSystemThreads = 4;
        UseGroupsForShows = false;
        SeparateMovies = false;
        MovieSpecialsAsExtraFeaturettes = false;
        AddTrailers = true;
        AddCreditsAsThemeVideos = true;
        AddCreditsAsSpecialFeatures = false;
        SeasonOrdering = OrderType.Default;
        SpecialsPlacement = SpecialOrderType.AfterSeason;
        AddMissingMetadata = true;
        MarkSpecialsWhenGrouped = true;
        CollectionGrouping = CollectionCreationType.None;
        CollectionMinSizeOfTwo = true;
        UserList = new();
        MediaFolders = new();
        IgnoredFolders = new[] { ".streams", "@recently-snapshot" };
        LibraryFilteringMode = LibraryFilteringMode.Auto;
        LibraryScanReactionTimeInSeconds = 1;
        SignalR_AutoConnectEnabled = false;
        SignalR_AutoReconnectInSeconds = new[] { 0, 2, 10, 30, 60, 120, 300 };
        SignalR_RefreshEnabled = false;
        SignalR_FileEvents = false;
        EXPERIMENTAL_AutoMergeVersions = true;
        EXPERIMENTAL_SplitThenMergeMovies = true;
        EXPERIMENTAL_SplitThenMergeEpisodes = false;
        EXPERIMENTAL_MergeSeasons = false;
    }
}
