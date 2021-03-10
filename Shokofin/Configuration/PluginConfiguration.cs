using MediaBrowser.Model.Plugins;
using DisplayLanguageType = Shokofin.Utils.Text.DisplayLanguageType;
using SeriesAndBoxSetGroupType = Shokofin.Utils.Ordering.SeriesOrBoxSetGroupType;
using SeasonAndMovieOrderType = Shokofin.Utils.Ordering.SeasonAndMovieOrderType;

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

        public SeriesAndBoxSetGroupType SeriesGrouping { get; set; }

        public SeasonAndMovieOrderType SeasonOrdering { get; set; }

        public bool MarkSpecialsWhenGrouped { get; set; }

        public SeriesAndBoxSetGroupType BoxSetGrouping { get; set; }

        public SeasonAndMovieOrderType MovieOrdering { get; set; }

        public bool SeperateLibraries { get; set; }

        public DisplayLanguageType TitleMainType { get; set; }

        public DisplayLanguageType TitleAlternateType { get; set; }

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
            TitleMainType = DisplayLanguageType.Default;
            TitleAlternateType = DisplayLanguageType.Origin;
            SeriesGrouping = SeriesAndBoxSetGroupType.Default;
            SeasonOrdering = SeasonAndMovieOrderType.Default;
            MarkSpecialsWhenGrouped = true;
            BoxSetGrouping = SeriesAndBoxSetGroupType.Default;
            MovieOrdering = SeasonAndMovieOrderType.Default;
            SeperateLibraries = false;
        }
    }
}
