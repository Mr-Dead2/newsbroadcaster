using HarnessRuntime;

var mode = args.Length > 0 ? args[0] : "previews";
var outDir = args.Length > 1 ? args[1] : "out";

switch (mode)
{
    case "previews": Previews.Run(outDir); break;
    case "tests": Environment.ExitCode = Tests.Run(); break;
    default: Console.WriteLine("modes: previews <dir> | tests"); break;
}

static class Sample
{
    public const string WipeImg = "https://img.example/wipe-day.png";
    public const string PatchImg = "https://img.example/patch-notes.png";

    public static readonly (string title, string img, string body, string type, string date)[] Posts =
    {
        ("Welcome to Frontier Rust", "", "Welcome survivor! Please read the rules with /rules and join our Discord for support.\\n\\nGroup limit: 4  •  Wipe: every Thursday 19:00", "Info", "2026-10-01 12:00"),
        ("Discord Giveaway Winners", "", "Congrats to the 3 winners of last week's skin giveaway! Open a ticket on Discord to claim your prize.", "Info", "2026-10-28 17:10"),
        ("Cheater Ban Wave", "", "We banned 14 accounts for cheating this week. Report suspicious players with F7 - every report is reviewed by an admin.", "Alert", "2026-11-01 20:45"),
        ("Patch 2.4 - Balance Changes", PatchImg, "Patch 2.4 is live.\\n\\n• Furnace smelt speed x2\\n• Recycler time reduced to 5s\\n• Custom loot tables updated for military crates\\n\\nFull changelog on our Discord.", "Update", "2026-11-03 16:00"),
        ("Server Maintenance Tonight", "", "The server will restart at 23:00 for a hardware upgrade. Expected downtime is about 15 minutes. Your bases and items are safe.", "Warning", "2026-11-04 21:30"),
        ("Wipe Day - Fresh Map Is Live", WipeImg,
            "The map has been wiped and a brand-new 4250 procedural map is live!\\n\\nWhat's new this wipe:\\n• Blueprints have been wiped\\n• New custom monument next to the Launch Site\\n• Scrap from barrels increased (x2)\\n• Airdrop event every Saturday at 20:00\\n\\nReminder: the group limit is 4 players and alliances count towards the limit. Raiding offline bases is allowed after 18:00 on Friday.\\n\\nGood luck out there and see you on the beach!",
            "Event", "2026-11-05 19:00"),
    };
}

static class Previews
{
    public static void Run(string outDir)
    {
        Sim.ResetAll();
        H.Boot();
        var admin = H.MakePlayer(76561198000000001, "DEDA", admin: true);
        var player = H.MakePlayer(76561198000000002, "Survivor", admin: false);

        foreach (var post in Sample.Posts)
            H.Console(null, $"news.show \"{post.title}\" \"{(string.IsNullOrEmpty(post.img) ? "-" : post.img)}\" \"{post.body}\" {post.type}");

        // Realistic dates/timestamps (oldest first was inserted first => index 0 newest).
        var baseTicks = new DateTime(2026, 11, 5, 19, 0, 0, DateTimeKind.Utc).Ticks;
        foreach (var post in Sample.Posts)
        {
            var a = H.AnnByTitle(post.title);
            H.ASet(a, "Date", post.date);
            var dt = DateTime.ParseExact(post.date, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            H.ASet(a, "Timestamp", dt.Ticks);
        }
        H.Console(null, "news.poll \"Next Wipe: Pick The Map Size\" \"Vote for the map size of the next wipe! The winning size is announced on Thursday.\" \"3500 - small and fast|4250 - the classic|4500 - big, more monuments|Custom map\" Event");
        var poll = H.AnnByTitle("Next Wipe: Pick The Map Size");
        H.ASet(poll, "Date", "2026-11-05 20:30");
        H.ASet(poll, "Timestamp", new DateTime(2026, 11, 5, 20, 30, 0).Ticks);
        var pollVotes = (Dictionary<ulong, int>)H.AGet(poll, "PollVotes");
        int[] dist = { 9, 21, 11, 3 };
        ulong voter = 76561198200000000;
        for (int o = 0; o < dist.Length; o++) for (int k = 0; k < dist[o]; k++) pollVotes[voter++] = o;
        var welcome = H.AnnByTitle("Welcome to Frontier Rust");
        var wipe = H.AnnByTitle("Wipe Day - Fresh Map Is Live");
        var maint = H.AnnByTitle("Server Maintenance Tonight");
        var patch = H.AnnByTitle("Patch 2.4 - Balance Changes");
        H.Console(admin, $"news.admin.togglepin {H.Id(welcome)} 0");

        // Likes from a few players
        var liked = (HashSet<ulong>)H.AGet(wipe, "LikedPlayers");
        for (ulong i = 0; i < 23; i++) liked.Add(76561198100000000 + i);
        var likedP = (HashSet<ulong>)H.AGet(patch, "LikedPlayers");
        for (ulong i = 0; i < 9; i++) likedP.Add(76561198100000000 + i);
        var readW = (HashSet<ulong>)H.AGet(wipe, "ReadByPlayers");
        for (ulong i = 0; i < 61; i++) readW.Add(76561198100000000 + i);

        // The player has read two older posts.
        foreach (var a in new[] { welcome, H.AnnByTitle("Discord Giveaway Winners") })
            ((HashSet<ulong>)H.AGet(a, "ReadByPlayers")).Add(player.userID);

        H.Console(player, "news.close");
        H.Console(admin, "news.close");
        Sim.Advance(30);

        // 1) Toast notification for a fresh broadcast
        H.CallIfExists("ShowNotification", player, wipe);
        H.Save(player, outDir, "01_notification");
        H.Console(player, "news.close.notif");

        // 2) Popup with image
        H.Console(player, $"news.view {H.Id(wipe)}");
        H.Save(player, outDir, "02_popup_image");

        // 3) Popup without image
        H.Console(player, $"news.view {H.Id(maint)}");
        H.Save(player, outDir, "03_popup_text");

        // 4) Archive
        H.Console(player, "news.close");
        H.Chat(player, "news");
        H.Save(player, outDir, "04_archive");
        H.Console(player, "news.close");

        // 5) Admin list with selection
        H.Console(admin, "news.admin");
        H.Console(admin, $"news.admin.toggleselect {H.Id(maint)} 0");
        H.Console(admin, $"news.admin.toggleselect {H.Id(patch)} 0");
        H.Save(admin, outDir, "05_admin");

        // 6) Delete confirmation
        H.Console(admin, $"news.admin.delconfirm {H.Id(maint)}");
        H.Save(admin, outDir, "06_delete_confirm");
        H.Console(admin, "news.confirm.close");
        H.Console(admin, "news.admin.clearsel 0");

        // 7) Editor (editing an existing post)
        H.Console(admin, $"news.admin.edit {H.Id(wipe)}");
        H.Save(admin, outDir, "07_editor");
        H.Console(admin, "news.editor.cancel");

        // 7b) Editor for a brand-new post (newer versions show option toggles incl. Discord)
        if (H.HasConsole("news.editor.settype"))
        {
            H.CfgSet("Discord.Enabled", true);
            H.CfgSet("Discord.WebhookUrl", "https://discord.com/api/webhooks/1/x");
            H.Console(admin, "news.admin.create");
            H.Console(admin, "news.editor.input title Raid Base Event This Saturday");
            H.Console(admin, "news.editor.input text Join the community raid event this Saturday at 20:00!\\n\\nTeams of up to 4. The first team to break into the event base wins 5000 scrap.");
            H.Console(admin, "news.editor.settype event");
            H.Console(admin, "news.editor.toggle pin");
            H.Save(admin, outDir, "07b_editor_new");
            H.Console(admin, "news.editor.cancel");
            H.CfgSet("Discord.Enabled", false);
        }

        // 7c) Editor with a poll
        if (H.HasConsole("news.vote"))
        {
            H.Console(admin, "news.admin.create");
            H.Console(admin, "news.editor.input title Next Wipe: Pick The Map Size");
            H.Console(admin, "news.editor.input text Vote for the map size of the next wipe! The winning size is announced on Thursday.");
            H.Console(admin, "news.editor.settype event");
            H.Console(admin, "news.editor.toggle poll");
            H.Console(admin, "news.editor.input poll1 3500 - small and fast");
            H.Console(admin, "news.editor.input poll2 4250 - the classic");
            H.Console(admin, "news.editor.input poll3 4500 - big, more monuments");
            H.Console(admin, "news.editor.input poll4 Custom map");
            H.Console(admin, "news.editor.toggle notify");
            H.Console(admin, "news.editor.toggle notify");
            H.Save(admin, outDir, "07c_editor_poll");
            H.Console(admin, "news.editor.cancel");
        }

        // 8) Themes
        H.Console(admin, "news.admin.themes");
        H.Save(admin, outDir, "08_themes");

        // Extra screens that only exist in newer versions
        if (H.HasConsole("news.editor.preview"))
        {
            H.Console(admin, $"news.admin.edit {H.Id(wipe)}");
            H.Console(admin, "news.editor.preview");
            H.Save(admin, outDir, "09_editor_preview");
            H.Console(admin, "news.editor.cancel");
        }

        if (H.HasConsole("news.vote"))
        {
            H.Console(admin, "news.close");
            H.Console(player, $"news.view {H.Id(poll)}");
            H.Save(player, outDir, "10a_popup_poll_vote");
            H.Console(player, $"news.vote {H.Id(poll)} 1");
            H.Save(player, outDir, "10b_popup_poll_results");
        }

        Console.WriteLine($"previews done. data writes={Sim.DataWrites} hooks={Sim.Hooks.Count} webreq={Sim.WebRequests.Count}");
        if (Sim.MissingLangKeys.Count > 0) Console.WriteLine("!! missing lang keys: " + string.Join(", ", Sim.MissingLangKeys));
    }
}
