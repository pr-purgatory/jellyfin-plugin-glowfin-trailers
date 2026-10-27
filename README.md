# Glowfin Trailers

A Jellyfin plugin for the Glowfin Apple TV app. When a movie or show has no
trailer saved on the server, Glowfin's **Play Trailer** uses the title's online trailer instead: this plugin
fetches it onto the server once and Glowfin plays it in its own player, never in another app.

Trailers saved next to your media (`Movie (2010)-trailer.mp4`, a `trailers` folder) are always used first; Glowfin
plays those without this plugin.

> **YouTube's terms.** The online trailer links Jellyfin's metadata provides (TMDB) are YouTube videos. This plugin
> downloads them with [yt-dlp](https://github.com/yt-dlp/yt-dlp), which goes against YouTube's terms of service.
> Install it only if you accept that for your own server.

## Install
1. In Jellyfin, open **Dashboard › Plugins › Repositories** (Catalog › ⚙ on some versions) and add:
   - Name: `Glowfin Trailers`
   - URL: `https://raw.githubusercontent.com/pr-purgatory/jellyfin-plugin-glowfin-trailers/main/manifest.json`
2. In **Catalog**, install **Glowfin Trailers**, then restart Jellyfin.

Updates appear in the catalog like any other plugin's. Requires Jellyfin 12.3 or later.

## Settings
Dashboard › Plugins › Glowfin Trailers:

| Setting | Default | |
|---|---|---|
| yt-dlp path | empty | Empty: the plugin downloads the official standalone yt-dlp for your server's OS and CPU into its data folder on first use and updates it (`yt-dlp -U`) weekly, since YouTube changes break old versions. Set a path to use your own. |
| Highest quality | 1080 | Tallest video to fetch, in lines. |
| Trailer cache size | 5000 MB | When full, the trailers played longest ago are deleted. |
| Extra yt-dlp arguments | empty | One per line, added to every run, e.g. `--cookies` and a path, or a `--js-runtimes` option if yt-dlp asks for one. |

## How it works
- The trailer: the item's YouTube links from `RemoteTrailers`, ones named "Official … Trailer" first, teasers, clips,
  promos and featurettes last, otherwise the metadata's order.
- The first request fetches it with yt-dlp as an H.264 + AAC MP4 (what Apple TV plays without transcoding),
  merged with Jellyfin's own ffmpeg with the index at the front. It's cached in `<cache>/glowfin-trailers/`, keyed
  by YouTube id and shared by every title and user. Two requests for the same trailer share one download.
- Nothing is written to your media folders or library.

### Endpoints
All need a signed-in user (the usual `Authorization` header, or an `ApiKey` query parameter).

| Endpoint | |
|---|---|
| `GET /GlowfinTrailers/Info` | `{"Version": "1.0.0.0"}`; clients use it to see the plugin is installed. |
| `GET /GlowfinTrailers/Items/{itemId}` | Fetches the item's trailer if it isn't cached (waits, up to 5 minutes), then returns `{"Name", "Path": "GlowfinTrailers/Videos/<videoId>"}`. 404 when the item has no YouTube trailer link or the user can't see it; 502 with yt-dlp's error when fetching fails. |
| `GET /GlowfinTrailers/Videos/{videoId}` | The cached MP4, with range requests. |

## Building
```bash
scripts/package.sh 1.0.0.0          # with the .NET 10 SDK
scripts/build-in-docker.sh 1.0.0.0  # without it (Docker)
```
Output: `artifacts/glowfin-trailers_<version>.zip`. To install a build by hand, unzip it into the server's plugins
folder as `GlowfinTrailers_<version>/` (Docker images: `/config/plugins/`) and restart Jellyfin.

The plugin builds against the `Jellyfin.Controller` version in the csproj, which is also the manifest's `targetAbi`
(the oldest server offered the release). Raise it when a new Jellyfin needs a rebuild.

## Releasing
Push an annotated tag; its message becomes the changelog:
```bash
git tag -a v1.0.1.0 -m "What changed, for the plugin catalog"
git push origin v1.0.1.0
```
`.github/workflows/release.yml` builds the zip, publishes a GitHub release with it, and adds the version (with its
MD5 checksum) to `manifest.json` on `main`, which servers read. Versions have four parts, as Jellyfin expects.

## License
GPL-3.0, like Jellyfin, whose libraries the plugin builds on.
