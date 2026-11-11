// Harness-only runtime: emulates the Rust client's CUI tree, chat, timers, data files, hooks, web requests.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HarnessRuntime
{
    public static class Sim
    {
        public class WebRequestRecord { public string Url; public string Body; public Oxide.Core.Libraries.RequestMethod Method; public Dictionary<string, string> Headers; }

        public class ClientUi
        {
            public readonly List<JObject> Elements = new List<JObject>();   // live elements in creation order
            public readonly List<string> Warnings = new List<string>();
            public int AddCalls, DestroyCalls;
            // How often the mouse cursor was grabbed afresh: Rust recentres the cursor each time an element
            // with NeedsCursor appears while no other one is open (what players see as the mouse "jumping").
            public int CursorGrabs;
        }

        public static readonly JsonSerializerSettings OxideJsonSettings = new JsonSerializerSettings();
        public static readonly Dictionary<string, string> DataFiles = new Dictionary<string, string>();
        public static readonly Dictionary<string, string> ConfigFiles = new Dictionary<string, string>();
        public static int DataWrites, ConfigWrites, ElementIds;
        public static readonly List<KeyValuePair<string, object[]>> Hooks = new List<KeyValuePair<string, object[]>>();
        public static readonly List<WebRequestRecord> WebRequests = new List<WebRequestRecord>();
        public static Func<string, string, (int, string)> WebResponder = (u, b) => (204, "");
        public static readonly List<Oxide.Plugins.Timer> Timers = new List<Oxide.Plugins.Timer>();
        public static readonly List<Action> NextTicks = new List<Action>();
        public static readonly Oxide.Core.Libraries.Permission Permission = new Oxide.Core.Libraries.Permission();
        public static readonly Oxide.Core.Libraries.Lang Lang = new Oxide.Core.Libraries.Lang();
        public static readonly HashSet<string> MissingLangKeys = new HashSet<string>();
        public static readonly List<string> LogLines = new List<string>();
        public static readonly Dictionary<ulong, List<string>> ChatLog = new Dictionary<ulong, List<string>>();
        public static readonly List<string> ConsoleReplies = new List<string>();
        public static readonly Dictionary<ulong, ClientUi> Clients = new Dictionary<ulong, ClientUi>();
        public static bool Quiet;
        // Benchmarks: the plugin still builds and serialises its UI, the simulated client ignores it.
        public static bool SkipClient;

        public static readonly HashSet<string> RootLayers = new HashSet<string> { "Overlay", "Hud", "Hud.Menu", "Under", "Overall", "OverlayNonScaled", "HudNonScaled", "UnderNonScaled", "Inventory" };

        public static void Log(string line) { LogLines.Add(line); if (!Quiet) Console.WriteLine("  | " + line); }
        public static void Chat(BasePlayer p, string msg)
        {
            if (!ChatLog.TryGetValue(p.userID, out var l)) ChatLog[p.userID] = l = new List<string>();
            l.Add(msg);
            if (!Quiet) Console.WriteLine($"  [chat->{p.displayName}] {msg}");
        }
        public static void ConsoleReply(ConsoleSystem.Arg arg, string msg) { ConsoleReplies.Add(msg); if (!Quiet) Console.WriteLine($"  [reply] {msg}"); }

        public static ClientUi Ui(BasePlayer p)
        {
            if (!Clients.TryGetValue(p.userID, out var c)) Clients[p.userID] = c = new ClientUi();
            return c;
        }

        public static void ClientAddUi(BasePlayer player, string json)
        {
            var ui = Ui(player);
            ui.AddCalls++;
            var arr = JArray.Parse(json);
            foreach (JObject el in arr)
            {
                string name = (string)el["name"] ?? "AddUI CreatedPanel";
                string parent = (string)el["parent"] ?? "Hud";
                string destroy = (string)el["destroyUi"];
                if (!string.IsNullOrEmpty(destroy)) ClientDestroyUi(player, destroy, countCall: false);
                bool update = el["update"] != null && (bool)el["update"];
                var existing = ui.Elements.FirstOrDefault(e => (string)e["name"] == name);
                if (update)
                {
                    if (existing == null) { ui.Warnings.Add($"update for missing element '{name}'"); continue; }
                    existing["components"] = MergeComponents((JArray)existing["components"], (JArray)el["components"]);
                    continue;
                }
                if (!RootLayers.Contains(parent) && !ui.Elements.Any(e => (string)e["name"] == parent))
                {
                    ui.Warnings.Add($"element '{name}' has unknown parent '{parent}' (client would drop it)");
                    continue;
                }
                if (existing != null && name != "AddUI CreatedPanel")
                    ui.Warnings.Add($"duplicate element name '{name}' (parent '{parent}')");
                // The game client blends a translucent RawImage (in linear colour space) differently from a
                // translucent panel, so a translucent rounded-corner piece shows up as a light square in game.
                var raw = ((JArray)el["components"])?.OfType<JObject>().FirstOrDefault(c => (string)c["type"] == "UnityEngine.UI.RawImage");
                if (raw != null && uint.TryParse((string)raw["png"], out var png) && FileStorage.server.Files.ContainsKey(png) && Alpha((string)raw["color"]) < 0.999f)
                    ui.Warnings.Add($"translucent corner piece '{name}' (colour '{(string)raw["color"]}'): blend it onto the surface below so it is opaque");
                bool wantsCursor = ((JArray)el["components"])?.OfType<JObject>().Any(c => (string)c["type"] == "NeedsCursor") == true;
                if (wantsCursor && !ui.Elements.Any(e => ((JArray)e["components"])?.OfType<JObject>().Any(c => (string)c["type"] == "NeedsCursor") == true))
                    ui.CursorGrabs++;
                el["name"] = name;
                el["parent"] = parent;
                el["__id"] = ++ElementIds;
                ui.Elements.Add(el);
            }
        }

        private static float Alpha(string rgba)
        {
            var parts = (rgba ?? "1 1 1 1").Trim().Split(' ');
            return parts.Length >= 4 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 1f;
        }

        private static JArray MergeComponents(JArray a, JArray b)
        {
            var res = new JArray(a.Select(x => x.DeepClone()));
            foreach (JObject c in b)
            {
                var match = res.OfType<JObject>().FirstOrDefault(x => (string)x["type"] == (string)c["type"]);
                if (match == null) res.Add(c.DeepClone()); else match.Merge(c);
            }
            return res;
        }

        public static void ClientDestroyUi(BasePlayer player, string name, bool countCall = true)
        {
            var ui = Ui(player);
            if (countCall) ui.DestroyCalls++;
            const string Anon = "AddUI CreatedPanel";
            if (!ui.Elements.Any(e => (string)e["name"] == name)) return;
            var doomed = new HashSet<string> { name };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var e in ui.Elements)
                {
                    var n = (string)e["name"]; var p = (string)e["parent"];
                    if (n != Anon && !doomed.Contains(n) && doomed.Contains(p)) { doomed.Add(n); grew = true; }
                }
            }
            ui.Elements.RemoveAll(e => ((string)e["name"] != Anon && doomed.Contains((string)e["name"])) || doomed.Contains((string)e["parent"]));
        }

        public static string Snapshot(BasePlayer p) => new JArray(Ui(p).Elements).ToString(Formatting.None);

        public static void Advance(double seconds)
        {
            double target = Oxide.Core.Interface.Clock + seconds;
            while (true)
            {
                RunNextTicks();
                var due = Timers.Where(t => !t.Destroyed && t.FireAt <= target).OrderBy(t => t.FireAt).FirstOrDefault();
                if (due == null) break;
                Oxide.Core.Interface.Clock = Math.Max(Oxide.Core.Interface.Clock, due.FireAt);
                var cb = due.Callback;
                if (due.RepsLeft == 1) { due.Destroy(); }
                else { if (due.RepsLeft > 1) SetReps(due, due.RepsLeft - 1); SetFire(due, due.FireAt + Math.Max(0.001, due.Delay)); }
                cb?.Invoke();
            }
            Oxide.Core.Interface.Clock = target;
            RunNextTicks();
        }

        private static void SetReps(Oxide.Plugins.Timer t, int r) => typeof(Oxide.Plugins.Timer).GetField("RepsLeft", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(t, r);
        private static void SetFire(Oxide.Plugins.Timer t, double f) => typeof(Oxide.Plugins.Timer).GetField("FireAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(t, f);

        public static void RunNextTicks()
        {
            while (NextTicks.Count > 0)
            {
                var list = NextTicks.ToList();
                NextTicks.Clear();
                foreach (var a in list) a();
            }
        }

        // Facepunch-style argument splitting: whitespace separated, double quotes group, quotes stripped.
        public static string[] SplitQuotes(string input)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(input)) return result.ToArray();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false, any = false;
            for (int i = 0; i < input.Length; i++)
            {
                char ch = input[i];
                if (ch == '\\' && i + 1 < input.Length && input[i + 1] == '"') { sb.Append('"'); i++; any = true; continue; }
                if (ch == '"') { inQuotes = !inQuotes; any = true; continue; }
                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (any || sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); any = false; }
                    continue;
                }
                sb.Append(ch); any = true;
            }
            if (any || sb.Length > 0) result.Add(sb.ToString());
            return result.ToArray();
        }

        public static void ResetAll()
        {
            DataFiles.Clear(); ConfigFiles.Clear(); Hooks.Clear(); WebRequests.Clear(); Timers.Clear(); NextTicks.Clear();
            LogLines.Clear(); ChatLog.Clear(); ConsoleReplies.Clear(); Clients.Clear(); MissingLangKeys.Clear();
            Permission.UserPerms.Clear(); Permission.DefaultGroupPerms.Clear();
            Permission.GroupMembers.Clear(); Permission.CreateGroup("default", "default", 0); Permission.CreateGroup("admin", "admin", 1);
            BasePlayer.activePlayerList.Clear();
            FileStorage.server.Files.Clear();
            CommunityEntity.ServerInstance = new CommunityEntity();
            CommunityEntity.ServerInstance.net.ID = new NetworkableId(1234567);
            DataWrites = 0; ConfigWrites = 0;
        }
    }
}
