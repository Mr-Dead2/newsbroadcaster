# NewsBroadcaster on RogueDepot

Everything for RogueDepot's "new listing" form, in the order the form asks for it.

---

## Step 1: Details

| Field | What to enter |
|---|---|
| **Listing Name** | `NewsBroadcaster` (the name of the file and of the plugin in the console; "News Broadcaster" with a space also works) |
| **Tagline** | `In-game news with popups, polls, countdowns, rewards, Discord and a full admin panel.` |
| **Platform** | Rust |
| **Category** | Plugins (or the closest one in the list) |
| **Description** | Paste everything in `ROGUEDEPOT_DESCRIPTION.md` (about 8,800 of the 50,000 characters). It uses only what the editor's formatting help lists: headings, tables, quotes, code blocks, `<details>` spoilers and `<span style="color:...">` |
| **Tags** (max 10) | `news, announcements, ui, notifications, polls, countdown, discord, rewards, admin, vip` |

Other taglines, if you want a different angle:

- `Post news in game and everyone online sees it. Polls, countdowns, VIP posts, rewards and Discord.`
- `A clean in-game news system: toasts, popups, polls, wipe countdowns, archive, themes.`

After pasting, press **Preview**. The colours are the plugin's blue (`#5cb5ff`) and the logo's orange (`#ff7a33`). The description does not repeat the listing name as a heading, because the page shows it already. To show pictures inside the text, drop them into the editor where you want them (for example `previews/02_popup_image.png` under the first paragraph); the gallery from step 2 is shown either way.

---

## Step 2: Files & media

**Download:** `NewsBroadcaster.cs`. One file, no dependencies.

**Screenshots:** the first one becomes the listing cover, so upload them in this order.

| # | File | What it shows |
|---|---|---|
| 1 (cover) | `branding/banner.png` | The logo and the name |
| 2 | `previews/02_popup_image.png` | The popup with a picture |
| 3 | `previews/01_notification.png` | The toast for a new post |
| 4 | `previews/10a_popup_poll_vote.png` | A poll with a timer |
| 5 | `previews/10b_popup_poll_results.png` | Poll results |
| 6 | `previews/13_popup_countdown.png` | The live countdown |
| 7 | `previews/04_archive.png` | The archive with unread badges |
| 8 | `previews/11_archive_filtered.png` | Archive filters and search |
| 9 | `previews/05_admin.png` | News Management |
| 10 | `previews/05c_admin_filtered.png` | Tabs, search and sort |
| 11 | `previews/07b_editor_new.png` | The editor |
| 12 | `previews/07c_editor_poll.png` | The editor with a poll |
| 13 | `previews/09_editor_preview.png` | Preview before publishing |
| 14 | `previews/05b_admin_unsaved.png` | Editor autosave |
| 15 | `previews/08_themes.png` | Theme picker |
| 16 | `previews/12_settings.png` | Player settings |

If there is a limit on the number of screenshots, keep 1, 2, 4, 6, 7, 9 and 11.

**Video:** `previews/newsbroadcaster-animations.mp4`.

The screenshots are renders from the test tool, not captures from a live server. They show the real layout and colours; if the store wants in-game captures, take them on your server.

---

## Step 3: Pricing & extras

- **Price:** your call.
- **License:** the store's standard licence, unless you want something else.
- **Collaborators:** none.

**Carbon:** the plugin only uses the standard Oxide plugin API (CUI, lang, permissions, data files, web requests, timers), which Carbon supports, so it should load there. It has not been tested on Carbon. Say it supports Carbon only after you have loaded it on a Carbon server once.

---

## Changelog entry

**1.0.0** — First release.

Popups and toasts, polls with timers, event countdowns, archive with filters, search and unread tracking, likes, read and like rewards, drafts, copy post, posts for one group, auto-delete, News Management dashboard, editor with preview and autosave, six themes, Discord webhook, console commands, hooks and API methods for other plugins.

---

## Notes for the review team

Put this in the message to the reviewers if the form has a field for it. It answers what a reviewer usually has to work out from the code.

- **What it is:** one C# file, one class (`Oxide.Plugins.NewsBroadcaster : RustPlugin`), about 5,700 lines. No `System.IO`, `System.Net`, reflection or threads.
- **Network:** the only outgoing request is the Discord webhook, to the URL the server owner puts in the config, through Oxide's `webrequest`. Nothing else is contacted. No licence check, no telemetry, no code is downloaded.
- **Files:** it writes its own config, lang and data file (`oxide/data/NewsBroadcaster_Data.json`) through Oxide. At startup it stores eight small PNGs (rounded corners, drawn in code) in the server's `FileStorage`.
- **Server load:** no per-tick or per-frame hooks. One 30-second timer deletes expired posts and closes timed polls. Likes, reads and editor autosaves are written with a 15-second delay, and on server save and unload. UI is sent only when a player clicks.
- **Hooks used:** `Init`, `OnServerInitialized`, `OnPluginLoaded`, `OnServerSave`, `OnPlayerSleepEnded`, `OnPlayerDisconnected`, `Unload`.
- **Permissions:** `newsbroadcaster.view` and `newsbroadcaster.admin`. Every admin command checks the permission on the server, and every player-facing command checks whether that player may see the post.
- **Optional plugins** (`ImageLibrary`, `Notify`, `ServerRewards`, `Economics`) are null-checked and `IsLoaded`-checked before each call.
- **Tested:** compiled as C# 6 and C# 7 against the .NET Framework 4.8 API with stand-ins for the Oxide and Rust types, and run through 340 automated checks (commands, permissions, data migration, UI tree, cleanup on disconnect and unload).

---

## Before you press submit

1. **Load it on your own server once** (`oxide.reload NewsBroadcaster`). The automated checks use stand-ins for the game, and RogueDepot compiles uploads against the real Rust and Oxide files. A real load is the only proof that it compiles there.
2. Click through it in game: post with `news.show`, open `/news`, vote in a poll, open `/news admin`, write a post, close the game mid-post and check the UNSAVED bar when you are back.
3. Read the creator agreement on the site before you list. It says you must be able to explain how the plugin works and support it for buyers. `PLUGIN_DOCS.md` and `new-joiner-architecture.tour` are the places to look things up.
4. The Support section says "message me through my RogueDepot profile". Add your Discord there if you want buyers to reach you that way.
5. Decide on Carbon (see step 3) and on the price.
