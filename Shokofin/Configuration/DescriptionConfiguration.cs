using System.Collections.Generic;
using System.Linq;

using DescriptionProvider = Shokofin.Utils.TextUtility.DescriptionProvider;

namespace Shokofin.Configuration;

public class DescriptionConfiguration {
    /// <summary>
    /// Determines if we should add the note(s) which show up on the AniDB anime
    /// page to the description for the series and/or season entities.
    /// </summary>
    public bool AddNotes { get; set; } = true;

    /// <summary>
    /// The collection of providers for descriptions. Replaces the former `DescriptionSource`.
    /// </summary>
    public DescriptionProvider[] List { get; set; } = [];

    /// <summary>
    /// The prioritization order of source providers for description sources.
    /// </summary>
    public DescriptionProvider[] Order { get; set; } = [
        DescriptionProvider.Shoko,
        DescriptionProvider.AniDB,
        DescriptionProvider.TMDB,
    ];

    /// <summary>
    /// Returns a list of the providers to check, and in what order.
    /// </summary>
    public IEnumerable<DescriptionProvider> GetOrderedDescriptionProviders()
        => Order.Where((t) => List.Contains(t));
}

public class ToggleDescriptionConfiguration : DescriptionConfiguration {
    /// <summary>
    /// Whether or not the description configuration is enabled.
    /// </summary>
    public bool Enabled { get; set; }
}
