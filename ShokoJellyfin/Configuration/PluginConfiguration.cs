using MediaBrowser.Model.Plugins;

namespace ShokoJellyfin.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string Host { get; set; }
        
        public string Port { get; set; }
        
        public string Username { get; set; }
        
        public string Password { get; set; }
        
        public string ApiKey { get; set; }
        
        public bool UpdateWatchedStatus { get; set; }
        
        public bool UseTvDbSeasonOrdering { get; set; }
        
        // public bool UseShokoThumbnails { get; set; }
        
        public bool HideArtStyleTags { get; set; }
        
        public bool HideSourceTags { get; set; }
        
        public bool HideMiscTags { get; set; }
        
        public bool HidePlotTags { get; set; }

        public bool HideAniDbTags { get; set; }
        
        public bool SynopsisCleanLinks { get; set; }
        
        public bool SynopsisCleanMiscLines { get; set; }
        
        public bool SynopsisRemoveSummary { get; set; }
        
        public bool SynopsisCleanMultiEmptyLines { get; set; }

        public PluginConfiguration()
        {
            Host = "127.0.0.1";
            Port = "8111";
            Username = "Default";
            Password = "";
            ApiKey = "";
            UpdateWatchedStatus = false;
            UseTvDbSeasonOrdering = false;
            // UseShokoThumbnails = true;
            HideArtStyleTags = false;
            HideSourceTags = false;
            HideMiscTags = false;
            HidePlotTags = true;
            HideAniDbTags = true;
            SynopsisCleanLinks = true;
            SynopsisCleanMiscLines = true;
            SynopsisRemoveSummary = true;
            SynopsisCleanMultiEmptyLines = true;
        }
    }
}