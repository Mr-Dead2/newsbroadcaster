# NewsBroadcaster — Plugin Documentation

**Plugin:** NewsBroadcaster  
**Version:** 1.1.5  
**Author:** DEDA  
**Framework:** Oxide / uMod (Rust)

---

## Overview

NewsBroadcaster is a Rust server plugin that lets admins post rich in-game news announcements with a modern CUI (Client UI). Announcements can include a title, image, body text, and a type badge. Players receive a pop-up notification when new news is posted and can browse the announcement archive at any time.

Optional integrations: **ImageLibrary** (for cached images), **Notify** (for third-party notification popups), and **Discord Webhooks**.

---

## Features

- Full-featured in-game announcement editor (create / edit / delete via UI) with a live preview pane and one-click type selection
- Archive list with paged navigation
- Per-announcement like/heart button for players
- Scrollable long-form content body with page indicator
- Auto-close timer for pop-ups
- "Show on connect" — shows the latest unseen announcement to joining players
- 6 built-in UI themes (Default, Dark, Ocean, Rust, Midnight, Forest) — switchable in-game with per-theme color swatches
- Discord webhook integration with embed colours per announcement type
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
| `news.show "Title" "ImageURL" "Body" [Type]` | Admin | Post a new announcement from console/RCON |
| `news.trigger <SteamID/Name> [index]` | Admin | Force-show a popup to a specific player |
| `news.delete <index>` | Admin | Delete an announcement by its list index (0 = newest) |
| `news.admin` | Admin | Open the in-game admin management panel |

### `news.show` Usage

```
news.show "Server Update" "https://example.com/img.png" "The server has been updated!" Update
news.show "Alert!" "-" "PvP zone closing in 10 minutes." Alert
```

- Use `"-"` as the image URL to post without an image.
- Supported types: `Info`, `Warning`, `Alert`, `Event`, `Update` (case-insensitive, defaults to `Info`).
- The `[Type]` argument is only treated as a type when passed unquoted at the end of the line. Words inside the quoted body that happen to match a type name (e.g. `"Status: Update"`) are preserved verbatim.

### Announcement Types & Colours

| Type | Colour |
|---|---|
| Info | Blue |
| Warning | Orange |
| Alert | Red |
| Event | Purple |
| Update | Cyan |

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
| `news.editor.type` | Cycle the announcement type in the editor (legacy; the editor now uses direct type buttons) |
| `news.editor.settype <0-4>` | Set the announcement type directly (used by the editor's type buttons) |
| `news.editor.save` | Save and broadcast the edited/new announcement (requires a non-empty title) |
| `news.editor.cancel` | Cancel editing and return to admin list |
| `news.confirm.close` | Dismiss the delete-confirmation dialog |

### Body scrollbar

Long announcements use a paged scrollbar with two navigation buttons and a clickable track:

| Button | Action |
|---|---|
| `▲` | Page up (jumps a full visible page) |
| Track click | Jumps to the clicked position (16 zones) |
| `▼` | Page down (jumps a full visible page) |

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
| `OnNewsEdited` | `void OnNewsEdited(Dictionary<string, object> ann)` | An existing announcement's body, type, or pinned-state is updated through the admin editor or `news.admin.togglepin` / bulk pin. |
| `OnNewsDeleted` | `void OnNewsDeleted(Dictionary<string, object> ann)` | An announcement is removed (RCON `news.delete`, single delete, or any bulk delete — fires once per victim). |
| `OnNewsRead` | `void OnNewsRead(BasePlayer player, Dictionary<string, object> ann)` | A player has kept an announcement popup open for `Rewards.ReadDelaySeconds` (default 5s). Fires at most **once per player per announcement**, regardless of whether read rewards are enabled. |
| `OnNewsLiked` | `void OnNewsLiked(BasePlayer player, Dictionary<string, object> ann, bool added)` | A player toggles the heart on an announcement. `added == true` for a like, `false` for an un-like. |

The announcement payload dictionary contains:

| Key | Type | Notes |
|---|---|---|
| `id` | `string` | Stable hex Guid for the announcement |
| `title` | `string` | |
| `author` | `string` | |
| `type` | `string` | One of `Info`, `Warning`, `Alert`, `Event`, `Update` |
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
            Puts($"[News] {ann["author"]} posted '{ann["title"]}' ({ann["type"]})");
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
| `Type` | enum | `Info / Warning / Alert / Event / Update` |
| `Timestamp` | long | UTC ticks — used for ordering and last-seen |
| `LikedPlayers` | HashSet\<ulong\> | Steam IDs of players who liked this post |

---

## Localization

All UI strings are registered in Oxide's lang system and can be overridden per language in `oxide/lang/<lang>/NewsBroadcaster.json`.

Default keys: `NoPermissionCommand`, `NoPermissionView`, `NoNewsHistory`, `NewsBroadcasted`, `ArchiveTitle`, `ReadMore`, `ViewArchive`, `PostedBy`, `NewAnnouncement`, `Close`, `Previous`, `Next`, `Page`, `AdminControl`, `NewPost`, `Themes`, `NoAnnouncementsYet`, `CreateAnnouncement`, `EditAnnouncement`, `AnnouncementTitle`, `ImageUrl`, `AnnouncementType`, `ContentBody`, `ContentBodyHint`, `SaveBroadcast`, `Cancel`, `SelectTheme`, `Active`, `Unknown`, `EditButton`, `DelButton`, `DeleteAnnouncement`, `DeleteConfirmBody`, `ConfirmDelete`, `EditTargetGone`, `AnnouncementSavedNew`, `AnnouncementUpdated`, `RewardRead`, `RewardLike`, `PinButton`, `UnpinButton`, `PinnedBadge`, `SelectedCount`, `BulkDelete`, `BulkPin`, `BulkUnpin`, `ClearSelection`, `SelectPageToggle`, `BulkDeleteTitle`, `BulkDeleteBody`, `BulkDeleted`, `BulkPinned`, `BulkUnpinned`, `LivePreview`, `SaveChanges`, `TitleRequired`, `TypeLabel`, `NoContent`, `CharCount`.

---

## Behavior Notes

- **URLs are stripped from body text.** `NormalizeBodyText` removes `http(s)://` and `www.` links from announcement bodies on save — both for the console command (`news.show`) and the in-game editor. Links belong in the image URL field or in Discord, not in the CUI body.
- **Editing never re-notifies players.** Only newly created announcements trigger the notification toast / popup broadcast. Edits also preserve the announcement's pinned state, likes, and read/like reward tracking.
- **Dates are server-local; ordering is UTC.** `Date` (display) uses the server's local clock; `Timestamp` (sorting, last-seen tracking) uses UTC ticks.
- **Per-player tracking writes are debounced.** Last-seen markers, likes, and read marks are flushed to the data file a few seconds after they change (and on plugin unload / server save). Structural changes (post, edit, delete, pin) are saved immediately.

---

## Known Limitations & Ideas

- Notification toast position supports `"Left"` and `"Right"` only — no top-center option.
- No scheduled / recurring announcements (e.g., re-post rules every 30 minutes).
- No `news.reload` RCON command to re-read a hand-edited data file.
- Body wrap widths (`BodyWrapCharacters = 58`, `34` with an image, `44` in the editor preview) are constants tied to the current popup dimensions and font sizes; resizing the panels requires retuning them.
- The default theme definitions are duplicated between `LoadDefaultConfig` and the migration block in `LoadConfig`.
- The theme selector lists as many themes as fit in the panel; with many custom themes, extra entries must be applied via `news.admin.settheme "Name"`.
