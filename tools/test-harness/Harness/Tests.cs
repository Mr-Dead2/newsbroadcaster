using HarnessRuntime;
using Newtonsoft.Json.Linq;

class FakePlugin : Oxide.Core.Plugins.Plugin
{
    public readonly List<(string hook, object[] args)> Calls = new();
    public Func<string, object[], object> Handler = (h, a) => null;
    public FakePlugin(string name) { Name = name; Title = name; }
    protected override object OnCallHook(string hook, object[] args) { Calls.Add((hook, args)); return Handler(hook, args); }
}

static class Tests
{
    static int pass, fail;
    static readonly List<string> failures = new();

    static void Check(bool cond, string what)
    {
        if (cond) { pass++; Console.WriteLine("  ok   " + what); }
        else { fail++; failures.Add(what); Console.WriteLine("  FAIL " + what); }
    }

    static void Section(string name) => Console.WriteLine($"\n== {name}");

    static BasePlayer admin, player, noperm;

    static void Fresh(Action<object> beforeLoad = null, Action preBoot = null)
    {
        Sim.ResetAll();
        Sim.Quiet = true;
        Oxide.Core.Interface.Clock = 0;
        preBoot?.Invoke();
        H.Boot(beforeLoad);
        admin = H.MakePlayer(76561198000000001, "DEDA", admin: true);
        player = H.MakePlayer(76561198000000002, "Survivor");
        noperm = H.MakePlayer(76561198000000003, "Stranger", viewPerm: false);
    }

    static int HooksNamed(string n) => Sim.Hooks.Count(h => h.Key == n);
    static bool UiOpen(BasePlayer p) => H.HasElement(p, "NewsBroadcasterUI");
    static bool ToastOpen(BasePlayer p) => H.Live(p).Any(e => (string)e["parent"] == "Hud" && ((string)e["name"]).StartsWith("NewsNotificationUI."));
    static string LastChat(BasePlayer p) => H.ChatOf(p).LastOrDefault() ?? "";
    static List<string> CuiWarnings() => Sim.Clients.Values.SelectMany(c => c.Warnings).ToList();
    static object Ann0 => H.Ann(0);
    static float Alpha(string rgba)
    {
        var p = (rgba ?? "1 1 1 1").Trim().Split(' ');
        return p.Length >= 4 ? float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture) : 1f;
    }

    // Decodes the plugin's PNGs (8-bit RGBA, filter 0, zlib) back to raw pixels.
    static byte[] DecodePng(byte[] png, out int width, out int height)
    {
        int Be(int o) => (png[o] << 24) | (png[o + 1] << 16) | (png[o + 2] << 8) | png[o + 3];
        width = Be(16); height = Be(20);
        var idat = new System.IO.MemoryStream();
        for (int o = 8; o < png.Length;)
        {
            int len = Be(o); string type = System.Text.Encoding.ASCII.GetString(png, o + 4, 4);
            if (type == "IDAT") idat.Write(png, o + 8, len);
            o += 12 + len;
        }
        idat.Position = 0;
        var raw = new System.IO.MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress)) z.CopyTo(raw);
        var bytes = raw.ToArray(); var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++) Array.Copy(bytes, y * (width * 4 + 1) + 1, rgba, y * width * 4, width * 4);
        return rgba;
    }
    static HashSet<ulong> Set(object ann, string f) => (HashSet<ulong>)H.AGet(ann, f);

    // A click on a page / filter / search / sort control, at a human pace: these redraws have a cooldown.
    static void Nav(BasePlayer p, string command)
    {
        Sim.Advance(0.5);
        H.Console(p, command);
    }

    static void Post(string title, string text = "Body text", string type = "Info", string img = "-")
        => H.Console(null, $"news.show \"{title}\" \"{img}\" \"{text}\" {type}");

    public static int Run()
    {
        // ---------------------------------------------------------------
        Section("Fresh install");
        Fresh();
        var cfg = JObject.Parse(Sim.ConfigFiles["config:NewsBroadcaster"]);
        Check(cfg["Themes"].Count() == 6, "default config has 6 themes");
        Check(cfg["General"]["RequireViewPermission"] != null && cfg["General"]["PopupImageFrame"]?.ToString() == "Tall", "new options are written to config");
        Check(Sim.Permission.PermissionExists("newsbroadcaster.admin") && Sim.Permission.PermissionExists("newsbroadcaster.view"), "permissions registered");
        Check(Sim.DataFiles.ContainsKey("NewsBroadcaster_Data"), "data file created");

        // ---------------------------------------------------------------
        Section("Config: plain load and save");
        Fresh(preBoot: () =>
        {
            Sim.ConfigFiles["config:NewsBroadcaster"] = @"{
              ""General"": { ""AutoCloseSeconds"": 20, ""ServerName"": ""MY SERVER"", ""AnnouncementsPerPage"": 4 },
              ""Rewards"": { ""EnableReadReward"": true, ""ReadRewards"": { ""Items"": [ { ""Shortname"": ""wood"", ""Amount"": 100, ""SkinId"": 0 } ], ""Points"": 0, ""Currency"": 0.0 } },
              ""SelectedTheme"": ""Ocean"",
              ""Themes"": { ""Default"": { ""PanelBg"": ""0.1 0.1 0.1 1"", ""HeaderBg"": ""0 0 0 1"", ""ContentBg"": ""0 0 0 1"", ""ButtonPrimary"": ""1 0 0 1"", ""ButtonSecondary"": ""0 0 0 1"", ""TextTitle"": ""1 1 1 1"", ""TextNormal"": ""1 1 1 1"", ""TextMuted"": ""1 1 1 1"" },
                            ""Ocean"": { ""PanelBg"": ""0.05 0.09 0.12 0.97"", ""HeaderBg"": ""0.02 0.05 0.07 0.55"", ""ContentBg"": ""0.08 0.13 0.17 0.55"", ""ButtonPrimary"": ""0.20 0.78 0.95 0.95"", ""ButtonSecondary"": ""0.10 0.20 0.26 0.85"", ""TextTitle"": ""0.95 0.98 1.00 1.0"", ""TextNormal"": ""0.82 0.90 0.94 1.0"", ""TextMuted"": ""0.48 0.62 0.72 1.0"" } }
            }";
        });
        cfg = JObject.Parse(Sim.ConfigFiles["config:NewsBroadcaster"]);
        Check((string)cfg["SelectedTheme"] == "Ocean" && cfg["Themes"].Count() == 2, "themes and the selected theme are kept as they are");
        Check((string)cfg["General"]["ServerName"] == "MY SERVER" && (int)cfg["General"]["AutoCloseSeconds"] == 20 && (int)cfg["General"]["AnnouncementsPerPage"] == 4, "values in the file are kept");
        Check(cfg["Rewards"]["ReadRewards"]["Items"].Count() == 1 && cfg["Rewards"]["ReadRewards"]["Items"][0]["Shortname"]?.ToString() == "wood", "reward items are read as written (no doubling on load and save)");
        Check(cfg["General"]["MaxStoredAnnouncements"] != null && cfg["Discord"]["AvatarUrl"] != null && cfg["Polls"] != null, "options the file did not have are written with their defaults");
        const string brokenConfig = @"{ ""General"": { ""ServerName"": ""MY SERVER"", }, oops";
        Fresh(preBoot: () => { Sim.ConfigFiles["config:NewsBroadcaster"] = brokenConfig; });
        Check(Sim.ConfigFiles["config:NewsBroadcaster"] == brokenConfig, "a config file that can't be read is left alone, so the typo can be fixed");
        Check((string)H.CfgPath("General.ServerName") == "SERVER NEWS" && Sim.LogLines.Any(l => l.StartsWith("ERR") && l.Contains("configuration file could not be read")), "...the defaults are used meanwhile, and the console says so");
        Sim.LogLines.RemoveAll(l => l.StartsWith("ERR"));

        // ---------------------------------------------------------------
        Section("Data file: loaded as stored");
        Fresh(preBoot: () =>
        {
            Sim.DataFiles["NewsBroadcaster_Data"] = @"{
              ""Announcements"": [
                { ""Id"": ""a2"", ""Title"": ""Newer"", ""ImageUrl"": """", ""Text"": ""visit https://example.com now"", ""Date"": ""2026-02-01 10:00"", ""Author"": ""x"", ""Type"": 3, ""Timestamp"": 200, ""Pinned"": true, ""LikedPlayers"": [1,2], ""ReadByPlayers"": [5] },
                { ""Id"": ""a1"", ""Title"": ""Old"", ""ImageUrl"": """", ""Text"": ""old text"", ""Date"": ""2026-01-01 10:00"", ""Author"": ""x"", ""Type"": 0, ""Timestamp"": 100, ""PollOptions"": [""Yes"", ""No""], ""PollVotes"": { ""76561198000000002"": 1 } }
              ],
              ""LastSeenNews"": { ""76561198000000002"": 200 }
            }";
        });
        Check(H.Announcements.Count == 2 && (string)H.AGet(H.Ann(0), "Title") == "Newer" && H.Id(H.Ann(1)) == "a1", "posts load in the stored order");
        Check(Set(H.Ann(0), "LikedPlayers").Count == 2 && Set(H.Ann(0), "ReadByPlayers").Contains(5) && H.AGet(H.Ann(0), "Type").ToString() == "Event", "likes, reads and type are read back");
        Check(((string)H.AGet(H.Ann(0), "Text")).Contains("https://example.com"), "stored text is not touched on load");
        Check(((Dictionary<ulong, int>)H.AGet(H.Ann(1), "PollVotes"))[76561198000000002] == 1 && Set(H.Ann(1), "LikeRewardedPlayers").Count == 0, "poll votes are read back, lists the file leaves out start empty");

        // ---------------------------------------------------------------
        Section("news.show broadcast, notifications, permissions, sound");
        Fresh();
        Post("Hello World", "Line one/nLine two with w/new and\\nthird", "Event");
        Check(H.Announcements.Count == 1 && (string)H.AGet(Ann0, "Title") == "Hello World", "post created");
        Check(H.AGet(Ann0, "Type").ToString() == "Event", "type parsed");
        Check((string)H.AGet(Ann0, "Text") == "Line one\nLine two with w/new and\nthird", "line breaks: /n and \\n converted, 'w/new' kept");
        Check(ToastOpen(player) && ToastOpen(admin) && !ToastOpen(noperm), "toast shown only to players with view permission");
        Check(HooksNamed("OnNewsBroadcast") == 1, "OnNewsBroadcast fired");
        Check(Sim.LogLines.Any(l => l.StartsWith("[effect -> Survivor]")) && !Sim.LogLines.Any(l => l.StartsWith("[effect broadcast")), "sound sent privately to each player, never broadcast");
        Check(Sim.ConsoleReplies.Last() == "News broadcasted!", "console reply");
        Sim.Advance(9);
        Check(!ToastOpen(player), "toast disappears after Notification.Duration");

        // ---------------------------------------------------------------
        Section("Links stripping option");
        Post("Links", "Join www.mysite.com or https://x.y/z today");
        Check((string)H.AGet(Ann0, "Text") == "Join or today", "links stripped by default (old behaviour)");
        H.CfgSet("General.StripLinksFromBody", false);
        Post("Links2", "Join www.mysite.com today");
        Check(((string)H.AGet(Ann0, "Text")).Contains("www.mysite.com"), "links kept when StripLinksFromBody=false");

        // ---------------------------------------------------------------
        Section("Popup: open, like (partial update), cooldown, debounce save");
        Fresh();
        Post("First", "a"); Post("Second", "b"); Post("Third", string.Join("\\n", Enumerable.Range(1, 80).Select(i => "Long line number " + i)), "Update");
        var third = Ann0;
        H.Console(player, $"news.view {H.Id(third)}");
        Check(UiOpen(player) && H.HasElement(player, "NewsBroadcasterUI.Main.Body"), "popup open with scroll body");
        var scroll = H.Live(player).First(e => (string)e["name"] == "NewsBroadcasterUI.Main.Body")["components"].First(c => (string)c["type"] == "UnityEngine.UI.ScrollView");
        Check(((string)scroll["contentTransform"]["offsetmin"]).StartsWith("0 -") && int.Parse(((string)scroll["contentTransform"]["offsetmin"]).Substring(3)) > 1000, "long body gets a tall scroll content");
        int writesBefore = Sim.DataWrites;
        int addCallsBefore = Sim.Ui(player).AddCalls;
        H.Console(player, $"news.like {H.Id(third)}");
        Check(Set(third, "LikedPlayers").Contains(player.userID), "like added");
        Check(UiOpen(player) && H.HasElement(player, "NewsBroadcasterUI.Like") && H.HasElement(player, "NewsBroadcasterUI.Main.Body"), "like only swaps the button (popup + scroll position kept)");
        Check(Sim.Ui(player).AddCalls == addCallsBefore + 1 && H.Texts(player).Contains("❤  1"), "like button shows new count");
        H.Console(player, $"news.like {H.Id(third)}");
        Check(Set(third, "LikedPlayers").Contains(player.userID), "double-click within cooldown ignored");
        Check(Sim.DataWrites == writesBefore, "like does not write the data file immediately");
        Sim.Advance(16);
        Check(Sim.DataWrites == writesBefore + 1, "debounced save flushes once");
        H.Call("OnServerSave");
        Check(Sim.DataWrites == writesBefore + 1, "a server save with nothing changed writes nothing");
        Check(HooksNamed("OnNewsLiked") == 1, "OnNewsLiked fired once");
        H.Console(player, $"news.like {H.Id(third)}");
        Check(!Set(third, "LikedPlayers").Contains(player.userID), "unlike after cooldown");

        // ---------------------------------------------------------------
        Section("Read tracking + read rewards (items + ServerRewards points)");
        Fresh(beforeLoad: p => { });
        var sr = new FakePlugin("ServerRewards");
        sr.Handler = (h, a) => h == "AddPoints" ? (object)(a[0] is ulong) : null;
        H.Set("ServerRewards", sr);
        H.CfgSet("Rewards.EnableReadReward", true);
        var bundle = H.CfgPath("Rewards.ReadRewards");
        H.ASet(bundle, "Points", 25);
        Post("Reward me", "text");
        var rp = Ann0;
        H.Console(player, $"news.view {H.Id(rp)}");
        Sim.Advance(3);
        H.Console(player, "news.close");
        Sim.Advance(5);
        Check(!Set(rp, "ReadByPlayers").Contains(player.userID), "closing before ReadDelaySeconds does not count as read");
        H.Console(player, $"news.view {H.Id(rp)}");
        Sim.Advance(6);
        Check(Set(rp, "ReadByPlayers").Contains(player.userID), "read after delay");
        Check(player.inventory.given.Any(i => i.info.shortname == "scrap" && i.amount == 5), "item reward given");
        Check(sr.Calls.Any(c => c.hook == "AddPoints" && c.args[0] is ulong && (ulong)c.args[0] == player.userID.Get()), "ServerRewards gets a plain ulong id");
        Check(HooksNamed("OnNewsRead") == 1, "OnNewsRead fired");
        Check(LastChat(player).StartsWith("Thanks for reading the news! Reward: 5× Scrap, 25 RP"), "reward chat message");
        H.Console(player, "news.close");
        H.Console(player, $"news.view {H.Id(rp)}");
        Sim.Advance(6);
        Check(player.inventory.given.Count == 1, "read reward only once");
        // After a broadcast everybody reads within the same seconds: paid rewards wait for the delayed save.
        var readers = Enumerable.Range(0, 6).Select(i => H.MakePlayer(76561198000000100 + (ulong)i, "Reader" + i)).ToList();
        Sim.Advance(16);
        int rewardWrites = Sim.DataWrites;
        foreach (var reader in readers) H.Console(reader, $"news.view {H.Id(rp)}");
        Sim.Advance(6);
        Check(readers.All(r => r.inventory.given.Count == 1 && Set(rp, "ReadRewardedPlayers").Contains(r.userID)), "six players get the read reward");
        Check(Sim.DataWrites == rewardWrites, "...without a write of the data file for each of them");
        Sim.Advance(16);
        Check(Sim.DataWrites == rewardWrites + 1 && Sim.DataFiles["NewsBroadcaster_Data"].Contains("76561198000000105"), "one delayed save stores all of them");
        readers[0].inventory.FullInventory = true;
        H.CfgSet("Rewards.EnableLikeReward", true);
        H.Console(readers[0], $"news.like {H.Id(rp)}");
        Check(readers[0].dropped.Count == 1 && readers[0].dropped[0].amount == 10 && Set(rp, "LikeRewardedPlayers").Contains(readers[0].userID), "full inventory: the reward is dropped at the player (BasePlayer.GiveItem)");
        Check(Sim.DataWrites == rewardWrites + 1, "a like reward waits for the delayed save too");
        H.CfgSet("Rewards.EnableLikeReward", false);

        // A reward that can't be paid (its plugin is gone) is reported once, not once for every player.
        H.Set("ServerRewards", null);
        int given = readers.Sum(r => r.inventory.given.Count);
        Post("Second reward", "text");
        foreach (var reader in readers) { H.Console(reader, "news.close.notif"); H.Console(reader, $"news.view {H.Id(Ann0)}"); }
        Sim.Advance(6);
        Check(Sim.LogLines.Count(l => l.StartsWith("WARN") && l.Contains("ServerRewards is not loaded")) == 1 && readers.Sum(r => r.inventory.given.Count) == given && Set(Ann0, "ReadRewardedPlayers").Count == 0 && Set(Ann0, "ReadByPlayers").Count == readers.Count,
            "a reward that can't be paid: six readers, one console warning, nothing paid or marked as paid");
        H.Set("ServerRewards", sr);

        // ---------------------------------------------------------------
        Section("Like reward once");
        H.CfgSet("Rewards.EnableLikeReward", true);
        H.Console(player, $"news.like {H.Id(rp)}");
        Sim.Advance(2);
        H.Console(player, $"news.like {H.Id(rp)}");
        Sim.Advance(2);
        H.Console(player, $"news.like {H.Id(rp)}");
        Check(player.inventory.given.Count(i => i.amount == 10) == 1, "like reward only once despite re-liking");

        // ---------------------------------------------------------------
        Section("Popup when notifications are disabled: auto-close + editors protected");
        Fresh();
        H.CfgSet("Notification.Enabled", false);
        H.Console(admin, "news.admin.create");
        Post("Forced popup");
        Check(UiOpen(player) && H.Texts(player).Any(t => t == "FORCED POPUP"), "popup forced for idle player");
        Check(H.Texts(admin).Contains("CREATE NEW ANNOUNCEMENT") && ToastOpen(admin), "admin in editor gets a toast, editor kept");
        Sim.Advance(16);
        Check(!UiOpen(player), "broadcast popup auto-closes");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        Sim.Advance(30);
        Check(UiOpen(player), "popup opened by the player does not auto-close");

        // ---------------------------------------------------------------
        Section("Editor: create, validation, type chips, toggles, preview, save");
        Fresh();
        H.Console(admin, "news.admin.create");
        Check(H.Texts(admin).Contains("CREATE NEW ANNOUNCEMENT"), "editor open");
        int addsBefore = Sim.Ui(admin).AddCalls;
        H.Console(admin, "news.editor.save");
        Check(LastChat(admin) == "Please enter a title before publishing." && H.Announcements.Count == 0, "empty title rejected");
        H.Console(admin, "news.editor.input title My  Great Title ");
        addsBefore = Sim.Ui(admin).AddCalls;
        static long ElementId(BasePlayer p, Func<JObject, bool> match) => (long?)H.Live(p).FirstOrDefault(match)?["__id"] ?? -1;
        Func<JObject, bool> isTextField = e => e["components"].Any(cc => (string)cc["command"] == "news.editor.input text");
        long rootBefore = ElementId(admin, e => (string)e["name"] == "NewsBroadcasterUI"), fieldBefore = ElementId(admin, isTextField);
        H.Console(admin, "news.editor.input text Hello\\nworld");
        Check(rootBefore > 0 && fieldBefore > 0 && ElementId(admin, e => (string)e["name"] == "NewsBroadcasterUI") == rootBefore && ElementId(admin, isTextField) == fieldBefore
              && Sim.Ui(admin).AddCalls <= addsBefore + 1, "typing in a field does not rebuild the editor (only its counter is swapped)");
        H.Console(admin, "news.editor.input image notaurl");
        Check(LastChat(admin).StartsWith("That image URL is not valid"), "invalid image url rejected with message");
        H.Console(admin, "news.editor.input image https://img.example/wipe-day.png");
        H.Console(admin, "news.editor.settype alert");
        H.Console(admin, "news.editor.toggle pin");
        H.Console(admin, "news.editor.preview");
        Check(H.Texts(admin).Contains("PREVIEW") && H.Texts(admin).Contains("< BACK TO EDITOR") && H.Announcements.Count == 0, "preview shows popup without publishing");
        Check(H.Texts(admin).Any(t => t.Contains("ALERT")), "preview reflects chosen type");
        Sim.Advance(10);
        Check(H.Announcements.Count == 0 && HooksNamed("OnNewsRead") == 0, "preview does not count as a read");
        H.Console(admin, "news.editor.back");
        Check(H.Texts(admin).Contains("CREATE NEW ANNOUNCEMENT"), "back to editor keeps draft");
        Check(H.Commands(admin).Contains("news.editor.settype Info"), "type chips present");
        H.Console(admin, "news.editor.save");
        var created = Ann0;
        Check((string)H.AGet(created, "Title") == "My  Great Title" && (bool)H.AGet(created, "Pinned") && H.AGet(created, "Type").ToString() == "Alert", "saved with title/pin/type");
        Check((string)H.AGet(created, "Text") == "Hello\nworld", "body normalized");
        Check(LastChat(admin).StartsWith("Announcement published and sent to 2"), "publish message with reach count");
        Check(ToastOpen(player) && !ToastOpen(noperm), "players notified");
        Check(H.Texts(admin).Any(t => t != null && t.StartsWith("NEWS MANAGEMENT")), "back to admin list after save");

        // ---------------------------------------------------------------
        Section("Editor: edit keeps likes/reads, marks edited, no re-notify by default");
        Set(created, "LikedPlayers").Add(42); Set(created, "ReadByPlayers").Add(43);
        H.Console(player, "news.close.notif");
        H.Console(admin, $"news.admin.edit {H.Id(created)}");
        Check(H.Texts(admin).Contains("EDIT ANNOUNCEMENT") && H.Texts(admin).Contains("SAVE CHANGES"), "edit mode");
        H.Console(admin, "news.editor.input title \"Edited Title\"");
        H.Console(admin, "news.editor.save");
        Check((string)H.AGet(created, "Title") == "Edited Title", "quoted input value parsed");
        Check(H.AGet(created, "EditedDate") != null && Set(created, "LikedPlayers").Contains(42) && Set(created, "ReadByPlayers").Contains(43), "edited stamp set, likes/reads preserved");
        Check(HooksNamed("OnNewsEdited") == 1 && !ToastOpen(player), "OnNewsEdited fired, no re-notify");

        // ---------------------------------------------------------------
        Section("Discord webhook");
        Fresh();
        H.CfgSet("Discord.Enabled", true);
        H.CfgSet("Discord.WebhookUrl", "https://discord.com/api/webhooks/1/abc");
        H.CfgSet("Discord.RoleMention", "@everyone");
        Post("Discord Post", "Body here", "Warning", "https://img.example/wipe-day.png");
        Check(Sim.WebRequests.Count == 1, "webhook sent");
        var payload = JObject.Parse(Sim.WebRequests[0].Body);
        Check((string)payload["embeds"][0]["title"] == "Discord Post" && (int)payload["embeds"][0]["color"] == 15105570, "embed title + colour");
        Check(payload["embeds"][0]["timestamp"] != null && ((string)payload["embeds"][0]["author"]["name"]).StartsWith("WARNING"), "embed timestamp + type line");
        Check(payload["allowed_mentions"]["parse"].Values<string>().Contains("everyone") && (string)payload["content"] == "@everyone", "@everyone mention allowed");
        Check((string)payload["embeds"][0]["image"]["url"] == "https://img.example/wipe-day.png" && payload["avatar_url"] == null, "image set, empty avatar omitted");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title No discord");
        H.Console(admin, "news.editor.toggle discord");
        H.Console(admin, "news.editor.save");
        Check(Sim.WebRequests.Count == 1, "editor 'post to Discord' toggle respected");

        // ---------------------------------------------------------------
        Section("Admin list: pin, resend, delete (keeps page), bulk");
        Fresh();
        for (int i = 1; i <= 7; i++) Post("Post " + i);
        Nav(admin, "news.admin.page 1");
        Check(H.Texts(admin).Any(t => t.Contains("PAGE 2 / 2")), "admin page 2");
        var p2 = H.AnnByTitle("Post 1");
        H.Console(admin, $"news.admin.delconfirm {H.Id(p2)} 1");
        Check(H.HasElement(admin, "NewsConfirmUI"), "confirm dialog");
        H.Console(admin, $"news.admin.del {H.Id(p2)} 1");
        Check(H.AnnByTitle("Post 1") == null && !H.HasElement(admin, "NewsConfirmUI") && H.Texts(admin).Any(t => t.Contains("PAGE 2 / 2")), "deleted and stayed on page 2");
        Check(HooksNamed("OnNewsDeleted") == 1, "OnNewsDeleted fired");
        H.Console(player, "news.close.notif");
        var p3 = H.AnnByTitle("Post 3");
        H.Console(admin, $"news.admin.resend {H.Id(p3)} 0");
        Check(ToastOpen(player) && LastChat(admin) == "Notification re-sent to 2 online player(s).", "resend notifies online players");
        H.Console(admin, $"news.admin.togglepin {H.Id(p3)} 0");
        Check((bool)H.AGet(p3, "Pinned"), "toggle pin");
        H.Console(admin, $"news.admin.toggleselect {H.Id(H.AnnByTitle("Post 4"))} 0");
        H.Console(admin, $"news.admin.toggleselect {H.Id(H.AnnByTitle("Post 5"))} 0");
        Check(H.Texts(admin).Contains("2 SELECTED"), "bulk bar shows selection");
        H.Console(admin, "news.admin.bulkpin 1 0");
        Check((bool)H.AGet(H.AnnByTitle("Post 4"), "Pinned") && (bool)H.AGet(H.AnnByTitle("Post 5"), "Pinned"), "bulk pin");
        H.Console(admin, "news.admin.bulkdelconfirm 0");
        H.Console(admin, "news.admin.bulkdel 0");
        Check(H.AnnByTitle("Post 4") == null && H.AnnByTitle("Post 5") == null && H.Announcements.Count == 4, "bulk delete");
        H.Console(player, "news.admin.create");
        Check(!H.Texts(player).Contains("CREATE NEW ANNOUNCEMENT"), "non-admin cannot open editor");
        H.Console(player, $"news.admin.del {H.Id(p3)} 0");
        Check(H.AnnByTitle("Post 3") != null, "non-admin cannot delete");

        // ---------------------------------------------------------------
        Section("Max stored: pinned posts survive trimming");
        Fresh();
        H.CfgSet("General.MaxStoredAnnouncements", 3);
        Post("Old pinned");
        H.Console(admin, $"news.admin.togglepin {H.Id(Ann0)} 0");
        for (int i = 1; i <= 4; i++) Post("Filler " + i);
        Check(H.Announcements.Count == 3 && H.AnnByTitle("Old pinned") != null, "pinned kept, oldest unpinned dropped");
        Check((string)H.AGet(Ann0, "Title") == "Filler 4", "list stays newest-first");
        // Pinned posts and drafts using up the whole limit: a new post must not vanish as it is published.
        Fresh();
        H.CfgSet("General.MaxStoredAnnouncements", 3);
        Post("Pin A"); Post("Pin B");
        H.Console(admin, $"news.admin.togglepin {H.Id(H.AnnByTitle("Pin A"))} 0");
        H.Console(admin, $"news.admin.togglepin {H.Id(H.AnnByTitle("Pin B"))} 0");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Draft C");
        H.Console(admin, "news.editor.savedraft");
        H.Console(player, "news.close.notif");
        int trimmedBefore = HooksNamed("OnNewsDeleted");
        Post("Fresh news");
        Check(H.AnnByTitle("Fresh news") != null && HooksNamed("OnNewsDeleted") == trimmedBefore, "limit used up by pinned posts and drafts: a new post stays");
        Check(ToastOpen(player) && H.Commands(player).Contains($"news.view {H.Id(H.AnnByTitle("Fresh news"))}"), "...and its toast opens it");
        Check(Sim.LogLines.Count(l => l.StartsWith("WARN") && l.Contains("MaxStoredAnnouncements (3) is used up by pinned posts and drafts")) == 1, "...and the console says the limit is used up");
        Post("Fresher news");
        Check(H.AnnByTitle("Fresher news") != null && H.AnnByTitle("Fresh news") == null && H.Announcements.Count == 4, "the next post replaces it; pinned posts and the draft are never removed");
        Check(H.AnnByTitle("Pin A") != null && H.AnnByTitle("Pin B") != null && H.AnnByTitle("Draft C") != null && Sim.LogLines.Count(l => l.Contains("is used up by pinned posts")) == 1, "the warning is printed once");

        // ---------------------------------------------------------------
        Section("Archive, unread badges, popup navigation");
        Fresh();
        for (int i = 1; i <= 7; i++) Post("News " + i);
        H.Chat(player, "news");
        Check(H.Texts(player).Contains("7 UNREAD") && H.Texts(player).Count(t => t == "NEW") == 5, "unread counter + NEW badges");
        Nav(player, "news.page 1");
        Check(H.Texts(player).Contains("PAGE 2 / 2"), "archive page 2");
        var n2 = H.AnnByTitle("News 2");
        H.Console(player, $"news.view {H.Id(n2)} archive");
        Check(H.Commands(player).Contains("news.page 1") && H.Texts(player).Contains("< BACK TO ARCHIVE"), "back-to-archive returns to the same page");
        Check(H.Texts(player).Contains("6 / 7"), "position counter");
        Check(H.Commands(player).Contains($"news.view {H.Id(H.AnnByTitle("News 1"))} archive") && H.Commands(player).Contains($"news.view {H.Id(H.AnnByTitle("News 3"))} archive"), "prev/next navigation keeps origin");
        Sim.Advance(6);
        Nav(player, "news.page 1");
        Check(H.Texts(player).Contains("6 UNREAD"), "read post no longer unread");

        // ---------------------------------------------------------------
        Section("Themes");
        H.Console(admin, "news.admin.themes");
        Check(H.Texts(admin).Contains("SELECT THEME") && H.Texts(admin).Contains("OCEAN"), "theme grid");
        H.Console(admin, "news.admin.settheme \"ocean\" 0");
        Check((string)H.AGet(H.Config, "SelectedTheme") == "Ocean" && JObject.Parse(Sim.ConfigFiles["config:NewsBroadcaster"])["SelectedTheme"].ToString() == "Ocean", "theme set (case-insensitive) and saved");
        Check(LastChat(admin) == "Theme set to: Ocean", "theme chat confirmation");
        var themes = (System.Collections.IDictionary)H.AGet(H.Config, "Themes");
        var themeType = H.AGet(H.Config, "Colors").GetType();
        for (int i = 0; i < 6; i++) themes["Custom " + i] = Activator.CreateInstance(themeType, true);
        H.Console(admin, "news.admin.themes 1");
        Check(H.Texts(admin).Contains("PAGE 2 / 2") && H.Texts(admin).Contains("CUSTOM 5"), "theme pagination with 12 themes");
        H.Console(admin, "news.admin.settheme Missing");
        Check(LastChat(admin).StartsWith("Theme 'Missing' not found"), "unknown theme message");

        // ---------------------------------------------------------------
        Section("Chat commands");
        H.Console(player, "news.close");
        H.Chat(player, "news", "latest");
        Check(H.Texts(player).Contains("NEWS 7"), "/news latest opens newest");
        H.Chat(player, "news", "admin");
        Check(LastChat(player) == "You do not have permission to use this command.", "/news admin denied for players");
        H.Chat(admin, "news", "admin");
        Check(H.Texts(admin).Any(t => t != null && t.StartsWith("NEWS MANAGEMENT")), "/news admin opens management for admins");
        H.Chat(admin, "news", "help");
        Check(LastChat(admin).Contains("/news admin"), "/news help lists admin command for admins");
        H.Chat(noperm, "news");
        Check(LastChat(noperm) == "You do not have permission to view news." && !UiOpen(noperm), "no view permission -> denied");
        H.CfgSet("General.RequireViewPermission", false);
        H.Chat(noperm, "news");
        Check(UiOpen(noperm), "RequireViewPermission=false lets everyone view");

        // ---------------------------------------------------------------
        Section("Console commands");
        H.Console(null, "news.list");
        Check(Sim.ConsoleReplies.Last().Contains("=== Stored Announcements (7) ===") && Sim.ConsoleReplies.Last().Contains("(id "), "news.list shows ids");
        string id5 = H.Id(H.AnnByTitle("News 5"));
        H.Console(null, $"news.delete {id5}");
        Check(H.AnnByTitle("News 5") == null, "news.delete by id");
        H.Console(null, "news.delete 0");
        Check(H.AnnByTitle("News 7") == null, "news.delete by index");
        H.Console(null, "news.delete 99");
        Check(Sim.ConsoleReplies.Last().StartsWith("Invalid index"), "news.delete invalid");
        H.Console(null, "news.trigger Survivor 1");
        Check(Sim.ConsoleReplies.Last() == "News popup triggered for Survivor", "news.trigger");
        H.Console(null, "news.resend 0");
        Check(Sim.ConsoleReplies.Last().StartsWith("Notification re-sent to"), "news.resend");
        H.Console(player, "news.show \"x\" \"-\" \"y\"");
        Check(Sim.ConsoleReplies.Last() == "You do not have permission to use this command.", "news.show denied for players");
        H.Console(null, "news.show \"only title\"");
        Check(Sim.ConsoleReplies.Last().StartsWith("Usage: news.show"), "news.show usage");

        // ---------------------------------------------------------------
        Section("Connect popup, unread reminder, disconnect cleanup, unload");
        Fresh();
        Post("A"); Post("B"); Post("C");
        H.Console(player, "news.close.notif");
        var joiner = H.MakePlayer(76561198000000005, "Joiner");
        H.Call("OnPlayerSleepEnded", joiner);
        Sim.Advance(2.5);
        Check(UiOpen(joiner) && H.Texts(joiner).Contains("C"), "connect popup shows latest to a player who missed it");
        Check(LastChat(joiner) == "You have 2 more unread announcement(s). Type /news to read them.", "reminder counts the other unread posts");
        H.Console(joiner, "news.close");
        int chats = H.ChatOf(joiner).Count;
        H.Call("OnPlayerSleepEnded", joiner);
        Sim.Advance(3);
        Check(!UiOpen(joiner) && H.ChatOf(joiner).Count == chats, "no repeat popup/reminder on respawn");
        H.Call("OnPlayerSleepEnded", player);
        Sim.Advance(3);
        Check(!UiOpen(player) && !H.ChatOf(player).Any(m => m.Contains("unread")), "player who saw the live broadcast gets no duplicate popup/reminder");
        H.Call("OnPlayerDisconnected", joiner, "bye");
        BasePlayer.activePlayerList.Remove(joiner);
        Sim.ClientDestroyUi(joiner, "NewsBroadcasterUI"); Sim.ClientDestroyUi(joiner, "NewsNotificationUI");
        Post("D");
        H.Console(admin, "news.close.notif");
        BasePlayer.activePlayerList.Add(joiner);
        var rejoin = joiner;
        Check(!ToastOpen(rejoin), "offline player got no toast");
        H.Call("OnPlayerSleepEnded", rejoin);
        Sim.Advance(2.5);
        Check(UiOpen(rejoin) && H.Texts(rejoin).Contains("D") && !H.ChatOf(rejoin).Skip(chats).Any(m => m.Contains("unread")), "next visit: popup for the new post only, old unread ones not nagged again");
        H.Console(rejoin, "news.close");
        H.Call("OnPlayerDisconnected", rejoin, "bye");
        H.Console(admin, "news.admin.create");
        H.Console(admin, $"news.admin.toggleselect {H.Id(Ann0)} 0");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        Nav(player, "news.filter event");
        H.Call("OnPlayerDisconnected", admin, "bye");
        H.Call("OnPlayerDisconnected", player, "bye");
        bool clean = true;
        foreach (var f in new[] { "editors", "playersWithUiOpen", "adminSelections", "openPopups", "archivePages", "remindedPlayers", "readRewardTimers", "autoCloseTimers", "notificationTimers", "likeCooldowns", "voteCooldowns", "currentScreens", "toastNames", "archiveFilters", "adminViews", "redrawCooldowns", "redrawTimers" })
        {
            var coll = H.Get(f);
            int n = (int)coll.GetType().GetProperty("Count").GetValue(coll);
            if (n != 0) { clean = false; Console.WriteLine($"     leftover in {f}: {n}"); }
        }
        Check(clean, "all per-player state cleaned on disconnect");
        H.MakePlayer(76561198000000009, "Late");
        var late = BasePlayer.activePlayerList.Values.Last();
        H.Console(late, $"news.view {H.Id(Ann0)}");
        H.Console(late, $"news.like {H.Id(Ann0)}");
        int w = Sim.DataWrites;
        H.Call("Unload");
        Check(Sim.DataWrites == w + 1, "unload flushes pending data");
        Check(!UiOpen(late), "unload destroys UI");

        // ---------------------------------------------------------------
        Section("Layout sanity: long title & long body, wide image frame");
        Fresh();
        string longTitle = new string('W', 30) + " " + string.Join(" ", Enumerable.Repeat("LONG TITLE WORDS", 6));
        Post(longTitle.Substring(0, 120), "x", "Info", "https://img.example/wipe-day.png");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        var titleEl = H.Live(player).First(e => (string)e["name"] == "NewsBroadcasterUI.Main.TitleText");
        int fs = (int?)titleEl["components"].First(c => (string)c["type"] == "UnityEngine.UI.Text")["fontSize"] ?? 14;
        Check(fs >= 14 && fs <= 18, $"120-char title shrinks to fit (font {fs})");
        H.CfgSet("General.PopupImageFrame", "Wide");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        Check(H.Live(player).Any(e => e["components"].Any(c => (string)c["type"] == "RectTransform" && (string)c["anchormin"] == "0.03 0.29")), "wide image frame layout");

        // ---------------------------------------------------------------
        Section("Mark all read");
        Fresh();
        for (int i = 1; i <= 4; i++) Post("M" + i);
        H.Console(player, "news.close.notif");
        var m1 = H.AnnByTitle("M1");
        H.Console(player, $"news.view {H.Id(m1)}");
        Sim.Advance(6);
        H.Chat(player, "news");
        Check(H.Texts(player).Contains("3 UNREAD") && H.Commands(player).Contains("news.markread"), "archive shows MARK ALL READ while posts are unread");
        H.Console(player, "news.markread");
        Check(LastChat(player) == "Marked 3 announcement(s) as read." && UiOpen(player) && !H.Texts(player).Any(t => t != null && t.EndsWith("UNREAD")), "mark all read clears the badges and redraws the archive");
        Check(!H.Commands(player).Contains("news.markread") && H.Texts(player).Count(t => t == "NEW") == 0, "no NEW badges, button hidden");
        Check(Set(H.AnnByTitle("M2"), "ReadByPlayers").Count == 0 && Set(m1, "ReadByPlayers").Count == 1, "per-post read stats are not inflated");
        Check(HooksNamed("OnNewsRead") == 1, "mark all read does not fire OnNewsRead");
        Post("M5");
        H.Console(player, "news.close.notif");
        H.Chat(player, "news");
        Check(H.Texts(player).Contains("1 UNREAD"), "posts published afterwards are unread again");
        H.Chat(player, "news", "read");
        Check(LastChat(player) == "Marked 1 announcement(s) as read.", "/news read");
        H.Chat(player, "news", "read");
        Check(LastChat(player) == "You have no unread announcements.", "/news read with nothing unread");
        Sim.Advance(16);
        Check(JObject.Parse(Sim.DataFiles["NewsBroadcaster_Data"])["ReadAllUpTo"][player.UserIDString] != null, "mark-read marker saved");

        // ---------------------------------------------------------------
        Section("Polls: console, voting, results, partial redraw, closing, editing");
        Fresh();
        H.Console(null, "news.poll \"Map size?\" \"Pick one\" \"Small|Medium|Large\" Event");
        var pl = Ann0;
        Check(((List<string>)H.AGet(pl, "PollOptions")).Count == 3 && H.AGet(pl, "Type").ToString() == "Event", "news.poll creates a 3-option poll");
        Check(H.Texts(player).Any(t => t != null && t.StartsWith("NEW POLL")), "toast says NEW POLL");
        H.Console(null, "news.poll \"Bad\" \"x\" \"Only one\"");
        Check(Sim.ConsoleReplies.Last() == "A poll needs at least 2 options." && H.Announcements.Count == 1, "poll with < 2 options rejected");
        H.Console(player, $"news.view {H.Id(pl)}");
        Check(H.HasElement(player, "NewsBroadcasterUI.Poll") && H.Commands(player).Contains($"news.vote {H.Id(pl)} 0") && !H.Texts(player).Any(t => t != null && t.Contains("%")), "poll shows vote buttons, results hidden before voting");
        Check(H.Texts(player).Contains("CLICK AN OPTION TO VOTE"), "vote hint");
        int adds = Sim.Ui(player).AddCalls;
        H.Console(player, $"news.vote {H.Id(pl)} 2");
        var votes = (Dictionary<ulong, int>)H.AGet(pl, "PollVotes");
        Check(votes[player.userID] == 2, "vote recorded");
        Check(Sim.Ui(player).AddCalls == adds + 1 && H.HasElement(player, "NewsBroadcasterUI.Main.Body") && H.HasElement(player, "NewsBroadcasterUI.Poll"), "vote redraws only the poll block");
        Check(H.Texts(player).Any(t => t == "✓  Large") && H.Texts(player).Any(t => t != null && t.StartsWith("100%")), "results shown after voting with own choice ticked");
        Check(HooksNamed("OnNewsPollVote") == 1, "OnNewsPollVote fired");
        H.Console(player, $"news.vote {H.Id(pl)} 0");
        Check(votes[player.userID] == 2, "vote change within cooldown ignored");
        Sim.Advance(1);
        H.Console(player, $"news.vote {H.Id(pl)} 0");
        Check(votes[player.userID] == 0, "vote changed after cooldown (AllowVoteChange)");
        H.Console(player, $"news.vote {H.Id(pl)} 7");
        Check(votes.Count == 1 && votes[player.userID] == 0, "invalid option ignored");
        H.CfgSet("Polls.AllowVoteChange", false);
        Sim.Advance(1);
        H.Console(player, $"news.vote {H.Id(pl)} 1");
        Check(votes[player.userID] == 0 && LastChat(player) == "You have already voted in this poll.", "AllowVoteChange=false keeps the first vote");
        H.CfgSet("Polls.AllowVoteChange", true);
        H.Console(admin, $"news.view {H.Id(pl)}");
        Check(H.Texts(admin).Any(t => t != null && t.StartsWith("100%")), "admins see results before voting");
        H.Chat(noperm, "news");
        H.Console(noperm, $"news.vote {H.Id(pl)} 1");
        Check(votes.Count == 1, "players without view permission cannot vote");

        // edit: close voting + drop option 2 (votes follow their option)
        votes[76561198000000077] = 2;
        H.Console(admin, $"news.admin.edit {H.Id(pl)}");
        Check(H.Texts(admin).Contains("POLL OPTIONS (2-4, empty ones are skipped)") && H.Texts(admin).Contains("CLOSE VOTING"), "editor shows poll section for existing poll");
        H.Console(admin, "news.editor.input poll2");
        H.Console(admin, "news.editor.toggle pollclosed");
        H.Console(admin, "news.editor.save");
        var opts = (List<string>)H.AGet(pl, "PollOptions");
        Check(opts.Count == 2 && opts[1] == "Large" && !ReferenceEquals(votes, H.AGet(pl, "PollVotes")), "empty option removed");
        var newVotes = (Dictionary<ulong, int>)H.AGet(pl, "PollVotes");
        Check(newVotes[player.userID] == 0 && newVotes[76561198000000077] == 1, "votes remapped to the remaining options");
        Check((bool)H.AGet(pl, "PollClosed"), "poll closed");
        Sim.Advance(1);
        H.Console(player, $"news.view {H.Id(pl)}");
        Check(!H.Commands(player).Any(cmd => cmd.StartsWith("news.vote")) && H.Texts(player).Contains("VOTING CLOSED"), "closed poll: no vote buttons");
        H.Console(player, $"news.vote {H.Id(pl)} 1");
        Check(newVotes[player.userID] == 0, "closed poll rejects votes");

        // editor: new poll validation + creation
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Poll from editor");
        H.Console(admin, "news.editor.toggle poll");
        H.Console(admin, "news.editor.input poll3 Only");
        H.Console(admin, "news.editor.save");
        Check(LastChat(admin) == "A poll needs at least 2 options." && H.AnnByTitle("Poll from editor") == null, "editor rejects a 1-option poll");
        H.Console(admin, "news.editor.input poll1 Yes");
        H.Console(admin, "news.editor.preview");
        Check(H.HasElement(admin, "NewsBroadcasterUI.Poll") && !H.Commands(admin).Any(cmd => cmd.StartsWith("news.vote")), "preview shows the poll without vote buttons");
        H.Console(admin, "news.editor.back");
        H.Console(admin, "news.editor.save");
        var ep = H.AnnByTitle("Poll from editor");
        Check(ep != null && string.Join(",", (List<string>)H.AGet(ep, "PollOptions")) == "Yes,Only", "editor poll created from filled slots");
        H.Console(admin, $"news.admin.edit {H.Id(ep)}");
        H.Console(admin, "news.editor.toggle poll");
        H.Console(admin, "news.editor.save");
        Check(((List<string>)H.AGet(ep, "PollOptions")).Count == 0, "turning the poll off removes it");

        // archive + admin + discord + hook payload
        H.Chat(player, "news");
        Check(H.Texts(player).Any(t => t != null && t.Contains("POLL · 2 VOTES")), "archive row shows poll + vote count");
        H.Chat(admin, "news", "admin");
        Check(H.Texts(admin).Contains("POLL VOTES") && H.Texts(admin).Count(t => t == "2") >= 2 && H.Texts(admin).Any(t => t != null && t.StartsWith("2 POSTS")), "admin: votes on the row and in the stat cards; post count in the footer");
        H.CfgSet("Discord.Enabled", true);
        H.CfgSet("Discord.WebhookUrl", "https://discord.com/api/webhooks/1/abc");
        H.Console(null, "news.poll \"Discord poll\" \"Pick\" \"A|B\"");
        var dp = JObject.Parse(Sim.WebRequests.Last().Body);
        Check(dp["embeds"][0]["fields"][0]["value"].ToString().Contains("A") && dp["embeds"][0]["fields"][0]["value"].ToString().Contains("B"), "Discord embed lists poll options");
        var hook = (Dictionary<string, object>)Sim.Hooks.Last(h => h.Key == "OnNewsBroadcast").Value[0];
        Check(((string[])hook["pollOptions"]).Length == 2 && hook.ContainsKey("title"), "hook payload has pollOptions, old keys unchanged");

        // ---------------------------------------------------------------
        Section("Animations");
        Fresh();
        Post("Anim A", "a"); Post("Anim B", "b");
        static float Fade(JObject e) => e["components"].Children<JObject>().Select(c => (float?)c["fadeIn"] ?? 0f).DefaultIfEmpty(0f).Max();
        H.Console(player, "news.close.notif");
        H.Chat(player, "news");
        var arch = H.Live(player).ToList();
        Check(Fade(arch.First(e => (string)e["name"] == "NewsBroadcasterUI")) == 0.25f, "opening from nothing fades the background in");
        var rows = arch.Where(e => System.Text.RegularExpressions.Regex.IsMatch((string)e["name"], @"^NewsBroadcasterUI\.List\.\d+$") && (string)e["parent"] == "NewsBroadcasterUI.List").ToList();
        Check(rows.Count == 2 && Fade(rows[0]) > 0 && Fade(rows[1]) > Fade(rows[0]), "archive rows cascade (later rows fade slower)");
        long rootId = (long)arch.First(e => (string)e["name"] == "NewsBroadcasterUI")["__id"];
        H.Console(player, $"news.view {H.Id(Ann0)} archive");
        var pop = H.Live(player).ToList();
        Check((long)pop.First(e => (string)e["name"] == "NewsBroadcasterUI")["__id"] == rootId, "switching screens keeps the background (no blink)");
        Check(Fade(pop.First(e => (string)e["name"] == "NewsBroadcasterUI.Main.TitleText")) > Fade(pop.First(e => (string)e["name"] == "NewsBroadcasterUI.Main")), "popup reveals panel first, then title");
        H.Console(admin, "news.close.notif");
        H.Console(admin, "news.admin");
        Check(H.Live(admin).Any(e => Fade(e) > 0f), "admin list animates on open");
        H.Console(admin, $"news.admin.toggleselect {H.Id(Ann0)} 0");
        Check(!H.Live(admin).Any(e => (string)e["name"] != "NewsBroadcasterUI" && Fade(e) > 0f), "admin refresh (select) is instant");
        H.Console(admin, "news.close");

        // toast fade-out + replacement
        Post("Anim C", "c");
        var toast1 = H.Live(player).First(e => (string)e["parent"] == "Hud" && ((string)e["name"]).StartsWith("NewsNotificationUI."));
        Check(H.Live(player).Where(e => ((string)e["name"]).StartsWith((string)toast1["name"])).All(e => (float?)e["fadeOut"] == 0.35f), "toast elements fade out");
        Post("Anim D", "d");
        var toasts = H.Live(player).Where(e => (string)e["parent"] == "Hud" && ((string)e["name"]).StartsWith("NewsNotificationUI.")).ToList();
        Check(toasts.Count == 1 && (string)toasts[0]["name"] != (string)toast1["name"], "new toast replaces the old one under a new name");

        // countdown on broadcast popups
        H.CfgSet("Notification.Enabled", false);
        H.Console(player, "news.close");
        Post("Anim E", "e");
        var cd = H.Live(player).FirstOrDefault(e => (string)e["name"] == "NewsBroadcasterUI.AutoClose");
        Check(cd != null && cd["components"].Any(c => (string)c["type"] == "Countdown" && (int)c["startTime"] == 15) && H.Texts(player).Contains("CLOSES IN %TIME_LEFT%S"), "broadcast popup shows a live countdown");
        H.Console(player, $"news.like {H.Id(Ann0)}");
        Check(!H.HasElement(player, "NewsBroadcasterUI.AutoClose") && !((System.Collections.IDictionary)H.Get("autoCloseTimers")).Contains(player.userID.Get()), "interacting removes the countdown and keeps the popup open");
        Sim.Advance(20);
        Check(UiOpen(player), "popup stays open after interaction");
        Check(H.Live(player).Where(e => (string)e["parent"] == "NewsBroadcasterUI.Like").All(e => Fade(e) == 0.25f), "like button pops in");
        H.CfgSet("Notification.Enabled", true);

        // poll bars grow in after voting
        H.Console(null, "news.poll \"Anim poll\" \"x\" \"A|B\"");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        H.Console(player, $"news.vote {H.Id(Ann0)} 1");
        Check(H.Live(player).Any(e => (string)e["parent"] == "NewsBroadcasterUI.Poll.Opt1" && Fade(e) == 0.5f), "poll result bars fade in");

        // ---------------------------------------------------------------
        Section("Rounded corners");
        Fresh();
        Check(FileStorage.server.Files.Count == 8, "8 corner textures stored in FileStorage");
        Check(FileStorage.server.Files.Values.All(b => b.Length > 100 && b[0] == 137 && b[1] == 80 && b[2] == 78 && b[3] == 71), "corner textures are PNG files");
        var cornerIds = FileStorage.server.Files.Keys.Select(k => k.ToString()).ToHashSet();
        Post("Round", "x", "Info", "https://img.example/wipe-day.png");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        Func<BasePlayer, List<JObject>> cornersOf = p => H.Live(p).Where(e => e["components"].Any(cc => (string)cc["type"] == "UnityEngine.UI.RawImage" && cornerIds.Contains((string)cc["png"]))).ToList();
        var cornerPieces = cornersOf(player);
        // Rounded: the panel and its rim, the main button, the picture's corners. Everything small is flat.
        Check(cornerPieces.Count == 16, $"popup: only the panel, its rim, the main button and the picture are rounded ({cornerPieces.Count} corner pieces)");
        // In game a translucent corner texture is blended differently from a translucent panel (light squares),
        // so every corner piece must be opaque: translucent colours are blended onto the surface below.
        Func<JObject, string> rawColor = e => (string)e["components"].First(cc => (string)cc["type"] == "UnityEngine.UI.RawImage")["color"];
        Check(cornerPieces.All(e => Alpha(rawColor(e)) >= 0.999f), "every corner piece is opaque");
        // Shadows sit over the game view (no known colour below): plain stepped panels, no corner textures.
        var black = H.Live(player).Where(e => e["components"].Any(cc => (string)cc["type"] == "UnityEngine.UI.Image" && ((string)cc["color"] ?? "").StartsWith("0 0 0 0.") && (string)cc["color"] != "0 0 0 0")).ToList();
        Check(black.Count == 15, $"shadow: two stepped layers, fourteen panels and the backdrop ({black.Count})");
        // In game a texture's bottom row can pick up its top row (wrapping). Fill corners are covered there by
        // the bands; picture masks carry a transparent row at the top and bottom so no line crosses the picture.
        int fills = 0, masks = 0;
        foreach (var file in FileStorage.server.Files.Values)
        {
            var px = DecodePng(file, out int tw, out int th);
            Func<int, bool> clearRow = y => Enumerable.Range(0, tw).All(x => px[(y * tw + x) * 4 + 3] == 0);
            if (tw == 32 && th == 32 && !clearRow(0) && !clearRow(31)) fills++;
            if (tw == 32 && th == 34 && clearRow(0) && clearRow(33) && !(clearRow(1) && clearRow(32))) masks++;
        }
        Check(fills == 4 && masks == 4, $"corner textures: 4 fills, 4 masks with clear top/bottom rows ({fills}, {masks})");
        // The like button is two elements (button + text), also when it is redrawn on its own.
        int popupSize = H.Live(player).Count();
        H.Console(player, $"news.like {H.Id(Ann0)}");
        Check(H.Live(player).Count() == popupSize && H.Live(player).Count(e => (string)e["name"] == "NewsBroadcasterUI.Like" || (string)e["parent"] == "NewsBroadcasterUI.Like") == 2, "like button: two elements, swapped in place");
        Check(popupSize < 70, $"popup with a picture stays small ({popupSize} elements)");
        // What a page costs: the archive with five posts, all unread, with pictures.
        Fresh();
        for (int i = 1; i <= 5; i++) Post("Archive post " + i, "text", "Event", "https://img.example/p" + i + ".png");
        H.Console(player, "news.close.notif");
        H.Chat(player, "news");
        int archiveRounded = H.Live(player).Count();
        Check(archiveRounded < 200, $"archive page with rounded corners ({archiveRounded} elements)");
        Check(cornersOf(player).Count == 4 * (2 + 5 * 2), "archive: the panel and the cards are rounded (with their rims), nothing else");
        Check(H.Live(player).Count(e => H.Live(player).Any(t => (string)t["parent"] == (string)e["name"] && t["components"].Any(cc => (string)cc["type"] == "UnityEngine.UI.Text"))
            && e["components"].Any(cc => (string)cc["type"] == "UnityEngine.UI.Button" && ((string)cc["command"] ?? "").StartsWith("news.filter"))) == 7, "filter chips are one button and one text each");
        H.Chat(admin, "news", "admin");
        Check(H.Live(admin).Count() < 330, $"News Management with five posts ({H.Live(admin).Count()} elements)");
        H.CfgSet("General.RoundedCorners", false);
        H.Console(player, "news.close");
        H.Chat(player, "news");
        Check(H.Live(player).Count() < 110 && cornersOf(player).Count == 0, $"archive page with RoundedCorners off ({H.Live(player).Count()} elements, was {archiveRounded})");
        // no FileStorage (e.g. API changed): square fallback, no errors
        Fresh(preBoot: () => { CommunityEntity.ServerInstance = null; });
        Post("Square", "x");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        Check(FileStorage.server.Files.Count == 0 && UiOpen(player) && !H.Live(player).Any(e => e["components"].Any(cc => (string)cc["type"] == "UnityEngine.UI.RawImage")), "falls back to square corners when textures are unavailable");
        Fresh();
        H.CfgSet("General.RoundedCorners", false);
        H.Console(player, "news.close");
        Post("Square2", "x");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        Check(!H.Live(player).Any(e => e["components"].Any(cc => (string)cc["type"] == "UnityEngine.UI.RawImage")), "RoundedCorners=false draws square corners");

        // ---------------------------------------------------------------
        Section("ImageLibrary integration");
        Fresh();
        var il = new FakePlugin("ImageLibrary");
        var ready = new HashSet<string>();
        il.Handler = (h, a) => h == "HasImage" ? (object)ready.Contains((string)a[0]) : h == "GetImage" ? (object)("png:" + a[0]) : null;
        H.Set("ImageLibrary", il);
        Post("Img", "x", "Info", "https://img.example/a.png");
        Check(il.Calls.Any(c => c.hook == "AddImage" && (string)c.args[0] == "https://img.example/a.png"), "new image registered with ImageLibrary");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        Check(H.Live(player).Any(e => e["components"].Any(c => (string)c["type"] == "UnityEngine.UI.RawImage" && (string)c["url"] == "https://img.example/a.png")), "falls back to URL while ImageLibrary is still downloading");
        ready.Add("https://img.example/a.png");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        Check(H.Live(player).Any(e => e["components"].Any(c => (string)c["type"] == "UnityEngine.UI.RawImage" && (string)c["png"] == "png:https://img.example/a.png")), "uses ImageLibrary png when ready");
        H.Call("OnPluginLoaded", il);
        Sim.RunNextTicks();
        Check(il.Calls.Any(c => c.hook == "ImportImageList" && c.args.Length >= 4 && (bool)c.args[3] == false), "re-imports images when ImageLibrary (re)loads, without forcing re-download");


        // ---------------------------------------------------------------
        Section("Drafts and copy post");
        Fresh();
        Post("Live one", "x");
        H.Console(player, "news.close.notif");
        int bcBefore = HooksNamed("OnNewsBroadcast");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Wipe plan draft");
        H.Console(admin, "news.editor.input text Not ready yet");
        H.Console(admin, "news.editor.savedraft");
        var draft = H.AnnByTitle("Wipe plan draft");
        Check(draft != null && (bool)H.AGet(draft, "Draft") && LastChat(admin).StartsWith("Draft saved"), "SAVE DRAFT keeps the post unpublished");
        Check(HooksNamed("OnNewsBroadcast") == bcBefore && !ToastOpen(player), "a draft fires no broadcast hook and notifies nobody");
        H.Chat(player, "news");
        Check(UiOpen(player) && !H.Texts(player).Any(t => t.Contains("WIPE PLAN DRAFT")), "players don't see drafts in the archive");
        H.Console(player, $"news.view {H.Id(draft)}");
        Check(!H.Texts(player).Any(t => t.Contains("WIPE PLAN DRAFT")), "...or by opening them directly");
        H.Console(player, "news.close");
        H.Chat(player, "news", "latest");
        Check(H.Texts(player).Any(t => t.Contains("LIVE ONE")), "/news latest skips drafts");
        H.Console(player, "news.close");
        H.Console(admin, "news.admin");
        Check(H.Texts(admin).Contains("DRAFT") && H.Commands(admin).Contains($"news.admin.publish {H.Id(draft)} 0"), "admin list shows the draft with PUBLISH");
        long draftStamp = (long)H.AGet(draft, "Timestamp");
        H.Console(admin, $"news.admin.publish {H.Id(draft)} 0");
        Check(!(bool)H.AGet(draft, "Draft") && HooksNamed("OnNewsBroadcast") == bcBefore + 1 && ToastOpen(player), "PUBLISH makes it live, fires the hook and notifies players");
        Check(H.Ann(0) == draft && (long)H.AGet(draft, "Timestamp") >= draftStamp, "a published draft moves to the top");
        H.Console(player, "news.close.notif");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Second draft");
        H.Console(admin, "news.editor.savedraft");
        var d2 = H.AnnByTitle("Second draft");
        H.Console(admin, $"news.admin.edit {H.Id(d2)}");
        Check(H.Texts(admin).Contains("SAVE DRAFT") && H.Texts(admin).Contains("PUBLISH ANNOUNCEMENT"), "editing a draft offers SAVE DRAFT and PUBLISH");
        H.Console(admin, "news.editor.save");
        Check(!(bool)H.AGet(d2, "Draft") && ToastOpen(player), "publishing a draft from the editor notifies players");
        H.Console(player, "news.close.notif");
        H.Console(admin, $"news.admin.edit {H.Id(d2)}");
        Check(!H.Texts(admin).Contains("SAVE DRAFT") && H.Texts(admin).Contains("SAVE CHANGES"), "a published post can't go back to drafts");
        H.Console(admin, "news.editor.cancel");
        int countBefore = H.Announcements.Count;
        H.Console(admin, $"news.admin.copy {H.Id(d2)}");
        Check(LastChat(admin).StartsWith("Copied") && H.Texts(admin).Contains("CREATE NEW ANNOUNCEMENT"), "COPY opens the editor with the post's content");
        H.Console(admin, "news.editor.save");
        Check(H.Announcements.Count == countBefore + 1 && H.Announcements.Cast<object>().Count(a => (string)H.AGet(a, "Title") == "Second draft") == 2, "saving the copy creates a new post");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Console draft");
        H.Console(admin, "news.editor.savedraft");
        int draftIndex = H.Announcements.IndexOf(H.AnnByTitle("Console draft"));
        H.Console(null, $"news.publish {draftIndex}");
        Check(!(bool)H.AGet(H.AnnByTitle("Console draft"), "Draft") && Sim.ConsoleReplies.Last().StartsWith("Published"), "news.publish publishes a draft from the console");

        // ---------------------------------------------------------------
        Section("Posts for one group only");
        Fresh();
        Sim.Permission.CreateGroup("vip", "VIP", 0);
        H.CfgSet("Discord.Enabled", true);
        H.CfgSet("Discord.WebhookUrl", "https://discord.com/api/webhooks/1/x");
        Post("Public news", "for everyone");
        H.Console(player, "news.close.notif");
        int webBefore = Sim.WebRequests.Count;
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title VIP crate drop");
        H.Console(admin, "news.editor.toggle audience");
        H.Console(admin, "news.editor.toggle audience");
        Check(H.Texts(admin).Contains("VIP ONLY"), "audience cycles through the Oxide groups");
        H.Console(admin, "news.editor.save");
        var vip = H.AnnByTitle("VIP crate drop");
        Check((string)H.AGet(vip, "Group") == "vip", "post saved for the vip group");
        Check(Sim.WebRequests.Count == webBefore, "group-only posts stay off Discord by default");
        Check(!ToastOpen(player), "players outside the group are not notified");
        H.Chat(player, "news");
        Check(H.Texts(player).Any(t => t.Contains("PUBLIC NEWS")) && !H.Texts(player).Any(t => t.Contains("VIP CRATE")), "the archive hides posts for other groups");
        Check(H.Texts(player).Contains("1 UNREAD"), "unread count only counts visible posts");
        H.Console(player, $"news.view {H.Id(vip)}");
        int likesBefore = Set(vip, "LikedPlayers").Count;
        H.Console(player, $"news.like {H.Id(vip)}");
        Check(!H.Texts(player).Any(t => t.Contains("VIP CRATE")) && Set(vip, "LikedPlayers").Count == likesBefore, "can't open or like it by id");
        H.Console(player, "news.close");
        H.Chat(admin, "news");
        Check(H.Texts(admin).Contains("VIP ONLY"), "admins see it with a VIP ONLY badge");
        H.Console(admin, "news.close");
        Sim.Permission.AddUserGroup(player.UserIDString, "vip");
        H.Console(admin, $"news.admin.resend {H.Id(vip)} 0");
        Check(ToastOpen(player), "once in the group, the player is notified");
        H.Console(player, $"news.view {H.Id(vip)}");
        Check(H.Texts(player).Any(t => t.Contains("VIP CRATE")) && H.Texts(player).Contains("VIP ONLY"), "...and can read it (VIP ONLY tag in the header)");
        H.CfgSet("Discord.PostGroupOnlyPosts", true);
        H.Call("SendToDiscord", vip);
        Check(Sim.WebRequests.Count == webBefore + 1, "Discord.PostGroupOnlyPosts sends them after all");

        // ---------------------------------------------------------------
        Section("Auto-delete");
        Fresh();
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Weekend event");
        H.Console(admin, "news.editor.toggle expire");
        H.Console(admin, "news.editor.toggle expire");
        Check(H.Texts(admin).Contains("AFTER 3 DAYS"), "auto-delete cycles through the presets");
        H.Console(admin, "news.editor.save");
        var weekend = H.AnnByTitle("Weekend event");
        Check(Math.Abs((long)H.AGet(weekend, "ExpiresAt") - (DateTime.UtcNow.Ticks + 3 * TimeSpan.TicksPerDay)) < TimeSpan.TicksPerMinute, "publishing sets the delete time 3 days ahead");
        H.Console(admin, "news.admin");
        Check(H.Texts(admin).Any(t => t.Contains("DELETES IN 2D 23H") || t.Contains("DELETES IN 3D 0H")), "admin list shows when it deletes itself");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Draft with expiry");
        H.Console(admin, "news.editor.toggle expire");
        H.Console(admin, "news.editor.savedraft");
        var keptDraft = H.AnnByTitle("Draft with expiry");
        Check((int)H.AGet(keptDraft, "ExpireDays") == 1 && (long)H.AGet(keptDraft, "ExpiresAt") == 0, "a draft's auto-delete only starts when it is published");
        H.ASet(weekend, "ExpiresAt", DateTime.UtcNow.Ticks - 1);
        H.Console(player, "news.close.notif");
        H.Chat(player, "news");
        Check(!H.Texts(player).Any(t => t.Contains("WEEKEND EVENT")), "an expired post is hidden right away");
        int deletedBefore = HooksNamed("OnNewsDeleted");
        H.Call("ScheduleNextCheck");   // the test moved the time by hand
        Sim.Advance(2);
        Check(H.AnnByTitle("Weekend event") == null && HooksNamed("OnNewsDeleted") == deletedBefore + 1, "the timer deletes it when it is due (OnNewsDeleted fired)");
        Check(H.AnnByTitle("Draft with expiry") != null, "drafts are never auto-deleted");

        // ---------------------------------------------------------------
        Section("Poll timer and results");
        Fresh();
        H.CfgSet("Discord.Enabled", true);
        H.CfgSet("Discord.WebhookUrl", "https://discord.com/api/webhooks/1/x");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Pick a map");
        H.Console(admin, "news.editor.toggle poll");
        H.Console(admin, "news.editor.input poll1 Small");
        H.Console(admin, "news.editor.input poll2 Big");
        H.Console(admin, "news.editor.toggle polltimer");
        H.Console(admin, "news.editor.toggle polltimer");
        Check(H.Texts(admin).Contains("AFTER 6 HOURS"), "poll timer cycles through the presets");
        H.Console(admin, "news.editor.save");
        var mapPoll = H.AnnByTitle("Pick a map");
        Check(Math.Abs((long)H.AGet(mapPoll, "PollEndsAt") - (DateTime.UtcNow.Ticks + 6 * TimeSpan.TicksPerHour)) < TimeSpan.TicksPerMinute, "publishing starts the poll timer");
        H.Console(player, "news.close.notif");
        H.Console(player, $"news.view {H.Id(mapPoll)}");
        Check(H.Texts(player).Any(t => t.Contains("CLOSES IN 5H 59M")), "the poll shows when it closes");
        H.Console(player, $"news.vote {H.Id(mapPoll)} 1");
        H.ASet(mapPoll, "PollEndsAt", DateTime.UtcNow.Ticks - 1);
        int webBeforeResults = Sim.WebRequests.Count, closedHooks = HooksNamed("OnNewsPollClosed");
        H.Call("ScheduleNextCheck");   // the test moved the time by hand
        Sim.Advance(2);
        Check((bool)H.AGet(mapPoll, "PollClosed") && HooksNamed("OnNewsPollClosed") == closedHooks + 1, "the timer closes the poll (OnNewsPollClosed fired)");
        Check(H.ChatOf(player).Any(m => m == "Poll closed: Pick a map - winner: Big (100%, 1 vote)."), "players get the result in chat");
        Check(Sim.WebRequests.Count == webBeforeResults + 1 && Sim.WebRequests.Last().Body.Contains("POLL RESULTS") && Sim.WebRequests.Last().Body.Contains("**Big"), "results are posted to Discord");
        H.Console(player, $"news.vote {H.Id(mapPoll)} 0");
        Check(((Dictionary<ulong, int>)H.AGet(mapPoll, "PollVotes"))[player.userID] == 1, "no votes after the poll closed");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Quick poll");
        H.Console(admin, "news.editor.toggle poll");
        H.Console(admin, "news.editor.input poll1 Yes");
        H.Console(admin, "news.editor.input poll2 No");
        H.Console(admin, "news.editor.save");
        var quick = H.AnnByTitle("Quick poll");
        H.Console(admin, $"news.admin.edit {H.Id(quick)}");
        H.Console(admin, "news.editor.toggle pollclosed");
        H.Console(admin, "news.editor.save");
        Check((bool)H.AGet(quick, "PollClosed") && H.ChatOf(player).Last() == "Poll closed: Quick poll - no votes were cast.", "CLOSE VOTING in the editor announces the result too");

        // ---------------------------------------------------------------
        Section("One timer for what is due next");
        Fresh();
        Func<Oxide.Plugins.Timer> scheduleTimer = () => (Oxide.Plugins.Timer)H.Get("scheduleTimer");
        Func<int> timersRunning = () => Sim.Timers.Count;
        Post("Nothing scheduled", "x");
        H.Console(player, "news.close.notif"); H.Console(admin, "news.close.notif");
        Sim.Advance(20);
        Check(scheduleTimer() == null && timersRunning() == 0, "no auto-delete and no poll timer anywhere: no timer runs at all");
        int idleWrites = Sim.DataWrites;
        Sim.Advance(600);
        Check(Sim.DataWrites == idleWrites && timersRunning() == 0, "...and nothing happens while the server idles");
        H.Call("API_CreateNews", new Dictionary<string, object> { ["title"] = "Gone in a day", ["expireDays"] = 1, ["notify"] = false });
        Check(scheduleTimer() != null && !scheduleTimer().Destroyed && Math.Abs(scheduleTimer().Delay - 3600f) < 0.01f, "a post with auto-delete starts one timer (far away: checked again within the hour)");
        var firstTimer = scheduleTimer();
        H.Call("API_CreateNews", new Dictionary<string, object> { ["title"] = "Quick poll", ["pollOptions"] = new[] { "Yes", "No" }, ["pollHours"] = 1, ["notify"] = false });
        Check(!ReferenceEquals(scheduleTimer(), firstTimer) && firstTimer.Destroyed && timersRunning() == 1, "something due sooner replaces it (still one timer)");
        var pollTimer = scheduleTimer();
        Post("Unrelated", "x");
        H.Console(player, "news.close.notif"); H.Console(admin, "news.close.notif");
        Check(ReferenceEquals(scheduleTimer(), pollTimer), "saving without a change to what is due leaves the timer alone");
        var quickPoll = H.AnnByTitle("Quick poll");
        H.ASet(quickPoll, "PollEndsAt", DateTime.UtcNow.Ticks + 20 * TimeSpan.TicksPerSecond);
        H.Call("ScheduleNextCheck");
        Check(scheduleTimer().Delay > 18f && scheduleTimer().Delay <= 20f, "the timer is set for the moment the poll closes");
        H.ASet(quickPoll, "PollEndsAt", DateTime.UtcNow.Ticks - 1);
        Sim.Advance(21);
        Check((bool)H.AGet(quickPoll, "PollClosed") && scheduleTimer() != null && Math.Abs(scheduleTimer().Delay - 3600f) < 0.01f, "it closes the poll and waits for the next thing (the auto-delete)");
        H.Call("API_DeleteNews", H.Id(H.AnnByTitle("Gone in a day")));
        Check(scheduleTimer() == null && timersRunning() == 0, "nothing left to wait for: the timer is gone");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Draft with timer");
        H.Console(admin, "news.editor.toggle expire");
        H.Console(admin, "news.editor.savedraft");
        Check(scheduleTimer() == null, "a draft's auto-delete does not start a timer");
        H.Console(admin, $"news.admin.publish {H.Id(H.AnnByTitle("Draft with timer"))} 0");
        Check(scheduleTimer() != null, "publishing it does");
        H.Call("Unload");
        Check(timersRunning() == 0, "unload stops it");
        // After a restart: what ran out while the server was down is handled at once.
        long past = DateTime.UtcNow.Ticks - TimeSpan.TicksPerMinute;
        Fresh(preBoot: () =>
        {
            Sim.DataFiles["NewsBroadcaster_Data"] = @"{ ""Announcements"": [
                { ""Id"": ""keep"", ""Title"": ""Stays"", ""Text"": ""b"", ""Timestamp"": 3000 },
                { ""Id"": ""poll"", ""Title"": ""Old poll"", ""Text"": ""b"", ""Timestamp"": 2000, ""PollOptions"": [""A"", ""B""], ""PollEndsAt"": " + past + @" },
                { ""Id"": ""gone"", ""Title"": ""Expired"", ""Text"": ""a"", ""Timestamp"": 1000, ""ExpiresAt"": " + past + @" } ] }";
        });
        Check(H.AnnByTitle("Expired") == null && (bool)H.AGet(H.AnnByTitle("Old poll"), "PollClosed") && H.Get("scheduleTimer") == null, "on load, overdue posts are deleted and overdue polls closed; no timer when nothing else is due");

        // ---------------------------------------------------------------
        Section("Redraw cooldown: pages, filters, search");
        Fresh();
        for (int i = 1; i <= 12; i++) Post("Paged post " + i, i == 7 ? "needle" : "text", i % 2 == 0 ? "Event" : "Info");
        H.Console(player, "news.close.notif");
        H.Chat(player, "news");
        Sim.Advance(1);
        int draws = Sim.Ui(player).AddCalls;
        for (int i = 0; i < 10; i++) H.Console(player, "news.page " + (i % 3));
        Check(Sim.Ui(player).AddCalls == draws + 1 && H.Texts(player).Contains("PAGE 1 / 3"), "ten page clicks in one moment: the first is drawn at once, the rest wait");
        Sim.Advance(0.5);
        Check(Sim.Ui(player).AddCalls == draws + 2 && H.Texts(player).Contains("PAGE 1 / 3") && (int)H.Call("GetArchivePage", player.userID.Get()) == 0, "...and are drawn once, with the page clicked last");
        Sim.Advance(1);
        draws = Sim.Ui(player).AddCalls;
        // Typing a search and clicking a chip sends both commands together: neither may be lost.
        H.Console(player, "news.search needle");
        H.Console(player, "news.filter info");
        Check(Sim.Ui(player).AddCalls == draws + 1, "search + chip click together: one redraw now");
        Sim.Advance(0.5);
        Check(Sim.Ui(player).AddCalls == draws + 2 && H.Texts(player).Contains("PAGED POST 7") && !H.Texts(player).Contains("PAGED POST 5") && !H.Texts(player).Contains("PAGED POST 8"), "...one after the cooldown, with the search and the filter both applied");
        Sim.Advance(1);
        H.Console(player, "news.search.clear");
        H.Console(player, "news.page 2");
        H.Console(player, "news.close");
        Sim.Advance(1);
        Check(!UiOpen(player), "a redraw that was waiting is dropped when the player closes the menu");
        H.Chat(player, "news");
        Sim.Advance(1);
        H.Console(player, "news.page 1");
        H.Console(player, "news.page 2");
        H.Console(player, $"news.view {H.Id(H.AnnByTitle("Paged post 12"))} archive");
        Sim.Advance(1);
        Check(H.HasElement(player, "NewsBroadcasterUI.Main") && !H.HasElement(player, "NewsBroadcasterUI.List"), "...and when they open a post meanwhile");
        H.Console(player, "news.close");
        H.Chat(player, "news");
        Sim.Advance(1);
        H.Console(player, "news.page 1");
        H.Console(player, "news.page 2");
        H.Console(player, "news.settings");
        Sim.Advance(1);
        Check(H.HasElement(player, "NewsConfirmUI.Settings") && H.Texts(player).Contains("PAGE 3 / 3"), "a dialog opened meanwhile stays open (the waiting page is drawn under it first)");
        H.Console(player, "news.close");
        H.Chat(admin, "news", "admin");
        Sim.Advance(1);
        draws = Sim.Ui(admin).AddCalls;
        H.Console(admin, "news.admin.filter published");
        H.Console(admin, "news.admin.sort");
        H.Console(admin, "news.admin.search needle");
        H.Console(admin, "news.admin.page 3");
        Check(Sim.Ui(admin).AddCalls == draws + 1, "News Management: four clicks together, one redraw now");
        Sim.Advance(0.5);
        Check(Sim.Ui(admin).AddCalls == draws + 2 && H.Texts(admin).Contains("OLDEST FIRST") && H.Texts(admin).Any(t => t.StartsWith("1 POSTS")), "...and one with all of them applied");
        H.Console(admin, "news.admin.page 0");
        H.Call("OnPlayerDisconnected", admin, "bye");
        Check(!((Dictionary<ulong, Oxide.Plugins.Timer>)H.Get("redrawTimers")).ContainsKey(admin.userID) && !((Dictionary<ulong, float>)H.Get("redrawCooldowns")).ContainsKey(admin.userID), "a waiting redraw goes with the player");

        // ---------------------------------------------------------------
        Section("Live countdown");
        Fresh();
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Wipe countdown");
        H.Console(admin, "news.editor.input cdlabel Wipe in");
        H.Console(admin, "news.editor.input cdtime soon-ish");
        Check(H.Texts(admin).Any(t => t.StartsWith("Not a time")), "text that isn't a time is flagged");
        H.Console(admin, "news.editor.input cdtime 2d 4h");
        Check(H.Texts(admin).Any(t => t.StartsWith("Ends ") && (t.Contains("(in 2d 3h)") || t.Contains("(in 2d 4h)"))), "2d 4h is read as a time from now");
        Check(!CuiWarnings().Any(), "the hint is swapped in place");
        H.Console(admin, "news.editor.save");
        var countdown = H.AnnByTitle("Wipe countdown");
        Check(Math.Abs((long)H.AGet(countdown, "CountdownTo") - (DateTime.UtcNow.Ticks + 52 * TimeSpan.TicksPerHour)) < TimeSpan.TicksPerMinute, "countdown time saved");
        H.Console(player, "news.close.notif");
        H.Console(player, $"news.view {H.Id(countdown)}");
        var cdElement = H.Live(player).FirstOrDefault(e => e["components"].Any(cc => (string)cc["type"] == "Countdown" && ((string)cc["command"] ?? "").StartsWith("news.countdown")));
        var cdComp = cdElement?["components"].First(cc => (string)cc["type"] == "Countdown");
        var cdText = cdElement?["components"].First(cc => (string)cc["type"] == "UnityEngine.UI.Text");
        Check(cdElement != null && (string)cdComp["timerFormat"] == "HoursMinutesSeconds" && Math.Abs((float)cdComp["startTime"] - 4 * 3600) < 120 && ((string)cdText["text"]).StartsWith("2 DAYS  %TIME_LEFT%"), "popup shows a live countdown (2 DAYS + ticking hh:mm:ss)");
        Check(H.Texts(player).Contains("WIPE IN"), "countdown title shown");
        H.ASet(countdown, "CountdownTo", DateTime.UtcNow.Ticks + 3 * TimeSpan.TicksPerDay);
        H.Console(player, $"news.countdown {H.Id(countdown)}");
        cdText = H.Live(player).First(e => e["components"].Any(cc => (string)cc["type"] == "Countdown"))["components"].First(cc => (string)cc["type"] == "UnityEngine.UI.Text");
        Check(((string)cdText["text"]).StartsWith("2 DAYS") && !CuiWarnings().Any(), "when a day runs out the block is redrawn in place");
        H.ASet(countdown, "CountdownTo", DateTime.UtcNow.Ticks - 1);
        H.Console(player, $"news.countdown {H.Id(countdown)}");
        Check(H.Texts(player).Contains("STARTED") && !H.Live(player).Any(e => e["components"].Any(cc => (string)cc["type"] == "Countdown")), "when it has ended it says STARTED");
        H.Console(player, "news.close");
        H.ASet(countdown, "CountdownTo", DateTime.UtcNow.Ticks + 3 * TimeSpan.TicksPerDay);
        H.Chat(player, "news");
        Check(H.Texts(player).Any(t => t.Contains("WIPE IN 2D 23H") || t.Contains("WIPE IN 3D 0H")), "archive rows show the countdown");
        long parsed = (long)H.Call("ParseCountdown", "2030-01-02 03:04", DateTime.UtcNow.Ticks);
        Check(parsed == new DateTime(2030, 1, 2, 3, 4, 0, DateTimeKind.Local).ToUniversalTime().Ticks, "a date and time is read as server time");
        Check((long)H.Call("ParseCountdown", "", 0L) == 0 && (long)H.Call("ParseCountdown", "tomorrow", 0L) == -1, "empty clears it, nonsense is rejected");

        // ---------------------------------------------------------------
        Section("Archive filters and search");
        Fresh();
        Post("Raid event tonight", "Bring your team", "Event");
        Post("Patch notes", "Balance changes", "Update");
        H.Console(null, "news.poll \"Map vote\" \"Pick one\" \"A|B\" Event");
        Post("Server rules", "Be nice", "Info");
        H.Console(player, "news.close.notif");
        H.Chat(player, "news");
        Func<List<string>> rowTitles = () => H.Texts(player).Where(t => t == "RAID EVENT TONIGHT" || t == "PATCH NOTES" || t == "MAP VOTE" || t == "SERVER RULES").ToList();
        Check(H.Commands(player).Contains("news.filter event") && H.Commands(player).Contains("news.filter polls") && rowTitles().Count == 4, "filter chips shown above all posts");
        Nav(player, "news.filter event");
        Check(rowTitles().OrderBy(t => t).SequenceEqual(new[] { "MAP VOTE", "RAID EVENT TONIGHT" }), "EVENT chip shows only events");
        Nav(player, "news.filter polls");
        Check(rowTitles().SequenceEqual(new[] { "MAP VOTE" }), "POLLS chip shows only polls");
        Nav(player, "news.filter all");
        Nav(player, "news.search balance");
        Check(rowTitles().SequenceEqual(new[] { "PATCH NOTES" }), "search matches the text of a post");
        Nav(player, "news.search zzz");
        Check(H.Texts(player).Contains("No posts match this filter.") && H.Commands(player).Contains("news.search.clear"), "no matches: message and SHOW ALL");
        Nav(player, "news.search.clear");
        Check(rowTitles().Count == 4, "clearing shows everything again");
        Nav(player, "news.filter event");
        H.Console(player, $"news.view {H.Id(H.AnnByTitle("Raid event tonight"))} archive");
        Check(H.Texts(player).Contains("2 / 2"), "prev / next stays inside the filter");
        H.Console(player, "news.close");
        H.Chat(player, "news");
        Check(rowTitles().Count == 4, "/news opens the full archive again");

        // ---------------------------------------------------------------
        Section("Image link check");
        Fresh();
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input image https://imgur.com/a/abc123");
        Check(H.Texts(admin).Any(t => t.StartsWith("That's an imgur page")), "imgur page link flagged");
        H.Console(admin, "news.editor.input image https://i.imgur.com/abc123.png");
        Check(H.Texts(admin).Contains("✓ Direct image link"), "direct image link accepted");
        H.Console(admin, "news.editor.input image https://cdn.discordapp.com/attachments/1/2/pic.png?ex=abc");
        Check(H.Texts(admin).Any(t => t.StartsWith("Discord image links")), "Discord links flagged (they expire)");
        H.Console(admin, "news.editor.input image https://example.com/anim.gif");
        Check(H.Texts(admin).Contains("Rust only shows PNG and JPG pictures"), "GIF / WEBP flagged");
        H.Console(admin, "news.editor.input image https://example.com/gallery/page");
        Check(H.Texts(admin).Any(t => t.StartsWith("Doesn't end in .png")), "links without .png / .jpg flagged");
        Check(!CuiWarnings().Any(), "hint swapped in place");
        H.Console(null, "news.show \"Pic\" \"https://imgur.com/gallery/xyz\" \"text\"");
        Check(Sim.ConsoleReplies.Last().StartsWith("Image link warning: That's an imgur page"), "news.show warns in the console too");

        // ---------------------------------------------------------------
        Section("Character counters");
        Fresh();
        H.Console(admin, "news.admin.create");
        Check(H.Texts(admin).Contains("0 / 120") && H.Texts(admin).Contains("0 CHARACTERS"), "counters start at zero");
        H.Console(admin, "news.editor.input title Hello world");
        Check(H.Texts(admin).Contains("11 / 120"), "title counter updates after typing");
        H.Console(admin, "news.editor.input text " + new string('a', 1300));
        Check(H.Texts(admin).Any(t => t.StartsWith("1300 CHARACTERS") && t.Contains("LONG")), "long body flagged");
        Check(H.Live(admin).Any(e => e["components"].Any(cc => (string)cc["command"] == "news.editor.input title" && (int?)cc["characterLimit"] == 120)), "title field limited to 120 characters");

        // ---------------------------------------------------------------
        Section("Player settings");
        Fresh();
        H.Chat(player, "news", "settings");
        Check(H.Texts(player).Contains("NEWS SETTINGS") && H.Commands(player).Contains("news.settings.toggle sound"), "/news settings opens the settings");
        H.Chat(player, "news", "help");
        Check(LastChat(player).Contains("/news settings"), "/news help mentions it");
        H.Console(player, "news.settings.toggle sound");
        H.Console(player, "news.settings.toggle popups");
        Check(H.Texts(player).Count(t => t == "OFF") == 2 && H.Texts(player).Count(t => t == "ON") == 1, "switches show OFF after clicking");
        H.Console(player, "news.confirm.close");
        Post("Quiet post", "x");
        Check(!ToastOpen(player) && LastChat(player).StartsWith("New announcement: Quiet post"), "pop-ups off: a chat line instead of the toast");
        H.Console(player, "news.settings.toggle popups");
        H.Console(player, "news.confirm.close");
        H.CfgSet("Notification.Enabled", false);
        H.Console(admin, "news.close");
        int logLines = Sim.LogLines.Count;
        Post("Loud post", "x");
        var effects = Sim.LogLines.Skip(logLines).ToList();
        Check(UiOpen(player) && !effects.Any(l => l.Contains("[effect -> Survivor]")) && effects.Any(l => l.Contains("[effect -> DEDA]")), "sound off: the popup opens silently (others still hear it)");
        H.Console(player, "news.close");
        H.Console(player, "news.settings.toggle join");
        H.Console(player, "news.confirm.close");
        BasePlayer.activePlayerList.Remove(player);
        Post("While away", "x");
        BasePlayer.activePlayerList.Add(player);
        H.Call("OnPlayerSleepEnded", player);
        Sim.Advance(2.5);
        Check(!UiOpen(player), "join pop-up off: no popup when waking up");
        Sim.Advance(16);
        Check(Sim.DataFiles["NewsBroadcaster_Data"].Contains("\"PlayerSettings\"") && Sim.DataFiles["NewsBroadcaster_Data"].Contains("\"JoinPopup\": false"), "settings are saved per player");

        // ---------------------------------------------------------------
        Section("Notification position");
        Fresh();
        Func<JToken> toastRect = () => H.Live(player).First(e => (string)e["parent"] == "Hud" && ((string)e["name"]).StartsWith("NewsNotificationUI."))["components"].First(cc => (string)cc["type"] == "RectTransform");
        H.CfgSet("Notification.Position", "BottomCenter");
        H.CfgSet("Notification.OffsetY", 12f);
        Post("Toast here", "x");
        Check((string)toastRect()["anchormin"] == "0.405 0.17" && (string)toastRect()["offsetmin"] == "0 12", "BottomCenter + OffsetY place the toast");
        H.CfgSet("Notification.Position", "topleft");
        H.CfgSet("Notification.OffsetY", 0f);
        Post("Toast there", "x");
        Check((string)toastRect()["anchormin"] == "0.01 0.845", "the position is read whatever its capitals");
        H.CfgSet("Notification.Position", "somewhere");
        Post("Toast anywhere", "x");
        Check((string)toastRect()["anchormin"] == "0.79 0.845", "an unknown position is the top right corner");

        // ---------------------------------------------------------------
        Section("No blinking, the mouse stays put");
        Fresh();
        Post("First", "a", "Event"); Post("Second", "b", "Info"); Post("Third", "c", "Event");
        H.Console(player, "news.close.notif");
        H.Chat(player, "news");
        static long IdOf(BasePlayer p, string name) => (long?)H.Live(p).FirstOrDefault(e => (string)e["name"] == name)?["__id"] ?? -1;
        long root = IdOf(player, "NewsBroadcasterUI");
        Check(root > 0 && Sim.Ui(player).CursorGrabs == 1, "opening the archive grabs the cursor once");
        int destroysBefore = Sim.Ui(player).DestroyCalls;
        Nav(player, "news.filter event");
        Check(!H.Live(player).Any(e => (string)e["name"] != "NewsBroadcasterUI" && Fade(e) > 0f), "a click in the same screen redraws instantly (no fade)");
        Check(H.Live(player).Count(e => (string)e["name"] == "NewsBroadcasterUI.Screen") == 1 && !CuiWarnings().Any(), "the old view is swapped out in the same message");
        Nav(player, "news.search second");
        Nav(player, "news.search.clear");
        Nav(player, "news.page 0");
        var thirdPost = H.AnnByTitle("Third");
        H.Console(player, $"news.view {H.Id(thirdPost)} archive");
        Check(H.Live(player).Max(e => (string)e["name"] == "NewsBroadcasterUI" ? 0f : Fade(e)) <= 0.2f, "moving to another screen only fades in briefly");
        H.Console(player, $"news.like {H.Id(thirdPost)}");
        H.Console(player, $"news.view {H.Id(H.AnnByTitle("Second"))} archive");
        Nav(player, "news.page 0");
        H.Console(player, "news.settings");
        H.Console(player, "news.settings.toggle sound");
        H.Console(player, "news.settings.toggle sound");
        H.Console(player, "news.confirm.close");
        Check(IdOf(player, "NewsBroadcasterUI") == root && Sim.Ui(player).CursorGrabs == 1, "clicking through filters, posts, likes and settings never recreates the cursor (mouse stays put)");
        H.Console(player, "news.close");
        H.Chat(player, "news");
        Check(Sim.Ui(player).CursorGrabs == 2, "closing and reopening grabs it again, as expected");
        H.Console(player, "news.close");

        H.Console(admin, "news.close.notif");
        H.Chat(admin, "news", "admin");
        long adminRoot = IdOf(admin, "NewsBroadcasterUI");
        H.Console(admin, $"news.admin.toggleselect {H.Id(thirdPost)} 0");
        H.Console(admin, "news.admin.clearsel 0");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.toggle pin");
        H.Console(admin, "news.editor.settype alert");
        H.Console(admin, "news.editor.toggle expire");
        H.Console(admin, "news.editor.preview");
        H.Console(admin, "news.editor.back");
        H.Console(admin, "news.editor.cancel");
        H.Console(admin, $"news.admin.delconfirm {H.Id(thirdPost)} 0");
        H.Console(admin, "news.confirm.close");
        H.Console(admin, "news.admin.themes");
        Nav(admin, "news.admin.page 0");
        Check(IdOf(admin, "NewsBroadcasterUI") == adminRoot && Sim.Ui(admin).CursorGrabs == 1 && !CuiWarnings().Any(), "admin: list, editor, preview, dialogs and themes keep the same cursor");

        H.Chat(noperm, "news", "settings");
        var stranger = H.MakePlayer(76561198000000077, "Viewer");
        H.Chat(stranger, "news", "settings");
        long dialogRoot = IdOf(stranger, "NewsConfirmUI");
        H.Console(stranger, "news.settings.toggle popups");
        H.Console(stranger, "news.settings.toggle popups");
        Check(dialogRoot > 0 && IdOf(stranger, "NewsConfirmUI") == dialogRoot && Sim.Ui(stranger).CursorGrabs == 1, "settings opened from chat: switches don't recreate the cursor either");
        // ---------------------------------------------------------------
        Section("Console details: types, news.trigger, stale toasts");
        Fresh();
        Post("First live", "x");
        H.Console(null, "news.show \"Wipe\" - Restart in 5");
        var wipe = H.AnnByTitle("Wipe");
        Check((string)H.AGet(wipe, "Text") == "Restart in 5" && H.AGet(wipe, "Type").ToString() == "Info", "a number at the end of news.show is text, not a post type");
        H.Console(null, "news.show \"Typed\" - Hello warning");
        var typed = H.AnnByTitle("Typed");
        Check((string)H.AGet(typed, "Text") == "Hello" && H.AGet(typed, "Type").ToString() == "Warning", "a type name at the end still sets the type");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.settype 9");
        H.Console(admin, "news.editor.input title Hidden draft");
        H.Console(admin, "news.editor.savedraft");
        var hidden = H.AnnByTitle("Hidden draft");
        Check(H.AGet(hidden, "Type").ToString() == "Info", "the editor ignores type numbers that are not a type");
        H.Console(admin, "news.close");

        H.Console(player, "news.close.notif");
        H.Console(null, "news.trigger Survivor 0");
        Check(Sim.ConsoleReplies.Last().StartsWith("Survivor cannot see that announcement") && !UiOpen(player), "news.trigger says so when the player can't see the post (index 0 is the draft)");
        H.Console(null, "news.trigger Survivor 1");
        Check(Sim.ConsoleReplies.Last() == "News popup triggered for Survivor" && H.Texts(player).Contains("TYPED"), "news.trigger uses the same index as news.list");
        H.Console(null, $"news.trigger Survivor {H.Id(wipe)}");
        Check(H.Texts(player).Contains("WIPE"), "news.trigger accepts an id");
        H.Console(player, "news.close");
        H.Console(null, "news.trigger Survivor");
        Check(H.Texts(player).Contains("TYPED"), "news.trigger without an index shows the newest post the player can see");
        H.Console(player, "news.close");
        H.Console(null, "news.trigger Survivor 99");
        Check(Sim.ConsoleReplies.Last().StartsWith("Invalid index") && !UiOpen(player), "news.trigger rejects an unknown index");

        Post("Short lived", "x");
        Check(ToastOpen(player), "toast for the new post");
        string shortId = H.Id(H.AnnByTitle("Short lived"));
        H.Console(null, $"news.delete {shortId}");
        H.Console(player, $"news.view {shortId}");
        Check(!ToastOpen(player) && !UiOpen(player), "clicking the toast of a deleted post just removes the toast");

        // ---------------------------------------------------------------
        Section("Mark all read only covers what the player can see");
        Fresh();
        Sim.Permission.CreateGroup("vip", "VIP", 0);
        Post("For everyone", "x");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title VIP only news");
        H.Console(admin, "news.editor.toggle audience");
        H.Console(admin, "news.editor.toggle audience");
        H.Console(admin, "news.editor.save");
        H.Console(admin, "news.close");
        Check((string)H.AGet(H.AnnByTitle("VIP only news"), "Group") == "vip", "a newer post for the vip group");
        H.Chat(player, "news", "read");
        Check(LastChat(player) == "Marked 1 announcement(s) as read." && (int)H.Call("CountUnread", player) == 0, "/news read marks the one visible post");
        Sim.Permission.AddUserGroup(player.UserIDString, "vip");
        Check((int)H.Call("CountUnread", player) == 1, "a group post the player gets access to later is still unread");

        // ---------------------------------------------------------------
        Section("Poll whose timer ran out but is not closed yet");
        Fresh();
        H.Console(null, "news.poll \"Late poll\" \"x\" \"Yes|No\"");
        var latePoll = H.AnnByTitle("Late poll");
        H.ASet(latePoll, "PollEndsAt", DateTime.UtcNow.Ticks - 1);
        H.Console(player, "news.close.notif");
        H.Console(player, $"news.view {H.Id(latePoll)}");
        Check(!(bool)H.AGet(latePoll, "PollClosed") && H.Texts(player).Contains("VOTING CLOSED") && !H.Commands(player).Any(c => c.StartsWith("news.vote")), "the popup already shows it closed, with nothing to click");

        // ---------------------------------------------------------------
        Section("Broken translations and stale player markers");
        Fresh();
        Post("Anything", "x");
        var langMessages = (Dictionary<string, Dictionary<string, string>>)H.AGet(H.Get("lang"), "messages");
        langMessages["en"]["MarkedAllRead"] = "Marked {0} of {3} as read";
        H.Chat(player, "news", "read");
        Check(LastChat(player) == "Marked {0} of {3} as read", "a lang line with a bad placeholder is sent as it is instead of breaking the command");

        Fresh(preBoot: () =>
        {
            Sim.DataFiles["NewsBroadcaster_Data"] = @"{
              ""Announcements"": [
                { ""Id"": ""n2"", ""Title"": ""Newer"", ""Text"": ""b"", ""Timestamp"": 2000 },
                { ""Id"": ""n1"", ""Title"": ""Older"", ""Text"": ""a"", ""Timestamp"": 1000 }
              ],
              ""LastSeenNews"": { ""76561198000000002"": 1000, ""76561198000000050"": 400 },
              ""ReadAllUpTo"": { ""76561198000000002"": 2000, ""76561198000000051"": 999 }
            }";
        });
        Sim.Advance(16);
        var storedAfterLoad = JObject.Parse(Sim.DataFiles["NewsBroadcaster_Data"]);
        Check(storedAfterLoad["LastSeenNews"]["76561198000000050"] == null && storedAfterLoad["ReadAllUpTo"]["76561198000000051"] == null, "markers older than the oldest post are dropped on load");
        Check(storedAfterLoad["LastSeenNews"]["76561198000000002"] != null && storedAfterLoad["ReadAllUpTo"]["76561198000000002"] != null, "markers that still matter are kept");
        Check((int)H.Call("CountUnread", player) == 0, "...and still work");

        // ---------------------------------------------------------------
        Section("News Management: stat cards, tabs, search, sort");
        Fresh();
        Post("Alpha wipe news", "first", "Event");
        Post("Beta patch notes", "second", "Update");
        Post("Gamma rules", "third wipe mention");
        H.Console(null, "news.poll \"Delta poll\" \"x\" \"Yes|No\"");
        var alpha = H.AnnByTitle("Alpha wipe news");
        var beta = H.AnnByTitle("Beta patch notes");
        for (ulong i = 0; i < 5; i++) Set(alpha, "ReadByPlayers").Add(900 + i);
        for (ulong i = 0; i < 3; i++) Set(beta, "LikedPlayers").Add(900 + i);
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Epsilon draft");
        H.Console(admin, "news.editor.savedraft");
        H.Console(admin, $"news.admin.togglepin {H.Id(beta)} 0");
        string[] RowTitles() => H.Live(admin).Where(e => System.Text.RegularExpressions.Regex.IsMatch((string)e["name"] ?? "", @"\.Admin\.\d+$"))
            .Select(e => H.Live(admin).Where(x => (string)x["parent"] == (string)e["name"]).SelectMany(x => x["components"].Children<JObject>()).Where(c => (string)c["type"] == "UnityEngine.UI.Text").Select(c => (string)c["text"]).First(t => !string.IsNullOrEmpty(t) && t != "✓")).ToArray();
        Check(H.Texts(admin).Contains("PUBLISHED") && H.Texts(admin).Contains("1 PINNED") && H.Texts(admin).Contains("1.3 PER POST") && H.Texts(admin).Contains("60% OF READS"), "stat cards: published, pinned, reads per post, like rate");
        Check(RowTitles().SequenceEqual(new[] { "BETA PATCH NOTES", "EPSILON DRAFT", "DELTA POLL", "GAMMA RULES", "ALPHA WIPE NEWS" }), "default order: pinned first, then newest");
        Nav(admin, "news.admin.filter drafts");
        Check(RowTitles().SequenceEqual(new[] { "EPSILON DRAFT" }) && H.Texts(admin).Any(t => t.StartsWith("1 POSTS")), "DRAFTS tab");
        Nav(admin, "news.admin.filter polls");
        Check(RowTitles().SequenceEqual(new[] { "DELTA POLL" }), "POLLS tab");
        Nav(admin, "news.admin.filter pinned");
        Check(RowTitles().SequenceEqual(new[] { "BETA PATCH NOTES" }), "PINNED tab");
        Nav(admin, "news.admin.filter published");
        Check(RowTitles().Length == 4 && !RowTitles().Contains("EPSILON DRAFT"), "PUBLISHED tab");
        Nav(admin, "news.admin.search wipe");
        Check(RowTitles().SequenceEqual(new[] { "GAMMA RULES", "ALPHA WIPE NEWS" }), "search looks in titles and text");
        H.Console(admin, "news.admin.selectpage 0");
        Check(H.Texts(admin).Contains("2 SELECTED"), "SELECT PAGE only takes the rows that are listed");
        H.Console(admin, "news.admin.clearsel 0");
        Nav(admin, "news.admin.search zzz");
        Check(RowTitles().Length == 0 && H.Texts(admin).Contains("No posts match this filter.") && H.Commands(admin).Contains("news.admin.filter reset"), "nothing found: message and SHOW ALL");
        Nav(admin, "news.admin.filter reset");
        Check(RowTitles().Length == 5, "SHOW ALL clears the tab and the search");
        Nav(admin, "news.admin.sort");
        Check(H.Texts(admin).Contains("OLDEST FIRST") && RowTitles().First() == "ALPHA WIPE NEWS", "sort: oldest first");
        Nav(admin, "news.admin.sort");
        Check(H.Texts(admin).Contains("MOST READ") && RowTitles().First() == "ALPHA WIPE NEWS", "sort: most read");
        Nav(admin, "news.admin.sort");
        Check(H.Texts(admin).Contains("MOST LIKED") && RowTitles().First() == "BETA PATCH NOTES", "sort: most liked");
        Nav(admin, "news.admin.sort");
        Check(H.Texts(admin).Contains("NEWEST FIRST"), "sort cycles back");
        long adminMenu = IdOf(admin, "NewsBroadcasterUI");
        int grabsBefore = Sim.Ui(admin).CursorGrabs;
        H.Console(admin, "news.admin.themes");
        string backCommand = H.Commands(admin).First(c => c.StartsWith("news.admin.page"));
        Nav(admin, backCommand);
        Check(IdOf(admin, "NewsBroadcasterUI") == adminMenu && Sim.Ui(admin).CursorGrabs == grabsBefore, "BACK from the themes keeps the menu (no blink, mouse stays)");
        Nav(admin, "news.admin.filter drafts");
        H.Chat(admin, "news", "admin");
        Check(RowTitles().Length == 5, "/news admin opens the full list again");
        H.CfgSet("General.AnnouncementsPerPage", 8);
        Nav(admin, "news.admin.page 0");
        H.CfgSet("General.ArchiveThumbnails", false);
        Nav(admin, "news.admin.page 0");
        Check(RowTitles().Length == 5 && !CuiWarnings().Any(), "8 rows per page / no thumbnails draw without warnings");

        // ---------------------------------------------------------------
        Section("Editor autosave");
        Fresh();
        Post("Existing post", "x");
        H.Console(admin, "news.admin");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.cancel");
        Check(!Sim.DataFiles["NewsBroadcaster_Data"].Contains("Unfinished") && !H.Texts(admin).Contains("UNSAVED"), "an untouched editor leaves nothing behind");
        H.Console(admin, "news.admin.create");
        Check(H.Texts(admin).Contains("CHANGES ARE SAVED AUTOMATICALLY"), "the editor says that it autosaves");
        H.Console(admin, "news.editor.input title Unfinished business");
        H.Console(admin, "news.editor.input text Half a sentence and");
        H.Console(admin, "news.editor.settype alert");
        H.Console(admin, "news.editor.toggle poll");
        H.Console(admin, "news.editor.input poll2 Second option");
        H.Call("OnPlayerDisconnected", admin, "Timed out");
        Sim.Ui(admin).Elements.Clear();
        H.Call("OnServerSave");
        Check(Sim.DataFiles["NewsBroadcaster_Data"].Contains("Unfinished business"), "what was typed is in the data file after a disconnect");
        string withAutosave = Sim.DataFiles["NewsBroadcaster_Data"];

        Fresh(preBoot: () => { Sim.DataFiles["NewsBroadcaster_Data"] = withAutosave; });
        H.Console(admin, "news.admin");
        Check(H.Texts(admin).Contains("UNSAVED") && H.Texts(admin).Any(t => t.StartsWith("\"Unfinished business\"")) && H.Commands(admin).Contains("news.admin.resume"), "after a restart News Management offers to continue");
        Check(H.AnnByTitle("Unfinished business") == null, "...and nothing was published on its own");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.cancel");
        Check(H.Texts(admin).Contains("UNSAVED"), "opening and cancelling another editor does not throw the unsaved post away");
        H.Console(admin, "news.admin.resume");
        var resumed = H.Live(admin).SelectMany(e => e["components"].Children<JObject>()).Where(c => (string)c["type"] == "UnityEngine.UI.InputField").Select(c => (string)c["text"]).ToList();
        Check(resumed.Contains("Unfinished business") && resumed.Contains("Half a sentence and") && resumed.Contains("Second option"), "CONTINUE EDITING brings back title, text and poll options");
        H.Console(admin, "news.editor.input poll1 First option");
        H.Console(admin, "news.editor.save");
        var finished = H.AnnByTitle("Unfinished business");
        Check(finished != null && H.AGet(finished, "Type").ToString() == "Alert" && ((List<string>)H.AGet(finished, "PollOptions")).Count == 2, "...and it publishes like any other post");
        H.Call("OnServerSave");
        Check(!H.Texts(admin).Contains("UNSAVED") && JObject.Parse(Sim.DataFiles["NewsBroadcaster_Data"])["EditorAutosaves"].Count() == 0, "publishing removes the autosave");

        var existing = H.AnnByTitle("Existing post");
        H.Console(admin, $"news.admin.edit {H.Id(existing)}");
        H.Console(admin, "news.editor.input text Rewritten but never saved");
        H.Console(admin, "news.close");
        H.Console(null, $"news.delete {H.Id(existing)}");
        H.Console(admin, "news.admin");
        H.Console(admin, "news.admin.resume");
        Check(H.Texts(admin).Contains("CREATE NEW ANNOUNCEMENT") && H.Texts(admin).Contains("PUBLISH ANNOUNCEMENT"), "an unsaved edit of a post that was deleted carries on as a new post");
        H.Console(admin, "news.editor.cancel");
        Check(!H.Texts(admin).Contains("UNSAVED"), "CANCEL in the editor discards it");
        H.Console(admin, "news.admin.create");
        H.Console(admin, "news.editor.input title Throw me away");
        H.Console(admin, "news.close");
        H.Console(admin, "news.admin");
        H.Console(admin, "news.admin.discard");
        Check(!H.Texts(admin).Contains("UNSAVED") && LastChat(admin) == "Unsaved post discarded.", "DISCARD removes it");
        H.Console(player, "news.admin.resume");
        H.Console(player, "news.admin.discard");
        Nav(player, "news.admin.filter drafts");
        Check(!UiOpen(player), "players can't use the admin commands");

        Fresh(preBoot: () =>
        {
            Sim.DataFiles["NewsBroadcaster_Data"] = @"{ ""Announcements"": [], ""EditorAutosaves"": {
                ""76561198000000001"": { ""Draft"": { ""Title"": ""Ancient"" }, ""SavedAt"": 5 },
                ""76561198000000009"": { ""Draft"": { ""Title"": ""Yesterday"" }, ""SavedAt"": " + (DateTime.UtcNow.Ticks - TimeSpan.TicksPerDay) + @" } } }";
        });
        Sim.Advance(16);
        var keptAutosaves = JObject.Parse(Sim.DataFiles["NewsBroadcaster_Data"])["EditorAutosaves"];
        Check(keptAutosaves.Count() == 1 && keptAutosaves["76561198000000009"] != null, "autosaves nobody came back for (30 days) are dropped on load, newer ones stay");

        // ---------------------------------------------------------------
        Section("API for other plugins");
        Fresh();
        Sim.Permission.CreateGroup("vip", "VIP", 0);
        H.CfgSet("Discord.Enabled", true);
        H.CfgSet("Discord.WebhookUrl", "https://discord.com/api/webhooks/1/x");
        int apiHooks = HooksNamed("OnNewsBroadcast"), apiWeb = Sim.WebRequests.Count;
        string apiId = (string)H.Call("API_PostNews", "Airdrop event", "Starts in 10 minutes!", "Event");
        var apiPost = H.AnnByTitle("Airdrop event");
        Check(apiId != null && apiPost != null && H.Id(apiPost) == apiId && H.AGet(apiPost, "Type").ToString() == "Event" && (string)H.AGet(apiPost, "Author") == "SERVER NEWS", "API_PostNews publishes and returns the id");
        Check(ToastOpen(player) && HooksNamed("OnNewsBroadcast") == apiHooks + 1 && Sim.WebRequests.Count == apiWeb + 1, "...players are notified, the hook fires, Discord gets it");
        Check(H.Call("API_PostNews", "  ", "no title") == null, "no title, no post");
        H.Console(player, "news.close.notif");

        string quietId = (string)H.Call("API_CreateNews", new Dictionary<string, object>
        {
            ["title"] = "Vote: next event", ["text"] = "Pick one", ["type"] = "update", ["author"] = "EventManager", ["pinned"] = true,
            ["notify"] = false, ["discord"] = false, ["expireDays"] = 3, ["pollOptions"] = new List<string> { "Raid", "Race", " " }, ["pollHours"] = 12,
            ["countdownTo"] = DateTime.UtcNow.AddHours(5), ["countdownLabel"] = "Voting ends in"
        });
        var quiet = H.AnnByTitle("Vote: next event");
        Check(quietId != null && (bool)H.AGet(quiet, "Pinned") && (string)H.AGet(quiet, "Author") == "EventManager" && H.AGet(quiet, "Type").ToString() == "Update", "API_CreateNews: type, author, pinned");
        Check(((List<string>)H.AGet(quiet, "PollOptions")).SequenceEqual(new[] { "Raid", "Race" }) && (long)H.AGet(quiet, "PollEndsAt") > DateTime.UtcNow.Ticks && (long)H.AGet(quiet, "ExpiresAt") > DateTime.UtcNow.Ticks && (long)H.AGet(quiet, "CountdownTo") > DateTime.UtcNow.Ticks, "...poll, poll timer, auto-delete and countdown");
        Check(!ToastOpen(player) && Sim.WebRequests.Count == apiWeb + 1, "...notify=false / discord=false keep it quiet");

        string draftId = (string)H.Call("API_CreateNews", new Dictionary<string, object> { ["title"] = "Later", ["draft"] = true });
        Check((bool)H.AGet(H.AnnByTitle("Later"), "Draft") && HooksNamed("OnNewsBroadcast") == apiHooks + 2, "draft=true saves a draft (no broadcast)");
        Check(H.Call("API_CreateNews", new Dictionary<string, object> { ["title"] = "For nobody", ["group"] = "nosuchgroup" }) == null && H.AnnByTitle("For nobody") == null, "an unknown group is refused");
        Check(H.Call("API_CreateNews", new Dictionary<string, object> { ["title"] = "For VIPs", ["group"] = "vip", ["notify"] = false }) != null && (string)H.AGet(H.AnnByTitle("For VIPs"), "Group") == "vip", "a real group works");

        var apiData = (Dictionary<string, object>)H.Call("API_GetNews", apiId);
        Check(apiData != null && (string)apiData["title"] == "Airdrop event" && H.Call("API_GetNews", "nope") == null, "API_GetNews returns the hook payload");
        var apiList = (List<Dictionary<string, object>>)H.Call("API_GetNewsList");
        Check(apiList.Count == 3 && !apiList.Any(d => (string)d["title"] == "Later"), "API_GetNewsList: published posts only");
        Check((int)H.Call("API_GetUnreadCount", player) == 2 && (int)H.Call("API_GetUnreadCount", noperm) == 0, "API_GetUnreadCount counts what the player can see");
        Check((bool)H.Call("API_ShowNews", player, apiId) && H.Texts(player).Contains("AIRDROP EVENT"), "API_ShowNews opens the post");
        H.Console(player, "news.close");
        Check(!(bool)H.Call("API_ShowNews", player, draftId) && !(bool)H.Call("API_ShowNews", noperm, apiId) && !UiOpen(player), "...but not a draft, and not without permission");
        int deletedHooks = HooksNamed("OnNewsDeleted");
        Check((bool)H.Call("API_DeleteNews", apiId) && H.AnnByTitle("Airdrop event") == null && HooksNamed("OnNewsDeleted") == deletedHooks + 1 && !(bool)H.Call("API_DeleteNews", apiId), "API_DeleteNews");

        // ---------------------------------------------------------------
        Section("Garbage input: every command, every caller");
        Fresh();
        Sim.Permission.CreateGroup("vip", "VIP", 0);
        H.CfgSet("Rewards.EnableReadReward", true);
        H.CfgSet("Rewards.EnableLikeReward", true);
        var crashes = new List<string>();
        Action<BasePlayer, string> tryConsole = (who, line) =>
        {
            try { H.Console(who, line); Sim.Advance(0.5); }
            catch (Exception ex) { crashes.Add($"{who?.displayName ?? "console"}: {line} -> {ex.InnerException?.GetType().Name ?? ex.GetType().Name}: {(ex.InnerException ?? ex).Message.Split('\n')[0]}"); }
        };
        Action stock = () =>
        {
            // Keep a few posts of every kind around, whatever the commands before deleted.
            if (H.Announcements.Count >= 4) return;
            Post("Stock picture", "body", "Event", "https://img.example/stock.png");
            H.Console(null, "news.poll \"Stock poll\" \"x\" \"A|B|C\" Update");
            H.Call("API_CreateNews", new Dictionary<string, object> { ["title"] = "Stock countdown", ["countdownTo"] = DateTime.UtcNow.Ticks + TimeSpan.TicksPerHour, ["expireDays"] = 1, ["group"] = "vip", ["pinned"] = true });
            H.Call("API_CreateNews", new Dictionary<string, object> { ["title"] = "Stock draft", ["draft"] = true });
        };
        var commandNames = H.ConsoleCommands();
        string longText = new string('x', 5000);
        var junk = new List<Func<string>>
        {
            () => "", () => "x", () => "-1", () => "0", () => "1", () => "999999999", () => "99999999999999999999", () => "\"\"", () => "a b c d e f",
            () => H.Id(H.Ann(0)), () => H.Id(H.Ann(0)) + " -5", () => H.Id(H.Ann(0)) + " 99999", () => H.Id(H.Ann(H.Announcements.Count - 1)) + " archive",
            () => "title <color=red>rich</color> {0} %TIME_LEFT% \\n", () => "text " + longText, () => "poll1 A", () => "poll9 A", () => "cdtime 2d 4h", () => "cdtime never",
            () => "image not-a-link", () => "image https://img.example/a.png", () => "polls", () => "event", () => "reset", () => "sound", () => "audience", () => "expire", () => "polltimer", () => "poll", () => "pollclosed",
            () => "\"Title\" \"-\" \"Body\" Event", () => "\"Title\" \"Body\" \"A|B\"", () => "\"Title\" \"Body\" \"|\"", () => "Ocean 0", () => "Survivor", () => "Survivor 0", () => "Nobody 5"
        };
        int ran = 0;
        foreach (var make in junk)
        {
            foreach (string command in commandNames)
            {
                foreach (var who in new[] { null, noperm, player, admin })
                {
                    stock();
                    tryConsole(who, command + " " + make());
                    ran++;
                }
            }
            // Between rounds: put everybody on a different screen, so the in-place redraws are hit too.
            stock();
            tryConsole(admin, "news.admin.create");
            tryConsole(player, $"news.view {H.Id(H.AnnByTitle("Stock poll") ?? H.Ann(0))}");
            Sim.Advance(6);
        }
        Check(crashes.Count == 0, $"{ran} commands with junk arguments from the console, a stranger, a player and an admin: no exception" + (crashes.Count > 0 ? $" ({crashes.Count}): " + string.Join(" | ", crashes.Distinct().Take(6)) : ""));
        Check(!Sim.LogLines.Any(l => l.StartsWith("ERR")), "...and nothing in the error log" + string.Join(" | ", Sim.LogLines.Where(l => l.StartsWith("ERR")).Distinct().Take(3)));
        var junkWarnings = CuiWarnings();
        Check(junkWarnings.Count == 0, "...and no broken UI (unknown parents, duplicate names)" + (junkWarnings.Count > 0 ? ": " + string.Join(" | ", junkWarnings.Distinct().Take(5)) : ""));
        Check(!UiOpen(noperm) && !ToastOpen(noperm), "a player without permission never got a screen out of it");
        H.Call("OnPlayerDisconnected", admin, "bye"); H.Call("OnPlayerDisconnected", player, "bye"); H.Call("OnPlayerDisconnected", noperm, "bye");
        H.Call("Unload");
        Check(Sim.Timers.Count == 0, $"after everybody left and the plugin unloaded, no timer is left ({Sim.Timers.Count})");

        // ---------------------------------------------------------------
        Section("Config values nobody should set");
        var themeDefaults = new Func<object>(() => Activator.CreateInstance(H.AGet(H.Config, "Colors").GetType(), true));
        var sillyConfigs = new List<(string name, Action apply)>
        {
            ("AnnouncementsPerPage 0", () => H.CfgSet("General.AnnouncementsPerPage", 0)),
            ("AnnouncementsPerPage -5", () => H.CfgSet("General.AnnouncementsPerPage", -5)),
            ("AnnouncementsPerPage 60", () => H.CfgSet("General.AnnouncementsPerPage", 60)),
            ("MaxStoredAnnouncements 0", () => H.CfgSet("General.MaxStoredAnnouncements", 0)),
            ("MaxStoredAnnouncements -1", () => H.CfgSet("General.MaxStoredAnnouncements", -1)),
            ("AutoCloseSeconds 0, popups instead of toasts", () => { H.CfgSet("General.AutoCloseSeconds", 0); H.CfgSet("Notification.Enabled", false); }),
            ("AutoCloseSeconds -10, popups instead of toasts", () => { H.CfgSet("General.AutoCloseSeconds", -10); H.CfgSet("Notification.Enabled", false); }),
            ("Notification.Duration 0", () => H.CfgSet("Notification.Duration", 0)),
            ("Notification.Duration -3", () => H.CfgSet("Notification.Duration", -3)),
            ("ReadDelaySeconds -5 with rewards", () => { H.CfgSet("Rewards.ReadDelaySeconds", -5); H.CfgSet("Rewards.EnableReadReward", true); H.CfgSet("Rewards.EnableLikeReward", true); }),
            ("reward bundles null", () => { H.CfgSet("Rewards.ReadRewards", null); H.CfgSet("Rewards.LikeRewards", null); H.CfgSet("Rewards.EnableReadReward", true); H.CfgSet("Rewards.EnableLikeReward", true); }),
            ("reward item that does not exist", () => { ((System.Collections.IList)H.AGet(H.CfgPath("Rewards.ReadRewards"), "Items")).Clear(); H.ASet(H.CfgPath("Rewards.ReadRewards"), "Items", null); H.CfgSet("Rewards.EnableReadReward", true); }),
            ("reward labels null", () => { H.CfgSet("Rewards.PointsLabel", null); H.CfgSet("Rewards.CurrencyLabel", null); H.CfgSet("Rewards.EnableReadReward", true); }),
            ("ServerName null", () => H.CfgSet("General.ServerName", null)),
            ("PopupImageFrame null", () => H.CfgSet("General.PopupImageFrame", null)),
            ("Notification.Position null", () => H.CfgSet("Notification.Position", null)),
            ("NotificationSound null", () => H.CfgSet("Notification.NotificationSound", null)),
            ("SelectedTheme null", () => H.ASet(H.Config, "SelectedTheme", null)),
            ("SelectedTheme that does not exist", () => H.ASet(H.Config, "SelectedTheme", "Nope")),
            ("no themes at all", () => ((System.Collections.IDictionary)H.AGet(H.Config, "Themes")).Clear()),
            ("theme colours null or garbage", () => { var t = themeDefaults(); H.ASet(t, "PanelBg", null); H.ASet(t, "ButtonPrimary", "blue"); H.ASet(t, "TextTitle", ""); H.ASet(t, "ContentBg", "1 2"); ((System.Collections.IDictionary)H.AGet(H.Config, "Themes"))["Broken"] = t; H.ASet(H.Config, "SelectedTheme", "Broken"); }),
            ("Discord on with empty names", () => { H.CfgSet("Discord.Enabled", true); H.CfgSet("Discord.WebhookUrl", "https://discord.com/api/webhooks/1/x"); H.CfgSet("Discord.BotName", null); H.CfgSet("Discord.AvatarUrl", null); H.CfgSet("Discord.RoleMention", null); }),
            ("Notify plugin wanted but not installed", () => H.CfgSet("Notification.UseNotifyPlugin", true)),
            ("view permission not required", () => H.CfgSet("General.RequireViewPermission", false)),
        };
        foreach (var silly in sillyConfigs)
        {
            string failed = null;
            try
            {
                Fresh();
                Sim.Permission.CreateGroup("vip", "VIP", 0);
                silly.apply();
                Post("Tour picture", "body text\\nsecond line", "Event", "https://img.example/tour.png");
                H.Console(null, "news.poll \"Tour poll\" \"x\" \"A|B|C\" Update");
                H.Call("API_CreateNews", new Dictionary<string, object> { ["title"] = "Tour countdown", ["countdownTo"] = DateTime.UtcNow.Ticks + TimeSpan.TicksPerHour, ["expireDays"] = 1, ["pollOptions"] = new[] { "Y", "N" }, ["pollHours"] = 1, ["pinned"] = true });
                for (int i = 0; i < 4; i++) Post("Tour filler " + i, "x");
                Sim.Advance(1);
                var joiner2 = H.MakePlayer(76561198000000777, "Joiner");
                H.Call("OnPlayerSleepEnded", joiner2);
                Sim.Advance(3);
                foreach (var who in new[] { player, admin, joiner2 })
                {
                    H.Chat(who, "news"); Sim.Advance(1);
                    foreach (string line in new[] { "news.page 1", "news.page -3", "news.page 999", "news.filter event", "news.search tour", "news.search.clear", "news.markread", "news.settings", "news.settings.toggle sound", "news.confirm.close" })
                    { H.Console(who, line); Sim.Advance(0.5); }
                    foreach (var post in H.Announcements.Cast<object>().ToList())
                    {
                        H.Console(who, $"news.view {H.Id(post)} archive"); Sim.Advance(0.5);
                        H.Console(who, $"news.like {H.Id(post)}");
                        H.Console(who, $"news.vote {H.Id(post)} 1");
                        H.Console(who, $"news.countdown {H.Id(post)}");
                        Sim.Advance(7);
                    }
                    H.Chat(who, "news", "latest"); H.Chat(who, "news", "read"); H.Chat(who, "news", "help"); H.Chat(who, "news", "settings");
                    H.Console(who, "news.close");
                }
                H.Chat(admin, "news", "admin"); Sim.Advance(1);
                foreach (string line in new[] { "news.admin.page 1", "news.admin.page -2", "news.admin.filter polls", "news.admin.sort", "news.admin.sort", "news.admin.sort", "news.admin.search tour", "news.admin.filter reset", "news.admin.selectpage 0", "news.admin.bulkpin 1 0", "news.admin.bulkdelconfirm 0", "news.confirm.close", "news.admin.clearsel 0", "news.admin.themes", "news.admin.themes 5", "news.admin.settheme Ocean 0", "news.admin.page 0" })
                { H.Console(admin, line); Sim.Advance(0.5); }
                H.Console(admin, "news.admin.create");
                foreach (string line in new[] { "news.editor.input title Written in the tour", "news.editor.input text Some body", "news.editor.input image https://img.example/e.png", "news.editor.toggle poll", "news.editor.input poll1 One", "news.editor.input poll2 Two", "news.editor.toggle polltimer", "news.editor.toggle expire", "news.editor.toggle audience", "news.editor.toggle pin", "news.editor.input cdtime 2h", "news.editor.settype Alert", "news.editor.preview", "news.editor.back", "news.editor.savedraft" })
                    H.Console(admin, line);
                var tourDraft = H.AnnByTitle("Written in the tour");
                if (tourDraft != null)
                {
                    H.Console(admin, $"news.admin.edit {H.Id(tourDraft)}");
                    H.Console(admin, "news.editor.save");
                    H.Console(admin, $"news.admin.copy {H.Id(H.Ann(0))}");
                    H.Console(admin, "news.editor.cancel");
                    H.Console(admin, $"news.admin.resend {H.Id(H.Ann(0))} 0");
                    H.Console(admin, $"news.admin.togglepin {H.Id(H.Ann(0))} 0");
                    H.Console(admin, $"news.admin.delconfirm {H.Id(H.Ann(0))} 0");
                    H.Console(admin, $"news.admin.del {H.Id(H.Ann(0))} 0");
                }
                H.Console(null, "news.list"); H.Console(null, "news.trigger Survivor"); H.Console(null, "news.resend 0"); H.Console(null, "news.delete 0");
                foreach (var post in H.Announcements.Cast<object>()) { H.ASet(post, "ExpiresAt", DateTime.UtcNow.Ticks - 1); H.ASet(post, "PollEndsAt", DateTime.UtcNow.Ticks - 1); }
                H.Call("ScheduleNextCheck");
                Sim.Advance(30);
                H.Call("OnServerSave");
                H.Call("OnPlayerDisconnected", joiner2, "bye");
                H.Call("Unload");
                var errors = Sim.LogLines.Where(l => l.StartsWith("ERR")).ToList();
                var uiWarnings = CuiWarnings();
                if (errors.Count > 0) failed = "error log: " + errors[0];
                else if (uiWarnings.Count > 0) failed = "broken UI: " + uiWarnings[0];
            }
            catch (Exception ex) { failed = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message.Split('\n')[0] + " @ " + ((ex.InnerException ?? ex).StackTrace ?? "").Split('\n').FirstOrDefault(l => l.Contains("NewsBroadcaster"))?.Trim(); }
            Check(failed == null, $"{silly.name}: every screen and command still works" + (failed != null ? " -> " + failed : ""));
        }
        Fresh();

        // ---------------------------------------------------------------
        Section("Global checks");
        var warns = CuiWarnings();
        Check(warns.Count == 0, "no CUI warnings (unknown parents / duplicate names)" + (warns.Count > 0 ? ": " + string.Join(" | ", warns.Distinct().Take(5)) : ""));
        Check(Sim.MissingLangKeys.Count == 0, "no missing lang keys" + (Sim.MissingLangKeys.Count > 0 ? ": " + string.Join(", ", Sim.MissingLangKeys) : ""));
        Check(!Sim.LogLines.Any(l => l.StartsWith("ERR")), "no errors logged");
        Check(Facepunch.Pool.Outstanding == 0, $"every pooled list was given back ({Facepunch.Pool.Outstanding} outstanding)");
        string pluginSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NewsBroadcaster.cs"));
        Check(!pluginSource.Contains("System.Linq") && !pluginSource.Contains(".ToList()"), "the plugin does not use LINQ");

        Console.WriteLine($"\n{pass} passed, {fail} failed");
        foreach (var f in failures) Console.WriteLine("  - " + f);
        return fail == 0 ? 0 : 1;
    }
}
