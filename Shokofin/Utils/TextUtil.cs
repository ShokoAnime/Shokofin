using Shokofin.API.Info;
using Shokofin.API.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Shokofin.Utils
{
    public class Text
    {
        private static HashSet<char> PunctuationMarks = new() {
            // Common punctuation marks
            '.',   // period
            ',',   // comma
            ';',   // semicolon
            ':',   // colon
            '!',   // exclamation point
            '?',   // question mark
            '-',   // hyphen
            '_',   // underscore
            '(',   // left parenthesis
            ')',   // right parenthesis
            '[',   // left bracket
            ']',   // right bracket
            '{',   // left brace
            '}',   // right brace
            '"',  // double quote
            '\'',   // single quote
            '/',   // forward slash
            '\\',  // backslash
            '|',   // vertical bar
            '@',   // at symbol
            '#',   // pound or hash
            '$',   // dollar
            '%',   // percent
            '^',   // caret
            '&',   // ampersand
            '*',   // asterisk
            '+',   // plus
            '=',   // equals
            '<',   // less-than
            '>',   // greater-than
            '。',  // Chinese full stop
            '，',  // Chinese comma
            '、',  // Chinese enumeration comma
            '；',  // Chinese semicolon
            '：',  // Chinese colon
            '！',  // Chinese exclamation point
            '？',  // Chinese question mark
            '“',  // Chinese double quote
            '”',  // Chinese double quote
            '‘',  // Chinese single quote
            '’',  // Chinese single quote
            '【',  // Chinese left bracket
            '】',  // Chinese right bracket
            '《',  // Chinese left angle bracket
            '》',  // Chinese right angle bracket
            '（',  // Chinese left parenthesis
            '）',  // Chinese right parenthesis
            '－',  // Chinese hyphen
            '・',  // Japanese middle dot

            // Less common punctuation marks
            '‽',    // interrobang
            '❞',   // double question mark
            '❝',   // double exclamation mark
            '⁇',   // question mark variation
            '⁈',   // exclamation mark variation
            '❕',   // white exclamation mark
            '❔',   // white question mark
            '‽',   // interrobang
            '⁉',   // exclamation mark
            '‽',   // interrobang
            '※',   // reference mark
            '‒',   // figure dash
            '–',   // en dash
            '—',   // em dash
            '⸺',   // two-em dash
            '⸻',   // three-em dash
            '⟨',   // left angle bracket
            '⟩',   // right angle bracket
            '❮',   // left angle bracket
            '❯',   // right angle bracket
            '❬',   // left angle bracket
            '❭',   // right angle bracket
            '〈',   // left angle bracket
            '〉',   // right angle bracket
            '⌈',   // left angle bracket
            '⌉',   // right angle bracket
            '⌊',   // left angle bracket
            '⌋',   // right angle bracket
            '⦃',   // left angle bracket
            '⦄',   // right angle bracket
            '⦅',   // left angle bracket
            '⦆',   // right angle bracket
            '⦇',   // left angle bracket
            '⦈',   // right angle bracket
            '⦉',   // left angle bracket
            '⦊',   // right angle bracket
            '⦋',   // left angle bracket
            '⦌',   // right angle bracket
            '⦍',   // left angle bracket
            '⦎',   // right angle bracket
        };

        /// <summary>
        /// Where to get text the text from.
        /// </summary>
        public enum TextSourceType {
            /// <summary>
            /// Use the default source for the current series grouping.
            /// </summary>
            Default = 1,

            /// <summary>
            /// Only use AniDb, or null if no data is available.
            /// </summary>
            OnlyAniDb = 2,

            /// <summary>
            /// Prefer the AniDb data, but use the other provider if there is no
            /// AniDb data available.
            /// </summary>
            PreferAniDb = 3,

            /// <summary>
            /// Prefer the other provider (e.g. TvDB/TMDB)
            /// </summary>
            PreferOther = 4,

            /// <summary>
            /// Only use the other provider, or null if no data is available.
            /// </summary>
            OnlyOther = 5,
        }

        /// <summary>
        /// Determines the language to construct the title in.
        /// </summary>
        public enum DisplayLanguageType {
            /// <summary>
            /// Let Shoko decide what to display.
            /// </summary>
            Default = 1,

            /// <summary>
            /// Prefer to use the selected metadata language for the library if
            /// available, but fallback to the default view if it's not
            /// available.
            /// </summary>
            MetadataPreferred = 2,

            /// <summary>
            /// Use the origin language for the series.
            /// </summary>
            Origin = 3,

            /// <summary>
            /// Don't display a title.
            /// </summary>
            Ignore = 4,
        }

        /// <summary>
        /// Determines the type of title to construct.
        /// </summary>
        public enum DisplayTitleType {
            /// <summary>
            /// Only construct the main title.
            /// </summary>
            MainTitle = 1,

            /// <summary>
            /// Only construct the sub title.
            /// </summary>
            SubTitle = 2,

            /// <summary>
            /// Construct a combined main and sub title.
            /// </summary>
            FullTitle = 3,
        }

        public static string GetDescription(SeriesInfo series)
            => GetDescription(series.AniDB.Description, series.TvDB?.Description);

        public static string GetDescription(EpisodeInfo episode)
            => GetDescription(episode.AniDB.Description, episode.TvDB?.Description);

        public static string GetDescription(IEnumerable<EpisodeInfo> episodeList)
            => JoinText(episodeList.Select(episode => GetDescription(episode)));

        private static string GetDescription(string aniDbDescription, string otherDescription)
        {
            string overview;
            switch (Plugin.Instance.Configuration.DescriptionSource) {
                default:
                    switch (Plugin.Instance.Configuration.SeriesGrouping) {
                        default:
                            goto preferAniDb;
                        case Ordering.GroupType.MergeFriendly:
                            goto preferOther;
                    }
                case TextSourceType.PreferOther:
                    preferOther: overview = otherDescription ?? "";
                    if (string.IsNullOrEmpty(overview))
                        goto case TextSourceType.OnlyAniDb;
                    break;
                case TextSourceType.PreferAniDb:
                    preferAniDb: overview = Text.SanitizeTextSummary(aniDbDescription);
                    if (string.IsNullOrEmpty(overview))
                        goto case TextSourceType.OnlyOther;
                    break;
                case TextSourceType.OnlyAniDb:
                    overview = Text.SanitizeTextSummary(aniDbDescription);
                    break;
                case TextSourceType.OnlyOther:
                    overview = otherDescription ?? "";
                    break;
            }
            return overview;
        }

        /// <summary>
        /// Based on ShokoMetadata's summary sanitizer which in turn is based on HAMA's summary sanitizer.
        /// </summary>
        /// <param name="summary">The raw AniDB summary</param>
        /// <returns>The sanitized AniDB summary</returns>
        public static string SanitizeTextSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return "";

            var config = Plugin.Instance.Configuration;

            if (config.SynopsisCleanLinks)
                summary = Regex.Replace(summary, @"https?:\/\/\w+.\w+(?:\/?\w+)? \[([^\]]+)\]", match => match.Groups[1].Value);

            if (config.SynopsisCleanMiscLines)
                summary = Regex.Replace(summary, @"^(\*|--|~) .*", "", RegexOptions.Multiline);

            if (config.SynopsisRemoveSummary)
                summary = Regex.Replace(summary, @"\n(Source|Note|Summary):.*", "", RegexOptions.Singleline);

            if (config.SynopsisCleanMultiEmptyLines)
                summary = Regex.Replace(summary, @"\n{2,}", "\n", RegexOptions.Singleline);

            return summary.Trim();
        }

        public static ( string, string ) GetEpisodeTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string episodeTitle, string metadataLanguage)
            => GetTitles(seriesTitles, episodeTitles, null, episodeTitle, DisplayTitleType.SubTitle, metadataLanguage);

        public static ( string, string ) GetSeriesTitles(IEnumerable<Title> seriesTitles, string seriesTitle, string metadataLanguage)
            => GetTitles(seriesTitles, null, seriesTitle, null,  DisplayTitleType.MainTitle, metadataLanguage);

        public static ( string, string ) GetMovieTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, string metadataLanguage)
            => GetTitles(seriesTitles, episodeTitles, seriesTitle, episodeTitle, DisplayTitleType.FullTitle, metadataLanguage);

        public static ( string, string ) GetTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, DisplayTitleType outputType, string metadataLanguage)
        {
            // Don't process anything if the series titles are not provided.
            if (seriesTitles == null)
                return ( null, null );
            var originLanguage = GuessOriginLanguage(seriesTitles);
            return (
                GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, Plugin.Instance.Configuration.TitleMainType, outputType, metadataLanguage, originLanguage),
                GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, Plugin.Instance.Configuration.TitleAlternateType, outputType, metadataLanguage, originLanguage)
            );
        }

        public static string JoinText(IEnumerable<string> textList)
        {
            var filteredList = textList
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Select(title => title.Trim())
                // We distinct the list because some episode entries contain the **exact** same description.
                .Distinct()
                .ToList();

            if (filteredList.Count == 0)
                return "";

            var index = 1;
            var outputText = filteredList[0];
            while (index < filteredList.Count) {
                var lastChar = outputText[^1];
                outputText += PunctuationMarks.Contains(lastChar) ? " " : ". ";
                outputText += filteredList[index++];
            }

            if (filteredList.Count > 1)
                outputText.TrimEnd();

            return outputText;
        }

        public static string GetEpisodeTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string episodeTitle, string metadataLanguage)
            => GetTitle(seriesTitles, episodeTitles, null, episodeTitle, DisplayTitleType.SubTitle, metadataLanguage);

        public static string GetSeriesTitle(IEnumerable<Title> seriesTitles, string seriesTitle, string metadataLanguage)
            => GetTitle(seriesTitles, null, seriesTitle, null, DisplayTitleType.MainTitle, metadataLanguage);

        public static string GetMovieTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, string metadataLanguage)
            => GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, DisplayTitleType.FullTitle, metadataLanguage);

        public static string GetTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, DisplayTitleType outputType, string metadataLanguage, params string[] originLanguages)
            => GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, Plugin.Instance.Configuration.TitleMainType, outputType, metadataLanguage, originLanguages);

        public static string GetTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, DisplayLanguageType languageType, DisplayTitleType outputType, string displayLanguage, params string[] originLanguages)
        {
            // Don't process anything if the series titles are not provided.
            if (seriesTitles == null)
                return null;
            // Guess origin language if not provided.
            if (originLanguages.Length == 0)
                originLanguages = GuessOriginLanguage(seriesTitles);
            switch (languageType) {
                // 'Ignore' will always return null, and all other values will also return null.
                default:
                case DisplayLanguageType.Ignore:
                    return null;
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
            }
        }

        private static string __GetTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, DisplayTitleType outputType, params string[] languageCandidates)
        {
            // Lazy init string builder when/if we need it.
            StringBuilder titleBuilder = null;
            switch (outputType) {
                default:
                    return null;
                case DisplayTitleType.MainTitle:
                case DisplayTitleType.FullTitle: {
                    string title = (GetTitleByTypeAndLanguage(seriesTitles, TitleType.Official, languageCandidates) ?? seriesTitle)?.Trim();
                    // Return series title.
                    if (outputType == DisplayTitleType.MainTitle)
                        return title;
                    titleBuilder = new StringBuilder(title);
                    goto case DisplayTitleType.SubTitle;
                }
                case DisplayTitleType.SubTitle: {
                    string title = (GetTitleByLanguages(episodeTitles, languageCandidates) ?? episodeTitle)?.Trim();
                    // Return episode title.
                    if (outputType == DisplayTitleType.SubTitle)
                        return title;
                    // Ignore sub-title of movie if it strictly equals the text below.
                    if (title != "Complete Movie" && !string.IsNullOrEmpty(title?.Trim()))
                        titleBuilder?.Append($": {title}");
                    return titleBuilder?.ToString() ?? "";
                }
            }
        }

        public static string GetTitleByTypeAndLanguage(IEnumerable<Title> titles, TitleType type, params string[] langs)
        {
            if (titles != null) foreach (string lang in langs) {
                string title = titles.FirstOrDefault(s => s.LanguageCode == lang && s.Type == type)?.Value;
                if (title != null)
                    return title;
            }
            return null;
        }

        public static string GetTitleByLanguages(IEnumerable<Title> titles, params string[] langs)
        {
            if (titles != null) foreach (string lang in langs) {
                string title = titles.FirstOrDefault(s => lang.Equals(s.LanguageCode, System.StringComparison.OrdinalIgnoreCase))?.Value;
                if (title != null)
                    return title;
            }
            return null;
        }

        /// <summary>
        /// Guess the origin language based on the main title.
        /// </summary>
        /// <returns></returns>
        private static string[] GuessOriginLanguage(IEnumerable<Title> titles)
        {
            string langCode = titles.FirstOrDefault(t => t?.Type == TitleType.Main)?.LanguageCode;
            // Guess the origin language based on the main title.
            switch (langCode) {
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
