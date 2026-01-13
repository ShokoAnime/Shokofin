using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using Shokofin.API.Models;
using Shokofin.Events.Interfaces;

namespace Shokofin.API.Info;

public interface IExtendedItemInfo : IBaseItemInfo {
    bool IsAvailable { get; }

    IReadOnlyList<string> Tags { get; }

    IReadOnlyList<string> Genres { get; }

    IReadOnlyList<string> Studios { get; }

    IReadOnlyDictionary<ProviderName, IReadOnlyList<string>> ProductionLocations { get; }

    IReadOnlyList<ContentRating> ContentRatings { get; }

    IReadOnlyList<PersonInfo> Staff { get; }
}
