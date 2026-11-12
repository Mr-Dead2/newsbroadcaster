# Testing NewsBroadcaster in game

`NewsBroadcasterTest.cs` is a helper plugin for a **test server**. It makes sample posts, posts with awkward texts, logs the plugin's hooks, runs a self-test of the API and times it. It only uses NewsBroadcaster's public API (the `API_*` methods and `OnNews*` hooks), like any other plugin would.

**Do not install it on a live server and do not ship it.** Only `NewsBroadcaster.cs` goes to buyers.

It is compiled (C# 6 and C# 7) and tested by `tools/test-harness/run.sh` together with the plugin, so it keeps working when the plugin changes.

## 1. Set up a test server

1. Install a Rust dedicated server with SteamCMD (app `258550`) and put Oxide (umod.org) on top.
2. Copy `NewsBroadcaster.cs` and `tools/ingame-test/NewsBroadcasterTest.cs` to `oxide/plugins/`.
3. Optional: also install ImageLibrary, to test both with and without it.
4. In the server console: `ownerid <yourSteamID>`, then `server.writecfg`.
5. In game, F1: `client.connect localhost:28015`.

Keep the server console in view: compile errors, exceptions and Oxide's "took too long" warnings show up there.

## 2. Commands

All commands run in the server console, RCON or the F1 console in game (admins only). `nbtest.help` lists them.

| Command | What it does |
|---|---|
| `nbtest.run` | Self-test: creates posts through the API, checks what comes back (fields, timers, drafts, groups, unread count, hooks), deletes them again. Prints PASS / FAIL. Run it from F1 in game: some checks need a player. |
| `nbtest.seed [notify]` | 12 sample posts: every type, pictures, a pinned post, two polls (one closes after 1 hour), a 2-minute and a 1-day countdown, a post that deletes itself after a day, a group-only post, a draft, a long title |
| `nbtest.junk [notify]` | Awkward texts: trailing backslashes, quotes, `{0}` braces, broken and working rich-text tags, emoji and other scripts, a 120-character title, an empty text with a broken picture link, links. Each post's text says what you should see |
| `nbtest.long [chars]` | One post with a very long text (default 11,500 characters; the plugin keeps at most 12,000) |
| `nbtest.countdown [sec] [label]` | A countdown that ends in `sec` seconds (default 90), announced to everyone |
| `nbtest.poll [hours]` | A poll that closes after `hours` (default 1, `0` = no timer), announced to everyone |
| `nbtest.group` | A post only the test group (`nbtest`) sees; creates the group |
| `nbtest.flood [n] [sec]` | `n` posts, one every `sec` seconds, with toasts (default 5 every 2 s) |
| `nbtest.show [player] [id]` | Open a post for a player (default: you, the newest post) |
| `nbtest.unread [player]` | A player's unread count |
| `nbtest.list` | Every post players can see |
| `nbtest.hooks [on\|off]` | Print every NewsBroadcaster hook with its payload in the server console |
| `nbtest.bench [n]` | Server time per API call on your machine |
| `nbtest.check` | What is installed and set up (version, ImageLibrary, permissions, test group) |
| `nbtest.clear` | Delete every post the helper made (your own posts are left alone) |

The helper's config (`oxide/config/NewsBroadcasterTest.json`) holds the test group name and the picture links of the sample posts. Replace the links if your server can't reach them.

## 3. Built-in tools that help

- **F1 console (client):** the game's UI errors show here (`AddUI: Unknown Parent`, JSON errors). It should stay empty while you click around.
- **`print_alluis`** (F1, as admin) lists every UI panel you have. After closing the menu, nothing named `NewsBroadcasterUI…` should be left.
- **`oxide.plugins`** shows how much time each plugin spent in hooks. Compare NewsBroadcaster with your other plugins after a busy test.
- **`news.list`** shows every post, drafts and timers included; `news.trigger <name> [index]` forces a pop-up onto one player.
- **Files:** `oxide/data/NewsBroadcaster_Data.json`, `oxide/logs/`.

## 4. A test round (about 20 minutes)

1. **Start:** `nbtest.check`, then `nbtest.run` in F1. Everything must PASS.
2. **Content:** `nbtest.seed notify`.
   - Open `/news`: three pages, thumbnails only with ImageLibrary, NEW badges, the group post visible to you (you are an admin).
   - Open every post. Look at the rounded corners (no light squares or seams), the pictures (inside their frame), the scrolling of the long post, the title sizes.
   - Leave the "Raid base event" pop-up open until its countdown reaches zero.
3. **Awkward texts:** `nbtest.junk`, then open each one. Every screen must draw, F1 must stay free of UI errors, and each post looks the way its text says. Tags in titles and in texts with a stray tag show as plain text (the game only reads a text's tags when they pair up, and titles are in capitals); the "Rich text that works" post shows real colours, bold and size.
4. **Players' actions:** like, unlike, vote, change your vote. Only the button or the poll redraws; the scroll position stays.
5. **Hooks:** `nbtest.hooks on`, like and vote again, watch the server console, then `nbtest.hooks off`.
6. **Admin:** `/news admin`.
   - Try the tabs, the search, the sort, the bulk selection, pin and unpin, COPY, EDIT, PUBLISH on the draft, and the themes.
   - In the editor, type a countdown time of `2m`, tick CLOSE VOTING on a poll (the result goes to chat), PREVIEW.
7. **Autosave:** type something in a new post, `disconnect`, reconnect, `/news admin`. The UNSAVED bar offers CONTINUE EDITING.
8. **Joining and respawning:**
   - F1 `kill` and respawn: no pop-up for news you already saw.
   - Post something with `nbtest.countdown 300`, `disconnect`, reconnect: the pop-up and the unread reminder show.
9. **Many toasts:** `nbtest.flood 10 1`. The toasts replace each other, and none stays on screen.
10. **Reload with the menu open:** open `/news`, then `oxide.reload NewsBroadcaster` in the server console. The menu closes, and `print_alluis` shows nothing left.
11. **Without ImageLibrary:** `oxide.unload ImageLibrary`, then `oxide.reload NewsBroadcaster`.
    - The console prints a hint.
    - The lists show no thumbnails.
    - The pop-up still shows its picture.
12. **Broken data file:**
    1. Back up `oxide/data/NewsBroadcaster_Data.json`.
    2. `oxide.unload NewsBroadcaster`.
    3. Delete one `}` from the file.
    4. `oxide.load NewsBroadcaster`.

    The console shows an error. The file is not touched, even after a like or a server save, and `/news admin` tells you nothing is saved. Put the backup back and reload.
13. **Clean up:** `nbtest.clear`.

## 5. Testing as a normal player

As the server owner you always count as an admin. Use a second account or a friend, or take your owner status away for a while:

1. `removeowner <yourSteamID>`, then `server.writecfg`, then reconnect.
2. `oxide.grant user <name> newsbroadcaster.view`.
3. Check that the group-only post is hidden.
4. `oxide.usergroup add <name> nbtest`: the post shows.
5. `oxide.usergroup remove <name> nbtest`: it is hidden again.
6. `oxide.revoke user <name> newsbroadcaster.view` (with `RequireViewPermission` on): `/news` says you have no permission.
7. Give yourself owner back: `ownerid <yourSteamID>`, `server.writecfg`, reconnect.
