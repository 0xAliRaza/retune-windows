# Retune for Windows

A small, private, keyboard-driven IPTV player for Windows 11. Free and open source (GPL-3.0-or-later), with no accounts, ads or tracking.

> **Status: early development.** Phase 0 (foundation) is done. See [docs/PLAN.md](docs/PLAN.md) for the roadmap. The feature specs are [docs/spec/core.md](docs/spec/core.md) and [docs/spec/ui-player.md](docs/spec/ui-player.md).

Retune is only a player. It comes with no channels, playlists or media. You bring your own legal source.

## Build it

You need Windows 10 or 11 (x64), the [.NET 10 SDK](https://dotnet.microsoft.com/download) and, for the tests, FFmpeg.

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Gyan.FFmpeg          # only needed to generate test media

./tools/fetch-libmpv.ps1            # downloads the pinned libmpv build and checks its SHA-256
./tools/make-test-media.ps1         # generates synthetic test videos (git-ignored)

dotnet build Retune.slnx
dotnet test --solution Retune.slnx
```

Run it:

```powershell
dotnet run --project src/Retune.App -- --play "C:\path\to\video.mp4"
```

### Developer flags

| Flag | What it does |
| --- | --- |
| `--play <path or URL>` | Play this immediately |
| `--silent` | Start muted |
| `--no-activate` | Don't take keyboard focus when the window opens |
| `--window x,y,w,h` | Place the window at these physical-pixel bounds (for example, on a second display) |
| `--spike-steps block@5,fullscreen@9,quit@17` | Unattended checks: run scripted actions at the given seconds |

## Layout

```
src/Retune.Mpv     libmpv interop and MpvClient (event loop, typed properties, commands)
src/Retune.App     WPF app: native video host, transparent overlay host, full screen
tests/             xUnit v3 tests (Microsoft Testing Platform)
tools/             libmpv fetch and test-media scripts
docs/              plan and feature specs
```

## How video is drawn

mpv renders into a native child window (`VideoHost`) with its own D3D11 swap chain, on its own thread. A busy WPF UI thread therefore can't make the video stutter. Player controls are WPF content in a per-pixel-transparent child window (`OverlayHost`) stacked above it. The overlay moves, resizes and goes full screen together with the main window, with no separate top-level window to keep in sync.

## Privacy and security

Retune talks only to your provider and the artwork hosts your playlist names. mpv runs with scripts, youtube-dl and unsafe playlists disabled. See the security rules in [docs/spec/core.md](docs/spec/core.md#0-non-negotiable-product-rules-from-agentsmd--securitymd).
