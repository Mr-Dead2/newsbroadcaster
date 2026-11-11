# NewsBroadcaster 1.0.0

**A clean, modern in-game news system for Rust — popups, polls, event countdowns, drafts, VIP-only posts, archive with filters, search and unread tracking, likes & rewards, Discord and themes, all managed from an in-game admin UI.**

NewsBroadcaster pushes clean, modern announcements straight to your players: title, image, body text and a coloured type badge, wrapped in a polished CUI. Players get a toast the moment something is posted and can browse the full archive any time with `/news`.

---

## Highlights

- **Premium, modern UI** — rounded anti-aliased corners on panels, cards and pictures, soft shadows, a blurred background and article-style popups, all in your theme colours.
- **Smooth animations** — screens reveal top-to-bottom, archive rows cascade in, toasts fade in and out, likes pop, poll bars fade in, and auto-closing popups show a live countdown. All client-side: no lag, no extra network traffic.
- **In-game admin panel** — a dashboard with stat cards, tabs, search and sorting; create, edit, preview, pin, resend and delete announcements without leaving the game.
- **Editor autosave** — a disconnect or restart never loses a half-written post.
- **Live preview** — see the exact popup your players will get before you hit publish.
- **Polls** — attach a 2–4 option poll to any post. One-click voting, live result bars, change-your-vote. Close voting yourself or set a timer (1 hour – 7 days); the result is announced in chat and on Discord.
- **Event countdowns** — a live "WIPE IN 2 DAYS 04:12:33" timer right in the post, counted by the player's game.
- **Drafts & copy** — save posts as drafts and publish them later; copy last week's wipe post in one click.
- **Posts for one group** — VIP news, staff notes: only that Oxide group (and admins) can see them.
- **Auto-delete** — event posts remove themselves 1–30 days after publishing.
- **Archive filters & search** — chips per type, a Polls filter and a search box.
- **Player settings** — `/news settings`: players can mute the sound, swap pop-ups for a chat line, or skip the join popup.
- **Image link check** — the editor warns about links that won't show in game (imgur pages, expiring Discord links, GIFs).
- **One-click type chips** — Info, Warning, Alert, Event, Update, each with its own colour.
- **Unread tracking** — `NEW` badges, an unread counter and a **Mark all read** button in the archive, plus a one-line chat reminder for posts a player missed while offline.
- **Archive with thumbnails** — image previews, type labels and like counts on every row.
- **Prev / next navigation** inside the popup, and "back to archive" returns players to the page they were on.
- **Pinned posts** — keep rules, wipe schedules or events on top with a gold badge and frame. Pinned posts are never trimmed.
- **Read & like rewards** — items, RP (ServerRewards) and/or currency (Economics), paid once per player per post.
- **Resend** — re-notify everyone online about an important post with one click.
- **Discord webhook** — type-coloured embeds with image, timestamp and an optional role / `@here` / `@everyone` mention.
- **6 built-in themes** with colour-swatch previews, plus as many custom themes as you like.
- **Bulk tools** — multi-select to delete, pin or unpin many posts at once.
- **Private notification sound** — only the receiving player hears it.
- **API** for other plugins: hooks for broadcast, edit, delete, read, like and polls, and methods to post news from your own plugins.
- Long posts scroll natively, long titles shrink to fit, images can be cached with ImageLibrary, and Notify is supported.

---

## Permissions

| Permission | What it does |
|---|---|
| `newsbroadcaster.view` | See news (archive, popups, toasts). Can be turned off with `RequireViewPermission: false` |
| `newsbroadcaster.admin` | Full admin access |

```
oxide.grant group default newsbroadcaster.view
oxide.grant user <SteamID> newsbroadcaster.admin
```

---

## Commands

### Chat
| Command | Description |
|---|---|
| `/news` | Open the news archive |
| `/news latest` | Open the newest announcement |
| `/news read` | Mark everything as read |
| `/news settings` | Sound, pop-up and join options |
| `/news admin` | Open news management (admins) |
| `/news help` | List commands |

### Console / RCON
| Command | Description |
|---|---|
| `news.show "Title" "ImageURL" "Body" [Type]` | Post a new announcement |
| `news.poll "Title" "Text" "A\|B\|C" [Type]` | Post an announcement with a poll |
| `news.list` | List posts with index, id, reads and likes |
| `news.publish <index\|id>` | Publish a draft |
| `news.delete <index\|id>` | Delete a post |
| `news.resend <index\|id>` | Re-notify online players |
| `news.trigger <SteamID/Name> [index]` | Show a popup to one player |
| `news.admin` | Open the in-game admin panel |

```
news.show "Server Update" "https://example.com/img.png" "The server has been updated!" Update
news.show "Alert!" "-" "PvP zone closing in 10 minutes." Alert
```

Use `"-"` for no image. Types: `Info`, `Warning`, `Alert`, `Event`, `Update`. Use `\n` for a new line.

---

## Rewards

Two independent bundles — one for **reading** a post (popup open for `ReadDelaySeconds`) and one for **liking** it. Each can grant any mix of:

- **Items** — any shortname with optional skin id
- **Points** — RP via ServerRewards
- **Currency** — via Economics

```json
"ReadRewards": {
  "Items":    [ { "Shortname": "scrap", "Amount": 5 } ],
  "Points":   25,
  "Currency": 100.0
}
```

---

## Configuration highlights

| Option | Default | What it does |
|---|---|---|
| `ShowNewsOnConnect` | `true` | Show the newest unseen post when a player wakes up |
| `UnreadReminderOnConnect` | `true` | One chat line about posts missed since the last visit |
| `RequireViewPermission` | `true` | `false` lets everyone see news |
| `PopupImageFrame` | `"Tall"` | `"Wide"` = 16:9 frame so widescreen images aren't stretched |
| `ArchiveThumbnails` | `true` | Image thumbnails in the archive |
| `StripLinksFromBody` | `true` | Remove links from post bodies |
| `Notification.Position` | `"TopRight"` | Six toast positions, plus `OffsetX` / `OffsetY` |
| `Discord.RoleMention` | `""` | Role / `@here` / `@everyone` ping for Discord posts |
| `Polls.AllowVoteChange` | `true` | Players can change their vote |
| `Polls.ShowResultsBeforeVoting` | `false` | Show results before a player votes |
| `Polls.AnnounceResults` | `true` | Announce the result when a poll closes |
| `Discord.PostGroupOnlyPosts` | `false` | Also send group-only posts to Discord |

Updating from an older version keeps your config, themes and announcements — new options are added automatically.

---

## Requirements

| Plugin | When you need it |
|---|---|
| [Oxide (uMod)](https://umod.org/games/rust) | Always. NewsBroadcaster is an Oxide plugin |
| [ServerRewards](https://umod.org/plugins/server-rewards) | For RP rewards (`Points` in the read or like reward) |
| [Economics](https://umod.org/plugins/economics) | For currency rewards (`Currency` in the read or like reward) |
| [Notify](https://umod.org/plugins/notify) | Only if you turn on `UseNotifyPlugin`; otherwise the plugin's own notification is used |
| [ImageLibrary](https://umod.org/plugins/image-library) | Not required. If it's installed, post pictures are cached through it so they load faster |

A reward that includes RP or currency isn't paid while its plugin is missing, including any items in the same reward; the server console says why. It isn't marked as paid, so it can still be paid after you install the plugin. Everything else, including item rewards, rounded corners, polls, countdowns and Discord posts, needs only Oxide.

---

## API hooks

`OnNewsBroadcast(Dictionary<string, object> ann)`, `OnNewsEdited(...)`, `OnNewsDeleted(...)`, `OnNewsRead(BasePlayer, ...)`, `OnNewsLiked(BasePlayer, ..., bool added)`, `OnNewsPollVote(BasePlayer, ..., int option)`, `OnNewsPollClosed(...)`.
Methods: `API_PostNews(title, text, type, imageUrl, author)`, `API_CreateNews(options)`, `API_DeleteNews(id)`, `API_GetNews(id)`, `API_GetNewsList()`, `API_GetUnreadCount(player)`, `API_ShowNews(player, id)`. Details in `PLUGIN_DOCS.md`.

Payload: `id`, `title`, `author`, `type`, `timestamp`, `date`, `text`, `imageUrl`, `likes`, `pinned`, `pollOptions`, `pollVotes`, `group`, `expiresAt`, `pollEndsAt`, `countdownTo`.

---

## Localization

Every UI string can be translated or reworded in `oxide/lang/<lang>/NewsBroadcaster.json`.

---

## Support

Open an issue, leave a review, or DM me on Discord — happy to fix bugs and consider feature requests.

---

## Release notes — 1.0.0

See `CHANGELOG.md` for the full list. Highlights:

- New premium look: rounded corners, soft shadows, clean flat design, smooth animations
- Polls with live results and timers, unread tracking and Mark all read
- Event countdowns, drafts & copy, VIP-only posts, auto-delete, archive filters & search, player settings
- Better editor (type chips, pin / notify / Discord / poll toggles, live preview) and RESEND
- Popup navigation, live auto-close countdown, archive thumbnails
- Many fixes (cut-off titles, theme overflow, private sound, ServerRewards on current Rust, comma-locale servers)
- Built for the November 2026 Rust update
- Light on the server: small screens (an archive page is about 160 UI elements), batched saves, no idle timers
- First release: files from earlier builds are not converted, so start with a fresh config and data file
