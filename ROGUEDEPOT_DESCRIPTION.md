# <span style="color:#5cb5ff">In-game news for your Rust server</span>

Post an announcement and everyone online gets a toast. Players read it in a clean popup, vote in polls, watch the wipe countdown and catch up on what they missed with `/news`. You run all of it from an admin panel inside the game.

> **One file. Needs only Oxide.** No web panel, no database, no required plugins.

---

## <span style="color:#5cb5ff">For your players</span>

| Feature | What players get |
| --- | --- |
| **Toast and popup** | A toast the moment something is posted. Click it for the full post: picture, title, author, date and text. Long posts scroll, long titles shrink to fit. |
| **Polls** | 2 to 4 options on any post. One click to vote, live result bars, and the vote can be changed. A poll can close on a timer, and the winner is announced in chat. |
| **Event countdowns** | A live `WIPE IN 2 DAYS 04:12:33` timer inside the post. |
| **Archive** | `/news` opens every post, with thumbnails, type filters, a Polls filter and a search box. |
| **Unread tracking** | `NEW` badges, an unread counter, **Mark all read**, and one chat reminder about posts missed while offline. |
| **Likes** | A like button on every post. |
| **Their own settings** | `/news settings`: turn off the sound, get a chat line instead of pop-ups, or skip the post shown when joining. |

Five post types, each with its own colour: **<span style="color:#6699cc">INFO</span>**, **<span style="color:#e6991a">WARNING</span>**, **<span style="color:#d94040">ALERT</span>**, **<span style="color:#994dcc">EVENT</span>** and **<span style="color:#33b3e6">UPDATE</span>**.

---

## <span style="color:#5cb5ff">For you</span>

| Feature | What you get |
| --- | --- |
| **News Management** | A dashboard with stat cards (published, drafts, reads, likes, poll votes), tabs, search and sorting. Every post shows its reads, likes and votes. |
| **Editor with preview** | Title, picture, text, type, poll and countdown on one screen. See the exact popup before you publish. |
| **Autosave** | A disconnect, a restart or a plugin reload never loses a half-written post. |
| **Drafts and copy** | Save a post for later. Copy last week's wipe post in one click. |
| **Posts for one group** | VIP news or staff notes: only that Oxide group (and admins) can see them. |
| **Auto-delete** | Event posts remove themselves 1 to 30 days after publishing. |
| **Pin, resend, bulk tools** | Keep the rules on top, notify everyone online again, or pin and delete many posts at once. |
| **Rewards** | Pay players for reading or liking a post: items, RP or currency, once per player per post. |
| **Discord** | A webhook posts every announcement as an embed in the type's colour, with the picture and an optional role ping. Poll results are posted too. |
| **Console and RCON** | Post, list, publish, resend and delete from the console, so a scheduler or another tool can do it. |
| **Image link check** | The editor warns about links that will not show in game: imgur pages, Discord links that expire, GIFs. |
| **Themes** | Six built in, and as many of your own as you like. |
| **Translations** | Every text is in the lang file. |

---

## <span style="color:#5cb5ff">How it looks</span>

- Rounded corners on every panel, button and picture
- Soft shadows over a blurred background
- Smooth animations that run on the player's game: **<span style="color:#ff7a33">no lag and no extra traffic</span>**
- Your theme colours everywhere

The corner images are drawn by the plugin itself. No other plugin and no download is needed for the look.

---

## <span style="color:#5cb5ff">Quick start</span>

1. Put `NewsBroadcaster.cs` in `oxide/plugins/`.
2. Run the two commands below in the server console.
3. Type `/news admin` in game and press **NEW ANNOUNCEMENT**.

```
oxide.grant group default newsbroadcaster.view
oxide.grant user <SteamID> newsbroadcaster.admin
```

Or post straight from the console:

```
news.show "Wipe Day" "-" "The map has been wiped. Good luck out there!" Event
```

---

## <span style="color:#5cb5ff">Permissions</span>

| Permission | What it does |
| --- | --- |
| `newsbroadcaster.view` | See news: toasts, popups and the archive. Set `RequireViewPermission` to `false` to let everyone see news |
| `newsbroadcaster.admin` | Create, edit and delete posts, change the theme |

Server admins (auth level) always have admin access.

---

## <span style="color:#5cb5ff">Reference</span>

<details>
<summary>Commands</summary>

**Chat**

- `/news` opens the archive
- `/news latest` opens the newest post
- `/news read` marks everything as read
- `/news settings` has the sound, pop-up and join options
- `/news admin` opens News Management (admins)
- `/news help` lists the commands

**Console / RCON**

- `news.show` posts an announcement
- `news.poll` posts an announcement with a poll
- `news.list` lists posts with index, id, reads and likes
- `news.publish` publishes a draft
- `news.delete` deletes a post
- `news.resend` notifies online players again
- `news.trigger` opens a post for one player
- `news.admin` opens News Management

```
news.show "Title" "ImageURL or -" "Text" [Type]
news.poll "Title" "Text" "Option 1|Option 2|Option 3" [Type]
news.publish <index or id>
news.delete <index or id>
news.resend <index or id>
news.trigger <SteamID or name> [index or id]
```

Types: `Info`, `Warning`, `Alert`, `Event`, `Update`. Use `-` for no picture and `\n` for a new line.

</details>

<details>
<summary>Config: the options most servers change</summary>

| Option | Default | What it does |
| --- | --- | --- |
| `ServerName` | `SERVER NEWS` | The name shown in the popup header |
| `ShowNewsOnConnect` | `true` | Show the newest unseen post when a player wakes up |
| `UnreadReminderOnConnect` | `true` | One chat line about posts missed while offline |
| `RequireViewPermission` | `true` | `false` lets everyone see news |
| `EnableAutoClose` | `true` | Broadcast popups close by themselves after `AutoCloseSeconds` (15) |
| `AnnouncementsPerPage` | `5` | Rows per page, 1 to 8 |
| `MaxStoredAnnouncements` | `50` | The oldest posts are removed above this. Pinned posts and drafts stay |
| `PopupImageFrame` | `Tall` | `Wide` keeps a 16:9 frame for widescreen pictures |
| `Notification.Position` | `TopRight` | Six toast positions, plus X and Y offsets |
| `Discord.WebhookUrl` | empty | Paste a webhook URL and set `Enabled` to post to Discord |
| `Rewards` | off | Read and like rewards: items, points, currency |
| `Polls.AllowVoteChange` | `true` | Players can change their vote |

New options are added to your config file by themselves when you update.

</details>

<details>
<summary>For developers: hooks and API</summary>

Seven hooks with plain dictionary payloads: `OnNewsBroadcast`, `OnNewsEdited`, `OnNewsDeleted`, `OnNewsRead`, `OnNewsLiked`, `OnNewsPollVote`, `OnNewsPollClosed`.

Post from your own plugin:

```csharp
[PluginReference] private Plugin NewsBroadcaster;

void AnnounceEvent()
{
    NewsBroadcaster?.Call("API_PostNews", "Airdrop event", "Starts in 10 minutes!", "Event");
}
```

Also `API_CreateNews` (polls, groups, countdowns, drafts), `API_DeleteNews`, `API_GetNews`, `API_GetNewsList`, `API_GetUnreadCount` and `API_ShowNews`.

</details>

<details>
<summary>Questions and answers</summary>

**Which other plugins do I need?**
Only Oxide. The plugins under "Works with" add extras.

**Why does my picture not show in game?**
Rust needs a direct link to a PNG or JPG. The editor tells you when a link will not work.

**How do I make a post only VIPs can see?**
Create the group (`oxide.group add vip`), add players to it, then pick **VIP ONLY** under **WHO CAN SEE IT** in the editor.

**Can players turn the notifications off?**
Yes, with `/news settings`: the sound, the pop-ups and the post shown when joining.

**Can I schedule a post for later?**
Not at a set time. Save it as a draft and publish it when it is time, in game or with `news.publish` over RCON.

**Can a poll have more than one answer?**
No, polls are single choice.

</details>

---

## <span style="color:#5cb5ff">Works with</span>

| Plugin | When you need it |
| --- | --- |
| **ServerRewards** | For RP as a read or like reward |
| **Economics** | For currency as a read or like reward |
| **Notify** | Only if you turn on `UseNotifyPlugin` |
| **ImageLibrary** | Optional. It caches post pictures so they load faster |

**<span style="color:#ff7a33">None of them is required.</span>**

---

## <span style="color:#5cb5ff">Support</span>

Message me through my RogueDepot profile. Tell me your Rust and Oxide version and paste the console error if there is one, and I will look into it.
