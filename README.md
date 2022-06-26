# Shokofin

A Jellyfin plugin to integrate [Jellyfin](https://jellyfin.org/docs/) with [Shoko Server](https://shokoanime.com/downloads/shoko-server/).

## Read this before installing

The plugin requires a version of Jellyfin greater or equal to **10.7.0** (`>=10.7.0`) and an unstable version of Shoko Server greater or equal to **4.1.1** (`>=4.1.1`) to be installed. It also requires that you have already set up and are using Shoko Server, and that the directories/folders you intend to use in Jellyfin are fully indexed (and optionally managed) by Shoko Server, otherwise the plugin won't be able to funciton properly — it won't be able to find metadata about any entries that are not indexed by Shoko Server since the metadata we want is not available.

## Breaking Changes

### 1.5.0

If you're upgrading from an older version to version 1.5.0, then be sure to update the "Host" field in the plugin settings before you continue using the plugin. **Update: Starting with 1.7.0 you just need to reset the connection then log in again.**

## Install

There are many ways to install the plugin, but the recomended way is to use the official Jellyfin repository.

### Official Repository

1. Go to Dashboard -> Plugins -> Repositories
2. Add new repository with the following details
   * Repository Name: `Shokofin Stable`
   * Repository URL: `https://raw.githubusercontent.com/ShokoAnime/Shokofin/master/manifest.json`
3. Go to the catalog in the plugins page
4. Find and install Shokofin from the Metadata section
5. Restart your server to apply the changes.

### Github Releases

1. Download the `shokofin_*.zip` file from the latest release from GitHub [here](https://github.com/ShokoAnime/shokofin/releases/latest).

2. Extract the contained `Shokofin.dll` and `meta.json`, place both the files in a folder named `Shokofin` and copy this folder to the `plugins` folder under the Jellyfin program data directory or inside the portable install directory. Refer to the "Data Directory" section on [this page](https://jellyfin.org/docs/general/administration/configuration.html) for where to find your jellyfin install.

3. Start or restart your server to apply the changes

### Build Process

1. Clone or download this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build plugin with following command.

```sh
$ dotnet restore Shokofin/Shokofin.csproj
$ dotnet publish -c Release Shokofin/Shokofin.csproj
```

4. Copy the resulting file `bin/Shokofin.dll` to the `plugins` folder under the Jellyfin program data directory or inside the portable install directory.
