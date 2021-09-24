using MediaBrowser.Model.Plugins;
using System;
using System.Text.Json.Serialization;

using TextSourceType = Shokofin.Utils.Text.TextSourceType;
using DisplayLanguageType = Shokofin.Utils.Text.DisplayLanguageType;
using SeriesAndBoxSetGroupType = Shokofin.Utils.Ordering.GroupType;
using OrderType = Shokofin.Utils.Ordering.OrderType;
using SpecialOrderType = Shokofin.Utils.Ordering.SpecialOrderType;

namespace Shokofin.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string Host { get; set; }

        public string PublicHost { get; set; }

        [JsonIgnore]
        public virtual string PrettyHost 
            => string.IsNullOrEmpty(PublicHost) ? Host : PublicHost;

        public string Username { get; set; }

        public string Password { get; set; }

        public string ApiKey { get; set; }

        public bool UpdateWatchedStatus { get; set; }

        public bool HideArtStyleTags { get; set; }

        public bool HideSourceTags { get; set; }

        public bool HideMiscTags { get; set; }

        public bool HidePlotTags { get; set; }

        public bool HideAniDbTags { get; set; }

        public bool SynopsisCleanLinks { get; set; }

        public bool SynopsisCleanMiscLines { get; set; }

        public bool SynopsisRemoveSummary { get; set; }

        public bool SynopsisCleanMultiEmptyLines { get; set; }

        public bool AddAniDBId { get; set; }

        public bool PreferAniDbPoster { get; set; }

        public TextSourceType DescriptionSource { get; set; }

        public SeriesAndBoxSetGroupType SeriesGrouping { get; set; }

        public OrderType SeasonOrdering { get; set; }

        public bool MarkSpecialsWhenGrouped { get; set; }

        public SpecialOrderType SpecialsPlacement { get; set; }

        public SeriesAndBoxSetGroupType BoxSetGrouping { get; set; }

        public OrderType MovieOrdering { get; set; }

        public bool FilterOnLibraryTypes { get; set; }

        public DisplayLanguageType TitleMainType { get; set; }

        public DisplayLanguageType TitleAlternateType { get; set; }

        public UserConfiguration[] UserList { get; set; }

        public PluginConfiguration()
        {
            Host = "http://127.0.0.1:8111";
            PublicHost = "";
            Username = "Default";
            Password = "";
            ApiKey = "";
            UpdateWatchedStatus = false;
            HideArtStyleTags = false;
            HideSourceTags = false;
            HideMiscTags = false;
            HidePlotTags = true;
            HideAniDbTags = true;
            SynopsisCleanLinks = true;
            SynopsisCleanMiscLines = true;
            SynopsisRemoveSummary = true;
            SynopsisCleanMultiEmptyLines = true;
            AddAniDBId = true;
            PreferAniDbPoster = true;
            TitleMainType = DisplayLanguageType.Default;
            TitleAlternateType = DisplayLanguageType.Origin;
            DescriptionSource = TextSourceType.Default;
            SeriesGrouping = SeriesAndBoxSetGroupType.Default;
            SeasonOrdering = OrderType.Default;
            SpecialsPlacement = SpecialOrderType.Default;
            MarkSpecialsWhenGrouped = true;
            BoxSetGrouping = SeriesAndBoxSetGroupType.Default;
            MovieOrdering = OrderType.Default;
            FilterOnLibraryTypes = false;
            UserList = Array.Empty<UserConfiguration>();
        }
    }
}
