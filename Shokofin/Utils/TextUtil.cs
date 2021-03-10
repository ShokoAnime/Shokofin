using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Shokofin.API.Models;

namespace Shokofin.Utils
{
    public class Text
    {
        public enum DisplayLanguageType {
            Default = 1,
            MetadataPreferred,
            Origin,
            Ignore
        }

        public enum DisplyTitleType {
            MainTitle = 1,
            SubTitle,
            FullTitle,
        }

        public static string SummarySanitizer(string summary) // Based on ShokoMetadata which is based on HAMA's
        {
            var config = Plugin.Instance.Configuration;

            if (config.SynopsisCleanLinks)
                summary = Regex.Replace(summary, @"https?:\/\/\w+.\w+(?:\/?\w+)? \[([^\]]+)\]", match => match.Groups[1].Value);

            if (config.SynopsisCleanMiscLines)
                summary = Regex.Replace(summary, @"^(\*|--|~) .*", "", RegexOptions.Multiline);

            if (config.SynopsisRemoveSummary)
                summary = Regex.Replace(summary, @"\n(Source|Note|Summary):.*", "", RegexOptions.Singleline);

            if (config.SynopsisCleanMultiEmptyLines)
                summary = Regex.Replace(summary, @"\n\n+", "", RegexOptions.Singleline);

            return summary;
        }

        public static ( string, string ) GetEpisodeTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string episodeTitle, string metadataLanguage)
            => GetTitles(seriesTitles, episodeTitles, null, episodeTitle, DisplyTitleType.SubTitle, metadataLanguage);

        public static ( string, string ) GetSeriesTitles(IEnumerable<Title> seriesTitles, string seriesTitle, string metadataLanguage)
            => GetTitles(seriesTitles, null, seriesTitle, null,  DisplyTitleType.MainTitle, metadataLanguage);

        public static ( string, string ) GetMovieTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, string metadataLanguage)
            => GetTitles(seriesTitles, episodeTitles, seriesTitle, episodeTitle, DisplyTitleType.FullTitle, metadataLanguage);

        public static ( string, string ) GetTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, DisplyTitleType outputType, string metadataLanguage)
        {
            // Don't process anything if the series titles are not provided.
            if (seriesTitles == null) return ( null, null );
            var originLanguage = GuessOriginLanguage(seriesTitles);
            return (
                GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, Plugin.Instance.Configuration.TitleMainType, outputType, metadataLanguage, originLanguage),
                GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, Plugin.Instance.Configuration.TitleAlternateType, outputType, metadataLanguage, originLanguage)
            );
        }

        public static string GetEpisodeTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string episodeTitle, string metadataLanguage)
            => GetTitle(seriesTitles, episodeTitles, null, episodeTitle, DisplyTitleType.SubTitle, metadataLanguage);

        public static string GetSeriesTitle(IEnumerable<Title> seriesTitles, string seriesTitle, string metadataLanguage)
            => GetTitle(seriesTitles, null, seriesTitle, null, DisplyTitleType.MainTitle, metadataLanguage);

        public static string GetMovieTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, string metadataLanguage)
            => GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, DisplyTitleType.FullTitle, metadataLanguage);

        public static string GetTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, DisplyTitleType outputType, string metadataLanguage, params string[] originLanguages)
            => GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, Plugin.Instance.Configuration.TitleMainType, outputType, metadataLanguage, originLanguages);

        public static string GetTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, DisplayLanguageType languageType, DisplyTitleType outputType, string displayLanguage, params string[] originLanguages)
        {
            // Don't process anything if the series titles are not provided.
            if (seriesTitles == null)
                return null;
            // Guess origin language if not provided.
            if (originLanguages.Length == 0)
                originLanguages = GuessOriginLanguage(seriesTitles);
            switch (languageType)
            {
                // Let Shoko decide the title.
                case DisplayLanguageType.Default:
                    return __GetTitle(null, null, seriesTitle, episodeTitle, outputType);
                // Display in metadata-preferred language, or fallback to default.
                case DisplayLanguageType.MetadataPreferred:
                    var title = __GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, outputType, displayLanguage);
                    if (string.IsNullOrEmpty(title))
                        goto case DisplayLanguageType.Default;
                    return title;
                // Display in origin language without fallback.
                case DisplayLanguageType.Origin:
                    return __GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, outputType, originLanguages);
                // 'Ignore' will always return null, and all other values will also return null.
                case DisplayLanguageType.Ignore:
                default:
                    return null;
            }
        }

        private static string __GetTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, DisplyTitleType outputType, params string[] languageCandidates)
        {
            // Lazy init string builder when/if we need it.
            StringBuilder titleBuilder = null;
            switch (outputType)
            {
                case DisplyTitleType.MainTitle:
                case DisplyTitleType.FullTitle: {
                    string title = (GetTitleByTypeAndLanguage(seriesTitles, "official", languageCandidates) ?? seriesTitle)?.Trim();
                    // Return series title.
                    if (outputType == DisplyTitleType.MainTitle)
                        return title;
                    titleBuilder = new StringBuilder(title);
                    goto case DisplyTitleType.SubTitle;
                }
                case DisplyTitleType.SubTitle: {
                    string title = (GetTitleByLanguages(episodeTitles, languageCandidates) ?? episodeTitle)?.Trim();
                    // Return episode title.
                    if (outputType == DisplyTitleType.SubTitle)
                        return title;
                    // Ignore sub-title of movie if it strictly equals the text below.
                    if (title != "Complete Movie" && !string.IsNullOrEmpty(title?.Trim()))
                        titleBuilder?.Append($": {title}");
                    return titleBuilder?.ToString() ?? "";
                }
                default:
                    return null;
            }
        }

        public static string GetTitleByTypeAndLanguage(IEnumerable<Title> titles, string type, params string[] langs)
        {
            if (titles != null) foreach (string lang in langs)
            {
                string title = titles.FirstOrDefault(s => s.Language == lang && s.Type == type)?.Name;
                if (title != null) return title;
            }
            return null;
        }

        public static string GetTitleByLanguages(IEnumerable<Title> titles, params string[] langs)
        {
            if (titles != null) foreach (string lang in langs)
            {
                string title = titles.FirstOrDefault(s => lang.Equals(s.Language, System.StringComparison.OrdinalIgnoreCase))?.Name;
                if (title != null) return title;
            }
            return null;
        }

        /// <summary>
        /// Guess the origin language based on the main title.
        /// </summary>
        /// <returns></returns>
        private static string[] GuessOriginLanguage(IEnumerable<Title> titles)
        {
            string langCode = titles.FirstOrDefault(t => t?.Type == "main")?.Language.ToLower();
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
