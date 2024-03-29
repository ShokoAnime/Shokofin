# Shokofin

A Jellyfin plugin to integrate [Jellyfin](https://jellyfin.org/docs/) with
[Shoko Server](https://shokoanime.com/downloads/shoko-server/).

## Read this before installing

**This plugin requires that you have already set up and are using Shoko
Server**, and that the directories/folders you intend to use in Jellyfin are
**fully indexed** (and optionally managed) by Shoko Server. **Otherwise, the
plugin won't be able to function properly**, meaning, the plugin won't be able
to any find metadata about any entries that are not indexed by Shoko Server
since there is no metadata to find.

### What Is Shoko?

Shoko is an anime cataloging program designed to automate the cataloging of your
collection regardless of the size and amount of files you have. Unlike other
anime cataloging programs which make you manually add your series or link the
files to them, Shoko removes the tedious, time-consuming and boring task of
having to manually add every file and manually input the file information. You
have better things to do with your time like actually watching the series in
your collection so let Shoko handle all the heavy lifting.

Learn more about Shoko at https://shokoanime.com/.

## Feature Overview

- [/] Metadata integration

  - [X] Basic metadata, e.g. titles, description, dates, etc.

    - [X] Customisable main title for items

    - [X] Optional customisable alternate/original title for items

    - [X] Customisable description source for items

      Choose between AniDB, TvDB, or a mix of the two.

    - [X] Support optionally adding titles and descriptions for all episodes for
      multi-entry files.

  - [X] Genres

  - [X] Tags

    With some settings to choose which tags to add.

  - [/] Voice Actors

    - [X] Displayed on the Show/Season/Movie items

    - [ ] Person provider for image and details

  - [/] General staff (e.g. producer, writer, etc.)

    - [X] Displayed on the Show/Season/Movie items

    - [ ] Person provider for image and details

  - [/] Studios

    - [X] Displayed on the Show/Season/Movie items

    - [ ] Studio provider for image and details

- [X] Library integration

  - [X] Support for different library types

    - [X] Show library

    - [X] Movie library

    - [X] Mixed show/movie library¹.

      ¹ _You need at least one movie in your library for this to currently work as expected. This is an issue with Jellyfin 10.8._

  - [/] Supports adding local trailers

    - [X] on Show items

    - [X] on Season items

    - [ ] on Movie items

  - [X] Specials and extra features. 

    - [X] Customise how Specials are placed in your library. I.e. if they are
      mapped to the normal seasons, or if they are strictly kept in season zero.

    - [X] Extra features. The plugin will map specials stored in Shoko such as
      interviews, etc. as extra features, and all other specials as episodes in
      season zero.

  - [X] Map OPs/EDs to Theme Videos, so they can be displayed as background video
    while you browse your library.

  - [X] Support merging multi-version episodes/movies into a single entry.

    Tidying up the UI if you have multiple versions of the same episode or
    movie.

      - [X] Auto merge after library scan (if enabled).

      - [X] Manual merge/split tasks

  - [X] Support optionally setting other provider IDs Shoko knows about (e.g.
    AniDB, TvDB, TMDB, etc.) on some item types when an ID is available for
    the items in Shoko.

  - [X] Multiple ways to organise your library.

    - [X] Choose between three ways to group your Shows/Seasons; no grouping,
      following TvDB (to-be replaced with TMDB soon™-ish), and using Shoko's
      groups feature.

      _For the best compatibility it is **strongly** advised **not** to use
      "season" folders with anime as it limits which grouping you can use, you
      can still create "seasons" in the UI using Shoko's groups or using the
      TvDB/TMDB compatibility mode._

    - [X] Optionally create Box-Sets for your Movies…

      - [X] using the Shoko series.

      - [X] using the Shoko groups.

    - [X] Supports separating your on-disc library into a two Show and Movie
      libraries.

      _Provided you apply the workaround to do it_.

  - [/] Automatically populates all missing episodes not in your collection, so
    you can see at a glance what you are missing out on.

    - [ ] Deleting a missing episode item marks the episode as hidden/ignored
      in Shoko.

  - [ ] Optionally react to events sent from Shoko.

    Coming soon™-ish

- [X] User data

  - [X] Able to sync the watch data to/from Shoko on a per-user basis in
    multiple ways. And Shoko can further sync the to/from other linked services.

    - [X] During import.

    - [X] Player events (play/pause/resume/stop events)

    - [X] After playback (stop event)

    - [X] Live scrobbling (every 1 minute during playback after the last
      play/resume event or when jumping)

  - [X] Import and export user data tasks

## Install

There are many ways to install the plugin, but the recommended way is to use
the official Jellyfin repository. Alternatively it can be installed from this
GitHub repository. Or you build it from source.

Below is a version compatibility matrix for which version of Shokofin is
compatible with what.

| Shokofin   | Jellyfin | Shoko Server  |
|------------|----------|---------------|
| `0.x.x`    | `10.7`   | `4.0.0-4.1.2` |
| `1.x.x`    | `10.7`   | `4.1.0-4.1.2` |
| `2.x.x`    | `10.8`   | `4.1.2`       |
| `3.x.x`    | `10.8`   | `4.2.0`       |
| `unstable` | `10.8`   | `4.2.2`       |
| `N/A`      | `10.9`   | `N/A`         |

### Official Repository

1. Go to Dashboard -> Plugins -> Repositories

2. Add new repository with the following details

   * Repository Name: `Shokofin Stable`

   * Repository URL:
   `https://raw.githubusercontent.com/ShokoAnime/Shokofin/master/manifest.json`

3. Go to the catalog in the plugins page

4. Find and install Shokofin from the Metadata section

5. Restart your server to apply the changes.

### Github Releases

1. Download the `shokofin_*.zip` file from the latest release from GitHub
  [here](https://github.com/ShokoAnime/shokofin/releases/latest).

2. Extract the contained `Shokofin.dll` and `meta.json`, place both the files in
a folder named `Shokofin` and copy this folder to the `plugins` folder under
the Jellyfin program data directory or inside the portable install directory.
Refer to the "Data Directory" section on
[this page](https://jellyfin.org/docs/general/administration/configuration.html)
for where to find your jellyfin install.

3. Start or restart your server to apply the changes

### Build Process

1. Clone or download this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build plugin with following command.

```sh
$ dotnet restore Shokofin/Shokofin.csproj
$ dotnet publish -c Release Shokofin/Shokofin.csproj
```

4. Copy the resulting file `bin/Shokofin.dll` to the `plugins` folder under the
Jellyfin program data directory or inside the portable install directory.
