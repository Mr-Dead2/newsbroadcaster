# NewsBroadcaster — Plugin Documentation

**Plugin:** NewsBroadcaster  
**Version:** 2.0.0  
**Author:** DEDA  
**Framework:** Oxide / uMod (Rust)

---

## Overview

NewsBroadcaster is a Rust server plugin that lets admins post rich in-game news announcements with a modern CUI (Client UI). Announcements have a title, an optional image, body text and a coloured type badge. Players get a notification toast when news is posted and can browse the archive at any time with `/news`.

Optional integrations: **ImageLibrary** (image caching), **Notify** (third-party notification style), **ServerRewards** / **Economics** (read & like rewards) and **Discord webhooks**.

---

## What's new in 2.0.0

2.0.0 is built on the 1.2.0 design — same look, same layout, same data and config files — with fixes and new features on top.

**Players**
- **Unread tracking** — `NEW` badges and an "N UNREAD" counter in the archive; unread rows are highlighted.
- **Archive thumbnails** — posts with an image show a small preview in the archive (`ArchiveThumbnails`).
- **Prev / next arrows** in the popup header with a `2 / 6` position counter.
- **Back to archive** — opening a post from the archive returns you to the same archive page.
- **Smoother likes** — liking only updates the heart button, so you keep your scroll position in long posts.
- **Long titles fit** — the title font shrinks automatically and wraps onto two lines instead of being cut off.
- **Unread reminder** — on connect, players get one chat line about posts they missed since their last visit.
- **New chat commands** — `/news latest`, `/news help`, and `/news admin` for admins.
- **Bigger toast** with the post type in colour and a "click to read" hint.

**Admins**
- **Type chips** — all five types are visible as one-click buttons (no more cycling).
- **Options** — `PIN TO TOP`, `NOTIFY ONLINE PLAYERS` and `POST TO DISCORD` toggles in the editor.
- **Preview** — see the exact popup players will get before you publish.
- **Typing no longer loses focus** — the editor isn't rebuilt after every field, so you can click straight into the next field.
- **RESEND** — re-notify online players about an existing post (admin row button or `news.resend`).
- **Reads / likes per post** in the admin list, plus the type and an `EDITED` marker.
- **Theme cards with colour swatches**, pagination for more than 8 themes, and a panel that fits its contents.
- Deleting from page 2+ keeps you on that page.

**Fixes**
- The 6th theme button no longer spills outside the theme panel.
- Notification sounds are sent **only to the player** — nearby players no longer hear everyone's chimes.
- ServerRewards points are paid correctly on current Rust builds (the player id is passed as a plain `ulong`).
- `/n` no longer breaks normal words like `w/new` or `and/no` (`\n` and `/n` before a capital, digit or space still make a new line).
- Players already in a NewsBroadcaster screen (e.g. an admin mid-edit) get a toast instead of having their screen replaced by a forced popup.
- Players who saw a live broadcast don't get the same post again as a popup when they respawn.
- Pinned posts are never removed by `MaxStoredAnnouncements` trimming.
- The popup title outline now actually renders (it was attached to an empty element).
- All UI coordinates are formatted culture-independently (safe on servers with a comma decimal separator).
- While ImageLibrary is still downloading an image, the client loads it from the URL instead of showing a placeholder.
- Likes, reads and "last seen" are saved with a 15 s delay instead of rewriting the data file on every click; everything is flushed on server save and unload.

---

## Dependencies

| Plugin | Required | Purpose |
|---|---|---|
| ImageLibrary | Optional | Cache remote images for faster display |
| Notify | Optional | Use a third-party notification popup style |
| ServerRewards | Optional | Grant RP (reward points) on read / like |
| Economics | Optional | Grant currency on read / like |

---

## Permissions

| Permission | Description |
|---|---|
| `newsbroadcaster.admin` | Full admin access — create, edit, delete, pin, resend, themes |
| `newsbroadcaster.view` | See news: archive, popups and toasts (not needed when `RequireViewPermission` is `false`) |

```
oxide.grant group default newsbroadcaster.view
oxide.grant user <SteamID> newsbroadcaster.admin
```

---

## Chat Commands

| Command | Who | Description |
|---|---|---|
| `/news` | viewers | Open the news archive |
| `/news latest` | viewers | Open the newest announcement |
| `/news help` | everyone | List the commands |
| `/news admin` | admins | Open news management |

---

## Console / RCON Commands

| Command | Permission | Description |
|---|---|---|
| `news.show "Title" "ImageURL" "Body" [Type]` | Admin | Post a new announcement (notifies players, posts to Discord) |
| `news.list` | Admin | List stored announcements with index, id, reads and likes |
| `news.delete <index\|id>` | Admin | Delete an announcement |
| `news.resend <index\|id>` | Admin | Re-send the notification for an announcement to online players |
| `news.trigger <SteamID/Name> [index]` | Admin | Force-show a popup to one player |
| `news.admin` | Admin | Open the in-game admin panel |

Index `0` is always the newest announcement.

### `news.show` usage

```
news.show "Server Update" "https://example.com/img.png" "The server has been updated!" Update
news.show "Alert!" "-" "PvP zone closing in 10 minutes." Alert
```

- Use `"-"` as the image URL to post without an image.
- Types: `Info`, `Warning`, `Alert`, `Event`, `Update` (case-insensitive, default `Info`). The type is only read when it is the last, unquoted word.
- Use `\n` for a new line.

### Announcement types

| Type | Colour |
|---|---|
| Info | Blue |
| Warning | Orange |
| Alert | Red |
| Event | Purple |
| Update | Cyan |

---

## The editor

`/news admin` → **NEW ANNOUNCEMENT** (or **EDIT** on a row).

| Control | What it does |
|---|---|
| Title / Image URL / Content body | Text fields. Enter (or `\n`) makes a new line in the body. An invalid image URL is rejected with a chat message |
| Type chips | Pick one of the five types |
| **PIN TO TOP** | Pin the post (same as the PIN button in the list) |
| **NOTIFY ONLINE PLAYERS** | New posts: send the toast/popup to everyone online (on by default). Edits: **RE-NOTIFY PLAYERS**, off by default |
| **POST TO DISCORD** | New posts only, shown when the Discord webhook is configured |
| **PREVIEW** | Shows the popup exactly as players will see it. Nothing is published; **BACK TO EDITOR** returns with your draft intact |
| **PUBLISH / SAVE CHANGES** | A title is required |

Editing keeps likes, reads and reward history, and adds an `EDITED` marker.

---

## Internal console commands (UI buttons)

These are run by CUI buttons. They address announcements by their stable id, not list position.

| Command | Description |
|---|---|
| `news.page <page>` | Archive page |
| `news.view <id> [archive\|direct]` | Open a post (`archive` = "back to archive" button) |
| `news.like <id>` | Toggle like |
| `news.close` / `news.close.notif` | Close the UI / the toast |
| `news.admin.page <page>` | Admin list page |
| `news.admin.create` / `news.admin.edit <id>` | Open the editor |
| `news.admin.togglepin <id> [page]` | Pin / unpin |
| `news.admin.resend <id> [page]` | Re-notify online players |
| `news.admin.delconfirm <id> [page]` / `news.admin.del <id> [page]` | Delete with confirmation |
| `news.admin.toggleselect <id> [page]`, `news.admin.selectpage <page>`, `news.admin.clearsel [page]` | Bulk selection |
| `news.admin.bulkpin <0\|1> [page]`, `news.admin.bulkdelconfirm [page]`, `news.admin.bulkdel [page]` | Bulk actions |
| `news.admin.themes [page]` / `news.admin.settheme "Name" [page]` | Theme picker |
| `news.editor.input <title\|image\|text> <value>` | Editor field |
| `news.editor.settype <Type>` / `news.editor.type` | Set / cycle the type |
| `news.editor.toggle <pin\|notify\|discord>` | Editor options |
| `news.editor.preview` / `news.editor.back` | Preview and return |
| `news.editor.save` / `news.editor.cancel` | Save / cancel |
| `news.confirm.close` | Close a confirmation dialog |

---

## Configuration (`oxide/config/NewsBroadcaster.json`)

Existing configs are upgraded in place: your values and themes are kept and new options are added with their defaults.

```json
{
  "General": {
    "AutoCloseSeconds": 15,
    "EnableAutoClose": true,
    "ShowNewsOnConnect": true,
    "UnreadReminderOnConnect": true,
    "RequireViewPermission": true,
    "ServerName": "SERVER NEWS",
    "AnnouncementsPerPage": 5,
    "MaxStoredAnnouncements": 50,
    "StripLinksFromBody": true,
    "PopupImageFrame": "Tall",
    "ArchiveThumbnails": true
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
    "AvatarUrl": "",
    "RoleMention": ""
  },
  "Rewards": {
    "EnableReadReward": false,
    "ReadDelaySeconds": 5,
    "ReadRewards": { "Items": [{ "Shortname": "scrap", "Amount": 5, "SkinId": 0 }], "Points": 0, "Currency": 0.0 },
    "EnableLikeReward": false,
    "LikeRewards": { "Items": [{ "Shortname": "scrap", "Amount": 10, "SkinId": 0 }], "Points": 0, "Currency": 0.0 },
    "NotifyOnReward": true,
    "PointsLabel": "RP",
    "CurrencyLabel": "coins"
  },
  "SelectedTheme": "Default",
  "Themes": { "Default": { }, "Dark": { }, "Ocean": { }, "Rust": { }, "Midnight": { }, "Forest": { } }
}
```

### General

| Key | Default | Description |
|---|---|---|
| `AutoCloseSeconds` | `15` | Seconds before a broadcast / connect popup closes by itself |
| `EnableAutoClose` | `true` | Auto-close broadcast and connect popups (popups a player opens never auto-close) |
| `ShowNewsOnConnect` | `true` | Show the newest post to a player who has not seen it yet when they wake up |
| `UnreadReminderOnConnect` | `true` | Once per session, tell the player in chat how many posts they missed since their last visit |
| `RequireViewPermission` | `true` | `false` = everyone can see news without `newsbroadcaster.view` |
| `ServerName` | `"SERVER NEWS"` | Label in the UI headers and the Discord embed |
| `AnnouncementsPerPage` | `5` | Rows per archive / admin page (1–8) |
| `MaxStoredAnnouncements` | `50` | Max posts kept (1–500). Oldest unpinned posts are removed first; pinned posts are kept |
| `StripLinksFromBody` | `true` | Remove `http(s)://` and `www.` links from new post bodies (the 1.2.0 behaviour). Existing posts are never changed |
| `PopupImageFrame` | `"Tall"` | `"Tall"` fills the popup's left column (1.2.0 look). `"Wide"` uses a 16:9 frame so widescreen images aren't stretched |
| `ArchiveThumbnails` | `true` | Show image thumbnails in the archive |

### Notification

| Key | Default | Description |
|---|---|---|
| `Enabled` | `true` | Show the toast for new posts. `false` = open the popup directly instead |
| `UseNotifyPlugin` | `false` | Send the notification through the Notify plugin |
| `NotifyType` | `0` | Notify plugin type index |
| `Position` | `"Right"` | Toast position: `"Right"` or `"Left"` |
| `Duration` | `8` | Seconds the toast stays visible |
| `NotificationSound` | loot drag fx | Sound prefab, played only for the receiving player. Empty = no sound |

### Discord

| Key | Default | Description |
|---|---|---|
| `Enabled` | `false` | Post new announcements to Discord |
| `WebhookUrl` | `""` | Channel webhook URL |
| `BotName` | `"Server News"` | Webhook display name (empty = webhook default) |
| `AvatarUrl` | `""` | Optional webhook avatar image URL |
| `RoleMention` | `""` | Sent above the embed, e.g. `<@&123456789>`, `@here` or `@everyone` |

The embed has a type-coloured side bar, a `TYPE • SERVER NAME` header, the title, body (truncated at 4000 characters), image, "Posted by" footer and Discord's timestamp. Only the configured `RoleMention` can ping — text inside titles and bodies never does.

### Rewards

| Key | Default | Description |
|---|---|---|
| `EnableReadReward` | `false` | Pay the read bundle when a player keeps a post open for `ReadDelaySeconds` |
| `ReadDelaySeconds` | `5` | Seconds the popup must stay open to count as read |
| `ReadRewards` | scrap × 5 | Read bundle |
| `EnableLikeReward` | `false` | Pay the like bundle on a player's first like of a post |
| `LikeRewards` | scrap × 10 | Like bundle |
| `NotifyOnReward` | `true` | Tell the player in chat what they received |
| `PointsLabel` / `CurrencyLabel` | `"RP"` / `"coins"` | Labels used in that message |

Each bundle can combine **Items** (any shortname, optional `SkinId`), **Points** (ServerRewards) and **Currency** (Economics). Rewards are paid at most once per player per post. Full inventory → items drop at the player's feet. If a needed plugin is missing, the reward is skipped with a console warning and **not** recorded, so it can be paid later. Old configs with bare item arrays are migrated automatically.

### Theme colours

Each theme has these `"R G B A"` (0–1) fields: `PanelBg`, `HeaderBg`, `ContentBg`, `ButtonPrimary`, `ButtonSecondary`, `TextTitle`, `TextNormal`, `TextMuted`. Add your own themes to the `Themes` list; the picker pages through them 8 at a time.

---

## Pinned announcements

Pinned posts always sit at the top of the archive and admin list, with a gold tint, a gold **PINNED** badge and a gold frame around the popup. Pin from the admin row (**PIN / UNPIN**), from the editor (**PIN TO TOP**) or in bulk.

## Bulk admin operations

Tick the checkbox on admin rows (or **SELECT PAGE**) and a toolbar appears: **DELETE** (with confirmation), **PIN ALL**, **UNPIN ALL**, **CLEAR**. The selection is per admin, survives paging, and is dropped on disconnect.

## Read tracking

A post counts as **read** for a player after its popup has stayed open for `ReadDelaySeconds`. That drives the archive's `NEW` badges, the unread counter, the admin list's read counts, the read reward and the `OnNewsRead` hook.

---

## API Hooks

Other plugins can subscribe to these hooks. Payloads use plain types, so consumers don't need NewsBroadcaster's internal classes.

| Hook | Signature | When it fires |
|---|---|---|
| `OnNewsBroadcast` | `void OnNewsBroadcast(Dictionary<string, object> ann)` | A new announcement is published (console or editor) |
| `OnNewsEdited` | `void OnNewsEdited(Dictionary<string, object> ann)` | An announcement is edited, pinned or unpinned |
| `OnNewsDeleted` | `void OnNewsDeleted(Dictionary<string, object> ann)` | An announcement is deleted (single, bulk, console) or trimmed by `MaxStoredAnnouncements` |
| `OnNewsRead` | `void OnNewsRead(BasePlayer player, Dictionary<string, object> ann)` | First time a player reads a post |
| `OnNewsLiked` | `void OnNewsLiked(BasePlayer player, Dictionary<string, object> ann, bool added)` | A player likes (`true`) or un-likes (`false`) a post |

Payload keys: `id` (string), `title`, `author`, `type` (`Info`/`Warning`/`Alert`/`Event`/`Update`), `timestamp` (UTC ticks), `date` (`yyyy-MM-dd HH:mm`), `text`, `imageUrl`, `likes` (int), `pinned` (bool). Unchanged from 1.2.0.

```csharp
void OnNewsBroadcast(Dictionary<string, object> ann)
{
    Puts($"[News] {ann["author"]} posted '{ann["title"]}' ({ann["type"]})");
}

void OnNewsLiked(BasePlayer player, Dictionary<string, object> ann, bool added)
{
    Puts($"{player.displayName} {(added ? "liked" : "un-liked")} {ann["title"]}");
}
```

---

## Data file (`oxide/data/NewsBroadcaster_Data.json`)

Stores announcements and each player's last-seen marker. 1.2.0 data loads unchanged.

| Field | Type | Description |
|---|---|---|
| `Id` | string | Stable id |
| `Title`, `ImageUrl`, `Text` | string | Content (`Text` uses `\n` line breaks) |
| `Date` | string | Post date shown in the UI |
| `EditedDate` | string | Last edit (new in 2.0.0, empty if never edited) |
| `Author` | string | Poster |
| `Type` | enum | `Info / Warning / Alert / Event / Update` |
| `Timestamp` | long | UTC ticks — ordering and last-seen |
| `Pinned` | bool | Keeps the post on top |
| `LikedPlayers`, `ReadByPlayers` | Steam IDs | Likes and reads |
| `ReadRewardedPlayers`, `LikeRewardedPlayers` | Steam IDs | Players already paid each reward |

---

## Localization

All player-facing text is in Oxide's lang system (`oxide/lang/<lang>/NewsBroadcaster.json`). New keys are added to your existing lang file automatically. Keys whose meaning changed in 2.0.0 were renamed so the new text shows up (`TypeLabel`, `BodyHint`, `NoPostsYet`, `PublishedNoNotify`, `ActiveBadge`); the old keys can be deleted from your lang file.

---

## Testing a build

The repository includes `tools/test-harness/`, which compiles the plugin against stand-ins for the Oxide/Rust APIs (strict C# 6/7, .NET Framework 4.8 surface), runs a functional test suite and renders the CUI to screenshots. See `tools/test-harness/README.md`. Final checks should still be done on a real server (`oxide.reload NewsBroadcaster`).

---

## Known limitations

- The toast position is `Left` or `Right` only.
- The popup body's scroll height is estimated from a characters-per-line guess. On very narrow (4:3) screens the last line of a very long post may sit close to the bottom edge.
- Scheduling, drafts and per-post audiences are not part of this version.
