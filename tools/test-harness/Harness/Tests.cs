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
        Section("Config migration from 1.2.0 (incl. legacy reward arrays)");
        Fresh(preBoot: () =>
        {
            Sim.ConfigFiles["config:NewsBroadcaster"] = @"{
              ""General"": { ""AutoCloseSeconds"": 20, ""EnableAutoClose"": true, ""ShowNewsOnConnect"": true, ""ServerName"": ""MY SERVER"", ""AnnouncementsPerPage"": 5, ""MaxStoredAnnouncements"": 50 },
              ""Notification"": { ""Enabled"": true, ""UseNotifyPlugin"": false, ""NotifyType"": 0, ""Position"": ""Left"", ""Duration"": 8, ""NotificationSound"": ""x.prefab"" },
              ""Discord"": { ""Enabled"": false, ""WebhookUrl"": """", ""BotName"": ""Server News"", ""RoleMention"": """" },
              ""Rewards"": { ""EnableReadReward"": true, ""ReadDelaySeconds"": 5, ""ReadRewards"": [ { ""Shortname"": ""wood"", ""Amount"": 100, ""SkinId"": 0 } ], ""EnableLikeReward"": false, ""LikeRewards"": { ""Items"": [], ""Points"": 0, ""Currency"": 0.0 }, ""NotifyOnReward"": true, ""PointsLabel"": ""RP"", ""CurrencyLabel"": ""coins"" },
              ""SelectedTheme"": ""Ocean"",
              ""Themes"": { ""Default"": { ""PanelBg"": ""0.1 0.1 0.1 1"", ""HeaderBg"": ""0 0 0 1"", ""ContentBg"": ""0 0 0 1"", ""ButtonPrimary"": ""1 0 0 1"", ""ButtonSecondary"": ""0 0 0 1"", ""TextTitle"": ""1 1 1 1"", ""TextNormal"": ""1 1 1 1"", ""TextMuted"": ""1 1 1 1"" },
                            ""Ocean"": { ""PanelBg"": ""0.05 0.09 0.12 0.97"", ""HeaderBg"": ""0.02 0.05 0.07 0.55"", ""ContentBg"": ""0.08 0.13 0.17 0.55"", ""ButtonPrimary"": ""0.20 0.78 0.95 0.95"", ""ButtonSecondary"": ""0.10 0.20 0.26 0.85"", ""TextTitle"": ""0.95 0.98 1.00 1.0"", ""TextNormal"": ""0.82 0.90 0.94 1.0"", ""TextMuted"": ""0.48 0.62 0.72 1.0"" } }
            }";
        });
        cfg = JObject.Parse(Sim.ConfigFiles["config:NewsBroadcaster"]);
        Check((string)cfg["SelectedTheme"] == "Ocean" && cfg["Themes"].Count() == 2, "existing themes + selection kept");
        Check((string)cfg["General"]["ServerName"] == "MY SERVER" && (int)cfg["General"]["AutoCloseSeconds"] == 20, "existing values kept");
        Check(cfg["Rewards"]["ReadRewards"]["Items"][0]["Shortname"]?.ToString() == "wood", "legacy reward array migrated to bundle");
        Check(cfg["General"]["UnreadReminderOnConnect"] != null && cfg["Discord"]["AvatarUrl"] != null, "new keys added on upgrade");

        // ---------------------------------------------------------------
        Section("Data migration from 1.2.0 data file");
        Fresh(preBoot: () =>
        {
            Sim.DataFiles["NewsBroadcaster_Data"] = @"{
              ""Announcements"": [
                { ""Id"": ""a1"", ""Title"": ""Old"", ""ImageUrl"": """", ""Text"": ""old text"", ""Date"": ""2026-01-01 10:00"", ""Author"": ""x"", ""Type"": 0, ""Timestamp"": 100, ""Pinned"": false, ""LikedPlayers"": [1,2], ""ReadByPlayers"": null, ""ReadRewardedPlayers"": [5], ""LikeRewardedPlayers"": null },
                { ""Id"": ""a1"", ""Title"": ""Newer"", ""ImageUrl"": """", ""Text"": ""visit https://example.com now"", ""Date"": ""2026-02-01 10:00"", ""Author"": ""x"", ""Type"": 3, ""Timestamp"": 200, ""Pinned"": true },
                null
              ],
              ""LastSeenNews"": { ""76561198000000002"": 200 }
            }";
        });
        Check(H.Announcements.Count == 2, "null record dropped");
        Check((string)H.AGet(H.Ann(0), "Title") == "Newer", "list re-sorted newest first");
        Check(H.Id(H.Ann(0)) != H.Id(H.Ann(1)), "duplicate ids repaired");
        Check(Set(H.Ann(1), "ReadByPlayers").Contains(5) && Set(H.Ann(1), "LikeRewardedPlayers") != null, "null sets repaired (reads seeded from rewards)");
        Check(((string)H.AGet(H.Ann(0), "Text")).Contains("https://example.com"), "links in stored posts are not stripped on load");
        Check(H.AGet(H.Ann(0), "PollOptions") != null && H.AGet(H.Ann(1), "PollVotes") != null, "old posts get empty poll fields");

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
        H.Console(admin, "news.editor.input text Hello\\nworld");
        Check(Sim.Ui(admin).AddCalls == addsBefore, "typing in a field does not rebuild the editor");
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
        H.Console(admin, "news.admin.page 1");
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

        // ---------------------------------------------------------------
        Section("Archive, unread badges, popup navigation");
        Fresh();
        for (int i = 1; i <= 7; i++) Post("News " + i);
        H.Chat(player, "news");
        Check(H.Texts(player).Contains("7 UNREAD") && H.Texts(player).Count(t => t == "NEW") == 5, "unread counter + NEW badges");
        H.Console(player, "news.page 1");
        Check(H.Texts(player).Contains("PAGE 2 / 2"), "archive page 2");
        var n2 = H.AnnByTitle("News 2");
        H.Console(player, $"news.view {H.Id(n2)} archive");
        Check(H.Commands(player).Contains("news.page 1") && H.Texts(player).Contains("< BACK TO ARCHIVE"), "back-to-archive returns to the same page");
        Check(H.Texts(player).Contains("6 / 7"), "position counter");
        Check(H.Commands(player).Contains($"news.view {H.Id(H.AnnByTitle("News 1"))} archive") && H.Commands(player).Contains($"news.view {H.Id(H.AnnByTitle("News 3"))} archive"), "prev/next navigation keeps origin");
        Sim.Advance(6);
        H.Console(player, "news.page 1");
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
        H.Call("OnPlayerDisconnected", admin, "bye");
        H.Call("OnPlayerDisconnected", player, "bye");
        bool clean = true;
        foreach (var f in new[] { "editors", "playersWithUiOpen", "adminSelections", "openPopups", "archivePages", "remindedPlayers", "readRewardTimers", "autoCloseTimers", "notificationTimers", "likeCooldowns", "voteCooldowns", "currentScreens", "toastNames" })
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
        Check(H.Texts(admin).Any(t => t != null && t.Contains("2 VOTES")) && H.Texts(admin).Any(t => t != null && t.StartsWith("2 POSTS")), "admin row shows votes; header stats line");
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
        var rows = arch.Where(e => ((string)e["name"]).StartsWith("NewsBroadcasterUI.List.") && (string)e["parent"] == "NewsBroadcasterUI.List").ToList();
        Check(rows.Count == 2 && Fade(rows[0]) > 0 && Fade(rows[1]) > Fade(rows[0]), "archive rows cascade (later rows fade slower)");
        H.Console(player, $"news.view {H.Id(Ann0)} archive");
        var pop = H.Live(player).ToList();
        Check(Fade(pop.First(e => (string)e["name"] == "NewsBroadcasterUI")) == 0f, "switching screens does not re-fade the background (no blink)");
        Check(Fade(pop.First(e => (string)e["name"] == "NewsBroadcasterUI.Main.TitleText")) > Fade(pop.First(e => (string)e["name"] == "NewsBroadcasterUI.Main")), "popup reveals panel first, then title");
        H.Console(admin, "news.close.notif");
        H.Console(admin, "news.admin");
        Check(H.Live(admin).Any(e => Fade(e) > 0f), "admin list animates on open");
        H.Console(admin, $"news.admin.toggleselect {H.Id(Ann0)} 0");
        Check(!H.Live(admin).Any(e => Fade(e) > 0f), "admin refresh (select) is instant");
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
        var barHolders = H.Live(player).Where(e => (string)e["parent"] == "NewsBroadcasterUI.Poll.Opt1").Select(e => (string)e["name"]).ToHashSet();
        Check(H.Live(player).Any(e => barHolders.Contains((string)e["parent"]) && Fade(e) == 0.5f), "poll result bars fade in");

        // ---------------------------------------------------------------
        Section("Rounded corners");
        Fresh();
        Check(FileStorage.server.Files.Count == 8, "8 corner textures stored in FileStorage");
        Check(FileStorage.server.Files.Values.All(b => b.Length > 100 && b[0] == 137 && b[1] == 80 && b[2] == 78 && b[3] == 71), "corner textures are PNG files");
        var cornerIds = FileStorage.server.Files.Keys.Select(k => k.ToString()).ToHashSet();
        Post("Round", "x", "Info", "https://img.example/wipe-day.png");
        H.Console(player, $"news.view {H.Id(Ann0)}");
        var cornerPieces = H.Live(player).Where(e => e["components"].Any(cc => (string)cc["type"] == "UnityEngine.UI.RawImage" && cornerIds.Contains((string)cc["png"]))).ToList();
        Check(cornerPieces.Count >= 28 && cornerPieces.Count % 4 == 0, $"popup uses rounded corner pieces ({cornerPieces.Count})");
        // In game a translucent corner texture is blended differently from a translucent panel (light squares),
        // so every corner piece must be opaque: translucent colours are blended onto the surface below.
        Func<JObject, string> rawColor = e => (string)e["components"].First(cc => (string)cc["type"] == "UnityEngine.UI.RawImage")["color"];
        Check(cornerPieces.All(e => Alpha(rawColor(e)) >= 0.999f), "every corner piece is opaque");
        var pieceColors = cornerPieces.Select(rawColor).Distinct().ToList();
        Check(pieceColors.Count >= 4, $"translucent buttons and tags are blended to their own opaque shades ({pieceColors.Count} colours)");
        // Shadows sit over the game view (no known colour below): plain stepped panels, no corner textures.
        var black = H.Live(player).Where(e => e["components"].Any(cc => (string)cc["type"] == "UnityEngine.UI.Image" && ((string)cc["color"] ?? "").StartsWith("0 0 0 0.") && (string)cc["color"] != "0 0 0 0")).ToList();
        Check(black.Count >= 20, $"shadow is drawn with stepped panels ({black.Count})");
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
        // The like button is redrawn on its own: it still knows the popup colour below it.
        H.Console(player, $"news.like {H.Id(Ann0)}");
        var likePieces = H.Live(player).Where(e => cornerIds.Contains((string)e["components"].FirstOrDefault(cc => (string)cc["type"] == "UnityEngine.UI.RawImage")?["png"] ?? "")).ToList();
        Check(likePieces.Count == cornerPieces.Count && likePieces.All(e => Alpha(rawColor(e)) >= 0.999f), "like button redrawn in place keeps opaque corners");
        Check(H.Live(player).Count() < 260, $"popup stays a reasonable size ({H.Live(player).Count()} elements)");
        H.Chat(admin, "news", "admin");
        Check(H.Live(admin).Count() < 600, $"admin list element count ok ({H.Live(admin).Count()})");
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
        Section("Global checks");
        var warns = CuiWarnings();
        Check(warns.Count == 0, "no CUI warnings (unknown parents / duplicate names)" + (warns.Count > 0 ? ": " + string.Join(" | ", warns.Distinct().Take(5)) : ""));
        Check(Sim.MissingLangKeys.Count == 0, "no missing lang keys" + (Sim.MissingLangKeys.Count > 0 ? ": " + string.Join(", ", Sim.MissingLangKeys) : ""));
        Check(!Sim.LogLines.Any(l => l.StartsWith("ERR")), "no errors logged");

        Console.WriteLine($"\n{pass} passed, {fail} failed");
        foreach (var f in failures) Console.WriteLine("  - " + f);
        return fail == 0 ? 0 : 1;
    }
}
