# Shokofin

A plugin to integrate your Shoko database with the Jellyfin media server.

## Install

There are multiple ways to install this plugin, but the recomended way is to use the official Jellyfin repository.

### Official Repository

TBD

### Github Releases

1. Download the `shokofin_*.zip` file from the latest release from GitHub [here](https://github.com/Shoko/Shokofin/releases/latest).

2. Extract the contained `Shokofin.dll` and copy it to the `plugins` folder under the Jellyfin program data directory or inside the portable install directory.

### Build Process

1. Clone or download this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build plugin with following command.

```sh
$ dotnet restore Shokofin/Shokofin.csproj -s https://api.nuget.org/v3/index.json -s https://pkgs.dev.azure.com/jellyfin-project/jellyfin/_packaging/unstable/nuget/v3/index.json
$ dotnet publish -c Release Shokofin/Shokofin.csproj
```

4. Copy the resulting file `bin/Shokofin.dll` to the `plugins` folder under the Jellyfin program data directory or inside the portable install directory.
