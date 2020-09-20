using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ShokoJellyfin.API.Models;
using Title = ShokoJellyfin.API.Models.Title;
using DisplayTitleType = ShokoJellyfin.Configuration.PluginConfiguration.DisplayTitleType;

namespace ShokoJellyfin.Providers
{
    public class Helper
    {
        public static string GetImageUrl(Image image)
        {
            return image != null ? $"http://{Plugin.Instance.Configuration.Host}:{Plugin.Instance.Configuration.Port}/api/v3/Image/{image.Source}/{image.Type}/{image.ID}" : null;
        }

        public static string SummarySanitizer(string summary) // Based on ShokoMetadata which is based on HAMA's
        {
            var config = Plugin.Instance.Configuration;
            
            if (config.SynopsisCleanLinks)
                summary = Regex.Replace(summary, @"https?:\/\/\w+.\w+(?:\/?\w+)? \[([^\]]+)\]", "");
            
            if (config.SynopsisCleanMiscLines)
                summary = Regex.Replace(summary, @"^(\*|--|~) .*", "", RegexOptions.Multiline);
            
            if (config.SynopsisRemoveSummary)
                summary = Regex.Replace(summary, @"\n(Source|Note|Summary):.*", "", RegexOptions.Singleline);
            
            if (config.SynopsisCleanMultiEmptyLines)
                summary = Regex.Replace(summary, @"\n\n+", "", RegexOptions.Singleline); 

            return summary;
        }

        // Produce titles for episodes if the series-fallback-title is not provided.
        public static ( string, string ) GetEpisodeTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, DisplayTitleType displayTitleType, DisplayTitleType alternateTitleType, string metadataLanguage)
            => GetFullTitles(seriesTitles, episodeTitles, null, displayTitleType, alternateTitleType, metadataLanguage);

        // Produce titles for series if episode titles are omitted.
        public static ( string, string ) GetSeriesTitles(IEnumerable<Title> seriesTitles, string seriesTitle, DisplayTitleType displayTitleType, DisplayTitleType alternateTitleType, string metadataLanguage)
            => GetFullTitles(seriesTitles, null, seriesTitle, displayTitleType, alternateTitleType, metadataLanguage);

        // Produce combined/full titles if both episode titles and a fallback title is provided.
        public static ( string, string ) GetFullTitles(IEnumerable<Title> rSeriesTitles, IEnumerable<Title> rEpisodeTitles, string seriesTitle, DisplayTitleType displayTitleType, DisplayTitleType alternateTitleType, string metadataLanguage)
        {
            // Don't process anything if the series titles are not provided.
            if (rSeriesTitles == null) return ( null, null );
            var seriesTitles = (List<Title>)rSeriesTitles;
            var episodeTitles = (List<Title>)rEpisodeTitles;
            var originLanguage = GuessOriginLanguage(seriesTitles);
            var displayLanguage = metadataLanguage?.ToLower() ?? "en";
            return ( GetFullTitle(seriesTitles, episodeTitles, seriesTitle, displayTitleType, displayLanguage, originLanguage), GetFullTitle(seriesTitles, episodeTitles, seriesTitle, alternateTitleType, displayLanguage, originLanguage) );
        }

        private static string GetEpisodeTitle(IEnumerable<Title> episodeTitle, DisplayTitleType displayTitleType, string displayLanguage, params string[] originLanguages)
            => GetFullTitle(null, episodeTitle, null, displayTitleType, displayLanguage, originLanguages);

        private static string GetSeriesTitle(IEnumerable<Title> seriesTitles, string seriesTitle, DisplayTitleType displayTitleType, string displayLanguage, params string[] originLanguages)
            => GetFullTitle(seriesTitles, null, seriesTitle, displayTitleType, displayLanguage, originLanguages);

        private static string GetFullTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, DisplayTitleType displayTitleType, string displayLanguage, params string[] originLanguages)
        {
            // We need one of them, or it won't work as intended.
            if (seriesTitle == null && episodeTitles == null) return null;
            switch (displayTitleType)
            {
                case DisplayTitleType.Default:
                    // Fallback to prefered series title, but choose the episode title based on this order.
                    // The "main" title on AniDB is _most_ of the time in english, but we also fallback to romaji (japanese) or pinyin (chinese) in case it is not provided.
                    return GetTitle(null, episodeTitles, seriesTitle, "en", "x-jat", "x-zht");
                case DisplayTitleType.Origin:
                    return GetTitle(seriesTitles, episodeTitles, seriesTitle, originLanguages);
                case DisplayTitleType.Localized:
                    var title = GetTitle(seriesTitles, episodeTitles, seriesTitle, displayLanguage);
                    if (string.IsNullOrEmpty(title))
                        goto case DisplayTitleType.Default;
                    return title;
                default:
                    return null;
            }
        }

        private static string GetTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, params string[] languageCandidates)
        {
            if (seriesTitles != null || seriesTitle != null)
            {
                StringBuilder title = new StringBuilder();
                string mainTitle = GetTitleByTypeAndLanguage(seriesTitles, "official", languageCandidates) ?? seriesTitle;
                title.Append(mainTitle);
                if (episodeTitles != null) {
                    var episodeTitle = GetTitleByLanguages(episodeTitles, languageCandidates);
                    // We could not create the complete title, and no mixed languages allowed (outside the specified one(s)), so abort here.
                    if (episodeTitle == null)
                    {
                        // Some movies provide only an english title, so we fallback to english.
                        episodeTitle = GetTitleByLanguages(episodeTitles, "en");
                        if (episodeTitle == null)
                            return null;
                    }
                    if (!string.IsNullOrWhiteSpace(episodeTitle) && episodeTitle != "Complete Movie")
                        title.Append($": {episodeTitle}");
                }
                return title.ToString();
            }
            // Will fallback to null if episode titles are null.
            return GetTitleByLanguages(episodeTitles, languageCandidates);
        }

        private static string GetTitleByTypeAndLanguage(IEnumerable<Title> titles, string type, params string[] langs)
        {
            if (titles != null) foreach (string lang in langs)
            {
                string title = titles.FirstOrDefault(s => s.Language == lang && s.Type == type)?.Name;
                if (title != null) return title;
            }
            return null;
        }

        private static string GetTitleByLanguages(IEnumerable<Title> titles, params string[] langs)
        {
            if (titles != null) foreach (string lang in langs)
            {
                string title = titles.FirstOrDefault(s => s.Language.ToLower() == lang)?.Name;
                if (title != null) return title;
            }
            return null;
        }

        // Guess the origin language based on the main title.
        private static string[] GuessOriginLanguage(IEnumerable<Title> seriesTitle)
        {
            string langCode = seriesTitle.FirstOrDefault(t => t?.Type == "main")?.Language.ToLower();
            // Guess the origin language based on the main title.
            switch (langCode)
            {
                case null: // fallback
                case "x-other":
                case "x-jat":
                    return new string[] { "ja" };
                case "x-zht":
                    return new string[] { "zn-hans", "zn-hant", "zn-c-mcm", "zn" };
                default:
                    return new string[] { langCode };
            }

        }
    }
}