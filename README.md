# Shokofin

**Warning**: This plugin currently requires the latest version of Jellyfin (`>10.7.0`) and daily version of Shoko (`>4.1.0`) to be installed to work.

A plugin to integrate your Shoko database with the Jellyfin media server.

## Install

There are multiple ways to install this plugin, but the recomended way is to use the official Jellyfin repository.

### Official Repository

1. Go to Dashboard -> Plugins -> Repositories
2. Add new repository with the following details
   * Repository Name: `Shokofin`
   * Repository URL: `https://raw.githubusercontent.com/ShokoAnime/Shokofin/master/manifest.json`
3. Go to the catalog in the plugins page
4. Find and install Shokofin from the Metadata section

### Github Releases

1. Download the `shokofin_*.zip` file from the latest release from GitHub [here](https://github.com/ShokoAnime/shokofin/releases/latest).

2. Extract the contained `Shokofin.dll` and `meta.json`, place both the files in a folder named `Shokofin` and copy this folder to the `plugins` folder under the Jellyfin program data directory or inside the portable install directory.

### Build Process

1. Clone or download this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build plugin with following command.

```sh
$ dotnet restore Shokofin/Shokofin.csproj
$ dotnet publish -c Release Shokofin/Shokofin.csproj
```

4. Copy the resulting file `bin/Shokofin.dll` to the `plugins` folder under the Jellyfin program data directory or inside the portable install directory.
