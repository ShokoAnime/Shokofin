using MediaBrowser.Model.Plugins;
using TextSourceType = Shokofin.Utils.Text.TextSourceType;
using DisplayLanguageType = Shokofin.Utils.Text.DisplayLanguageType;
using SeriesAndBoxSetGroupType = Shokofin.Utils.Ordering.GroupType;
using OrderType = Shokofin.Utils.Ordering.OrderType;

namespace Shokofin.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string Host { get; set; }

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

        public bool AddTvDBId { get; set; }

        public bool PreferAniDbPoster { get; set; }

        public TextSourceType DescriptionSource { get; set; }

        public SeriesAndBoxSetGroupType SeriesGrouping { get; set; }

        public OrderType SeasonOrdering { get; set; }

        public bool MarkSpecialsWhenGrouped { get; set; }

        public SeriesAndBoxSetGroupType BoxSetGrouping { get; set; }

        public OrderType MovieOrdering { get; set; }

        public bool FilterOnLibraryTypes { get; set; }

        public DisplayLanguageType TitleMainType { get; set; }

        public DisplayLanguageType TitleAlternateType { get; set; }

        public bool AddMissingEpisodeMetadata { get; set; }

        public PluginConfiguration()
        {
            Host = "http://127.0.0.1:8111";
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
            AddTvDBId = false;
            PreferAniDbPoster = true;
            TitleMainType = DisplayLanguageType.Default;
            TitleAlternateType = DisplayLanguageType.Origin;
            DescriptionSource = TextSourceType.Default;
            SeriesGrouping = SeriesAndBoxSetGroupType.Default;
            SeasonOrdering = OrderType.Default;
            MarkSpecialsWhenGrouped = true;
            BoxSetGrouping = SeriesAndBoxSetGroupType.Default;
            MovieOrdering = OrderType.Default;
            AddMissingEpisodeMetadata = false;
            FilterOnLibraryTypes = false;
        }
    }
}
