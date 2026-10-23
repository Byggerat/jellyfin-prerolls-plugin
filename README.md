# jellyfin-prerolls-plugin

A Jellyfin 12.x plugin that plays one random preroll video before every movie. The prerolls come from a folder you choose, and the same preroll never plays twice in a row. TV episodes are not affected.

## Build

Requires the .NET 10 SDK.

```sh
dotnet build Jellyfin.Plugin.Prerolls/Jellyfin.Plugin.Prerolls.csproj -c Release
```

The output is `Jellyfin.Plugin.Prerolls/bin/Release/net10.0/Jellyfin.Plugin.Prerolls.dll`.

## Install

1. Copy `Jellyfin.Plugin.Prerolls.dll` to `<jellyfin-config>/plugins/Prerolls_1.0.0.0/`.
2. Restart Jellyfin.
3. Go to **Dashboard → Plugins → Prerolls** and set the preroll folder.
4. Each user must have **Cinema mode** turned on under **Settings → Playback**. It is on by default.

## Notes

- The folder must be readable by the Jellyfin server. With Docker, mount it into the container and enter the path as seen inside the container.
- Supported file types: `.mp4 .mkv .m4v .mov .avi .webm .ts`.
- The plugin folder must be owned by the user Jellyfin runs as (e.g. `chown -R 1000:1002`), otherwise the Dashboard → Plugins page can break.
- To find the container path to enter, see [Finding the path for the preroll folder setting](documentation/development-notes.md#finding-the-path-for-the-preroll-folder-setting).
- See [documentation/development-notes.md](documentation/development-notes.md) for internals and troubleshooting.
- The folder does not need to be a library. The plugin adds each preroll to Jellyfin's database the first time it plays, because Jellyfin only plays intros that are in its database.
