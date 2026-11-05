# NewsBroadcaster — Plugin Documentation

**Plugin:** NewsBroadcaster  
**Version:** 2.3.0  
**Author:** DEDA  
**Framework:** Oxide / uMod (Rust)

---

## Overview

NewsBroadcaster is a Rust server plugin that lets admins post rich in-game news announcements with a modern CUI (Client UI). Announcements have a title, an optional image, body text and a coloured type badge. Players get a notification toast when news is posted and can browse the archive at any time with `/news`.

Optional integrations: **ImageLibrary** (image caching), **Notify** (third-party notification style), **ServerRewards** / **Economics** (read & like rewards) and **Discord webhooks**.

---

## What's new in 2.3.0

Animations. They all run on the client (Rust's built-in fade-in / fade-out), so they are smooth and add no extra network traffic. A short preview is in `previews/newsbroadcaster-animations.mp4` (and `.gif`).

- **Staggered reveal** — screens build up softly: panel first, then header, title, text and buttons; archive and admin rows cascade in one after another (about 0.6 s in total).
- **No more blink between screens** — the dimmed background only fades in when nothing was open before. Moving archive → post → archive, or opening the editor, changes the panel without flashing the game behind it.
- **Instant redraws** — clicking toggles, chips, checkboxes or pins in the admin screens redraws instantly instead of re-playing the animation.
- **Toast** fades in and fades out (a new toast cross-fades with the previous one).
- **Live countdown** — broadcast/connect popups that close automatically show "CLOSES IN 12S", counted down by the client. Liking or voting keeps the popup open and removes the countdown.
- **Likes pop** and **poll result bars fade in** after voting.

New lang key: `AutoCloseIn` (keep the `%TIME_LEFT%` token).

## What's new in 2.2.0

A modern restyle that keeps the same layout, colours and themes:

- **Flat outlined surfaces** — thin outline borders and hairline dividers replace the heavy header/footer bars; secondary actions are subtle outlined ("ghost") buttons and only the main action is a filled button.
- **Popup reads like an article** — type pill and server name in the header, then the title, a "posted by · date" line right under it, and the text aligned with the title. The image sits in a framed card (the duplicate type badge on the image is gone). Like button bottom-left, main action bottom-right.
- **Archive rows are clickable cards** — click anywhere on a row to open it; a `›` chevron replaces the per-row READ button. Unread rows get an accent outline, pinned rows a gold one.
- Admin list, editor, theme picker, confirmation dialogs and the toast use the same outlined style (outlined fields, chips, toggles and checkboxes).
- Popup body sizing was re-tuned so short posts don't show an unnecessary scrollbar.

No config, data or command changes.

## What's new in 2.1.0

- **Polls** — any announcement can carry a 2–4 option poll. Players vote with one click and see live result bars after voting; admins always see results. Votes can be changed (configurable), admins can close voting, and polls show up in the archive, the admin list and Discord. Create one in the editor (**ADD POLL**) or with `news.poll`.
- **Mark all read** — a **MARK ALL READ** button in the archive header (shown while something is unread) and `/news read`.
- **Visual polish** — blurred background behind every screen, soft shadows around panels, a coloured type pill (plus a **POLL** chip) in the popup header, a thin type-coloured line along the popup's top edge, a stats line in the admin header (posts / pinned / reads / likes), and evenly spaced option toggles in the editor. The layout and colours of 2.0.0 are unchanged.
- New hook `OnNewsPollVote`; hook payloads gained `pollOptions` and `pollVotes` (existing keys unchanged).

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
| `/news read` | viewers | Mark every announcement as read |
| `/news help` | everyone | List the commands |
| `/news admin` | admins | Open news management |

---

## Console / RCON Commands

| Command | Permission | Description |
|---|---|---|
| `news.show "Title" "ImageURL" "Body" [Type]` | Admin | Post a new announcement (notifies players, posts to Discord) |
| `news.poll "Title" "Text" "Option 1\|Option 2\|..." [Type]` | Admin | Post an announcement with a poll (2–4 options separated by `\|`) |
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
| **ADD POLL** | Shows four poll option fields under the body. Fill 2–4 of them; empty ones are skipped. When editing a poll, **CLOSE VOTING** stops new votes and shows the results to everyone. Turning **ADD POLL** off removes the poll and its votes |
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
| `news.vote <id> <option>` | Vote in a poll (option index from 0) |
| `news.markread` | Mark all read (archive button) |
| `news.close` / `news.close.notif` | Close the UI / the toast |
| `news.admin.page <page>` | Admin list page |
| `news.admin.create` / `news.admin.edit <id>` | Open the editor |
| `news.admin.togglepin <id> [page]` | Pin / unpin |
| `news.admin.resend <id> [page]` | Re-notify online players |
| `news.admin.delconfirm <id> [page]` / `news.admin.del <id> [page]` | Delete with confirmation |
| `news.admin.toggleselect <id> [page]`, `news.admin.selectpage <page>`, `news.admin.clearsel [page]` | Bulk selection |
| `news.admin.bulkpin <0\|1> [page]`, `news.admin.bulkdelconfirm [page]`, `news.admin.bulkdel [page]` | Bulk actions |
| `news.admin.themes [page]` / `news.admin.settheme "Name" [page]` | Theme picker |
| `news.editor.input <title\|image\|text\|poll1-poll4> <value>` | Editor field |
| `news.editor.settype <Type>` / `news.editor.type` | Set / cycle the type |
| `news.editor.toggle <pin\|notify\|discord\|poll\|pollclosed>` | Editor options |
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
  "Polls": {
    "AllowVoteChange": true,
    "ShowResultsBeforeVoting": false
  },
  "SelectedTheme": "Default",
  "Themes": { "Default": { }, "Dark": { }, "Ocean": { }, "Rust": { }, "Midnight": { }, "Forest": { } }
}
```

### General

| Key | Default | Description |
|---|---|---|
| `AutoCloseSeconds` | `15` | Seconds before a broadcast / connect popup closes by itself (shown as a live countdown) |
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

### Polls

| Key | Default | Description |
|---|---|---|
| `AllowVoteChange` | `true` | Players can switch their vote by clicking another option |
| `ShowResultsBeforeVoting` | `false` | Show result bars to players who haven't voted yet (admins always see them) |

### Theme colours

Each theme has these `"R G B A"` (0–1) fields: `PanelBg`, `HeaderBg`, `ContentBg`, `ButtonPrimary`, `ButtonSecondary`, `TextTitle`, `TextNormal`, `TextMuted`. Add your own themes to the `Themes` list; the picker pages through them 8 at a time.

---

## Pinned announcements

Pinned posts always sit at the top of the archive and admin list, with a gold tint, a gold **PINNED** badge and a gold frame around the popup. Pin from the admin row (**PIN / UNPIN**), from the editor (**PIN TO TOP**) or in bulk.

## Bulk admin operations

Tick the checkbox on admin rows (or **SELECT PAGE**) and a toolbar appears: **DELETE** (with confirmation), **PIN ALL**, **UNPIN ALL**, **CLEAR**. The selection is per admin, survives paging, and is dropped on disconnect.

## Animations

Rust CUI can fade an element's graphic in when it appears (`FadeIn`) and out when it is destroyed (`FadeOut`); the client runs both. NewsBroadcaster uses them like this:

| Where | Animation |
|---|---|
| Opening any screen from nothing | Background fades in (0.25 s), then the content reveals top-to-bottom (0.15 s → 0.6 s) |
| Switching screens (archive ↔ post, admin → editor) | Background stays, new content reveals |
| Prev/next post, archive paging | Shorter reveal |
| Admin list / editor / theme picker redraws | Instant |
| Toast | Fades in, fades out after `Notification.Duration` or when clicked |
| Like / vote | Like button pops, result bars fade in; rest of the popup stays still |
| Auto-closing popups | Live "CLOSES IN %TIME_LEFT%S" countdown |

## Read tracking

A post counts as **read** for a player after its popup has stayed open for `ReadDelaySeconds`. That drives the archive's `NEW` badges, the unread counter, the admin list's read counts, the read reward and the `OnNewsRead` hook.

**Mark all read** (archive button or `/news read`) clears a player's `NEW` badges for every current post. It is stored as one timestamp per player, so it does not inflate the per-post read counts, pay read rewards or fire `OnNewsRead`. Posts published afterwards show as unread again.

## Polls

A poll is part of an announcement: the options appear in a block under the text in the popup. Before voting, players see the options as buttons; after voting they see each option's share and count, with their own choice ticked. Voting only redraws the poll block, so the reader keeps their scroll position. Each player has one vote per poll (index stored in `PollVotes`).

Editing a poll keeps existing votes: options keep their votes when you clear an empty slot in between, and votes for an option you delete are dropped. Discord posts list the poll options in an embed field.

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
| `OnNewsPollVote` | `void OnNewsPollVote(BasePlayer player, Dictionary<string, object> ann, int option)` | A player votes (or changes their vote) in a poll; `option` is the 0-based index |

Payload keys: `id` (string), `title`, `author`, `type` (`Info`/`Warning`/`Alert`/`Event`/`Update`), `timestamp` (UTC ticks), `date` (`yyyy-MM-dd HH:mm`), `text`, `imageUrl`, `likes` (int), `pinned` (bool) — unchanged from 1.2.0 — plus `pollOptions` (string[], empty when there is no poll) and `pollVotes` (int, total votes) since 2.1.0.

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

Stores announcements, each player's last-seen marker and each player's "mark all read" marker (`ReadAllUpTo`). 1.2.0 and 2.0.0 data load unchanged.

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
| `PollOptions` | list of strings | Poll options (empty = no poll) |
| `PollVotes` | Steam ID → option index | One vote per player |
| `PollClosed` | bool | Voting closed |

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
- Polls are single-choice and have no end time; close them from the editor.
