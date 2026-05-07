# NewsBroadcaster

**A clean, modern in-game news system for Rust — with rewards, pinned posts, Discord, themes, and a full admin UI.**

NewsBroadcaster lets you push beautiful announcements straight to your players: title, image, body text and a coloured type badge, all wrapped in a polished CUI. Players see a pop-up the moment something is broadcast and can browse the full archive at any time with `/news`.

---

## Highlights

- **Full in-game admin panel** — create, edit, delete, pin and theme announcements without ever leaving the game.
- **Pinned announcements** — keep server rules, wipe schedules or current events at the top of the archive with a coherent gold treatment (gold tint, gold "PINNED" chip, gold popup frame).
- **Bulk admin operations** — multi-select rows with checkboxes, then delete / pin / unpin everything in one click.
- **Read & Like rewards** — pay players in items, RP (ServerRewards) and/or currency (Economics) for actually reading and liking your news. Once-per-announcement-per-player.
- **Discord webhook integration** — every broadcast can mirror to Discord with embed colours per announcement type and an optional role mention.
- **4 built-in themes** — Default, Dark, Ocean, Rust — switchable in-game with a one-click theme picker.
- **Show on connect** — the latest unseen announcement greets returning players automatically.
- **API hooks** for plugin developers (see below) so other plugins can react to broadcasts, edits, reads and likes.
- **Smart pagination, scrollable long-form bodies, image caching via ImageLibrary, sound effects, auto-close timer, optional Notify integration.**

---

## Permissions

| Permission | What it does |
|---|---|
| `newsbroadcaster.view` | Open `/news` archive |
| `newsbroadcaster.admin` | Full admin access — create / edit / delete / pin / themes |

```
oxide.grant group default newsbroadcaster.view
oxide.grant user <SteamID> newsbroadcaster.admin
```

---

## Commands

### Chat
| Command | Description |
|---|---|
| `/news` | Open the news archive UI |

### Console / RCON
| Command | Description |
|---|---|
| `news.show "Title" "ImageURL" "Body" [Type]` | Post a new announcement |
| `news.trigger <SteamID/Name> [index]` | Force-show a popup to a specific player |
| `news.delete <index>` | Delete an announcement by index (0 = newest) |
| `news.list` | List every stored announcement with its index, id and pin state |
| `news.admin` | Open the in-game admin panel |

**Example:**
```
news.show "Server Update" "https://example.com/img.png" "The server has been updated!" Update
news.show "Alert!" "-" "PvP zone closing in 10 minutes." Alert
```

Use `"-"` as the image URL to post without an image. Supported types: `Info`, `Warning`, `Alert`, `Event`, `Update` (case-insensitive, defaults to `Info`).

---

## Announcement Types

| Type | Colour |
|---|---|
| Info | Blue |
| Warning | Orange |
| Alert | Red |
| Event | Purple |
| Update | Cyan |

---

## Rewards

Two independent reward bundles — one for **reading** an announcement (popup must stay open for `ReadDelaySeconds`) and one for **liking** it. Each bundle can grant any combination of:

- **Items** — any Rust shortname (`scrap`, `wood`, `metal.refined`, …) with optional skin id
- **Points** — RP via [ServerRewards](https://umod.org/plugins/server-rewards)
- **Currency** — via [Economics](https://umod.org/plugins/economics)

Rewards fire **at most once per player per announcement**. Inventory full? Items drop at the player's feet. ServerRewards / Economics not loaded? Items still apply, points/currency are silently skipped.

```json
"ReadRewards": {
  "Items":    [ { "Shortname": "scrap", "Amount": 5 } ],
  "Points":   25,
  "Currency": 100.0
}
```

Old configs using bare item arrays are migrated automatically.

---

## Pinned Announcements

Toggle the pin from the admin list — every row has a **PIN / UNPIN** button. Pinned posts get:

- A gold tint and **PINNED** chip in the archive list
- A matching gold pill in the admin list
- A thin gold frame and **PINNED** chip on the popup itself

Sort order: pinned (newest first) → unpinned (newest first). Insertion order in the data file is unchanged, so "show on connect" still surfaces the most recent broadcast.

---

## Bulk Admin Operations

Click the checkbox on any admin row to start a selection set. A toolbar appears with:

| Button | Action |
|---|---|
| **DELETE** | Confirm, then delete every selected announcement |
| **PIN ALL** | Pin every selected announcement |
| **UNPIN ALL** | Unpin every selected announcement |
| **SELECT PAGE** | Toggle-select every row on the current page |
| **CLEAR** | Drop the selection without changing data |

Selection is per-admin and persists across pages until you clear it, disconnect, or unload the plugin. Stale ids (deleted by another admin) are pruned automatically.

---

## API Hooks

Other plugins can subscribe to:

| Hook | Signature |
|---|---|
| `OnNewsBroadcast` | `void OnNewsBroadcast(Dictionary<string, object> ann)` |
| `OnNewsEdited` | `void OnNewsEdited(Dictionary<string, object> ann)` |
| `OnNewsDeleted` | `void OnNewsDeleted(Dictionary<string, object> ann)` |
| `OnNewsRead` | `void OnNewsRead(BasePlayer player, Dictionary<string, object> ann)` |
| `OnNewsLiked` | `void OnNewsLiked(BasePlayer player, Dictionary<string, object> ann, bool added)` |

The announcement payload contains:
`id`, `title`, `author`, `type`, `timestamp`, `date`, `text`, `imageUrl`, `likes`, `pinned`.

```csharp
void OnNewsBroadcast(Dictionary<string, object> ann)
{
    Puts($"[News] {ann["author"]} posted '{ann["title"]}' ({ann["type"]})");
}
```

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
  "Themes": { "Default": { ... }, "Dark": { ... }, "Ocean": { ... }, "Rust": { ... } }
}
```

Webhook safety: payloads use `allowed_mentions: { parse: ["roles"] }`, so titles and body text **cannot** trigger `@everyone` / `@here` even if literally typed.

---

## Optional Dependencies

| Plugin | Purpose |
|---|---|
| [ImageLibrary](https://umod.org/plugins/image-library) | Cache remote images for faster display |
| [Notify](https://umod.org/plugins/notify) | Use a third-party notification popup style |
| [ServerRewards](https://umod.org/plugins/server-rewards) | Grant RP on read / like |
| [Economics](https://umod.org/plugins/economics) | Grant currency on read / like |

All four are optional — the plugin runs perfectly without any of them.

---

## Localization

Every UI string is registered in Oxide's lang system and can be overridden per language in `oxide/lang/<lang>/NewsBroadcaster.json`.

---

## Support

Open an issue, drop a review, or DM me on Discord — happy to fix bugs and consider feature requests.
