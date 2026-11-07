# Retune for Windows: architecture and build plan

Status: **draft for approval**. Feature parity is defined by `docs/spec/core.md` (data and logic) and `docs/spec/ui-player.md` (UI and player), both derived from the macOS app. This document covers *how* we build it.

## 1. Quality bar

- **Smooth:** video never stutters because the UI is busy. Browsing 88k items stays instant. No UI-thread IO.
- **Robust:** hostile playlists and flaky providers never crash the app. Streams recover on their own. Data files survive crashes and corruption.
- **Native Windows 11 feel:** Fluent controls, Mica, light and dark themes, the system accent colour, keyboard-first use, and proper accessibility names.
- **Tested:** every rule in the specs has a test. Critical journeys run end to end against a fake provider. CI must be green before anything merges.

## 2. Stack

| Concern | Choice | Why |
| --- | --- | --- |
| Runtime | **.NET 10 (LTS)**, C# latest | Current LTS, fast, AOT-friendly core |
| UI | **WPF** + **WPF-UI** (lepoco, MIT) | WPF is mature and stable. WPF-UI adds the Fluent pieces WPF lacks: NavigationView, ContentDialog, Mica TitleBar, Fluent icons, Snackbar |
| MVVM | **CommunityToolkit.Mvvm** | Source-generated observable properties and commands, no reflection |
| Composition | Microsoft.Extensions Hosting, DI, Logging | Standard. Every boundary is an interface |
| JSON | System.Text.Json with source generators | Fast, trimming-safe |
| Video | **libmpv** (the same engine as on macOS), called through our own small `LibraryImport` P/Invoke layer | Full control over reconnects, tracks, stats. No third-party wrapper to depend on |
| Secrets | Windows Credential Manager (`CredWriteW`/`CredReadW` P/Invoke), target `Retune/<uuid>` | OS-encrypted, per user |
| Tests | xUnit v3, hand-written fakes, **FlaUI** (UI Automation) for end-to-end | Deterministic, with no network |
| Installer | Inno Setup (installer) + portable zip | Free and simple. No auto-update, per the product rule of no outbound calls |
| CI | GitHub Actions `windows-latest`: build, analyzers, all tests, package | |

## 3. Key decision: how video gets on screen

WPF can't draw on top of a native video window (the "airspace" problem). There are two options:

| | **A. mpv owns a child window (`wid`) + a transparent overlay window for the controls** ✅ | B. mpv renders through OpenGL into a WPF image (`D3DImage` interop) |
| --- | --- | --- |
| Playback smoothness | ✅ mpv renders on its own thread with its own D3D11 swap chain and proper vsync. The WPF UI thread can't stall the video | ❌ Each frame has to be handed over on the WPF UI thread, so a busy UI (loading 88k items) makes the video stutter |
| Hardware decoding | ✅ d3d11va zero-copy, HDR-capable, mpv's best renderer (`gpu-next`) | ⚠️ Needs a GL↔D3D interop that is fragile on some drivers, or copy-back |
| Overlays | ⚠️ A separate owned, borderless, transparent window that tracks the player. Proven: LibVLCSharp.WPF does exactly this | ✅ Ordinary WPF elements |
| Screenshots | mpv `screenshot-to-file … video` at source resolution | Read back the frame |

**Pick: A.** Video smoothness is the core of a player, and A gets mpv's best path. The overlay complexity is contained in one component (`PlayerOverlayHost`) with its own tests for focus, Alt+Tab, full screen, DPI changes, multiple monitors and minimise/restore. Translucent panels become dark semi-opaque fills, since blur isn't possible over video (the spec allows this).

## 4. Solution layout

```
retune-windows/
  src/
    Retune.Core/            net10.0, no Windows or UI deps. Pure domain + application logic
      Models/  Xtream/  M3u/  Catalog/  Search/  Library/  Parental/  Playback/  Policies/  Formatting/
    Retune.Infrastructure/  net10.0-windows. JSON stores, credential store, settings, artwork cache, file ACLs
    Retune.Mpv/             net10.0-windows. libmpv interop + MpvEngine : IMediaEngine
    Retune.App/             WPF. Views, view models, key routing, video host, overlay, theming
  tests/
    Retune.Core.Tests/            unit tests (the bulk)
    Retune.Infrastructure.Tests/  temp-dir file stores, corruption, ACLs, credentials
    Retune.Mpv.Tests/             integration: real libmpv on generated local media
    Retune.App.Tests/             view models and key routing with fakes
    Retune.E2E/                   FlaUI journeys against the real app + fake provider
  tools/
    FakeProvider/      local Xtream + M3U server with generated demo data (dev, screenshots, E2E, 88k-item perf runs)
    fetch-libmpv.ps1   downloads a pinned libmpv build and checks its SHA-256
  docs/  spec/  PLAN.md
```

Layering rules (enforced by project references):
- `App → Core, Infrastructure, Mpv`; `Infrastructure → Core`; `Mpv → Core`. Core references nothing.
- **Playback logic lives in Core**: `PlaybackSession` covers queueing, the 350 ms zap debounce, reconnect backoff, early-end detection, progress commits and the autoplay countdown. It runs against `IMediaEngine` and `IClock`, so every timing rule is unit-tested with a fake engine and a fake clock. `Retune.Mpv` is only a thin adapter.
- View models never touch IO directly. They call application services in Core through interfaces.

Notable designs:
- **Catalog indexes:** names are folded once into search keys when the catalog loads, on a background thread. Search does substring matching on those keys. Results are applied to the UI as one batch.
- **Grid virtualisation:** items are chunked into rows (column count from the spec formula), shown in a recycling `VirtualizingStackPanel`. This makes ↑/↓ row moves exact and keeps scrolling smooth.
- **Artwork:** an `ArtworkService` provides a memory LRU (600 entries / 300 MiB), a content-addressed disk cache (1 GiB), in-flight de-duplication, a negative cache, decoding off the UI thread to the requested size, frozen bitmaps, and cancellation when a list container is recycled.
- **Data:** files live in `%APPDATA%\Retune\` with a user-only ACL. Writes are atomic (temp file + replace), and unreadable files are moved to `.corrupt-<ts>`. The artwork cache lives in `%LOCALAPPDATA%\Retune\Cache\Artwork`.

## 5. Decisions on the open questions (my defaults; override any)

| # | Question | Default |
| --- | --- | --- |
| 1 | Screenshot folder | `Pictures\Retune`. The flash message names the folder, and clicking it opens Explorer |
| 2 | Screenshot content | Source resolution, video only (mpv `screenshot-to-file … video`) |
| 3 | Numeric channel entry | **Add.** Typing digits shows them on screen and tunes after 1.5 s (Enter tunes at once) |
| 4 | Media keys + Windows media flyout (SMTC) | **Add** (phase 5) |
| 5 | Delete-playlist confirmation | **Add** |
| 6 | Ctrl+1–3 while the player is open | Disabled |
| 7 | Shortcuts | Layouts Ctrl+Shift+1–3; Add Playlist Ctrl+N; Back = Alt+←, mouse Back button, Backspace; Help = F1, Ctrl+/ and `?` app-wide |
| 8 | Wrong-PIN attempts | 5 tries, then an escalating lockout (30 s, then 1 min, 5 min) |
| 9 | Settings | A page inside the main window (gear at the bottom of the sidebar), as in Windows 11 apps |
| 10 | Favourite icon | Star everywhere |
| 11 | Trackpad swipe back | Skip. Mouse Back button and Alt+← cover it |
| 12 | Single instance | Yes. A second launch brings the first window forward |
| 13 | Mini player / PiP | Deferred until after v1 |
| 14 | Sort ties | Stable sort, with provider order as the tie-break |
| 15 | URL encoding of credentials | Strict RFC 3986 percent-encoding (UTF-8) |
| 16 | PIN digits | ASCII 0–9 only |
| 17 | Search folding | Diacritic-, case- and width-insensitive, with tests for ß, æ, the Turkish i, Arabic and CJK |
| 18 | Blank M3U titles | Fall back to `tvg-name`, then to the URL file name |
| 19 | Importing macOS data | No. Windows uses its own files |

## 6. Phases (each ends green: build, zero analyzer warnings, all tests passing)

| Phase | Delivers | Exit check |
| --- | --- | --- |
| **0. Foundation** | Solution, `.editorconfig`, analyzers with warnings-as-errors, CI, `fetch-libmpv.ps1`, a spike window that plays a local file through the A approach with an overlay button on top | Video plays smoothly while the overlay stays aligned through resize, maximise, full screen and Alt+Tab |
| **1. Core** | Models, server input, Xtream client + lenient decoding, M3U parser, URL policy, locators, catalog + indexes + search, library, PIN, queue, `PlaybackSession`, formatting | All ~120 macOS test cases ported, plus new edge cases. Search over 88k items < 50 ms |
| **2. Infrastructure** | JSON stores, credential store, settings, artwork service, FakeProvider | Crash-safety, corruption and ACL tests. Artwork cache tests |
| **3. Browse UI** | Shell (Mica, title-bar search), welcome screen, sidebar, categories, three virtualised layouts, sort and tile size, detail pages, playlist editor, empty/loading/error states, context menus, pane key routing | View-model tests + E2E: add playlist → browse → search → open details. Scrolling 65k posters stays smooth |
| **4. Player** | Video host + overlay, top and bottom bars, OSD, tuning card, list panel, tracks, subtitle panel, speed, volume, stats, reconnect, EPG, autoplay, full screen, screenshots, sleep prevention | Engine-level integration tests + E2E: zap through channels, resume, autoplay, a reconnect after the fake provider drops the stream |
| **5. Parental, settings, Windows extras** | PIN flows + lockout, settings page, shortcuts sheet, numeric entry, media keys/SMTC, single instance, accessibility names | E2E: lock, unlock, re-lock. Accessibility Insights pass |
| **6. Hardening and release** | 88k-item perf runs, a 4-hour playback soak (memory flat), installer + portable zip, README | Release build installs cleanly on a fresh user profile |

I'll review my own work at the end of every phase (the code-review skill) before moving on.

## 7. Testing strategy

- **Unit tests (Core):** every constant and rule in the specs: thresholds, backoffs, debounce timings (with a fake clock), decoding quirks, the URL policy, search folding, queue windowing.
- **Integration tests:** file stores in temp folders, Credential Manager under a test target prefix, real libmpv playing generated MPEG-TS/MP4 fixtures (audio-less, multi-track, subtitled, truncated).
- **E2E tests:** FlaUI drives the real app against `FakeProvider`, with an isolated data folder (`RETUNE_DATA_DIR`) and the audio output off.
- **Manual verification:** I launch the app and check every UI change by eye before marking it done.

## 8. External downloads to approve

- **libmpv** dev build for Windows from `shinchiro/mpv-winbuild-cmake` GitHub releases (GPL, matching Retune's GPL-3.0). The version is pinned and the hash checked by `fetch-libmpv.ps1`.
- **FFmpeg** (dev only, for generating test media): `winget install Gyan.FFmpeg`.
- **Inno Setup** (phase 6 only): `winget install JRSoftware.InnoSetup`. This one shows a Windows prompt.
- NuGet packages: WPF-UI, CommunityToolkit.Mvvm, Microsoft.Extensions.*, xUnit, FlaUI.
