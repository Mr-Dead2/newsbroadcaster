// Minimal Oxide/uMod surface (Oxide.Core, Oxide.CSharp, Oxide.Rust). Signatures mirror the real framework.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HarnessRuntime;

namespace Oxide.Core
{
    public struct VersionNumber
    {
        public int Major, Minor, Patch;
        public VersionNumber(int major, int minor, int patch) { Major = major; Minor = minor; Patch = patch; }
        public override string ToString() => $"{Major}.{Minor}.{Patch}";
    }

    public class DataFileSystem
    {
        public string Directory => "oxide/data";
        public bool ExistsDatafile(string name) => Sim.DataFiles.ContainsKey(name);

        public T ReadObject<T>(string name)
        {
            if (!ExistsDatafile(name))
            {
                T instance = Activator.CreateInstance<T>();
                WriteObject(name, instance);
                return instance;
            }
            return JsonConvert.DeserializeObject<T>(Sim.DataFiles[name], Sim.OxideJsonSettings);
        }

        public void WriteObject<T>(string name, T Object, bool sync = false)
        {
            Sim.DataWrites++;
            Sim.FileWrites[name] = Sim.WritesOf(name) + 1;
            Sim.DataFiles[name] = JsonConvert.SerializeObject(Object, Formatting.Indented, Sim.OxideJsonSettings);
        }

        public Configuration.DynamicConfigFile GetFile(string name) => new Configuration.DynamicConfigFile("data:" + name);
    }

    public class OxideMod
    {
        public DataFileSystem DataFileSystem { get; } = new DataFileSystem();
        public string RootDirectory => "/srv/rust";
        public string DataDirectory => "/srv/rust/oxide/data";
        public string ConfigDirectory => "/srv/rust/oxide/config";
        public string LogDirectory => "/srv/rust/oxide/logs";
        public void NextTick(Action callback) => Sim.NextTicks.Add(callback);
        public void LogWarning(string format, params object[] args) => Sim.Log("WARN " + string.Format(format, args));
    }

    public static class Interface
    {
        public static OxideMod Oxide { get; } = new OxideMod();
        public static double Clock;   // harness-only

        // Like Oxide: a hook is called on every loaded plugin that has a method of that name taking those arguments.
        public static object CallHook(string hook, params object[] args)
        {
            Sim.Hooks.Add(new KeyValuePair<string, object[]>(hook, args));
            foreach (var listener in Sim.HookListeners.ToArray())
            {
                var method = listener.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == hook && m.GetParameters().Length == args.Length);
                if (method == null) continue;
                try { method.Invoke(listener, args); }
                catch (System.Reflection.TargetInvocationException tie) { Sim.Log($"ERR Failed to call hook '{hook}' on plugin '{listener.GetType().Name}' ({tie.InnerException})"); }
            }
            return null;
        }

        public static object Call(string hook, params object[] args) => CallHook(hook, args);
    }
}

namespace Oxide.Core.Configuration
{
    public class DynamicConfigFile
    {
        private readonly string key;
        public JsonSerializerSettings Settings { get; set; } = new JsonSerializerSettings();
        public DynamicConfigFile(string filename) { key = filename; }
        public string Filename => key;
        public bool Exists(string filename = null) => Sim.ConfigFiles.ContainsKey(filename ?? key);

        public T ReadObject<T>(string filename = null)
        {
            filename = filename ?? key;
            if (Exists(filename)) return JsonConvert.DeserializeObject<T>(Sim.ConfigFiles[filename], Settings);
            T obj = Activator.CreateInstance<T>();
            WriteObject(obj, false, filename);
            return obj;
        }

        public void WriteObject<T>(T config, bool sync = false, string filename = null)
        {
            Sim.ConfigWrites++;
            Sim.ConfigFiles[filename ?? key] = JsonConvert.SerializeObject(config, Formatting.Indented, Settings);
        }

        public void Load(string filename = null)
        {
            var f = filename ?? key;
            if (Sim.ConfigFiles.TryGetValue(f, out var json)) JObject.Parse(json); // throws on corrupt
        }

        public void Save(string filename = null) { var f = filename ?? key; if (!Sim.ConfigFiles.ContainsKey(f)) Sim.ConfigFiles[f] = "{}"; }
        public void Clear() => Sim.ConfigFiles[key] = "{}";
    }
}

namespace Oxide.Core.Libraries
{
    public enum RequestMethod { DELETE, GET, PATCH, POST, PUT }

    public class Library { }

    public class WebRequests : Library
    {
        public void Enqueue(string url, string body, Action<int, string> callback, Plugins.Plugin owner, RequestMethod method = RequestMethod.GET, Dictionary<string, string> headers = null, float timeout = 0f)
        {
            Sim.WebRequests.Add(new Sim.WebRequestRecord { Url = url, Body = body, Method = method, Headers = headers });
            var (code, resp) = Sim.WebResponder(url, body);
            callback?.Invoke(code, resp);
        }
    }

    public class Permission : Library
    {
        private readonly HashSet<string> registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, HashSet<string>> UserPerms = new Dictionary<string, HashSet<string>>();
        public readonly HashSet<string> DefaultGroupPerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void RegisterPermission(string name, Plugins.Plugin owner)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!registered.Add(name)) Sim.Log($"WARN duplicate permission registration '{name}'");
        }

        public bool PermissionExists(string name, Plugins.Plugin owner = null) => registered.Contains(name);

        // On a server every one of these is a linear scan over the player's permissions and groups: tests count them.
        public static int Lookups;

        public bool UserHasPermission(string id, string perm)
        {
            Lookups++;
            if (string.IsNullOrEmpty(perm)) return false;
            if (DefaultGroupPerms.Contains(perm)) return true;
            return UserPerms.TryGetValue(id ?? "", out var set) && set.Contains(perm);
        }

        public void GrantUserPermission(string id, string perm, Plugins.Plugin owner)
        {
            if (!UserPerms.TryGetValue(id, out var set)) UserPerms[id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(perm);
        }

        public void RevokeUserPermission(string id, string perm) { if (UserPerms.TryGetValue(id, out var s)) s.Remove(perm); }
        public bool GroupHasPermission(string name, string perm) => name == "default" && DefaultGroupPerms.Contains(perm);
        public void GrantGroupPermission(string name, string perm, Plugins.Plugin owner) { if (name == "default") DefaultGroupPerms.Add(perm); }

        // Groups (real signatures from Oxide.Core Permission). Every player is in "default", as on a server.
        public readonly Dictionary<string, HashSet<string>> GroupMembers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = new HashSet<string>(), ["admin"] = new HashSet<string>()
        };
        public bool CreateGroup(string groupName, string groupTitle, int groupRank)
        {
            if (string.IsNullOrEmpty(groupName) || GroupMembers.ContainsKey(groupName)) return false;
            GroupMembers[groupName] = new HashSet<string>();
            return true;
        }
        public bool GroupExists(string groupName) => !string.IsNullOrEmpty(groupName) && (groupName.Equals("*") || GroupMembers.ContainsKey(groupName));
        public string[] GetGroups() => GroupMembers.Keys.ToArray();
        public string[] GetUserGroups(string playerId) => GroupMembers.Where(g => g.Key == "default" || g.Value.Contains(playerId)).Select(g => g.Key).ToArray();
        public void AddUserGroup(string playerId, string groupName) { if (GroupMembers.TryGetValue(groupName, out var m)) m.Add(playerId); }
        public void RemoveUserGroup(string playerId, string groupName) { if (GroupMembers.TryGetValue(groupName, out var m)) m.Remove(playerId); }
        public bool UserHasGroup(string playerId, string groupName)
        {
            Lookups++;
            if (!GroupExists(groupName)) return false;
            return groupName.Equals("default", StringComparison.OrdinalIgnoreCase) || GroupMembers[groupName].Contains(playerId);
        }
    }

    public class Lang : Library
    {
        private readonly Dictionary<string, Dictionary<string, string>> messages = new Dictionary<string, Dictionary<string, string>>();
        // Test access: the library in use, and the language each player has set (default "en").
        public static Lang Instance;
        public static readonly Dictionary<string, string> UserLanguages = new Dictionary<string, string>();
        public Lang() { Instance = this; UserLanguages.Clear(); }
        public Dictionary<string, string> Messages(string lang) => messages.TryGetValue(lang, out var dict) ? dict : new Dictionary<string, string>();
        public void RegisterMessages(Dictionary<string, string> newMessages, Plugins.Plugin plugin, string lang = "en")
        {
            if (!messages.TryGetValue(lang, out var dict)) messages[lang] = dict = new Dictionary<string, string>();
            foreach (var kv in newMessages) if (!dict.ContainsKey(kv.Key)) dict[kv.Key] = kv.Value;
        }

        public string GetMessage(string key, Plugins.Plugin plugin, string userId = null)
        {
            if (key == null) return null;
            if (messages.TryGetValue(GetLanguage(userId), out var own) && own.TryGetValue(key, out var translated)) return translated;
            if (messages.TryGetValue("en", out var dict) && dict.TryGetValue(key, out var v)) return v;
            Sim.MissingLangKeys.Add(key);
            return key;
        }

        public string GetLanguage(string userId = null) => userId != null && UserLanguages.TryGetValue(userId, out var language) ? language : "en";
        public string[] GetLanguages(Plugins.Plugin plugin = null) => messages.Keys.ToArray();
    }
}

namespace Oxide.Core.Plugins
{
    [AttributeUsage(AttributeTargets.Field)]
    public class PluginReferenceAttribute : Attribute
    {
        public string Name { get; }
        public PluginReferenceAttribute() { }
        public PluginReferenceAttribute(string name) { Name = name; }
    }

    public abstract class Plugin
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public VersionNumber Version { get; set; }
        public bool IsLoaded { get; set; } = true;
        public bool HasConfig { get; set; } = true;

        public object Call(string hook, params object[] args) => OnCallHook(hook, args);

        public T Call<T>(string hook, params object[] args)
        {
            var r = OnCallHook(hook, args);
            if (r is T t) return t;
            return default(T);
        }

        public object CallHook(string hook, params object[] args) => OnCallHook(hook, args);

        protected virtual object OnCallHook(string hook, object[] args) => null;

        protected virtual void LoadConfig() { }
        protected virtual void SaveConfig() { }
        protected virtual void LoadDefaultConfig() { }
        protected virtual void LoadDefaultMessages() { }
    }

    public abstract class CSPlugin : Plugin { }
}

namespace Oxide.Plugins
{
    using Oxide.Core;
    using Oxide.Core.Libraries;
    using Oxide.Core.Plugins;

    [AttributeUsage(AttributeTargets.Class)]
    public class InfoAttribute : Attribute
    {
        public string Title { get; }
        public string Author { get; }
        public VersionNumber Version { get; private set; }
        public InfoAttribute(string Title, string Author, string Version)
        {
            this.Title = Title; this.Author = Author;
            var p = Version.Split('.').Select(int.Parse).ToArray();
            this.Version = new VersionNumber(p[0], p.Length > 1 ? p[1] : 0, p.Length > 2 ? p[2] : 0);
        }
        public InfoAttribute(string Title, string Author, double Version) : this(Title, Author, Version.ToString(System.Globalization.CultureInfo.InvariantCulture)) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class DescriptionAttribute : Attribute
    {
        public string Description { get; }
        public DescriptionAttribute(string description) { Description = description; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ConsoleCommandAttribute : Attribute
    {
        public string Command { get; }
        public ConsoleCommandAttribute(string command) { Command = command; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ChatCommandAttribute : Attribute
    {
        public string Command { get; }
        public ChatCommandAttribute(string command) { Command = command; }
    }

    public class Timer
    {
        internal double FireAt;
        internal float Interval;
        internal int RepsLeft;   // 0 = infinite
        internal Action Cb;
        public bool Destroyed { get; private set; }
        public float Delay => Interval;
        public int Repetitions => RepsLeft;
        public Action Callback => Cb;

        public bool Destroy()
        {
            if (Destroyed) return false;
            Destroyed = true;
            Sim.Timers.Remove(this);
            return true;
        }

        public void Reset(float delay = -1f, int repetitions = 1)
        {
            if (delay >= 0) Interval = delay;
            RepsLeft = repetitions;
            FireAt = Interface.Clock + Interval;
            Destroyed = false;
            if (!Sim.Timers.Contains(this)) Sim.Timers.Add(this);
        }
    }

    public class PluginTimers
    {
        private Timer Make(float interval, int reps, Action cb)
        {
            var t = new Timer { Interval = interval, RepsLeft = reps, Cb = cb, FireAt = Interface.Clock + interval };
            Sim.Timers.Add(t);
            return t;
        }

        public Timer Once(float seconds, Action callback) => Make(seconds, 1, callback);
        public Timer In(float seconds, Action callback) => Make(seconds, 1, callback);
        public Timer Every(float interval, Action callback) => Make(interval, 0, callback);
        public Timer Repeat(float interval, int reps, Action callback) => Make(interval, reps, callback);
        public void Destroy(ref Timer timer) { timer?.Destroy(); timer = null; }
    }

    public abstract class CSharpPlugin : CSPlugin
    {
        protected PluginTimers timer = new PluginTimers();
        protected Permission permission = Sim.Permission;
        protected Lang lang = Sim.Lang;
        protected WebRequests webrequest = new WebRequests();
        protected Oxide.Core.Configuration.DynamicConfigFile Config { get; private set; }

        protected CSharpPlugin()
        {
            var info = (InfoAttribute)Attribute.GetCustomAttribute(GetType(), typeof(InfoAttribute));
            Name = GetType().Name;
            Title = info?.Title ?? Name;
            Author = info?.Author;
            Version = info?.Version ?? default(VersionNumber);
            Config = new Oxide.Core.Configuration.DynamicConfigFile("config:" + Name);
        }

        protected override void LoadConfig()
        {
            if (!Config.Exists())
            {
                LoadDefaultConfig();
                SaveConfig();
            }
            try { Config.Load(); }
            catch (Exception ex) { Sim.Log("ERR Failed to load config file (is the config file corrupt?) (" + ex.Message + ")"); }
        }

        protected override void SaveConfig() => Config.Save();

        protected void Puts(string format, params object[] args) => Sim.Log("[" + Title + "] " + (args.Length > 0 ? string.Format(format, args) : format));
        protected void PrintWarning(string format, params object[] args) => Sim.Log("WARN [" + Title + "] " + (args.Length > 0 ? string.Format(format, args) : format));
        protected void PrintError(string format, params object[] args) => Sim.Log("ERR [" + Title + "] " + (args.Length > 0 ? string.Format(format, args) : format));
        protected void NextTick(Action callback) => Sim.NextTicks.Add(callback);
        protected void NextFrame(Action callback) => Sim.NextTicks.Add(callback);
        protected void LogToFile(string filename, string text, Plugin plugin, bool datedFilename = true, bool timestampPrefix = false)
            => Sim.Log($"[logfile:{filename}] {text}");
    }

    public abstract class RustPlugin : CSharpPlugin
    {
        protected void PrintToChat(BasePlayer player, string format, params object[] args)
        {
            if (player?.net?.connection == null) return;
            player.SendConsoleCommand("chat.add", 2, 0, args.Length > 0 ? string.Format(format, args) : format);
        }

        protected void PrintToChat(string format, params object[] args)
        {
            foreach (var p in BasePlayer.activePlayerList) PrintToChat(p, format, args);
        }

        protected void SendReply(BasePlayer player, string format, params object[] args) => PrintToChat(player, format, args);

        protected void SendReply(ConsoleSystem.Arg arg, string format, params object[] args)
        {
            var message = args.Length > 0 ? string.Format(format, args) : format;
            var player = arg.Connection?.player as BasePlayer;
            if (player?.net?.connection != null)
            {
                player.SendConsoleCommand("echo " + message);
                Sim.ConsoleReply(arg, message);
                return;
            }
            Sim.ConsoleReply(arg, message);
        }

        protected void PrintToConsole(BasePlayer player, string format, params object[] args)
            => player?.SendConsoleCommand("echo " + (args.Length > 0 ? string.Format(format, args) : format));
    }
}
