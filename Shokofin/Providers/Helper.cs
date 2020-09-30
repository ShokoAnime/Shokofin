using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Shokofin.API.Models;
using Title = Shokofin.API.Models.Title;
using DisplayLanguageType = Shokofin.Configuration.PluginConfiguration.DisplayLanguageType;
using EpisodeType = Shokofin.API.Models.Episode.EpisodeType;
using Models = Shokofin.API.Models;
using MediaBrowser.Model.Entities;

namespace Shokofin.Providers
{
    public class Helper
    {
        public static string GetImageUrl(Image image)
        {
            return image != null ? $"http://{Plugin.Instance.Configuration.Host}:{Plugin.Instance.Configuration.Port}/api/v3/Image/{image.Source}/{image.Type}/{image.ID}" : null;
        }

        public static (int, int) GetNumbers(Models.Series series, Models.Episode.AniDB episode)
        {
            return (GetIndexNumber(series, episode), GetMaxNumber(series));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="series"></param>
        /// <param name="episode"></param>
        /// <returns></returns>
        public static int GetIndexNumber(Models.Series series, Models.Episode.AniDB episode)
        {
            int offset = 0;
            switch (episode.Type)
            {
                case EpisodeType.Episode:
                    break;
                case EpisodeType.Special:
                    offset += series.Sizes.Total.Episodes;
                    break; // goto case EpisodeType.Episode;
                case EpisodeType.Credits:
                    offset += series.Sizes.Total?.Specials ?? 0;
                    goto case EpisodeType.Special;
                case EpisodeType.Other:
                    offset += series.Sizes.Total?.Credits ?? 0;
                    goto case EpisodeType.Credits;
                case EpisodeType.Parody:
                    offset += series.Sizes.Total?.Others ?? 0;
                    goto case EpisodeType.Other;
                case EpisodeType.Trailer:
                    offset += series.Sizes.Total?.Parodies ?? 0;
                    goto case EpisodeType.Parody;
            }
            return offset + episode.EpisodeNumber;
        }

        public static int GetMaxNumber(Models.Series series)
        {
            var dict = series.Sizes.Total;
            return dict.Episodes + dict?.Specials ?? 0 + dict?.Credits ?? 0 + dict?.Others ?? 0 + dict?.Parodies ?? 0 + dict?.Trailers ?? 0;
        }

        public static ExtraType? GetExtraType(Models.Episode.AniDB episode)
        {
            switch (episode.Type)
            {
                case EpisodeType.Episode:
                    return null;
                case EpisodeType.Trailer:
                    return ExtraType.Trailer;
                case EpisodeType.Special: {
                    var enTitle = Helper.GetTitleByLanguages(episode.Titles, "en");
                    if (enTitle != null && (enTitle.Contains("intro") || enTitle.Contains("outro"))) {
                        return ExtraType.DeletedScene;
                    }
                    return ExtraType.Scene;
                }
                default:
                    return null;
            }
        }

        public static int GetTagFilter()
        {
            var config = Plugin.Instance.Configuration;
            var filter = 0;

            if (config.HideAniDbTags) filter = 1;
            if (config.HideArtStyleTags) filter |= (filter << 1);
            if (config.HideSourceTags) filter |= (filter << 2);
            if (config.HideMiscTags) filter |= (filter << 3);
            if (config.HidePlotTags) filter |= (filter << 4);

            return filter;
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

        public enum TitleType {
            MainTitle = 1,
            SubTitle,
            FullTitle,
        }

        public static ( string, string ) GetEpisodeTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string episodeTitle, DisplayLanguageType mainLanguage, DisplayLanguageType alternateLanguage, string metadataLanguage)
            => GetTitles(seriesTitles, episodeTitles, null, episodeTitle, mainLanguage, alternateLanguage, TitleType.SubTitle, metadataLanguage);

        public static ( string, string ) GetSeriesTitles(IEnumerable<Title> seriesTitles, string seriesTitle, DisplayLanguageType mainLanguage, DisplayLanguageType alternateLanguage, string metadataLanguage)
            => GetTitles(seriesTitles, null, seriesTitle, null, mainLanguage, alternateLanguage, TitleType.MainTitle, metadataLanguage);

        public static ( string, string ) GetMovieTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, DisplayLanguageType mainLanguage, DisplayLanguageType alternateLanguage, string metadataLanguage)
            => GetTitles(seriesTitles, episodeTitles, seriesTitle, episodeTitle, mainLanguage, alternateLanguage, TitleType.FullTitle, metadataLanguage);

        public static ( string, string ) GetTitles(IEnumerable<Title> rSeriesTitles, IEnumerable<Title> rEpisodeTitles, string seriesTitle, string episodeTitle, DisplayLanguageType mainLanguage, DisplayLanguageType alternateLanguage, TitleType outputType, string metadataLanguage)
        {
            // Don't process anything if the series titles are not provided.
            if (rSeriesTitles == null) return ( null, null );
            var seriesTitles = (List<Title>)rSeriesTitles;
            var episodeTitles = (List<Title>)rEpisodeTitles;
            var originLanguage = GuessOriginLanguage(seriesTitles);
            var displayLanguage = metadataLanguage?.ToLower() ?? "en";
            return ( GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, mainLanguage, outputType, displayLanguage, originLanguage), GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, alternateLanguage, outputType, displayLanguage, originLanguage) );
        }

        public static string GetTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, DisplayLanguageType languageType, TitleType outputType, string displayLanguage, params string[] originLanguages)
        {
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
                default:
                    return null;
            }
        }

        internal static string __GetTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, TitleType outputType, params string[] languageCandidates)
        {
            // Lazy init string builder when/if we need it.
            StringBuilder titleBuilder = null;
            switch (outputType)
            {
                case TitleType.MainTitle:
                case TitleType.FullTitle: {
                    string title = (GetTitleByTypeAndLanguage(seriesTitles, "official", languageCandidates) ?? seriesTitle)?.Trim();
                    // Return series title.
                    if (outputType == TitleType.MainTitle)
                        return title;
                    titleBuilder = new StringBuilder(title);
                    goto case TitleType.SubTitle;
                }
                case TitleType.SubTitle: {
                    string title = (GetTitleByLanguages(episodeTitles, languageCandidates) ?? episodeTitle)?.Trim();
                    // Return episode title.
                    if (outputType == TitleType.SubTitle)
                        return title;
                    // Ignore sub-title of movie if it strictly equals the text below.
                    if (title != "Complete Movie")
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