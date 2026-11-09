# Retune for Windows: UI and Player Functional Spec

Status: draft 1. Derived from the macOS app `retune-macos` v1.0.2 (Swift/SwiftUI + libmpv), read-only.
Scope: windows, screens, navigation, keyboard, player, image loading, settings, parental PIN UI, platform behaviours and debug tooling. The data layer (Xtream/M3U parsing, catalog, library storage, PIN hashing, secret storage) is covered by a separate spec and appears here only where the UI depends on it.

Conventions in this document:

- "pt" means macOS points. On Windows, treat 1 pt = 1 WPF DIP as the starting point.
- macOS keys are shown with their symbols (⌘ Command, ⌃ Control, ⌥ Option, ⇧ Shift, ⌫ Backspace/Delete, ⇥ Tab). The proposed Windows binding is given next to each.
- "Accent" means the system accent colour (macOS `Color.accentColor`). On Windows use `SystemAccentColor` from the Fluent theme.
- Source references point at the macOS files so the behaviour can be re-checked: `Views/…`, `Player/…`, `Support/…`, `AppModel.swift`, `RetuneCore/…`.

---

## 1. App structure

### 1.1 Windows

| Window | macOS behaviour | Notes |
| --- | --- | --- |
| Main window, title "Retune" | Single instance (`Window` scene, not a multi-window group). Minimum 980×620, default 1360×860. | Closing it quits the app (`applicationShouldTerminateAfterLastWindowClosed = true`) and stops playback first. |
| Settings window | Separate standard macOS Settings window, opened with ⌘, (app menu "Settings…") or the "N hidden" link in the category column. Fixed width 520. | Has its own sheet host for PIN prompts started from Settings. |
| Sheets (modal, attached to the main window) | Add Playlist, Edit Playlist, Keyboard Shortcuts, PIN prompt. | Sheets block the pane key router (section 3.4). |

There is no separate player window. The player is a full-window overlay inside the main window (section 4).

On quit (`applicationWillTerminate`), in this order: stop the playback session (which saves the final progress), flush the library to disk immediately (cancelling the 1 s debounce), release the video render context, then destroy the mpv handle. The Windows app must keep that order (render context released before `mpv_terminate_destroy`).

### 1.2 Root layout

The root view picks one of two layouts:

1. **No playlists** → Welcome screen (section 2.1).
2. **At least one playlist** → three-column browser:

| Column | Width (min / ideal / max) | Content |
| --- | --- | --- |
| Sidebar | 200 / 220 / 280 | Library sections and playlists (section 2.2) |
| Category column | 220 / 260 / 340 | Category filter field, fixed filters and provider categories (section 2.3) |
| Content (detail) | remaining | Browse collection, or a pushed detail page (movie or series), with a back stack |

The window toolbar holds: the search field (prompt "Search Live TV" / "Search Movies" / "Search Series"), a Refresh button, the layout segmented control and the "View Options" (sort) menu. The toolbar is hidden while the player is presented or while a playback session is still active (it stays hidden until playback has fully stopped, so it never pops in over the fading player).

The sidebar column is collapsible (standard split view behaviour). The published screenshots show it collapsed: only the category column (titled with the section name, e.g. "Movies") and the content are visible.

Navigation titles: category column shows the section title; content shows the filter title (`All Live TV`, `Favorites`, `Continue Watching`, `Recently Watched`, the category name, or `Results for “<query>”` while searching); a detail page shows the movie or series name.

### 1.3 Sections

| Section | Title | Icon (SF Symbol → suggested Segoe Fluent) | Availability |
| --- | --- | --- | --- |
| live | Live TV | `dot.radiowaves.left.and.right` → "Streaming"/antenna glyph | Always |
| movies | Movies | `film` → "Video"/film glyph | Always |
| series | Series | `play.rectangle.on.rectangle` → "SlideShow"/stacked glyph | Xtream playlists only. Hidden in the sidebar and its shortcut disabled for M3U playlists. If the active playlist changes to M3U while Series is selected, the section switches to Live TV. |

### 1.4 Navigation model

State that drives navigation (from `AppModel`):

- `section` (live, movies, series). Changing it **resets browsing**: clears the detail back stack, clears the search text, resets sort to "Default", and sets the filter to the section default.
- `filter`: All, Favorites, Continue Watching (Movies/Series), Recently Watched (Live), or a specific category.
- Section default filter: Live → All. Movies/Series → Continue Watching if it has entries, otherwise the first visible (not hidden) and unlocked category, otherwise All. Rationale in the source: rendering 60k+ posters under All is slow to scan.
- The default filter is also applied the first time a different playlist's catalog loads.
- `detailPath`: back stack of routes in the content column. Routes are `movie(Movie)` and `series(Series)`. Selecting any category clears it.
- Back: ⌘[ (menu "Back"), Esc in the content column when a detail page is shown, or the trackpad back swipe. Back pops one route. Back is disabled when the stack is empty or the player is presented.
- Per-section layout mode is remembered across launches (section 2.4).

Opening things:

| Item | Primary action (click, Return) | Secondary |
| --- | --- | --- |
| Channel | Play immediately | — |
| Movie | Push movie detail page | Hover play button on the poster plays directly |
| Series | Push series detail page | — |
| Continue Watching entry | Resume playback | — |
| Episode row (series page) | Play | — |

### 1.5 First run and add-playlist flow

1. App starts with no playlists → Welcome screen.
2. "Add Playlist" (button, sidebar button, File menu, ⇧⌘N) opens the playlist editor sheet (section 2.8).
3. Xtream: the editor authenticates against the server before saving. Errors are shown inline in the sheet and the sheet stays open. M3U: saved without verification.
4. On save of a **new** playlist it becomes the active playlist, which triggers loading:
   - If an Xtream playlist has no stored password: status becomes failed with the "missing password" message (content area error state) and nothing cached is shown.
   - Status "Opening <playlist name>…" while the cached catalog is read.
   - Cached catalog found → shown immediately; if older than **12 hours** a background refresh runs (spinner in the toolbar instead of the Refresh button).
   - No cache → full download, status "Downloading channels, movies and series…".
   - After load, for Xtream, account info is fetched (status, expiry, max connections) for the sidebar badge.
5. Saving changes to the **active** playlist triggers a refresh that supersedes any refresh still using the old details.
6. Switching the active playlist stops playback first (the final progress belongs to the old playlist), clears the visible catalog, and loads the new one.
7. A background refresh failure while a cached catalog is shown is silent: the cached content stays.

Active playlist is persisted (`activePlaylist` = UUID). At launch the stored one is used if it still exists, otherwise the first playlist.

---

## 2. Screens

### 2.0 Visual design and feel (from the screenshots)

- Dark appearance in all screenshots (the app follows the system theme; nothing forces dark except the player chrome). Background is a near-black neutral grey; the sidebar/category column is a slightly different neutral separated by a hairline.
- Density: relaxed, "media app" density in grid; list rows are tall enough for a logo; compact rows are dense for scanning.
- Typography hierarchy: column/section title bold (~17 pt) at the top of each column; section headers ("Categories") small, semibold, secondary grey; list rows body size; tile titles callout-size medium weight under the artwork; counts in small secondary digits right-aligned.
- Selection in the category list is a rounded grey pill (not accent). Accent (blue in screenshots) is used for: fixed-filter icons (All, Favorites, Continue Watching/Recently Watched glyphs are accent-coloured line icons), keyboard focus rings on tiles, focused list row tint, progress bars, "Now playing" text, the active playlist checkmark, primary buttons.
- Rating badge: yellow star + one-decimal number in bold, on a black 60% capsule at the poster's top-left.
- Posters: 2:3, 10 pt corner radius, soft drop shadow; four columns at a typical window width with ~20 pt gaps and ~24 pt vertical gap including the title line.
- Live list: channel number in a dim column, logo in a small rounded dark tile, channel name; alternating row striping.
- Toolbar: icon-only buttons (refresh, three layout icons as a segmented control with the selected one in a subtle rounded highlight, a sort "↑↓" menu with chevron) and a search field with a magnifier and placeholder "Search <Section>".

### 2.1 Welcome screen

Centred vertical stack, spacing 22, padding 40:

- Icon `play.tv.fill`, 64 pt, purple→blue diagonal gradient.
- "Welcome to Retune" (large title, bold).
- "Add your IPTV subscription to start watching live TV, movies and series." (title3, secondary, centred).
- "Add Playlist" button: prominent/accent, extra-large, with a plus icon.
- Row of three hints (170 pt wide each, spacing 28, top padding 10): icon (title2, secondary), title (headline), detail (caption, secondary):
  - server icon, "Xtream Codes", "Server, username & password"
  - link icon, "M3U URL", "A playlist link from your provider"
  - document icon, "M3U File", "A .m3u file on your Mac" (Windows: "on this PC")

### 2.2 Sidebar

List with sidebar styling.

**Section "Library"**: one row per available section with icon, title and a count badge (total items in that section for the loaded catalog; 0 shows no badge). Selecting a row sets `section`.

**Section "Playlists"**: one row per playlist:

- Leading icon: filled check circle in accent for the active playlist, empty circle in secondary for others.
- Name (1 line) over the kind label in caption/secondary: "Xtream Codes", "M3U URL" or "M3U File".
- Click: make active.
- Context menu: "Use This Playlist", "Edit…", divider, "Delete" (destructive). **Delete has no confirmation in the macOS app** (see Open questions). Deleting the active playlist stops playback, invalidates refreshes in flight, deletes the catalog cache, removes the password and all library entries for that playlist, then activates the first remaining playlist (or shows Welcome when none remain).

**Bottom inset** (padding 12, spacing 8):

- Account badge (Xtream only, after authentication):
  - Status text with a person-check icon, caption semibold, green when status is "Active" (case-insensitive), orange otherwise.
  - "Expires <abbreviated date>" (caption2, secondary) if known.
  - "<N> connection(s) allowed" (caption2, secondary) if known.
- "Add Playlist" borderless button with `plus.circle.fill`, full width, left-aligned.

### 2.3 Category column

Shown only when a catalog is loaded (otherwise empty).

- **Top inset**: "Filter categories" rounded text field (padding h10 v6). Filters the provider categories by accent/case/width-insensitive, all-terms-must-match search. Cleared when the section changes.
- **Fixed section** (no header):
  - "All" (grid icon) with the section total as badge.
  - "Favorites" (star icon).
  - Live: "Recently Watched" (clock-arrow icon). Context menu "Clear Recently Watched…".
  - Movies/Series: "Continue Watching" (play-circle icon). Context menu "Clear Continue Watching…".
  - Clear confirmation dialog: title "Clear Recently Watched?" / "Clear Continue Watching?", message "This only affects this playlist. Favorites and watched marks are kept.", destructive button "Clear".
- **"Pinned"** section (only if any pinned categories), then **"Categories"** section.
- Category row: trimmed name (1 line), spacer, lock indicator (`lock.fill` caption secondary when locked; `lock.open` caption tertiary when unlocked this session; none otherwise), count badge. **The badge is hidden for a locked category** so the count can't reveal what it holds.
- Category context menu: "Pin to Top" / "Unpin" (pin icons); "Lock with PIN" or "Remove Lock…" (lock icons); divider; "Hide Category" (eye-slash).
  - Pinned categories appear first, in the order they were pinned.
  - Hiding the currently selected category switches the filter to All.
- **Bottom inset** (only when the section has hidden categories): borderless secondary link "<N> hidden" with eye-slash icon, tooltip "Manage hidden categories in Settings"; opens Settings.
- Selecting any row sets the filter and clears the detail back stack.

### 2.4 Content area: layouts

One generic "browse collection" renders any list of items (channels, movies, series, Continue Watching entries) in one of three layouts. All three must be virtualised: the macOS app replaced `List` with lazy stacks because Live "All" with ~16k channels froze the UI for about 1 s. Real catalogs reach ~88k items (65k movies).

Layout is per section, persisted as `layout.live`, `layout.movies`, `layout.series`. Defaults: **Live = List**, **Movies = Grid**, **Series = Grid**. Selected via toolbar segmented control (tooltip "Layout: Grid (⌃⌘1), List (⌃⌘2), Compact (⌃⌘3)"), View menu "as Grid/as List/as Compact", or ⌃⌘1/2/3.

Tile size (grid only), global, persisted as `layout.tileSize`, default **Medium**. Values are the grid's minimum column width: **Small 112, Medium 150, Large 210**. Landscape cards (Continue Watching) use 1.7× that minimum.

Sort (toolbar "View Options" menu, tooltip "Sort and tile size"), not persisted, reset to Default on every section change:

| Option | Label | Live | Movies | Series |
| --- | --- | --- | --- | --- |
| provider | Default | yes | yes | yes |
| title | Title | yes | yes | yes |
| recentlyAdded | Recently Added | no | yes | yes |
| rating | Rating | no | yes | yes |

The menu shows the sort picker inline and, in grid layout only, a "Tile Size" picker (Small/Medium/Large).

#### Grid

- Adaptive columns: minimum = tile min width, maximum = 1.35 × minimum. Horizontal spacing 20, vertical spacing 24, outer padding 24. Each tile has 4 pt padding.
- Keyboard focus ring: accent stroke 3 pt, corner radius 12, around the focused tile (the system focus ring is disabled).
- Column count for keyboard navigation: `max(1, floor((width − 2×24 + 20) / (min + 20)))`.

Tiles by artwork type:

**Poster card** (movies, series):
- Fixed 2:3 box, artwork fills and is clipped, corner radius 10. Image requested at 420 px max.
- Top-left badges (padding 7, spacing 4): rating capsule (caption2 bold, yellow star + "8.4", black 60% background) and, if favourite, a pink filled heart in a black 60% circle. (Note: grid uses a heart; list rows use a yellow star for the same favourite state.)
- Bottom: 4 pt progress bar (accent fill over black 50%) when progress > 1%, bottom corners rounded to match.
- Hover: shadow from (black 15%, radius 5, y 3) to (black 35%, radius 14, y 8), scale to 1.03, animated ease-out 0.15 s. Movies also show a 52 pt circular "play" button with a thin material background (tooltip "Play") that plays directly instead of opening details.
- Below: title (callout, medium, up to 2 lines); caption (caption, secondary, 1 line). Movies show no caption on posters (their "Added <date>" is list-only). Series caption is "<year> · <genre>".
- Tooltip: full title.

**Logo tile** (live channels, grid):
- 16:10 box, background quaternary 60%, corner radius 10, logo fitted with 16 pt padding (no padding when showing the placeholder). Image 300 px.
- Accent 2.5 pt border when this channel is currently playing.
- Yellow filled star (caption2) top-right if favourite.
- Hover: 40 pt play glyph in a thin-material circle; shadow from black 8% r3 to black 25% r10.
- Below: channel number (caption, monospaced digits, tertiary) then name (callout, medium, semibold if playing, 2 lines).

**Landscape card** (Continue Watching):
- 16:9 box, artwork fills and clips, corner radius 10, image 600 px.
- Linear progress bar (accent) at the bottom with 10 pt padding.
- Centre play glyph: white, 48 pt thin-material circle, opacity 0.85 (1.0 on hover).
- Shadow black 12% r4 → black 30% r12 on hover.
- Title (callout medium, 1 line) and subtitle (caption secondary, 1 line). Subtitle = item subtitle and remaining time joined by " · ", e.g. "S1 E3 · Pilot · 42m left" or "1h 12m left".

#### List (thumbnail rows)

- Row min height 46, horizontal padding 14, vertical 2 (+3 inner). Background: rounded rect radius 6, inset 6 pt from the sides; odd rows quaternary 35% stripe, even clear; focused row accent 28%.
- Contents left→right (spacing 12):
  - Channel number (callout, monospaced, tertiary, 44 pt wide, right-aligned) when present.
  - Thumbnail: logo 56×34 fitted with 4 pt padding on quaternary 50% rounded-6 background (image 120 px, fallback TV glyph); poster 40×60 radius 5 (140 px); landscape 80×45 radius 5 (200 px).
  - Title (body; semibold if playing) over either "Now playing" (caption, accent) or the subtitle (caption, secondary). Movies' subtitle is "Added <abbreviated date>"; series "<year> · <genre>".
  - Spacer.
  - Progress bar 60 pt wide (accent) if progress > 1%.
  - Rating label: star + "7.4", caption monospaced, secondary.
  - Favourite star button: yellow filled when favourite (always visible), outline secondary on hover only. Tooltip "Add to Favorites"/"Remove from Favorites". Clicking toggles.
  - Trailing glyph: speaker (accent) if playing, else play glyph (secondary) visible on hover only.

#### Compact (text rows)

- Row min height 26, vertical padding 3, same striping/focus as List.
- Number (caption, monospaced, tertiary, 40 pt), speaker glyph (accent) if playing, title (callout; semibold if playing), spacer, subtitle (caption secondary), progress as "NN%" (caption monospaced) if > 1%, rating (caption), favourite star (caption).

#### Content keyboard model (all layouts)

- Arrow keys move a focus highlight; Return opens the focused item (primary action).
- Lists are one column: → does nothing, ← always goes to the category column.
- Grid: ← from the first column → category column; ↑ from the top row → toolbar search field.
- Movement rules (`GridNavigation.move`): no focus yet → first item; left/right ±1, clamped to first/last; up/down ± columns; up from the top row stays (unless leaving per the edge rule); down past the end goes to the last item, or stays if already on the last row.
- The focused item is scrolled into view (grid animates 0.15 s ease-out).
- The collection remembers the last focused item; when focus returns to the content column (e.g. after closing the player) it restores that item, else focuses the first.

### 2.5 Content area: states

Priority order:

1. **Loading** (status loading and no catalog yet): centred large spinner, the status message (headline: "Opening <name>…" or "Downloading channels, movies and series…"), and "Large playlists can take a minute the first time. After that they open instantly." (callout, secondary).
2. **Failed** (status failed and no catalog): "Couldn't load playlist" with a warning-triangle icon, the error message, buttons "Try Again" (prominent; runs refresh) and "Edit Playlist…" (opens the editor for the active playlist).
3. **No catalog**: "No playlist selected" with a list icon.
4. **Locked category selected** (and not searching): Locked view (section 7).
5. **Continue Watching filter** (and not searching): landscape cards for the section's resumable items (movies for Movies, episodes for Series, one entry per series, most recent first).
6. Otherwise the section's items filtered by category / favourites / recents / search, sorted.

Empty results (`EmptyResults`):

| Condition | Title | Description | Icon |
| --- | --- | --- | --- |
| Searching | Standard "No Results for “<query>”" | system default | magnifier |
| Continue Watching | "Nothing in progress" | "Movies and episodes you start will appear here so you can pick up where you left off." | play-circle |
| Favorites | "No favorites yet" | "Right-click anything or use the star to add it here." | star |
| Anything else (incl. empty Recently Watched) | "Nothing here" | — | tray |

Search rules (UI-visible): accent-, case- and width-insensitive; every whitespace-separated term must appear in the name. While searching, results span every category of the current section (the category and Favorites/Recent filters are ignored). Hidden categories and still-locked categories are excluded from All, Favorites, Search, Recently Watched and Continue Watching; a category the user selected and unlocked is shown.

Recently Watched: up to 30 channels, most recent first (data layer constant).

### 2.6 Context menus (browse items)

| Item | Menu items |
| --- | --- |
| Channel | Play · Add to Favorites / Remove from Favorites · (only under Recently Watched) Remove from Recently Watched |
| Movie | Play · Show Details · Add/Remove Favorites · Mark as Watched / Mark as Unwatched |
| Series | Open · Add/Remove Favorites |
| Continue Watching entry | Resume · Remove from Continue Watching (animated removal) |
| Episode row | Play · Mark as Watched / Mark as Unwatched |
| Category | see 2.3 |
| Playlist | see 2.2 |

"Watched" means the saved progress is finished (≥ 95% or < 90 s left). Resumable means position ≥ 30 s and not finished.

### 2.7 Detail pages

Both pages share a **hero** (scrolls with content, extends under the top bar):

- Backdrop image (backdrop, else poster; 1600 px), height 420, full width, clipped, with a vertical gradient overlay from clear → window background 85% → window background. Placeholder: black 40%.
- Bottom-left row (padding 32, spacing 28): poster 190×285, radius 12, shadow black 40% r18 y10 (600 px); then title (34 pt bold, up to 3 lines), metadata line, action buttons (large control size, spacing 10).
- Metadata line (callout medium, secondary, spacing 10): rating with yellow star, year, runtime ("1h 42m" / "45m"), genre (1 line).

Info block (horizontal padding 32): plot (body, line spacing 3, max width 760, selectable) and a two-column grid (callout, label secondary): "Cast", "Director", "Released" (values up to 2 lines, selectable). Page bottom padding 32.

**Movie page**

- Movie info is fetched asynchronously from the provider when opened (Xtream only; M3U shows just the catalog rating/poster).
- Buttons:
  - If resumable: "Resume · <1h 2m left>" (prominent, default button → Return) and "Start Over".
  - Else: "Play" (prominent, default).
  - Favourite toggle: "Favorite" (outline heart) / "Favorited" (filled heart, pink tint).
  - "Trailer" (only if a YouTube id is known): opens `https://www.youtube.com/watch?v=<id>` in the default browser.
- Playing from the detail page queues the current browse results if they contain this movie, otherwise just this movie.

**Series page**

- Series detail (seasons, episodes, info) is fetched when opened. States: loading "Loading episodes…" spinner (padding 40); error "Couldn't load episodes" + message + "Try Again"; empty "No episodes available yet." (secondary).
- Primary button (prominent, default), chosen by `EpisodePicker` over all episodes in play order, using the most recently updated progress entry that is resumable or finished:
  - Most recent is in progress → "Resume S<s> E<e>".
  - Most recent is finished and has a successor → "Play Next · S<s> E<e>".
  - Nothing started → "Play S1 E1" (first episode's numbers).
  - Most recent finished is the last episode → "Watch Again" if any episode is watched, else "Play S.. E..", playing the first episode.
- Favourite toggle and Trailer as for movies.
- Episodes section: header "Episodes" (title2 semibold) with a "Season" picker on the right when there is more than one season (shows season names). Initial season = season of the primary-button episode, else the first season.
- Episode row (button, padding 10, hover background quaternary 60% radius 12):
  - Still 200×112 radius 8 (episode image, else series cover; 480 px; placeholder with a play-rectangle glyph), progress bar overlay when > 1%, hover play glyph 40 pt.
  - "<n>. <title>" (headline, 1 line), runtime right-aligned (caption, secondary), plot (callout, secondary, 3 lines), "Watched" label (checkmark, green, caption) when finished.
- Playing an episode queues every episode of every season, queue title = series name, resuming from the saved position (minus 5 s).

### 2.8 Playlist editor (sheet)

Width 520. See screenshot `plain-http-warning.png` for the grouped-form look (label left, value right-aligned, rounded group background, warning row with an open-lock icon).

- Header (padding 24): "Add Playlist" / "Edit Playlist" (title2 semibold) and "Your details are stored on this Mac only. Nothing is sent anywhere except your provider." (callout secondary). Windows: "on this PC".
- Form (grouped, not scrollable):
  - "Type" segmented picker: Xtream Codes / M3U URL / M3U File. **Only when adding**; the type can't change when editing.
  - "Name" (placeholder "e.g. My IPTV").
  - Xtream:
    - "Server URL" (placeholder "http://example.com:8080"). Pasting a full provider link containing `username` and `password` query items fills Server, Username and Password automatically.
    - "Username".
    - "Password" (masked). When editing, placeholder "Unchanged"; leaving it empty keeps the stored password.
    - Plain-HTTP warning when the normalised server is `http` (no scheme counts as http): open-lock icon, callout secondary, "This address uses plain HTTP, so your login travels unencrypted. Use an https:// address if your provider offers one."
  - M3U URL:
    - "Playlist URL" (placeholder "http://…/get.php?username=…&type=m3u_plus").
    - Same plain-HTTP warning.
    - If the URL parses as an Xtream link: sparkles icon + "This is an Xtream link — switching to Xtream Codes gives you series, posters and EPG." and a button "Use Xtream Codes Instead" (switches type and copies the URL into Server, which autofills the rest).
  - M3U File: file name or "No file chosen" (secondary) and "Choose…" (file picker filtered to .m3u, .m3u8, plain text). Choosing a file fills Name from the file name if Name is empty.
- Error row (red, warning icon, callout, padding h24) for validation/authentication/save failures.
- Buttons (padding 24, right-aligned): "Cancel" (Esc) and "Add"/"Save" (Return, prominent). Disabled while invalid or verifying; while verifying shows a small spinner instead of the label.
- Validity: Xtream needs a normalisable server, non-empty username, and a password unless editing. M3U URL needs a leniently parseable URL. File needs a chosen file.
- Default names: Xtream → server host, else "Xtream"; M3U URL → host, else "Playlist"; M3U file → file name without extension.

### 2.9 Keyboard Shortcuts sheet

Width 860, padding 26, spacing 18. Header "Keyboard Shortcuts" (title2 semibold) with "Done" (Esc). Two columns (spacing 36): "Player" and "App" (headline, secondary), each a two-column grid: key caps right-aligned, action (callout) left. Key cap: 12 pt rounded-design medium text, padding h7 v3, min width 26, quaternary fill, 0.5 pt tertiary border, radius 5. Content is the shortcut catalog in section 3. Opened with ⌘/ (Help menu "Keyboard Shortcuts") or `?` in the player.

---

## 3. Keyboard

### 3.1 Player shortcuts

Handled by a key monitor on the main window while the player is presented. Events with ⌘ are ignored by the player (so menu shortcuts still fire). Every key press (handled or not) first re-shows the controls and resets the auto-hide timer.

| macOS key | Proposed Windows key | Action | Live | On-demand | OSD flash |
| --- | --- | --- | --- | --- | --- |
| Space | Space | Play/pause. If at end of file, resume seeks to 0. | yes | yes | — |
| ← / → | ← / → | Seek −10 / +10 s (`relative+exact`) | no-op (consumed) | yes | "−10s" / "+10s" |
| ⇧← / ⇧→ | Shift+← / → | Seek −60 / +60 s | no-op | yes | "−60s" / "+60s" |
| ↑ / ↓ | ↑ / ↓ | Previous / next item in queue | wraps | stops at ends | — |
| Page Up / Page Down | PgUp / PgDn | Same as ↑ / ↓ | wraps | stops | — |
| N / ⇧N | N / Shift+N | Next / previous | wraps | stops | — |
| ⌫ (Delete) | Backspace | Last watched channel (toggles between two) | yes | no-op | — |
| L | L | Toggle channel / up-next list (only if queue has > 1 item) | yes | yes | — |
| [ / ] | [ / ] | Slower / faster through speed presets | no-op | yes | "Speed 1.25×" |
| \ | \ | Normal speed | no-op | yes | "Speed 1×" |
| − / = (also +) | − / = / + (and numpad) | Volume down / up in steps of 5 | yes | yes | "Volume 85%" |
| M | M | Mute toggle | yes | yes | "Muted" / "Sound on" |
| C | C | Subtitles on/off (restores last used track, else first) | yes | yes | "Subtitles on" / "Subtitles off" / "No subtitles in this stream" |
| F (or double-click video) | F, F11, double-click | Full screen toggle | yes | yes | — |
| — | P | Picture in picture toggle (Windows addition, §4.13a) | yes | yes | — |
| I | I | Stats overlay toggle | yes | yes | — |
| S | S (avoid Ctrl+S) | Screenshot | yes | yes | "Screenshot saved to Desktop" / "Couldn't save the screenshot" |
| ? | ? (Shift+/) and F1 | Keyboard Shortcuts sheet | yes | yes | — |
| Esc | Esc | Close list panel; else close stats; else leave player | yes | yes | — |

Mouse: single click on the video toggles pause and shows the controls; double-click toggles full screen; any mouse movement shows the controls.

While the list panel's search field has focus, every key except Esc goes to the text field (no player shortcuts).

There is **no numeric channel entry** (typing a channel number) and no media-key support in the macOS app. Channel numbers are displayed only. See Open questions.

TV-remote behaviour details (`PlaybackSession`):

- Live queues wrap around at both ends; on-demand queues stop at the ends.
- Step changes (↑ ↓ PgUp PgDn N ⇧N, and autoplay) are **debounced by 350 ms**: the current stream is stopped immediately (freeing the single provider connection), the tuning card shows the target channel, the EPG clears, and the new stream only connects once the user stops pressing for 350 ms. Each press cancels the pending load.
- Last channel (⌫) and picking from the list panel start immediately (no debounce).
- "Previous index" is updated whenever the current index changes, so ⌫ flips between the last two channels. Starting a new queue clears it.
- Changing item via step uses the saved resume point of the target (on-demand).

### 3.2 App-wide and browse shortcuts

| macOS | Proposed Windows | Action | Enabled when |
| --- | --- | --- | --- |
| ⌘1 / ⌘2 / ⌘3 | Ctrl+1 / 2 / 3 | Live TV / Movies / Series | Series disabled for M3U. Note: not disabled while the player is open (changes the section underneath). |
| ⌃⌘1 / 2 / 3 | Ctrl+Shift+1 / 2 / 3 (or Ctrl+Alt) | Grid / List / Compact layout | always |
| ⇥ / ⇧⇥ | Tab / Shift+Tab | Next / previous column (sidebar → categories → content) | browse |
| → / Return | → / Enter | Into the next column; open/play in content | browse |
| ← / Esc | ← / Esc | Back to the previous column; Esc leaves a detail page | browse |
| ↑ / ↓ | ↑ / ↓ | Move within a column; ↑ at the top reaches the filter/search field | browse |
| ⌘[ | Alt+←, Mouse Back (XButton1), Backspace (when no text field focused) | Back (pop detail) | detail shown, player not presented |
| ⌘F | Ctrl+F | Focus search | player not presented. (macOS 14: no-op because the API is missing.) |
| ⌘R | Ctrl+R and F5 | Refresh playlist | a playlist is active |
| ⇧⌘N | Ctrl+Shift+N (or Ctrl+N) | Add playlist | always |
| ⌘L | Ctrl+L | Lock categories again | something is unlocked |
| ⌘, | Ctrl+, | Settings | always |
| ⌃⌘F | F11 | Full screen (system) | always |
| ⌘/ | Ctrl+/ and F1 | Keyboard Shortcuts | always |

The macOS `ShortcutCatalog` (single source of truth for the sheet) lists exactly the player and app rows above. The README says "Press `?` in the app" but `?` only works in the player; in browse it is ⌘/.

### 3.3 Menu bar (macOS) → Windows

| macOS menu | Items |
| --- | --- |
| File (after New) | Add Playlist… ⇧⌘N; Refresh Playlist ⌘R (disabled with no active playlist) |
| View (after toolbar items) | as Grid ⌃⌘1, as List ⌃⌘2, as Compact ⌃⌘3 (checkmarked toggles), divider, system Enter Full Screen |
| Browse (custom) | Live TV ⌘1, Movies ⌘2, Series ⌘3; divider; Back ⌘[; Search ⌘F; divider; Lock Categories Again ⌘L |
| Help (replaced) | Keyboard Shortcuts ⌘/ |
| App menu | Settings… ⌘,; Quit ⌘Q |

Windows has no global menu bar. Suggested: no classic menu; expose the same commands via `KeyBinding`s on the main window, toolbar buttons, a "…" overflow / app menu button in the title bar (Add Playlist, Refresh, Layout submenu, Lock Categories Again, Settings, Keyboard Shortcuts, About), and tooltips that show the shortcut.

### 3.4 Focus and key routing (PaneKeyRouter)

The router intercepts key-down events in the main window only when: no sheet is open, the player is not presented, and no ⌘/⌃/⌥ modifier is held (Shift allowed, for ⇧⇥).

**When a text field has focus:**

| Field | Key | Result |
| --- | --- | --- |
| Category filter | ↓, Return, Enter, Esc | Focus category list |
| Category filter | ← with caret at position 0 and no selection | Focus sidebar |
| Category filter | → with caret at end and no selection | Focus content |
| Toolbar search | ↓ | Focus content (results) |
| Toolbar search | Return, Esc | Left to the search field (Esc clears it natively) |
| Either | anything else | Normal text editing |

**Pane rules** (`PaneNavigation.action`; keys Tab, Shift+Tab, →, ←, Return/Enter, Esc; ↑/↓ always pass through to the column):

| Pane | Tab | Shift+Tab | → | ← | Return | Esc |
| --- | --- | --- | --- | --- | --- | --- |
| Sidebar | categories | content | categories | (pass) | categories | (pass) |
| Categories | content | sidebar | content | sidebar | content | sidebar |
| Content | sidebar | categories | (pass: grid/list handles) | (pass: grid/list handles; leaves to categories at the edge) | (pass: opens item) | detail shown → back (then focus content); else categories |

Special case: in the category column, ↑ while the selected filter is "All" moves focus up into the "Filter categories" field.

Which pane has focus is determined from the focused element's ancestor column; if that can't be determined (grid tiles), the last pane that reported focus is used. Each column reports when it gains focus. Moving focus is requested via the model; if the target column ignores programmatic focus, the router falls back to focusing that column's list control directly on the next dispatcher tick.

Windows implementation note: use a `PreviewKeyDown` handler on the main window with the same table, `FocusManager`/`Keyboard.Focus` for pane focus, `KeyboardNavigation.TabNavigation="None"` on the columns so Tab is fully owned by the router, and `TextBox.CaretIndex/SelectionLength` for the caret-edge rules.

---

## 4. Player

### 4.1 Presentation and lifecycle

- The player is an overlay covering the whole main window (ignores safe areas; window toolbar hidden). It appears with opacity + scale-from-0.985 over 0.28 s ease-in-out.
- Start: any play action builds a queue and sets "player presented". On appear the player clears keyboard focus from any browse text field, installs its key handler and the back-swipe handler, and shows the controls.
- Close (Esc, chevron-down button, back swipe): animate presented → false (0.28 s) **with video still running**; after the fade, if nothing new started in the meantime, stop the session, leave full screen only if the player itself entered it, and return keyboard focus to the content column (restoring the last focused tile/row).
- One video surface instance lives for the whole app lifetime so the GPU/mpv render context survives the player closing and reopening.
- A load issued before the render context exists is held and started once the context is ready (newer requests replace a waiting one). Without this, mpv drops video for the whole file and plays sound over black (CHANGELOG 1.0.2 fix).
- While a file is loaded and not paused, display sleep is prevented. Windows: `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED)` and clear it on pause/stop.

### 4.2 Queue building

- Queue = the list the item was picked from, windowed to **500 items** centred on the selection (shifted to stay in bounds), so playing from a 65k "All" view doesn't build 65k URLs. Items with no playable URL are skipped without shifting the selection.
- Queue title (list panel header): the content filter title for channels and movies (e.g. category name, "Favorites", "Results for “x”"), the series name for episodes, "Continue Watching" for resumes.
- Live item subtitle = category name. Episode item title = series name, subtitle = "S<s> E<e> · <episode title>". Movie item: title only.
- Live URLs use the "Live stream format" setting (section 6). Streams are checked against the URL scheme allowlist (http, https, rtmp, rtmps, rtsp, rtsps, rtp, udp, with a non-empty host) right before loading; failure shows "This stream uses an address type Retune doesn't play."

### 4.3 mpv configuration

**Global options, set before `mpv_initialize`:**

| Option | Value | Why |
| --- | --- | --- |
| `vo` | `libmpv` | Render API; app owns the surface |
| `hwdec` | `auto-safe` | |
| `deinterlace` | `auto` | Only acts on frames flagged interlaced (1.0.2) |
| `keep-open` | `yes` | Stay on the last frame at EOF |
| `idle` | `yes` | |
| `input-default-bindings` | `no` | App handles all input |
| `input-vo-keyboard` | `no` | |
| `osd-level` | `0` | App draws its own OSD |
| `load-scripts` | `no` | Security (hostile URLs) |
| `ytdl` | `no` | Security |
| `load-unsafe-playlists` | `no` | Security |
| `volume` | stored volume (default 100) | |
| `volume-max` | `100` | No amplification (1.0.2) |
| `cache` | `yes` | |
| `demuxer-max-bytes` | `512MiB` | |
| `demuxer-readahead-secs` | `120` | |
| `network-timeout` | `20` | seconds |
| `demuxer-lavf-analyzeduration` | `3` | Faster start; 2 s found audio on all probed TS channels |
| `user-agent` | `Retune/1.0 (Macintosh; libmpv)` | Windows: `Retune/1.0 (Windows; libmpv)` |
| `sub-auto` | `fuzzy` | |
| `slang`, `alang` | system preferred language codes + `en,eng` (comma-separated) | |
| `sub-font-size` | `40` | |
| `sub-border-size` | `2.5` | |
| `sub-scale`, `sub-pos`, `sub-border-style`, `sub-ass-override` | from saved subtitle style (4.7) | |
| `ao` | `null` | Only in debug `--probe` runs |

**Per-load properties (`StreamProfile`, set before every `loadfile` because mpv keeps them across files):**

| Property | Live | On-demand |
| --- | --- | --- |
| `stream-lavf-o` | `""` (no FFmpeg reconnect) | `reconnect=1,reconnect_delay_max=5` (no `reconnect_streamed`, which would restart range-less servers from byte 0) |
| `demuxer-max-back-bytes` | `0` | `128MiB` |
| `demuxer-seekable-cache` | `no` | `auto` |
| `speed` | forced to `1` | unchanged (keeps the viewer's speed) |
| `start` | `none` | resume seconds formatted `%.0f`, else `none` |

Then `loadfile <url> replace` and `pause=no`. Never enable FFmpeg reconnect for live: it splices the new connection into the running demuxer and replays old video when the server resumes behind the live edge.

**Commands used:** `loadfile url replace`, `stop`, `seek <s> relative+exact` (keys, buttons), `seek <s> absolute+keyframes` (scrubber, clamped ≥ 0). All commands are async (`mpv_command_async`).

**Observed properties:** `pause`, `time-pos`, `duration`, `demuxer-cache-duration`, `paused-for-cache`, `seeking`, `eof-reached`, `volume`, `mute`, `speed`, `track-list`, `sid`, `aid`, `video-params/w`, `video-params/h`.

**Events:** START_FILE → buffering; FILE_LOADED → loaded, refresh tracks; PLAYBACK_RESTART → first frame on screen (`hasVideoFrame`), not buffering, reset reconnect counter; END_FILE with reason error → error path (4.10); `eof-reached` true/false → end-of-file / left-end callbacks.

Buffering = `paused-for-cache` or `seeking` true, or between START_FILE and FILE_LOADED/PLAYBACK_RESTART.

"No audio" = file loaded, at least one video track and zero audio tracks.

### 4.4 Overlays and layout

Layer order (bottom → top): black background; video; status overlay (error / tuning card / buffering spinner); top and bottom bars; list panel; stats panel; up-next card; OSD flash.

**Auto-hide:** controls show on any mouse movement, click or key; they hide after **3 s** unless paused, the list panel is open or the subtitle panel is open. While paused they are always shown. When they hide, the cursor hides until the mouse moves. Fade 0.2 s.

**Top bar** (padding h24, top 30, bottom 40; background gradient black 75% → clear, top to bottom; white content):

- Live: channel logo 64×40 fitted (160 px).
- Line 1 (spacing 8): red "LIVE" pill (caption2 heavy, radius 4) for live; title (title3 semibold, 1 line); quality badge ("4K", "1440p", "1080p", "720p", "SD"; caption2 bold, 1 pt white-70% outline, radius 4) once the video size is known; orange "No audio in this stream" label with speaker-slash icon (tooltip "The provider is sending this channel without a sound track.").
- Line 2: when EPG is available: "Now: <title> · <start> – <end>" (callout) and "Next: <title> at <time>" (caption, white 70%). Otherwise the item subtitle (callout, white 80%).
- Right: favourite star (Windows addition; only while the item can be a favourite: the channel, the movie, or an episode's series), filled yellow when it is a favourite, tooltip "Add to Favorites" / "Remove from Favorites", flash "Added to Favorites" / "Removed from Favorites"; then the list button (live: always, even for a one-channel list such as a single favourite; on demand: only if queue > 1; tooltip "Channel list (L)" for live, "Up next (L)" otherwise), chevron-down close button (tooltip "Back to browsing (Esc)").

Quality thresholds: 4K if w ≥ 3200 or h ≥ 2000; 1440p if w ≥ 2200 or h ≥ 1400; 1080p if w ≥ 1700 or h ≥ 1000; 720p if w ≥ 1200 or h ≥ 700; else SD.

**Bottom bar** (glass/material panel, radius 18, forced dark, inner padding h18 v14, outer padding 24 left/right/bottom; spacing 10):

- Scrubber (on-demand and seekable only): elapsed "m:ss"/"h:mm:ss" (min width 56, right-aligned); track 6 pt high in an 18 pt hit area: background capsule white 20%, buffered capsule white 35% up to (position + demuxer cache)/duration, played capsule accent, 14 pt white knob with small shadow; hover shows a time tooltip above the pointer (caption monospaced, black 75%, radius 5, clamped inside the track); dragging previews and seeks on release; remaining "−h:mm:ss" (min width 64). Font callout monospaced digits, white 90%.
- Controls row (spacing 6), identical layout for live and on-demand so nothing shifts:
  - Previous (tooltip "Previous (↑)"; disabled when no previous and not live).
  - −10 s (on-demand only; tooltip "Back 10 seconds (←)"; disabled when not seekable).
  - Play/Pause (larger glyph, 30 wide; tooltip "Play (Space)"/"Pause (Space)").
  - +10 s (on-demand only; "Forward 10 seconds (→)").
  - Next ("Next (↓)"; disabled when no next and not live).
  - Volume control (leading padding 8): mute button (glyph speaker-slash if muted or 0, one wave if < 50, three waves otherwise; tooltip "Mute (M)"), slider 100 pt wide 0–100 (white tint, small), percent text (caption monospaced, white 70%, 34 wide).
  - Spacer.
  - Readout "1080p · 25 fps" (11 pt monospaced medium, white 50%): quality label and source fps (else rendered fps), from the 1 s stats poll.
  - Audio track menu (only if > 1 audio track): waveform glyph; checkable list of tracks.
  - Subtitles button (only if subtitle tracks exist): filled captions glyph when on; opens the subtitle panel as a popover above; tooltip "Subtitles (C turns them on/off)".
  - Speed menu (on-demand only): gauge glyph; checkable list "0.5×, 0.75×, Normal, 1.25×, 1.5×, 1.75×, 2×, 3×"; tooltip "Playback speed ([ slower · ] faster · \ normal)".
  - Stats toggle (info glyph, filled when on; "Stats for nerds (I)").
  - Full screen ("Full screen (F)").
- Icon button style: title3 medium glyphs, min hit area 34×34, pressed opacity 0.6.

**Status overlay** (centred, one of):

1. Error panel (4.10).
2. Tuning card while no frame of the current item has shown yet.
3. Buffering spinner (large, white, in a black-35% circle, padding 18), only when buffering after the first frame and only if it lasts more than **250 ms** (avoids flicker on quick seeks). Fade 0.2 s.

**Tuning card** (covers the gap between choosing and first frame; cross-fades 0.18 s as the user zaps so the screen never flashes black): black 88% full-screen backdrop; centred stack (spacing 16, padding 32):

- Live: logo fitted in 180×112 with 14 pt padding on white 8%, radius 16 (320 px; fallback TV glyph 40 pt white 40%).
- Episode: still 260×146; movie: poster 140×210; radius 12, shadow black 50% r20 y8 (480 px).
- Title (title2 semibold, 2 lines, centred), subtitle (callout, white 65%).
- Small spinner + status (callout, white 70%, numeric transition): "Tuning…" (live), "Loading…" (on-demand), or "Reconnecting… (n/4)" during automatic retries.

**OSD flash**: centred text (title2 semibold, monospaced digits) in a glass panel (padding h18 v10, radius 14), shown **1 s**, fade 0.2 s; a newer flash replaces the older.

**Stats panel** (I): top-left (leading 24, top 120), 12 pt monospaced white on black 72%, radius 12, padding 14; two-column grid, labels white 60%, missing values "—". Polled every **1 s** for as long as the player is open (the same poll feeds the readout).

| Row | Source (mpv property) | Format |
| --- | --- | --- |
| Resolution | `video-params/w`, `/h` | "1920×1080 (1080p)" |
| Video | `video-codec`, `video-params/pixelformat`, HDR if `video-params/gamma` ∈ {pq, hlg} | joined with " · " |
| Decoder | `hwdec-current` | "Hardware (<name>)" or "Software (CPU)" when "no" |
| Frame rate | `container-fps`, `estimated-vf-fps` | "%.3f source · %.2f rendered" or "%.3f source" |
| Dropped | `frame-drop-count`, `decoder-frame-drop-count` | "N output · M decoder" |
| Video bitrate | `video-bitrate` | "4.2 Mbps" (≥ 1,000,000 bps, 1 decimal) or "640 kbps" |
| Audio | `audio-codec-name`, `audio-params/hr-channels`, `audio-params/samplerate`, `audio-bitrate` | "aac · stereo · 48 kHz · 128 kbps" |
| Buffer | `demuxer-cache-duration` | "%.1f s ahead" |
| Network | `cache-speed` (bytes/s × 8) | bitrate format |
| A/V sync | `avsync` | "%+.0f ms" |

### 4.5 List panel (queue drawer)

Toggled by L or the top-bar list button; only available when the queue has more than one item. Slides in from the right with opacity (0.25 s). Right-aligned, width 340, padding 16, glass panel radius 18 forced dark, vertical margin 90, trailing margin 24.

- Header: queue title, else current item's subtitle, else "Up Next" (headline, white, 1 line).
- Search field: "Find channel" (live) or "Find"; filters by the same accent-insensitive term matching on "<label> <title>". While it has focus, player shortcuts are suspended except Esc.
- Rows (spacing 2, padding 8, radius 8): thumbnail (live logo 44×28 fitted, 100 px, fallback TV glyph; episode 72×40 radius 5, 180 px; movie 32×48 radius 4, 120 px), label (episodes: "S1 E2 · Title"; others: title; 2 lines, white), progress bar (max 120 wide, accent) for on-demand items with progress > 1%. Current row: accent 45% background and a speaker glyph.
- On open, scrolls the current item to the centre. Clicking a row jumps to it (immediate, saving the current progress, starting from the target's resume point) and closes the panel.
- Esc closes the panel (first priority).

### 4.6 Audio tracks

Menu lists all audio tracks as checkable items; selecting sets `aid`. Display name rules (`MediaTrack.displayName`): localised language name from the track's `lang` (else the code uppercased); then the track title if not already included; else "Track <id>"; joined with " · ". Detail suffix in parentheses: codec uppercased, and channel layout from `demux-channel-count` (6 → "5.1", 8 → "7.1", else "<n>ch"). Example: "English · Commentary (AAC, 5.1)".

### 4.7 Subtitles

**Subtitle panel** (popover, width 340, padding 16, spacing 14):

- Radio group: "Off" + every subtitle track (display name as above). Selecting sets `sid` (or `no`).
- Divider.
- Grid:
  - "Size": slider 0.5–2.5 step 0.1, readout "<percent>%" (44 wide). Maps to `sub-scale`.
  - "Position": slider inverted so dragging right moves subtitles up; stored value 50–100 (100 = bottom), step 1; readout "Bottom" at 100, else "↑ <100 − value>". Maps to `sub-pos`.
  - "Delay": stepper ±0.1 s, label "%+.1f s". Maps to `sub-delay`. **Per file**: reset to 0 on every load.
- Toggle "Background box": `sub-border-style` = `background-box` (on) / `outline-and-shadow` (off). Default off. (Requires a libmpv recent enough to support `background-box`.)
- Toggle "Keep original styling": `sub-ass-override` = `scale` (on) / `strip` (off). Default off, because many SRT rips carry inline tags like `<font size="80">`. Tooltip "Use fonts and sizes embedded in styled (ASS) subtitles. Turn off if subtitles look huge or odd."
- Footer: "C turns subtitles on/off" (caption, secondary) and "Reset" (restores the default style and delay 0).

Style defaults: scale 1.0, position 100, background off, keep original off. Values are clamped (scale rounded to 0.1 within 0.5–2.5; position rounded within 50–100), persisted globally as JSON under `player.subtitleStyle`, and applied live to mpv on every change.

C toggle: if a subtitle track is selected, remember it and turn subtitles off; otherwise select the remembered track, else the first one; if there are none, flash "No subtitles in this stream".

### 4.8 Speed

Presets: 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 3. `[`/`]` step from the first preset ≥ current − 0.01 (fallback 1), clamped to the ends. `\` resets to 1. On-demand only; live always loads at speed 1. On-demand files keep the viewer's speed across files (mpv keeps it).

### 4.9 Volume

- Scale 0–100 (integer, never amplified; `volume-max=100`). Persisted on every change as `player.volume` (default 100) and applied at startup.
- Keys step to the next multiple of 5 (so a slider value of 94 goes to 95 / 90, not 99 / 89).
- Setting a volume > 0 while muted unmutes. Mute itself is not persisted.

### 4.10 Errors and reconnection

**Automatic reconnect** (`ReconnectPolicy`), applies to live and on-demand:

- Backoff delays before retry 1–4: **1 s, 2 s, 4 s, 6 s**. After 4 failed retries the error is shown.
- During a retry wait the current stream is stopped and the tuning card shows "Reconnecting… (n/4)".
- The counter resets when a frame appears (PLAYBACK_RESTART), when a new item starts, and on manual "Try Again".
- Triggers:
  - mpv END_FILE with reason error. Message: "This stream couldn't be played (<mpv error string>). The channel may be offline or your connection limit reached."
  - Live end of file (the feed dropped): treated as failure "The live stream stopped."
  - On-demand "early end": FFmpeg reports a dropped connection as a normal end. If duration > 0 and more than **60 s** remain, it's a dropped connection ("The connection dropped."), unless a previous early end was recorded and playback didn't get at least **5 s** further, in which case the end is accepted as real.
- Resume point for retries: on-demand → current position if a frame has shown and position > 0, else the originally requested start; live → none (always fresh from the live edge).

**Error panel** (after retries are exhausted, or immediately for the URL policy and engine-start errors): yellow warning triangle 40 pt, message (centred, max width 420), buttons "Try Again" (prominent; reloads the current item at its resume point) and "Next Channel" (live with more than one item in the queue). White content on a glass panel, radius 20, padding 28. Other messages: "The video engine failed to start." (mpv init failure).

### 4.11 Progress, resume and autoplay

- Progress is reported every **5 s** while playing, and as "final" on stop, item switch and end of file. Non-final reports reach the stored library at most every **30 s** per item. Nothing is saved for live items, before the first frame (avoids overwriting a real resume point after a failed start), or when duration ≤ 0.
- When a live channel begins, it's recorded as most recent in Recently Watched.
- Library writes are debounced **1 s**.
- Resume starts at saved position **− 5 s** (min 0) when resumable.
- **Next-episode autoplay**: when an episode reaches its real end, the next queue item is an episode of the same series, and the setting is on, an **8-second countdown** starts:
  - "Up Next" card bottom-right (trailing 28, bottom 120), width 380, padding 14, glass radius 16, forced dark: thumbnail 120×68 radius 8 (300 px), "Up next in <n>s" (caption, white 70%), next item's subtitle or title (headline, 2 lines), buttons "Play Now" (prominent) and "Cancel" (small).
  - Ticks once per second; at 0 it plays next (debounced step).
  - Cancelled by Cancel, by seeking back / replaying (leaving EOF), by starting any other item, and by stopping.
  - Movies never autoplay, and a mixed Continue Watching queue never rolls into a different show.
- At end without autoplay the last frame stays (keep-open). Pressing play seeks to 0.

### 4.12 EPG (live)

For Xtream channels the short EPG (limit 4) is fetched when the channel begins. "Now" = entry whose start ≤ now < end; "Next" = first entry starting at or after Now's end (or the first future entry when no Now). It's refetched when the current programme ends (wait clamped to **60 s – 6 h**). Cleared during zapping. M3U channels show no EPG.

### 4.13 Full screen

- F, the bottom-bar button, or double-clicking the video toggles the window's full screen.
- The player remembers whether it entered full screen; on close it leaves full screen only in that case.
- Windows: borderless, maximised over the monitor (incl. taskbar) via `WindowStyle=None` + `WindowState=Maximized` (or `AppWindow` full-screen presenter); restore previous bounds/state on exit. Also allow F11 and Esc-to-exit-player semantics as above.
- Windows 11 puts the 1-pixel window border back when the window is activated again (Alt+Tab) or the theme changes, so the borderless edge is re-applied after each of those.

### 4.13a Picture in picture (Windows addition)

- P, the bottom-bar button (tooltip "Picture in picture (P)") or the right-click menu shrinks the main window into a 480×270 (16:9, DPI-scaled) always-on-top mini player in the bottom-right corner of its screen's work area (16 px margin, at most half the screen's width). The one video window moves with it, so playback continues untouched. Full screen is left first and comes back afterwards.
- The mini player shows only its own chrome (auto-hiding like the rest): title, "Back to the full player (P)", "Stop and close", and previous / play-pause / next. Panels (list, stats, shortcuts) close. Drag anywhere to move it; a click pauses; a double-click (or P, Esc, F) goes back. Clicks don't activate the window, so the app the user switched to keeps the keyboard.
- **Switching apps**: with the setting "Picture in picture when switching apps" (on by default; in Settings and as a checkable item in the player's right-click menu) the mini player opens when Retune loses focus while something plays (not when paused, ended, failed, or minimised by the user). Coming back to Retune (Alt+Tab, taskbar) closes a mini player opened this way; one opened by hand stays.
- Closing the player from the mini player while another app is in front restores the window's layout and minimises it, so it doesn't land on top of that app.

### 4.14 Screenshots

- S saves the frame currently on screen (requires a frame to have shown, else "Couldn't save the screenshot").
- macOS captures the **rendered framebuffer** of the video layer on the next draw (mpv's `screenshot` command can't read hardware-decoded frames via the render API there). Consequently the image is at the video surface's pixel size, video only, without app overlays. A **2 s** timeout cancels the capture if no frame is drawn (e.g. display asleep).
- Saved as PNG to the **Desktop**, name `Retune yyyy-MM-dd at HH.mm.ss.SSS.png` (POSIX locale, local time zone). Built from the time only, because titles are untrusted. Never overwrites an existing file.
- Windows: try mpv `screenshot-to-file <path> video` first (works with copy-back hwdec and often with d3d11va); fall back to reading back the render target. Save location: see Open questions (Desktop vs Pictures\Screenshots vs Pictures\Retune).

---

## 5. Image loading

`ImagePipeline` (singleton actor) + `RemoteImage` view:

- **Memory cache** of decoded, downsampled bitmaps: max **600** entries, max **300 MiB** total cost (cost = width × height × 4). Key = `<url>#<maxPixelSize>`.
- **Disk/HTTP cache**: 32 MiB memory, **1 GiB** disk, at `Caches/Retune/Artwork`. Policy "return cached data else load" (ignores expiry). Windows: `%LOCALAPPDATA%\Retune\Cache\Artwork` with your own content-addressed file cache (hash of URL) since `HttpClient` has no persistent cache.
- **Network**: request timeout 20 s, resource timeout 60 s, max **8 connections per host**, body limit **32 MiB**, only `http`/`https` URLs. Plain HTTP is allowed (most artwork hosts are HTTP).
- **In-flight de-duplication** per key. Work runs at utility/background priority.
- **Negative cache** for the session: URLs that returned 404, 410, or 200 with undecodable data are not retried. Timeouts and other errors are retried on next request.
- **Downsampling at decode** to the requested max pixel size (longest side), honouring EXIF orientation. Windows: `BitmapImage.DecodePixelWidth/Height` or WIC scaler, frozen bitmaps, decode off the UI thread.
- **View**: shows placeholder until loaded; fades the image in over 0.2 s; reloads when the URL changes and ignores stale results.
- **Clear** (Settings → Privacy): empties memory, negative cache and disk cache.

Requested sizes (px, longest side):

| Use | Size |
| --- | --- |
| Poster card | 420 |
| Logo tile | 300 |
| Landscape card | 600 |
| List: poster / logo / landscape | 140 / 120 / 200 |
| Detail backdrop / poster | 1600 / 600 |
| Episode still | 480 |
| Player top-bar logo | 160 |
| Tuning card: live / on-demand | 320 / 480 |
| List panel: live / episode / movie | 100 / 180 / 120 |
| Up Next card | 300 |

**Placeholder** (`ArtworkPlaceholder`): diagonal gradient whose hue is derived from a stable hash of the title (`seed = fold(seed*31 + scalar) & 0xFFFF` over Unicode scalars; hue = seed mod 360; colours HSB(hue, 0.45, 0.45) → HSB(hue + 0.08, 0.55, 0.28)), with either the initials (first letter of the first two alphanumeric words, uppercased; 28 pt bold rounded, white 90%) or a glyph (26 pt medium, white 85%) when a symbol is given (TV for channels, play-rectangle for episodes). Logo slots in lists use a plain tertiary TV glyph instead.

Virtualisation is essential: only realised tiles request images. Windows: `VirtualizingWrapPanel`-style grid (e.g. custom or community) with recycling, cancel/ignore loads for recycled containers.

---

## 6. Settings

Separate window, tabbed, width 520.

### 6.1 Playback tab (grouped form)

| Setting | Options | Default | Storage key |
| --- | --- | --- | --- |
| Live stream format | Automatic · MPEG-TS (lower latency) · HLS (more resilient) | Automatic | `player.liveFormat` (`automatic`/`ts`/`hls`) |
| (help text) | "Try HLS if a channel keeps freezing; MPEG-TS starts faster." | | |
| Play next episode automatically | toggle | on | `player.autoplayNext` |

Automatic resolves to `ts` when the account's allowed output formats are unknown/empty or include `ts`, otherwise `m3u8`. Applies to Xtream live URLs built at play time (M3U channels use their own URL).

### 6.2 Privacy tab (grouped form)

- **Parental PIN**
  - PIN set: green "PIN is set" (shield-check icon), buttons "Change…" and "Remove…".
  - Not set: "Lock categories by right-clicking them in the category list." (secondary) and "Set PIN…".
- **Hidden categories**: rows for every hidden category of the **active playlist** across all three sections (section icon, trimmed name, "Show" button). Unknown ids appear as "Category <id>". Empty: "Right-click a category and choose Hide to tidy up the list."
- **History**
  - "Continue Watching and Recently Watched for this playlist" with destructive "Clear…" → confirmation "Clear watch history?", message "Favorites and watched marks are kept.", button "Clear History".
  - "Cached posters and logos" with "Clear" → becomes a disabled "Cleared" for the rest of the window's life.

### 6.3 Other persisted UI state (not shown in Settings)

| Key | Meaning | Default |
| --- | --- | --- |
| `activePlaylist` | active playlist UUID | first playlist |
| `layout.live`, `layout.movies`, `layout.series` | layout per section | list, grid, grid |
| `layout.tileSize` | small/medium/large | medium |
| `player.volume` | 0–100 | 100 |
| `player.subtitleStyle` | JSON {scale, position, background, keepsOriginalStyle} | 1, 100, false, false |

Windows: store in `%APPDATA%\Retune\settings.json` (atomic write), not the registry.

### 6.4 Shortcuts help screen

See 2.9.

---

## 7. Parental PIN UI flow

PIN rules: 4–8 digits. Locks are per playlist + section + category. Unlocks last for the app session only.

| Flow | Trigger | Steps |
| --- | --- | --- |
| Lock a category | Category context menu "Lock with PIN" | If no PIN exists: "Create a PIN" sheet, then the lock is applied on success. If a PIN exists: locked immediately, no prompt. Locking also re-locks it if it was unlocked. |
| Remove a lock | "Remove Lock…" | Verify sheet, message "Enter your PIN to remove this lock." |
| Open a locked category | Select it | Content area shows the Locked view (below). |
| Re-lock everything | ⌘L / "Lock Categories Again" | Clears all session unlocks. Disabled when nothing is unlocked. |
| Set PIN | Settings "Set PIN…" | Create sheet (in the Settings window). |
| Change PIN | Settings "Change…" | Verify "Enter your current PIN." → then the Create sheet. |
| Remove PIN | Settings "Remove…" | Verify "Enter your PIN. Removing it also removes every category lock." → removes the PIN, all locks and all unlocks. |

**PIN sheet** (width 380, padding 24, spacing 14): title with shield-lock icon ("Create a PIN" / "Enter PIN", title3 semibold); message (callout, secondary; create: "Locked categories need this 4–8 digit PIN to open. Keep it somewhere safe."; verify: the reason text); masked "PIN" field; "Confirm PIN" field (create only); red error text; "Cancel" (Esc) and "Continue" (Return, prominent, disabled while PIN is empty). Errors: "Use 4 to 8 digits.", "The PINs don't match.", "Wrong PIN." (verify; clears the field). On success the sheet dismisses first and the follow-up action runs on the next tick (so "Change" can present a second sheet). PIN prompts started from Settings appear in the Settings window, others in the main window.

**Locked view** (in place of content, centred, spacing 16): lock glyph 44 pt secondary; "This category is locked" (title2 semibold); "Enter your PIN to view it. It locks again when Retune quits, or with ⌘L." (secondary; Windows: Ctrl+L); masked PIN field 140 wide (auto-focused) with "Unlock" (prominent, disabled when empty; Return submits). Wrong PIN: field cleared, red "Wrong PIN", and the field+button row shakes horizontally (offsets 10, −8, 6, −4, 0 pt, 0.06 s each). Switching category resets the field and error.

Leak prevention: locked categories hide their count badge, and their items are excluded from All, Favorites, Search, Recently Watched and Continue Watching until unlocked. There is no attempt limit or lockout. A successful verify upgrades a legacy PIN hash (data layer).

---

## 8. Mac-specific behaviours and Windows equivalents

| macOS behaviour | Where | Windows equivalent |
| --- | --- | --- |
| Trackpad back swipe (two-finger "swipe between pages" when enabled, three-finger swipe). Only claims the back direction, honours natural scrolling, only active when back is possible. Browse: pops a detail page. Player: closes the player. | `SwipeBackMonitor` | Mouse XButton1 (Back), Alt+←, Backspace (no text focus). Optional: precision-touchpad horizontal pan via `ManipulationDelta`/pointer input, back-only, when back is possible. |
| Global menu bar with File/View/Browse/Help | `RetuneCommands` | Window-level `KeyBinding`s + toolbar + overflow app menu (3.3). |
| Settings as a separate standard window (⌘,) | `Settings` scene | Separate Fluent window or an in-app Settings page/dialog; Ctrl+,. Keep PIN prompts owned by whichever window started them. |
| Sheets attached to the window | editors, PIN, shortcuts | `ContentDialog`-style modal overlays (WPF Fluent / WPF-UI) or owned modal windows. |
| Liquid Glass (macOS 26) / regular material panels | player bars, panels, OSD | Acrylic/Mica-like translucent dark panels (blur may be unavailable over the video surface: use a semi-opaque dark fill ~70–80% with a subtle border). |
| Native full screen (separate Space), ⌃⌘F | player, View menu | Borderless maximised window (4.13), F11. |
| `NSCursor.setHiddenUntilMouseMoves` | player auto-hide | `Cursor = Cursors.None` over the video until the next mouse move. |
| Display-sleep assertion during playback | `MPVPlayer` | `SetThreadExecutionState`. |
| Toolbar search field with system focus (⌘F, macOS 15+) | Root | Search box in the title/command bar, Ctrl+F focuses it. |
| Right-click context menus | everywhere | Same, plus Shift+F10 / Menu key on the focused item. |
| `.help` tooltips | buttons | `ToolTip` (include the shortcut). |
| Hover effects (poster lift, play buttons, list hover icons) | browse | Same with `IsMouseOver` triggers; ensure keyboard-focused items get an equivalent visible state. |
| SF Symbols | all icons | Segoe Fluent Icons (`Segoe Fluent Icons` font) equivalents. |
| ⌘-based shortcuts, ⌫ "Delete" key | | Ctrl-based shortcuts; Backspace. |
| Keychain password + macOS keychain popups | data layer | Windows Credential Manager / DPAPI (data spec). Drop the "Mac password popup" documentation. |
| Screenshots to `~/Desktop` | player | Desktop or Pictures (Open questions). |
| Artwork cache in `~/Library/Caches/Retune/Artwork` | images | `%LOCALAPPDATA%\Retune\Cache\Artwork`. |
| App quits when the main window closes | AppDelegate | Default for a single-window WPF app (`ShutdownMode=OnMainWindowClose`). |
| Gatekeeper "Open Anyway" for unnotarised builds | README | SmartScreen "More info → Run anyway" for unsigned builds. |
| Copy "on this Mac" | Welcome, editor | "on this PC". |
| Video via CAOpenGLLayer + libmpv OpenGL render API on a Core Animation render thread | `MPVVideoView` | See Windows adaptation notes. |
| `ProcessInfo` OSLog debug logging (`keyboard`, `dev` categories) | Support | `ILogger`/`EventSource` or a debug log file. |

---

## 9. Dev and debug tooling

All compiled only into debug builds (`#if DEBUG`), started from the main window's first appearance. They log to OSLog subsystem `io.github.0xaliraza.retune`, category `dev`, and **never log stream URLs, usernames, passwords or titles** (except the resume probe's position number). `CFFIXED_USER_HOME` can redirect user data for test runs (does not isolate the Keychain).

| Flag | What it does |
| --- | --- |
| `--play <URL> [--live]` | Plays that URL directly as a single movie (or live item with `--live`) in the player. Logs at t+2, 4, 6, 8 s: position, frame shown, output fps, dropped and decoder-dropped frames, error. Used to verify rendering changes without clicking through the UI. |
| `--bench [--bench-loop]` | Waits for the catalog, logs "catalog ready <ms> after window appeared", waits 2 s, then switches sections (movies, series, live, movies, series, live; or live/movies × 8 with `--bench-loop`), logging per switch the elapsed time and main-thread busy time (polls the main queue until it answers within 20 ms, minimum 50 ms), 1 s between switches. |
| `--probe refresh\|live\|movie\|episode\|resume [--probe-index N] [--probe-seconds S] [--probe-screenshot]` | Runs a stored playlist through the real code path unattended. Waits up to 180 s for the catalog (1800 × 100 ms), logs counts, then: `refresh` runs a refresh and logs catalog age and channel count; `live`/`movie`/`episode` play the N-th (default 0) item from family-safe categories only (category name matches `news|kids|children|cartoons?|animation|family`, and neither category nor item name matches `adult|xxx|porn|18+|erotic|sex`); `resume` resumes the N-th Continue Watching entry. Logs one health line per second for S seconds (default 30): pos, dur, audio output, window visible, frame, buffering, cache seconds, network KB/s, fps, size, hardware decoding, dropped, reconnect attempts, error. With `--probe-screenshot` takes a screenshot at t = 3 s. Uses `ao=null` so it can't reach the speakers. Quits when done or on failure. |
| `--screens <folder>` | Waits for the catalog, then for each section waits 3 s and writes `live.png`, `movies.png`, `series.png` of the main window; opens the Edit Playlist sheet and writes `playlist-form.png` (captures the sheet if open); quits. Used for the README screenshots with demo data. |

Also: `PaneKeyRouter` logs debug lines (category `keyboard`) for each routed key and fallback focus.

Windows suggestions: the same flags in Debug configuration only (`#if DEBUG`); log via `ILogger` to a debug file under the data folder plus `Debug.WriteLine`; `ao=null` for probes; `--screens` via `RenderTargetBitmap` of the window/dialog; an environment variable such as `RETUNE_DATA_DIR` to isolate user data (and a test credential store target name).

---

## Windows adaptation notes

1. **Video surface and airspace.** WPF can't draw over a native child HWND. Options, in rough order of fidelity:
   - libmpv render API to a D3D11 texture shared into WPF via `D3DImage` (D3D9Ex interop) or, simpler, OpenGL render API on an offscreen FBO with `WGL_NV_DX_interop2` into a D3D surface for `D3DImage`. Overlays are then ordinary WPF elements. Matches macOS architecture (app-owned surface, frame readback for screenshots, render-context-ready gate).
   - mpv `wid` embedding in an `HwndHost` (vo=gpu/gpu-next, d3d11) with the player chrome in a separate owned, transparent, topmost-to-owner overlay window that tracks position/size. Simplest and best hwdec support, but overlays/popups need care and frame readback differs.
   Whichever is chosen, keep: one surface for the app lifetime; hold loads until the surface is ready; release the render context before `mpv_terminate_destroy`; black background; vsync pacing.
2. **hwdec on Windows.** `auto-safe` picks d3d11va/nvdec/dxva2 depending on the VO. With the OpenGL render API, zero-copy needs the DX interop; otherwise prefer `auto-copy-safe`. Expose decoder name in stats as on macOS.
3. **mpv options to keep verbatim**: all security options (`load-scripts=no`, `ytdl=no`, `load-unsafe-playlists=no`), the StreamProfile per-load properties, cache/network values, subtitle defaults. Add `input-cursor=no`, `cursor-autohide=no` and `input-vo-keyboard=no` if using `wid`. Change `user-agent` platform token.
4. **Keyboard.** Implement player keys in a window `PreviewKeyDown` while the player is shown (skip when Ctrl is held so app shortcuts still work; suspend when the list panel search has focus, except Esc). Implement PaneKeyRouter as a separate `PreviewKeyDown` stage active only when no dialog is open and the player is hidden. Map ⌘ → Ctrl. Avoid stealing Alt (menu access keys) and Windows-reserved combos.
5. **Virtualisation.** Lists and grids must virtualise and recycle containers (88k items). Use `VirtualizingStackPanel` with `VirtualizationMode=Recycling` for list/compact, and a virtualising wrap/uniform-grid panel for grid. Compute grid columns with the same formula so ↑/↓ move by a row.
6. **Focus visuals.** Replace the default dotted focus rectangle with the accent 3 pt ring (grid) and accent 28% row tint (lists); keep "remember last focused item" per collection.
7. **Theming.** WPF .NET 9+ Fluent theme (`ThemeMode`), follow system light/dark and accent. Player chrome is always dark regardless of theme.
8. **Animations.** Keep the timing constants (player 0.28 s, controls 0.2 s, list panel 0.25 s, tuning crossfade 0.18 s, image fade 0.2 s, hover 0.15 s, shake 5 × 0.06 s). Respect "Show animations" (`SystemParameters.ClientAreaAnimation`) to reduce motion.
9. **Timers.** Use `DispatcherTimer`/cancellable tasks for: zap debounce 350 ms, controls hide 3 s, spinner delay 250 ms, flash 1 s, stats poll 1 s, progress tick 5 s (commit ≥ 30 s), library save debounce 1 s, autoplay 8 × 1 s, reconnect 1/2/4/6 s, screenshot timeout 2 s, EPG refresh 60 s–6 h, catalog stale 12 h.
10. **Copy changes.** "Mac" → "PC", key names (⌘ → Ctrl, ⌫ → Backspace), "Screenshot saved to Desktop" text should match the chosen folder.
11. **Accessibility.** macOS lacked full VoiceOver labels. Give every icon-only button an `AutomationProperties.Name` (reuse tooltip text) from the start.
12. **Window chrome.** Put the search box and toolbar in the title bar area (Fluent style). Hide them while the player is shown and extend the player into the title bar.

---

## Open questions

1. **Video rendering approach** on WPF: render API + `D3DImage` interop vs `wid` + overlay window (affects overlays, screenshots, hwdec, fullscreen).
2. **Screenshot folder**: keep Desktop (parity) or use `Pictures\Screenshots` / `Pictures\Retune`? Should the flash text name the folder, and should clicking it open Explorer?
3. **Screenshot resolution**: macOS saves at surface size (not source resolution, no overlays). Keep, or switch to mpv `screenshot-to-file … video` at source resolution?
4. **Numeric channel entry** (type "123" to tune): absent on macOS but natural for "TV-remote" zapping on Windows. Add (with an OSD showing digits and a ~1.5 s commit timeout)?
5. **Media keys / SMTC** (play/pause, next/previous, now-playing in the Windows media flyout): listed as "not here yet" on macOS. Include in v1?
6. **Playlist delete confirmation**: macOS deletes immediately from the context menu. Add a confirmation dialog on Windows?
7. **⌘1–3 while the player is open** changes the browse section underneath. Keep, or disable while playing?
8. **Shortcut choices** needing sign-off: layouts Ctrl+Shift+1–3 vs Ctrl+Alt+1–3; Add Playlist Ctrl+N vs Ctrl+Shift+N; Back Alt+← / XButton1 / Backspace; Help F1 in addition to Ctrl+/ and `?`.
9. **`?` outside the player**: README claims it works app-wide; macOS only handles it in the player. Make it app-wide on Windows (when no text field is focused)?
10. **PIN brute force**: no attempt limit exists. Add a short lockout after N wrong attempts?
11. **Settings window vs in-app page**: separate window (parity) or a Settings page/dialog in the main window? (PIN sheet ownership depends on it.)
12. **Picture in Picture / mini player** (not on macOS): decided 2026-11-09, built as §4.13a.
13. **Grid favourite icon** inconsistency (heart on posters, star in lists and channel tiles): keep or unify on Windows?
14. **Trackpad back gesture**: worth implementing precision-touchpad horizontal swipe, or rely on mouse Back button and Alt+←?
15. **Single instance**: macOS has one main window by design. Enforce single instance on Windows (second launch activates the first) via a named mutex?
16. **libmpv build** to bundle on Windows (shinchiro / zhongfly builds vs own build), minimum version for `sub-border-style=background-box` and `deinterlace=auto`.
