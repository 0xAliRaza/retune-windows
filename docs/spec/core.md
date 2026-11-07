# Retune core: functional spec (data and logic layer)

Source of truth: the macOS app `retune-macos` (v1.0.2). Scope is `Sources/RetuneCore/*`, plus the state and persistence logic in `Sources/Retune/AppModel.swift`, `Support/PlayerPreferences.swift` and `Support/Formatting.swift`. The spec describes behaviour, not Swift code. Where macOS-specific technology is involved (Keychain, UserDefaults, POSIX permissions), the behaviour is described and a Windows equivalent is suggested in *italics*.

Conventions:
- "trim" means remove leading and trailing whitespace. Where the original trims only spaces and tabs and not newlines, it says so.
- "Playlist UUID" is written in canonical uppercase form (`XXXXXXXX-XXXX-...`), which is what Swift's `uuidString` produces. Every library key embeds it, so keep the casing consistent.
- Constants are given exactly as in the source.

---

## 0. Non-negotiable product rules (from AGENTS.md / SECURITY.md)

These are requirements, not implementation notes:

1. Treat everything from a playlist, provider, artwork host or stream as hostile.
2. Every stream URL must pass the **Stream URL policy** (section 2.9), both when it is parsed and again right before it goes to the player.
3. Every network body is read with a **hard size cap**: 256 MiB for API and playlist downloads, 32 MiB for artwork.
4. API requests use a client with **no HTTP cache, no cookie store and no persisted cookies**, because Xtream puts the password in the URL.
5. Xtream passwords live **only in the OS secret store**. Watch history stores credential-free *locators*, never stream URLs.
6. Never log URLs, usernames or passwords.
7. Data files are written **atomically** and kept **private to the user** (macOS uses folders `0700` and files `0600`). *Windows: put them under the user profile and restrict the ACL to the current user.*
8. No analytics, telemetry, crash reporting, update checks, or any network destination other than the user's provider and the artwork hosts their playlist names.
9. mpv runs with `load-scripts=no`, `ytdl=no` and `load-unsafe-playlists=no`.
10. Live streams must never use FFmpeg `reconnect_streamed`. Live recovery is done by the app reloading the stream (see section 9.1).
11. Resume progress is saved only after the first video frame has been shown. Otherwise a failed start would overwrite a real resume point.

---

## 1. Data model

### 1.1 Enumerations

| Name | Values | Notes |
| --- | --- | --- |
| ContentKind | `live`, `movie`, `series`, `episode` | Raw strings are used inside keys and persisted JSON. |
| LibrarySection (UI section) | `live` ("Live TV"), `movies` ("Movies"), `series` ("Series") | Maps to ContentKind `live`, `movie` and `series`. |
| LayoutMode | `grid` ("Grid"), `list` ("List"), `compact` ("Compact") | Stored per section. |
| TileSize | `small`, `medium`, `large` | Minimum grid column width: 112, 150 and 210 points. Default `medium`. |
| BrowseSort | `provider` ("Default"), `title` ("Title"), `recentlyAdded` ("Recently Added"), `rating` ("Rating") | |
| CategoryFilter | `all`, `favorites`, `continueWatching`, `recent`, `category(id)` | The sidebar selection. |
| LoadStatus | `idle`, `loading(message)`, `failed(message)`, `ready` | |
| LiveFormat (preference) | `automatic` ("Automatic"), `ts` ("MPEG-TS (lower latency)"), `hls` ("HLS (more resilient)") | |

### 1.2 Playlist (a user-configured source)

| Field | Type | Notes |
| --- | --- | --- |
| id | UUID | Generated when the playlist is created and never changed. |
| name | string | Defaults when blank: Xtream uses the server host, or "Xtream". M3U URL uses the URL host, or "Playlist". M3U file uses the file name without its extension. |
| source | one of `xtream(server: URL, username: string)` or `m3u(url: URL)` | For an M3U file the URL is a local file URL or path. The Xtream **password is not part of the playlist**. It lives in the secret store, keyed by the playlist UUID. |
| createdAt | timestamp | Kept when the playlist is edited. |

Derived values:
- **kindLabel**: "Xtream Codes", "M3U File" (local file), or "M3U URL".
- **supportsSeries**: true only for Xtream. When the active playlist is M3U and the Series section is selected, the app switches to Live.

### 1.3 XtreamCredentials

`server` (normalised base URL), `username`, `password`. See section 2.1 for normalisation and link parsing.

### 1.4 MediaCategory

`id` (string), `name` (string). Ids are the provider's `category_id` for Xtream and the group title for M3U. Live, VOD and series category ids **can overlap**, so a category is always identified together with its section.

### 1.5 Channel (live)

| Field | Type | Xtream source | M3U source |
| --- | --- | --- | --- |
| id | string | `stream_id` | hash of the URL (section 3.4) |
| number | int? | `num` | 1-based running index among live entries |
| name | string | `name`, or "Channel <id>" | the EXTINF title |
| logo | URL? | `stream_icon` | `tvg-logo` |
| categoryID | string? | `category_id` | group title, or "Uncategorized" |
| epgChannelID | string? | `epg_channel_id` | `tvg-id` (raw, can be an empty string) |
| directURL | URL? | null (built from credentials) | the entry URL |
| hasArchive | bool | `tv_archive` | false |

For browse sorting, a channel has no date and no rating.

### 1.6 Movie (VOD)

| Field | Type | Xtream | M3U |
| --- | --- | --- | --- |
| id | string | `stream_id` | URL hash |
| name | string | `name`, or "Untitled" | the EXTINF title |
| poster | URL? | `stream_icon` | `tvg-logo` |
| categoryID | string? | `category_id` | group title, or "Uncategorized" |
| rating | double? (0–10) | normalised from `rating` and `rating_5based` (section 2.6) | null |
| added | timestamp? | `added` (unix seconds) | null |
| containerExtension | string? | `container_extension` | the URL path extension, or null when empty |
| directURL | URL? | null | the entry URL |

Sorting uses `added` as the date and `rating` as the rating.

### 1.7 Series

`id` (`series_id`), `name` (default "Untitled"), `cover` (`cover`), `backdrop` (first URL of `backdrop_path`), `categoryID`, `plot`, `genre`, `releaseDate` (string, from `releaseDate` or else `release_date`), `rating` (0–10, normalised), `lastModified` (`last_modified`, unix seconds). Sorting uses `lastModified` as the date and `rating` as the rating. Series exist only for Xtream.

### 1.8 MediaInfo (detail metadata for a movie or series)

All fields are optional: `plot`, `cast`, `director`, `genre`, `releaseDate` (string), `durationSeconds` (int), `rating` (0–10), `poster`, `backdrop`, `youtubeTrailer` (string).
- **year**: the first 4 characters of `releaseDate`, but only if `releaseDate` has at least 4 characters and those 4 parse as an integer. Otherwise null. Example: "2025-06-20" gives "2025".
- The detail screen falls back to `MediaInfo(rating: movie.rating)` and the list artwork while details are missing.

### 1.9 Season, Episode, SeriesDetail

- **Episode**: `id` (string), `season` (int), `number` (int), `title`, `containerExtension?`, `plot?`, `image?`, `durationSeconds?`, `directURL?` (always null for Xtream).
- **Season**: `number` (int, which is also its id), `name`, `cover?`, `episodes` (sorted by number).
- **SeriesDetail**: `info` (MediaInfo) and `seasons` (sorted by number ascending).
- Series details are fetched each time a series is opened. They are **not cached**.

### 1.10 EPGEntry (Now/Next)

`title`, `summary?`, `start`, `end`.
- id: "<start unix seconds>-<title>".
- `isLive(t)` is true when `start <= t < end`.
- `progress(t)` = clamp((t − start) / (end − start), 0, 1). It is 0 when the length is 0 or negative.
- **Now** is the first entry (sorted by start) that is live now.
- **Next**: if there is a Now entry, the first entry whose `start >= Now.end`. If there is none, the first entry whose `start > now`.

### 1.11 AccountInfo (from Xtream authentication)

`status` (string, default "Unknown"), `message?`, `expiresAt?`, `isTrial` (bool), `activeConnections?`, `maxConnections?`, `allowedFormats` (string list), `serverTimezone?`.
- `isActive`: status equals "Active", ignoring case.
- The UI shows the status (green when active, orange otherwise), "Expires <date>" and "N connection(s) allowed".
- `allowedFormats` drives the automatic live format (section 9.3).
- AccountInfo is held in memory only and never persisted. It is refreshed each time a playlist is opened.

### 1.12 Catalog (cached per playlist)

`fetchedAt`, `liveCategories`, `vodCategories`, `seriesCategories`, `channels`, `movies`, `series`. Each list keeps provider order.
- `isEmpty`: channels, movies and series are all empty.
- `isStale(maxAge)`: `now − fetchedAt > maxAge` (strictly greater). The app uses maxAge = **12 h** (43,200 s).

### 1.13 PlaybackItem (what the player and the history store)

`id`, `kind`, `title`, `subtitle?`, `url`, `artwork?`, `streamID?` (live channels, used for EPG), `parentID?` (episodes: the series id).

How items are built:
- **Live**: id is the channel id. Title is the channel name. Subtitle is the channel's category name (trimmed). URL is `directURL`, or the Xtream live URL in the resolved live format. Artwork is the logo. streamID is the channel id. If no URL can be built, the channel is unplayable and skipped.
- **Movie**: id, name and poster. URL is the Xtream movie URL, or `directURL`. With neither, it is unplayable.
- **Episode** (Xtream only): id is the episode id. **Title is the series name.** Subtitle is "S{season} E{number} · {episode title}". URL is the Xtream episode URL. Artwork is the episode image, or else the series cover. parentID is the series id.

### 1.14 WatchProgress (resume point)

`item` (a credential-free PlaybackItem), `position` (s), `duration` (s), `updatedAt`.
- `fraction` = clamp(position / duration, 0, 1). It is 0 when duration ≤ 0.
- `isFinished` = duration > 0 AND (position ≥ 0.95 × duration OR duration − position < **90 s**).
- `isResumable` = position ≥ **30 s** AND NOT isFinished.
- `remaining` = max(duration − position, 0).
- `resumePosition` = if resumable, max(position − **5 s**, 0). Otherwise none.

### 1.15 LibraryState (single persisted document, all playlists)

| Field | Type | Meaning |
| --- | --- | --- |
| favorites | set of content keys | Favourited channels, movies and series |
| progress | map content key → WatchProgress | Resume points and watched marks (movies and episodes) |
| recentChannels | ordered list of content keys | Most recent first, capped at 30 |
| hiddenCategories | set of category keys | |
| lockedCategories | set of category keys | Parental locks |
| pinnedCategories | ordered list of category keys | Order of pinning |
| pinHash | string? | Parental PIN hash. Null means no PIN is set. |

Limits: `recentChannelLimit = 30`, `progressLimit = 2000`, `maxPlausibleDuration = 604,800 s` (7 days).

### 1.16 Keys

- **Playlist prefix**: `"<UUID>/"`
- **Content key**: `"<UUID>/<kind>/<id>"`, for example `.../live/42`, `.../movie/7`, `.../series/9` or `.../episode/11`. Recent channels use kind `live` with the item's `streamID ?? id`.
- **Category key**: `"<UUID>/category/<kind>/<categoryId>"`, where kind is the section's ContentKind (`live`, `movie` or `series`). Example: `<uuid>/category/live/104`.
- **Category prefix**: `"<UUID>/category/<kind>/"`. Category ids for a section are recovered by filtering keys with this prefix and stripping it.
- Every key starts with the playlist prefix, so deleting a playlist can remove its entries with a prefix match.
- If no playlist is active, the original builds keys with a random UUID, so the keys match nothing. Windows should simply no-op.

### 1.17 Session-only state (never persisted)

- `unlockedCategories`: category keys unlocked with the PIN this session.
- `account`: the latest AccountInfo.
- Browse state: section, filter, search text, sort, the detail navigation stack.
- `lastProgressCommit`: a map from key to the time progress was last committed (section 5.3).
- Player queue, current index, previous index (for "last channel"), EPG list, reconnect counters.

---

## 2. Xtream Codes

### 2.1 Server input handling

**normalizeServer(raw)** returns a base URL or null:
1. Trim whitespace and newlines. Reject the input if it is empty or **contains a space character**.
2. If it doesn't start with `http://` or `https://` (case-insensitive), prepend `http://`. A bare `host:port` therefore means plain HTTP.
3. Parse it as a URL. It must have a non-empty host.
4. Drop the query and the fragment.
5. Strip each of these path suffixes, in order and case-insensitively: `/player_api.php`, `/get.php`, `/xmltv.php`.
6. Remove all trailing `/` characters from the path. Keep the port and any other path prefix.

| Input | Result |
| --- | --- |
| `iptv.example:8080` | `http://iptv.example:8080` |
| `http://iptv.example:8080/` | `http://iptv.example:8080` |
| `https://host.example/player_api.php` | `https://host.example` |
| `http://host.example:80/get.php?username=a&password=b` | `http://host.example:80` (port kept) |
| `"  host.example  "` | `http://host.example` |
| `""` and `"not a host"` | null |

**usesPlainHTTP(raw)**: true when normalizeServer(raw) has the scheme `http`. This covers a bare host. Empty input gives false. The Add/Edit form shows a warning whenever this is true, for the Xtream server field and for the M3U URL field: "This address uses plain HTTP, so your login travels unencrypted. Use an https:// address if your provider offers one." The app never probes whether HTTPS is available.

**parse(fromPlaylistURL)**: takes a link such as `http://h:8080/get.php?username=u&password=p&type=m3u_plus` and extracts credentials. The first `username` and first `password` query items must both exist and be non-empty. The server is normalizeServer(link). Otherwise the result is null.
- Pasting such a link into the Xtream server field **autofills** the server, username and password.
- In the M3U URL field, if the link parses this way, the form suggests "This is an Xtream link — switching to Xtream Codes gives you series, posters and EPG." It offers a "Use Xtream Codes Instead" button, which switches the type and copies the link into the server field.

### 2.2 Add/Edit validation and the auth flow

The form is valid when:
- **Xtream**: normalizeServer succeeds, the username is non-empty, and the password is non-empty. On edit, the password may be left empty, which means unchanged.
- **M3U URL**: the lenient URL parse succeeds (section 2.7). This means only `http` and `https` M3U URLs can be added.
- **M3U file**: a file was chosen (`.m3u`, `.m3u8` or plain text).

Saving:
- **Xtream**:
  1. Normalise the server. If that fails, show "That server address doesn't look right. Use something like http://example.com:8080."
  2. Trim spaces from the username. The secret is the typed password, or the stored one when the field was left empty on edit.
  3. **Call `authenticate()` first.** Any error is shown inline and nothing is saved.
  4. On success, store the password in the secret store (account = playlist UUID), then save the playlist. A secret-store error is shown and the playlist isn't saved.
- **M3U**: saved immediately, with no download or validation first. Load errors show up later as the load status.
- **Adding** a playlist appends it, saves, and makes it active, which triggers a load.
- **Editing** replaces it in place and saves. If it is the active playlist, the load generation is bumped (so a refresh still using the old details is discarded) and a refresh starts.

### 2.3 Endpoints

All requests are `GET <server>/player_api.php?<query>`. The path is appended to the server URL, so any path prefix is kept.

The query is built in this order: `username`, `password`, then `action` (if any), then the extra parameters **sorted by key**. Each value is percent-encoded, leaving only alphanumerics and `-._~` unencoded. So `+` becomes `%2B`, `&` becomes `%26`, `=` becomes `%3D`, `/` becomes `%2F` and a space becomes `%20`. This matters because PHP reads a raw `+` as a space. Keys are not encoded.

Example: `http://h.example:8080/player_api.php?username=u&password=p%20w&action=get_series_info&series_id=5`

| Purpose | action | Extra params | Response shape |
| --- | --- | --- | --- |
| Authenticate / account info | *(none)* | — | object with `user_info` and `server_info` |
| Live categories | `get_live_categories` | — | array of categories |
| VOD categories | `get_vod_categories` | — | array of categories |
| Series categories | `get_series_categories` | — | array of categories |
| Live streams | `get_live_streams` | — | array of streams |
| VOD streams | `get_vod_streams` | — | array of streams |
| Series list | `get_series` | — | array of series |
| Movie details | `get_vod_info` | `vod_id=<id>` | object with `info` |
| Series details | `get_series_info` | `series_id=<id>` | object with `info`, `seasons` and `episodes` |
| Short EPG | `get_short_epg` | `stream_id=<id>`, `limit=4` | object with `epg_listings` |

No category or stream filtering parameter (such as `category_id`) is ever sent. Full lists are fetched.

### 2.4 Full catalog download (refresh)

1. Fetch the three category lists **in parallel** (they are small, so a bad account fails fast). If any of them fails, the whole refresh fails.
2. Fetch the three stream lists (live, VOD, series) **in parallel**. Any failure fails the refresh.
3. Build a Catalog with `fetchedAt = now`. An Xtream catalog may be entirely empty without counting as an error.

### 2.5 Transport, timeouts, retries and limits

HTTP client settings: no cache, no cookies, **request (idle) timeout 45 s, total resource timeout 600 s, at most 4 connections per host**. The body is streamed and the request is aborted with `responseTooLarge` once it passes **256 MiB** (`256 << 20` = 268,435,456 bytes).

Retry loop, used for every Xtream API call:
- At most **3 attempts**. Before attempt *n* (n = 1 or 2), wait **700 × n ms**, so 700 ms and then 1,400 ms.
- A missing HTTP status is treated as 200.
- Status 200 with a non-empty body: success. For the auth call (no action), any 200 is a success, even with an empty body.
- Status 200 with an **empty body** on an action call: record `emptyResponse` and retry. Throttling panels answer bursts with empty bodies.
- Status **222, 429 or ≥ 500**: record `http(status)` and retry.
- Any other non-200 status, such as 401, 403 or 404: throw `http(status)` immediately.
- A transport **timeout** or **connection lost**: record the error and retry. Any other transport error, such as DNS failure, refused connection, or `responseTooLarge`, is thrown immediately.
- When the attempts run out, throw the last recorded error.

Decoding, after the fetch succeeds:
- **List endpoints** (categories and streams):
  - An empty body throws `emptyResponse`. An empty body is never treated as an empty list, which would overwrite a good cache with nothing.
  - A body that is **exactly** the two bytes `{}` means an empty list.
  - Otherwise the body must be a JSON array. If it isn't, throw `unreadableResponse`.
  - Each element is decoded on its own. Elements that fail, or that lack their id, are **skipped** and don't fail the list.
- **Object endpoints** (vod_info, series_info, short_epg): an empty body throws `emptyResponse`, and an undecodable body throws `unreadableResponse`.
- **Auth**: an undecodable body throws `unreadableResponse`. If `user_info` is missing or `user_info.auth` (lenient int) is not 1, throw `invalidCredentials`. If the status is not "Active" (ignoring case), throw `accountInactive(status)`.

When authenticate runs:
- In the Add/Edit form, as validation (section 2.2).
- After every playlist load. It runs after the cached catalog is shown and after any refresh, only for Xtream, and only if the load generation hasn't changed. Errors are ignored and `account` is set to the result or null.

### 2.6 Field mapping and lenient decoding

**Lenient primitives.** These never throw. A missing key, a null, or the wrong type gives null.

| Helper | Accepts | Rules |
| --- | --- | --- |
| lenientString | string, int, double, bool | Tried in that order. An int becomes its decimal string. A double becomes its default string form (`3.5` gives "3.5"). A bool becomes "1" or "0". |
| lenientText | as above | Trims whitespace and newlines. An empty result is null. **Almost every text field uses this, so `""` and `"   "` become null.** |
| lenientInt | int, double, numeric string | Tries int, then double truncated toward zero, then string. The string is trimmed of spaces only, parsed as an int, then as a double and truncated. A double must be finite and strictly between Int64.min and Int64.max, so `NaN`, `inf`, `"nan"`, `"inf"` and `1e300` all give null. Bools are not accepted. |
| lenientDouble | double, int, numeric string | Strings are trimmed of spaces. Non-finite values give null. |
| lenientBool | bool, or anything lenientInt accepts | A JSON bool is used as-is. Otherwise the result is `lenientInt != 0`, so `"1"` and `1` are true, and `"0"`, `0`, null, missing and `"yes"` are false. |
| lenientDate | lenientDouble unix seconds | Valid only when **0 < value < 253,402,300,800** (year 10000). Otherwise null. `"0"` and `1e21` are null. |
| lenientURL | string | Lenient URL parse (section 2.7). |
| lenientFirstURL | an array of strings, or a string | For an array, the first element that parses as a lenient URL. Otherwise lenientURL. |
| Failable row | any | A row that fails to decode becomes null and is dropped. Neighbouring rows survive. |

**Rating normalisation (tenPoint)**: if `rating` is in (0, 10], use it. Otherwise, if `rating_5based` is in (0, 5], use it × 2. Otherwise null. A 0 is treated as "no rating".

| rating | rating_5based | Result |
| --- | --- | --- |
| `""` | `"3.5"` | 7.0 |
| `"7.2"` | `0` | 7.2 |
| `"0"` | `4.1` | 8.2 |
| `"0"` | `"0"` | null |

**Category row**: `category_id` (text, **required**, or the row is dropped), `category_name` (text, default "Untitled"). Names are trimmed by lenientText, and the UI trims spaces again when showing them.

**Live row** (`get_live_streams`): `stream_id` (text, required), `num` (int), `name` (default "Channel <id>"), `stream_icon` (URL), `category_id` (text), `epg_channel_id` (text, null if empty), `tv_archive` (bool). `stream_type` and other fields are ignored.

**VOD row** (`get_vod_streams`): `stream_id` (required), `name` (default "Untitled"), `stream_icon` (poster), `category_id`, `rating` and `rating_5based` (tenPoint), `added` (date), `container_extension` (text).

**Series row** (`get_series`): `series_id` (required), `name` (default "Untitled"), `cover` (URL), `backdrop_path` (first URL, string or array), `category_id`, `plot`, `genre`, `releaseDate` or else `release_date`, `rating` and `rating_5based`, `last_modified` (date).

**Info object** (the `info` key of `get_vod_info` and `get_series_info`):
- plot = `plot`, or else `description`
- cast = `cast`, or else `actors`
- director = `director`
- genre = `genre`
- releaseDate = `releasedate`, or `releaseDate`, or `release_date`
- durationSeconds = `duration_secs` (int), or else `episode_run_time` (minutes) × 60. **If the multiplication overflows, the result is null.**
- rating = tenPoint(`rating`, `rating_5based`)
- poster = `movie_image`, or `cover_big`, or `cover`
- backdrop = first URL of `backdrop_path`
- youtubeTrailer = `youtube_trailer` (text)

**get_vod_info**: if `info` is not a decodable object (panels send `"info": []` when there is no metadata), the result is an empty MediaInfo. `movie_data` is ignored. A movie-info failure in the app gives null: the detail screen just shows list data and no error.

**get_series_info**:
- `info`: decoded as above. If it is not an object (for example `[]`), MediaInfo is empty.
- `seasons`: an array of failable rows. Each has `season_number` (int), `name` (text), and cover = `cover_tmdb`, or `cover_big`, or `cover`. A missing or invalid array gives an empty list. These rows are **metadata only**. A season appears only if it has episodes.
- `episodes`: two shapes are accepted.
  1. **An object keyed by season** (`{"1":[...],"2":[...]}`, the usual case). For each key, decode its array of failable episode rows. The season number is the key parsed as an int, or else the first episode's `season`, or else 0. Lists that resolve to the same number are concatenated.
  2. **Nested arrays** (`[[...],[...]]`). For each inner list at index *i*, the season number is the first episode's `season`, or else *i* + 1.
  3. Anything else gives no seasons.
- **Episode row**:
  - `id` (text, required).
  - `episode_num` (int, default 0). `season` (int, default 0).
  - `title` (default "Episode <n>").
  - `container_extension` (text).
  - `info`, a nested object with `plot`, `movie_image` (the image) and `duration_secs`. If `info` is not an object (for example `[]`), all three are null.
- **Assembly**: seasons are sorted ascending by number. Each episode's `season` is **overwritten** with the number of the group it sits in. Episodes are sorted ascending by `number`. The season name comes from the first metadata row with the same number, or else "Season N". The cover comes from that row too.
- Series-detail failure in the app: the error message is shown on the series page, with a way to retry.

**get_short_epg**: `epg_listings` is an array of failable rows.
- A row needs `start_timestamp` and `stop_timestamp` (lenient dates, number or string) and a `title`. Without all three, the row is dropped.
- `title` and `description` are **base64-decoded**. If the text is valid base64 and decodes to valid UTF-8, the decoded string is used, trimmed of whitespace and newlines. Otherwise the raw text is used.
- An empty description gives a null summary. The entries are then sorted by start ascending.

**Auth** (no action):
- `user_info.auth` must be 1 (lenient int).
- From `user_info`: status = `status` (default "Unknown"), `message`, expiresAt = `exp_date` (date), isTrial = `is_trial` (bool), activeConnections = `active_cons` (int), maxConnections = `max_connections` (int).
- allowedFormats = `allowed_output_formats`, which must be a JSON array of strings. Anything else gives an empty list.
- serverTimezone = `server_info.timezone`.

### 2.7 Lenient URL parse (artwork and M3U URL field)

1. Trim the input. Empty input gives null.
2. Replace every `\/` with `/` (escaped JSON slashes).
3. The text must start with `http://` or `https://`, case-insensitively. So `httpx://` and `ftp://` give null.
4. Parse it. If parsing fails, percent-encode the characters a URL query can't hold (spaces and so on) and parse again. If that fails too, the result is null.

### 2.8 Stream URL formats (Xtream)

`<base>/<kind>/<enc(username)>/<enc(password)>/<enc(file)>`

- `base` is the server URL as a string, with **one** trailing `/` removed.
- `kind` is `live`, `movie` or `series` (the episode kind is `series`).
- `file` is `<id>.<ext>`.
- **enc** percent-encodes everything outside the RFC 3986 path-allowed set, **including `/`**. So a space becomes `%20`, `/` becomes `%2F`, `?` becomes `%3F`, `#` becomes `%23` and `%` becomes `%25`. Characters such as `+`, `&`, `=`, `@`, `:` and `!` stay literal.

| Kind | Extension | Example (server `http://h.example:8080`, user `u`, password `p w`) |
| --- | --- | --- |
| Live | resolved live format: `ts` or `m3u8` (default `ts`) | `http://h.example:8080/live/u/p%20w/42.ts` |
| Movie | `containerExtension`, or else `mp4`. If the movie has a directURL, that is used instead. | `.../movie/u/p%20w/7.mkv` |
| Episode | `containerExtension`, or else `mp4`. If the episode has a directURL, that is used instead. | `.../series/u/p%20w/9.mp4` |

With user `u/1` and password `a/b`, the live URL is `http://h.example:8080/live/u%2F1/a%2Fb/7.ts`.

### 2.9 Stream URL policy (security)

- Allowed schemes (case-insensitive): `http`, `https`, `rtmp`, `rtmps`, `rtsp`, `rtsps`, `rtp`, `udp`. The URL must also have a **non-empty host**.
- Everything else is rejected, including `file:`, `av:`, `edl:`, `memory:`, `fd:`, `lavf:`, `javascript:`, bare paths like `/etc/passwd`, and look-alikes like `httpx:`.
- `udp://@239.0.0.1:1234` is accepted.
- The policy is applied when parsing M3U and again before every player load.
- Artwork has a stricter rule: only `http` and `https`.

### 2.10 Credential-free locators (watch history)

History must never contain a stream URL. It stores one of these instead:

- **Xtream locator** = `retune://<kind>/<file>`.
  - Built from the **percent-encoded** path of the real stream URL, split on `/` with empty segments dropped.
  - It needs at least 4 segments, and the segment 4th from the end must be `movie`, `series` or `live`. Then kind is that segment and file is the last segment, still percent-encoded.
  - Anything else gives no locator.
  - Example: `http://h:8080/movie/u/secret/9.mkv` gives `retune://movie/9.mkv`. A `/` in the password is encoded, so the segment count still works.
- **Opaque locator** (M3U, no account) = `retune://item/<id>`. The id is the URL hash, which never contains the password.
- **credentialFree(item, hasAccount)**:
  - If the URL already uses the `retune` scheme, keep the item as it is.
  - If hasAccount, use the Xtream locator. **If that fails, the item is not stored at all.** There is deliberately no fallback to the raw URL.
  - Otherwise, use the opaque locator.
- **resolve(locator)** with the current credentials: if the scheme is `retune` and there is a host, rebuild the stream URL as `streamURL(kind = host, file = last path component)`. Any other URL passes through unchanged.
- **playable(stored item)** in the app:
  - A non-locator URL is used as-is.
  - If an Xtream client exists, resolve the locator.
  - Otherwise (M3U), look the id up in the current catalog's **movies** and use its directURL. If it's not found, the item can't be played and is skipped.
- So history keeps working after the user changes their password or server.

---

## 3. M3U

### 3.1 Loading

- **Local file**: read the whole file. The original has no size cap; *add one on Windows*.
- **URL**: GET through the same client as Xtream (no cache, no cookies, 45 s / 600 s timeouts), capped at 256 MiB. A non-200 status throws `http(status)`. There are no retries.
- **Text decoding**: UTF-8. If that fails, ISO-8859-1. If that fails too, `unreadableResponse`.
- Parse the text, then build the catalog. If the catalog has no channels and no movies, throw `emptyPlaylist` ("No playable entries were found in this playlist.").

### 3.2 Line parsing

- Split on **any newline** (LF, CR, CRLF and the other Unicode line breaks). Trim spaces and tabs from each line. Skip empty lines.
- A line starting with `#EXTINF` (case-sensitive) sets the **pending info** to (title, attributes), replacing any earlier pending info.
- A line starting with `#EXTGRP:` sets the **pending group** to the rest of the line, trimmed.
- Any other line starting with `#` is ignored. That includes `#EXTM3U` and its `url-tvg` and `x-tvg-url` attributes: **external XMLTV EPG is not supported.**
- Any other line is a URL candidate.
  - If it parses as a URL **and** passes the Stream URL policy, emit an entry:
    - Attributes come from the pending info, or none.
    - If there is no `group-title` attribute and there is a pending group, set `group-title` to the pending group. An explicitly empty `group-title=""` blocks EXTGRP.
    - Title = the pending info's title, or `tvg-name`, or the URL's last path component. See open question Q5.
    - Then clear the pending info and the pending group.
  - If the candidate fails to parse or is rejected, it is skipped **and the pending info is kept**. So the info carries over to the next valid URL line (Q6).

### 3.3 EXTINF header and attributes

Format: `#EXTINF:-1 tvg-id="x" tvg-logo="y" group-title="News",Channel Name`.

- **Title split**: scan the line, toggling an in-quotes flag on every `"`. The title starts after the **first comma outside quotes**. The header is everything before that comma. If there is no such comma, the title is `""` and the header is the whole line. The title is trimmed of spaces.
  - Example: `#EXTINF:-1 tvg-name="A, B" group-title="G",Real, Title` gives the title `Real, Title` and `tvg-name` = `A, B`.
- **Attributes**: a single linear pass over the header bytes, equivalent to the regex `([A-Za-z0-9_-]+)="([^"]*)"` applied left to right without overlaps.
  - A key is a run of `A-Z a-z 0-9 _ -` immediately followed by `="`. The value runs up to the next `"`.
  - If there is no closing quote, scanning stops: no attribute can follow.
  - Keys are **lowercased**, so `TVG-Logo` is the same as `tvg-logo`. A later duplicate key overwrites an earlier one. Values are taken literally: no unescaping and no trimming.
  - Scanning resumes right after the closing quote, so `x-y_z9="v"tail="w"` yields both attributes.
  - `a = "spaced"` doesn't match, because of the spaces. `a=b` without quotes doesn't match.
  - It must be **linear time**. A regex engine that backtracks can hang on a 500,000-character line, and a test requires such a line to parse in under 2 s.

Attributes the app uses:

| Attribute | Use |
| --- | --- |
| `group-title` | Category. An empty value counts as missing. |
| `tvg-logo` | Logo or poster, through the lenient URL parse (http/https only). |
| `tvg-id` | Channel `epgChannelID`. It is stored but not used, because M3U has no EPG. |
| `tvg-name` | Title fallback only. In practice it is never reached (Q5). |

Everything else (`tvg-chno`, `catchup*`, `tvg-shift`, `#EXTVLCOPT` and so on) is ignored.

### 3.4 Building the catalog

- **Dedupe by URL**: only the first entry for an exact URL is kept. A test lists the same URL twice and expects only "One" to survive.
- **id** = lowercase hex of the **first 16 bytes of SHA-256(UTF-8 of the absolute URL string)**, which gives 32 hex characters. The id never contains the password, and it is stable across refreshes.
- **Group** = the `group-title` value, or "Uncategorized".
- **Live or on-demand.** An entry is a Movie if its URL path, lowercased, contains `/movie/` or `/series/`, **or** its path extension (lowercased) is one of `mkv`, `mp4`, `avi`, `mov`, `m4v` or `wmv`. Everything else is a Channel.
  - There are **no series** in M3U, and `/series/` links become movies.
  - Categories: id = name = group title. They are kept in **order of first appearance**, separately for live and for VOD. The series category list is empty.
  - Movie: poster = logo, `containerExtension` = path extension (null when empty), `directURL` = URL, no rating and no added date.
  - Channel: number = 1-based count among channels, `epgChannelID` = `tvg-id`, `directURL` = URL, `hasArchive` = false.

### 3.5 Sample (from tests)

```
#EXTM3U url-tvg="http://epg.example/xmltv.php"
#EXTINF:-1 tvg-id="bbc1.uk" tvg-name="BBC One" tvg-logo="http://logo/bbc.png" group-title="UK, Entertainment",BBC One HD
http://h.example:8080/live/u/p/1.ts
#EXTINF:-1 tvg-logo="http://logo/m.jpg" group-title="Movies",Some Movie (2024)
http://h.example:8080/movie/u/p/77.mkv
#EXTINF:-1,No Attributes Channel
#EXTGRP:Misc
https://cdn.example/stream.m3u8
#EXTINF:-1 group-title="Docs",Local File
http://cdn.example/files/doc.mp4
```

This gives 4 entries:
- Channels: "BBC One HD" and "No Attributes Channel". Live categories: "UK, Entertainment" and "Misc".
- Movies: "Some Movie (2024)" (extension mkv) and "Local File". VOD categories: "Movies" and "Docs".

---

## 4. Catalog, browse and search

### 4.1 Loading the active playlist (state machine)

Each playlist load increments a **load generation** counter. Every asynchronous step checks that the generation hasn't changed before it touches state. This prevents a stale refresh from overwriting a newer playlist, or from re-saving a deleted playlist's cache.

**At app start:**
1. Purge legacy OS network caches (section 8.4).
2. Tighten file permissions.
3. Migrate legacy secrets.
4. Load the playlists.
5. Load the library, then sanitise its stream URLs (section 8.5).
6. Set the active playlist to the stored `activePlaylist` id if it still exists, or else the first playlist, or none.

**Changing the active playlist:**
1. First, stop playback. The final progress save then lands on the old playlist.
2. Persist the new id.
3. Load the new playlist.

**Loading a playlist:**
1. Bump the generation and clear `isRefreshing`.
2. Reset browsing: clear the detail stack and the search, set sort to `provider`, and use the section's default filter.
3. Clear `account` and the loaded catalog. The previous playlist's content is never shown, or used for keys, while another loads.
4. If there is no active playlist, set the status to `idle`. Stop here.
5. If the section is Series and the playlist doesn't support series, switch the section to Live.
6. If the playlist is Xtream and there is **no stored password**, set the status to `failed(missingPassword)`. The cache is **not** shown, because nothing in it could play. Stop here.
7. Set the status to `loading("Opening <name>…")`.
8. Load the cached catalog. Try the fast parallel decode first; if that fails, use the normal store load, which backs up a corrupt file.
9. If a cache was found:
   - Build the indexes, then set loaded and the status to `ready`.
   - If the cache is stale (older than 12 h), refresh while the cached content stays visible.
10. If no cache was found, refresh.
11. Finally, for Xtream, authenticate and store the AccountInfo. Errors are ignored.

**refresh():**
- If a refresh for the current generation is already running, join it.
- A refresh left over from an older generation is cancelled.
- The menu "Refresh Playlist" (⌘R), the toolbar Refresh button and "Try Again" all call this.

**performRefresh:**
1. Set `isRefreshing`. If nothing is loaded yet, set the status to `loading("Downloading channels, movies and series…")`.
2. Fetch the catalog (Xtream or M3U). If the Xtream password is missing, that is a `missingPassword` error.
3. On success, if the generation hasn't changed:
   - Queue an atomic cache save on the serial file queue.
   - Build the indexes, then set loaded and the status to `ready`.
4. On failure, if the generation hasn't changed:
   - If cached content is showing, **keep it** silently.
   - Otherwise set the status to `failed(<error message>)`.
5. A refresh **replaces** the catalog completely. Nothing is merged.

**Deleting a playlist:**
1. If it is the active playlist, stop playback (before the library is cleared) and bump the generation.
2. Remove the playlist and save the list.
3. Remove its secret.
4. Queue deletion of its catalog cache on the serial file queue, so it runs after any pending save.
5. Remove all of its library entries (section 5.8) and schedule a library save.
6. If it was active, clear loaded and switch to the first remaining playlist.

### 4.2 Indexes (performance for 88k+ items)

For each list (channels, movies, series), build a BrowseIndex when the catalog loads. The three indexes are built **in parallel off the UI thread**. Each one holds:
- `items` in provider order.
- `normalizedNames[i]`: the search-normalised name (section 4.4), computed once per item.
- `byCategory`: a map from categoryID to the item offsets, in provider order. Items without a category aren't in it.
- `positionByID`: a map from id to offset. **The first occurrence wins** when ids repeat.

Operations:
- `count(category)` is the size of that category's offset list. Counts **ignore** hidden and locked exclusions, and every category row shows one.
- `item(id)` is an O(1) lookup.
- `select(category?, query, ids?, excludedCategories, sort)` runs these steps in order:
  1. Take the category's offsets, or all offsets when the category is null.
  2. If `ids` is given, keep only items whose id is in it.
  3. If the excluded set isn't empty, drop items whose categoryID is in it. **Items with a null category are never excluded.**
  4. If the query has terms, keep only items whose normalised name contains **every** term.
  5. Sort.

**Sorts:**
- `provider`: the order as filtered.
- `title`: ascending by normalised name, using plain code-point comparison (not locale collation).
- `recentlyAdded`: descending by date, with a missing date treated as the minimum.
- `rating`: descending by rating, with a missing rating treated as −1.
- Ties are not explicitly ordered. *Windows should use a stable sort.*
- Live offers only `provider` and `title`. Movies and Series offer all four.
- The sort resets to `provider` when the section changes or a playlist loads.

Other performance techniques in the original:
- **Parallel decoding of the cached catalog**: channels, movies and the rest (the categories, the series list and `fetchedAt`) are decoded concurrently from the same file. Measured gain: 445 ms → 345 ms on 80k+ items.
- **Queue windowing** (section 5.6).
- **Throttled progress commits** (section 5.3).
- **Debounced library saves** (section 8.3).
- **Artwork** is downsampled to display size, with a memory cache of 600 images / 300 MiB of decoded bitmaps and a disk HTTP cache of 1 GiB.

### 4.3 What the content area shows

Definitions:
- **isSearching**: the search text, trimmed of spaces, is non-empty.
- **effectiveCategory**: null while searching. Otherwise the selected `category(id)`, or null for any other filter.
- **restrictedIDs**: none while searching. For `favorites`, the favourite ids of the current section's kind in this playlist. For `recent`, the recent channel ids. Otherwise none.
- **excludedCategories(section)**: the hidden categories of that section, plus the locked categories that are **not unlocked** this session.
- **activeExclusions**: the excludedCategories when effectiveCategory is null. When a specific category is chosen, no exclusions apply.

What is shown:
- **Locked category selected** (and not searching): the unlock prompt replaces the content (section 6).
- **Live with the `recent` filter, not searching**: the recent channel ids in recency order, mapped to channels in the catalog (unknown ids dropped), minus channels in excluded live categories.
- **Continue Watching, not searching**: the continueWatching list (section 5.4), with section filtering.
- **Otherwise**: `select(effectiveCategory, searchText, restrictedIDs, activeExclusions, sortOrder)` on the section's index.
- While searching, results span **every category in the section**. The favorites and recent restrictions don't apply, but the exclusions do.

**Titles**:
- Searching: `Results for “<text>”`, with curly quotes.
- `all`: `All Live TV`, `All Movies` or `All Series`.
- `favorites`: `Favorites`.
- `continueWatching`: `Continue Watching`.
- `recent`: `Recently Watched`.
- A category: its name trimmed of spaces, or else the section title.

**Empty states**:
- Search: the standard "no results for <text>" view.
- Continue Watching: "Nothing in progress", with "Movies and episodes you start will appear here so you can pick up where you left off."
- Favorites: "No favorites yet", with "Right-click anything or use the star to add it here."
- Anything else: "Nothing here".

### 4.4 Accent-insensitive search algorithm

- **normalize(text)** = fold the text so that case, diacritics and width are ignored, without locale-specific rules. Matching is then exact substring matching on the folded strings.
  - *Windows suggestion*: apply NFKC (for width), then NFD, remove `NonSpacingMark` characters, then case-fold with `ToLowerInvariant` or `ToUpperInvariant`. Precompute this per item. Q4 covers the edge cases (ß, ligatures).
- **terms(query)** = normalize(query), split on whitespace, with empty pieces dropped.
- **matches** = every term is a substring of the normalised name (AND, in any order, anywhere in the name).
- Examples: "amelie" matches "Amélie". "BATMAN" matches "The Batman" and "Batman Begins". "batman begins" matches only "Batman Begins".
- The sidebar's "Filter categories" field uses the same algorithm on category names. The category query resets when the section changes.

### 4.5 Category list (sidebar)

**Fixed entries**:
- "All", with a badge showing the total item count of the section.
- "Favorites".
- Live shows "Recently Watched", with a context action "Clear Recently Watched…". Movies and Series show "Continue Watching", with "Clear Continue Watching…". Both ask for confirmation.

**Category entries**:
- **browsableCategories(section)** is the provider's category list minus hidden categories, with **pinned categories first in pin order**, then the rest in provider order.
- The sidebar shows a "Pinned" group and a "Categories" group, each with per-category counts.
- When hidden categories exist, a footer shows their count and links to Settings.
- A locked category shows a lock indicator. That applies whether or not it is unlocked this session.
- Context menu: "Pin to Top" or "Unpin". "Lock with PIN…" or "Remove Lock…". "Hide Category".
  - Hiding the currently selected category switches the filter to `all`.

**hiddenCategories(section)**, for the Settings list:
- The category records for the hidden keys. If an id isn't found in the catalog, it shows as "Category <id>".
- Each row has a "Show" action that unhides it. Rows from different sections are told apart by section, because ids overlap.

**Default filter**, used on section change and on the first load of a playlist:
- Live: `all`.
- Movies and Series: `continueWatching` if that list is non-empty for the section. Otherwise the first browsable category that isn't locked. Otherwise `all`.
- Choosing any sidebar entry also clears the detail stack.

---

## 5. Library

### 5.1 Favorites

- Toggle by content key (the playlist, plus `live`, `movie` or `series`, plus the id). Channels, movies and series can be favourited; episodes can't.
- `favoriteIDs(playlist, kind)` = the raw ids for keys with the prefix `"<UUID>/<kind>/"`.
- The favourites view is the index selection restricted to those ids. So it follows **provider order, or the chosen sort**, not the order things were added. Hidden and locked exclusions apply.
- Favourites are never pruned. They are removed only by toggling or by deleting the playlist.

### 5.2 Recent channels

- An entry is recorded **each time a live item starts loading**, that is, after the 350 ms zap debounce when stepping through channels.
- Key = content key `(live, item.streamID ?? item.id)`.
- Recording removes any existing copy of the key, inserts it at the front, and truncates the list to **30**. The list is shared across playlists, so one global cap of 30 covers them all.
- Users can remove one entry or clear a playlist's entries (Settings > History "Clear…", or the sidebar context menu).

### 5.3 Resume points (progress)

The player reports progress for **non-live** items in three cases:
- Every **5 s** while playing (non-final).
- On stop, on switching items or starting a new queue, and at the end of a file (final).

It reports only when `duration > 0` **and a video frame has been shown**.

**App-level throttle**:
- A non-final report is ignored if this key was committed less than **30 s** ago. The timestamps are kept in memory per key.
- Final reports always commit.
- The item is first made credential-free (section 2.10). If that fails, nothing is stored.

**LibraryState.recordProgress(key, item, position, duration, at)**:
- Reject the report unless `duration > 0`, position and duration are finite, and `duration < 604,800`. Rejected reports are never saved.
- Otherwise, upsert the entry with `updatedAt = at`, then prune.
- Position isn't range-checked; `fraction` clamps it.

**Pruning**, when the entry count exceeds **2000**:
- Order the entries as **unfinished first** (in progress or barely started), then finished. Within each group, oldest `updatedAt` first.
- Remove `count − 2000` entries from the front of that order. Watched marks therefore survive longest.

**Watched marks**:
- **markWatched(key, item, duration?)**: L = max(duration ?? 1, 1). Store position = duration = L with `updatedAt = now`. The entry is then finished.
  - For a movie, the duration is the existing progress duration.
  - For an episode, it is `episode.durationSeconds`, or else the existing progress duration.
  - Marking needs a storable item. Episodes need an Xtream client.
- **markUnwatched(key)**: remove the entry completely, which also removes any resume point.
- **isWatched** = the entry exists and is finished.
- UI: "Mark as Watched" or "Mark as Unwatched" on movies and on episodes.

**Resume start position**: `resumePosition` (section 1.14).

**Movie detail buttons**:
- If the movie is resumable: "Resume · <remaining> left" (default action) and "Start Over".
- Otherwise: "Play".

### 5.4 Continue Watching

**LibraryState.continueWatching(playlist)**:
- Entries for the playlist (prefix match) that are **resumable**.
- Sorted by `updatedAt`, newest first.
- **Episodes collapse to one entry per series**: only the newest resumable episode for each `parentID` is kept. Episodes without a parentID aren't collapsed.

**App-level section filter** (applied to that list):
- Movies section: only movie entries. An entry is dropped if the movie is in the catalog and its category is excluded (hidden, or locked and not unlocked). Movies not found in the catalog are kept.
- Series section: only episode entries. An entry is dropped if the parent series is found and its category is excluded.
- Live section: empty.

**Removing an entry**:
- Not an episode, or an episode with no parentID: remove that key.
- An episode: remove **every resumable entry** in the same playlist (same key prefix up to the first `/`) with the same parentID. Otherwise the row would just fall back to an older episode. **Finished entries (watched marks) are kept.**

**clearContinueWatching(playlist)**: remove all **resumable** entries for that playlist. Watched marks and barely-started entries (under 30 s) are kept. The confirmation dialog in Settings reads "Clear watch history?" / "Favorites and watched marks are kept." and clears both Continue Watching and Recently Watched for the active playlist.

**Resuming from Continue Watching**: the queue is the current continueWatching list (the entry alone if it isn't in that list). The queue title is "Continue Watching", playback starts at the entry's resumePosition, and items are rebuilt with `playable()`.

### 5.5 Next-episode logic

**EpisodePicker.pick(count, progress(i))** chooses the series page's main button. Episodes are taken in **play order**: seasons ascending, then episodes ascending.
- If count = 0, there is no button.
- Consider only the episodes whose progress is resumable **or** finished (barely-started ones are ignored). Take the one with the **latest updatedAt**.
  - There is none: `start(0)`.
  - It is resumable: `resume(i)`.
  - It is finished and i + 1 < count: `next(i + 1)`. Otherwise `start(0)`, which starts the series over.
- Labels: `resume` gives "Resume S{s} E{n}". `next` gives "Play Next · S{s} E{n}". `start` gives "Watch Again" if **any** episode is watched, and otherwise "Play S{s} E{n}".
- Examples from the tests:
  - Episode 2 abandoned at 600 s (t = 1), then episode 4 finished (t = 5): `next(4)`.
  - Episode 2 restarted later (t = 9): `resume(1)`.
  - The final episode finished: `start(0)`.
  - Episode 3 at 10 s only: `start(0)`.

**Autoplay**:
- When an on-demand file really ends (not an early end, section 9.1), progress is saved.
- Then, if `shouldAutoplay(current, next, enabled)` is true, an **8-second countdown** starts, ticking once per second, and then the next item plays.
- `shouldAutoplay` requires all of the following:
  - The setting is enabled (default **on**).
  - There is a next item in the queue.
  - Current and next are **both episodes**.
  - They have the **same non-null parentID**.
- Movies never autoplay. A mixed Continue Watching queue never rolls into a different show.
- Seeking back from the end, replaying or any navigation cancels the countdown.

### 5.6 Play queues

**Window**: queues are limited to **500 items** centred on the selection. Let start = clamp(index − 250, 0, count − 500). If count ≤ 500, the whole list is used.

| count | index | size | Window |
| --- | --- | --- | --- |
| 1000 | 500 | 100 | 450..<550 |
| 1000 | 5 | 100 | 0..<100 |
| 1000 | 990 | 100 | 900..<1000 |

**Build**:
- Map each element to a PlaybackItem and skip the elements that can't be played. The selected index is re-pointed so it still refers to the selected element.
- If the selected element itself can't be played, nothing plays.

**Sources**:
- **Channel**: the visible channel list it was picked from. The queue title is the current filter title.
- **Movie**: the list it was picked from. From a detail page, the current browse results are used. If the movie isn't in that list, the queue is just the movie. Starts at the resume position unless "Start Over" was chosen.
- **Episode**: all episodes of all seasons, flattened in play order. The queue title is the series name. Starts at the episode's resume position unless "resume: false" was chosen.
- **Continue Watching**: see section 5.4.

**Navigation**:
- `target(index, delta, count, wraps)`. Live queues **wrap** like a TV remote, so from 0, −1 goes to the last item. On-demand queues **stop** at the ends (null).
- An empty queue gives null.
- Steps (↑/↓, N, Page Up/Down) debounce the actual load by **350 ms**, so rapid zapping only connects once.
- Switching to another item saves the current progress first, then starts the target at its resume position (live items have none).

**Last channel** (⌫): only while the current item is live. It jumps to the previously played queue index, which is tracked whenever the index changes. That index is reset when a new queue starts.

### 5.7 Hidden, pinned and locked categories

- `toggleHidden(key)` and `togglePinned(key)`. Pinning appends to the end of the pin order. Unpinning removes the key.
- `setLocked(key, bool)`.
- All three are per playlist and per section, through the category key.

### 5.8 removeAll(playlist)

Removes every favourite, progress entry, recent channel, hidden key, locked key and pinned key whose key starts with `"<UUID>/"`. Other playlists are untouched. The PIN is global and stays.

---

## 6. Parental PIN

**Format**: 4 to 8 characters, all digits. The original uses `Character.isNumber`, which also accepts non-ASCII numerals (Q3); *use ASCII 0–9 on Windows*.
- "1234" and "12345678" are valid.
- "123", "12a4" and "123456789" are invalid.
- The create form message is "Use 4 to 8 digits." When the confirmation differs: "The PINs don't match."

**Hash (current scheme)**:
- PBKDF2-HMAC-SHA256 with **310,000 iterations**, a **16-byte** random salt from a cryptographically secure source (never a fixed fallback) and a **32-byte** output.
- The password bytes are the UTF-8 encoding of the PIN.
- Stored as `pbkdf2-sha256$310000$<salt hex>$<hash hex>`, in lowercase hex. Each new hash gets a fresh salt, so hashing the same PIN twice gives different strings.
- **Verify**:
  1. Split on `$`. There must be exactly 4 parts, part 0 must be `pbkdf2-sha256`, and the iterations must be an unsigned 32-bit integer greater than 0.
  2. Salt and hash must be non-empty, even-length and valid hex.
  3. Derive with the **stored** iteration count and the **stored hash length**, then compare in **constant time**.
  4. Any malformed value fails, for example `pbkdf2-sha256$0$00$00` or `pbkdf2-sha256$x$y$z`.

**Legacy scheme**:
- Any stored value that doesn't start with `pbkdf2-sha256$` is legacy: lowercase hex of SHA-256 of the UTF-8 string `"lumen.parental." + pin`, compared in constant time.
- After a correct PIN entry against a legacy hash, the hash is **replaced** with a new PBKDF2 hash and saved. A correct entry is the only moment an upgrade is possible.
- *Windows only needs this if it imports macOS library files.*

**Storage**: `pinHash` lives in library.json. The PIN is a family deterrent, not a security boundary.

**Lock semantics**:
- **Locking a category**:
  - If a PIN exists, the category locks **immediately**, with no prompt, and is removed from the session's unlocked set.
  - If no PIN exists, a "Create a PIN" sheet appears: PIN plus confirmation, with the text "Locked categories need this 4–8 digit PIN to open. Keep it somewhere safe." When it succeeds, the category locks.
- **A locked category** (locked, and not unlocked this session):
  - Selecting it shows an unlock prompt instead of the content: "Enter your PIN to view it. It locks again when Retune quits, or with ⌘L." A wrong PIN shows "Wrong PIN". There is no attempt limit or delay.
  - A correct PIN unlocks the category **for this session only**.
  - Its items are excluded from All, Favorites, Search, Recently Watched and Continue Watching, and it is skipped when the default category is chosen.
  - Once unlocked, its items appear everywhere again.
- **Remove Lock…**: asks for the PIN ("Enter your PIN to remove this lock."), then unlocks permanently.
- **Lock Categories Again** (⌘L) clears every session unlock. Quitting does too, because unlocks are never persisted.
- **Settings > Parental PIN**:
  - "Set PIN…" creates a PIN.
  - "Change…" asks for the current PIN ("Enter your current PIN."), then opens Create.
  - "Remove…" asks for the PIN ("Enter your PIN. Removing it also removes every category lock."). It then clears the hash, **all locks in every playlist**, and the session unlocks.
  - Without a PIN, the hint reads "Lock categories by right-clicking them in the category list."
- **When the PIN is asked for**: unlocking a locked category for viewing, removing a lock, changing the PIN and removing the PIN.
  - It is **not** asked for when locking, when hiding or unhiding (hidden categories aren't protected), or when clearing history.

---

## 7. Secrets

| Data | Where | Protection |
| --- | --- | --- |
| Xtream password | OS secret store. macOS: Keychain generic password, service `io.github.0xaliraza.retune.playlist`, account = playlist UUID string, value = UTF-8 password, accessible only while the device is unlocked. | Encrypted by the OS. *Windows: Credential Manager (`CredWrite`, target `Retune/<uuid>`), or DPAPI CurrentUser-protected bytes.* |
| Xtream server URL and username | playlists.json | Plain JSON, private to the user |
| M3U URL (often contains the username and password) or local file path | playlists.json | Plain. A known limitation. |
| M3U stream URLs (may contain credentials) | the catalog cache file | Plain |
| PIN hash, favourites, history (locators only), category keys | library.json | Plain |
| Preferences | user preferences store | Plain |

**Secret store operations**:
- **set**: update the item if it exists, otherwise add it. Any other failure gives the error `keychain(status)`: "The password couldn't be saved to your Keychain (error N)."
- **get**: returns null when the item is missing or access is denied.
- **remove**: no-op if absent.

**Failure modes**:
- If the password can't be read for an Xtream playlist, the load fails with "The saved password for this playlist is missing, or Keychain access was denied. Edit the playlist to re-enter it."
- On edit, an empty password field reuses the stored secret.

**Legacy migration**: early builds kept `secrets.json` (a JSON map of playlist UUID to password) in the data folder. At launch, every entry is written to the secret store and read back. The file is deleted **only if every entry reads back equal**, so a denied prompt loses nothing and the migration retries at the next launch.

---

## 8. Persistence

### 8.1 Files

macOS location: `~/Library/Application Support/Retune/`. *Windows suggestion: `%APPDATA%\Retune\` or `%LOCALAPPDATA%\Retune\`, with a user-only ACL.*

| File | Content | Written |
| --- | --- | --- |
| `playlists.json` | JSON array of Playlist | Immediately on add, edit and delete |
| `library.json` | LibraryState JSON | Debounced (section 8.3) |
| `Catalogs/<UUID>.json` | Catalog JSON | After each successful refresh, on a serial queue |
| `secrets.json` | Legacy only: read, migrated, deleted | — |

Artwork cache: `~/Library/Caches/Retune/Artwork` (HTTP cache, 1 GiB on disk). Settings > History "Cached posters and logos — Clear" empties the memory cache, the disk cache and the failed-URL list. Artwork that returned 404, 410 or an undecodable 200 is not retried in the same session; timeouts are retried. Artwork requests: 20 s request timeout, 60 s resource timeout, 8 connections per host, 32 MiB body cap.

### 8.2 JSON store semantics (applies to every file)

- **load**:
  - A missing file gives null. The caller uses defaults: no playlists, or an empty library.
  - A file that **exists but can't be decoded** is **renamed to `<name>.corrupt-<unix seconds>`**, then null is returned. The next save can't destroy the old data. No error is shown.
- **save**:
  1. Create the folder (user-only).
  2. Write the JSON to a temporary file in the same folder, named `.<name>.<random UUID>.tmp` and created user-only from the first byte.
  3. **Atomically rename** it over the target, which keeps the private permissions. On failure, delete the temporary file and raise an error.
  - The app ignores save errors.
- **delete**: remove the file, ignoring errors.
- **Permissions at launch**: the data folder gets `0700`, every subfolder `0700` and every file `0600`, including files written by older, looser builds. *Windows: set a user-only ACL on the folder, inherited.*

### 8.3 Write scheduling

- **Library**: every mutation schedules a save of a snapshot **1 s** later, cancelling any pending one (debounce). On quit, playback is stopped first (which flushes the final progress), then the library is saved synchronously.
- **Catalog writes and deletes** run strictly in order on one serial background queue. Deleting a playlist's cache can therefore never be undone by a save that was still running.
- **Catalog fast load**: the file is decoded as three parts in parallel. If any part fails, the normal load runs, which backs up a corrupt file. If there is no usable cache, the app downloads.

### 8.4 Network cache purge (macOS-specific)

Older builds let the OS URL cache store API URLs containing passwords. At launch the app deletes the folders `~/Library/Caches/<bundleID>` and `~/Library/HTTPStorages/<bundleID>` (whole folders, because deleted SQLite rows stay readable). Nothing happens without a bundle id. The app's own `Caches/Retune/Artwork` is untouched. *Windows: make sure the HTTP stack has no disk cache. WinINet/WinHTTP caches must not be used for API calls.*

### 8.5 Migrations and tolerance

- **Library decode**:
  - Every field is optional and defaults to empty, so files from older versions load.
  - The `progress` map is decoded **entry by entry**, and one malformed entry (for example `kind: "hologram"`) is skipped.
  - A type error in other fields (for example `favorites` not being an array of strings) fails the whole file. It is then moved to `.corrupt-…` and an empty library is used.
- **Stream URL sanitising**, at launch: every progress entry whose URL can be reduced to an Xtream locator is rewritten as that locator. If anything changed, the library is saved at once. The operation is idempotent: `retune://series/42.mkv` is left alone.
- **PIN upgrade**: see section 6.
- **Legacy secrets**: see section 7.

### 8.6 Preferences (key–value store)

| Key | Type | Default | Values |
| --- | --- | --- | --- |
| `activePlaylist` | string (UUID) | first playlist | |
| `layout.live` / `layout.movies` / `layout.series` | string | live: `list`, movies and series: `grid` | `grid`, `list`, `compact` |
| `layout.tileSize` | string | `medium` | `small`, `medium`, `large` (grid only) |
| `player.liveFormat` | string | `automatic` | `automatic`, `ts`, `hls` |
| `player.autoplayNext` | bool | true | |
| `player.volume` | double | 100 | 0–100, whole numbers |
| `player.subtitleStyle` | JSON blob of SubtitleStyle | the SubtitleStyle defaults | see section 9.5 |

An unknown or invalid stored value falls back to the default. A SubtitleStyle that can't be decoded gives the defaults.

---

## 9. Stream profiles, formats, quality, subtitles and volume

### 9.1 StreamProfile (per load, because mpv keeps properties across files)

| Property | Live | On demand |
| --- | --- | --- |
| `stream-lavf-o` | `""` (no FFmpeg reconnect) | `reconnect=1,reconnect_delay_max=5` (**never** `reconnect_streamed`) |
| `demuxer-max-back-bytes` | `0` | `128MiB` |
| `demuxer-seekable-cache` | `no` | `auto` |
| forced speed | 1.0, reset on every load | none (keeps the viewer's speed) |

Both profiles must set **the same property names**, so each one resets what the other set. Speed controls are disabled for live. Speed presets: 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 3.

**Reconnect policy**:
- Automatic retries wait **1 s, 2 s, 4 s and 6 s** (4 attempts). After that, the error is shown. The counter resets when playback starts or a new item begins.
- A live stream "ending" is treated as a failure and reconnects. On-demand items retry from the last position.
- **Early end** for on-demand files: when the end-of-file arrives with duration > 0 and duration − position > **60 s**, it counts as a dropped connection. A retry counts only if it gets more than **5 s** further than the previous early end. A truly truncated file is therefore retried once, then accepted as ended.

| position | duration | previous early end | Dropped connection? |
| --- | --- | --- | --- |
| 600 | 3600 | none | yes |
| 3539 | 3600 | none | yes |
| 3540 | 3600 | none | no |
| 600 | 0 | none | no |
| 600 | 3600 | 600 | no |
| 605 | 3600 | 600 | no |
| 606 | 3600 | 600 | yes |

**Load gate (PendingUntilReady)**: a load waits until the video surface is ready. Only the **latest** waiting request runs, it runs once, it can be cancelled, and if readiness is lost, requests wait again.

### 9.2 Live format resolution

- `ts` gives `ts`.
- `hls` gives `m3u8`.
- `automatic` gives `ts` if the account's `allowed_output_formats` is empty or contains `ts`, and `m3u8` otherwise.
- Before authentication finishes, the list is empty, so the result is `ts`.
- Settings: "Live stream format", with the hint "Try HLS if a channel keeps freezing; MPEG-TS starts faster." The other Settings option is "Play next episode automatically".

### 9.3 VideoQuality (stats overlay)

The label comes from the frame size. **Width matters as much as height**:
- Width ≤ 0 or height ≤ 0: null.
- Width ≥ 3200 or height ≥ 2000: `4K`.
- Width ≥ 2200 or height ≥ 1400: `1440p`.
- Width ≥ 1700 or height ≥ 1000: `1080p`.
- Width ≥ 1200 or height ≥ 700: `720p`.
- Anything else: `SD`.

Examples: 3840×1600 is 4K, 1920×800 is 1080p, 1280×534 is 720p and 720×576 is SD.

Bitrate:
- At or above 1,000,000 bit/s: `"%.1f Mbps"`, so 4,200,000 shows as "4.2 Mbps".
- Below that: `"%.0f kbps"`, so 640,000 shows as "640 kbps".
- The network speed is shown as `cache-speed × 8`.

### 9.4 Volume

- Maximum **100**, with no amplification. The step for `-` and `=` is **5**.
- `clamped(v)` = round v to the nearest whole number, then clamp it to 0–100.
- `stepped(v, up)`: going up gives (floor(v/5) + 1) × 5. Going down gives (ceil(v/5) − 1) × 5. Then clamp. So 97 goes up to 100 and down to 95. 99.39 goes up to 100 and down to 95. 100 going up stays 100. 0 going down stays 0.
- Label: `"<rounded>%"`, so 99.39 shows as "99%" and 99.6 as "100%".
- The volume is persisted on every change, and the stored value is clamped at startup (default 100). Setting a volume above 0 while muted unmutes.

### 9.5 SubtitleStyle

| Field | Default | Range and clamping | mpv mapping |
| --- | --- | --- | --- |
| scale | 1.0 | Rounded to 0.1, then clamped to 0.5–2.5 | `sub-scale` = the value as a decimal string ("1.0") |
| position | 100 (bottom) | Rounded to a whole number, then clamped to 50–100 | `sub-pos` ("100.0") |
| background | false | | `sub-border-style` = `background-box` when true, `outline-and-shadow` when false |
| keepsOriginalStyle | false | | `sub-ass-override` = `scale` when true, `strip` when false |

Clamping examples: a scale of 9 becomes 2.5, a position of 10 becomes 50, a scale of 1.24 becomes 1.2 and a position of 87.6 becomes 88.

`strip` is the default because many SRT files carry inline tags (such as `<font size="80">`) that would fill the screen. Every change is clamped, saved and applied straight away, and there is a "reset to defaults" action.

### 9.6 Screenshots

- File name: `Retune yyyy-MM-dd 'at' HH.mm.ss.SSS.png`, using local time with a POSIX locale and a 24-hour clock. Example: `Retune 1970-01-01 at 00.00.00.000.png`. **The name is built from the time only**, because titles are untrusted and could contain path characters.
- The file is saved to the Desktop without overwriting an existing file. Nothing is saved unless a video frame is on screen. After 2 s without a captured frame, the capture is reported as failed.
- If the frame buffer comes bottom-up, its rows are flipped: `[1,2,3,4,5,6]` with 2 bytes per row becomes `[5,6,3,4,1,2]`. Empty input, or 0 bytes per row, gives an empty result.

---

## 10. Formatting helpers

| Helper | Rule |
| --- | --- |
| clock(s) | Non-finite or negative input gives `0:00`. Otherwise clamp to at most 7 days and floor to whole seconds. With hours > 0: `H:MM:SS` (for example `1:02:03`). Otherwise `M:SS` (`4:05`). |
| runtime(seconds int) | With hours > 0: `"Xh Ym"` (`1h 42m`). Otherwise `"Ym"` with at least 1, so under 60 s, including 0, shows as `1m`. |
| remaining(s) | `runtime(whole seconds) + " left"` |
| rating(v) | `"%.1f"` |
| time(date) | The locale's short time, with no date. |
| timeRange(a, b) | `"<time a> – <time b>"` (a spaced en dash) |
| Artwork placeholder | Initials: the first character of each of the first two words, where words are split on anything that isn't a letter or digit, uppercased. Colour: seed = fold over the Unicode scalars of the title using `(seed × 31 + scalar) & 0xFFFF` (with wrapping arithmetic); hue = (seed mod 360) / 360. Gradient from HSB(hue, 0.45, 0.45) to HSB((hue + 0.08) mod 1, 0.55, 0.28). The seed must be stable across launches, so don't use a randomised string hash. |

---

## 11. Keyboard and grid logic (pure rules in the core)

### 11.1 Pane navigation

There are three columns: sidebar → categories → content.

| Pane | Key | Action |
| --- | --- | --- |
| sidebar | Tab, →, Enter | focus categories |
| sidebar | Shift+Tab | focus content |
| categories | Tab, →, Enter | focus content |
| categories | Shift+Tab, ←, Esc | focus sidebar |
| content | Esc | go back if a detail page is showing, otherwise focus categories |
| content | Tab | focus sidebar |
| content | Shift+Tab | focus categories |
| content | ← | pass through when a grid or detail page is showing (the content uses ← itself), otherwise focus categories |
| any other combination | | pass through |

### 11.2 Grid navigation

- **Columns**: columns(width, minimum, spacing) = max(1, floor((width + spacing) / (minimum + spacing))). If minimum ≤ 0, the result is 1. With a minimum of 150 and spacing of 20: a width of 1000 gives 6, 999 gives 5 and 100 gives 1.
- **Move**:
  - count = 0 gives null. No current index gives 0.
  - left/right move by ±1. up/down move by ±columns.
  - If the target is below 0: going up stays put. Going left goes to 0.
  - If the target is at or beyond count:
    - Going down: stay put if you are already in the last row. Otherwise go to the last item.
    - Going right: go to the last item.
- **Edge**: up is at the edge when the index is less than columns. Left is at the edge when the index mod columns is 0. Down and right are never at the edge. The edge test lets focus leave the grid to the sidebar or search.

Moves with 4 columns and 10 items:

| From | Direction | To |
| --- | --- | --- |
| none | down | 0 |
| 5 | right | 6 |
| 5 | left | 4 |
| 5 | up | 1 |
| 1 | up | 1 |
| 5 | down | 9 |
| 4 | down | 8 |
| 9 | down | 9 |
| 0 | left | 0 |
| 9 | right | 9 |

---

## 12. Errors and edge cases

### 12.1 Error catalogue (user-visible messages)

| Error | Message |
| --- | --- |
| invalidServer | That server address doesn't look right. Use something like http://example.com:8080. |
| invalidCredentials | The server rejected that username or password. |
| accountInactive(s) | This account is \<s lowercased\>. |
| http(code) | The server responded with an error (HTTP \<code\>). |
| emptyResponse | The server returned an empty response. It may be rate limiting — try again in a moment. |
| unreadableResponse | The server returned data Retune couldn't read. |
| emptyPlaylist | No playable entries were found in this playlist. |
| missingPassword | The saved password for this playlist is missing, or Keychain access was denied. Edit the playlist to re-enter it. |
| keychain(status) | The password couldn't be saved to your Keychain (error \<status\>). |
| responseTooLarge | The server sent far more data than a playlist should contain. |

Transport errors (timeouts, DNS failures and so on) show the platform's own message.

`unreadableResponse` is also raised when series details are requested without an Xtream client.

### 12.2 Notable edge cases

- An empty body is never treated as an empty section. A body of exactly `{}` is.
- Duplicate ids within a list: index lookups use the first occurrence.
- Absurd timestamps (≥ year 10000, ≤ 0) and durations ≥ 7 days are discarded.
- Every Int conversion from a provider number is guarded against NaN, infinity and overflow.
- EPG refetch timing:
  - The guide is fetched when a live item starts.
  - It is fetched again when the current programme ends. If nothing is on now, it is fetched when the next programme starts.
  - The wait is clamped between **60 s and 6 h**.
  - If there are no times at all, it isn't fetched again.
  - The results are dropped if the channel has changed meanwhile.
  - EPG errors give an empty guide.
  - EPG needs Xtream and a `streamID`.
- Progress for an item shorter than 90 s counts as finished as soon as it is saved, because duration − position < 90 is true at once (Q7).
- Before the first video frame, position is 0, so nothing is saved. This protects the resume point.
- Live progress is never stored. Only the recent-channel entry is.
- Deleting or switching playlists stops playback first, so the final progress lands in the right playlist and a deleted playlist's entries aren't brought back.
- A cached catalog stays visible when a background refresh fails.
- An Xtream playlist without a password never shows its cache.
- M3U lines that are local paths, `file://` or other disallowed schemes are dropped without any message.

---

## 13. Test cases (Tests/RetuneCoreTests) — summary

Every row should be reproduced as a Windows unit test.

### Server and URL handling
| Test | Expectation |
| --- | --- |
| normalizesServer | The 5 normalisation examples in section 2.1. |
| flagsPlainHTTPAddresses | `http://…` → true. `HTTP://…` → true. Bare `host:8080` → true. A get.php link with http → true. `https://…` → false. `""` → false. |
| rejectsGarbageServer | `""` and `"not a host"` give null. |
| extractsCredentialsFromM3ULink | get.php link → server `http://h.example:8080`, username "viewer", password "12". `/list.m3u` → null. |
| buildsStreamAndAPIURLs | live `…/live/u/p%20w/42.ts`, movie `…/movie/u/p%20w/7.mkv`, episode with no extension `…/series/u/p%20w/9.mp4`, and the exact API URL with parameter order. |

### M3U parsing
| Test | Expectation |
| --- | --- |
| parsesEntriesAttributesAndTitles | 4 entries. Group with a comma inside quotes. `tvg-id`. Logo. EXTGRP fallback "Misc". |
| splitsLiveAndOnDemand | Channels, movies and both category lists as in section 3.5. Movie extension `mkv`. The channel's directURL is kept. |
| titleCommaInsideQuotedAttributeIsNotTheSeparator | Title "Real, Title". `tvg-name` "A, B". |
| m3uDuplicateURLsAreDropped | Only "One" survives. |
| AttributeScanner (11 fixed headers + 5,000 generated) | Same result as the regex `([A-Za-z0-9_-]+)="([^"]*)"` with lowercased keys. Covers: mixed-case keys, `key=""`, duplicate keys (last wins), unclosed quotes, missing key, unquoted values, spaces around `=`, `x-y_z9="v"tail="w"`, Unicode values and an empty header. |
| aHugeLineParsesQuickly | A 500,000-character header parses in under 2 s. |

### Browse and search
| Test | Expectation |
| --- | --- |
| countsAndFiltersByCategory | count("a") = 2. A missing category counts 0. select(b) = ["2"]. All = 3. |
| searchIsAccentAndCaseInsensitiveAndMultiTerm | amelie → [2]. BATMAN → [1, 3]. "batman begins" → [3]. |
| sortsAndRestrictsToIDs | title → [2, 3, 1] ("amelie" < "batman begins" < "the batman"). rating → [2, 1, 3] (a missing rating sorts last). recentlyAdded → [1, 3, 2]. ids {3, 2} → provider order [2, 3]. item(id) lookup works. |
| excludedCategoriesAreFilteredOut | Excluding "adult" keeps items "kids" and the null category. Searching inside an excluded category finds nothing. |

### Library
| Test | Expectation |
| --- | --- |
| progressThresholds | 10/6000 not resumable. 600/6000 resumable. 5800/6000 finished. 3000/6000 has fraction 0.5. |
| continueWatchingIsScopedSortedAndCollapsesSeries | → [e2, m1]. The collapsed series shows its newest episode. Finished items and other playlists are excluded. |
| favoritesToggleAndScope | The favourite is scoped by kind. A second toggle removes it. |
| recentChannelsDedupeAndCap | After 40 entries and k5 again: k5 is first, the count is 30 and there is one k5. |
| removeAllClearsOnlyThatPlaylist | Only the other playlist's favourite remains. |
| catalogStaleness | maxAge 60: stale at 61 s, not stale at 59 s. |
| pinHashVerifiesAndRemovalClearsLocks | No PIN → verify is false. Set → the right PIN verifies and the wrong one doesn't. removePIN clears the locks. |
| pinValidation | See section 6. |
| hidePinAndCategoryIDs | Category key prefix extraction. Toggling twice clears. Prefixes are section-scoped. |
| removingEpisodeFromContinueWatchingClearsWholeSeriesButKeepsWatched | e1 and e2 are removed. The watched e0 stays. The other series' x1 remains. |
| markWatchedAndUnwatched | Watched with a null duration → finished and not in Continue Watching. Unwatched → the entry is gone. |
| clearingHistoryIsScopedAndKeepsWatchedMarks | Clearing one playlist keeps its watched marks and leaves the other playlist intact. removeRecentChannel works. |
| decodesLegacyLibraryWithoutNewFields | `{"favorites":["a"],"progress":{},"recentChannels":["r"]}` decodes with defaults. |
| libraryDecodingSkipsOneBadProgressEntry | `kind: "hologram"` is skipped and "good" is kept. |
| pruningKeepsWatchedMarksOverInProgress | After 2000 in-progress entries plus 1 old watched mark: the count is 2000, the watched mark is kept, and the oldest in-progress entry `p0` is dropped. |
| implausibleDurationsAreNotSaved | A duration of 9e18 is not stored. |
| sanitizeRewritesStoredStreamURLs | The first pass changes the URL to `retune://series/42.mkv`. The second pass makes no change. |

### Queue and playback rules
| Test | Expectation |
| --- | --- |
| QueueWindow small and large | See section 5.6. |
| liveWrapsOnDemandStops | 0 − 1 wraps to 4. 4 + 1 wraps to 0. Without wrapping: null at both ends, and 2 + 1 = 3. count 0 → null. |
| autoplayOnlyWithinSameSeries | Only a1 → a2 with autoplay enabled is true. |
| buildKeepsSelectionAlignedWhenItemsAreSkipped | ["a","skip","b","c"] with "b" selected → items [a, b, c], with the index pointing at b. Selecting "skip" → null. |
| EpisodePicker (6 tests) | See section 5.5. |
| ReconnectPolicy backsOffThenGivesUp | delay(0) = 1 s. delay(3) = 6 s. delay(4) = null. Total ≥ 10 s. |
| EarlyEnd (9 cases + truncated file retried once) | See section 9.1. |
| StreamProfile (3) and ForcedSpeed (2) | See section 9.1. |
| PendingUntilReady (6) | See the load-gate rules in section 9.1. |

### Xtream decoding
| Test | Expectation |
| --- | --- |
| liveStreamsDecodeWithNullsAndMixedTypes | An int id. A `\/`-escaped logo. A null EPG id. `"3"` is parsed as a number. A category of `5` gives "5". An empty icon gives null. `"1"` archive gives true. A row without an id is dropped. |
| vodRatingAcceptsStringIntAndFloat | Ratings [7.0, 7.2, 8.2, null]. A string `added` timestamp. |
| seriesTakesFirstBackdropFromArray | First backdrop. A rating of "0" gives null. `releaseDate` is read. |
| seriesInfoGroupsEpisodesBySeasonKey | Seasons [1, 2], sorted, with episodes sorted. Metadata name and `cover_tmdb`. Default name "Season 2". The episode's season is overwritten. `episode_run_time` "45" gives 2,700 s. year is "2025". `info: []` on an episode is tolerated. |
| seriesInfoAcceptsEpisodesAsNestedArrays | Season taken from the episode (3). `info: []` gives an empty plot. |
| vodInfoToleratesEmptyInfoArray | An empty MediaInfo. A full one gives duration 6163, rating 6.5 and a poster. |
| epgDecodesBase64AndSorts | Titles decoded and sorted. Description decoded. isLive and progress = 0.5 at the midpoint. |
| authParsesAccountAndRejectsBadLogin | Active. Max connections 1. Formats [m3u8, ts]. Expiry. Not a trial. `auth: 0` gives null. |
| lenientIntRejectsNonFiniteAndHugeValues | `"nan"`, `1e300` and `"inf"` give a null number, but the rows are kept. |
| runtimeOverflowDoesNotTrap | An `episode_run_time` of Int64.max gives a null duration. |
| absurdEPGTimestampsAreDiscarded | 1e21 → null. 1,700,000,000 → valid. |

### Transport (stubbed)
| Test | Expectation |
| --- | --- |
| emptyBodyIsRetriedThenSucceeds | Responses: 200 empty, 222, 200 with data. The call succeeds after exactly 3 requests. |
| persistentEmptyBodyIsAnErrorNotAnEmptySection | Three empty 200 responses give `emptyResponse`. |
| oversizedResponsesAreCutOff | 2,048 bytes with a 1,024-byte limit gives `responseTooLarge`. |
| boundedDataReturnsNormalBodies | A normal body is returned unchanged. |
| emptyArrayIsAGenuinelyEmptySection | `[]` gives an empty list. |

### Security and credential hygiene
| Test | Expectation |
| --- | --- |
| slashInCredentialsIsEscapedInStreamURLs | `u%2F1/a%2Fb`. |
| locatorsStripCredentialsAndResolveBack | `retune://movie/9.mkv`. No secret in it. It resolves back to the original URL. A non-Xtream URL gives no locator and passes resolve unchanged. |
| apiSessionNeverPersistsRequests | No URL cache and no cookie store. |
| plusInPasswordSurvivesPHPQueryParsing | `ab+c&d=e` → `password=ab%2Bc%26d%3De`. |
| slashInPasswordStillProducesACredentialFreeLocator | The locator is still `retune://movie/9.mkv`. |
| m3uIDsNeverContainTheStreamURL | The id and opaque locator have no secret. The directURL keeps it. |
| CredentialFreeItem (4) | Xtream → locator. M3U → `retune://item/42`. An unrecognised account URL (get.php) gives null, so it isn't stored. An already-stored item is unchanged. |
| rejectsLocalAndSpecialProtocols (9) and acceptsNetworkStreams (5) | See section 2.9. Rejected URLs also produce no M3U entry. |
| lenientURLRejectsHTTPLookalikes | `httpx://` → null. |
| restrictPermissionsTightensExistingFiles | Folders get 0700 and files 0600. |
| purgeRemovesOnlyThisAppsNetworkCaches | A null bundle id → no-op. Only `Caches/<bundleID>` is removed, and `Caches/Retune/Artwork` stays. |
| corruptStoreIsBackedUpNotOverwritten | Load → null and a `library.json.corrupt-*` file exists. Save → the file is readable again with mode 0600, even though it was 0644 before. |
| PINHash (3) | Salted, so two hashes differ. The `pbkdf2-sha256$` prefix. Wrong PINs fail. Legacy hashes verify and are flagged as legacy. Malformed values never verify. |
| SecretStore (2) | Set, update, get and remove work. Legacy file migrated, then deleted. |

### Miscellaneous
| Test | Expectation |
| --- | --- |
| VideoQuality (9 labels + bitrate) | See section 9.3. |
| SubtitleStyle (3) | See section 9.5. |
| GridNavigation, GridEdge and PaneNavigation | See section 11. |
| ScreenshotFile (3) | See section 9.6. |
| SerialWork (2) | A delete waits for the save queued before it. `finished()` returns at once when nothing is queued. |
| VolumeLevel (5) | See section 9.4. |

---

## 14. Open questions and ambiguities

1. **Sort stability.** Swift's sort isn't guaranteed stable, so ties under Title, Recently Added or Rating have no defined order. *Proposal: use a stable sort, with provider order as the tiebreaker.*
2. **Query encoding of non-ASCII characters.** The original's "allowed" set for query values is Unicode alphanumerics plus `-._~`, so non-ASCII letters in a username or password may not be percent-encoded consistently. *Proposal: percent-encode the UTF-8 bytes of everything except ASCII `A–Z a–z 0–9 - . _ ~` (`Uri.EscapeDataString` semantics).*
3. **PIN digits.** `isNumber` accepts non-ASCII numerals, such as Arabic-Indic digits and "½". *Proposal: accept ASCII 0–9 only.* Is cross-platform PIN-hash compatibility needed?
4. **Search folding details.** Foundation's case, diacritic and width folding may differ from a .NET implementation on ß→ss, ligatures, Turkish dotted/dotless i, and Hangul or kana width. Decide an exact normalisation and test it against the test vectors.
5. **M3U title fallback.** Whenever an `#EXTINF` exists, its title is used even if it is empty, so `tvg-name` is effectively never used and an entry can end up with an empty name. Should Windows fall back to `tvg-name`, then the URL file name, when the EXTINF title is blank? This would be an improvement, not parity.
6. **The pending EXTINF survives a rejected URL line.** The metadata then attaches to the next valid URL. Parity, or reset it on any non-comment line?
7. **Items shorter than 90 s are "finished" immediately.** Is that acceptable (trailers, short clips)?
8. **Continue Watching for M3U** resolves stored items only against `movies`, which is fine because only on-demand items have progress. Opaque ids are URL hashes, so if the provider changes a URL (for example a password rotation baked into M3U links), history entries silently stop resolving.
9. **"{}" detection is a byte-exact match.** `{ }` or `{}\n` would be `unreadableResponse`. Should whitespace be tolerated?
10. **The M3U local file has no size cap** in the original. *Proposal: apply the same 256 MiB cap.*
11. **M3U URL playlists accept only http and https** (lenient parse), although the stream policy allows rtmp and others for entries. Parity?
12. **The PIN is global, but locks are per playlist.** Removing the PIN clears locks in every playlist. Confirm.
13. **Unlimited PIN attempts** with no back-off. Keep, or add a delay?
14. **The recent-channel cap is global** (30 across all playlists), not per playlist. Confirm.
15. **Importing macOS data** (playlists.json, library.json, catalog caches). The Swift encodings are idiosyncratic: dates count seconds from 2001-01-01, and enums with associated values use `{"xtream":{"server":…,"username":…}}` / `{"m3u":{"_0":…}}`. *Proposal: no import; Windows defines its own JSON schema with the same fields and semantics.*
16. **Secret store choice on Windows**: Credential Manager or DPAPI-encrypted file. A related point: should the M3U URL (which often embeds credentials) also be protected? That would close a known macOS limitation.
17. **The account badge's "Expires" date** uses the provider's `exp_date` in unix seconds; `server_info.timezone` is decoded but unused. Should the badge show the server timezone?
18. **`allowed_output_formats` is strictly a string array.** A panel sending a comma-separated string would be read as empty, and the automatic format would resolve to `ts`.
19. **Locked categories while searching.** The search excludes locked categories, but a category unlocked this session is included. A locked category selected *while searching* shows search results, not the prompt. Parity is assumed.
20. **The live sort menu** offers only Default and Title. If the stored sort is `recentlyAdded` or `rating` (it's session-only, so this can't really happen), channels have no date or rating and fall back to provider order.
