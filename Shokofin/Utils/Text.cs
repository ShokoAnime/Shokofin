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
        '⁉',   // exclamation mark
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
    /// Determines which provider to use to provide the descriptions.
    /// </summary>
    public enum DescriptionProvider {
        /// <summary>
        /// Provide the Shoko Group description for the show, if the show is
        /// constructed using Shoko's groups feature.
        /// </summary>
        Shoko = 1,

        /// <summary>
        /// Provide the description from AniDB.
        /// </summary>
        AniDB = 2,

        /// <summary>
        /// Provide the description from TvDB.
        /// </summary>
        TvDB = 3,

        /// <summary>
        /// Provide the description from TMDB.
        /// </summary>
        TMDB = 4
    }

    /// <summary>
    /// Determines which provider and method to use to look-up the title.
    /// </summary>
    public enum TitleProvider {
        /// <summary>
        /// Let Shoko decide what to display.
        /// </summary>
        Shoko_Default = 1,

        /// <summary>
        /// Use the default title as provided by AniDB.
        /// </summary>
        AniDB_Default = 2,

        /// <summary>
        /// Use the selected metadata language for the library as provided by
        /// AniDB.
        /// </summary>
        AniDB_LibraryLanguage = 3,

        /// <summary>
        /// Use the title in the origin language as provided by AniDB.
        /// </summary>
        AniDB_CountryOfOrigin = 4,

        /// <summary>
        /// Use the default title as provided by TMDB.
        /// </summary>
        TMDB_Default = 5,

        /// <summary>
        /// Use the selected metadata language for the library as provided by
        /// TMDB.
        /// </summary>
        TMDB_LibraryLanguage = 6,

        /// <summary>
        /// Use the title in the origin language as provided by TMDB.
        /// </summary>
        TMDB_CountryOfOrigin = 7,
    }

    /// <summary>
    /// Determines which type of title to look-up.
    /// </summary>
    public enum TitleProviderType {
        /// <summary>
        /// The main title used for metadata entries.
        /// </summary>
        Main = 0,

        /// <summary>
        /// The secondary title used for metadata entries.
        /// </summary>
        Alternate = 1,
    }

    public static string GetDescription(ShowInfo show)
        => GetDescriptionByDict(new() {
            {DescriptionProvider.Shoko, show.Shoko?.Description},
            {DescriptionProvider.AniDB, show.DefaultSeason.AniDB.Description},
            {DescriptionProvider.TvDB, show.DefaultSeason.TvDB?.Description},
        });

    public static string GetDescription(SeasonInfo season)
        => GetDescriptionByDict(new() {
            {DescriptionProvider.AniDB, season.AniDB.Description},
            {DescriptionProvider.TvDB, season.TvDB?.Description},
        });

    public static string GetDescription(EpisodeInfo episode)
        => GetDescriptionByDict(new() {
            {DescriptionProvider.AniDB, episode.AniDB.Description},
            {DescriptionProvider.TvDB, episode.TvDB?.Description},
        });

    public static string GetDescription(IEnumerable<EpisodeInfo> episodeList)
        => JoinText(episodeList.Select(episode => GetDescription(episode))) ?? string.Empty;

    /// <summary>
    /// Returns a list of the description providers to check, and in what order
    /// </summary>
    private static DescriptionProvider[] GetOrderedDescriptionProviders()
        => Plugin.Instance.Configuration.DescriptionSourceOverride
            ? Plugin.Instance.Configuration.DescriptionSourceOrder.Where((t) => Plugin.Instance.Configuration.DescriptionSourceList.Contains(t)).ToArray()
            : new[] { DescriptionProvider.Shoko, DescriptionProvider.AniDB, DescriptionProvider.TvDB, DescriptionProvider.TMDB };

    private static string GetDescriptionByDict(Dictionary<DescriptionProvider, string?> descriptions)
    {
        foreach (var provider in GetOrderedDescriptionProviders())
        {
            var overview = provider switch
            {
                DescriptionProvider.Shoko =>
                    descriptions.TryGetValue(DescriptionProvider.Shoko, out var desc) ? desc : null,
                DescriptionProvider.AniDB =>
                    descriptions.TryGetValue(DescriptionProvider.AniDB, out var desc) ? SanitizeAnidbDescription(desc ?? string.Empty) : null,
                DescriptionProvider.TvDB =>
                    descriptions.TryGetValue(DescriptionProvider.TvDB, out var desc) ? desc : null,
                _ => null
            };
            if (!string.IsNullOrEmpty(overview))
                return overview;
        }
        return string.Empty;
    }

    /// <summary>
    /// Sanitize the AniDB entry description to something usable by Jellyfin.
    /// </summary>
    /// <remarks>
    /// Based on ShokoMetadata's summary sanitizer which in turn is based on HAMA's summary sanitizer.
    /// </remarks>
    /// <param name="summary">The raw AniDB description.</param>
    /// <returns>The sanitized AniDB description.</returns>
    public static string SanitizeAnidbDescription(string summary)
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

    public static (string?, string?) GetEpisodeTitles(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string metadataLanguage)
        => (
            GetEpisodeTitleByType(episodeInfo, seasonInfo, TitleProviderType.Main, metadataLanguage),
            GetEpisodeTitleByType(episodeInfo, seasonInfo, TitleProviderType.Alternate, metadataLanguage)
        );

    public static (string?, string?) GetSeasonTitles(SeasonInfo seasonInfo, string metadataLanguage)
        => GetSeasonTitles(seasonInfo, 0, metadataLanguage);

    public static (string?, string?) GetSeasonTitles(SeasonInfo seasonInfo, int baseSeasonOffset, string metadataLanguage)
    {
        var displayTitle = GetSeriesTitleByType(seasonInfo, seasonInfo.Shoko.Name, TitleProviderType.Main, metadataLanguage);
        var alternateTitle = GetSeriesTitleByType(seasonInfo, seasonInfo.Shoko.Name, TitleProviderType.Alternate, metadataLanguage);
        if (baseSeasonOffset > 0) {
            string type = string.Empty;
            switch (baseSeasonOffset) {
                default:
                    break;
                case 1:
                    type = "Alternate Version";
                    break;
            }
            if (!string.IsNullOrEmpty(type)) {
                displayTitle += $" ({type})";
                alternateTitle += $" ({type})";
            }
        }
        return (displayTitle, alternateTitle);
    }

    public static (string?, string?) GetShowTitles(ShowInfo showInfo, string metadataLanguage)
        => (
            GetSeriesTitleByType(showInfo.DefaultSeason, showInfo.Name, TitleProviderType.Main, metadataLanguage),
            GetSeriesTitleByType(showInfo.DefaultSeason, showInfo.Name, TitleProviderType.Alternate, metadataLanguage)
        );

    public static (string?, string?) GetMovieTitles(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string metadataLanguage)
        => (
            GetMovieTitleByType(episodeInfo, seasonInfo, TitleProviderType.Main, metadataLanguage),
            GetMovieTitleByType(episodeInfo, seasonInfo, TitleProviderType.Alternate, metadataLanguage)
        );

    /// <summary>
    /// Returns a list of the providers to check, and in what order
    /// </summary>
    private static TitleProvider[] GetOrderedTitleProvidersByType(TitleProviderType titleType)
        => titleType switch {
            TitleProviderType.Main =>
                Plugin.Instance.Configuration.TitleMainOverride
                    ? Plugin.Instance.Configuration.TitleMainOrder.Where((t) => Plugin.Instance.Configuration.TitleMainList.Contains(t)).ToArray()
                    : new[] { TitleProvider.Shoko_Default },
            TitleProviderType.Alternate =>
                Plugin.Instance.Configuration.TitleAlternateOverride
                    ? Plugin.Instance.Configuration.TitleAlternateOrder.Where((t) => Plugin.Instance.Configuration.TitleAlternateList.Contains(t)).ToArray()
                    : new[] { TitleProvider.AniDB_CountryOfOrigin, TitleProvider.TMDB_CountryOfOrigin },
            _ => Array.Empty<TitleProvider>(),
        };

    private static string? GetMovieTitleByType(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, TitleProviderType type, string metadataLanguage)
    {
        var mainTitle = GetSeriesTitleByType(seasonInfo, seasonInfo.Shoko.Name, type, metadataLanguage);
        var subTitle = GetEpisodeTitleByType(episodeInfo, seasonInfo, type, metadataLanguage);

        if (!(string.IsNullOrEmpty(subTitle) || IgnoredSubTitles.Contains(subTitle)))
            return $"{mainTitle}: {subTitle}".Trim();
        return mainTitle?.Trim();
    }

    private static string? GetEpisodeTitleByType(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, TitleProviderType type, string metadataLanguage)
    {
        foreach (var provider in GetOrderedTitleProvidersByType(type)) {
            var title = provider switch {
                TitleProvider.Shoko_Default =>
                    episodeInfo.Shoko.Name,
                TitleProvider.AniDB_Default =>
                    GetDefaultTitle(episodeInfo.AniDB.Titles),
                TitleProvider.AniDB_LibraryLanguage =>
                    GetTitlesForLanguage(episodeInfo.AniDB.Titles, false, metadataLanguage),
                TitleProvider.AniDB_CountryOfOrigin =>
                    GetTitlesForLanguage(episodeInfo.AniDB.Titles, false, GuessOriginLanguage(GetMainLanguage(seasonInfo.AniDB.Titles))),
                _ => null,
            };
            if (!string.IsNullOrEmpty(title))
                return title.Trim();
        }
        return null;
    }

    private static string? GetSeriesTitleByType(SeasonInfo seasonInfo, string defaultName, TitleProviderType type, string metadataLanguage)
    {
        foreach (var provider in GetOrderedTitleProvidersByType(type)) {
            var title = provider switch {
                TitleProvider.Shoko_Default =>
                    defaultName,
                TitleProvider.AniDB_Default =>
                    GetDefaultTitle(seasonInfo.AniDB.Titles),
                TitleProvider.AniDB_LibraryLanguage =>
                    GetTitlesForLanguage(seasonInfo.AniDB.Titles, true, metadataLanguage),
                TitleProvider.AniDB_CountryOfOrigin =>
                    GetTitlesForLanguage(seasonInfo.AniDB.Titles, true, GuessOriginLanguage(GetMainLanguage(seasonInfo.AniDB.Titles))),
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
    /// <returns>The default title.</returns>
    private static string? GetDefaultTitle(List<Title> titles)
        => titles.FirstOrDefault(t => t.IsDefault)?.Value;

    /// <summary>
    /// Get the first title available for the language, optionally using types
    /// to filter the list in addition to the metadata languages provided.
    /// </summary>
    /// <param name="titles">Title list to search.</param>
    /// <param name="usingTypes">Search using titles</param>
    /// <param name="metadataLanguages">The metadata languages to search for.</param>
    /// <returns>The first found title in any of the provided metadata languages, or null.</returns>
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
    /// <returns>The main title language code.</returns>
    private static string GetMainLanguage(IEnumerable<Title> titles)
        => titles.FirstOrDefault(t => t?.Type == TitleType.Main)?.LanguageCode ?? titles.FirstOrDefault()?.LanguageCode ?? "x-other";

    /// <summary>
    /// Guess the origin language based on the main title language.
    /// </summary>
    /// <param name="langCode">The main title language code.</param>
    /// <returns>The list of origin language codes to try and use.</returns>
    private static string[] GuessOriginLanguage(string langCode)
        => langCode switch {
            "x-other" => new string[] { "ja" },
            "x-jat" => new string[] { "ja" },
            "x-zht" => new string[] { "zn-hans", "zn-hant", "zn-c-mcm", "zn" },
            _ => new string[] { langCode },
        };
}
