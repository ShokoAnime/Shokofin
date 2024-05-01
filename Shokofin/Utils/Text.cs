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
    public enum DescriptionSourceType {
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
        => GetDescription(new Dictionary<DescriptionSourceType, string>() {
            {DescriptionSourceType.AniDb, season.AniDB.Description ?? string.Empty},
            {DescriptionSourceType.TvDb, season.TvDB?.Description ?? string.Empty},
        });

    public static string GetDescription(EpisodeInfo episode)
        => GetDescription(new Dictionary<DescriptionSourceType, string>() {
            {DescriptionSourceType.AniDb, episode.AniDB.Description ?? string.Empty},
            {DescriptionSourceType.TvDb, episode.TvDB?.Description ?? string.Empty},
        });

    public static string GetDescription(IEnumerable<EpisodeInfo> episodeList)
        => JoinText(episodeList.Select(episode => GetDescription(episode))) ?? string.Empty;

    private static string GetDescription(Dictionary<DescriptionSourceType, string> descriptions)
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
                DescriptionSourceType.AniDb => descriptions.TryGetValue(DescriptionSourceType.AniDb, out var desc) ? SanitizeTextSummary(desc) : string.Empty,
                DescriptionSourceType.TvDb => descriptions.TryGetValue(DescriptionSourceType.TvDb, out var desc) ? desc : string.Empty,
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

    public static (string?, string?) GetEpisodeTitle(EpisodeInfo episode, SeasonInfo series, string metadataLanguage)
        => (
            GetEpisodeTitleByType(episode, series, DisplayTitleType.Main, metadataLanguage),
            GetEpisodeTitleByType(episode, series, DisplayTitleType.Alternate, metadataLanguage)
        );

    public static (string?, string?) GetSeasonTitle(SeasonInfo series, string metadataLanguage)
        => (
            GetSeriesTitleByType(series, series.Shoko.Name, DisplayTitleType.Main, metadataLanguage),
            GetSeriesTitleByType(series, series.Shoko.Name, DisplayTitleType.Alternate, metadataLanguage)
        );

    public static (string?, string?) GetShowTitle(ShowInfo series, string metadataLanguage)
        => (
            GetSeriesTitleByType(series.DefaultSeason, series.Name, DisplayTitleType.Main, metadataLanguage),
            GetSeriesTitleByType(series.DefaultSeason, series.Name, DisplayTitleType.Alternate, metadataLanguage)
        );

    public static (string?, string?) GetMovieTitle(EpisodeInfo episode, SeasonInfo series, string metadataLanguage)
        => (
            GetMovieTitleByType(episode, series, DisplayTitleType.Main, metadataLanguage),
            GetMovieTitleByType(episode, series, DisplayTitleType.Alternate, metadataLanguage)
        );

    /// <summary>
    /// Returns a list of the providers to check, and in what order
    /// </summary>
    private static TitleProviderLookupMethod[] GetOrderedProvidersForTitle(DisplayTitleType titleType)
        => titleType switch {
            DisplayTitleType.Main =>
                Plugin.Instance.Configuration.TitleMainOverride
                    ? Plugin.Instance.Configuration.TitleMainOrder.Where((t) => Plugin.Instance.Configuration.TitleMainList.Contains(t)).ToArray()
                    : new[] { TitleProviderLookupMethod.Shoko_Default },
            DisplayTitleType.Alternate =>
                Plugin.Instance.Configuration.TitleAlternateOverride
                    ? Plugin.Instance.Configuration.TitleAlternateOrder.Where((t) => Plugin.Instance.Configuration.TitleAlternateList.Contains(t)).ToArray()
                    : new[] { TitleProviderLookupMethod.AniDb_CountryOfOrigin, TitleProviderLookupMethod.TMDB_CountryOfOrigin },
            _ => Array.Empty<TitleProviderLookupMethod>(),
        };

    private static string? GetMovieTitleByType(EpisodeInfo episode, SeasonInfo series, DisplayTitleType type, string metadataLanguage)
    {
        var mainTitle = GetSeriesTitleByType(series, series.Shoko.Name, type, metadataLanguage);
        var subTitle = GetEpisodeTitleByType(episode, series, type, metadataLanguage);

        if (!(string.IsNullOrEmpty(subTitle) || IgnoredSubTitles.Contains(subTitle)))
            return $"{mainTitle}: {subTitle}".Trim();
        return mainTitle?.Trim();
    }

    private static string? GetEpisodeTitleByType(EpisodeInfo episode, SeasonInfo series, DisplayTitleType type, string metadataLanguage)
    {
        foreach (var provider in GetOrderedProvidersForTitle(type)) {
            var title = provider switch {
                TitleProviderLookupMethod.Shoko_Default =>
                    episode.Shoko.Name,
                TitleProviderLookupMethod.AniDb_Default =>
                    GetDefaultTitle(episode.AniDB.Titles),
                TitleProviderLookupMethod.AniDb_LibraryLanguage =>
                    GetTitlesForLanguage(episode.AniDB.Titles, false, metadataLanguage),
                TitleProviderLookupMethod.AniDb_CountryOfOrigin =>
                    GetTitlesForLanguage(episode.AniDB.Titles, false, GuessOriginLanguage(GetMainLanguage(series.AniDB.Titles))),
                _ => null,
            };
            if (!string.IsNullOrEmpty(title))
                return title.Trim();
        }
        return null;
    }

    private static string? GetSeriesTitleByType(SeasonInfo series, string defaultName, DisplayTitleType type, string metadataLanguage)
    {
        foreach (var provider in GetOrderedProvidersForTitle(type)) {
            var title = provider switch {
                TitleProviderLookupMethod.Shoko_Default =>
                    defaultName,
                TitleProviderLookupMethod.AniDb_Default =>
                    GetDefaultTitle(series.AniDB.Titles),
                TitleProviderLookupMethod.AniDb_LibraryLanguage =>
                    GetTitlesForLanguage(series.AniDB.Titles, true, metadataLanguage),
                TitleProviderLookupMethod.AniDb_CountryOfOrigin =>
                    GetTitlesForLanguage(series.AniDB.Titles, true, GuessOriginLanguage(GetMainLanguage(series.AniDB.Titles))),
                _ => null,
            };
            if (!string.IsNullOrEmpty(title))
                return title.Trim();
        }
        return null;
    }

    /// <summary>
    /// Get the default title from the title list.
    /// </summary>
    /// <param name="titles"></param>
    /// <returns></returns>
    private static string? GetDefaultTitle(List<Title> titles)
        => titles.FirstOrDefault(t => t.IsDefault)?.Value;

    /// <summary>
    /// Get the first title availalbe for the language, optionally using types
    /// to filter the list in addition to the metadata languages provided.
    /// </summary>
    /// <param name="titles">Title list to search.</param>
    /// <param name="usingTypes">Search using titles</param>
    /// <param name="metadataLanguages"></param>
    /// <returns></returns>
    public static string? GetTitlesForLanguage(List<Title> titles, bool usingTypes, params string[] metadataLanguages)
    {
        foreach (string lang in metadataLanguages) {
            var titleList = titles.Where(t => t.LanguageCode == lang).ToList();
            if (titleList.Count == 0)
                continue;

            if (usingTypes) {
                var title = titleList.FirstOrDefault(t => t.Type == TitleType.Official)?.Value;
                if (string.IsNullOrEmpty(title) && Plugin.Instance.Configuration.TitleAllowAny)
                    title = titleList.FirstOrDefault()?.Value;
                if (title != null)
                    return title;
            }
            else {
                var title = titles.FirstOrDefault()?.Value;
                if (title != null)
                    return title;
            }
        }
        return null;
    }

    /// <summary>
    /// Get the main title language from the title list.
    /// </summary>
    /// <param name="titles">Title list.</param>
    /// <returns></returns>
    private static string GetMainLanguage(IEnumerable<Title> titles)
        => titles.FirstOrDefault(t => t?.Type == TitleType.Main)?.LanguageCode ?? titles.FirstOrDefault()?.LanguageCode ?? "x-other";

    /// <summary>
    /// Guess the origin language based on the main title language.
    /// </summary>
    /// <param name="titles">Title list.</param>
    /// <returns></returns>
    private static string[] GuessOriginLanguage(string langCode)
        => langCode switch {
            "x-other" => new string[] { "ja" },
            "x-jat" => new string[] { "ja" },
            "x-zht" => new string[] { "zn-hans", "zn-hant", "zn-c-mcm", "zn" },
            _ => new string[] { langCode },
        };
}
