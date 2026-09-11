# About News Broadcaster

The ultimate, modern, and highly-customizable announcement system for your Rust server.

Keep your players informed with sleek, intuitive UI popups and real-time Discord webhooks. Whether it's a server wipe, an upcoming event, a rule change, or a massive update, NewsBroadcaster delivers your message with style — **now with full scheduling, so you can write it today and let it post itself on Friday.**

Forget editing clunky JSON files and wrestling with config reloads. NewsBroadcaster features a fully functional in-game Admin Editor UI, allowing you to create, schedule, edit, theme, and broadcast announcements directly from your Rust server!

---

## 🆕 New in 1.7.0

**Scheduled Announcements** — Set a publish time and the post stays hidden until it's due, then broadcasts itself: in-game popup, Discord webhook and API hook, all fired automatically. It survives server restarts and never double-posts. Write your whole wipe-week schedule in one sitting.

**Expiring Announcements** — Give an event post an end date and it drops out of the player archive on its own. No more stale "Double XP Weekend" sitting at the top three weeks later. Admins still see it.

**Drafts** — Save a post without publishing it. Work on the wipe announcement across several sessions, then flip one toggle when you're ready to go live.

**Targeted Announcements** — Restrict any post to a permission group with a single field. Type `vip` into the Audience box and only holders of `newsbroadcaster.vip` ever see it. The permission is registered automatically — no config editing. Perfect for VIP-only news, staff notices, or early event reveals.

**Filterable Player Archive** — Players can filter by type, toggle unread-only, or search titles and bodies with a live search box. One click marks everything read.

**Export / Import** — Move your announcements between servers or back them up with two console commands. Ideal for server networks that share news.

**Quiet Connect Mode** — Prefer not to hijack a player's screen on spawn? Switch to a simple "You have 3 unread announcements" chat line instead.

---

## ✨ Key Features

**Modern UI** — Smooth UI components and a premium, non-intrusive aesthetic.

**In-Game Admin Editor** — Create, edit, schedule and delete announcements seamlessly without ever leaving the game or touching a configuration file.

**Admin Dashboard** — A totals bar (posts / pinned / likes / reads), per-post like and read counts, and lifecycle badges showing at a glance what's LIVE, DRAFT, SCHEDULED or EXPIRED.

**Bulk Admin Operations** — Multi-select rows with checkboxes, then delete, pin or unpin everything in one click.

**Instant Discord Webhooks** — Automatically broadcast your in-game announcements straight to your Discord server with beautifully formatted rich embeds. Supports custom role mentions (`@everyone`, `<@&RoleID>`). Scheduled posts fire their webhook the moment they go live.

**Dynamic Theme Engine** — Switch between six gorgeous pre-made colour schemes (Default, Dark, Ocean, Rust, Midnight, Forest) instantly via the in-game Admin menu, or create your own custom themes in the configuration. The theme picker renders every card in its own colours so you can see exactly what you're choosing.

**Smart Notifications** — Send smaller toast-style notifications (with sound!) or full-screen popups depending on the urgency. Supports native UI or the popular Notify plugin. Notification sounds are sent privately to each player, so a group sharing a base doesn't hear the same chime five times over.

**Player Archive & History** — Players browse past announcements they missed in a clean, paginated archive. Unread posts stand out with an accent wash, an accent frame and an `UNREAD` badge, so nothing important gets lost.

**Engagement Tracking** — Players can "Like" (❤️) announcements, letting you gauge community interest in events or updates!

**Image Support** — Full integration with ImageLibrary to showcase banners or screenshots alongside your text to make your news pop.

**Read & Like Rewards** — Pay players in items, RP (ServerRewards) and/or currency (Economics) for actually reading and liking your news. Once per announcement, per player.

**Pinned Announcements** — Keep server rules, wipe schedules or current events at the top of the archive with a coherent gold treatment (gold tint, gold "PINNED" chip, gold popup frame). Pinned posts are never removed by the storage cap.

---

## Scheduling, Drafts & Targeting

The in-game editor gives you four controls alongside the type selector:

- **DRAFT** — while set, the post is never broadcast and never visible to players. Turn it off and save to publish.
- **PUBLISH AT** — leave empty to post now, or set a time and the post waits until then.
- **EXPIRES AT** — leave empty for a permanent post, or set a time and it leaves the archive automatically.
- **AUDIENCE** — leave empty for everyone, or enter a name like `vip` to restrict the post to `newsbroadcaster.vip`.

Both time fields accept an exact time or a quick relative offset:

```
2026-01-31 18:00      exact date and time (server local)
2026-01-31            midnight on that date
+30m  +2h  +3d  +1w   relative to right now
```

Editing a published post never re-broadcasts it, so you can fix a typo without spamming your players. Move a post back to draft (or forward to a future time) and it will announce again when it next goes live.

---

## Archive Filters

The archive header carries a filter bar: type chips, an `UNREAD` toggle, a search box that matches titles and bodies, and a `CLEAR` button. Filters are per player and survive paging.

A **MARK ALL READ** button appears whenever a player has unread posts. It deliberately does *not* pay read rewards — those still require actually opening the announcement, so there's no way to farm them.

---

## Commands

### Player Commands

- `/news` — Opens the Announcement Archive and History UI.
- `/news read` — Mark every announcement you can see as read.
- `/news unread` — Show only unread announcements.
- `/news <type>` — Filter by type, e.g. `/news alert`.
- `/news <text>` — Search titles and bodies, e.g. `/news wipe`.
- `/news <page>` — Jump straight to a page.

### Admin / Console Commands

Requires the `newsbroadcaster.admin` permission.

- `news.admin` — Opens the In-Game Admin Control Center (Create, Schedule, Edit, Delete, change Themes).
- `news.show "Title" "ImageURL" "Text" [Type]` — Quick-broadcast an announcement from console/RCON. Use `-` for ImageURL if you don't want an image.
  Types: `Info`, `Warning`, `Alert`, `Event`, `Update`
- `news.list` — List every stored announcement with its index, status and type.
- `news.delete <index>` — Delete a specific announcement via console.
- `news.trigger <SteamID/Name> [index]` — Force-open an announcement popup for a specific player. Great for rules screens or welcome messages!
- `news.export [filename]` — Back up every announcement to `oxide/data/`.
- `news.import <filename> [merge|replace]` — Restore announcements from `oxide/data/`. `merge` skips duplicates, `replace` clears first.

---

## Permissions

- `newsbroadcaster.view` — Required to open the `/news` archive. Grant to the default group so all players can browse your news history.
- `newsbroadcaster.admin` — Required to access the Admin Editor UI and all console commands.
- `newsbroadcaster.<audience>` — Created automatically when you give an announcement an Audience. Only holders see that post.

```
oxide.grant group default newsbroadcaster.view
oxide.grant user <SteamID> newsbroadcaster.admin
oxide.grant group vip newsbroadcaster.vip
```

> **Note:** Broadcast popups and notification toasts reach every connected player, exactly as in earlier versions. `newsbroadcaster.view` controls the **archive**. To restrict who receives a specific announcement, give it an **Audience**.

---

## Configuration

NewsBroadcaster is designed to be plug-and-play, but offers deep customization for server owners who want everything perfect.

```json
{
  "General": {
    "AutoCloseSeconds": 15,
    "EnableAutoClose": true,
    "ShowNewsOnConnect": true,
    "ServerName": "SERVER NEWS",
    "AnnouncementsPerPage": 5,
    "MaxStoredAnnouncements": 50,
    "UnreadSummaryOnConnect": false
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
    "WebhookUrl": "YOUR_DISCORD_WEBHOOK_URL_HERE",
    "BotName": "Server News",
    "RoleMention": "@everyone"
  },
  "SelectedTheme": "Default",
  "Themes": {
    "Default":  { "..." : "..." },
    "Dark":     { "..." : "..." },
    "Ocean":    { "..." : "..." },
    "Rust":     { "..." : "..." },
    "Midnight": { "..." : "..." },
    "Forest":   { "..." : "..." }
  }
}
```

Set `UnreadSummaryOnConnect` to `true` to swap the connect popup for a quiet chat summary.

Out-of-range settings are corrected automatically on load with a warning in the console, so a stray `0` can never break your archive layout or empty your data file.

---

## Rewards

Two independent reward bundles — one for reading an announcement (popup must stay open for `ReadDelaySeconds`) and one for liking it. Each bundle can grant any combination of:

- **Items** — any Rust shortname (`scrap`, `wood`, `metal.refined`, …) with optional skin id
- **Points** — RP via ServerRewards
- **Currency** — via Economics

Rewards fire at most once per player per announcement. Inventory full? Items drop at the player's feet. ServerRewards / Economics not loaded? Items still apply, points/currency are silently skipped.

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

Toggle the pin from the admin list — every row has a PIN / UNPIN button. Pinned posts get:

- A gold tint and PINNED chip in the archive list
- A matching gold pill in the admin list
- A thin gold frame and PINNED chip on the popup itself

Pinned posts, drafts and scheduled posts are protected from the `MaxStoredAnnouncements` cap, so your rules post is never quietly deleted to make room.

---

## API Hooks

Other plugins can subscribe to:

- `OnNewsBroadcast` — `void OnNewsBroadcast(Dictionary<string, object> ann)`
- `OnNewsEdited` — `void OnNewsEdited(Dictionary<string, object> ann)`
- `OnNewsDeleted` — `void OnNewsDeleted(Dictionary<string, object> ann)`
- `OnNewsRead` — `void OnNewsRead(BasePlayer player, Dictionary<string, object> ann)`
- `OnNewsLiked` — `void OnNewsLiked(BasePlayer player, Dictionary<string, object> ann, bool added)`

The announcement payload contains: `id`, `title`, `author`, `type`, `timestamp`, `date`, `text`, `imageUrl`, `likes`, `pinned`, `draft`, `status`, `publishAt`, `expiresAt`, `audience`.

`status` is one of `Live`, `Draft`, `Scheduled` or `Expired`. `OnNewsBroadcast` fires when a post actually goes live — including when the scheduler publishes it — and never fires for a draft or twice for the same post.

Every payload key from earlier versions is unchanged, so existing integrations keep working.

```csharp
void OnNewsBroadcast(Dictionary<string, object> ann)
{
    Puts($"[News] {ann["author"]} posted '{ann["title"]}' ({ann["type"]})");
}
```

---

## Also in 1.7.0

A round of stability work alongside the new features:

- Fixed the archive, individual announcements and the like button being reachable from the F1 console without the `newsbroadcaster.view` permission.
- Fixed broken UI layouts on servers running a non-English system locale (comma decimal separators).
- Fixed `AnnouncementsPerPage: 0` breaking the archive, and `MaxStoredAnnouncements: 0` emptying the data file.
- Fixed pinned posts being the first thing deleted when hitting the storage cap.
- Fixed ordinary text containing `/n` (like `w/newbies` or `24/7`) having line breaks injected into it.
- Fixed notification sounds being audible to nearby players instead of just the recipient.
- Fixed announcement images never loading when ImageLibrary loads after NewsBroadcaster.
- Fixed long titles causing the entire Discord webhook to fail.
- A broken config file is now backed up rather than silently overwritten with defaults.
- Per-player tracking writes are batched, cutting data-file churn on busy servers.

---

## Optional Dependencies

- **ImageLibrary** — image caching for announcement banners
- **Notify** — third-party notification popups
- **ServerRewards** — RP rewards
- **Economics** — currency rewards

All are optional; the plugin degrades gracefully when they're absent.
