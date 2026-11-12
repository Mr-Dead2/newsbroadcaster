using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    // Test helper for NewsBroadcaster. For a test server only: do not install it on a live server and do not ship it.
    // Every command is a console command (server console, RCON or F1 in game) for admins. Type nbtest.help for the list.
    // It only uses NewsBroadcaster's public API (API_* methods and OnNews* hooks), the same way any other plugin would.
    [Info("NewsBroadcasterTest", "DEDA", "1.0.0")]
    [Description("Test-server helper for NewsBroadcaster: sample posts, awkward texts, hook log, self-test and timings")]
    public class NewsBroadcasterTest : RustPlugin
    {
        [PluginReference] private Plugin NewsBroadcaster, ImageLibrary;

        private const string Author = "NBTEST";
        private const string DataFile = "NewsBroadcasterTest";
        private static readonly string[] HookKeys = { "id", "title", "author", "type", "timestamp", "date", "text", "imageUrl", "likes", "pinned", "pollOptions", "pollVotes", "group", "expiresAt", "pollEndsAt", "countdownTo" };

        private TestConfig config;
        private TestData data;
        private bool logHooks;
        private List<string> hooksSeen;
        private Timer floodTimer;

        #region Config and data
        private class TestConfig
        {
            [JsonProperty("Test group (created when needed)")]
            public string TestGroup = "nbtest";

            [JsonProperty("Picture links used by the sample posts (replace them if your server can't reach these)")]
            public List<string> Pictures = new List<string>
            {
                "https://picsum.photos/seed/nbtest-wipe/960/540.jpg",
                "https://picsum.photos/seed/nbtest-event/960/540.jpg",
                "https://picsum.photos/seed/nbtest-raid/960/540.jpg",
                "https://picsum.photos/seed/nbtest-tall/600/800.jpg"
            };
        }

        private class TestData
        {
            public List<string> CreatedIds = new List<string>();
        }

        protected override void LoadDefaultConfig() => config = new TestConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<TestConfig>();
                if (config == null) throw new JsonException("the file is empty");
            }
            catch (Exception ex)
            {
                PrintError("Config could not be read, using defaults: " + ex.Message);
                config = new TestConfig();
            }
            if (config.Pictures == null || config.Pictures.Count == 0) config.Pictures = new TestConfig().Pictures;
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        private void Init()
        {
            data = Interface.Oxide.DataFileSystem.ReadObject<TestData>(DataFile) ?? new TestData();
            if (data.CreatedIds == null) data.CreatedIds = new List<string>();
        }

        private void OnServerInitialized()
        {
            PrintWarning("Test helper loaded. Use it on a test server only. Type nbtest.help in the console.");
        }

        private void Unload()
        {
            floodTimer?.Destroy();
            SaveData();
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(DataFile, data);

        private string Picture(int index) => config.Pictures[index % config.Pictures.Count];
        #endregion

        #region Helpers
        private bool Allowed(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null || arg.IsAdmin) return true;
            if (permission.UserHasPermission(arg.Connection.userid.ToString(), "newsbroadcaster.admin")) return true;
            Reply(arg, "nbtest: admins only.");
            return false;
        }

        private bool Ready(ConsoleSystem.Arg arg)
        {
            if (!Allowed(arg)) return false;
            if (NewsBroadcaster != null && NewsBroadcaster.IsLoaded) return true;
            Reply(arg, "nbtest: NewsBroadcaster is not loaded. Check the console for its compile errors, then oxide.load NewsBroadcaster.");
            return false;
        }

        private void Reply(ConsoleSystem.Arg arg, string text) => SendReply(arg, text);

        private string Create(Dictionary<string, object> options)
        {
            if (!options.ContainsKey("author")) options["author"] = Author;
            if (!options.ContainsKey("notify")) options["notify"] = false;
            if (!options.ContainsKey("discord")) options["discord"] = false;
            string id = NewsBroadcaster.Call("API_CreateNews", options) as string;
            if (!string.IsNullOrEmpty(id))
            {
                data.CreatedIds.Add(id);
                SaveData();
            }
            return id;
        }

        private Dictionary<string, object> Get(string id) => NewsBroadcaster.Call("API_GetNews", id) as Dictionary<string, object>;

        private List<Dictionary<string, object>> List() => NewsBroadcaster.Call("API_GetNewsList") as List<Dictionary<string, object>> ?? new List<Dictionary<string, object>>();

        private int Unread(BasePlayer player)
        {
            object count = NewsBroadcaster.Call("API_GetUnreadCount", player);
            return count is int ? (int)count : -1;
        }

        private bool Show(BasePlayer player, string id)
        {
            object shown = NewsBroadcaster.Call("API_ShowNews", player, id);
            return shown is bool && (bool)shown;
        }

        private BasePlayer Target(ConsoleSystem.Arg arg, int index)
        {
            string who = arg.GetString(index);
            if (string.IsNullOrEmpty(who)) return arg.Player();
            return BasePlayer.Find(who);
        }

        private string EnsureGroup()
        {
            string group = string.IsNullOrEmpty(config.TestGroup) ? "nbtest" : config.TestGroup;
            if (!permission.GroupExists(group)) permission.CreateGroup(group, group, 0);
            return group;
        }

        private static string Lines(params string[] lines) => string.Join("/n", lines);

        private static string Value(Dictionary<string, object> post, string key)
        {
            object value;
            return post != null && post.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string Short(string text, int max)
        {
            text = (text ?? string.Empty).Replace("\n", " ");
            return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
        }
        #endregion

        #region Commands
        [ConsoleCommand("nbtest.help")]
        private void CmdHelp(ConsoleSystem.Arg arg)
        {
            if (!Allowed(arg)) return;
            Reply(arg, string.Join("\n", new[]
            {
                "NewsBroadcasterTest commands (console, RCON or F1):",
                "  nbtest.run                 self-test of the API and hooks, prints PASS / FAIL (leaves nothing behind)",
                "  nbtest.seed [notify]       12 sample posts: every type, pictures, polls, countdowns, group-only, draft, pinned",
                "  nbtest.junk [notify]       awkward texts: trailing backslashes, quotes, braces, rich text, unicode, 120-char title",
                "  nbtest.long [chars]        one post with a very long text (default 11500 characters)",
                "  nbtest.countdown [sec]     a post whose countdown ends in sec seconds (default 90), shown to everyone online",
                "  nbtest.poll [hours]        a poll that closes after hours (default 1), shown to everyone online",
                "  nbtest.group               a post only the test group sees (creates the group)",
                "  nbtest.flood [n] [sec]     n posts, one every sec seconds, with toasts (default 5 every 2 s)",
                "  nbtest.show [player] [id]  open a post for a player (default: you, newest post)",
                "  nbtest.unread [player]     unread count of a player",
                "  nbtest.list                every post players can see (newest first)",
                "  nbtest.hooks [on|off]      print every NewsBroadcaster hook in the console",
                "  nbtest.bench [n]           how long the API calls take on this server",
                "  nbtest.check               what is installed and set up",
                "  nbtest.clear               delete every post this helper created"
            }));
        }

        [ConsoleCommand("nbtest.check")]
        private void CmdCheck(ConsoleSystem.Arg arg)
        {
            if (!Allowed(arg)) return;
            var sb = new StringBuilder("nbtest.check\n");
            bool news = NewsBroadcaster != null && NewsBroadcaster.IsLoaded;
            sb.AppendLine("  NewsBroadcaster: " + (news ? "loaded, version " + NewsBroadcaster.Version : "NOT LOADED"));
            sb.AppendLine("  ImageLibrary:    " + (ImageLibrary != null && ImageLibrary.IsLoaded ? "loaded (thumbnails on, pictures stored once)" : "not loaded (no list thumbnails, pop-up pictures load from the link)"));
            sb.AppendLine("  permission newsbroadcaster.admin: " + (permission.PermissionExists("newsbroadcaster.admin") ? "registered" : "MISSING"));
            sb.AppendLine("  permission newsbroadcaster.view:  " + (permission.PermissionExists("newsbroadcaster.view") ? "registered" : "MISSING"));
            sb.AppendLine("  test group '" + config.TestGroup + "': " + (permission.GroupExists(config.TestGroup) ? "exists" : "not created yet (nbtest.group creates it)"));
            if (news)
            {
                sb.AppendLine("  posts players can see: " + List().Count + ", made by this helper: " + data.CreatedIds.Count);
                var player = arg.Player();
                if (player != null) sb.AppendLine("  your unread count: " + Unread(player) + (player.IsAdmin ? " (you are an admin: you see every group)" : ""));
            }
            sb.Append("  players online: " + BasePlayer.activePlayerList.Count);
            Reply(arg, sb.ToString());
        }

        [ConsoleCommand("nbtest.seed")]
        private void CmdSeed(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            bool notify = string.Equals(arg.GetString(0), "notify", StringComparison.OrdinalIgnoreCase);
            string group = EnsureGroup();
            DateTime now = DateTime.UtcNow;
            int made = 0;

            var posts = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { ["title"] = "Wipe day - fresh map is live", ["type"] = "Event", ["imageUrl"] = Picture(0), ["text"] = Lines("The map has been wiped and a brand-new 4250 procedural map is live!", "", "What's new this wipe:", "• Blueprints have been wiped", "• Scrap from barrels increased (x2)", "• Airdrop event every Saturday at 20:00", "", "Good luck out there!") },
                new Dictionary<string, object> { ["title"] = "Server rules", ["type"] = "Info", ["pinned"] = true, ["text"] = Lines("1. No cheating or exploiting.", "2. No racism or harassment.", "3. Group limit: 4 players.", "4. Offline raiding allowed after 18:00 on Friday.") },
                new Dictionary<string, object> { ["title"] = "Raid base event", ["type"] = "Event", ["imageUrl"] = Picture(2), ["countdownTo"] = now.AddMinutes(2), ["countdownLabel"] = "Event starts in", ["text"] = "The countdown above ends two minutes after nbtest.seed. Watch it reach zero while the pop-up is open." },
                new Dictionary<string, object> { ["title"] = "Halloween event", ["type"] = "Event", ["countdownTo"] = now.AddDays(1).AddHours(2), ["countdownLabel"] = "Starts in", ["text"] = "A countdown longer than a day: the day count is drawn as text." },
                new Dictionary<string, object> { ["title"] = "Next wipe: pick the map size", ["type"] = "Event", ["pollOptions"] = new[] { "3500 (small)", "4250 (medium)", "4500 (large)", "5000 (huge)" }, ["pollHours"] = 1, ["text"] = "This poll closes one hour after nbtest.seed. The result is announced in chat." },
                new Dictionary<string, object> { ["title"] = "Do you like the new monument?", ["type"] = "Info", ["pollOptions"] = new[] { "Yes", "No" }, ["text"] = "A poll without a timer. Close it from the editor (CLOSE VOTING) to see the chat result." },
                new Dictionary<string, object> { ["title"] = "Server restart tonight", ["type"] = "Warning", ["expireDays"] = 1, ["text"] = "This post deletes itself one day after nbtest.seed (News Management shows DELETES IN ...)." },
                new Dictionary<string, object> { ["title"] = "Hackers banned", ["type"] = "Alert", ["text"] = "Short post without a picture." },
                new Dictionary<string, object> { ["title"] = "Patch notes - long read", ["type"] = "Update", ["imageUrl"] = Picture(3), ["text"] = LongText(3500) },
                new Dictionary<string, object> { ["title"] = "VIP kit drop", ["type"] = "Info", ["group"] = group, ["text"] = "Only the '" + group + "' group (and admins) can see this post. Add a player with: oxide.usergroup add <name> " + group },
                new Dictionary<string, object> { ["title"] = "Unfinished draft", ["type"] = "Update", ["draft"] = true, ["text"] = "A draft: only News Management shows it. Publish it there or with news.publish." },
                new Dictionary<string, object> { ["title"] = "A very long title that has to shrink so it fits in two lines at the top of the pop-up", ["type"] = "Info", ["imageUrl"] = Picture(1), ["text"] = "Title auto-sizing check." }
            };

            for (int i = 0; i < posts.Count; i++)
            {
                if (notify && i == posts.Count - 1) posts[i]["notify"] = true;
                if (Create(posts[i]) != null) made++;
            }
            Reply(arg, $"nbtest.seed: {made} of {posts.Count} posts created (one is a draft). Open /news, /news admin, or nbtest.list.{(notify ? " The last one was announced with a toast." : " Add 'notify' to also send a toast.")}");
        }

        [ConsoleCommand("nbtest.junk")]
        private void CmdJunk(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            bool notify = string.Equals(arg.GetString(0), "notify", StringComparison.OrdinalIgnoreCase);
            var posts = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { ["title"] = "Path C:\\Rust\\", ["author"] = "Admin\\", ["type"] = "Alert", ["text"] = "Ends with a backslash \\", ["pollOptions"] = new[] { "Yes\\", "No \"maybe\"" } },
                new Dictionary<string, object> { ["title"] = "He said \"hello\" and left", ["type"] = "Info", ["text"] = "Quotes: \"double\", 'single', “curly”. A backslash-n written out: C:\\new folder\\notes" },
                new Dictionary<string, object> { ["title"] = "Braces {0} {1} and percent %d %TIME_LEFT%", ["type"] = "Warning", ["text"] = "Format strings must show as typed: {0} {name} {{x}} 100%" },
                new Dictionary<string, object> { ["title"] = "<color=red>Rich</color> <b>text</b> </color>", ["type"] = "Event", ["text"] = "<size=40>big?</size> <color=#ff0000>red?</color> <i>italic</i> </b> stray closing tags" },
                new Dictionary<string, object> { ["title"] = "Ünïcödé ✓ ❤ 中文 — ±½", ["type"] = "Update", ["text"] = "Emoji and other scripts: 😀 🔥 Привет مرحبا שלום" },
                new Dictionary<string, object> { ["title"] = new string('W', 120), ["type"] = "Info", ["text"] = "A 120-character title made of wide letters." },
                new Dictionary<string, object> { ["title"] = "Empty text and a broken picture link", ["type"] = "Info", ["text"] = "", ["imageUrl"] = "https://example.invalid/missing.png" },
                new Dictionary<string, object> { ["title"] = "Links are removed", ["type"] = "Info", ["text"] = "Visit https://example.com and www.example.org for more. (StripLinksFromBody)" }
            };
            int made = 0;
            for (int i = 0; i < posts.Count; i++)
            {
                if (notify) posts[i]["notify"] = true;
                if (Create(posts[i]) != null) made++;
            }
            Reply(arg, $"nbtest.junk: {made} of {posts.Count} posts created. Open each one (/news): every screen must draw, and the F1 console must stay free of UI errors.");
        }

        [ConsoleCommand("nbtest.long")]
        private void CmdLong(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            int chars = Math.Max(100, Math.Min(50000, arg.GetInt(0, 11500)));
            string id = Create(new Dictionary<string, object> { ["title"] = $"Long text ({chars} characters)", ["type"] = "Update", ["text"] = LongText(chars) });
            int stored = Value(Get(id), "text").Length;
            Reply(arg, id == null ? "nbtest.long: not created." : $"nbtest.long: created, {stored} characters stored (the plugin keeps at most 12000). Open it and scroll to the end.");
        }

        [ConsoleCommand("nbtest.countdown")]
        private void CmdCountdown(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            int seconds = Math.Max(5, arg.GetInt(0, 90));
            string label = arg.GetString(1, "Starts in");
            string id = Create(new Dictionary<string, object> { ["title"] = $"Countdown test ({seconds} s)", ["type"] = "Event", ["countdownTo"] = DateTime.UtcNow.AddSeconds(seconds), ["countdownLabel"] = label, ["text"] = "Keep this pop-up open: the timer counts down and shows the 'done' text at zero.", ["notify"] = true });
            Reply(arg, id == null ? "nbtest.countdown: not created." : $"nbtest.countdown: ends in {seconds} s ({id}).");
        }

        [ConsoleCommand("nbtest.poll")]
        private void CmdPoll(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            int hours = Math.Max(0, arg.GetInt(0, 1));
            string id = Create(new Dictionary<string, object> { ["title"] = $"Poll test ({(hours == 0 ? "no timer" : hours + " h")})", ["type"] = "Event", ["pollOptions"] = new[] { "Option A", "Option B", "Option C" }, ["pollHours"] = hours, ["text"] = "Vote, change your vote, and ask a second player to vote.", ["notify"] = true });
            Reply(arg, id == null ? "nbtest.poll: not created." : $"nbtest.poll: created ({id}). The result is announced in chat when it closes.");
        }

        [ConsoleCommand("nbtest.group")]
        private void CmdGroup(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            string group = EnsureGroup();
            string id = Create(new Dictionary<string, object> { ["title"] = $"Only for {group}", ["type"] = "Info", ["group"] = group, ["text"] = "Players outside the group never see this post, its toast or its chat line.", ["notify"] = true });
            Reply(arg, id == null ? "nbtest.group: not created." : $"nbtest.group: created for group '{group}'. Add a test player: oxide.usergroup add <name> {group}   remove: oxide.usergroup remove <name> {group}");
        }

        [ConsoleCommand("nbtest.flood")]
        private void CmdFlood(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            int count = Math.Max(1, Math.Min(50, arg.GetInt(0, 5)));
            float every = Math.Max(0.2f, arg.GetFloat(1, 2f));
            floodTimer?.Destroy();
            int sent = 0;
            floodTimer = timer.Repeat(every, count, () =>
            {
                if (NewsBroadcaster == null || !NewsBroadcaster.IsLoaded) { floodTimer?.Destroy(); return; }
                sent++;
                Create(new Dictionary<string, object> { ["title"] = $"Flood {sent} of {count}", ["type"] = sent % 2 == 0 ? "Alert" : "Info", ["text"] = "Toasts must replace each other cleanly.", ["notify"] = true });
                if (sent == count) Puts($"nbtest.flood: {count} posts sent.");
            });
            Reply(arg, $"nbtest.flood: {count} posts, one every {every.ToString("0.##", CultureInfo.InvariantCulture)} s.");
        }

        [ConsoleCommand("nbtest.show")]
        private void CmdShow(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            var player = Target(arg, 0);
            if (player == null) { Reply(arg, "nbtest.show: player not found (give a name or SteamID when using the server console)."); return; }
            string id = arg.GetString(1);
            if (string.IsNullOrEmpty(id))
            {
                var list = List();
                if (list.Count == 0) { Reply(arg, "nbtest.show: there is no post to show."); return; }
                id = Value(list[0], "id");
            }
            Reply(arg, Show(player, id) ? $"nbtest.show: opened {id} for {player.displayName}." : $"nbtest.show: {player.displayName} can't see {id} (draft, expired, other group, no view permission or unknown id).");
        }

        [ConsoleCommand("nbtest.unread")]
        private void CmdUnread(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            var player = Target(arg, 0);
            Reply(arg, player == null ? "nbtest.unread: player not found." : $"nbtest.unread: {player.displayName} has {Unread(player)} unread.");
        }

        [ConsoleCommand("nbtest.list")]
        private void CmdList(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            var list = List();
            var sb = new StringBuilder($"nbtest.list: {list.Count} posts players can see (drafts and expired posts are left out; news.list shows everything)\n");
            for (int i = 0; i < list.Count; i++)
            {
                var post = list[i];
                sb.Append($"  {Value(post, "id")}  [{Value(post, "type")}] {Short(Value(post, "title"), 50)}  likes {Value(post, "likes")}, votes {Value(post, "pollVotes")}");
                if (Value(post, "pinned") == "True") sb.Append(", pinned");
                if (Value(post, "group").Length > 0) sb.Append(", group " + Value(post, "group"));
                if (data.CreatedIds.Contains(Value(post, "id"))) sb.Append(", test post");
                sb.Append('\n');
            }
            Reply(arg, sb.ToString().TrimEnd());
        }

        [ConsoleCommand("nbtest.hooks")]
        private void CmdHooks(ConsoleSystem.Arg arg)
        {
            if (!Allowed(arg)) return;
            string mode = arg.GetString(0).ToLowerInvariant();
            logHooks = mode == "on" || (mode != "off" && !logHooks);
            Reply(arg, "nbtest.hooks: hook log " + (logHooks ? "ON (printed in the server console)" : "OFF"));
        }

        [ConsoleCommand("nbtest.clear")]
        private void CmdClear(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            int removed = 0;
            foreach (string id in data.CreatedIds)
            {
                object done = NewsBroadcaster.Call("API_DeleteNews", id);
                if (done is bool && (bool)done) removed++;
            }
            foreach (var post in List())
            {
                if (Value(post, "author") != Author) continue;
                object done = NewsBroadcaster.Call("API_DeleteNews", Value(post, "id"));
                if (done is bool && (bool)done) removed++;
            }
            data.CreatedIds.Clear();
            SaveData();
            Reply(arg, $"nbtest.clear: {removed} test posts deleted. Posts you made yourself are left alone.");
        }

        [ConsoleCommand("nbtest.bench")]
        private void CmdBench(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            int runs = Math.Max(10, Math.Min(5000, arg.GetInt(0, 500)));
            var player = arg.Player();
            var sb = new StringBuilder($"nbtest.bench ({runs} runs, {List().Count} posts, {BasePlayer.activePlayerList.Count} online). Server time per call:\n");
            if (player != null) sb.AppendLine("  API_GetUnreadCount   " + Time(runs, () => Unread(player)));
            sb.AppendLine("  API_GetNewsList      " + Time(runs, () => List()));
            if (player != null)
            {
                string newest = List().Count > 0 ? Value(List()[0], "id") : null;
                if (newest != null) sb.AppendLine("  API_ShowNews (popup) " + Time(Math.Min(runs, 30), () => Show(player, newest)) + "   (draws the pop-up on your screen each time)");
            }
            sb.Append("  For the time NewsBroadcaster spends in hooks over the whole session, use: oxide.plugins");
            Reply(arg, sb.ToString());
        }

        private static string Time(int runs, Action action)
        {
            action();
            float start = UnityEngine.Time.realtimeSinceStartup;
            for (int i = 0; i < runs; i++) action();
            float ms = (UnityEngine.Time.realtimeSinceStartup - start) * 1000f / runs;
            return ms.ToString("0.000", CultureInfo.InvariantCulture) + " ms";
        }
        #endregion

        #region Self-test
        [ConsoleCommand("nbtest.run")]
        private void CmdRun(ConsoleSystem.Arg arg)
        {
            if (!Ready(arg)) return;
            var report = new StringBuilder("nbtest.run: self-test of NewsBroadcaster " + NewsBroadcaster.Version + "\n");
            int pass = 0, fail = 0;
            Action<bool, string> check = (ok, what) =>
            {
                if (ok) pass++; else fail++;
                report.Append(ok ? "  PASS " : "  FAIL ").AppendLine(what);
            };

            var player = arg.Player();
            var made = new List<string>();
            hooksSeen = new List<string>();
            long now = DateTime.UtcNow.Ticks;
            int unreadBefore = player != null ? Unread(player) : 0;

            string id = Create(new Dictionary<string, object> { ["title"] = "nbtest self-test", ["text"] = "Line one/nLine two", ["type"] = "Warning", ["expireDays"] = 1 });
            made.Add(id);
            var post = Get(id);
            check(!string.IsNullOrEmpty(id) && post != null, "API_CreateNews returns an id, API_GetNews finds it");
            check(Value(post, "title") == "nbtest self-test" && Value(post, "type") == "Warning" && Value(post, "author") == Author, "title, type and author stored as given");
            check(Value(post, "text").Contains("\n"), "/n in the text becomes a line break");
            var missing = new List<string>();
            foreach (string key in HookKeys) if (post == null || !post.ContainsKey(key)) missing.Add(key);
            check(missing.Count == 0, "the payload has every documented key" + (missing.Count > 0 ? " (missing: " + string.Join(", ", missing.ToArray()) + ")" : ""));
            long expires = post != null && post.ContainsKey("expiresAt") ? Convert.ToInt64(post["expiresAt"]) : 0;
            check(Math.Abs(expires - (now + TimeSpan.TicksPerDay)) < TimeSpan.TicksPerMinute, "expireDays 1 sets expiresAt one day ahead");
            check(hooksSeen.Contains("OnNewsBroadcast:" + id), "OnNewsBroadcast fired for the new post");
            bool listed = false;
            foreach (var item in List()) if (Value(item, "id") == id) listed = true;
            check(listed, "API_GetNewsList has it");
            if (player != null) check(Unread(player) == unreadBefore + 1, $"your unread count went up by one ({unreadBefore} -> {Unread(player)})");

            string draft = Create(new Dictionary<string, object> { ["title"] = "nbtest draft", ["draft"] = true });
            made.Add(draft);
            bool draftListed = false;
            foreach (var item in List()) if (Value(item, "id") == draft) draftListed = true;
            check(draft != null && !draftListed && !hooksSeen.Contains("OnNewsBroadcast:" + draft), "a draft is stored, not listed for players, not broadcast");
            if (player != null) check(!Show(player, draft), "a draft can't be opened by API_ShowNews");

            string poll = Create(new Dictionary<string, object> { ["title"] = "nbtest poll", ["pollOptions"] = new[] { "A", "B", " ", "C" }, ["pollHours"] = 2 });
            made.Add(poll);
            var pollPost = Get(poll);
            var options = pollPost != null ? pollPost["pollOptions"] as string[] : null;
            check(options != null && options.Length == 3, "poll options are cleaned (empty ones dropped)");
            long ends = pollPost != null ? Convert.ToInt64(pollPost["pollEndsAt"]) : 0;
            check(Math.Abs(ends - (now + 2 * TimeSpan.TicksPerHour)) < TimeSpan.TicksPerMinute, "pollHours 2 sets pollEndsAt two hours ahead");

            DateTime target = DateTime.UtcNow.AddMinutes(10);
            string countdown = Create(new Dictionary<string, object> { ["title"] = "nbtest countdown", ["countdownTo"] = target, ["countdownLabel"] = "Starts in" });
            made.Add(countdown);
            var countdownPost = Get(countdown);
            check(countdownPost != null && Math.Abs(Convert.ToInt64(countdownPost["countdownTo"]) - target.Ticks) < TimeSpan.TicksPerSecond, "countdownTo is stored as UTC ticks");

            string noGroup = Create(new Dictionary<string, object> { ["title"] = "nbtest bad group", ["group"] = "nbtest_group_that_does_not_exist" });
            check(noGroup == null, "a post for a group that does not exist is refused (NewsBroadcaster prints a warning: expected)");
            check(Create(new Dictionary<string, object> { ["title"] = "   " }) == null, "a post without a title is refused");

            string group = EnsureGroup();
            string groupPost = Create(new Dictionary<string, object> { ["title"] = "nbtest group post", ["group"] = group });
            made.Add(groupPost);
            check(Value(Get(groupPost), "group") == group, "a group-only post keeps its group");

            string junk = Create(new Dictionary<string, object> { ["title"] = "Ends with a backslash \\", ["author"] = "Quote \"me\"\\", ["text"] = "{0} {1} %TIME_LEFT% \\" });
            made.Add(junk);
            check(Value(Get(junk), "title").EndsWith("\\"), "a title ending with a backslash is stored as typed (the plugin fixes it only when drawing)");
            if (player != null)
            {
                check(Show(player, junk), "API_ShowNews opens it for you: look at your screen, the pop-up must be there, F1 free of UI errors");
                check(Show(player, id), "API_ShowNews opens a normal post");
            }
            check(!Show(null, id), "API_ShowNews with no player returns false");

            foreach (string madeId in made)
            {
                if (madeId == null) continue;
                object done = NewsBroadcaster.Call("API_DeleteNews", madeId);
                check(done is bool && (bool)done && Get(madeId) == null, "API_DeleteNews removes " + Short(madeId, 40));
                data.CreatedIds.Remove(madeId);
            }
            object again = NewsBroadcaster.Call("API_DeleteNews", id);
            check(again is bool && !(bool)again, "deleting it a second time returns false");
            check(hooksSeen.Contains("OnNewsDeleted:" + id), "OnNewsDeleted fired");
            if (player != null) check(Unread(player) == unreadBefore, "your unread count is back where it was");
            SaveData();
            hooksSeen = null;

            report.Append($"{pass} passed, {fail} failed.");
            if (player != null) report.Append(" The pop-up still on your screen belongs to a test post that is now deleted: close it.");
            if (player == null) report.Append(" Run it from the F1 console in game as well: some checks need a player.");
            Reply(arg, report.ToString());
            if (fail > 0) PrintWarning($"nbtest.run: {fail} check(s) failed, see the output above.");
        }

        private static string LongText(int chars)
        {
            var sb = new StringBuilder();
            int n = 1;
            while (sb.Length < chars)
            {
                sb.Append("Line ").Append(n).Append(": the quick brown fox jumps over the lazy dog while the raid tower burns in the distance.");
                if (n % 6 == 0) sb.Append("/n");
                sb.Append("/n");
                n++;
            }
            return sb.ToString(0, chars);
        }
        #endregion

        #region Hooks from NewsBroadcaster
        private void Seen(string hook, Dictionary<string, object> post, string extra = null)
        {
            hooksSeen?.Add(hook + ":" + Value(post, "id"));
            if (!logHooks) return;
            var missing = new List<string>();
            foreach (string key in HookKeys) if (post == null || !post.ContainsKey(key)) missing.Add(key);
            Puts($"hook {hook}: \"{Short(Value(post, "title"), 40)}\" ({Value(post, "id")}) likes {Value(post, "likes")}, votes {Value(post, "pollVotes")}{extra}"
                + (missing.Count > 0 ? "  MISSING KEYS: " + string.Join(", ", missing.ToArray()) : string.Empty));
        }

        private void OnNewsBroadcast(Dictionary<string, object> post) => Seen("OnNewsBroadcast", post);
        private void OnNewsEdited(Dictionary<string, object> post) => Seen("OnNewsEdited", post);
        private void OnNewsDeleted(Dictionary<string, object> post) => Seen("OnNewsDeleted", post);
        private void OnNewsPollClosed(Dictionary<string, object> post) => Seen("OnNewsPollClosed", post);
        private void OnNewsRead(BasePlayer player, Dictionary<string, object> post) => Seen("OnNewsRead", post, ", by " + player?.displayName);
        private void OnNewsLiked(BasePlayer player, Dictionary<string, object> post, bool liked) => Seen("OnNewsLiked", post, (liked ? ", liked by " : ", unliked by ") + player?.displayName);
        private void OnNewsPollVote(BasePlayer player, Dictionary<string, object> post, int option) => Seen("OnNewsPollVote", post, $", {player?.displayName} voted option {option}");
        #endregion
    }
}
