# NewsBroadcaster

**A clean, modern in-game news system for Rust — popups, polls, archive with unread tracking, likes & rewards, pinned posts, Discord and themes, all managed from an in-game admin UI.**

NewsBroadcaster pushes clean, modern announcements straight to your players: title, image, body text and a coloured type badge, wrapped in a polished CUI. Players get a toast the moment something is posted and can browse the full archive any time with `/news`.

---

## Highlights

- **Modern, flat UI** — outlined cards, hairline dividers, blurred background and article-style popups that still match your theme colours.
- **Smooth animations** — screens reveal top-to-bottom, archive rows cascade in, toasts fade in and out, likes pop, poll bars fade in, and auto-closing popups show a live countdown. All client-side: no lag, no extra network traffic.
- **In-game admin panel** — create, edit, preview, pin, resend and delete announcements without leaving the game.
- **Live preview** — see the exact popup your players will get before you hit publish.
- **Polls** — attach a 2–4 option poll to any post. One-click voting, live result bars, change-your-vote, close voting when you're done. Results show in the archive, the admin list and on Discord.
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
- **API hooks** for other plugins: broadcast, edit, delete, read, like.
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
| `/news admin` | Open news management (admins) |
| `/news help` | List commands |

### Console / RCON
| Command | Description |
|---|---|
| `news.show "Title" "ImageURL" "Body" [Type]` | Post a new announcement |
| `news.poll "Title" "Text" "A\|B\|C" [Type]` | Post an announcement with a poll |
| `news.list` | List posts with index, id, reads and likes |
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
| `Notification.Position` | `"Right"` | Toast on the left or right |
| `Discord.RoleMention` | `""` | Role / `@here` / `@everyone` ping for Discord posts |
| `Polls.AllowVoteChange` | `true` | Players can change their vote |
| `Polls.ShowResultsBeforeVoting` | `false` | Show results before a player votes |

Updating from an older version keeps your config, themes and announcements — new options are added automatically.

---

## Optional dependencies

| Plugin | Purpose |
|---|---|
| [ImageLibrary](https://umod.org/plugins/image-library) | Cache remote images |
| [Notify](https://umod.org/plugins/notify) | Third-party notification style |
| [ServerRewards](https://umod.org/plugins/server-rewards) | RP rewards |
| [Economics](https://umod.org/plugins/economics) | Currency rewards |

All optional — the plugin works fully without them.

---

## API hooks

`OnNewsBroadcast(Dictionary<string, object> ann)`, `OnNewsEdited(...)`, `OnNewsDeleted(...)`, `OnNewsRead(BasePlayer, ...)`, `OnNewsLiked(BasePlayer, ..., bool added)`, `OnNewsPollVote(BasePlayer, ..., int option)`.
Payload: `id`, `title`, `author`, `type`, `timestamp`, `date`, `text`, `imageUrl`, `likes`, `pinned`, `pollOptions`, `pollVotes`.

---

## Localization

Every UI string can be translated or reworded in `oxide/lang/<lang>/NewsBroadcaster.json`.

---

## Support

Open an issue, leave a review, or DM me on Discord — happy to fix bugs and consider feature requests.
