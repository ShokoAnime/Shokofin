# Shokofin

Repository for the [Shoko](https://github.com/ShokoAnime/ShokoServer)+[Jellyfin](https://github.com/jellyfin/jellyfin) integration project.

## Install

### Build Process

1. Clone or download this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build plugin with following command.

```sh
$ dotnet restore Shokofin/Shokofin.csproj -s https://api.nuget.org/v3/index.json -s https://pkgs.dev.azure.com/jellyfin-project/jellyfin/_packaging/unstable/nuget/v3/index.json
$ dotnet publish -c Release Shokofin/Shokofin.csproj
```

4. Copy the resulting file `bin/Shokofin.dll` to the `plugins` folder under the Jellyfin program data directory or inside the portable install directory
