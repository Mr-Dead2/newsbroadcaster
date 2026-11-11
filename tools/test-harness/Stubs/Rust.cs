// Minimal Rust (Assembly-CSharp / Facepunch) surface used by plugins. Signatures mirror the real game API.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HarnessRuntime;

public struct EncryptedValue<TValue> where TValue : unmanaged
{
    private TValue _value;
    public TValue Get() => _value;
    public void Set(TValue v) => _value = v;
    public static implicit operator TValue(EncryptedValue<TValue> v) => v._value;
    public static implicit operator EncryptedValue<TValue>(TValue v) => new EncryptedValue<TValue> { _value = v };
    // Deliberately no ToString override: boxing/formatting userID is a known plugin bug source.
}

public class ListHashSet<T> : IEnumerable<T>
{
    private readonly List<T> list = new List<T>();
    public int Count => list.Count;
    public List<T> Values => list;
    public T this[int i] => list[i];
    public void Add(T v) { if (!list.Contains(v)) list.Add(v); }
    public bool Remove(T v) => list.Remove(v);
    public bool Contains(T v) => list.Contains(v);
    public void Clear() => list.Clear();
    public List<T>.Enumerator GetEnumerator() => list.GetEnumerator();
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => list.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => list.GetEnumerator();
}

public struct NetworkableId
{
    public ulong Value;
    public NetworkableId(ulong value) { Value = value; }
    public override string ToString() => Value.ToString();
}

// Server-side file store used for CUI images (ImageLibrary stores its pictures here too).
// Mirrors the API from the November 2026 Rust update, which replaced Store(data, type, entityID[, numID])
// with StoreByCrc(entityId, type, data); the old 3-argument Store call no longer compiles on a server.
public class FileStorage
{
    public enum Type { png, jpg, ogg }
    public static FileStorage server = new FileStorage();
    public readonly Dictionary<uint, byte[]> Files = new Dictionary<uint, byte[]>();

    public uint StoreByCrc(NetworkableId entityId, Type type, byte[] data)
    {
        uint crc = ComputeCrc(data);
        Files[crc] = data;
        return crc;
    }

    public byte[] Get(NetworkableId entityId, uint part, uint crc) => Files.TryGetValue(crc, out var d) ? d : null;

    public void Remove(NetworkableId entityId, uint part) => Files.Remove(part);

    public static uint ComputeCrc(byte[] data) => Crc32(data);

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1; }
        return crc ^ 0xFFFFFFFF;
    }
}

namespace Network
{
    public class Connection
    {
        public ulong userid;
        public ulong ownerid;
        public string username;
        public object player;
        public uint authLevel;
        public bool connected = true;
        public string ipaddress = "127.0.0.1:28015";
    }

    public class Networkable
    {
        public Connection connection;
        public NetworkableId ID;
    }
}

public class BaseMonoBehaviour : MonoBehaviour { }
public class FacepunchBehaviour : BaseMonoBehaviour { }
public class BaseNetworkable : BaseMonoBehaviour
{
    public Network.Networkable net = new Network.Networkable();
    public bool IsDestroyed { get; set; }
}
public class BaseEntity : BaseNetworkable
{
    public enum GiveItemReason { Generic, ResourceHarvested, PickedUp, Crafted }
    public virtual void GiveItem(Item item, GiveItemReason reason = GiveItemReason.Generic) { }
}
public class BaseCombatEntity : BaseEntity { }
public class PointEntity : BaseEntity { }

public class CommunityEntity : PointEntity
{
    public static CommunityEntity ServerInstance;
}

public class PlayerInventory
{
    public BasePlayer owner;
    public List<Item> given = new List<Item>();
    public bool FullInventory;
    public bool GiveItem(Item item, ItemContainer container = null)
    {
        if (FullInventory) return false;
        given.Add(item);
        return true;
    }
}

public class ItemContainer { }

public class BasePlayer : BaseCombatEntity
{
    public static ListHashSet<BasePlayer> activePlayerList = new ListHashSet<BasePlayer>();
    public static ListHashSet<BasePlayer> sleepingPlayerList = new ListHashSet<BasePlayer>();

    public EncryptedValue<ulong> userID;
    public string displayName;
    public PlayerInventory inventory;
    public bool isAdminFlag;
    public bool connectedFlag = true;
    public List<Item> dropped = new List<Item>();   // harness: what GiveItem had to drop

    public BasePlayer() { inventory = new PlayerInventory { owner = this }; }

    public string UserIDString => userID.Get().ToString();
    public bool IsAdmin => isAdminFlag;
    public bool IsConnected => connectedFlag;
    public bool IsSleeping() => false;
    public bool IsDead() => false;
    public bool IsAlive() => true;

    public void ChatMessage(string msg) => Sim.Chat(this, msg);

    // Into the inventory, or dropped in front of the player when it is full (like the game does).
    public override void GiveItem(Item item, GiveItemReason reason = GiveItemReason.Generic)
    {
        if (inventory.GiveItem(item)) return;
        item.Drop(Vector3.zero, Vector3.zero);
        dropped.Add(item);
    }

    public void SendConsoleCommand(string command, params object[] obj)
    {
        if (command == "chat.add" && obj != null && obj.Length >= 3) Sim.Chat(this, Convert.ToString(obj[2]));
        else Sim.Log($"[console->{displayName}] {command} {string.Join(" ", obj ?? new object[0])}");
    }

    public static BasePlayer Find(string strNameOrIDOrIP)
    {
        foreach (var p in activePlayerList)
            if (p.UserIDString == strNameOrIDOrIP || string.Equals(p.displayName, strNameOrIDOrIP, StringComparison.OrdinalIgnoreCase))
                return p;
        foreach (var p in activePlayerList)
            if (p.displayName != null && p.displayName.IndexOf(strNameOrIDOrIP, StringComparison.OrdinalIgnoreCase) >= 0)
                return p;
        return null;
    }

    public static BasePlayer FindByID(ulong userID)
    {
        foreach (var p in activePlayerList) if (p.userID.Get() == userID) return p;
        return null;
    }

    public static BasePlayer FindAwakeOrSleeping(string strNameOrIDOrIP) => Find(strNameOrIDOrIP);
}

public class Translate
{
    public class Phrase
    {
        public string token;
        public string english;
        public virtual string translated => english;
        public Phrase(string t = "", string e = "") { token = t; english = e; }
    }
}

public class ItemDefinition : MonoBehaviour
{
    public int itemid;
    public string shortname;
    public Translate.Phrase displayName;
}

public class Item
{
    public ItemDefinition info;
    public int amount;
    public ulong skin;
    public bool dropped;
    public BaseEntity Drop(Vector3 vPos, Vector3 vVelocity, Quaternion rotation = default(Quaternion))
    {
        dropped = true;
        return new BaseEntity();
    }
    public void Remove(float fTime = 0f) { }
}

public static class ItemManager
{
    public static readonly Dictionary<string, ItemDefinition> Defs = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal)
    {
        ["scrap"] = new ItemDefinition { itemid = -932201673, shortname = "scrap", displayName = new Translate.Phrase("scrap", "Scrap") },
        ["wood"] = new ItemDefinition { itemid = -151838493, shortname = "wood", displayName = new Translate.Phrase("wood", "Wood") },
        ["stones"] = new ItemDefinition { itemid = -2099697608, shortname = "stones", displayName = new Translate.Phrase("stones", "Stones") },
        ["supply.signal"] = new ItemDefinition { itemid = 1397052267, shortname = "supply.signal", displayName = new Translate.Phrase("supply.signal", "Supply Signal") },
    };

    public static ItemDefinition FindItemDefinition(string shortName)
    {
        if (shortName == null) return null;
        Defs.TryGetValue(shortName, out var d);
        return d;
    }

    public static ItemDefinition FindItemDefinition(int itemID)
    {
        foreach (var d in Defs.Values) if (d.itemid == itemID) return d;
        return null;
    }

    public static Item CreateByName(string strName, int iAmount = 1, ulong skin = 0uL)
    {
        var def = FindItemDefinition(strName);
        if (def == null) return null;
        return new Item { info = def, amount = iAmount, skin = skin };
    }

    public static Item CreateByItemID(int itemID, int iAmount = 1, ulong skin = 0uL)
    {
        var def = FindItemDefinition(itemID);
        return def == null ? null : new Item { info = def, amount = iAmount, skin = skin };
    }

    public static Item Create(ItemDefinition template, int iAmount = 1, ulong skin = 0uL)
        => template == null ? null : new Item { info = template, amount = iAmount, skin = skin };
}

public class EffectData { }

public class Effect : EffectData
{
    public string pooledString;
    public Vector3 worldPos;
    public Effect() { }
    public Effect(string effectName, Vector3 posWorld, Vector3 normWorld, Network.Connection sourceConnection = null) { pooledString = effectName; worldPos = posWorld; }
    public Effect(string effectName, BaseEntity ent, uint boneID, Vector3 posLocal, Vector3 normLocal, Network.Connection sourceConnection = null) { pooledString = effectName; }

    public static class server
    {
        public static void Run(string strName, BaseEntity ent, uint boneID, Vector3 posLocal, Vector3 normLocal, Network.Connection sourceConnection = null, bool broadcast = false)
            => Sim.Log($"[effect broadcast @entity] {strName}");
        public static void Run(string strName, Vector3 posWorld = default(Vector3), Vector3 normWorld = default(Vector3), Network.Connection sourceConnection = null, bool broadcast = false)
            => Sim.Log($"[effect broadcast @{posWorld}] {strName}");
    }
}

public static class EffectNetwork
{
    public static void Send(Effect effect) => Sim.Log($"[effect broadcast] {effect.pooledString}");
    public static void Send(Effect effect, Network.Connection target)
        => Sim.Log($"[effect -> {target?.username}] {effect.pooledString}");
}

// Facepunch's lightweight string slice (ConsoleSystem.Arg.FullString). Deliberately no implicit
// conversion to string, so "arg.FullString ?? ..." or passing it as a string fails here like on a real server.
public struct StringView
{
    private readonly string value;
    public StringView(string value) { this.value = value ?? string.Empty; }
    public int Length => (value ?? string.Empty).Length;
    public override string ToString() => value ?? string.Empty;
}

public class ConsoleSystem
{
    public class Arg
    {
        private readonly string fullString;
        public string[] Args;
        public Network.Connection Connection { get; }
        public bool IsRcon { get; }
        public string Reply;

        public Arg(Network.Connection connection, string fullString, bool rcon = false)
        {
            Connection = connection;
            this.fullString = fullString ?? string.Empty;
            Args = Sim.SplitQuotes(this.fullString);
            IsRcon = rcon;
        }

        // Current Rust returns a StringView here, not a string: only .ToString() works on it.
        public StringView FullString => new StringView(fullString);
        public bool IsAdmin => Connection == null || Connection.authLevel > 0;
        public bool IsServerside => true;
        public bool IsClientside => false;
        public bool HasArgs(int iMinimum = 1) => Args != null && Args.Length >= iMinimum;
        public BasePlayer Player() => Connection?.player as BasePlayer;

        public string GetString(int iArg, string def = "")
        {
            if (Args == null || iArg < 0 || iArg >= Args.Length) return def;
            return Args[iArg];
        }

        public int GetInt(int iArg, int def = 0)
        {
            var s = GetString(iArg, null);
            if (s == null) return def;
            return int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        public uint GetUInt(int iArg, uint def = 0)
        {
            var s = GetString(iArg, null);
            return s != null && uint.TryParse(s, out var v) ? v : def;
        }

        public ulong GetULong(int iArg, ulong def = 0)
        {
            var s = GetString(iArg, null);
            return s != null && ulong.TryParse(s, out var v) ? v : def;
        }

        public ulong GetUInt64(int iArg, ulong def = 0) => GetULong(iArg, def);

        public float GetFloat(int iArg, float def = 0f)
        {
            var s = GetString(iArg, null);
            return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        public bool GetBool(int iArg, bool def = false)
        {
            var s = GetString(iArg, null);
            if (s == null) return def;
            if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return def;
        }

        public void ReplyWith(string strValue) { Reply = strValue; Sim.ConsoleReply(this, strValue); }
    }
}

namespace Facepunch
{
    // Object pool (current API: Get<T>() and FreeUnmanaged(ref collection); the old GetList / FreeList are gone).
    // Outstanding counts what was taken and not given back, so the tests can check nothing leaks.
    public static class Pool
    {
        public interface IPooled { void EnterPool(); void LeavePool(); }

        public static int Outstanding;
        private static readonly Dictionary<Type, Stack<object>> pools = new Dictionary<Type, Stack<object>>();

        public static T Get<T>() where T : class, new()
        {
            Outstanding++;
            Stack<object> stack;
            if (pools.TryGetValue(typeof(T), out stack) && stack.Count > 0) return (T)stack.Pop();
            return new T();
        }

        private static void Return(Type type, object obj)
        {
            Outstanding--;
            Stack<object> stack;
            if (!pools.TryGetValue(type, out stack)) pools[type] = stack = new Stack<object>();
            stack.Push(obj);
        }

        public static void FreeUnmanaged<T>(ref List<T> obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.Clear();
            Return(typeof(List<T>), obj);
            obj = null;
        }

        public static void FreeUnmanaged<T>(ref HashSet<T> obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.Clear();
            Return(typeof(HashSet<T>), obj);
            obj = null;
        }

        public static void FreeUnmanaged<TKey, TVal>(ref Dictionary<TKey, TVal> obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.Clear();
            Return(typeof(Dictionary<TKey, TVal>), obj);
            obj = null;
        }
    }
}
