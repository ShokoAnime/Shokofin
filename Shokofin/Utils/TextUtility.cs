using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Shokofin.API;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.Configuration;
using Shokofin.Extensions;

namespace Shokofin.Utils;

public static partial class TextUtility {
    private static readonly HashSet<char> PunctuationMarks = [
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
    ];

    internal static readonly HashSet<string> IgnoredSubTitles = new(StringComparer.InvariantCultureIgnoreCase) {
        "Complete Movie",
        "Music Video",
        "OAD",
        "OVA",
        "Short Movie",
        "Special",
        "TV Special",
        "Web",
    };

    private static readonly Regex SynopsisCleanLinks = new(@"(https?:\/\/\w+.\w+(?:\/?\w+)?) \[([^\]]+)\]", RegexOptions.Compiled);

    private static readonly Regex SynopsisCleanMiscLines = new(@"^(\*|--|~)\s*", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex SynopsisRemoveSummary1 = new(@"\b(Note|Summary):\s*", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SynopsisRemoveSummary2 = new(@"\bSource: [^ ]+", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SynopsisConvertNewLines = new(@"\r\n|\r", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SynopsisCleanMultiEmptyLines = new(@"\n{2,}", RegexOptions.Singleline | RegexOptions.Compiled);

    [GeneratedRegex(@"^(?:Special|Episode|Volume|OVA|OAD|Web) \d+$|^Part \d+ of \d+$|^Episode [COPRST]\d+$|^(?:OVA|OAD|Movie|Complete Movie|Short Movie|TV Special|Music Video|Web|Volume)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex InvalidEpisodeTitleRegex();

    // Currently copied from these two locations until I have more time to research if there are even more patterns across languages to support;
    // https://github.com/jellyfin/jellyfin/blob/4b6fb6c4bb2478badad068ce18aabe0c2955db48/Emby.Naming/TV/SeasonPathParser.cs#L13
    // https://github.com/jellyfin/jellyfin/blob/4b6fb6c4bb2478badad068ce18aabe0c2955db48/Emby.Naming/TV/SeasonPathParser.cs#L16
    [GeneratedRegex(@"^\s*((?:(?>\d+))(?:st|nd|rd|th|\.)*(?!\s*[Ee]\d+))\s*(?:[[시즌]*|[シーズン]*|[sS](?:eason|æson|esong|aison|taffel|eries|tagione|äsong|eizoen|easong|ezon|ezona|ezóna|ezonul)*|[tT](?:emporada)*|[kK](?:ausi)*|[Сс](?:езон)*)\s*(?<rightpart>.*)$|^\s*(?:[[시즌]*|[シーズン]*|[sS](?:eason|æson|esong|aison|taffel|eries|tagione|äsong|eizoen|easong|ezon|ezona|ezóna|ezonul)*|[tT](?:emporada)*|[kK](?:ausi)*|[Сс](?:езон)*)\s*(?:(?>\d+)(?!\s*[Ee]\d+))(?<rightpart>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex InvalidSeriesOrSeasonTitleRegex();

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
        /// Deprecated, but kept until the next major release for backwards compatibility.
        /// </summary>
        /// TODO: Break this during the next major version of the plugin.
        TvDB = 3,

        /// <summary>
        /// Provide the description from TMDB.
        /// </summary>
        TMDB = 4
    }

    /// <summary>
    /// Determines how to convert the description.
    /// </summary>
    public enum DescriptionConversionMode {
        /// <summary>
        /// Don't convert the description.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// Convert the description to plain text.
        /// </summary>
        PlainText = 1,

        /// <summary>
        /// Convert the description to markdown.
        /// </summary>
        Markdown = 2,
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

    public static string? JoinText(IEnumerable<string?> textList) {
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

    #region Description

    #region Description | Episode

    public static string GetEpisodeDescription(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string? metadataLanguage)
        => seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Description.AnidbEpisode.Enabled ? (
                GetDescription(episodeInfo, Plugin.Instance.Configuration.Description.AnidbEpisode, metadataLanguage)
            ) : (
                GetDescription(episodeInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Description.TmdbEpisode.Enabled ? (
                GetDescription(episodeInfo, Plugin.Instance.Configuration.Description.TmdbEpisode, metadataLanguage)
            ) : (
                GetDescription(episodeInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
            _ => Plugin.Instance.Configuration.Description.ShokoEpisode.Enabled ? (
                GetDescription(episodeInfo, Plugin.Instance.Configuration.Description.ShokoEpisode, metadataLanguage)
            ) : (
                GetDescription(episodeInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
        };

    public static string GetEpisodeDescription(IEnumerable<EpisodeInfo> episodeList, SeasonInfo seasonInfo, string? metadataLanguage)
        => JoinText(episodeList.Select(baseInfo => GetEpisodeDescription(baseInfo, seasonInfo, metadataLanguage))) ?? string.Empty;

    #endregion

    #region Description | Season

    public static string GetSeasonDescription(SeasonInfo seasonInfo, string? metadataLanguage)
        => seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Description.AnidbSeason.Enabled ? (
                GetDescription(seasonInfo, Plugin.Instance.Configuration.Description.AnidbSeason, metadataLanguage)
            ) : (
                GetDescription(seasonInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Description.TmdbSeason.Enabled ? (
                GetDescription(seasonInfo, Plugin.Instance.Configuration.Description.TmdbSeason, metadataLanguage)
            ) : (
                GetDescription(seasonInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
            _ => Plugin.Instance.Configuration.Description.ShokoSeason.Enabled ? (
                GetDescription(seasonInfo, Plugin.Instance.Configuration.Description.ShokoSeason, metadataLanguage)
            ) : (
                GetDescription(seasonInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
        };

    #endregion

    #region Description | Show

    public static string GetShowDescription(ShowInfo showInfo, string? metadataLanguage)
        => showInfo.DefaultSeason.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Description.AnidbAnime.Enabled ? (
                GetDescription(showInfo, Plugin.Instance.Configuration.Description.AnidbAnime, metadataLanguage)
            ) : (
                GetDescription(showInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Description.TmdbShow.Enabled ? (
                GetDescription(showInfo, Plugin.Instance.Configuration.Description.TmdbShow, metadataLanguage)
            ) : (
                GetDescription(showInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
            _ => Plugin.Instance.Configuration.Description.ShokoSeries.Enabled ? (
                GetDescription(showInfo, Plugin.Instance.Configuration.Description.ShokoSeries, metadataLanguage)
            ) : (
                GetDescription(showInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
        };

    #endregion

    #region Description | Movie

    public static string GetMovieDescription(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string? metadataLanguage) {
        // TMDB movies have a proper "episode" description.
        if (episodeInfo.Id[0] is IdPrefix.TmdbMovie)
            return GetEpisodeDescription(episodeInfo, seasonInfo, metadataLanguage);

        return seasonInfo.IsMultiEntry && !episodeInfo.IsMainEntry
            ? GetEpisodeDescription(episodeInfo, seasonInfo, metadataLanguage)
            : GetSeasonDescription(seasonInfo, metadataLanguage);
    }

    #endregion

    public static string GetCollectionDescription(SeasonInfo seasonInfo, string? metadataLanguage)
        => seasonInfo.StructureType switch {
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Description.TmdbCollection.Enabled ? (
                GetDescription(seasonInfo, Plugin.Instance.Configuration.Description.TmdbCollection, metadataLanguage)
            ) : (
                GetDescription(seasonInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
            _ => Plugin.Instance.Configuration.Description.ShokoCollection.Enabled ? (
                GetDescription(seasonInfo, Plugin.Instance.Configuration.Description.ShokoCollection, metadataLanguage)
            ) : (
                GetDescription(seasonInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
            ),
        };

    public static string GetCollectionDescription(CollectionInfo collectionInfo, string? metadataLanguage)
        => Plugin.Instance.Configuration.Description.ShokoCollection.Enabled ? (
            GetDescription(collectionInfo, Plugin.Instance.Configuration.Description.ShokoCollection, metadataLanguage)
        ) : (
            GetDescription(collectionInfo, Plugin.Instance.Configuration.Description.Default, metadataLanguage)
        );

    private static string GetDescription(IBaseItemInfo baseInfo, DescriptionConfiguration config, string? metadataLanguage) {
        foreach (var provider in config.GetOrderedDescriptionProviders()) {
            var overview = provider switch {
                DescriptionProvider.Shoko =>
                    baseInfo.Overview,
                DescriptionProvider.AniDB =>
                    baseInfo.Overviews.Where(o => o.Source is "AniDB" && string.Equals(o.LanguageCode, metadataLanguage, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault()?.Value,
                DescriptionProvider.TMDB =>
                    baseInfo.Overviews.Where(o => o.Source is "TMDB" && string.Equals(o.LanguageCode, metadataLanguage, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault()?.Value,
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
    public static string SanitizeAnidbDescription(string summary) {
        if (string.IsNullOrWhiteSpace(summary))
            return string.Empty;

        var config = Plugin.Instance.Configuration;
        if (config.SynopsisCleanLinks)
            summary = summary.Replace(SynopsisCleanLinks, match => config.SynopsisEnableMarkdown ? $"[{match.Groups[2].Value}]({match.Groups[1].Value})" : match.Groups[2].Value);

        if (config.SynopsisCleanMiscLines)
            summary = summary.Replace(SynopsisCleanMiscLines, string.Empty);

        if (config.SynopsisRemoveSummary)
            summary = summary
                .Replace(SynopsisRemoveSummary1, match => config.SynopsisEnableMarkdown ? $"**{match.Groups[1].Value}**: " : "")
                .Replace(SynopsisRemoveSummary2, string.Empty);

        if (config.SynopsisCleanMultiEmptyLines)
            summary = summary
                .Replace(SynopsisConvertNewLines, "\n")
                .Replace(SynopsisCleanMultiEmptyLines, "\n");

        return summary.Trim();
    }

    #endregion

    #region Titles

    #region Titles | Episode

    public static (string? displayTitle, string? alternateTitle) GetEpisodeTitles(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string? metadataLanguage) {
        var config = seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Title.AnidbEpisode.Enabled
                ? Plugin.Instance.Configuration.Title.AnidbEpisode
                : Plugin.Instance.Configuration.Title.Default,
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Title.TmdbEpisode.Enabled
                ? Plugin.Instance.Configuration.Title.TmdbEpisode
                : Plugin.Instance.Configuration.Title.Default,
            _ => Plugin.Instance.Configuration.Title.ShokoEpisode.Enabled
                ? Plugin.Instance.Configuration.Title.ShokoEpisode
                : Plugin.Instance.Configuration.Title.Default,
        };
        var displayTitle = GetEpisodeTitleByType(episodeInfo, seasonInfo, config.MainTitle, metadataLanguage);
        var alternateTitle = JoinTitles(config.AlternateTitles.Select(t => GetEpisodeTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)), displayTitle, !config.RemoveDuplicates);

        return (displayTitle, alternateTitle);
    }

    private static string? GetEpisodeTitleByType(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, TitleConfiguration configuration, string? metadataLanguage) {
        foreach (var provider in configuration.GetOrderedTitleProviders()) {
            var title = provider switch {
                TitleProvider.Shoko_Default =>
                    episodeInfo.Title,
                TitleProvider.AniDB_Default =>
                    episodeInfo.Titles.FirstOrDefault(title => title.Source is "AniDB" && title.LanguageCode is "en")?.Value,
                TitleProvider.AniDB_LibraryLanguage =>
                    GetTitleForLanguage(episodeInfo.Titles.Where(t => t.Source is "AniDB").ToList(), false, configuration.AllowAny, metadataLanguage),
                TitleProvider.AniDB_CountryOfOrigin =>
                    GetTitleForLanguage(episodeInfo.Titles.Where(t => t.Source is "AniDB").ToList(), false, configuration.AllowAny, GuessOriginLanguage(seasonInfo)),
                TitleProvider.TMDB_Default =>
                    episodeInfo.Titles.FirstOrDefault(title => title.Source is "TMDB" && title.LanguageCode is "en")?.Value,
                TitleProvider.TMDB_LibraryLanguage =>
                    GetTitleForLanguage(episodeInfo.Titles.Where(t => t.Source is "TMDB").ToList(), false, configuration.AllowAny, metadataLanguage),
                TitleProvider.TMDB_CountryOfOrigin =>
                    GetTitleForLanguage(episodeInfo.Titles.Where(t => t.Source is "TMDB").ToList(), false, configuration.AllowAny, episodeInfo.OriginalLanguageCode),
                _ => null,
            };
            if (!string.IsNullOrEmpty(title) && !InvalidEpisodeTitleRegex().IsMatch(title))
                return title.Trim();
        }

        return null;
    }

    #endregion

    #region Titles | Season

    public static (string? displayTitle, string? alternateTitle) GetSeasonTitles(SeasonInfo seasonInfo, int baseSeasonOffset, string? metadataLanguage) {
        var config = seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Title.AnidbSeason.Enabled
                ? Plugin.Instance.Configuration.Title.AnidbSeason
                : Plugin.Instance.Configuration.Title.Default,
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Title.TmdbSeason.Enabled
                ? Plugin.Instance.Configuration.Title.TmdbSeason
                : Plugin.Instance.Configuration.Title.Default,
            _ => Plugin.Instance.Configuration.Title.ShokoSeason.Enabled
                ? Plugin.Instance.Configuration.Title.ShokoSeason
                : Plugin.Instance.Configuration.Title.Default,
        };
        var displayTitle = GetSeriesTitleByType(seasonInfo, config.MainTitle, metadataLanguage);
        var alternateTitle = JoinTitles(config.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)), displayTitle, !config.RemoveDuplicates);
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
                if (!string.IsNullOrEmpty(displayTitle))
                    displayTitle += $" ({type})";
                if (!string.IsNullOrEmpty(alternateTitle))
                    alternateTitle += $" ({type})";
            }
        }

        return (displayTitle, alternateTitle);
    }

    #endregion

    #region Titles | Show

    public static (string? displayTitle, string? alternateTitle) GetShowTitles(ShowInfo showInfo, string? metadataLanguage) {
        var config = showInfo.DefaultSeason.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Title.AnidbAnime.Enabled
                ? Plugin.Instance.Configuration.Title.AnidbAnime
                : Plugin.Instance.Configuration.Title.Default,
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Title.TmdbShow.Enabled
                ? Plugin.Instance.Configuration.Title.TmdbShow
                : Plugin.Instance.Configuration.Title.Default,
            _ => Plugin.Instance.Configuration.Title.ShokoSeries.Enabled
                ? Plugin.Instance.Configuration.Title.ShokoSeries
                : Plugin.Instance.Configuration.Title.Default,
        };
        var displayTitle = GetSeriesTitleByType(showInfo, config.MainTitle, metadataLanguage);
        var alternateTitle = JoinTitles(config.AlternateTitles.Select(t => GetSeriesTitleByType(showInfo, t, metadataLanguage)), displayTitle, !config.RemoveDuplicates);

        return (displayTitle, alternateTitle);
    }

    private static string? GetSeriesTitleByType(IBaseItemInfo baseInfo, TitleConfiguration configuration, string? metadataLanguage) {
        foreach (var provider in configuration.GetOrderedTitleProviders()) {
            var title = provider switch {
                TitleProvider.Shoko_Default =>
                    baseInfo.Title,
                TitleProvider.AniDB_Default =>
                    baseInfo.Titles.Where(t => t.Source is "AniDB").FirstOrDefault(title => title.IsDefault)?.Value,
                TitleProvider.AniDB_LibraryLanguage =>
                    GetTitleForLanguage(baseInfo.Titles.Where(t => t.Source is "AniDB").ToList(), true, configuration.AllowAny, metadataLanguage),
                TitleProvider.AniDB_CountryOfOrigin =>
                    GetTitleForLanguage(baseInfo.Titles.Where(t => t.Source is "AniDB").ToList(), true, configuration.AllowAny, GuessOriginLanguage(baseInfo)),
                TitleProvider.TMDB_Default =>
                    baseInfo.Titles.Where(t => t.Source is "TMDB").FirstOrDefault(title => title.IsDefault)?.Value,
                TitleProvider.TMDB_LibraryLanguage =>
                    GetTitleForLanguage(baseInfo.Titles.Where(t => t.Source is "TMDB").ToList(), true, configuration.AllowAny, metadataLanguage),
                TitleProvider.TMDB_CountryOfOrigin =>
                    GetTitleForLanguage(baseInfo.Titles.Where(t => t.Source is "TMDB").ToList(), true, configuration.AllowAny, baseInfo.OriginalLanguageCode),
                _ => null,
            };
            if (!string.IsNullOrEmpty(title) && !InvalidSeriesOrSeasonTitleRegex().IsMatch(title))
                return title.Trim();
        }
        return null;
    }

    #endregion

    #region Titles | Movie

    public static (string? displayTitle, string? alternateTitle) GetMovieTitles(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string? metadataLanguage) {
        var config = seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.Title.AnidbSeason.Enabled
                ? Plugin.Instance.Configuration.Title.AnidbSeason
                : Plugin.Instance.Configuration.Title.Default,
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Title.TmdbSeason.Enabled
                ? Plugin.Instance.Configuration.Title.TmdbSeason
                : Plugin.Instance.Configuration.Title.Default,
            _ => Plugin.Instance.Configuration.Title.ShokoSeason.Enabled
                ? Plugin.Instance.Configuration.Title.ShokoSeason
                : Plugin.Instance.Configuration.Title.Default,
        };
        var displayTitle = GetMovieTitleByType(episodeInfo, seasonInfo, config.MainTitle, metadataLanguage);
        var alternateTitle = JoinTitles(config.AlternateTitles.Select(t => GetMovieTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)), displayTitle, !config.RemoveDuplicates);

        return (displayTitle, alternateTitle);
    }

    private static string? GetMovieTitleByType(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, TitleConfiguration configuration, string? metadataLanguage) {
        if (episodeInfo.Id[0] is IdPrefix.TmdbMovie)
            return GetEpisodeTitleByType(episodeInfo, seasonInfo, configuration, metadataLanguage);

        var mainTitle = GetSeriesTitleByType(seasonInfo, configuration, metadataLanguage);
        var subTitle = GetEpisodeTitleByType(episodeInfo, seasonInfo, configuration, metadataLanguage);

        if (!string.IsNullOrEmpty(subTitle))
            return $"{mainTitle}: {subTitle}".Trim();
        else if (episodeInfo.EpisodeNumber > 1)
            return $"{mainTitle} {NumericToRoman(episodeInfo.EpisodeNumber)}".Trim();
        return mainTitle?.Trim();
    }

    #endregion

    #region Titles | Collection

    public static (string? displayTitle, string? alternateTitle) GetCollectionTitles(SeasonInfo seasonInfo, string? metadataLanguage) {
        var config = seasonInfo.StructureType switch {
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.Title.TmdbCollection.Enabled
                ? Plugin.Instance.Configuration.Title.TmdbCollection
                : Plugin.Instance.Configuration.Title.Default,
            _ => Plugin.Instance.Configuration.Title.ShokoCollection.Enabled
                ? Plugin.Instance.Configuration.Title.ShokoCollection
                : Plugin.Instance.Configuration.Title.Default,
        };
        var displayTitle = GetSeriesTitleByType(seasonInfo, config.MainTitle, metadataLanguage);
        var alternateTitle = JoinTitles(config.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)), displayTitle, !config.RemoveDuplicates);

        return (displayTitle, alternateTitle);
    }

    public static (string? displayTitle, string? alternateTitle) GetCollectionTitles(CollectionInfo collectionInfo, string? metadataLanguage) {
        var config = Plugin.Instance.Configuration.Title.ShokoCollection.Enabled
            ? Plugin.Instance.Configuration.Title.ShokoCollection
            : Plugin.Instance.Configuration.Title.Default;
        var displayTitle = GetSeriesTitleByType(collectionInfo, config.MainTitle, metadataLanguage);
        var alternateTitle = JoinTitles(config.AlternateTitles.Select(t => GetSeriesTitleByType(collectionInfo, t, metadataLanguage)), displayTitle, !config.RemoveDuplicates);

        return (displayTitle, alternateTitle);
    }

    #endregion

    #region Titles | Helpers

    /// <summary>
    /// Get the first title available for the language, optionally using types
    /// to filter the list in addition to the metadata languages provided.
    /// </summary>
    /// <param name="titles">Title list to search.</param>
    /// <param name="usingTypes">Search using titles</param>
    /// <param name="allowAny">Allow any title to be returned.</param>
    /// <param name="metadataLanguages">The metadata languages to search for.</param>
    /// <returns>The first found title in any of the provided metadata languages, or null.</returns>
    public static string? GetTitleForLanguage(IReadOnlyList<Title> titles, bool usingTypes, bool allowAny, params string?[] metadataLanguages) {
        foreach (var lang in metadataLanguages) {
            if (string.IsNullOrEmpty(lang))
                continue;

            var titleList = titles.Where(t => t.LanguageCode == lang).ToList();
            if (titleList.Count == 0)
                continue;

            string? title = null;
            if (usingTypes) {
                title = titleList.FirstOrDefault(t => t.Type == TitleType.Official)?.Value;
                if (string.IsNullOrEmpty(title) && allowAny)
                    title = titleList.FirstOrDefault()?.Value;
            }
            else {
                title = titles.FirstOrDefault()?.Value;
            }
            if (!string.IsNullOrWhiteSpace(title) && !InvalidEpisodeTitleRegex().IsMatch(title))
                return title;
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
    internal static string[] GuessOriginLanguage(IBaseItemInfo baseItemInfo) {
        var langCode = GetMainLanguage(baseItemInfo.Titles.Where(t => t.Source is "AniDB"));
        return langCode switch {
            "x-other" => ["ja", "jap"],
            "x-jat" => ["ja", "jap"],
            "x-zht" => ["zn-hans", "zn-hant", "zn-c-mcm", "zn", "zht"],
            _ => [langCode],
        };
    }

    private static string NumericToRoman(int number) =>
        number switch {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            6 => "VI",
            7 => "VII",
            8 => "VIII",
            9 => "IX",
            10 => "X",
            11 => "XI",
            12 => "XII",
            13 => "XIII",
            14 => "XIV",
            15 => "XV",
            16 => "XVI",
            17 => "XVII",
            18 => "XVIII",
            19 => "XIX",
            20 => "XX",
            21 => "XXI",
            22 => "XXII",
            23 => "XXIII",
            24 => "XXIV",
            _ => number.ToString(),
        };

    private static string? JoinTitles(IEnumerable<string?> titleList, string? mainTitle, bool distinct) {
        if (distinct) {
            return titleList
                .Where(title => !string.IsNullOrWhiteSpace(title) && (string.IsNullOrEmpty(mainTitle) || !string.Equals(title, mainTitle, StringComparison.Ordinal)))
                .Select(title => title!.Trim())
                .Distinct(StringComparer.Ordinal)
                .Join(" | ") is { Length: > 0 } result ? result : null;
        }
        else {
            return titleList
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Select(title => title!.Trim())
                .Join(" | ") is { Length: > 0 } result ? result : null;
        }
    }

    #endregion

    #endregion
}
