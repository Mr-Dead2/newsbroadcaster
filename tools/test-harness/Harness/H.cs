using System.Reflection;
using HarnessRuntime;
using Newtonsoft.Json.Linq;
using Oxide.Plugins;

// Reflection helpers to drive the plugin like Oxide would.
public static class H
{
    public static object P;
    public static Type T;
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Boot(Action<object> beforeLoad = null)
    {
        T = typeof(NewsBroadcaster);
        P = Activator.CreateInstance(T, true);
        beforeLoad?.Invoke(P);
        Call("LoadConfig");
        Call("LoadDefaultMessages");
        CallIfExists("Init");
        CallIfExists("Loaded");
        CallIfExists("OnServerInitialized");
        Sim.RunNextTicks();
    }

    public static bool Has(string name) => T.GetMethods(All).Any(m => m.Name == name);

    public static object CallIfExists(string name, params object[] args) => Has(name) ? Call(name, args) : null;

    public static object Call(string name, params object[] args)
    {
        var cands = T.GetMethods(All).Where(m => m.Name == name).ToList();
        if (cands.Count == 0) throw new MissingMethodException(T.Name, name);
        foreach (var m in cands.OrderBy(m => m.GetParameters().Length))
        {
            var ps = m.GetParameters();
            if (ps.Length < args.Length) continue;
            if (ps.Skip(args.Length).Any(p => !p.IsOptional && !p.GetCustomAttributes<ParamArrayAttribute>().Any())) continue;
            bool ok = true;
            for (int i = 0; i < args.Length; i++)
                if (args[i] != null && !ps[i].ParameterType.IsInstanceOfType(args[i]) && !(ps[i].ParameterType.IsValueType && args[i].GetType() == ps[i].ParameterType)) { ok = false; break; }
            if (!ok) continue;
            var full = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                full[i] = i < args.Length ? args[i] : (ps[i].GetCustomAttributes<ParamArrayAttribute>().Any() ? Array.CreateInstance(ps[i].ParameterType.GetElementType(), 0) : ps[i].DefaultValue);
            try { return m.Invoke(m.IsStatic ? null : P, full); }
            catch (TargetInvocationException tie) { throw new Exception($"{name} threw: {tie.InnerException}", tie.InnerException); }
        }
        throw new MissingMethodException(T.Name, name + $" ({args.Length} args)");
    }

    public static object Get(string field) => T.GetField(field, All)?.GetValue(P) ?? T.GetProperty(field, All)?.GetValue(P);
    public static void Set(string field, object v) { var f = T.GetField(field, All); if (f != null) f.SetValue(P, v); else T.GetProperty(field, All).SetValue(P, v); }

    public static System.Collections.IList Announcements => (System.Collections.IList)Get("announcements");
    public static object Config => Get("config");

    public static object Ann(int i) => Announcements[i];
    public static object AnnByTitle(string title)
    {
        foreach (var a in Announcements) if ((string)AGet(a, "Title") == title) return a;
        return null;
    }
    public static object AGet(object o, string f) => o.GetType().GetField(f, All)?.GetValue(o) ?? o.GetType().GetProperty(f, All)?.GetValue(o);
    public static void ASet(object o, string f, object v) { var fi = o.GetType().GetField(f, All); if (fi != null) fi.SetValue(o, v); else o.GetType().GetProperty(f, All).SetValue(o, v); }
    public static string Id(object ann) => (string)AGet(ann, "Id");

    public static object CfgPath(string path)
    {
        object cur = Config;
        foreach (var part in path.Split('.')) cur = AGet(cur, part);
        return cur;
    }
    public static void CfgSet(string path, object v)
    {
        var parts = path.Split('.');
        object cur = Config;
        foreach (var part in parts.Take(parts.Length - 1)) cur = AGet(cur, part);
        ASet(cur, parts.Last(), v);
    }

    public static BasePlayer MakePlayer(ulong id, string name, bool admin = false, bool viewPerm = true)
    {
        var p = new BasePlayer { userID = id, displayName = name, isAdminFlag = admin };
        p.net.connection = new Network.Connection { userid = id, username = name, player = p, authLevel = admin ? 2u : 0u };
        BasePlayer.activePlayerList.Add(p);
        if (viewPerm) Sim.Permission.GrantUserPermission(id.ToString(), "newsbroadcaster.view", null);
        return p;
    }

    public static ConsoleSystem.Arg Console(BasePlayer p, string line)
    {
        line = line.Trim();
        int sp = line.IndexOf(' ');
        string cmd = sp < 0 ? line : line.Substring(0, sp);
        string rest = sp < 0 ? "" : line.Substring(sp + 1);
        var m = T.GetMethods(All).FirstOrDefault(x => x.GetCustomAttributes<ConsoleCommandAttribute>().Any(a => a.Command == cmd));
        if (m == null) throw new Exception("unknown console command " + cmd);
        var arg = new ConsoleSystem.Arg(p?.net.connection, rest);
        try { m.Invoke(P, new object[] { arg }); }
        catch (TargetInvocationException tie) { throw new Exception($"console '{line}' threw: {tie.InnerException}", tie.InnerException); }
        Sim.RunNextTicks();
        return arg;
    }

    public static bool HasConsole(string cmd) => T.GetMethods(All).Any(x => x.GetCustomAttributes<ConsoleCommandAttribute>().Any(a => a.Command == cmd));

    public static void Chat(BasePlayer p, string cmd, params string[] args)
    {
        var m = T.GetMethods(All).FirstOrDefault(x => x.GetCustomAttributes<ChatCommandAttribute>().Any(a => a.Command == cmd));
        if (m == null) throw new Exception("unknown chat command " + cmd);
        try { m.Invoke(P, new object[] { p, cmd, args }); }
        catch (TargetInvocationException tie) { throw new Exception($"chat '/{cmd}' threw: {tie.InnerException}", tie.InnerException); }
        Sim.RunNextTicks();
    }

    public static List<string> ChatOf(BasePlayer p) => Sim.ChatLog.TryGetValue(p.userID, out var l) ? l : new List<string>();

    public static void Save(BasePlayer p, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var ui = Sim.Ui(p);
        File.WriteAllText(Path.Combine(dir, name + ".json"), Sim.Snapshot(p));
        if (ui.Warnings.Count > 0)
        {
            System.Console.WriteLine($"  !! CUI warnings for {name}:");
            foreach (var w in ui.Warnings.Distinct()) System.Console.WriteLine("     - " + w);
            ui.Warnings.Clear();
        }
        System.Console.WriteLine($"  saved {name} ({ui.Elements.Count} live elements)");
    }

    public static IEnumerable<JObject> Live(BasePlayer p) => Sim.Ui(p).Elements;
    public static bool HasElement(BasePlayer p, string name) => Sim.Ui(p).Elements.Any(e => (string)e["name"] == name);
    public static IEnumerable<string> Texts(BasePlayer p) => Sim.Ui(p).Elements.SelectMany(e => e["components"].Children<JObject>()).Where(c => (string)c["type"] == "UnityEngine.UI.Text").Select(c => (string)c["text"]);
    public static IEnumerable<string> Commands(BasePlayer p) => Sim.Ui(p).Elements.SelectMany(e => e["components"].Children<JObject>()).Where(c => (string)c["type"] == "UnityEngine.UI.Button").Select(c => (string)c["command"]).Where(c => c != null);
}
