# NewsBroadcaster on RogueDepot

Everything for the product page, ready to paste. The fields are named the way a listing shows them (name, version, platforms, tags, price, description); if the upload form calls one of them differently, the text still fits.

---

## Basics

| Field | Value |
|---|---|
| **Name** | NewsBroadcaster |
| **Version** | 1.0.0 |
| **Author** | DEDA |
| **Category** | Plugin |
| **Platform** | Oxide (uMod). See the note on Carbon below |
| **Game version** | Current Rust (built for the November 2026 update) |
| **File to upload** | `NewsBroadcaster.cs` (one file, no dependencies). Attach `PLUGIN_DOCS.md` too if the form takes a documentation file |
| **Required plugins** | None |
| **Optional plugins** | ServerRewards, Economics, Notify, ImageLibrary |
| **Price** | Your call |

**Carbon:** the plugin only uses the standard Oxide plugin API (CUI, lang, permissions, data files, web requests, timers), which Carbon supports, so it should load there. It has not been tested on Carbon. Tick Carbon only after you have loaded it on a Carbon server once.

---

## Short description

Pick one (the first is the default):

1. In-game news for your Rust server: popups, polls, countdowns, an archive with unread tracking, Discord posts and a full admin panel.
2. Post news in game and everyone online sees it. Polls, event countdowns, VIP-only posts, rewards, Discord and an in-game editor.
3. A clean in-game news system: toast and popup announcements, polls, wipe countdowns, archive, rewards, Discord webhook, themes.

---

## Tags

`news` `announcements` `ui` `cui` `notifications` `popup` `polls` `voting` `countdown` `wipe` `discord` `webhook` `rewards` `admin` `vip` `themes` `api`

If only a few are allowed: `news`, `announcements`, `ui`, `polls`, `discord`.

---

## Images

Upload in this order. All files are in the repository.

| # | File | What it shows |
|---|---|---|
| Icon | `branding/logo-512.png` | The logo (use `logo-1024.png` if a larger one is asked for) |
| Cover | `branding/banner.png` | 1600×800 banner with the logo and name |
| 1 | `previews/02_popup_image.png` | The popup with a picture |
| 2 | `previews/01_notification.png` | The toast for a new post |
| 3 | `previews/10a_popup_poll_vote.png` | A poll with a timer |
| 4 | `previews/10b_popup_poll_results.png` | Poll results |
| 5 | `previews/13_popup_countdown.png` | The live countdown |
| 6 | `previews/04_archive.png` | The archive with unread badges |
| 7 | `previews/11_archive_filtered.png` | Archive filters and search |
| 8 | `previews/05_admin.png` | News Management |
| 9 | `previews/05c_admin_filtered.png` | Tabs, search and sort |
| 10 | `previews/07b_editor_new.png` | The editor |
| 11 | `previews/07c_editor_poll.png` | The editor with a poll |
| 12 | `previews/09_editor_preview.png` | Preview before publishing |
| 13 | `previews/05b_admin_unsaved.png` | Editor autosave |
| 14 | `previews/08_themes.png` | Theme picker |
| 15 | `previews/12_settings.png` | Player settings |
| Video | `previews/newsbroadcaster-animations.mp4` (or the `.gif`) | The animations |

The screenshots are renders from the test tool, not captures from a live server. They show the real layout and colours; if the store wants in-game captures, take them on your server.

---

## Description

Paste from here to the next line. It is Markdown; if the editor does not take Markdown tables, use the plain lists (the tables are only in Permissions, Commands and Config).

---

**NewsBroadcaster puts your server news in front of your players, in game.** Post an announcement and everyone online gets a toast. They read it in a clean popup, vote in polls, watch the wipe countdown and catch up on what they missed with `/news`. You run all of it from an in-game admin panel.

One file. Needs only Oxide. No web panel, no database.

### For your players

- **Toast and popup.** A toast the moment something is posted; click it for the full post with picture, title, author, date and text. Long posts scroll, long titles shrink to fit.
- **Polls.** Any post can have a 2–4 option poll. One click to vote, live result bars, and players can change their vote. Polls can close on a timer and the winner is announced in chat.
- **Event countdowns.** A live "WIPE IN 2 DAYS 04:12:33" timer inside the post.
- **Archive.** `/news` opens every post with thumbnails, type filters, a Polls filter and a search box.
- **Unread tracking.** `NEW` badges, an unread counter, **Mark all read**, and one chat reminder about posts missed while offline.
- **Likes.** A like button on every post.
- **Their own settings.** `/news settings`: turn off the sound, swap pop-ups for a chat line, or skip the post shown when joining.

### For you

- **News Management.** A dashboard with stat cards (published, drafts, reads, likes, poll votes), tabs, search and sorting. Every post shows its reads, likes and votes.
- **Editor with preview.** Title, picture, text, type, poll, countdown. See the exact popup before you publish.
- **Autosave.** A disconnect, a restart or a plugin reload never loses a half-written post.
- **Drafts and copy.** Save a post for later; copy last week's wipe post in one click.
- **Posts for one group.** VIP news or staff notes: only that Oxide group (and admins) can see them.
- **Auto-delete.** Event posts remove themselves 1–30 days after publishing.
- **Pin, resend, bulk tools.** Keep rules on top, re-notify everyone online, or pin / delete many posts at once.
- **Rewards.** Pay players for reading or liking a post: items, RP (ServerRewards) or currency (Economics), once per player per post.
- **Discord.** A webhook posts every announcement as an embed in the type's colour, with picture and an optional role ping. Poll results are posted too.
- **Console and RCON.** `news.show`, `news.poll`, `news.list`, `news.publish`, `news.delete`, `news.resend`, `news.trigger`: post from a scheduler or another tool.
- **Image link check.** The editor warns about links that will not show in game (imgur pages, Discord links that expire, GIFs).
- **Six themes**, and as many of your own as you like.
- **Every text can be translated** in the lang file.

### How it looks

Rounded panels, cards and pictures, soft shadows, a blurred background, and smooth animations that run on the player's client. The corner images are drawn by the plugin itself, so no other plugin or download is needed. Small controls are flat, so a whole archive page is about 160 UI elements (about 100 with `RoundedCorners` off).

### Installation

1. Put `NewsBroadcaster.cs` in `oxide/plugins/`.
2. Give your players the view permission: `oxide.grant group default newsbroadcaster.view` (or set `RequireViewPermission` to `false`).
3. Give yourself admin: `oxide.grant user <SteamID> newsbroadcaster.admin`, then type `/news admin` in game.

### Permissions

| Permission | What it does |
|---|---|
| `newsbroadcaster.view` | See news (toasts, popups, archive) |
| `newsbroadcaster.admin` | Create, edit and delete posts, change the theme |

Server admins (auth level) always have admin access.

### Commands

| Chat | What it does |
|---|---|
| `/news` | Open the archive |
| `/news latest` | Open the newest post |
| `/news read` | Mark everything as read |
| `/news settings` | Sound, pop-up and join options |
| `/news admin` | Open News Management (admins) |
| `/news help` | List the commands |

| Console / RCON | What it does |
|---|---|
| `news.show "Title" "ImageURL or -" "Text" [Type]` | Post an announcement |
| `news.poll "Title" "Text" "A\|B\|C" [Type]` | Post an announcement with a poll |
| `news.list` | List posts with index, id, reads and likes |
| `news.publish <index\|id>` | Publish a draft |
| `news.delete <index\|id>` | Delete a post |
| `news.resend <index\|id>` | Notify online players again |
| `news.trigger <SteamID/Name> [index\|id]` | Open a post for one player |
| `news.admin` | Open News Management |

Types: `Info`, `Warning`, `Alert`, `Event`, `Update`.

### Config (the options most servers change)

| Option | Default | What it does |
|---|---|---|
| `ServerName` | `SERVER NEWS` | The name shown in the popup header |
| `ShowNewsOnConnect` | `true` | Show the newest unseen post when a player wakes up |
| `UnreadReminderOnConnect` | `true` | One chat line about posts missed while offline |
| `RequireViewPermission` | `true` | `false` lets everyone see news |
| `EnableAutoClose` / `AutoCloseSeconds` | `true` / `15` | Broadcast popups close by themselves |
| `AnnouncementsPerPage` | `5` | Rows per page (keep it between 1 and 8) |
| `MaxStoredAnnouncements` | `50` | Oldest posts are removed above this; pinned posts, drafts and the newest post stay |
| `PopupImageFrame` | `Tall` | `Wide` keeps a 16:9 frame for widescreen pictures |
| `Notification.Position` | `TopRight` | Six toast positions, plus X / Y offsets |
| `Discord.WebhookUrl` | empty | Paste a webhook URL and set `Enabled` to post to Discord |
| `Rewards` | off | Read and like rewards: items, points, currency |
| `Polls.AllowVoteChange` | `true` | Players can change their vote |

Every option is explained in the documentation (`PLUGIN_DOCS.md`).

### Works with

| Plugin | When you need it |
|---|---|
| ServerRewards | For RP as a read or like reward |
| Economics | For currency as a read or like reward |
| Notify | Only if you turn on `UseNotifyPlugin` |
| ImageLibrary | Optional: caches post pictures so they load faster |

None of them is required.

### For developers

Seven hooks (`OnNewsBroadcast`, `OnNewsEdited`, `OnNewsDeleted`, `OnNewsRead`, `OnNewsLiked`, `OnNewsPollVote`, `OnNewsPollClosed`) with plain dictionary payloads, and methods to post from your own plugin:

```csharp
NewsBroadcaster?.Call("API_PostNews", "Airdrop event", "Starts in 10 minutes!", "Event");
```

Also `API_CreateNews` (polls, groups, countdowns, drafts), `API_DeleteNews`, `API_GetNews`, `API_GetNewsList`, `API_GetUnreadCount` and `API_ShowNews`.

### Good to know

- Rust shows only direct links to PNG or JPG pictures. The editor tells you when a link will not work.
- A post cannot be scheduled for a set time; save it as a draft and publish it when it is time (in game or with `news.publish`).
- Polls are single choice.

### Support

Message me here or on Discord: **YOUR DISCORD NAME**. Tell me your Rust and Oxide version and paste the console error if there is one.

---

## Changelog entry

**1.0.0** — First release.

Popups and toasts, polls with timers, event countdowns, archive with filters, search and unread tracking, likes, read and like rewards, drafts, copy post, posts for one group, auto-delete, News Management dashboard, editor with preview and autosave, six themes, Discord webhook, console commands, hooks and API methods for other plugins.

---

## Notes for the review team

Put this in the message to the reviewers if the form has a field for it. It answers what a reviewer usually has to work out from the code.

- **What it is:** one C# file, one class (`Oxide.Plugins.NewsBroadcaster : RustPlugin`), about 5,400 lines. No LINQ; temporary lists come from `Facepunch.Pool`. No `System.IO`, `System.Net`, reflection or threads.
- **Network:** the only outgoing request is the Discord webhook, to the URL the server owner puts in the config, through Oxide's `webrequest`. Nothing else is contacted. No licence check, no telemetry, no code is downloaded.
- **Files:** it writes its own config, lang and data file (`oxide/data/NewsBroadcaster_Data.json`) through Oxide. At startup it stores eight small PNGs (rounded corners, drawn in code) in the server's `FileStorage`.
- **Server load:** no per-tick or per-frame hooks and no repeating timer. One one-shot timer waits for the next auto-delete or poll closing; when nothing is scheduled, none runs. Likes, reads, votes, paid rewards and editor autosaves are written together with a 15-second delay, and on server save and unload. UI is sent only when a player clicks: an archive page is about 160 elements (about 100 with `RoundedCorners` off), and page, filter and search clicks redraw at most once every 0.4 seconds per player.
- **Config and data:** the plain Oxide shape (`ReadObject`, `WriteObject`). No migrations: files from the earlier builds are not converted.
- **Hooks used:** `Init`, `OnServerInitialized`, `OnPluginLoaded`, `OnServerSave`, `OnPlayerSleepEnded`, `OnPlayerDisconnected`, `Unload`.
- **Permissions:** `newsbroadcaster.view` and `newsbroadcaster.admin`. Every admin command checks the permission on the server, and every player-facing command checks whether that player may see the post.
- **Optional plugins** (`ImageLibrary`, `Notify`, `ServerRewards`, `Economics`) are null-checked and `IsLoaded`-checked before each call.
- **Tested:** compiled as C# 6 and C# 7 against the .NET Framework 4.8 API with stand-ins for the Oxide and Rust types, and run through 378 automated checks (commands, permissions, config and data loading, UI tree and element counts, cleanup on disconnect and unload).

---

## Before you press submit

1. **Load it on your own server once** (`oxide.reload NewsBroadcaster`). The automated checks use stand-ins for the game, and RogueDepot compiles uploads against the real Rust and Oxide files. A real load is the only proof that it compiles there.
2. Click through it in game: post with `news.show`, open `/news`, vote in a poll, open `/news admin`, write a post, close the game mid-post and check the UNSAVED bar when you are back.
3. Read the creator agreement on the site before you list. It says you must be able to explain how the plugin works and support it for buyers. `PLUGIN_DOCS.md` and `new-joiner-architecture.tour` are the places to look things up.
4. Replace **YOUR DISCORD NAME** in the Support section.
5. Decide on Carbon (see Basics) and on the price.
