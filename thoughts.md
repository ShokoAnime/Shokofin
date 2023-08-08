Collection → Shoko Group

An object holding information about the shoko group that may contain sub-groups
and/or shoko series.

**Properties**

- `Group` (ShokoGroup) — The Shoko Group entry linked to the Collection item.

- `Collections` (Collection[]) — Any sub-collections within the collection.

- `Shows` (Show[]) — The Show entries within the collection.

↓

Show → Shoko Group, Shoko Series

An object holding information about the direct parent shoko group and the
main shoko series for the group if the parent group does not contain sub-groups,
or just the shoko series if the parent group also contains other sub-groups.

**Properties**

- `IsStandalone` (boolean) — Indicates the Shoko Series linked is in a Shoko Group with
  sub-groups in it, and thus should not contain references to other series.

- `Group` (ShokoGroup?) — The Shoko Group, if available.

- `MainSeries` (ShokoSeries) — The main (and/or only) Shoko Series entry. Used for metadata.

- `AllSeries` (ShokoSeries[]) — All the Shoko Series entries linked to the Show item.

- `Seasons` (Season[]) — The Season entries within the show.

↓

Season → Shoko Series

An object holding information about the shoko series and the episodes both
available and not available in the user's library. Can contain multiple shoko
series references.

**Properties**

- `SeasonNumber` (int) — The season number.

- `Name` (string) — The season name.

- `EpisodeTypes` (EpisodeType[]) — The Shoko Episode Types this season is for. Only if a Shoko Series is split into multiple different
  seasons, each for a different Episode Type. E.g. One for "Normal" and "Special", and one for "Other", etc..

- `IsMixed` (boolean) — Indicates the Season contains Episode entries from multiple Shoko Series entries.

- `MainSeries` (ShokoSeries) — The main (and/or only) Shoko Series entry. Used for metadata.

- `AllSeries` (ShokoSeries[]) — All Shoko Series entries linked to the Season entry.

- `Episodes` (Episode[]) — All Episode entries within the Season.

- `Extras` (Episode[]) — Extras that doesn't' count as any episodes. We're re-using the episode model for now.

↓

Episode → Shoko Episode

An object holding information about the shoko episode and the alternatve
versions available from the user's library.

**Properties**

- `Name` — The episode name.

- `Number` — The computed episode number to use.

- `AbsoluteNumber` — The absolute episode number to use, if available.

- `EpisodeType` (EpisodeType) — The Shoko Episode Type for the Episode entry.

- `ExtraType` (ExtraType) — The extra type assigned to the Episode entry.

- `MainEpisode` (ShokoEpisode) — The main Shoko Episode entry to use for most of the metadata.

- `AllEpisodes` (ShokoEpisode[]) — All the Shoko Episode entries linked to the Episode entry.

- `AlternateVersions` (AlternateVersion) — All alternate episode versions that exist.

↓

Alternate Versions → Shoko Episode, Shoko File

An object holding information about which shoko files are linked to the same
episodes.

**Properties**

- `AllEpisodes` (ShokoEpisode[]) — All the Shoko Episode entries linked to the Episode entry.

- `AllFiles` (ShokoFile[]) — All Shoko File entries linked to the Episode entry.

- `PartialVersions` (PartialVersion[]) — All Partial Versions linked to this alternate version.

↓

Partial Versions → Shoko Episode, Shoko File

An object holding information about which shoko files are part of a multi-file
episode.

Holds the references to all the file links that form up one alternate version of
the episode.

**Properties**

- `MainEpisode` (ShokoEpisode) — The main Shoko Episode entry to use for most of the metadata.

- `AllEpisodes` (ShokoEpisode[]) — All the Shoko Episode entries linked to the Episode entry.

- `MainFile` (ShokoFile) — The primary Shoko File to use for file info.

- `AllFiles` (ShokoFile[]) — All Shoko File entries linked to the Episode entry.

- `Files` (File[]) — All File entries linked to the Episode entry.

↓

File → Shoko File

A reference to the shoko file.

**Properties**

- `File` (ShokoFile) — The primary Shoko File to use for file info.

- `Locations` (FileLocation[]) — All File Locations linked to the file, including
  the physical file location and any symbolic ones needed to compliment and fill
  out the library.

↓

File Location → Shoko File

An object holding information about either the original file or a symbolic-link
(managed by the plugin) pointing to the original file. Will be used to link the
same file to different episodes within the same series and across different
series.

When encountering a shoko file with multiple cross-reference for different
episode types within the same series, or cross-series references, then each
episode type within each series referenced will get their own link. The original
link is assigned to the normal episode for the series it is place in physically
in the library. The other links will be created on-demand and placed in a
directory managed by the plugin (outside the actual library), and added to their
respective series.

The links will be removed from the managed directory when either the library is
destroyed or when the links are otherwise no longer needed.

**Properties**

- `Path` (string) — The full path of the file location.

- `IsSymbolic` (boolean) — Indicates the file location is a symbolic link leading
  to the physical file, and not the physical file itself.

- `File` (ShokoFile) — The primary Shoko File to use for file info.

- `Series` (ShokoSeries) — The ShokoSeries entry for this file location.