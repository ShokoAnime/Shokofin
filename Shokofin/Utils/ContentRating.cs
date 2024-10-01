
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.Events.Interfaces;

using TagWeight = Shokofin.Utils.TagFilter.TagWeight;

namespace Shokofin.Utils;

public static class ContentRating
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
    public class TvContentIndicatorsAttribute : Attribute
    {
        public TvContentIndicator[] Values { get; init; }
        
        public TvContentIndicatorsAttribute(params TvContentIndicator[] values)
        {
            Values = values;
        }
    }

    /// <summary>
    /// Tv Ratings and Parental Controls
    /// </summary>
    /// <remarks>
    /// Based on https://web.archive.org/web/20210720014648/https://www.tvguidelines.org/resources/TheRatings.pdf
    /// </remarks>
    public enum TvRating {
        /// <summary>
        /// No rating.
        /// </summary>
        None = 0,

        /// <summary>
        /// Most parents would find this program suitable for all ages. Although
        /// this rating does not signify a program designed specifically for
        /// children, most parents may let younger children watch this program
        /// unattended. It contains little or no violence, no strong language
        /// and little or no sexual dialogue or situations.
        /// </summary>
        [Description("TV-G")]
        TvG,

        /// <summary>
        /// This program is designed to be appropriate for all children. Whether
        /// animated or live-action, the themes and elements in this program are
        /// specifically designed for a very young audience, including children
        /// from ages 2-6. This program is not expected to frighten younger
        /// children.
        /// </summary>
        [Description("TV-Y")]
        TvY,

        /// <summary>
        /// This program is designed for children age 7 and above. It may be
        /// more appropriate for children who have acquired the developmental
        /// skills needed to distinguish between make-believe and reality.
        /// Themes and elements in this program may include mild fantasy
        /// violence or comedic violence, or may frighten children under the
        /// age of 7. Therefore, parents may wish to consider the suitability of
        /// this program for their very young children.
        ///
        /// This program may contain one or more of the following:
        /// - intense or combative fantasy violence (FV).
        /// </summary>
        [Description("TV-Y7")]
        [TvContentIndicators(TvContentIndicator.FV)]
        TvY7,

        /// <summary>
        /// This program contains material that parents may find unsuitable for
        /// younger children. Many parents may want to watch it with their
        /// younger children.
        ///
        /// The theme itself may call for parental guidance and/or the program
        /// may contain one or more of the following:
        /// - some suggestive dialogue (D),
        /// - infrequent coarse language (L),
        /// - some sexual situations (S), or
        /// - moderate violence (V).
        /// </summary>
        [Description("TV-PG")]
        [TvContentIndicators(TvContentIndicator.D, TvContentIndicator.L, TvContentIndicator.S, TvContentIndicator.V)]
        TvPG,

        /// <summary>
        /// This program contains some material that many parents would find
        /// unsuitable for children under 14 years of age. Parents are strongly
        /// urged to exercise greater care in monitoring this program and are
        /// cautioned against letting children under the age of 14 watch
        /// unattended.
        ///
        /// This program may contain one or more of the following:
        /// - intensely suggestive dialogue (D),
        /// - strong coarse language (L),
        /// - intense sexual situations (S), or
        /// - intense violence (V).
        /// </summary>
        [Description("TV-14")]
        [TvContentIndicators(TvContentIndicator.D, TvContentIndicator.L, TvContentIndicator.S, TvContentIndicator.V)]
        Tv14,

        /// <summary>
        /// This program is specifically designed to be viewed by adults and
        /// therefore may be unsuitable for children under 17.
        ///
        /// This program may contain one or more of the following:
        /// - strong coarse language (L),
        /// - intense sexual situations (S), or
        /// - intense violence (V).
        /// </summary>
        [Description("TV-MA")]
        [TvContentIndicators(TvContentIndicator.D, TvContentIndicator.L, TvContentIndicator.S, TvContentIndicator.V)]
        TvMA,

        /// <summary>
        /// Porn. No, you didn't read that wrong.
        /// </summary>
        [Description("XXX")]
        [TvContentIndicators(TvContentIndicator.L, TvContentIndicator.S, TvContentIndicator.V)]
        XXX,
    }

    /// <summary>
    /// Available content indicators for the base <see cref="TvRating"/>.
    /// </summary>
    public enum TvContentIndicator {
        /// <summary>
        /// Intense or combative fantasy violence (FV), but only for <see cref="TvRating.TvPG"/>.
        /// </summary>
        FV = 1,
        /// <summary>
        /// Some or intense suggestive dialogue (D), depending on the base <see cref="TvRating"/>.
        /// </summary>
        D,
        /// <summary>
        /// infrequent or intense coarse language (L), depending on the base <see cref="TvRating"/>.
        /// </summary>
        L,
        /// <summary>
        /// Moderate or intense sexual situations (S), depending on the base <see cref="TvRating"/>.
        /// </summary>
        S,
        /// <summary>
        /// Moderate or intense violence, depending on the base <see cref="TvRating"/>.
        /// </summary>
        V,
    }

    private static ProviderName[] GetOrderedProviders()
        => Plugin.Instance.Configuration.ContentRatingOverride
            ? Plugin.Instance.Configuration.ContentRatingOrder.Where((t) => Plugin.Instance.Configuration.ContentRatingList.Contains(t)).ToArray()
            : [ProviderName.AniDB, ProviderName.TMDB];

#pragma warning disable IDE0060
    public static string? GetMovieContentRating(SeasonInfo seasonInfo, EpisodeInfo episodeInfo, string? metadataCountryCode)
#pragma warning restore IDE0060
    {
        // TODO: Add TMDB movie linked to episode content rating here.
        foreach (var provider in GetOrderedProviders()) {
            var title = provider switch {
                ProviderName.AniDB => seasonInfo.AssumedContentRating,
                // TODO: Add TMDB series content rating here.
                _ => null,
            };
            if (!string.IsNullOrEmpty(title))
                return title.Trim();
        }
        return null;
    }

#pragma warning disable IDE0060
    public static string? GetSeasonContentRating(SeasonInfo seasonInfo, string? metadataCountryCode)
#pragma warning restore IDE0060
    {
        foreach (var provider in GetOrderedProviders()) {
            var title = provider switch {
                ProviderName.AniDB => seasonInfo.AssumedContentRating,
                // TODO: Add TMDB series content rating here.
                _ => null,
            };
            if (!string.IsNullOrEmpty(title))
                return title.Trim();
        }
        return null;
    }

    public static string? GetShowContentRating(ShowInfo showInfo, string? metadataCountryCode)
    {
        var (contentRating, contentIndicators) = showInfo.SeasonOrderDictionary.Values
            .Select(seasonInfo => GetSeasonContentRating(seasonInfo, metadataCountryCode))
            .Where(contentRating => !string.IsNullOrEmpty(contentRating))
            .Distinct()
            .Select(text => TryConvertRatingFromText(text, out var cR, out var cI) ? (contentRating: cR, contentIndicators: cI ?? []) : (contentRating: TvRating.None, contentIndicators: []))
            .Where(tuple => tuple.contentRating is not TvRating.None)
            .GroupBy(tuple => tuple.contentRating)
            .OrderByDescending(groupBy => groupBy.Key)
            .Select(groupBy => (groupBy.Key, groupBy.SelectMany(tuple => tuple.contentIndicators).ToHashSet()))
            .FirstOrDefault();
        return ConvertRatingToText(contentRating, contentIndicators);
    }

    public static string? GetTagBasedContentRating(IReadOnlyDictionary<string, ResolvedTag> tags)
    {
        // User overridden content rating.
        if (tags.TryGetValue("/custom user tags/target audience", out var tag)) {
            var audience = tag.Children.Count == 1 ? tag.Children.Values.First() : null;
            if (TryConvertRatingFromText(audience?.Name.ToLowerInvariant().Replace("-", ""), out var cR, out var cI))
                return ConvertRatingToText(cR, cI);
        }

        // Base rating.
        var contentRating = TvRating.None;
        var contentIndicators = new HashSet<TvContentIndicator>();
        if (tags.TryGetValue("/target audience", out tag)) {
            var audience = tag.Children.Count == 1 ? tag.Children.Values.First() : null;
            contentRating = (audience?.Name.ToLowerInvariant()) switch {
                "mina" => TvRating.TvG,
                "kodomo" => TvRating.TvY,
                "shoujo" => TvRating.TvY7,
                "shounen" => TvRating.TvY7,
                "josei" => TvRating.Tv14,
                "seinen" => TvRating.Tv14,
                "18 restricted" => TvRating.XXX,
                _ => 0,
            };
        }

        // "Upgrade" the content rating if it contains any of these tags.
        if (contentRating is < TvRating.TvMA && tags.ContainsKey("/elements/ecchi/borderline porn"))
            contentRating = TvRating.TvMA;
        if (contentRating is < TvRating.Tv14 && (
            tags.ContainsKey("/elements/ecchi/Gainax bounce") ||
            tags.ContainsKey("/elements/ecchi/breast fondling") ||
            tags.ContainsKey("/elements/ecchi/paper clothes") ||
            tags.ContainsKey("/elements/ecchi/skimpy clothing")
        ))
            contentRating = TvRating.Tv14;
        if (contentRating is < TvRating.TvPG && (
            tags.ContainsKey("/elements/sexual humour") ||
            tags.ContainsKey("/technical aspects/very bloody wound in low-pg series")
        ))
            contentRating = TvRating.TvPG;
        if (tags.TryGetValue("/elements/ecchi", out tag)) {
            if (contentRating is < TvRating.Tv14 && tag.Weight is >= TagWeight.Four)
                contentRating = TvRating.Tv14;
            else if (contentRating is < TvRating.TvPG && tag.Weight is >= TagWeight.Three)
                contentRating = TvRating.TvPG;
            else if (contentRating is < TvRating.TvY7 && tag.Weight is >= TagWeight.Two)
                contentRating = TvRating.TvY7;
        }
        if (contentRating is < TvRating.Tv14 && tags.ContainsKey("/content indicators/sex"))
            contentRating = TvRating.Tv14;
        if (tags.TryGetValue("/content indicators/nudity", out tag)) {
            if (contentRating is < TvRating.Tv14 && tag.Weight is >= TagWeight.Four)
                contentRating = TvRating.Tv14;
            else if (contentRating is < TvRating.TvPG && tag.Weight is >= TagWeight.Three)
                contentRating = TvRating.TvPG;
            else if (contentRating is < TvRating.TvY7 && tag.Weight is >= TagWeight.Two)
                contentRating = TvRating.TvY7;
        }
        if (tags.TryGetValue("/content indicators/violence", out tag)) {
            if (contentRating is > TvRating.TvG && contentRating is < TvRating.Tv14 && tag.Weight is >= TagWeight.Four)
                contentRating = TvRating.Tv14;
            if (contentRating is > TvRating.TvG && contentRating is < TvRating.TvY7 && tag.Weight is >= TagWeight.Two)
                contentRating = TvRating.TvY7;
        }
        if (contentRating is > TvRating.TvG && contentRating is < TvRating.TvY7 && tags.ContainsKey("/content indicators/violence/gore"))
                contentRating = TvRating.TvY7;

        // Content indicators.
        if (tags.ContainsKey("/elements/sexual humour"))
            contentIndicators.Add(TvContentIndicator.D);
        if (tags.TryGetValue("/content indicators/sex", out tag)) {
            if (tag.Weight is <= TagWeight.Two)
                contentIndicators.Add(TvContentIndicator.D);
            else
                contentIndicators.Add(TvContentIndicator.S);
        }
        if (tags.TryGetValue("/content indicators/nudity", out tag)) {
            if (tag.Weight >= TagWeight.Four)
                contentIndicators.Add(TvContentIndicator.S);
        }
        if (tags.TryGetValue("/content indicators/violence", out tag)) {
            if (tags.ContainsKey("/elements/speculative fiction/fantasy"))
                contentIndicators.Add(TvContentIndicator.FV);
            if (tag.Weight is >= TagWeight.Two)
                contentIndicators.Add(TvContentIndicator.V);
        }

        return ConvertRatingToText(contentRating, contentIndicators);
    }

    private static bool TryConvertRatingFromText(string? value, out TvRating contentRating, [NotNullWhen(true)] out HashSet<TvContentIndicator>? contentIndicators)
    {
        // Return early if null or empty.
        contentRating = TvRating.None;
        if (string.IsNullOrEmpty(value)) {
            contentIndicators = null;
            return false;
        }

        // Trim input, remove dashes and underscores, and remove optional prefix.
        value = value.ToLowerInvariant().Trim().Replace("-", "").Replace("_", "");
        if (value.Length > 1 && value[0..1] == "tv")
            value = value.Length > 2 ? value[2..] : string.Empty;

        // Parse rating.
        var offset = 0;
        if (value.Length > 0) {
            contentRating = value[0] switch {
                'y' => TvRating.TvY,
                'g' => TvRating.TvG,
                _ => TvRating.None,
            };
            if (contentRating is not TvRating.None)
            offset = 1;
        }
        if (contentRating is TvRating.None && value.Length > 1) {
            contentRating = value[0..1] switch {
                "y7" => TvRating.TvY7,
                "pg" => TvRating.TvPG,
                "14" => TvRating.Tv14,
                "ma" => TvRating.TvMA,
                _ => TvRating.None,
            };
            if (contentRating is not TvRating.None)
            offset = 2;
        }
        if (contentRating is TvRating.None && value.Length > 2) {
            contentRating = value[0..2] switch {
                "xxx" => TvRating.XXX,
                _ => TvRating.None,
            };
            if (contentRating is not TvRating.None)
            offset = 3;
        }
        if (contentRating is TvRating.None) {
            contentIndicators = null;
            return false;
        }

        // Parse indicators.
        contentIndicators = [];
        if (value.Length <= offset)
            return true;
        foreach (var raw in value[offset..]) {
            if (!Enum.TryParse<TvContentIndicator>(raw.ToString(), out var indicator)) {
                contentRating = TvRating.None;
                contentIndicators = null;
                return false;
            }
            contentIndicators.Add(indicator);
        }

        return true;
    }

    internal static T[] GetCustomAttributes<T>(this System.Reflection.FieldInfo? fieldInfo, bool inherit = false)
        => fieldInfo?.GetCustomAttributes(typeof(T), inherit) is T[] attributes ? attributes : [];

    private static string? ConvertRatingToText(TvRating value, IEnumerable<TvContentIndicator>? contentIndicators)
    {
        var field = value.GetType().GetField(value.ToString())!;
        var attributes = field.GetCustomAttributes<DescriptionAttribute>();
        if (attributes.Length is 0)
            return null;

        var contentRating = attributes.First().Description;
        var allowedIndicators = (field.GetCustomAttributes<TvContentIndicatorsAttribute>().FirstOrDefault()?.Values ?? [])
            .Intersect(contentIndicators ?? [])
            .ToList();
        if (allowedIndicators.Count is > 0)
            contentRating += $"-{allowedIndicators.Select(cI => cI.ToString()).Join("")}";

        return contentRating;
    }
}