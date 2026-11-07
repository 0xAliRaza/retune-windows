# Retune for Windows: project instructions

Read `docs/PLAN.md` first. Feature behaviour is defined by `docs/spec/core.md` and `docs/spec/ui-player.md`; when the code and the spec disagree, the spec wins unless the plan's decision table (PLAN.md section 5) says otherwise.

## Commands

```powershell
./tools/fetch-libmpv.ps1          # once (and after changing the pinned build)
./tools/make-test-media.ps1       # once
dotnet build Retune.slnx          # warnings are errors
dotnet test --solution Retune.slnx
```

The app exe is at `src/Retune.App/bin/Debug/net10.0-windows/win-x64/Retune.exe`.

## Rules for this repo

- Layering: `Retune.Core` (pure, no Windows/UI) ← `Retune.Infrastructure` / `Retune.Mpv` ← `Retune.App`. Playback rules (debounce, reconnect, progress) live in Core behind `IMediaEngine` + `TimeProvider`, not in the app.
- Security rules in `docs/spec/core.md` §0 are non-negotiable: never log URLs or credentials, cap every network body, and keep `load-scripts=no`, `ytdl=no` and `load-unsafe-playlists=no`.
- `MpvClient` events arrive on the mpv thread. Marshal them to the dispatcher, and never call `Dispose` from a handler.
- Every feature gets tests. UI changes get checked in the running app.

## Manual and visual testing on this machine

- The user has two displays. **Never test on the 4K primary display** (`DISPLAY1`). Use the laptop's internal display, which is 1920×1200 at 125% with physical origin (3840, 546). Look the bounds up again if the layout may have changed.
- Always run test builds with `--silent --no-activate --window <bounds on the internal display>`, and drive them with `--spike-steps` rather than keystrokes. Never take the user's keyboard focus or play sound.
- Capture screenshots with a per-monitor-v2 DPI-aware process (`SetProcessDpiAwarenessContext(-4)`) so the coordinates are real pixels.
