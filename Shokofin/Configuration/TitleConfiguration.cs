using System.Collections.Generic;
using System.Linq;

using TitleProvider = Shokofin.Utils.TextUtility.TitleProvider;

namespace Shokofin.Configuration;

public class TitleConfiguration {
    /// <summary>
    /// Determines how we'll be selecting the title for entries.
    /// </summary>
    public TitleProvider[] List { get; set; } = [];

    /// <summary>
    /// The order of which we will be selecting the title for entries.
    /// </summary>
    public TitleProvider[] Order { get; set; } = [
        TitleProvider.Shoko_Default,
        TitleProvider.AniDB_Default,
        TitleProvider.AniDB_LibraryLanguage,
        TitleProvider.AniDB_CountryOfOrigin,
        TitleProvider.TMDB_Default,
        TitleProvider.TMDB_LibraryLanguage,
        TitleProvider.TMDB_CountryOfOrigin,
    ];

    /// <summary>
    /// Allow choosing any title in the selected language if no official
    /// title is available.
    /// </summary>
    public bool AllowAny { get; set; }

    /// <summary>
    /// Returns a list of the providers to check, and in what order.
    /// </summary>
    public IEnumerable<TitleProvider> GetOrderedTitleProviders()
        => Order.Where((t) => List.Contains(t));
}
