using Shokofin.API.Info;
using Shokofin.API.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shokofin.Utils;

public static class Text
{
    private static readonly HashSet<char> PunctuationMarks = new() {
        // Common punctuation marks
        '.',   // period
        ',',   // comma
        ';',   // semicolon
        ':',   // colon
        '!',   // exclamation point
        '?',   // question mark
        ')',   // right parenthesis
        ']',   // right bracket
        '}',   // right brace
        '"',  // double quote
        '\'',   // single quote
        '，',  // Chinese comma
        '、',  // Chinese enumeration comma
        '！',  // Chinese exclamation point
        '？',  // Chinese question mark
        '“',  // Chinese double quote
        '”',  // Chinese double quote
        '‘',  // Chinese single quote
        '’',  // Chinese single quote
        '】',  // Chinese right bracket
        '》',  // Chinese right angle bracket
        '）',  // Chinese right parenthesis
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
        '⟩',   // right angle bracket
        '❯',   // right angle bracket
        '❭',   // right angle bracket
        '〉',   // right angle bracket
        '⌉',   // right angle bracket
        '⌋',   // right angle bracket
        '⦄',   // right angle bracket
        '⦆',   // right angle bracket
        '⦈',   // right angle bracket
        '⦊',   // right angle bracket
        '⦌',   // right angle bracket
        '⦎',   // right angle bracket
    };

    private static readonly HashSet<string> IgnoredSubTitles = new(StringComparer.InvariantCultureIgnoreCase) {
        "Complete Movie",
        "OVA",
    };

    /// <summary>
    /// Where to get text the text from.
    /// </summary>
    public enum TextSourceType {
        /// <summary>
        /// Use data from AniDB.
        /// </summary>
        AniDb = 0,

        /// <summary>
        /// Use data from TvDB.
        /// </summary>
        TvDb = 1,

        /// <summary>
        /// Use data from TMDB
        /// </summary>
        TMDB = 2
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

        /// <summary>
        /// Use the main title for the series.
        /// </summary>
        Main = 5,

        // this is just temporary to allow the plugin to compile and show the settings page
        Shoko_Default = 6,
        AniDb_Default = 7,
        AniDb_LibraryLanguage = 8,
        AniDb_CountryOfOrigin = 9,
        TMDB_Default = 10,
        TMDB_LibraryLanguage = 11,
        TMDB_CountryOfOrigin = 12,
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

    public static string GetDescription(ShowInfo show)
        => GetDescription(show.DefaultSeason);

    public static string GetDescription(SeasonInfo season)
        => GetDescription(new Dictionary<TextSourceType, string>() {
            {TextSourceType.AniDb, season.AniDB.Description ?? string.Empty},
            {TextSourceType.TvDb, season.TvDB?.Description ?? string.Empty},
        });

    public static string GetDescription(EpisodeInfo episode)
        => GetDescription(new Dictionary<TextSourceType, string>() {
            {TextSourceType.AniDb, episode.AniDB.Description ?? string.Empty},
            {TextSourceType.TvDb, episode.TvDB?.Description ?? string.Empty},
        });

    public static string GetDescription(IEnumerable<EpisodeInfo> episodeList)
        => JoinText(episodeList.Select(episode => GetDescription(episode))) ?? string.Empty;

    private static string GetDescription(Dictionary<TextSourceType, string> descriptions)
    {
        var overview = string.Empty;

        var providerOrder = Plugin.Instance.Configuration.DescriptionSourceOrder;
        var providers = Plugin.Instance.Configuration.DescriptionSourceList;

        if (providers.Length == 0) {
            return overview; // This is what they want if everything is unticked...
        }

        foreach (var provider in providerOrder.Where(provider => providers.Contains(provider)))
        {
            if (!string.IsNullOrEmpty(overview)) {
                return overview;
            }

            overview = provider switch
            {
                TextSourceType.AniDb => descriptions.TryGetValue(TextSourceType.AniDb, out var desc) ? SanitizeTextSummary(desc) : string.Empty,
                TextSourceType.TvDb => descriptions.TryGetValue(TextSourceType.TvDb, out var desc) ? desc : string.Empty,
                _ => string.Empty
            };
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
            return string.Empty;

        var config = Plugin.Instance.Configuration;

        if (config.SynopsisCleanLinks)
            summary = Regex.Replace(summary, @"https?:\/\/\w+.\w+(?:\/?\w+)? \[([^\]]+)\]", match => match.Groups[1].Value);

        if (config.SynopsisCleanMiscLines)
            summary = Regex.Replace(summary, @"^(\*|--|~) .*", string.Empty, RegexOptions.Multiline);

        if (config.SynopsisRemoveSummary)
            summary = Regex.Replace(summary, @"\n(Source|Note|Summary):.*", string.Empty, RegexOptions.Singleline);

        if (config.SynopsisCleanMultiEmptyLines)
            summary = Regex.Replace(summary, @"\n{2,}", "\n", RegexOptions.Singleline);

        return summary.Trim();
    }

    public static (string?, string?) GetEpisodeTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string episodeTitle, string metadataLanguage)
        => GetTitles(seriesTitles, episodeTitles, null, episodeTitle, DisplayTitleType.SubTitle, metadataLanguage);

    public static (string?, string?) GetSeriesTitles(IEnumerable<Title> seriesTitles, string seriesTitle, string metadataLanguage)
        => GetTitles(seriesTitles, null, seriesTitle, null, DisplayTitleType.MainTitle, metadataLanguage);

    public static (string?, string?) GetMovieTitles(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, string metadataLanguage)
        => GetTitles(seriesTitles, episodeTitles, seriesTitle, episodeTitle, DisplayTitleType.FullTitle, metadataLanguage);

    public static (string?, string?) GetTitles(IEnumerable<Title>? seriesTitles, IEnumerable<Title>? episodeTitles, string? seriesTitle, string? episodeTitle, DisplayTitleType outputType, string metadataLanguage)
    {
        // Don't process anything if the series titles are not provided.
        if (seriesTitles == null)
            return (null, null);
        return (
            GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, Plugin.Instance.Configuration.TitleMainType, outputType, metadataLanguage),
            GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, Plugin.Instance.Configuration.TitleAlternateType, outputType, metadataLanguage)
        );
    }

    public static string? JoinText(IEnumerable<string?> textList)
    {
        var filteredList = textList
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title!.Trim())
            // We distinct the list because some episode entries contain the **exact** same description.
            .Distinct()
            .ToList();

        if (filteredList.Count == 0)
            return null;

        var index = 1;
        var outputText = filteredList[0];
        while (index < filteredList.Count) {
            var lastChar = outputText[^1];
            outputText += PunctuationMarks.Contains(lastChar) ? " " : ". ";
            outputText += filteredList[index++];
        }

        if (filteredList.Count > 1)
            outputText = outputText.TrimEnd();

        return outputText;
    }

    public static string? GetEpisodeTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string episodeTitle, string metadataLanguage)
        => GetTitle(seriesTitles, episodeTitles, null, episodeTitle, DisplayTitleType.SubTitle, metadataLanguage);

    public static string? GetSeriesTitle(IEnumerable<Title> seriesTitles, string seriesTitle, string metadataLanguage)
        => GetTitle(seriesTitles, null, seriesTitle, null, DisplayTitleType.MainTitle, metadataLanguage);

    public static string? GetMovieTitle(IEnumerable<Title> seriesTitles, IEnumerable<Title> episodeTitles, string seriesTitle, string episodeTitle, string metadataLanguage)
        => GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, DisplayTitleType.FullTitle, metadataLanguage);

    public static string? GetTitle(IEnumerable<Title>? seriesTitles, IEnumerable<Title>? episodeTitles, string? seriesTitle, string? episodeTitle, DisplayTitleType outputType, string metadataLanguage)
        => GetTitle(seriesTitles, episodeTitles, seriesTitle, episodeTitle, Plugin.Instance.Configuration.TitleMainType, outputType, metadataLanguage);

    public static string? GetTitle(IEnumerable<Title>? seriesTitles, IEnumerable<Title>? episodeTitles, string? seriesTitle, string? episodeTitle, DisplayLanguageType languageType, DisplayTitleType outputType, string displayLanguage)
    {
        // Don't process anything if the series titles are not provided.
        if (seriesTitles == null)
            return null;
        var mainTitleLanguage = GetMainLanguage(seriesTitles);
        var originLanguages = GuessOriginLanguage(mainTitleLanguage);
        switch (languageType) {
            // 'Ignore' will always return null, and all other values will also return null.
            default:
            case DisplayLanguageType.Ignore:
                return null;
            // Let Shoko decide the title.
            case DisplayLanguageType.Default: 
                return ConstructTitle(() => seriesTitle, () => episodeTitle, outputType);
            // Display in metadata-preferred language, or fallback to default.
            case DisplayLanguageType.MetadataPreferred: {
                var allowAny = Plugin.Instance.Configuration.TitleAllowAny;
                string? getSeriesTitle() => GetTitleByTypeAndLanguage(seriesTitles, TitleType.Official, displayLanguage) ?? (allowAny ? GetTitleByLanguages(seriesTitles, displayLanguage) : null) ?? seriesTitle;
                string? getEpisodeTitle() => GetTitleByLanguages(episodeTitles, displayLanguage) ?? episodeTitle;
                var title = ConstructTitle(getSeriesTitle, getEpisodeTitle, outputType);
                if (string.IsNullOrEmpty(title))
                    goto case DisplayLanguageType.Default;
                return title;
            }
            // Display in origin language.
            case DisplayLanguageType.Origin: {
                var allowAny = Plugin.Instance.Configuration.TitleAllowAny;
                string? getSeriesTitle() => GetTitleByTypeAndLanguage(seriesTitles, TitleType.Official, originLanguages) ?? (allowAny ? GetTitleByLanguages(seriesTitles, originLanguages) : null) ?? seriesTitle;
                string? getEpisodeTitle() => GetTitleByLanguages(episodeTitles, originLanguages) ?? episodeTitle;
                return ConstructTitle(getSeriesTitle, getEpisodeTitle, outputType);
            }
            // Display the main title.
            case DisplayLanguageType.Main: {
                string? getSeriesTitle() => GetTitleByType(seriesTitles, TitleType.Main) ?? seriesTitle;
                string? getEpisodeTitle() => GetTitleByLanguages(episodeTitles, "en", mainTitleLanguage) ?? episodeTitle;
                return ConstructTitle(getSeriesTitle, getEpisodeTitle, outputType);
            }
        }
    }

    private static string? ConstructTitle(Func<string?> getSeriesTitle, Func<string?> getEpisodeTitle, DisplayTitleType outputType)
    {
        switch (outputType) {
            // Return series title.
            case DisplayTitleType.MainTitle:
                return getSeriesTitle()?.Trim();
            // Return episode title.
            case DisplayTitleType.SubTitle:
                return getEpisodeTitle()?.Trim();
            // Return combined series and episode title.
            case DisplayTitleType.FullTitle: {
                var mainTitle = getSeriesTitle()?.Trim();
                var subTitle = getEpisodeTitle()?.Trim();
                // Include sub-title if it does not strictly equals any ignored sub titles.
                if (!string.IsNullOrWhiteSpace(subTitle) && !IgnoredSubTitles.Contains(subTitle))
                    return $"{mainTitle}: {subTitle}";
                return mainTitle;
            }
            default:
                return null;
        }
    }

    public static string? GetTitleByType(IEnumerable<Title> titles, TitleType type)
    {
        if (titles != null) {
            var title = titles.FirstOrDefault(s => s.Type == type)?.Value;
            if (title != null)
                return title;
        }
        return null;
    }

    public static string? GetTitleByTypeAndLanguage(IEnumerable<Title>? titles, TitleType type, params string[] langs)
    {
        if (titles != null) foreach (string lang in langs) {
            var title = titles.FirstOrDefault(s => s.LanguageCode == lang && s.Type == type)?.Value;
            if (title != null)
                return title;
        }
        return null;
    }

    public static string? GetTitleByLanguages(IEnumerable<Title>? titles, params string[] langs)
    {
        if (titles != null) foreach (string lang in langs) {
            var title = titles.FirstOrDefault(s => lang.Equals(s.LanguageCode, System.StringComparison.OrdinalIgnoreCase))?.Value;
            if (title != null)
                return title;
        }
        return null;
    }

    /// <summary>
    /// Get the main title language from the series list.
    /// </summary>
    /// <param name="titles">Series title list.</param>
    /// <returns></returns>
    private static string GetMainLanguage(IEnumerable<Title> titles) {
        return titles.FirstOrDefault(t => t?.Type == TitleType.Main)?.LanguageCode ?? titles.FirstOrDefault()?.LanguageCode ?? "x-other";
    }

    /// <summary>
    /// Guess the origin language based on the main title.
    /// </summary>
    /// <param name="titles">Series title list.</param>
    /// <returns></returns>
    private static string[] GuessOriginLanguage(string langCode)
    {
        // Guess the origin language based on the main title language.
        return langCode switch {
            "x-other" => new string[] { "ja" },
            "x-jat" => new string[] { "ja" },
            "x-zht" => new string[] { "zn-hans", "zn-hant", "zn-c-mcm", "zn" },
            _ => new string[] { langCode },
        };
    }
}
