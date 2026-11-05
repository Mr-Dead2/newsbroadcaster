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
    }
}

public class BaseMonoBehaviour : MonoBehaviour { }
public class FacepunchBehaviour : BaseMonoBehaviour { }
public class BaseNetworkable : BaseMonoBehaviour
{
    public Network.Networkable net = new Network.Networkable();
    public bool IsDestroyed { get; set; }
}
public class BaseEntity : BaseNetworkable { }
public class BaseCombatEntity : BaseEntity { }

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

    public BasePlayer() { inventory = new PlayerInventory { owner = this }; }

    public string UserIDString => userID.Get().ToString();
    public bool IsAdmin => isAdminFlag;
    public bool IsConnected => connectedFlag;
    public bool IsSleeping() => false;
    public bool IsDead() => false;
    public bool IsAlive() => true;

    public void ChatMessage(string msg) => Sim.Chat(this, msg);

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

        public string FullString => fullString;
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
