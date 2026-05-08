# NewsBroadcaster — Plugin Documentation

**Plugin:** NewsBroadcaster  
**Version:** 1.1.4  
**Author:** DEDA  
**Framework:** Oxide / uMod (Rust)

---

## Overview

NewsBroadcaster is a Rust server plugin that lets admins post rich in-game news announcements with a modern CUI (Client UI). Announcements can include a title, image, body text, and a category badge. Players receive a pop-up notification when new news is posted and can browse the announcement archive at any time.

Optional integrations: **ImageLibrary** (for cached images), **Notify** (for third-party notification popups), and **Discord Webhooks**.

---

## Features

- Full-featured in-game announcement editor (create / edit / delete via UI)
- Archive list with paged navigation
- Per-announcement like/heart button for players
- Scrollable long-form content body with page indicator
- Auto-close timer for pop-ups
- "Show on connect" — shows the latest unseen announcement to joining players
- 4 built-in UI themes (Default, Dark, Ocean, Rust) — switchable in-game
- Discord webhook integration with embed colours per announcement category
- Sound effect on notification
- ImageLibrary support for image caching
- Notify plugin integration (optional)
- Optional item rewards for reading announcements and/or liking them (once per player per announcement)
- **Pinned announcements** — flag posts so they always sit on top of the archive and admin list
- **Bulk admin operations** — multi-select rows, then delete / pin / unpin in one click
- **API hooks** — `OnNewsBroadcast`, `OnNewsEdited`, `OnNewsDeleted`, `OnNewsRead`, `OnNewsLiked` for integrations with other plugins

---

## Dependencies

| Plugin | Required | Purpose |
|---|---|---|
| ImageLibrary | Optional | Cache remote images for faster display |
| Notify | Optional | Use third-party notification popup style |
| ServerRewards | Optional | Grant RP (reward points) on read / like |
| Economics | Optional | Grant currency on read / like |

---

## Permissions

| Permission | Description |
|---|---|
| `newsbroadcaster.admin` | Full admin access — create, edit, delete, post, manage themes |
| `newsbroadcaster.view` | Access to `/news` (archive viewer) |

Grant permissions with:
```
oxide.grant user <SteamID> newsbroadcaster.view
oxide.grant group default newsbroadcaster.view
oxide.grant user <SteamID> newsbroadcaster.admin
```

---

## Chat Commands

| Command | Permission | Description |
|---|---|---|
| `/news` | `newsbroadcaster.view` | Open the news archive UI |

---

## Console / RCON Commands

| Command | Permission | Description |
|---|---|---|
| `news.show "Title" "ImageURL" "Body" [Category]` | Admin | Post a new announcement from console/RCON |
| `news.trigger <SteamID/Name> [index]` | Admin | Force-show a popup to a specific player |
| `news.delete <index>` | Admin | Delete an announcement by its list index (0 = newest) |
| `news.admin` | Admin | Open the in-game admin management panel |

### `news.show` Usage

```
news.show "Patch 1.2 Released" "https://example.com/img.png" "Server updated to 1.2." Changelog
news.show "Double XP Weekend" "-" "Bonus XP all weekend long." Event
```

- Use `"-"` as the image URL to post without an image.
- Supported categories: `Changelog`, `News`, `Event` (case-insensitive, defaults to `News`).
- The `[Category]` argument is only treated as a category when passed unquoted at the end of the line. Words inside the quoted body that happen to match a category name (e.g. `"Status: Changelog"`) are preserved verbatim.

### Announcement Categories & Colours

| Category | Colour |
|---|---|
| Changelog | Cyan |
| News | Blue |
| Event | Purple |

---

## Internal Console Commands (UI-driven, not for manual use)

These are called by CUI buttons and should not be run directly:

All UI-driven commands address announcements by their stable `Id` (a hex GUID assigned on creation), not by list position — this prevents a button from acting on the wrong announcement when posts are added/removed concurrently.

| Command | Description |
|---|---|
| `news.page <page>` | Navigate archive pages |
| `news.view <id>` | Open a specific announcement popup |
| `news.close` | Close the main UI |
| `news.close.notif` | Dismiss the notification toast |
| `news.scrollbody <id> <offset>` | Scroll the announcement body to a line offset |
| `news.like <id>` | Toggle like on an announcement |
| `news.admin.page <page>` | Navigate admin list pages |
| `news.admin.create` | Open the new-announcement editor |
| `news.admin.edit <id>` | Open the editor for an existing announcement |
| `news.admin.del <id>` | Delete an announcement via admin UI |
| `news.admin.delconfirm <id>` | Show the delete-confirmation dialog |
| `news.admin.togglepin <id> [page]` | Toggle the Pinned flag on an announcement |
| `news.admin.toggleselect <id> [page]` | Toggle the bulk-action selection for an announcement |
| `news.admin.selectpage <page>` | Select / deselect every announcement on the current page |
| `news.admin.clearsel [page]` | Clear the entire bulk-action selection |
| `news.admin.bulkdelconfirm [page]` | Show the bulk-delete confirmation dialog |
| `news.admin.bulkdel [page]` | Delete every announcement in the current selection |
| `news.admin.bulkpin <0\|1> [page]` | Pin (`1`) or unpin (`0`) every announcement in the current selection |
| `news.admin.themes` | Open the theme selector UI |
| `news.admin.settheme "ThemeName"` | Apply a theme |
| `news.editor.input <field> <value>` | Update a field in the editor (`title` / `image` / `text`) |
| `news.editor.category` | Cycle the announcement category in the editor |
| `news.editor.save` | Save and broadcast the edited/new announcement |
| `news.editor.cancel` | Cancel editing and return to admin list |
| `news.confirm.close` | Dismiss the delete-confirmation dialog |

### Body scrollbar

Long announcements use a paged scrollbar with four navigation buttons and a clickable track:

| Button | Action |
|---|---|
| `▲▲` | Page up (jumps a full visible page) |
| `▲` | Line up (one line) |
| Track click | Jumps to the clicked position (14 zones) |
| `▼` | Line down (one line) |
| `▼▼` | Page down (jumps a full visible page) |

The handle height visually reflects the visible-window-to-content ratio.

---

## Configuration (`oxide/config/NewsBroadcaster.json`)

```json
{
  "General": {
    "AutoCloseSeconds": 15,
    "EnableAutoClose": true,
    "ShowNewsOnConnect": true,
    "ServerName": "SERVER NEWS",
    "AnnouncementsPerPage": 5,
    "MaxStoredAnnouncements": 50
  },
  "Notification": {
    "Enabled": true,
    "UseNotifyPlugin": false,
    "NotifyType": 0,
    "Position": "Right",
    "Duration": 8,
    "NotificationSound": "assets/bundled/prefabs/fx/notice/loot.drag.fx.prefab"
  },
  "Discord": {
    "Enabled": false,
    "WebhookUrl": "",
    "BotName": "Server News",
    "RoleMention": ""
  },
  "Rewards": {
    "EnableReadReward": false,
    "ReadDelaySeconds": 5,
    "ReadRewards": {
      "Items": [{ "Shortname": "scrap", "Amount": 5, "SkinId": 0 }],
      "Points": 0,
      "Currency": 0.0
    },
    "EnableLikeReward": false,
    "LikeRewards": {
      "Items": [{ "Shortname": "scrap", "Amount": 10, "SkinId": 0 }],
      "Points": 0,
      "Currency": 0.0
    },
    "NotifyOnReward": true,
    "PointsLabel": "RP",
    "CurrencyLabel": "coins"
  },
  "SelectedTheme": "Default",
  "Themes": {
    "Default": { ... },
    "Dark":    { ... },
    "Ocean":   { ... },
    "Rust":    { ... }
  }
}
```

### General Settings

| Key | Default | Description |
|---|---|---|
| `AutoCloseSeconds` | `15` | Seconds before a broadcast popup auto-closes |
| `EnableAutoClose` | `true` | Toggle auto-close for broadcast popups |
| `ShowNewsOnConnect` | `true` | Show latest unseen announcement when a player wakes |
| `ServerName` | `"SERVER NEWS"` | Label shown in the UI header |
| `AnnouncementsPerPage` | `5` | Archive / admin list rows per page |
| `MaxStoredAnnouncements` | `50` | Maximum announcements kept in the data file |

### Notification Settings

| Key | Default | Description |
|---|---|---|
| `Enabled` | `true` | Use the built-in toast notification |
| `UseNotifyPlugin` | `false` | Delegate notification to the Notify plugin |
| `NotifyType` | `0` | Notify plugin notification type index |
| `Position` | `"Right"` | Toast position: `"Right"` or `"Left"` |
| `Duration` | `8` | Seconds the toast is visible |
| `NotificationSound` | loot drag fx | Sound asset path played on notification |

### Discord Settings

| Key | Default | Description |
|---|---|---|
| `Enabled` | `false` | Enable Discord webhook posting |
| `WebhookUrl` | `""` | Discord channel webhook URL |
| `BotName` | `"Server News"` | Webhook display name |
| `RoleMention` | `""` | Role mention string (e.g. `<@&123456>`) |

Note: the webhook payload sets `allowed_mentions: { parse: ["roles"] }`, so body text and titles cannot trigger `@everyone` or `@here` even if a user types those literally.

### Reward Settings

| Key | Default | Description |
|---|---|---|
| `EnableReadReward` | `false` | Grant the read bundle when a player keeps an announcement popup open for `ReadDelaySeconds` |
| `ReadDelaySeconds` | `5` | Seconds the popup must remain open before the read reward fires |
| `ReadRewards` | `{ Items: [scrap × 5], Points: 0, Currency: 0 }` | What to grant on a successful read |
| `EnableLikeReward` | `false` | Grant the like bundle when a player likes (♥) an announcement |
| `LikeRewards` | `{ Items: [scrap × 10], Points: 0, Currency: 0 }` | What to grant on the first like |
| `NotifyOnReward` | `true` | Whisper the player a chat message listing what they received |
| `PointsLabel` | `"RP"` | Label shown after the points amount in the chat message |
| `CurrencyLabel` | `"coins"` | Label shown after the currency amount in the chat message |

Each `RewardBundle` (`ReadRewards` / `LikeRewards`) has three sections — any combination may be set:

```json
{
  "Items":    [ { "Shortname": "scrap", "Amount": 5, "SkinId": 0 } ],
  "Points":   25,
  "Currency": 100.0
}
```

- **`Items`** — list of Rust items. Use any valid Rust shortname (`scrap`, `wood`, `stones`, `metal.refined`, etc.). `SkinId` is optional (default `0`).
- **`Points`** — integer RP (reward points) deposited via the [ServerRewards](https://umod.org/plugins/server-rewards) plugin. Set to `0` to skip.
- **`Currency`** — number of currency units deposited via the [Economics](https://umod.org/plugins/economics) plugin. Set to `0` to skip.

Behavior details:

- Both rewards are granted **at most once per announcement per player**. Subsequent re-reads or like-toggles do not re-trigger.
- The read reward only fires if the popup is still open when the timer elapses, so closing the popup early (or auto-close finishing first if `AutoCloseSeconds < ReadDelaySeconds`) skips the reward.
- The like reward fires on the *first* like only; un-liking does not refund or re-trigger.
- If the player's inventory is full, items are dropped at their feet.
- Points / Currency are silently skipped (with a server-console warning) if `ServerRewards` / `Economics` are not loaded. The other reward components still apply.
- Invalid item `Shortname`s are skipped with a warning; other items in the list still apply.

Plugin upgrades from the previous reward schema (`ReadRewards` / `LikeRewards` as bare JSON arrays) are migrated automatically — the existing item list is wrapped into `Items` and `Points` / `Currency` start at `0`.

### Theme Colors (`UIColors`)

Each theme exposes these RGBA string fields (`"R G B A"`, values 0–1):

| Field | Description |
|---|---|
| `PanelBg` | Main panel background |
| `HeaderBg` | Header / footer bar background |
| `ContentBg` | Archive list item background |
| `ButtonPrimary` | Primary action button colour |
| `ButtonSecondary` | Secondary / nav button colour |
| `TextTitle` | Title text colour |
| `TextNormal` | Body text colour |
| `TextMuted` | Hint / meta text colour |

---

## Pinned Announcements

Any announcement can be **pinned** so that it always sits at the top of the in-game archive (`/news`) and the admin list, regardless of its timestamp. Use cases: server rules, wipe schedule, donation links, current event banners.

- Toggle the pin from the admin list — every row has a **📌 PIN** / **📌 UNPIN** button next to **EDIT** / **DEL**. The button itself turns gold when the row is pinned, so admin lists at a glance show pin state.
- Pinned posts get a coherent gold treatment everywhere a player can see them:
  - **Archive list (`/news`)**: the pinned row gets a subtle gold tint and a solid gold **PINNED** chip between the title and the date.
  - **Admin list**: the pinned row gets the same gold tint, and the per-row pin button switches to a gold pill so it visually matches.
  - **Announcement popup**: a thin gold frame outlines the entire popup, and a solid gold **PINNED** chip sits in the top-right corner of the header bar — impossible to miss.
- Sort order: pinned (newest first) → unpinned (newest first). Insertion order in the underlying data is unchanged, so "show on connect" and `news.list` still surface the most recently broadcast announcement.

A console-side toggle is also available:

```
news.admin.togglepin <id> [returnPage]
```

`<id>` is the announcement's hex id (find it via `news.list`). `returnPage` is the admin-list page to re-render afterwards; defaults to `0`.

---

## Bulk Admin Operations

The admin list now supports multi-selection. Each row has a checkbox on the far left; click it to add the announcement to your selection set. While the selection is non-empty, a toolbar appears above the page footer:

| Toolbar button | Action |
|---|---|
| `✕ DELETE` | Open a confirmation dialog, then delete every selected announcement |
| `📌 PIN ALL` | Pin every selected announcement (fires `OnNewsEdited` per change) |
| `📌 UNPIN ALL` | Unpin every selected announcement |
| `CLEAR` | Empty the selection without affecting any data |
| `☑ SELECT PAGE` | Toggle: select every announcement currently visible on the page; if everything on the page is already selected, deselect them all |

Behavior details:

- The selection is **per-admin** and lives only in memory; it is dropped when the player disconnects, on `Unload`, and after a successful bulk-delete.
- Selecting items on one page and then paging to another does **not** lose the selection — the toolbar always reflects the global count.
- Stale ids (announcements deleted by another admin while you had them selected) are pruned automatically before any bulk action.
- Bulk delete fires `OnNewsDeleted` once **per** removed announcement.
- Permissions: every bulk command requires `newsbroadcaster.admin` (or the F1 `IsAdmin` flag), exactly like the single-item operations.

Console equivalents (mostly used by the UI buttons):

```
news.admin.toggleselect <id> [page]
news.admin.selectpage <page>
news.admin.clearsel [page]
news.admin.bulkdelconfirm [page]
news.admin.bulkdel [page]
news.admin.bulkpin <0|1> [page]
```

---

## API Hooks

Other plugins can react to NewsBroadcaster events by subscribing to these hooks. All hook payloads are plain primitive types (`Dictionary<string, object>`, `BasePlayer`, `bool`, etc.) so consumers don't need to reference NewsBroadcaster's internal types.

| Hook | Signature | When it fires |
|---|---|---|
| `OnNewsBroadcast` | `void OnNewsBroadcast(Dictionary<string, object> ann)` | A new announcement is published (RCON `news.show` or admin editor save with no existing id). |
| `OnNewsEdited` | `void OnNewsEdited(Dictionary<string, object> ann)` | An existing announcement's body, category, or pinned-state is updated through the admin editor or `news.admin.togglepin` / bulk pin. |
| `OnNewsDeleted` | `void OnNewsDeleted(Dictionary<string, object> ann)` | An announcement is removed (RCON `news.delete`, single delete, or any bulk delete — fires once per victim). |
| `OnNewsRead` | `void OnNewsRead(BasePlayer player, Dictionary<string, object> ann)` | A player has kept an announcement popup open for `Rewards.ReadDelaySeconds` (default 5s). Fires at most **once per player per announcement**, regardless of whether read rewards are enabled. |
| `OnNewsLiked` | `void OnNewsLiked(BasePlayer player, Dictionary<string, object> ann, bool added)` | A player toggles the heart on an announcement. `added == true` for a like, `false` for an un-like. |

The announcement payload dictionary contains:

| Key | Type | Notes |
|---|---|---|
| `id` | `string` | Stable hex Guid for the announcement |
| `title` | `string` | |
| `author` | `string` | |
| `category` | `string` | One of `Changelog`, `News`, `Event` |
| `timestamp` | `long` | `DateTime.UtcNow.Ticks` at creation |
| `date` | `string` | `yyyy-MM-dd HH:mm` invariant-culture timestamp shown in the UI |
| `text` | `string` | Body text (with `\n` line breaks) |
| `imageUrl` | `string` | May be empty |
| `likes` | `int` | Current number of likes |
| `pinned` | `bool` | |

### Example consumer

```csharp
using System.Collections.Generic;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("NewsListener", "you", "1.0.0")]
    class NewsListener : RustPlugin
    {
        void OnNewsBroadcast(Dictionary<string, object> ann)
        {
            Puts($"[News] {ann["author"]} posted '{ann["title"]}' ({ann["category"]})");
        }

        void OnNewsRead(BasePlayer player, Dictionary<string, object> ann)
        {
            Puts($"{player.displayName} read announcement {ann["id"]}");
        }

        void OnNewsLiked(BasePlayer player, Dictionary<string, object> ann, bool added)
        {
            Puts($"{player.displayName} {(added ? "liked" : "un-liked")} {ann["title"]}");
        }
    }
}
```

---

## Data File (`oxide/data/NewsBroadcaster_Data.json`)

Stores all announcements and per-player last-seen timestamps. The plugin handles migration from the legacy format (plain list) automatically.

**Announcement object fields:**

| Field | Type | Description |
|---|---|---|
| `Title` | string | Announcement title |
| `ImageUrl` | string | Remote image URL (empty = no image) |
| `Text` | string | Body text (supports `\n` for line breaks) |
| `Date` | string | Formatted post date (`MM/dd HH:mm`) |
| `Author` | string | Poster's display name or server name |
| `Category` | enum | `Changelog / News / Event` |
| `Timestamp` | long | UTC ticks — used for ordering and last-seen |
| `LikedPlayers` | HashSet\<ulong\> | Steam IDs of players who liked this post |

---

## Localization

All UI strings are registered in Oxide's lang system and can be overridden per language in `oxide/lang/<lang>/NewsBroadcaster.json`.

Default keys: `NoPermissionCommand`, `NoPermissionView`, `NoNewsHistory`, `NewsBroadcasted`, `ArchiveTitle`, `ReadMore`, `ViewArchive`, `PostedBy`, `NewAnnouncement`, `Close`, `Previous`, `Next`, `Page`, `AdminControl`, `NewPost`, `Themes`, `NoAnnouncementsYet`, `CreateAnnouncement`, `EditAnnouncement`, `AnnouncementTitle`, `ImageUrl`, `AnnouncementCategory`, `ContentBody`, `ContentBodyHint`, `SaveBroadcast`, `Cancel`, `SelectTheme`, `Active`, `Unknown`.

---

## Code Review — Issues & Suggestions

### Bugs

#### 1. Notification toast always opens announcement index 0
**File:** `NewsBroadcaster.cs:959`  
The notification click button hardcodes `Command = "news.view 0"`, so clicking any toast always opens the first (newest) announcement — not the one that was actually broadcasted.

**Fix:** Determine the index of `ann` before building the container and use `$"news.view {announcementIndex}"`.

---

#### 2. `news.delete` references unimplemented `news.list`
**File:** `NewsBroadcaster.cs:622`  
The error message says `"Invalid index. Use 'news.list' (not implemented, check data file)..."` which is confusing.

**Fix:** Implement `news.list` (see Missing Features below) or remove the reference.

---

#### 3. Edit broadcast spams all players for edits
**File:** `NewsBroadcaster.cs:869–875`  
`CmdEditorSave` sends a notification/popup to every online player whether the post is new (`index == -1`) **or an edit** (`index >= 0`). Editing an old announcement should not re-notify all players.

**Fix:** Only broadcast when `index == -1`.

```csharp
if (index == -1)
{
    foreach (var p in BasePlayer.activePlayerList)
    {
        if (config.Notification.Enabled)
            ShowNotification(p, ann);
        else
            ShowPopup(p, ann, false, true);
    }
}
```

---

#### 4. `LikedPlayers` edited by reference on edit
**File:** `NewsBroadcaster.cs:758`  
`LikedPlayers = original.LikedPlayers` copies the reference. If `announcements[index]` is later replaced with the edited copy, both objects share the same `HashSet`. This is harmless today, but is a subtle bug waiting to surface.

**Fix:** `LikedPlayers = new HashSet<ulong>(original.LikedPlayers)`

---

#### 5. `ShowPopup` mutates the stored announcement object
**File:** `NewsBroadcaster.cs:1018`  
`ann.Text = NormalizeBodyText(ann.Text);` is called inside `ShowPopup` on whatever `ann` reference is passed in. For stored announcements this results in a harmless double-normalization, but it is a side effect that makes the method impure.

**Fix:** Normalize text only on write (which already happens in `CmdEditorSave` / `CmdNewsShow`). Remove the mutation from `ShowPopup`.

---

### Memory Leaks

#### 6. Per-player dictionaries never cleaned up on disconnect
**File:** `NewsBroadcaster.cs:39–40`  
`historyContentScrollOffsets`, `activeEditors`, `activeEditorIndices`, `autoCloseTimers`, and `notificationTimers` are indexed by `ulong userID` but are never cleaned up when a player disconnects.

**Fix:** Add an `OnPlayerDisconnected` hook:

```csharp
void OnPlayerDisconnected(BasePlayer player, string reason)
{
    historyContentScrollOffsets.Remove(player.userID);
    activeEditors.Remove(player.userID);
    activeEditorIndices.Remove(player.userID);
    // autoCloseTimers and notificationTimers are already cleaned in DestroyUI/DestroyNotification
}
```

---

### Security / Permission

#### 7. `news.admin.page` silently ignores unauthorized access
**File:** `NewsBroadcaster.cs:712–715`  
Unlike other admin commands, `CmdNewsAdminPage` checks permission but does not send a reply when denied — it just returns silently. This is inconsistent.

**Fix:** Add `SendReply(arg, Msg("NoPermissionCommand"));` before the return.

---

#### 8. URL stripping in `news.show` body is undocumented and silent
**File:** `NewsBroadcaster.cs:523`  
`text = Regex.Replace(text, @"https?:\/\/[^\s]+", "").Trim();` strips all URLs from body text posted via the console command. This is not mentioned in any help text, so admins pasting content with links will silently lose those links.

**Fix:** Either document this behavior in the usage reply, or remove the stripping and let admins post URLs freely (the UI editor does not strip URLs, so the behavior is already inconsistent).

---

### Missing Features

#### 9. `news.list` console command
The admin `news.delete` error message references it, and it is a natural companion to the other console commands.

**Suggested implementation:** Print a numbered list of all announcements with title and date to the console/RCON caller.

---

#### 10. `OnPlayerDisconnected` cleanup (also listed as bug above)
Required to prevent unbounded memory growth on high-population servers.

---

#### 11. Delete confirmation in admin UI
Currently clicking `DEL` in the admin panel deletes immediately with no confirmation dialog. A misclick destroys an announcement.

**Suggestion:** Add a small confirmation panel (`"Are you sure? [YES] [NO]"`) before executing `news.admin.del`.

---

#### 12. `news.list` / `news.reload` RCON commands
Useful for server management without having to be in-game:
- `news.list` — prints all stored announcements with their index.
- `news.reload` — reloads the data file (useful after manual edits).

---

#### 13. Notification position `"Center"` option
Currently only `"Left"` and `"Right"` are supported. A top-center option would suit some server HUD layouts.

---

#### 14. Theme preview in the theme selector
The theme selector shows theme names but admins must apply the theme before seeing its colours. A small colour-swatch row per theme would greatly improve usability.

---

#### 15. Auto-broadcast / scheduled announcements
There is no way to schedule a recurring news post (e.g., post server rules every 30 minutes). A `Broadcast` timer list in config (similar to how other Rust plugins handle `AutoMessage`) would add significant value.

---

### Code Quality

#### 16. `LoadDefaultConfig` and `LoadConfig` migration block are duplicated
The 4 theme definitions (`Default`, `Dark`, `Ocean`, `Rust`) are copy-pasted verbatim between `LoadDefaultConfig` (line 308) and the migration block inside `LoadConfig` (line 283). Extract them to a helper method `BuildDefaultThemes()`.

---

#### 17. `DateTime.Now` vs `DateTime.UtcNow` mixed usage
`ann.Date = DateTime.Now.ToString("MM/dd HH:mm")` uses local server time for display.  
`ann.Timestamp = DateTime.UtcNow.Ticks` uses UTC for ordering.  
This is fine, but a config option for timezone offset or a note in the docs would help server operators who host in different regions.

---

#### 18. `RgbaToHex` ignores alpha channel
Used only for rich-text colour tags (e.g., `<color=#RRGGBB>`) which do not support alpha — so the behaviour is intentionally correct, but the method name is slightly misleading. Rename to `RgbToHex` or add a comment.

---

#### 19. Hardcoded body wrap at 52 chars
`BodyWrapCharacters = 52` is a magic number tied to the current UI panel width and font size. If the panel is ever resized or font changed, text will wrap incorrectly. Consider deriving this from UI constants or making it configurable.

---

## Quick-Fix Priority

| Priority | Item |
|---|---|
| High | Bug #3 — edit broadcasts to all players |
| High | Bug #6 — memory leak on disconnect |
| High | Bug #1 — notification opens wrong announcement |
| Medium | Bug #2 — misleading `news.list` reference |
| Medium | Bug #4 — LikedPlayers reference copy |
| Medium | Feature #12 — `news.list` console command |
| Medium | Feature #11 — delete confirmation |
| Low | Bug #8 — silent URL stripping |
| Low | Quality #16 — deduplicate theme definitions |
| Low | Feature #15 — scheduled announcements |
