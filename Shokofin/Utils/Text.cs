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
    /// Determines which provider, and which method to construct the title in.
    /// </summary>
    public enum TitleProviderLookupMethod {
        None = 0,
        Shoko_Default = 1,
        AniDb_Default = 2,
        AniDb_LibraryLanguage = 3,
        AniDb_CountryOfOrigin = 4,
        TMDB_Default = 5,
        TMDB_LibraryLanguage = 6,
        TMDB_CountryOfOrigin = 7,
    }

    /// <summary>
    /// Determines the type of title to construct.
    /// </summary>
    public enum DisplayTitleType {
        /// <summary>
        /// The Main title used for Series/Seasons/Episodes
        /// </summary>
        Main = 0,

        /// <summary>
        /// The secondary title used for Series/Seasons/Episodes
        /// </summary>
        Alternate = 1,
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

    /// <summary>
    /// Returns a list of the providers to check, and in what order
    /// </summary>
    private static TitleProviderLookupMethod[] GetOrderedProvidersForTitle(DisplayTitleType titleType)
    {
        switch (titleType) {
            case DisplayTitleType.Main:
                if (!Plugin.Instance.Configuration.TitleMainOverride) {
                    return new[] { 
                        TitleProviderLookupMethod.Shoko_Default,
                        TitleProviderLookupMethod.AniDb_Default, TitleProviderLookupMethod.AniDb_LibraryLanguage, TitleProviderLookupMethod.AniDb_CountryOfOrigin,
                        TitleProviderLookupMethod.TMDB_Default, TitleProviderLookupMethod.TMDB_LibraryLanguage, TitleProviderLookupMethod.TMDB_CountryOfOrigin
                    };
                }
                return Plugin.Instance.Configuration.TitleMainOrder.Where((t) => Plugin.Instance.Configuration.TitleMainList.Contains(t)).ToArray();
            case DisplayTitleType.Alternate:
                if (Plugin.Instance.Configuration.TitleAlternateOverride)
                    return Plugin.Instance.Configuration.TitleAlternateOrder.Where((t) => Plugin.Instance.Configuration.TitleAlternateList.Contains(t)).ToArray();
                return Array.Empty<TitleProviderLookupMethod>();
            default:
                return Array.Empty<TitleProviderLookupMethod>();
        }
    }

    public static string? GetTitlesForLanguage(List<Title> titles, string metadataLanguage)
    {
        var titleList = titles.Where(t => t.LanguageCode == metadataLanguage);
        if (!titleList.Any())
            return null;
        var title = titleList.FirstOrDefault(t => t.Type == TitleType.Official)?.Value;
        if (string.IsNullOrEmpty(title) && Plugin.Instance.Configuration.TitleAllowAny)
            title = titleList.FirstOrDefault()?.Value;
        return title;
    }

    public static (string?, string?) GetEpisodeTitle(EpisodeInfo episode, SeasonInfo series, string metadataLanguage)
        => (GetEpisodeTitleByType(episode, series, DisplayTitleType.Main, metadataLanguage), GetEpisodeTitleByType(episode, series, DisplayTitleType.Alternate, metadataLanguage));

    private static string? GetEpisodeTitleByType(EpisodeInfo episode, SeasonInfo series, DisplayTitleType type, string metadataLanguage)
    {
        string? title = null;
        foreach (var provider in GetOrderedProvidersForTitle(type)) {
            switch (provider) {
                case TitleProviderLookupMethod.Shoko_Default:
                    title = episode.Shoko.Name.Trim();
                    break;
                case TitleProviderLookupMethod.AniDb_Default:
                    title = episode.AniDB.Titles.FirstOrDefault(t => t.IsDefault)?.Value;
                    break;
                case TitleProviderLookupMethod.AniDb_LibraryLanguage:
                    title = GetTitlesForLanguage(episode.AniDB.Titles, metadataLanguage);
                    break;
                case TitleProviderLookupMethod.AniDb_CountryOfOrigin:
                    var langCode = series.AniDB.Titles.FirstOrDefault(t => t.Value == series.AniDB.Title)?.LanguageCode;
                    if (string.IsNullOrEmpty(langCode))
                        break;
                    title = GetTitlesForLanguage(episode.AniDB.Titles, langCode);
                    break;
                default:
                    break;
            };
            if (!string.IsNullOrEmpty(title))
                return title;
        }
        return title;
    }

    public static (string?, string?) GetSeriesTitle(SeasonInfo series, string metadataLanguage)
        => (GetSeriesTitleByType(series, DisplayTitleType.Main, metadataLanguage), GetSeriesTitleByType(series, DisplayTitleType.Alternate, metadataLanguage));

    private static string? GetSeriesTitleByType(SeasonInfo series, DisplayTitleType type, string metadataLanguage)
    {
        string? title = null;
        foreach (var provider in GetOrderedProvidersForTitle(type)) {
            switch (provider) {
                case TitleProviderLookupMethod.Shoko_Default:
                    title = series.Shoko.Name.Trim();
                    break;
                case TitleProviderLookupMethod.AniDb_Default:
                    title = series.AniDB.Titles.FirstOrDefault(t => t.IsDefault)?.Value;
                    break;
                case TitleProviderLookupMethod.AniDb_LibraryLanguage:
                    title = GetTitlesForLanguage(series.AniDB.Titles, metadataLanguage);
                    break;
                case TitleProviderLookupMethod.AniDb_CountryOfOrigin:
                    var langCode = series.AniDB.Titles.FirstOrDefault(t => t.Value == series.AniDB.Title)?.LanguageCode;
                    if (string.IsNullOrEmpty(langCode))
                        break;
                    title = GetTitlesForLanguage(series.AniDB.Titles, langCode);
                    break;
                default:
                    break;
            }
            if (!string.IsNullOrEmpty(title))
                break;
        }
        return title;
    }

    public static (string?, string?) GetMovieTitle(EpisodeInfo episode, SeasonInfo series, string metadataLanguage)
        => (GetMovieTitleByType(episode, series, DisplayTitleType.Main, metadataLanguage), GetMovieTitleByType(episode, series, DisplayTitleType.Alternate, metadataLanguage));

    private static string? GetMovieTitleByType(EpisodeInfo episode, SeasonInfo series, DisplayTitleType type, string metadataLanguage)
    {
        var mainTitle = GetSeriesTitleByType(series, type, metadataLanguage);
        var subTitle = GetEpisodeTitleByType(episode, series, type, metadataLanguage);

        if (!(string.IsNullOrEmpty(subTitle) || IgnoredSubTitles.Contains(subTitle)))
            return $"{mainTitle}: {subTitle}";
        return mainTitle;
    }
}
