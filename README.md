# Shokofin

A Jellyfin plugin to integrate [Jellyfin](https://jellyfin.org/docs/) with
[Shoko Server](https://shokoanime.com/downloads/shoko-server/).

## Read this before installing

**This plugin requires that you have already set up and are using Shoko Server**,
and that the files you intend to include in Jellyfin are **indexed** (and
optionally managed) by Shoko Server. **Otherwise, the plugin won't be able to
provide metadata for your files**, since there is no metadata to find for them.

### What Is Shoko?

Shoko is an anime cataloging program designed to automate the cataloging of your
collection regardless of the size and amount of files you have. Unlike other
anime cataloging programs which make you manually add your series or link the
files to them, Shoko removes the tedious, time-consuming and boring task of
having to manually add every file and manually input the file information. You
have better things to do with your time like actually watching the series in
your collection so let Shoko handle all the heavy lifting.

Learn more about Shoko at https://shokoanime.com/.

## Install

There are many ways to install the plugin, but the recommended way is to use
the official Jellyfin repository. Alternatively, it can be installed from this
GitHub repository, or you can build it from source.

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

1. **Access Plugin Repositories:**
   - Go to `Dashboard` -> `Plugins` -> `Repositories`.

2. **Add New Repository:**
   - Add a new repository with the following details:
     * **Repository Name:** `Shokofin Stable`
     * **Repository URL:** `https://raw.githubusercontent.com/ShokoAnime/Shokofin/metadata/stable/manifest.json`

3. **Install Shokofin:**
   - Go to the catalog in the plugins page.
   - Find and install `Shoko` from the `Metadata` section.

4. **Restart Jellyfin:**
   - Restart your server to apply the changes.

### Github Releases

1. **Download the Plugin:**
   - Go to the latest release on GitHub [here](https://github.com/ShokoAnime/shokofin/releases/latest).
   - Download the `shoko_*.zip` file.

2. **Extract and Place Files:**
   - Extract all `.dll` files and `meta.json` from the zip file.
   - Put them in a folder named `Shoko`.
   - Copy this `Shoko` folder to the `plugins` folder in your Jellyfin program
     data directory or inside the Jellyfin install directory. For help finding
     your Jellyfin install location, check the "Data Directory" section on
     [this page](https://jellyfin.org/docs/general/administration/configuration.html).

3. **Restart Jellyfin:**
   - Start or restart your Jellyfin server to apply the changes.

### Build Process

1. **Clone or Download the Repository:**
   - Clone or download the repository from GitHub.

2. **Set Up .NET Core SDK:**
   - Make sure you have the .NET Core SDK installed on your computer.

3. **Build the Plugin:**
   - Open a terminal and navigate to the repository directory.
   - Run the following commands to restore and publish the project:

     ```sh
     $ dotnet restore Shokofin/Shokofin.csproj
     $ dotnet publish -c Release Shokofin/Shokofin.csproj
     ```
4. **Copy Built Files:**
   - After building, go to the `bin/Release/dotnet8.0/` directory.
   - Copy all `.dll` files to a folder named `Shoko`.
   - Place this `Shoko` folder in the `plugins` directory of your Jellyfin
     program data directory or inside the portable install directory. For help
     finding your Jellyfin install location, check the "Data Directory" section
     on [this page](https://jellyfin.org/docs/general/administration/configuration.html).

## Feature Overview

- [/] Metadata integration

  - [X] Basic metadata, e.g. titles, description, dates, etc.

    - [X] Customizable main title for items

    - [X] Optional customizable alternate/original title for items

    - [X] Customizable description source for items

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

    - [X] Mixed show/movie library.

  - [X] Supports adding local trailers

    - [X] on Show items

    - [X] on Season items

    - [X] on Movie items

  - [X] Specials and extra features. 

    - [X] Customize how Specials are placed in your library. I.e. if they are
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

  - [X] Multiple ways to organize your library.

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