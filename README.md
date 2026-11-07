# Retune for Windows

A fast, private, keyboard-friendly IPTV player for Windows 10 and 11. Bring your Xtream Codes account or M3U
playlist and browse Live TV, Movies and Series in a native Fluent interface, with smooth hardware-accelerated
playback by [mpv](https://mpv.io). Free and open source (GPL-3.0-or-later). No accounts, ads or tracking.

![Browsing movies](docs/screenshots/browse-movies.png)

> Retune is only a player. It comes with no channels, playlists or media: you bring your own legal source.
> Every screenshot here shows synthetic demo data from the repository's own fake provider.

## Features

- **Xtream Codes and M3U** playlists (URL or local file), several playlists side by side, with the provider's
  account status and expiry in the sidebar.
- **Live TV, Movies and Series** with categories, a category filter, accent-insensitive search, sorting, and
  three layouts (grid, list, compact) at three tile sizes. Smooth with 80,000+ items.
- **Detail pages** for movies and series: artwork, plot, cast, seasons and episodes.
- **Favorites, Continue Watching and Recently Watched**, resume points, watched marks and "play next episode".
- **A TV-remote player**: zap with ↑/↓ or PgUp/PgDn (debounced, so the provider's single connection is freed
  at once), type a channel number, flip to the last channel with Backspace, channel list with filter, now/next
  programme guide, automatic reconnect, audio and subtitle tracks, subtitle size and position, playback speed,
  stats for nerds, screenshots to `Pictures\Retune`, full screen.
- **Windows integration**: media keys and the Windows media flyout, single instance, sleep prevention while
  playing, Mica, dark Fluent design, screen-reader names on every control.
- **Parental PIN**: lock categories; locked content never leaks into All, search, favorites or history.
  Wrong PINs pause entry (30 s, then 1 min, then 5 min).
- **Keyboard first**: every action has a shortcut. Press **F1**, **Ctrl+/** or **?** for the full list.

| | |
| --- | --- |
| ![Live TV with the channel list](docs/screenshots/player-live.png) | ![Movie playback](docs/screenshots/player-movie.png) |
| ![A series page](docs/screenshots/series-detail.png) | ![Live TV](docs/screenshots/live-tv.png) |
| ![Keyboard shortcuts in the player](docs/screenshots/player-shortcuts.png) | ![Settings](docs/screenshots/settings.png) |

## Install

Requirements: Windows 10 version 2004 (build 19041) or later, or Windows 11, on a 64-bit PC. Nothing else: the
packages include the .NET runtime and libmpv.

### Installer (recommended)

1. Download `Retune-<version>-setup.exe` and, optionally, `SHA256SUMS.txt`.
2. Optional, to check the download: `Get-FileHash .\Retune-<version>-setup.exe -Algorithm SHA256` and compare it
   with the line in `SHA256SUMS.txt`.
3. Run the installer. It installs for your Windows user only, into `%LOCALAPPDATA%\Programs\Retune`, so it never
   asks for administrator rights.
4. Builds aren't code-signed yet, so Windows SmartScreen may say *"Windows protected your PC"*. Choose
   **More info**, then **Run anyway**.
5. Start Retune from the Start menu and choose **Add Playlist**.

Silent install (for scripts): `Retune-<version>-setup.exe /VERYSILENT /SUPPRESSMSGBOXES`.

### Portable zip

Unzip `Retune-<version>-win-x64.zip` anywhere and run `Retune.exe`. Your playlists and settings are still kept in
your user profile (see [Your data](#your-data)), not next to the exe.

### Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and, for the tests, FFmpeg.

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Gyan.FFmpeg                 # only for generating the test videos

./tools/fetch-libmpv.ps1                   # the pinned libmpv build, checked against its SHA-256
./tools/make-test-media.ps1                # synthetic test videos (git-ignored)

dotnet build Retune.slnx                   # warnings are errors
dotnet test --solution Retune.slnx
dotnet run --project src/Retune.App
```

To build the release packages (self-contained publish, portable zip, installer and checksums in
`artifacts/dist`):

```powershell
winget install JRSoftware.InnoSetup --scope user   # once; per-user, no admin rights
./tools/package.ps1
```

## Using Retune

| Where | Keys |
| --- | --- |
| Anywhere | **Ctrl+1/2/3** Live TV / Movies / Series · **Ctrl+F** search · **Ctrl+N** add playlist · **Ctrl+R** or **F5** refresh · **Ctrl+,** settings · **Ctrl+L** lock categories again · **F11** full screen · **F1** shortcuts |
| Browsing | **Tab** next column · **Enter** open or play · **Alt+←**, **Backspace** or the mouse's Back button go back · **Ctrl+Shift+1/2/3** grid / list / compact |
| Player | **Space** play/pause · **←/→** 10 s (**Shift** 1 min) · **↑/↓** previous/next · **0–9** channel number · **Backspace** last channel · **L** channel list · **M** mute · **−/+** volume · **C** subtitles · **[ ] \\** speed · **I** stats · **S** screenshot · **F** full screen · **Esc** close |

Settings (the gear at the bottom of the sidebar) cover the live stream format (Automatic, MPEG-TS or HLS),
autoplay of the next episode, the parental PIN, hidden categories, clearing watch history and clearing the
artwork cache.

## Your data

| What | Where | Protection |
| --- | --- | --- |
| Playlists, favorites, history, settings, catalog cache | `%APPDATA%\Retune` | Folder readable only by you (and SYSTEM). Playlists and catalogs are encrypted with Windows DPAPI for your user. |
| Xtream passwords | Windows Credential Manager (`Retune/…`) | Encrypted by Windows; never written to Retune's files. |
| Poster and logo cache, logs | `%LOCALAPPDATA%\Retune` | Disposable. Logs never contain URLs, user names or passwords. |

Retune connects only to your provider and to the artwork hosts your playlist names. It sends no telemetry.
mpv runs with scripts, youtube-dl and unsafe playlists disabled, every network response is size-capped, and
stream URLs are checked against an allowlist of schemes.

**Uninstalling** from Settings › Apps (or the Start menu) asks whether to also delete your data. Choose **Yes** to
remove the folders above and the passwords Retune saved; choose **No** to keep them for a later reinstall. With the
portable zip, delete the folder; `Retune.exe --purge-data` removes your data and saved passwords.

## How it's built

C# on .NET 10 with WPF and [WPF-UI](https://github.com/lepoco/wpfui) (Fluent design, Mica), CommunityToolkit.Mvvm,
and libmpv. The decisions behind it are in [docs/PLAN.md](docs/PLAN.md); behaviour is specified in
[docs/spec/core.md](docs/spec/core.md) and [docs/spec/ui-player.md](docs/spec/ui-player.md).

```
src/Retune.Core            pure domain logic: models, Xtream and M3U, search, library, PIN, playback rules
src/Retune.Infrastructure  storage (atomic, DPAPI), Credential Manager, HTTP, artwork cache, logging
src/Retune.Mpv             libmpv interop: MpvClient (event loop, typed properties) and MpvEngine
src/Retune.Presentation    view models, independent of WPF
src/Retune.App             WPF views, video and overlay hosts, Windows integration
tests/                     xUnit v3 on Microsoft Testing Platform
tools/                     libmpv fetch, test media, fake provider, packaging, screenshots
installer/                 Inno Setup script (per-user)
```

**Video without "airspace" problems.** mpv renders into a native child window with its own D3D11 swap chain on its
own thread, so a busy UI thread never makes the video stutter. The player controls are WPF content in a
per-pixel-transparent child window stacked above it, which moves, resizes and goes full screen with the main
window.

## Quality

- **495 automated tests**: domain logic and edge cases, storage crash-safety and corruption, Credential Manager,
  HTTP limits and timeouts against a local server, view models against an in-process fake provider, and real mpv
  playback of generated media. CI runs them on every push.
- **Zero analyzer warnings**: `latest-recommended` analysis with warnings as errors.
- **Measured at scale** (Release build, 16,000 channels + 65,000 movies + 7,000 series from the fake provider):
  download, decode and index in about 0.9 s; switching sections about 0.3 s; list scrolling at a median 7.8 ms per
  wheel step; managed memory about 180 MB with everything loaded.
- **Accessibility**: every button has a screen-reader name, including the player controls drawn over the video.

## Developer options

These only help development and testing; regular users never need them.

| Option | What it does |
| --- | --- |
| `--demo <provider url>` | Adds a demo playlist for the fake provider (`tools/FakeProvider`) |
| `--autoplay` | With `--demo`, plays the first channel |
| `--mute` | No audio device at all, so nothing can be heard |
| `--no-activate` | Opens without taking keyboard focus |
| `--window x,y,w,h` | Places the window at these physical-pixel bounds |
| `--render <folder>` | With `--demo`, renders the main screens to PNG without showing a window |
| `--tour <folder>` | With `--demo`, walks the real window through the main screens and saves screenshots |
| `--perf <report>` | With `--demo`, measures load, navigation, search and scrolling and writes a report |
| `--purge-data` | Deletes your Retune data and saved passwords (used by the uninstaller) |

`RETUNE_DATA_DIR=<folder>` keeps all data, caches, logs and saved passwords (under a separate `Retune-Dev/`
prefix) apart from your real ones. `tools/make-screenshots.ps1` regenerates the screenshots in this README.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
