# Development Notes

What was learned while building and installing the plugin. These notes are checked against the Jellyfin 12.3 source.

## How prerolls work

Jellyfin has a built-in extension point for content that plays before an item: `MediaBrowser.Controller.Library.IIntroProvider`.

```csharp
public interface IIntroProvider
{
    string Name { get; }
    Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user);
}
```

- **Registration:** the server finds implementations by itself (`ApplicationHost` calls `GetExports<IIntroProvider>()`) and fills their constructor arguments, such as `ILibraryManager`. You don't register anything with `IPluginServiceRegistrator`.
- **Clients:** they ask the server for intros and play them before the movie. This only happens when the user has **Cinema mode** turned on under **Settings → Playback**. It's on by default.
- **Plugin entry point:** `PrerollIntroProvider.GetIntros` returns nothing for anything that isn't a `Movie`. It also returns nothing when the folder isn't set, doesn't exist, or holds no videos, so a movie is never blocked from playing.

### Intros must be in the database

`IntroInfo` has two fields, `Path` and `ItemId`. You'd expect returning just a file `Path` to work. It doesn't. In `LibraryManager.ResolveIntro` (in `Emby.Server.Implementations/Library/LibraryManager.cs`), Jellyfin resolves the path and then looks up the matching database item. If there is none, it **silently returns null** and the intro is skipped.

So `PrerollIntroProvider.GetOrCreateVideo` works like this:

1. Resolves the file with `ILibraryManager.ResolvePath` (item IDs are derived from the path, so they're stable).
2. Returns the existing database item if there is one.
3. Otherwise registers it with `ILibraryManager.CreateItem(video, null)` and runs `RefreshMetadata` once, so ffprobe fills in the duration and streams.
4. Returns `IntroInfo { ItemId = video.Id }`.

Because of this, the preroll folder does **not** need to be a library. The first play of a new preroll is a little slower because of the probe.

### Avoiding repeats

The last chosen path is kept in a static field, protected by a lock. With more than one file, the provider keeps picking at random until it gets a different path. This applies across the whole server, not per user.

## Targeting Jellyfin 12

- Jellyfin 12 runs on **.NET 10**. Plugins built for 10.11 (.NET 9) won't load.
- The project references `Jellyfin.Controller` / `Jellyfin.Model` **12.0.0** with `ExcludeAssets=runtime`, so it works on every 12.x server. Raise this only if you need an API added in a later version.
- `build.yaml` sets `targetAbi: 12.0.0.0`. The server loads the plugin only if its own version is at least this.
- The official `jellyfin-plugin-template` still targets 10.11/.NET 9 (as of 2026-10), so you can't copy its version numbers as-is.

## Building

Requires the .NET 10 SDK.

```sh
dotnet build Jellyfin.Plugin.Prerolls/Jellyfin.Plugin.Prerolls.csproj -c Release
# -> Jellyfin.Plugin.Prerolls/bin/Release/net10.0/Jellyfin.Plugin.Prerolls.dll
```

If you don't have the SDK installed, either of these works:

- **Docker:** `docker run --rm -v $PWD:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build Jellyfin.Plugin.Prerolls/Jellyfin.Plugin.Prerolls.csproj -c Release`. Add `sudo` if your user isn't in the `docker` group. Running as root leaves `bin/` and `obj/` owned by root.
- **Temporary SDK:** `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir <dir>`, then run `<dir>/dotnet build ...`.

Only the `.dll` needs to be deployed.

## Installing on Docker (linuxserver/jellyfin)

The paths below are as seen inside the container.

| What | Path |
|---|---|
| Plugins | `/config/data/plugins/<Name>/` (the linuxserver image puts the data folder at `/config/data`) |
| Plugin settings | `/config/data/plugins/configurations/Jellyfin.Plugin.Prerolls.xml` |
| Preroll folder setting | The path as seen **inside the container**, e.g. `/media/intros` when the host media folder is mounted at `/media` |

Steps:

1. Copy the DLL to `<host-config>/data/plugins/Prerolls/`.
2. Give the folder to the container's user, i.e. the image's `PUID:PGID`: `chown -R 1000:1002 <host-config>/data/plugins/Prerolls`.
3. Restart the container.
4. Set the folder in **Dashboard → Plugins → Prerolls**.

### Finding the path for the preroll folder setting

Jellyfin only sees paths **inside the container**, so the host path (e.g. `/srv/.../media/intros`) doesn't work. To work out the right one:

1. **Read the `volumes:` mapping** in your compose file. Each entry is `host-path:container-path`. Take the host folder your prerolls are in, then replace the host part on the left with the container part on the right.
   ```yaml
   volumes:
     - /srv/disk/media:/media      # host /srv/disk/media/intros  ->  /media/intros
   ```
2. **Check that the container can see it:**
   ```sh
   docker exec jellyfin ls -l /media/intros
   ```
   If this lists your videos, the path is correct. It also shows who owns the files, which helps with permissions.
3. **Or browse from Jellyfin itself:** **Dashboard → Libraries → Add Media Library → Folders (+)**. The folder picker shows the container's file system. Find the folder there, note its path, then cancel without creating the library.

Example from the author's setup: the setting that worked was `/movie/plugins`.

Also keep in mind:

- The container user must be able to **read** the preroll folder and its files.
- Keep the preroll folder **outside every library path**. If a library points at the parent folder (e.g. `/media`), Jellyfin scans the prerolls and lists them as movies.

### meta.json

If the plugin folder has no `meta.json`, `PluginManager.LoadManifest` creates a temporary manifest. It names the plugin after the folder (the text before a `_version` suffix), sets the ID to the MD5 of that name, and sets the version from the suffix, or the server version if there's none. Once the DLL loads, Jellyfin replaces the ID, name and version with the real ones and **writes `meta.json` into the plugin folder**. That's why the folder must be writable; see below.

## Troubleshooting

### Dashboard → Plugins crashes with `TypeError: f.forEach is not a function`

**Cause:** the plugin folder was owned by root (copied with `sudo`), so Jellyfin, running as the container user, couldn't write `meta.json`.

**Fix:** run `chown -R <PUID>:<PGID>` on the plugin folder (and `chmod` if needed), then restart.

How it was tracked down, in case it happens again:

- The plugins page (`jellyfin-web` `src/apps/dashboard/features/plugins/api/usePluginDetails.ts`) calls `.forEach` on the `/Plugins` and `/Packages` responses. This error means one of them returned 200 with something other than a JSON array. A failed request would show a normal error message instead.
- Removing the plugin folder made the page work again. The logs showed `Loaded plugin: Prerolls 1.0.0.0` with no errors.
- A local Jellyfin 12.3 server (built from source, see below) with only this plugin returned valid `/Plugins`, `/Packages` and `/web/ConfigurationPages` responses, even after saving the plugin settings. That ruled out the plugin code and pointed at the environment.
- To check a server directly, bypassing any reverse proxy:
  ```sh
  curl -s http://localhost:8096/Plugins -H "Authorization: MediaBrowser Token=<api-key>"
  docker logs --since 2m jellyfin 2>&1 | grep -E -A15 '\[ERR\]'
  ```

### Movie plays without a preroll

Check, in this order:

1. Cinema mode is on for the user.
2. The folder setting uses the container path.
3. The container user can read the files.
4. The file extension is supported: `.mp4 .mkv .m4v .mov .avi .webm .ts`.

Then look for `Preroll folder ...` warnings or `Could not resolve preroll` in the server log.

## Testing against a local Jellyfin server

This reproduces server behaviour without touching the real server:

```sh
# Source: https://codeload.github.com/jellyfin/jellyfin/tar.gz/refs/tags/v12.3
dotnet publish jellyfin-12.3/Jellyfin.Server/Jellyfin.Server.csproj -c Release -o jfbin
mkdir -p run/data/plugins/Prerolls run/config
cp Jellyfin.Plugin.Prerolls/bin/Release/net10.0/Jellyfin.Plugin.Prerolls.dll run/data/plugins/Prerolls/
# Optional: put <InternalHttpPort>18096</InternalHttpPort> in run/config/network.xml to avoid clashing with 8096
dotnet jfbin/jellyfin.dll --datadir run/data --configdir run/config --cachedir run/cache --logdir run/log --nowebclient
```

Complete the setup wizard through the API:

1. `POST /Startup/Configuration`, `POST /Startup/User` (`{"Name":"admin","Password":"pw"}`), then `POST /Startup/Complete`.
2. Authenticate:
   ```sh
   curl -X POST /Users/AuthenticateByName \
     -H 'Authorization: MediaBrowser Client="t", Device="t", DeviceId="t1", Version="1"' \
     -H 'Content-Type: application/json' -d '{"Username":"admin","Pw":"pw"}'
   ```
   The `Client`/`Device`/`Version` fields are required; without them the request fails with `request.App` null.
3. Pass the `AccessToken` from the response as `Authorization: MediaBrowser Token=<token>` on later requests.
