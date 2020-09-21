# Shokofin

Repo for the Jellyfin Plugin

## Build Process

1. Clone or download this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build plugin with following command.

```sh
dotnet publish --configuration Release --output bin
```
4. Copy the resulting file `bin/Shokofin.dll` to the `plugins` folder under the Jellyfin program data directory or inside the portable install directory
