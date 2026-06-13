using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NewsBroadcaster", "DEDA", "1.1.1")]
    [Description("Clean, modern news broadcaster with notifications")]
    public class NewsBroadcaster : RustPlugin
    {
        #region Fields & Constants
        [PluginReference] private Plugin ImageLibrary, Notify, ServerRewards, Economics;

        private const string LayerName = "NewsBroadcasterUI";
        private const string NotificationLayer = "NewsNotificationUI";
        private const string ConfirmLayer = "NewsConfirmUI";
        private const string DataFile = "NewsBroadcaster_Data";

        private const string PermAdmin = "newsbroadcaster.admin";
        private const string PermView = "newsbroadcaster.view";

        private static readonly Regex CommandSplitRegex = new Regex(@"[\""].+?[\""]|[^ ]+", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"(https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private ConfigData config;
        private StoredData storedData;
        private List<Announcement> announcements = new List<Announcement>();
        private Dictionary<ulong, Timer> autoCloseTimers = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, Timer> notificationTimers = new Dictionary<ulong, Timer>();

        private HashSet<ulong> playersWithUiOpen = new HashSet<ulong>();

        private Dictionary<ulong, Announcement> activeEditors = new Dictionary<ulong, Announcement>();
        private Dictionary<ulong, string> activeEditorIds = new Dictionary<ulong, string>();
        private Dictionary<ulong, int> historyContentScrollOffsets = new Dictionary<ulong, int>();
        private Dictionary<ulong, ReadRewardState> readRewardTimers = new Dictionary<ulong, ReadRewardState>();
        private Dictionary<ulong, HashSet<string>> adminSelections = new Dictionary<ulong, HashSet<string>>();
        private static readonly string InvariantDateFormat = "yyyy-MM-dd HH:mm";
        private const int MaxContentChars = 32768;
        private const int BodyVisibleLineCount = 22;
        private const int BodyWrapCharacters = 58;
        private const int BodyWrapCharactersImage = 34;
        private const int DiscordEmbedDescriptionLimit = 4000;
        #endregion

        #region Data Structures
        class StoredData
        {
            public List<Announcement> Announcements = new List<Announcement>();
            public Dictionary<ulong, long> LastSeenNews = new Dictionary<ulong, long>();
        }

        class Announcement
        {
            public string Id;
            public string Title;
            public string ImageUrl;
            public string Text;
            public string Date;
            public string Author;
            public AnnouncementType Type;
            public long Timestamp;
            public bool Pinned;
            public HashSet<ulong> LikedPlayers = new HashSet<ulong>();
            public HashSet<ulong> ReadByPlayers = new HashSet<ulong>();
            public HashSet<ulong> ReadRewardedPlayers = new HashSet<ulong>();
            public HashSet<ulong> LikeRewardedPlayers = new HashSet<ulong>();
        }

        class ReadRewardState
        {
            public string AnnId;
            public Timer Timer;
        }

        enum AnnouncementType { Info, Warning, Alert, Event, Update }

        class ConfigData
        {
            public GeneralSettings General { get; set; } = new GeneralSettings();
            public NotificationSettings Notification { get; set; } = new NotificationSettings();
            public DiscordSettings Discord { get; set; } = new DiscordSettings();
            public RewardSettings Rewards { get; set; } = new RewardSettings();

            public string SelectedTheme { get; set; } = "Default";
            public Dictionary<string, UIColors> Themes { get; set; } = new Dictionary<string, UIColors>();

            [JsonIgnore]
            public UIColors Colors
            {
                get
                {
                    if (Themes == null || Themes.Count == 0) return new UIColors();
                    if (Themes.TryGetValue(SelectedTheme, out var theme)) return theme;
                    return Themes.Values.First();
                }
            }
        }

        class GeneralSettings
        {
            public int AutoCloseSeconds { get; set; } = 15;
            public bool EnableAutoClose { get; set; } = true;
            public bool ShowNewsOnConnect { get; set; } = true;
            public string ServerName { get; set; } = "SERVER NEWS";
            public int AnnouncementsPerPage { get; set; } = 5;
            public int MaxStoredAnnouncements { get; set; } = 50;
        }

        class NotificationSettings
        {
            public bool Enabled { get; set; } = true;
            public bool UseNotifyPlugin { get; set; } = false;
            public int NotifyType { get; set; } = 0;
            public string Position { get; set; } = "Right";
            public int Duration { get; set; } = 8;
            public string NotificationSound { get; set; } = "assets/bundled/prefabs/fx/notice/loot.drag.fx.prefab";
        }

        class DiscordSettings
        {
            public bool Enabled { get; set; } = false;
            public string WebhookUrl { get; set; } = "";
            public string BotName { get; set; } = "Server News";
            public string RoleMention { get; set; } = "";
        }

        class RewardItem
        {
            public string Shortname { get; set; } = "";
            public int Amount { get; set; } = 1;
            public ulong SkinId { get; set; } = 0;
        }

        class RewardBundle
        {

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<RewardItem> Items { get; set; } = new List<RewardItem>();

            public int Points { get; set; } = 0;

            public double Currency { get; set; } = 0;
        }

        class RewardSettings
        {

            public bool EnableReadReward { get; set; } = false;
            public int ReadDelaySeconds { get; set; } = 5;
            public RewardBundle ReadRewards { get; set; } = new RewardBundle
            {
                Items = new List<RewardItem> { new RewardItem { Shortname = "scrap", Amount = 5 } }
            };

            public bool EnableLikeReward { get; set; } = false;
            public RewardBundle LikeRewards { get; set; } = new RewardBundle
            {
                Items = new List<RewardItem> { new RewardItem { Shortname = "scrap", Amount = 10 } }
            };

            public bool NotifyOnReward { get; set; } = true;

            public string PointsLabel { get; set; } = "RP";
            public string CurrencyLabel { get; set; } = "coins";
        }

        class UIColors
        {

            public string PanelBg { get; set; } = "0.07 0.08 0.10 0.97";
            public string HeaderBg { get; set; } = "0.04 0.05 0.06 0.55";
            public string ContentBg { get; set; } = "0.13 0.14 0.16 0.55";
            public string ButtonPrimary { get; set; } = "0.36 0.71 1.00 0.95";
            public string ButtonSecondary { get; set; } = "0.18 0.19 0.22 0.85";
            public string TextTitle { get; set; } = "0.97 0.97 0.98 1.0";
            public string TextNormal { get; set; } = "0.85 0.86 0.88 1.0";
            public string TextMuted { get; set; } = "0.55 0.57 0.62 1.0";
        }
        #endregion

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermissionCommand"] = "You do not have permission to use this command.",
                ["NoPermissionView"] = "You do not have permission to view news.",
                ["NoNewsHistory"] = "No news history available.",
                ["NewsBroadcasted"] = "News broadcasted!",
                ["ArchiveTitle"] = "ARCHIVE",
                ["ReadMore"] = "READ >",
                ["ViewArchive"] = "VIEW ARCHIVE",
                ["PostedBy"] = "POSTED BY",
                ["NewAnnouncement"] = "NEW ANNOUNCEMENT",
                ["Close"] = "CLOSE",
                ["Previous"] = "< PREVIOUS",
                ["Next"] = "NEXT >",
                ["Page"] = "PAGE {0} / {1}",
                ["AdminControl"] = "ADMIN CONTROL",
                ["NewPost"] = "+ NEW POST",
                ["Themes"] = "THEMES",
                ["NoAnnouncementsYet"] = "No announcements yet.\nClick '+ NEW POST' to create one.",
                ["CreateAnnouncement"] = "CREATE NEW ANNOUNCEMENT",
                ["EditAnnouncement"] = "EDIT ANNOUNCEMENT",
                ["AnnouncementTitle"] = "ANNOUNCEMENT TITLE",
                ["ImageUrl"] = "IMAGE URL (leave empty for no image)",
                ["AnnouncementType"] = "ANNOUNCEMENT TYPE (click to cycle)",
                ["ContentBody"] = "CONTENT BODY",
                ["ContentBodyHint"] = "Supports \\n for line breaks. Long content is supported and displayed with paged scrolling.",
                ["SaveBroadcast"] = "✔ SAVE & BROADCAST",
                ["Cancel"] = "✕ CANCEL",
                ["SelectTheme"] = "SELECT THEME",
                ["Active"] = "(ACTIVE)",
                ["Unknown"] = "UNKNOWN",
                ["EditButton"] = "EDIT",
                ["DelButton"] = "DEL",
                ["DeleteAnnouncement"] = "DELETE ANNOUNCEMENT",
                ["DeleteConfirmBody"] = "\"{0}\"\nThis cannot be undone.",
                ["ConfirmDelete"] = "✓ DELETE",
                ["EditTargetGone"] = "The announcement you were editing was removed before you could save.",
                ["AnnouncementSavedNew"] = "Announcement saved and broadcasted to all players!",
                ["AnnouncementUpdated"] = "Announcement updated.",
                ["RewardRead"] = "Thanks for reading the news! Reward: {0}",
                ["RewardLike"] = "Thanks for the like! Reward: {0}",
                ["PinButton"] = "PIN",
                ["UnpinButton"] = "UNPIN",
                ["PinnedBadge"] = "PINNED",
                ["SelectedCount"] = "{0} SELECTED",
                ["BulkDelete"] = "✕ DELETE",
                ["BulkPin"] = "PIN ALL",
                ["BulkUnpin"] = "UNPIN ALL",
                ["ClearSelection"] = "CLEAR",
                ["SelectPageToggle"] = "SELECT PAGE",
                ["BulkDeleteTitle"] = "DELETE SELECTED",
                ["BulkDeleteBody"] = "Delete {0} announcement(s)?\nThis cannot be undone.",
                ["BulkDeleted"] = "Deleted {0} announcement(s).",
                ["BulkPinned"] = "Pinned {0} announcement(s).",
                ["BulkUnpinned"] = "Unpinned {0} announcement(s)."
            }, this);
        }

        private string Msg(string key, BasePlayer player = null, params object[] args)
        {
            var message = lang.GetMessage(key, this, player?.UserIDString);
            return args != null && args.Length > 0 ? string.Format(message, args) : message;
        }

        private string NormalizeBodyText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            value = value.Replace("\r\n", "\n").Replace("\r", "\n");
            value = value.Replace("\\n", "\n").Replace("/n", "\n");
            value = LinkRegex.Replace(value, string.Empty);

            while (value.Contains("  "))
                value = value.Replace("  ", " ");

            while (value.Contains("\n\n\n"))
                value = value.Replace("\n\n\n", "\n\n");

            return value.Trim();
        }

        private int BodyWrapFor(Announcement ann)
        {
            bool hasImage = ann != null && !string.IsNullOrEmpty(ann.ImageUrl);
            return hasImage ? BodyWrapCharactersImage : BodyWrapCharacters;
        }

        private List<string> BuildBodyDisplayLines(string text, int wrapChars = BodyWrapCharacters)
        {
            if (wrapChars < 16) wrapChars = 16;
            text = NormalizeBodyText(text);
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                lines.Add(string.Empty);
                return lines;
            }

            var rawLines = text.Split('\n');
            foreach (var rawLine in rawLines)
            {
                if (string.IsNullOrEmpty(rawLine))
                {
                    lines.Add(string.Empty);
                    continue;
                }

                var working = rawLine;
                while (working.Length > wrapChars)
                {
                    int take = wrapChars;
                    int lastSpace = working.LastIndexOf(' ', Math.Min(wrapChars - 1, working.Length - 1), Math.Min(wrapChars, working.Length));
                    if (lastSpace > 15)
                        take = lastSpace;

                    lines.Add(working.Substring(0, take).TrimEnd());
                    working = working.Substring(Math.Min(working.Length, take)).TrimStart();
                }

                lines.Add(working);
            }

            if (lines.Count == 0)
                lines.Add(string.Empty);

            return lines;
        }

        private string GetVisibleBodySlice(string text, int offset, int wrapChars = BodyWrapCharacters)
        {
            var lines = BuildBodyDisplayLines(text, wrapChars);
            offset = ClampBodyOffset(text, offset, wrapChars);
            int take = Math.Min(BodyVisibleLineCount, Math.Max(0, lines.Count - offset));
            return string.Join("\n", lines.Skip(offset).Take(take).ToArray());
        }

        private int GetBodyMaxOffset(string text, int wrapChars = BodyWrapCharacters)
        {
            var lines = BuildBodyDisplayLines(text, wrapChars);
            return Math.Max(0, lines.Count - BodyVisibleLineCount);
        }

        private int ClampBodyOffset(string text, int offset, int wrapChars = BodyWrapCharacters)
        {
            int maxOffset = GetBodyMaxOffset(text, wrapChars);
            if (offset < 0) return 0;
            if (offset > maxOffset) return maxOffset;
            return offset;
        }

        private bool CanScrollBody(string text, int wrapChars = BodyWrapCharacters)
        {
            return BuildBodyDisplayLines(text, wrapChars).Count > BodyVisibleLineCount;
        }

        private static string NewAnnouncementId() => Guid.NewGuid().ToString("N");

        private Announcement FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < announcements.Count; i++)
                if (string.Equals(announcements[i].Id, id, StringComparison.Ordinal))
                    return announcements[i];
            return null;
        }

        private int FindIndexById(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < announcements.Count; i++)
                if (string.Equals(announcements[i].Id, id, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private HashSet<string> GetAdminSelection(ulong userId)
        {
            if (!adminSelections.TryGetValue(userId, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                adminSelections[userId] = set;
            }
            return set;
        }

        private void PruneAdminSelection(ulong userId)
        {
            if (!adminSelections.TryGetValue(userId, out var set) || set.Count == 0) return;
            var liveIds = new HashSet<string>(announcements.Select(a => a.Id), StringComparer.Ordinal);
            set.RemoveWhere(id => !liveIds.Contains(id));
        }

        private List<Announcement> GetDisplayOrder()
        {
            return announcements
                .OrderByDescending(a => a.Pinned)
                .ThenByDescending(a => a.Timestamp)
                .ToList();
        }

        private Dictionary<string, object> BuildHookData(Announcement ann)
        {
            if (ann == null) return null;
            return new Dictionary<string, object>
            {
                ["id"] = ann.Id,
                ["title"] = ann.Title,
                ["author"] = ann.Author,
                ["type"] = ann.Type.ToString(),
                ["timestamp"] = ann.Timestamp,
                ["date"] = ann.Date,
                ["text"] = ann.Text,
                ["imageUrl"] = ann.ImageUrl,
                ["likes"] = ann.LikedPlayers?.Count ?? 0,
                ["pinned"] = ann.Pinned
            };
        }
        #endregion

        #region Configuration
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                bool needsSave = false;

                JObject raw = null;
                try { raw = Config.ReadObject<JObject>(); }
                catch {  }

                if (raw != null && MigrateLegacyRewardArrays(raw)) needsSave = true;

                if (raw != null && raw["Rewards"] == null) needsSave = true;

                config = raw != null ? raw.ToObject<ConfigData>() : Config.ReadObject<ConfigData>();
                if (config == null) throw new Exception();

                if (config.Rewards != null)
                {
                    int collapsed = 0;
                    collapsed += DeduplicateConsecutiveRewardItems(config.Rewards.ReadRewards?.Items);
                    collapsed += DeduplicateConsecutiveRewardItems(config.Rewards.LikeRewards?.Items);
                    if (collapsed > 0)
                    {
                        PrintWarning($"Cleaned up {collapsed} duplicate reward item(s) from config (pre-1.1.1 deserialization bug).");
                        needsSave = true;
                    }
                }

                if (config.Themes == null) config.Themes = new Dictionary<string, UIColors>();

                if (config.Themes.Count == 0)
                {
                    UIColors legacyColors = ExtractLegacyColors(raw);

                    config.Themes["Default"] = legacyColors ?? new UIColors();
                    config.Themes["Dark"] = new UIColors { PanelBg = "0.04 0.04 0.05 0.98", HeaderBg = "0.02 0.02 0.03 0.55", ContentBg = "0.09 0.09 0.10 0.55", ButtonPrimary = "0.85 0.85 0.88 0.95", ButtonSecondary = "0.14 0.14 0.16 0.85", TextTitle = "0.97 0.97 0.97 1.0", TextNormal = "0.78 0.78 0.80 1.0", TextMuted = "0.48 0.48 0.52 1.0" };
                    config.Themes["Ocean"] = new UIColors { PanelBg = "0.05 0.09 0.12 0.97", HeaderBg = "0.02 0.05 0.07 0.55", ContentBg = "0.08 0.13 0.17 0.55", ButtonPrimary = "0.20 0.78 0.95 0.95", ButtonSecondary = "0.10 0.20 0.26 0.85", TextTitle = "0.95 0.98 1.00 1.0", TextNormal = "0.82 0.90 0.94 1.0", TextMuted = "0.48 0.62 0.72 1.0" };
                    config.Themes["Rust"] = new UIColors { PanelBg = "0.10 0.08 0.07 0.97", HeaderBg = "0.06 0.05 0.04 0.55", ContentBg = "0.16 0.13 0.11 0.55", ButtonPrimary = "0.95 0.40 0.18 0.95", ButtonSecondary = "0.22 0.18 0.15 0.85", TextTitle = "0.97 0.93 0.88 1.0", TextNormal = "0.84 0.78 0.72 1.0", TextMuted = "0.60 0.52 0.45 1.0" };
                    config.Themes["Midnight"] = new UIColors { PanelBg = "0.06 0.05 0.10 0.97", HeaderBg = "0.03 0.02 0.06 0.55", ContentBg = "0.11 0.10 0.16 0.55", ButtonPrimary = "0.62 0.40 0.98 0.95", ButtonSecondary = "0.17 0.16 0.22 0.85", TextTitle = "0.97 0.96 1.00 1.0", TextNormal = "0.82 0.81 0.88 1.0", TextMuted = "0.55 0.54 0.65 1.0" };
                    config.Themes["Forest"] = new UIColors { PanelBg = "0.06 0.09 0.07 0.97", HeaderBg = "0.03 0.05 0.04 0.55", ContentBg = "0.10 0.14 0.11 0.55", ButtonPrimary = "0.40 0.85 0.55 0.95", ButtonSecondary = "0.14 0.19 0.16 0.85", TextTitle = "0.94 0.98 0.94 1.0", TextNormal = "0.80 0.88 0.82 1.0", TextMuted = "0.50 0.62 0.54 1.0" };

                    config.SelectedTheme = "Default";
                    needsSave = true;
                }

                if (needsSave) SaveConfig();
            }
            catch
            {
                config = new ConfigData();
                SaveConfig();
            }
        }

        private static bool MigrateLegacyRewardArrays(JObject raw)
        {
            var rewards = raw?["Rewards"] as JObject;
            if (rewards == null) return false;

            bool changed = false;
            foreach (var key in new[] { "ReadRewards", "LikeRewards" })
            {
                if (rewards[key] is JArray legacyItems)
                {
                    rewards[key] = new JObject
                    {
                        ["Items"] = legacyItems,
                        ["Points"] = 0,
                        ["Currency"] = 0.0
                    };
                    changed = true;
                }
            }
            return changed;
        }

        private static int DeduplicateConsecutiveRewardItems(List<RewardItem> items)
        {
            if (items == null || items.Count < 2) return 0;
            int removed = 0;
            for (int i = items.Count - 1; i > 0; i--)
            {
                var a = items[i];
                var b = items[i - 1];
                if (a == null || b == null) continue;
                if (string.Equals(a.Shortname, b.Shortname, StringComparison.Ordinal)
                    && a.Amount == b.Amount
                    && a.SkinId == b.SkinId)
                {
                    items.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        private static UIColors ExtractLegacyColors(JObject raw)
        {
            try
            {
                var colorsToken = raw?["Colors"];
                if (colorsToken == null || colorsToken.Type != JTokenType.Object) return null;
                return colorsToken.ToObject<UIColors>();
            }
            catch
            {
                return null;
            }
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            config.Themes = new Dictionary<string, UIColors>
            {
                ["Default"] = new UIColors(),
                ["Dark"] = new UIColors
                {
                    PanelBg = "0.04 0.04 0.05 0.98",
                    HeaderBg = "0.02 0.02 0.03 0.55",
                    ContentBg = "0.09 0.09 0.10 0.55",
                    ButtonPrimary = "0.85 0.85 0.88 0.95",
                    ButtonSecondary = "0.14 0.14 0.16 0.85",
                    TextTitle = "0.97 0.97 0.97 1.0",
                    TextNormal = "0.78 0.78 0.80 1.0",
                    TextMuted = "0.48 0.48 0.52 1.0"
                },
                ["Ocean"] = new UIColors
                {
                    PanelBg = "0.05 0.09 0.12 0.97",
                    HeaderBg = "0.02 0.05 0.07 0.55",
                    ContentBg = "0.08 0.13 0.17 0.55",
                    ButtonPrimary = "0.20 0.78 0.95 0.95",
                    ButtonSecondary = "0.10 0.20 0.26 0.85",
                    TextTitle = "0.95 0.98 1.00 1.0",
                    TextNormal = "0.82 0.90 0.94 1.0",
                    TextMuted = "0.48 0.62 0.72 1.0"
                },
                ["Rust"] = new UIColors
                {
                    PanelBg = "0.10 0.08 0.07 0.97",
                    HeaderBg = "0.06 0.05 0.04 0.55",
                    ContentBg = "0.16 0.13 0.11 0.55",
                    ButtonPrimary = "0.95 0.40 0.18 0.95",
                    ButtonSecondary = "0.22 0.18 0.15 0.85",
                    TextTitle = "0.97 0.93 0.88 1.0",
                    TextNormal = "0.84 0.78 0.72 1.0",
                    TextMuted = "0.60 0.52 0.45 1.0"
                },
                ["Midnight"] = new UIColors
                {
                    PanelBg = "0.06 0.05 0.10 0.97",
                    HeaderBg = "0.03 0.02 0.06 0.55",
                    ContentBg = "0.11 0.10 0.16 0.55",
                    ButtonPrimary = "0.62 0.40 0.98 0.95",
                    ButtonSecondary = "0.17 0.16 0.22 0.85",
                    TextTitle = "0.97 0.96 1.00 1.0",
                    TextNormal = "0.82 0.81 0.88 1.0",
                    TextMuted = "0.55 0.54 0.65 1.0"
                },
                ["Forest"] = new UIColors
                {
                    PanelBg = "0.06 0.09 0.07 0.97",
                    HeaderBg = "0.03 0.05 0.04 0.55",
                    ContentBg = "0.10 0.14 0.11 0.55",
                    ButtonPrimary = "0.40 0.85 0.55 0.95",
                    ButtonSecondary = "0.14 0.19 0.16 0.85",
                    TextTitle = "0.94 0.98 0.94 1.0",
                    TextNormal = "0.80 0.88 0.82 1.0",
                    TextMuted = "0.50 0.62 0.54 1.0"
                }
            };
        }
        #endregion

        #region Data Management
        private void OnServerInitialized()
        {
            RegisterAllImages();
        }

        private void RegisterAllImages()
        {
            if (ImageLibrary == null) return;

            var imageUrls = announcements
                .Where(x => !string.IsNullOrEmpty(x.ImageUrl))
                .Select(x => x.ImageUrl)
                .Distinct()
                .ToDictionary(x => x, x => x);

            if (imageUrls.Count > 0)
                ImageLibrary.Call("ImportImageList", Title, imageUrls, 0UL, true);
        }

        private void SaveAnnouncements()
        {
            if (storedData.Announcements.Count > config.General.MaxStoredAnnouncements)
                storedData.Announcements = storedData.Announcements.OrderByDescending(x => x.Timestamp).Take(config.General.MaxStoredAnnouncements).ToList();

            announcements = storedData.Announcements;
            Interface.Oxide.DataFileSystem.WriteObject(DataFile, storedData);
        }

        private void LoadAnnouncements()
        {
            try { storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFile); }
            catch (Exception ex)
            {
                PrintWarning($"Failed to read Data File! It may be corrupted or contain invalid JSON. Exception: {ex.Message}");
            }

            if (storedData == null || (storedData.Announcements == null && storedData.LastSeenNews == null))
            {
                try
                {
                    var oldList = Interface.Oxide.DataFileSystem.ReadObject<List<Announcement>>(DataFile);
                    storedData = new StoredData();
                    if (oldList != null) storedData.Announcements = oldList;
                }
                catch { storedData = new StoredData(); }
            }

            if (storedData.Announcements == null) storedData.Announcements = new List<Announcement>();
            if (storedData.LastSeenNews == null) storedData.LastSeenNews = new Dictionary<ulong, long>();

            announcements = storedData.Announcements;

            long baseTime = DateTime.UtcNow.Ticks;
            bool changed = false;
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < announcements.Count; i++)
            {
                if (announcements[i].Timestamp == 0)
                {

                    announcements[i].Timestamp = baseTime - (i * 10000);
                    changed = true;
                }

                if (announcements[i].LikedPlayers == null)
                {
                    announcements[i].LikedPlayers = new HashSet<ulong>();
                    changed = true;
                }
                if (announcements[i].ReadRewardedPlayers == null)
                {
                    announcements[i].ReadRewardedPlayers = new HashSet<ulong>();
                    changed = true;
                }
                if (announcements[i].LikeRewardedPlayers == null)
                {
                    announcements[i].LikeRewardedPlayers = new HashSet<ulong>();
                    changed = true;
                }
                if (announcements[i].ReadByPlayers == null)
                {

                    announcements[i].ReadByPlayers = new HashSet<ulong>(announcements[i].ReadRewardedPlayers);
                    changed = true;
                }

                if (string.IsNullOrEmpty(announcements[i].Id) || !seenIds.Add(announcements[i].Id))
                {
                    announcements[i].Id = NewAnnouncementId();
                    seenIds.Add(announcements[i].Id);
                    changed = true;
                }

                string normalizedText = NormalizeBodyText(announcements[i].Text);
                if (announcements[i].Text != normalizedText)
                {
                    announcements[i].Text = normalizedText;
                    changed = true;
                }
            }
            if (changed) SaveAnnouncements();
        }

        void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermView, this);
            LoadAnnouncements();
        }

        void OnPlayerSleepEnded(BasePlayer player)
        {
            if (!config.General.ShowNewsOnConnect || announcements.Count == 0) return;

            var latest = announcements[0];
            if (storedData.LastSeenNews.TryGetValue(player.userID, out long lastSeen) && lastSeen >= latest.Timestamp)
                return;

            timer.Once(2f, () =>
            {
                if (player == null || !player.IsConnected || announcements.Count == 0) return;
                var current = announcements[0];
                ShowPopup(player, current, false);
                storedData.LastSeenNews[player.userID] = current.Timestamp;
                SaveAnnouncements();
            });
        }

        void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUI(player);
                DestroyNotification(player);
                CuiHelper.DestroyUi(player, ConfirmLayer);
            }

            foreach (var t in autoCloseTimers.Values) t?.Destroy();
            foreach (var t in notificationTimers.Values) t?.Destroy();
            foreach (var s in readRewardTimers.Values) s?.Timer?.Destroy();
            autoCloseTimers.Clear();
            notificationTimers.Clear();
            readRewardTimers.Clear();
            playersWithUiOpen.Clear();
            historyContentScrollOffsets.Clear();
            activeEditors.Clear();
            activeEditorIds.Clear();
            adminSelections.Clear();
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            ulong id = player.userID;
            historyContentScrollOffsets.Remove(id);
            activeEditors.Remove(id);
            activeEditorIds.Remove(id);
            playersWithUiOpen.Remove(id);
            adminSelections.Remove(id);
            CancelReadRewardTimer(id);

            if (autoCloseTimers.TryGetValue(id, out var ac))
            {
                ac?.Destroy();
                autoCloseTimers.Remove(id);
            }
            if (notificationTimers.TryGetValue(id, out var nt))
            {
                nt?.Destroy();
                notificationTimers.Remove(id);
            }
        }
        #endregion

        #region Commands
        [ConsoleCommand("news.show")]
        private void CmdNewsShow(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin && !permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            string fullCommand = arg.FullString.ToString() ?? string.Empty;
            var rawMatches = CommandSplitRegex.Matches(fullCommand).Cast<Match>().ToList();
            var values = rawMatches.Select(m => m.Value.Trim('"')).ToList();

            if (values.Count < 3)
            {
                SendReply(arg, $"Error: Invalid arguments. Parsed {values.Count}, expected at least 3.\nUsage: news.show \"Title\" \"ImageURL\" \"Text\" [Type]");
                return;
            }

            string title = values[0];
            string img = values[1];
            if (img == "-") img = "";

            string text;
            AnnouncementType type = AnnouncementType.Info;

            bool lastWasUnquoted = rawMatches.Count > 0 && !rawMatches[rawMatches.Count - 1].Value.StartsWith("\"");
            if (values.Count > 3 && lastWasUnquoted && Enum.TryParse(values[values.Count - 1], true, out AnnouncementType parsedType))
            {
                type = parsedType;
                text = string.Join(" ", values.GetRange(2, values.Count - 3));
            }
            else
            {
                text = string.Join(" ", values.GetRange(2, values.Count - 2));
            }

            text = NormalizeBodyText(text);

            string authorName = arg.Connection != null ? arg.Connection.username : config.General.ServerName;

            var ann = new Announcement
            {
                Id = NewAnnouncementId(),
                Title = title,
                ImageUrl = img,
                Text = text,
                Date = DateTime.Now.ToString(InvariantDateFormat, CultureInfo.InvariantCulture),
                Author = authorName,
                Type = type,
                Timestamp = DateTime.UtcNow.Ticks
            };

            if (!string.IsNullOrEmpty(img) && ImageLibrary != null)
            {
                ImageLibrary.Call("AddImage", img, img, 0UL);
            }

            announcements.Insert(0, ann);
            SaveAnnouncements();

            Interface.CallHook("OnNewsBroadcast", BuildHookData(ann));
            SendToDiscord(ann);

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (config.Notification.Enabled)
                    ShowNotification(player, ann);
                else
                    ShowPopup(player, ann, false, true);
            }

            SendReply(arg, Msg("NewsBroadcasted"));
        }

        [ConsoleCommand("news.trigger")]
        private void CmdNewsTrigger(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin && !permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, "Usage: news.trigger <SteamID/Name> [NewsIndex]");
                return;
            }

            var player = BasePlayer.Find(arg.GetString(0));
            if (player == null || !player.IsConnected)
            {
                SendReply(arg, "Player not found.");
                return;
            }

            int index = arg.GetInt(1, 0);
            if (index < 0 || index >= announcements.Count) index = 0;

            if (announcements.Count > 0)
            {
                ShowPopup(player, announcements[index], true);
                SendReply(arg, $"News popup triggered for {player.displayName}");
            }
            else
            {
                SendReply(arg, "No announcements available.");
            }
        }

        [ConsoleCommand("news.delete")]
        private void CmdNewsDelete(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin && !permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, "Usage: news.delete <index>");
                return;
            }

            int index = arg.GetInt(0, -1);
            if (index >= 0 && index < announcements.Count)
            {
                var removed = announcements[index];
                announcements.RemoveAt(index);
                SaveAnnouncements();
                Interface.CallHook("OnNewsDeleted", BuildHookData(removed));
                SendReply(arg, $"Deleted announcement: '{removed.Title}'");
            }
            else
            {
                SendReply(arg, "Invalid index. Use 'news.list' to see all announcements with their indices.");
            }
        }

        [ConsoleCommand("news.list")]
        private void CmdNewsList(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin && !permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            if (announcements.Count == 0)
            {
                SendReply(arg, "No announcements stored.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== Stored Announcements ({announcements.Count}) ===");
            for (int i = 0; i < announcements.Count; i++)
            {
                var a = announcements[i];
                sb.AppendLine($"[{i}] [{a.Type}] \"{a.Title}\" — {a.Date} by {a.Author}");
            }
            SendReply(arg, sb.ToString().TrimEnd());
        }

        [ChatCommand("news")]
        private void CmdChatNews(BasePlayer player, string cmd, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermView))
            {
                SendReply(player, Msg("NoPermissionView", player));
                return;
            }

            ShowHistory(player, 0);
        }

        [ConsoleCommand("news.page")]
        private void CmdConsolePage(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;

            int page = arg.GetInt(0, 0);
            ShowHistory(player, page);
        }

        [ConsoleCommand("news.view")]
        private void CmdConsoleView(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null) return;

            historyContentScrollOffsets[player.userID] = 0;
            ShowPopup(player, ann, true);
        }

        [ConsoleCommand("news.scrollbody")]
        private void CmdScrollBody(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null) return;
            int offset = arg.GetInt(1, 0);

            historyContentScrollOffsets[player.userID] = ClampBodyOffset(ann.Text, offset, BodyWrapFor(ann));
            ShowPopup(player, ann, true);
        }

        [ConsoleCommand("news.close")]
        private void CmdConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player != null)
            {
                historyContentScrollOffsets.Remove(player.userID);
                DestroyUI(player);
            }
        }

        [ConsoleCommand("news.close.notif")]
        private void CmdCloseNotif(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player != null) DestroyNotification(player);
        }

        [ConsoleCommand("news.admin")]
        private void CmdNewsAdmin(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin && !permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;

            ShowAdminList(player, 0);
        }

        [ConsoleCommand("news.admin.page")]
        private void CmdNewsAdminPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }
            ShowAdminList(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.create")]
        private void CmdNewsAdminCreate(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            activeEditors[player.userID] = new Announcement
            {
                Title = "New Announcement",
                Text = "Enter text here...",
                Type = AnnouncementType.Info,
                Author = player.displayName,
                Date = DateTime.Now.ToString(InvariantDateFormat, CultureInfo.InvariantCulture),
                ImageUrl = ""
            };
            activeEditorIds[player.userID] = string.Empty;
            ShowEditor(player);
        }

        [ConsoleCommand("news.admin.edit")]
        private void CmdNewsAdminEdit(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            var original = FindById(arg.GetString(0));
            if (original == null) return;

            activeEditors[player.userID] = new Announcement
            {
                Id = original.Id,
                Title = original.Title,
                ImageUrl = original.ImageUrl,
                Text = original.Text,
                Date = original.Date,
                Author = original.Author,
                Type = original.Type,
                Timestamp = original.Timestamp,
                LikedPlayers = new HashSet<ulong>(original.LikedPlayers)
            };
            activeEditorIds[player.userID] = original.Id;
            ShowEditor(player);
        }

        [ConsoleCommand("news.admin.togglepin")]
        private void CmdNewsAdminTogglePin(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null) return;

            ann.Pinned = !ann.Pinned;
            SaveAnnouncements();
            Interface.CallHook("OnNewsEdited", BuildHookData(ann));
            ShowAdminList(player, arg.GetInt(1, 0));
        }

        [ConsoleCommand("news.admin.toggleselect")]
        private void CmdNewsAdminToggleSelect(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            string id = arg.GetString(0);
            if (string.IsNullOrEmpty(id) || FindById(id) == null) return;

            var sel = GetAdminSelection(player.userID);
            if (!sel.Remove(id)) sel.Add(id);

            ShowAdminList(player, arg.GetInt(1, 0));
        }

        [ConsoleCommand("news.admin.selectpage")]
        private void CmdNewsAdminSelectPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            int page = arg.GetInt(0, 0);
            int perPage = config.General.AnnouncementsPerPage;
            var displayList = GetDisplayOrder();
            var sel = GetAdminSelection(player.userID);

            int start = page * perPage;
            int end = Math.Min(start + perPage, displayList.Count);

            bool allSelected = true;
            for (int i = start; i < end; i++) { if (!sel.Contains(displayList[i].Id)) { allSelected = false; break; } }

            for (int i = start; i < end; i++)
            {
                if (allSelected) sel.Remove(displayList[i].Id);
                else sel.Add(displayList[i].Id);
            }

            ShowAdminList(player, page);
        }

        [ConsoleCommand("news.admin.clearsel")]
        private void CmdNewsAdminClearSel(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            adminSelections.Remove(player.userID);
            ShowAdminList(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.bulkpin")]
        private void CmdNewsAdminBulkPin(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            bool pin = arg.GetString(0) != "0";
            int page = arg.GetInt(1, 0);

            PruneAdminSelection(player.userID);
            var sel = GetAdminSelection(player.userID);
            if (sel.Count == 0) { ShowAdminList(player, page); return; }

            int changed = 0;
            foreach (var id in sel.ToList())
            {
                var ann = FindById(id);
                if (ann == null || ann.Pinned == pin) continue;
                ann.Pinned = pin;
                changed++;
                Interface.CallHook("OnNewsEdited", BuildHookData(ann));
            }

            if (changed > 0)
            {
                SaveAnnouncements();
                SendReply(player, Msg(pin ? "BulkPinned" : "BulkUnpinned", player, changed));
            }
            ShowAdminList(player, page);
        }

        [ConsoleCommand("news.admin.bulkdelconfirm")]
        private void CmdNewsAdminBulkDelConfirm(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            PruneAdminSelection(player.userID);
            ShowBulkDeleteConfirm(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.bulkdel")]
        private void CmdNewsAdminBulkDel(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            PruneAdminSelection(player.userID);
            var sel = GetAdminSelection(player.userID);
            if (sel.Count == 0)
            {
                CuiHelper.DestroyUi(player, ConfirmLayer);
                ShowAdminList(player, arg.GetInt(0, 0));
                return;
            }

            int removed = 0;
            foreach (var id in sel.ToList())
            {
                int idx = FindIndexById(id);
                if (idx < 0) continue;
                var victim = announcements[idx];
                announcements.RemoveAt(idx);
                Interface.CallHook("OnNewsDeleted", BuildHookData(victim));
                removed++;
            }

            adminSelections.Remove(player.userID);
            if (removed > 0) SaveAnnouncements();
            CuiHelper.DestroyUi(player, ConfirmLayer);
            SendReply(player, Msg("BulkDeleted", player, removed));
            ShowAdminList(player, 0);
        }

        [ConsoleCommand("news.admin.del")]
        private void CmdNewsAdminDelete(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            int index = FindIndexById(arg.GetString(0));
            if (index >= 0)
            {
                var removed = announcements[index];
                announcements.RemoveAt(index);
                SaveAnnouncements();
                Interface.CallHook("OnNewsDeleted", BuildHookData(removed));
                CuiHelper.DestroyUi(player, ConfirmLayer);
                ShowAdminList(player, 0);
            }
        }

        [ConsoleCommand("news.editor.input")]
        private void CmdEditorInput(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;
            if (!activeEditors.ContainsKey(player.userID)) return;

            if (arg.Args == null || arg.Args.Length < 1) return;

            string field = arg.GetString(0).ToLowerInvariant();

            string fullStr = arg.FullString.ToString() ?? string.Empty;
            string value;
            if (fullStr.StartsWith(field, StringComparison.OrdinalIgnoreCase))
            {
                value = fullStr.Substring(field.Length).TrimStart();
                if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
                    value = value.Substring(1, value.Length - 2);
            }
            else
            {
                value = string.Join(" ", arg.Args.Skip(1));
            }

            var ann = activeEditors[player.userID];
            switch (field)
            {
                case "title": ann.Title = value; break;
                case "text": ann.Text = NormalizeBodyText(value); break;
                case "image": ann.ImageUrl = value; break;
            }

            ShowEditor(player);
        }

        [ConsoleCommand("news.editor.type")]
        private void CmdEditorType(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;
            if (!activeEditors.ContainsKey(player.userID)) return;

            var ann = activeEditors[player.userID];
            int current = (int)ann.Type;
            int next = (current + 1) % Enum.GetValues(typeof(AnnouncementType)).Length;
            ann.Type = (AnnouncementType)next;

            ShowEditor(player);
        }

        [ConsoleCommand("news.editor.save")]
        private void CmdEditorSave(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;
            if (!activeEditors.ContainsKey(player.userID)) return;

            var ann = activeEditors[player.userID];
            ann.Text = NormalizeBodyText(ann.Text);
            activeEditorIds.TryGetValue(player.userID, out string editingId);
            bool isNew = string.IsNullOrEmpty(editingId);

            if (isNew)
            {
                ann.Id = NewAnnouncementId();
                ann.Timestamp = DateTime.UtcNow.Ticks;
                ann.Date = DateTime.Now.ToString(InvariantDateFormat, CultureInfo.InvariantCulture);
                announcements.Insert(0, ann);
                SendToDiscord(ann);
                Interface.CallHook("OnNewsBroadcast", BuildHookData(ann));
            }
            else
            {
                int idx = FindIndexById(editingId);
                if (idx >= 0)
                {
                    ann.Id = editingId;
                    announcements[idx] = ann;
                    Interface.CallHook("OnNewsEdited", BuildHookData(ann));
                }
                else
                {
                    SendReply(player, Msg("EditTargetGone", player));
                    activeEditors.Remove(player.userID);
                    activeEditorIds.Remove(player.userID);
                    ShowAdminList(player, 0);
                    return;
                }
            }

            SaveAnnouncements();
            if (!string.IsNullOrEmpty(ann.ImageUrl) && ImageLibrary != null)
                ImageLibrary.Call("AddImage", ann.ImageUrl, ann.ImageUrl, 0UL);

            if (isNew)
            {
                foreach (var p in BasePlayer.activePlayerList)
                {
                    if (config.Notification.Enabled)
                        ShowNotification(p, ann);
                    else
                        ShowPopup(p, ann, false, true);
                }
            }

            activeEditors.Remove(player.userID);
            activeEditorIds.Remove(player.userID);

            ShowAdminList(player, 0);
            SendReply(player, isNew ? Msg("AnnouncementSavedNew", player) : Msg("AnnouncementUpdated", player));
        }

        [ConsoleCommand("news.editor.cancel")]
        private void CmdEditorCancel(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            activeEditors.Remove(player.userID);
            activeEditorIds.Remove(player.userID);
            ShowAdminList(player, 0);
        }
        #endregion

        #region Notification UI
        [ConsoleCommand("news.like")]
        private void CmdNewsLike(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null) return;

            bool addedLike;
            if (ann.LikedPlayers.Remove(player.userID))
            {
                addedLike = false;
            }
            else
            {
                ann.LikedPlayers.Add(player.userID);
                addedLike = true;
            }

            if (addedLike && config.Rewards != null && config.Rewards.EnableLikeReward)
            {
                if (ann.LikeRewardedPlayers == null) ann.LikeRewardedPlayers = new HashSet<ulong>();
                if (ann.LikeRewardedPlayers.Add(player.userID))
                {
                    GiveRewards(player, config.Rewards.LikeRewards, "RewardLike");
                }
            }

            SaveAnnouncements();
            Interface.CallHook("OnNewsLiked", player, BuildHookData(ann), addedLike);
            ShowPopup(player, ann, true, false);
        }

        private void DestroyNotification(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, NotificationLayer);
            if (notificationTimers.ContainsKey(player.userID))
            {
                notificationTimers[player.userID]?.Destroy();
                notificationTimers.Remove(player.userID);
            }
        }

        private void ShowNotification(BasePlayer player, Announcement ann)
        {
            string safeTitle = ann.Title ?? string.Empty;

            if (config.Notification.UseNotifyPlugin && Notify != null && Notify.IsLoaded)
            {
                Notify.Call("SendNotify", player.UserIDString, config.Notification.NotifyType, safeTitle);
                return;
            }

            DestroyNotification(player);

            string anchorMin, anchorMax;
            bool isLeft = string.Equals(config.Notification.Position, "left", StringComparison.OrdinalIgnoreCase);

            if (isLeft)
            {
                anchorMin = "0.01 0.85";
                anchorMax = "0.15 0.93";
            }
            else
            {
                anchorMin = "0.84 0.85";
                anchorMax = "0.98 0.93";
            }

            var container = new CuiElementContainer();
            var c = config.Colors;
            string typeColor = GetTypeColor(ann.Type);

            container.Add(new CuiButton
            {
                Button = { Color = "0.05 0.06 0.08 0.92", Command = $"news.view {ann.Id}", FadeIn = 0.30f },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Text = { Text = "" }
            }, "Hud", NotificationLayer);

            container.Add(new CuiPanel
            {
                Image = { Color = typeColor, FadeIn = 0.30f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.018 1" }
            }, NotificationLayer);

            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.05", FadeIn = 0.30f },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "1 0.015" }
            }, NotificationLayer);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("NewAnnouncement", player), FontSize = 8, Align = TextAnchor.LowerLeft, Color = c.ButtonPrimary, Font = "robotocondensed-bold.ttf", FadeIn = 0.30f },
                RectTransform = { AnchorMin = "0.06 0.65", AnchorMax = "0.9 0.9" }
            }, NotificationLayer);

            string title = safeTitle.Length > 25 ? safeTitle.Substring(0, 22) + "..." : safeTitle;
            container.Add(new CuiLabel
            {
                Text = { Text = title, FontSize = 12, Align = TextAnchor.UpperLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf", FadeIn = 0.30f },
                RectTransform = { AnchorMin = "0.06 0.1", AnchorMax = "0.9 0.65" }
            }, NotificationLayer);

            container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "news.close.notif" },
                Text = { Text = "✕", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, FadeIn = 0.30f },
                RectTransform = { AnchorMin = "0.90 0.65", AnchorMax = "0.98 0.95" }
            }, NotificationLayer);

            CuiHelper.AddUi(player, container);

            if (!string.IsNullOrEmpty(config.Notification.NotificationSound))
            {
                Effect.server.Run(config.Notification.NotificationSound, player.transform.position);
            }

            notificationTimers[player.userID] = timer.Once(config.Notification.Duration, () =>
            {
                if (player != null && player.IsConnected) DestroyNotification(player);
            });
        }
        #endregion

        #region UI Generation
        private void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, LayerName);
            playersWithUiOpen.Remove(player.userID);

            if (autoCloseTimers.ContainsKey(player.userID))
            {
                autoCloseTimers[player.userID]?.Destroy();
                autoCloseTimers.Remove(player.userID);
            }

            CancelReadRewardTimer(player.userID);
        }

        private void ShowPopup(BasePlayer player, Announcement ann, bool fromHistory = false, bool playSound = false)
        {
            DestroyUI(player);
            DestroyNotification(player);
            playersWithUiOpen.Add(player.userID);

            var container = new CuiElementContainer();
            var c = config.Colors;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.78", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            string mainPanel = LayerName + ".Main";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg, FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.175 0.175", AnchorMax = "0.825 0.825" }
            }, LayerName, mainPanel);

            container.Add(new CuiPanel
            {
                Image = { Color = c.HeaderBg, FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" }
            }, mainPanel);

            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.06", FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0 0.919", AnchorMax = "1 0.921" }
            }, mainPanel);

            if (ann.Pinned)
            {
                const string pinGold     = "0.95 0.70 0.20 0.95";
                const string pinGoldText = "0.05 0.05 0.05 1";

                container.Add(new CuiPanel { Image = { Color = pinGold }, RectTransform = { AnchorMin = "0 0.997", AnchorMax = "1 1"     } }, mainPanel);
                container.Add(new CuiPanel { Image = { Color = pinGold }, RectTransform = { AnchorMin = "0 0",     AnchorMax = "1 0.003" } }, mainPanel);
                container.Add(new CuiPanel { Image = { Color = pinGold }, RectTransform = { AnchorMin = "0 0",     AnchorMax = "0.0015 1" } }, mainPanel);
                container.Add(new CuiPanel { Image = { Color = pinGold }, RectTransform = { AnchorMin = "0.9985 0", AnchorMax = "1 1"    } }, mainPanel);

                container.Add(new CuiPanel
                {
                    Image = { Color = pinGold },
                    RectTransform = { AnchorMin = "0.83 0.905", AnchorMax = "0.93 0.985" }
                }, mainPanel);
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("PinnedBadge", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = pinGoldText, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.83 0.905", AnchorMax = "0.93 0.985" }
                }, mainPanel);
            }

            string headerText = $"{config.General.ServerName} <color={RgbaToHex(c.ButtonPrimary)}>//</color> {ann.Type.ToString().ToUpper()}";

            container.Add(new CuiLabel
            {
                Text = { Text = headerText, FontSize = 14, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.03 0.92", AnchorMax = "0.8 1" }
            }, mainPanel);

            container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "news.close" },
                Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.94 0.92", AnchorMax = "0.99 1" }
            }, mainPanel);

            bool hasImage = !string.IsNullOrEmpty(ann.ImageUrl);

            float contentLeft = hasImage ? 0.42f : 0.05f;

            if (hasImage)
            {
                string imgPanel = mainPanel + ".Img";

                container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0.3" },
                    RectTransform = { AnchorMin = "0.02 0.12", AnchorMax = "0.38 0.88" }
                }, mainPanel, imgPanel);

                var imgComp = new CuiRawImageComponent { Color = "1 1 1 1" };
                string imgId = GetImage(ann.ImageUrl);
                if (!string.IsNullOrEmpty(imgId))
                    imgComp.Png = imgId;
                else
                    imgComp.Url = ann.ImageUrl;

                container.Add(new CuiElement
                {
                    Parent = imgPanel,
                    Components =
                    {
                        imgComp,
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                });
            }

            string titleLabelName = mainPanel + ".TitleText";
            container.Add(new CuiLabel
            {
                Text = { Text = (ann.Title ?? "").ToUpper(), FontSize = 32, Align = TextAnchor.LowerLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = $"{contentLeft} 0.80", AnchorMax = "0.98 0.90" }
            }, mainPanel, titleLabelName);

            container.Add(new CuiElement
            {
                Parent = titleLabelName,
                Components =
                {
                    new CuiOutlineComponent { Color = "0 0 0 1", Distance = "1 1" }
                }
            });

            container.Add(new CuiPanel
            {
                 Image = { Color = c.ButtonPrimary },
                 RectTransform = { AnchorMin = $"{contentLeft} 0.79", AnchorMax = $"{contentLeft + 0.15f} 0.795" }
            }, mainPanel);

            int bodyWrap = BodyWrapFor(ann);

            int currentOffset = 0;
            historyContentScrollOffsets.TryGetValue(player.userID, out currentOffset);
            currentOffset = ClampBodyOffset(ann.Text, currentOffset, bodyWrap);
            historyContentScrollOffsets[player.userID] = currentOffset;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.15" },
                RectTransform = { AnchorMin = $"{contentLeft} 0.12", AnchorMax = "0.94 0.78" }
            }, mainPanel, mainPanel + ".TextViewport");

            string bodyLabelName = mainPanel + ".BodyText";
            container.Add(new CuiLabel
            {
                Text = { Text = GetVisibleBodySlice(ann.Text, currentOffset, bodyWrap), FontSize = 16, Align = TextAnchor.UpperLeft, Color = c.TextNormal, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = $"{contentLeft + 0.02f} 0.13", AnchorMax = "0.925 0.77" }
            }, mainPanel, bodyLabelName);

            container.Add(new CuiElement
            {
                Parent = bodyLabelName,
                Components =
                {
                    new CuiOutlineComponent { Color = "0 0 0 0.95", Distance = "0.9 0.9", UseGraphicAlpha = true }
                }
            });

            if (CanScrollBody(ann.Text, bodyWrap))
            {

                int maxOffset = GetBodyMaxOffset(ann.Text, bodyWrap);
                int pageUp     = ClampBodyOffset(ann.Text, currentOffset - BodyVisibleLineCount, bodyWrap);
                int pageDown   = ClampBodyOffset(ann.Text, currentOffset + BodyVisibleLineCount, bodyWrap);
                string annId   = ann.Id;

                const float trackLeft   = 0.955f;
                const float trackRight  = 0.975f;
                const float trackBottom = 0.135f;
                const float trackTop    = 0.765f;

                container.Add(new CuiPanel
                {
                    Image = { Color = "1 1 1 0.05", FadeIn = 0.18f },
                    RectTransform = { AnchorMin = $"{trackLeft} {trackBottom}", AnchorMax = $"{trackRight} {trackTop}" }
                }, mainPanel);

                if (maxOffset > 0)
                {
                    const int jumpZones = 16;
                    float zoneH = (trackTop - trackBottom) / jumpZones;
                    for (int z = 0; z < jumpZones; z++)
                    {
                        float zMin = trackBottom + z * zoneH;
                        float zMax = zMin + zoneH;
                        int targetOffset = ClampBodyOffset(ann.Text, Mathf.RoundToInt((float)(jumpZones - 1 - z) / (jumpZones - 1) * maxOffset), bodyWrap);
                        container.Add(new CuiButton
                        {
                            Button = { Color = "0 0 0 0", Command = $"news.scrollbody {annId} {targetOffset}" },
                            Text = { Text = "" },
                            RectTransform = { AnchorMin = $"{trackLeft} {zMin:F3}", AnchorMax = $"{trackRight} {zMax:F3}" }
                        }, mainPanel);
                    }
                }

                var allLines = BuildBodyDisplayLines(ann.Text, bodyWrap);
                float windowRatio = allLines.Count <= 0 ? 1f : Mathf.Clamp((float)BodyVisibleLineCount / allLines.Count, 0.12f, 0.90f);
                float trackH      = trackTop - trackBottom;
                float handleH     = trackH * windowRatio;
                float usable      = trackH - handleH;
                float progress    = maxOffset <= 0 ? 0f : Mathf.Clamp01((float)currentOffset / maxOffset);
                float handleMin   = trackTop - handleH - usable * progress;
                float handleMax   = handleMin + handleH;

                container.Add(new CuiPanel
                {
                    Image = { Color = c.ButtonPrimary, FadeIn = 0.18f },
                    RectTransform = { AnchorMin = $"{trackLeft} {handleMin:F4}", AnchorMax = $"{trackRight} {handleMax:F4}" }
                }, mainPanel);

                bool canUp = currentOffset > 0;
                bool canDown = currentOffset < maxOffset;

                const float chevLeft  = 0.943f;
                const float chevRight = 0.953f;
                if (canUp)
                {
                    container.Add(new CuiButton
                    {
                        Button = { Color = "1 1 1 0.06", Command = $"news.scrollbody {annId} {pageUp}" },
                        Text = { Text = "▲", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-bold.ttf", FadeIn = 0.18f },
                        RectTransform = { AnchorMin = $"{chevLeft} {trackTop - 0.030f:F3}", AnchorMax = $"{chevRight} {trackTop:F3}" }
                    }, mainPanel);
                }
                if (canDown)
                {
                    container.Add(new CuiButton
                    {
                        Button = { Color = "1 1 1 0.06", Command = $"news.scrollbody {annId} {pageDown}" },
                        Text = { Text = "▼", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-bold.ttf", FadeIn = 0.18f },
                        RectTransform = { AnchorMin = $"{chevLeft} {trackBottom:F3}", AnchorMax = $"{chevRight} {trackBottom + 0.030f:F3}" }
                    }, mainPanel);
                }

                int firstLine = currentOffset + 1;
                int lastLine  = Math.Min(currentOffset + BodyVisibleLineCount, allLines.Count);
                container.Add(new CuiLabel
                {
                    Text = { Text = $"{firstLine}–{lastLine} / {allLines.Count}", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "0.55 0.55 0.6 0.75", Font = "robotocondensed-regular.ttf", FadeIn = 0.18f },
                    RectTransform = { AnchorMin = "0.910 0.015", AnchorMax = "0.988 0.075" }
                }, mainPanel);
            }

             container.Add(new CuiPanel
            {
                Image = { Color = c.HeaderBg },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.10" }
            }, mainPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{Msg("PostedBy", player)} <color={RgbaToHex(c.ButtonPrimary)}>{(ann.Author ?? Msg("Unknown", player)).ToUpper()}</color> • {ann.Date}", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0.03 0", AnchorMax = "0.6 0.10" }
            }, mainPanel);

             container.Add(new CuiButton
            {
                Button = { Color = c.ButtonPrimary, Command = "news.page 0" },
                Text = { Text = Msg("ViewArchive", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.82 0.015", AnchorMax = "0.98 0.085" }
            }, mainPanel);

            bool liked = ann.LikedPlayers.Contains(player.userID);
            string heartColor = liked ? "0.8 0.25 0.25 1" : "0.6 0.6 0.6 1";
            int likeCount = ann.LikedPlayers.Count;

            if (!string.IsNullOrEmpty(ann.Id))
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "0 0 0 0", Command = $"news.like {ann.Id}" },
                    Text = { Text = $"❤ {likeCount}", FontSize = 12, Align = TextAnchor.MiddleRight, Color = heartColor, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.65 0.015", AnchorMax = "0.80 0.085" }
                }, mainPanel);
            }

            CuiHelper.AddUi(player, container);

            if (playSound && !string.IsNullOrEmpty(config.Notification.NotificationSound))
            {
                Effect.server.Run(config.Notification.NotificationSound, player.transform.position);
            }

            if (config.General.EnableAutoClose && !fromHistory)
            {
                if (autoCloseTimers.ContainsKey(player.userID)) autoCloseTimers[player.userID]?.Destroy();

                autoCloseTimers[player.userID] = timer.Once(config.General.AutoCloseSeconds, () =>
                {
                    if (player != null && player.IsConnected && playersWithUiOpen.Contains(player.userID))
                    {
                        DestroyUI(player);
                    }
                });
            }

            ScheduleReadCompletion(player, ann);
        }

        private void ShowHistory(BasePlayer player, int page)
        {
            DestroyUI(player);

            if (announcements.Count == 0)
            {
                SendReply(player, Msg("NoNewsHistory", player));
                return;
            }

            playersWithUiOpen.Add(player.userID);

            var container = new CuiElementContainer();
            var c = config.Colors;

            var displayList = GetDisplayOrder();
            int perPage = config.General.AnnouncementsPerPage;
            int totalPages = Mathf.CeilToInt((float)displayList.Count / perPage);
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.78", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            string mainPanel = LayerName + ".List";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg, FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.15 0.1", AnchorMax = "0.85 0.9" }
            }, LayerName, mainPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" } }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.919", AnchorMax = "1 0.921" } }, mainPanel);

            container.Add(new CuiLabel {
                Text = { Text = $"{config.General.ServerName} <color={RgbaToHex(c.ButtonPrimary)}>//</color> {Msg("ArchiveTitle", player)}", FontSize = 16, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.03 0.92", AnchorMax = "0.8 1" }
            }, mainPanel);

            container.Add(new CuiButton {
                Button = { Color = "0.8 0.2 0.2 0", Command = "news.close" },
                Text = { Text = Msg("Close", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.92 0.92", AnchorMax = "1 1" }
            }, mainPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.08" } }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.080", AnchorMax = "1 0.082" } }, mainPanel);

            int start = page * perPage;
            int count = 0;
            float rowHeight = 0.82f / perPage;
            float padding = 0.015f;

            for (int i = start; i < displayList.Count && count < perPage; i++)
            {
                var ann = displayList[i];
                float top = 0.90f - (count * rowHeight) - padding;
                float bottom = top - rowHeight + (padding * 2);

                string itemPanel = mainPanel + $".{i}";
                string typeColor = GetTypeColor(ann.Type);

                float rowFade = 0.18f + count * 0.04f;

                container.Add(new CuiPanel
                {
                    Image = { Color = c.ContentBg, FadeIn = rowFade },
                    RectTransform = { AnchorMin = $"0.02 {bottom}", AnchorMax = $"0.98 {top}" }
                }, mainPanel, itemPanel);

                if (ann.Pinned)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.95 0.70 0.20 0.10" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }, itemPanel);
                }

                container.Add(new CuiPanel {
                    Image = { Color = typeColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.005 1" }
                }, itemPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = (ann.Title ?? "(no title)").ToUpper(), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.025 0.55", AnchorMax = "0.62 0.9" }
                }, itemPanel);

                if (ann.Pinned)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.95 0.70 0.20 0.95" },
                        RectTransform = { AnchorMin = "0.63 0.62", AnchorMax = "0.74 0.86" }
                    }, itemPanel);
                    container.Add(new CuiLabel
                    {
                        Text = { Text = Msg("PinnedBadge", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.05 0.05 0.05 1", Font = "robotocondensed-bold.ttf" },
                        RectTransform = { AnchorMin = "0.63 0.62", AnchorMax = "0.74 0.86" }
                    }, itemPanel);
                }

                container.Add(new CuiLabel
                {
                    Text = { Text = ann.Date, FontSize = 10, Align = TextAnchor.MiddleRight, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.75 0.55", AnchorMax = "0.86 0.9" }
                }, itemPanel);

                string rawPreview = (ann.Text ?? "").Replace("\n", " ");
                string preview = rawPreview.Length > 80 ? rawPreview.Substring(0, 77) + "..." : rawPreview;
                container.Add(new CuiLabel
                {
                    Text = { Text = preview, FontSize = 11, Align = TextAnchor.UpperLeft, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.025 0.1", AnchorMax = "0.8 0.55" }
                }, itemPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonPrimary, Command = $"news.view {ann.Id}" },
                    Text = { Text = Msg("ReadMore", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.88 0.15", AnchorMax = "0.98 0.45" }
                }, itemPanel);

                count++;
            }

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("Page", player, page + 1, totalPages), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0.4 0", AnchorMax = "0.6 0.08" }
            }, mainPanel);

            if (page > 0)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonSecondary, Command = $"news.page {page - 1}" },
                    Text = { Text = Msg("Previous", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.02 0.01", AnchorMax = "0.15 0.07" }
                }, mainPanel);
            }

            if (page < totalPages - 1)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonSecondary, Command = $"news.page {page + 1}" },
                    Text = { Text = Msg("Next", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.85 0.01", AnchorMax = "0.98 0.07" }
                }, mainPanel);
            }

            CuiHelper.AddUi(player, container);
        }

        private string GetTypeColor(AnnouncementType type)
        {
            switch (type)
            {
                case AnnouncementType.Alert: return "0.85 0.25 0.25 1";
                case AnnouncementType.Warning: return "0.9 0.6 0.1 1";
                case AnnouncementType.Update: return "0.2 0.7 0.9 1";
                case AnnouncementType.Event: return "0.6 0.3 0.8 1";
                default: return "0.4 0.6 0.8 1";
            }
        }

        private string GetImage(string url)
        {
            if (ImageLibrary != null && !string.IsNullOrEmpty(url))
            {
                return ImageLibrary.Call<string>("GetImage", url);
            }
            return null;
        }

        private string RgbaToHex(string rgba)
        {
            try
            {
                var parts = rgba.Split(' ');
                int r = Mathf.Clamp(Mathf.RoundToInt(float.Parse(parts[0], CultureInfo.InvariantCulture) * 255), 0, 255);
                int g = Mathf.Clamp(Mathf.RoundToInt(float.Parse(parts[1], CultureInfo.InvariantCulture) * 255), 0, 255);
                int b = Mathf.Clamp(Mathf.RoundToInt(float.Parse(parts[2], CultureInfo.InvariantCulture) * 255), 0, 255);
                return $"#{r:X2}{g:X2}{b:X2}";
            }
            catch { return "#FFFFFF"; }
        }
        #endregion

        #region Admin UI
        private void ShowAdminList(BasePlayer player, int page)
        {
            DestroyUI(player);
            playersWithUiOpen.Add(player.userID);

            var container = new CuiElementContainer();
            var c = config.Colors;

            int perPage = config.General.AnnouncementsPerPage;
            int totalPages = Mathf.CeilToInt((float)announcements.Count / perPage);
            if (totalPages == 0) totalPages = 1;
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.85", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            string mainPanel = LayerName + ".Admin";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg, FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.15 0.15", AnchorMax = "0.85 0.85" }
            }, LayerName, mainPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.90", AnchorMax = "1 1" } }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.899", AnchorMax = "1 0.901" } }, mainPanel);

            container.Add(new CuiLabel {
                Text = { Text = $"{config.General.ServerName} <color={RgbaToHex(c.ButtonPrimary)}>//</color> {Msg("AdminControl", player)}", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.03 0.90", AnchorMax = "0.6 1" }
            }, mainPanel);

            container.Add(new CuiButton {
                Button = { Color = c.ButtonPrimary, Command = "news.admin.create" },
                Text = { Text = Msg("NewPost", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.75 0.92", AnchorMax = "0.88 0.98" }
            }, mainPanel);

            container.Add(new CuiButton {
                Button = { Color = c.ButtonSecondary, Command = "news.admin.themes" },
                Text = { Text = Msg("Themes", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.61 0.92", AnchorMax = "0.74 0.98" }
            }, mainPanel);

            container.Add(new CuiButton {
                Button = { Color = "0.8 0.2 0.2 0", Command = "news.close" },
                Text = { Text = "✕", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.94 0.90", AnchorMax = "0.99 1" }
            }, mainPanel);

            var displayList = GetDisplayOrder();

            totalPages = Mathf.CeilToInt((float)displayList.Count / perPage);
            if (totalPages == 0) totalPages = 1;
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;

            PruneAdminSelection(player.userID);
            var selection = GetAdminSelection(player.userID);
            int start = page * perPage;
            int count = 0;
            float rowHeight = 0.82f / perPage;
            float padding = 0.01f;

            if (displayList.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("NoAnnouncementsYet", player), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.1 0.3", AnchorMax = "0.9 0.7" }
                }, mainPanel);
            }

            for (int i = start; i < displayList.Count && count < perPage; i++)
            {
                var ann = displayList[i];
                float top = 0.88f - (count * rowHeight) - padding;
                float bottom = top - rowHeight + (padding * 2);

                string itemPanel = mainPanel + $".{i}";
                string typeColor = GetTypeColor(ann.Type);
                bool selected = selection.Contains(ann.Id);

                float adminRowFade = 0.18f + count * 0.04f;
                container.Add(new CuiPanel
                {
                    Image = { Color = c.ContentBg, FadeIn = adminRowFade },
                    RectTransform = { AnchorMin = $"0.02 {bottom}", AnchorMax = $"0.98 {top}" }
                }, mainPanel, itemPanel);

                if (ann.Pinned)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.95 0.70 0.20 0.10", FadeIn = adminRowFade },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }, itemPanel);
                }

                container.Add(new CuiButton
                {
                    Button = { Color = selected ? c.ButtonPrimary : "0.2 0.2 0.2 0.9", Command = $"news.admin.toggleselect {ann.Id} {page}" },
                    Text = { Text = selected ? "✓" : "", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.005 0.2", AnchorMax = "0.04 0.8" }
                }, itemPanel);

                container.Add(new CuiPanel {
                    Image = { Color = typeColor },
                    RectTransform = { AnchorMin = "0.045 0", AnchorMax = "0.05 1" }
                }, itemPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = (ann.Title ?? "(no title)").ToUpper(), FontSize = 12, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.06 0.4", AnchorMax = "0.62 0.9" }
                }, itemPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = ann.Date ?? "", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted },
                    RectTransform = { AnchorMin = "0.06 0.1", AnchorMax = "0.62 0.4" }
                }, itemPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = ann.Pinned ? "0.95 0.70 0.20 1" : "0.35 0.35 0.4 0.9", Command = $"news.admin.togglepin {ann.Id} {page}" },
                    Text = { Text = ann.Pinned ? Msg("UnpinButton", player) : Msg("PinButton", player), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = ann.Pinned ? "0.05 0.05 0.05 1" : "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.63 0.2", AnchorMax = "0.74 0.8" }
                }, itemPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.2 0.4 0.6 0.8", Command = $"news.admin.edit {ann.Id}" },
                    Text = { Text = Msg("EditButton", player), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.75 0.2", AnchorMax = "0.85 0.8" }
                }, itemPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.6 0.2 0.2 0.8", Command = $"news.admin.delconfirm {ann.Id}" },
                    Text = { Text = Msg("DelButton", player), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.86 0.2", AnchorMax = "0.96 0.8" }
                }, itemPanel);

                count++;
            }

            if (selection.Count > 0)
            {
                string bulkPanel = mainPanel + ".Bulk";
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.10 0.10 0.12 0.95" },
                    RectTransform = { AnchorMin = "0.02 0.085", AnchorMax = "0.98 0.155" }
                }, mainPanel, bulkPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("SelectedCount", player, selection.Count), FontSize = 11, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.18 1" }
                }, bulkPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.65 0.12 0.12 1", Command = $"news.admin.bulkdelconfirm {page}" },
                    Text = { Text = Msg("BulkDelete", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.19 0.15", AnchorMax = "0.32 0.85" }
                }, bulkPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonPrimary, Command = $"news.admin.bulkpin 1 {page}" },
                    Text = { Text = Msg("BulkPin", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.33 0.15", AnchorMax = "0.46 0.85" }
                }, bulkPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.35 0.35 0.4 0.9", Command = $"news.admin.bulkpin 0 {page}" },
                    Text = { Text = Msg("BulkUnpin", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.47 0.15", AnchorMax = "0.60 0.85" }
                }, bulkPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonSecondary, Command = $"news.admin.clearsel {page}" },
                    Text = { Text = Msg("ClearSelection", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.61 0.15", AnchorMax = "0.74 0.85" }
                }, bulkPanel);
            }

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.08" } }, mainPanel);

            container.Add(new CuiButton
            {
                Button = { Color = c.ButtonSecondary, Command = $"news.admin.selectpage {page}" },
                Text = { Text = Msg("SelectPageToggle", player), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = c.TextTitle },
                RectTransform = { AnchorMin = "0.02 0.01", AnchorMax = "0.18 0.07" }
            }, mainPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("Page", player, page + 1, totalPages), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0.4 0", AnchorMax = "0.6 0.08" }
            }, mainPanel);

            if (page > 0)
            {
                container.Add(new CuiButton { Button = { Color = c.ButtonSecondary, Command = $"news.admin.page {page - 1}" }, Text = { Text = "< PREV", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle }, RectTransform = { AnchorMin = "0.3 0.01", AnchorMax = "0.38 0.07" } }, mainPanel);
            }
            if (page < totalPages - 1)
            {
                container.Add(new CuiButton { Button = { Color = c.ButtonSecondary, Command = $"news.admin.page {page + 1}" }, Text = { Text = "NEXT >", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle }, RectTransform = { AnchorMin = "0.62 0.01", AnchorMax = "0.7 0.07" } }, mainPanel);
            }

            CuiHelper.AddUi(player, container);
        }

        private void ShowEditor(BasePlayer player)
        {
            if (!activeEditors.ContainsKey(player.userID)) return;
            var ann = activeEditors[player.userID];

            DestroyUI(player);
            playersWithUiOpen.Add(player.userID);

            var container = new CuiElementContainer();
            var c = config.Colors;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.85", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            string editorPanel = LayerName + ".Editor";
            container.Add(new CuiPanel { Image = { Color = c.PanelBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.2 0.2", AnchorMax = "0.8 0.8" } }, LayerName, editorPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" } }, editorPanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.899", AnchorMax = "1 0.901" } }, editorPanel);
            bool editingExisting = activeEditorIds.TryGetValue(player.userID, out string editingId) && !string.IsNullOrEmpty(editingId);
            container.Add(new CuiLabel { Text = { Text = editingExisting ? Msg("EditAnnouncement", player) : Msg("CreateAnnouncement", player), FontSize = 14, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.04 0.9", AnchorMax = "0.9 1" } }, editorPanel);

            container.Add(new CuiButton {
                Button = { Color = "0 0 0 0", Command = "news.editor.cancel" },
                Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.94 0.9", AnchorMax = "0.99 1" }
            }, editorPanel);

            container.Add(new CuiLabel { Text = { Text = Msg("AnnouncementTitle", player), FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.05 0.81", AnchorMax = "0.95 0.88" } }, editorPanel);
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0.45", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.05 0.75", AnchorMax = "0.95 0.81" } }, editorPanel);
            container.Add(new CuiElement
            {
                Parent = editorPanel,
                Components =
                {
                    new CuiInputFieldComponent { Text = ann.Title, FontSize = 12, Align = TextAnchor.MiddleLeft, Command = "news.editor.input title", Color = "1 1 1 1", NeedsKeyboard = true,  CharsLimit = MaxContentChars },
                    new CuiRectTransformComponent { AnchorMin = "0.06 0.75", AnchorMax = "0.94 0.81" }
                }
            });

            container.Add(new CuiLabel { Text = { Text = Msg("ImageUrl", player), FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.05 0.69", AnchorMax = "0.95 0.75" } }, editorPanel);
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0.45", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.05 0.63", AnchorMax = "0.95 0.69" } }, editorPanel);
            container.Add(new CuiElement
            {
                Parent = editorPanel,
                Components =
                {
                    new CuiInputFieldComponent { Text = ann.ImageUrl ?? "", FontSize = 12, Align = TextAnchor.MiddleLeft, Command = "news.editor.input image", Color = "1 1 1 1", NeedsKeyboard = true,  CharsLimit = MaxContentChars },
                    new CuiRectTransformComponent { AnchorMin = "0.06 0.63", AnchorMax = "0.94 0.69" }
                }
            });

            container.Add(new CuiLabel { Text = { Text = Msg("AnnouncementType", player), FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.05 0.57", AnchorMax = "0.95 0.63" } }, editorPanel);
            container.Add(new CuiButton {
                Button = { Color = GetTypeColor(ann.Type), Command = "news.editor.type" },
                Text = { Text = $"◀  {ann.Type.ToString().ToUpper()}  ▶", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.05 0.51", AnchorMax = "0.45 0.57" }
            }, editorPanel);

            container.Add(new CuiLabel { Text = { Text = Msg("ContentBody", player), FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.05 0.45", AnchorMax = "0.95 0.51" } }, editorPanel);
            container.Add(new CuiLabel { Text = { Text = Msg("ContentBodyHint", player), FontSize = 9, Align = TextAnchor.MiddleRight, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" }, RectTransform = { AnchorMin = "0.36 0.45", AnchorMax = "0.95 0.51" } }, editorPanel);
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0.45", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.05 0.14", AnchorMax = "0.95 0.45" } }, editorPanel);
            container.Add(new CuiElement
            {
                Parent = editorPanel,
                Components =
                {
                    new CuiInputFieldComponent { Text = ann.Text ?? "", FontSize = 12, Align = TextAnchor.UpperLeft, Command = "news.editor.input text", Color = "1 1 1 1", NeedsKeyboard = true, CharsLimit = MaxContentChars, LineType = UnityEngine.UI.InputField.LineType.MultiLineNewline },
                    new CuiRectTransformComponent { AnchorMin = "0.06 0.15", AnchorMax = "0.94 0.44" }
                }
            });

            string footerPanel = editorPanel + ".Footer";
            container.Add(new CuiPanel { Image = { Color = c.HeaderBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.12" } }, editorPanel, footerPanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.119", AnchorMax = "1 0.121" } }, editorPanel);

            container.Add(new CuiButton {
                Button = { Color = "0.30 0.78 0.45 0.95", Command = "news.editor.save", FadeIn = 0.20f },
                Text = { Text = Msg("SaveBroadcast", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf", FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.55 0.15", AnchorMax = "0.95 0.85" }
            }, footerPanel);

            container.Add(new CuiButton {
                Button = { Color = c.ButtonSecondary, Command = "news.editor.cancel", FadeIn = 0.20f },
                Text = { Text = Msg("Cancel", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextNormal, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.05 0.15", AnchorMax = "0.45 0.85" }
            }, footerPanel);

            CuiHelper.AddUi(player, container);
        }

        private void ShowDeleteConfirm(BasePlayer player, string id)
        {
            var ann = FindById(id);
            if (ann == null) return;
            CuiHelper.DestroyUi(player, ConfirmLayer);

            var container = new CuiElementContainer();
            var c = config.Colors;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.6", FadeIn = 0.15f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", ConfirmLayer);

            string dialogPanel = ConfirmLayer + ".Dialog";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg, FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0.34 0.40", AnchorMax = "0.66 0.60" }
            }, ConfirmLayer, dialogPanel);

            container.Add(new CuiPanel
            {
                Image = { Color = "0.85 0.32 0.32 0.95", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 1" }
            }, dialogPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("DeleteAnnouncement", player), FontSize = 13, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0 0.78", AnchorMax = "1 0.95" }
            }, dialogPanel);

            string displayTitle = (ann.Title ?? "").Length > 32 ? ann.Title.Substring(0, 29) + "..." : (ann.Title ?? "");
            container.Add(new CuiLabel
            {
                Text = { Text = Msg("DeleteConfirmBody", player, displayTitle), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextNormal, Font = "robotocondensed-regular.ttf", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0.05 0.30", AnchorMax = "0.95 0.74" }
            }, dialogPanel);

            container.Add(new CuiButton
            {
                Button = { Color = c.ButtonSecondary, Command = "news.confirm.close", FadeIn = 0.18f },
                Text = { Text = Msg("Cancel", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0.05 0.06", AnchorMax = "0.46 0.27" }
            }, dialogPanel);

            container.Add(new CuiButton
            {
                Button = { Color = "0.85 0.28 0.28 0.95", Command = $"news.admin.del {ann.Id}", FadeIn = 0.18f },
                Text = { Text = Msg("ConfirmDelete", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0.54 0.06", AnchorMax = "0.95 0.27" }
            }, dialogPanel);

            CuiHelper.AddUi(player, container);
        }

        private void ShowBulkDeleteConfirm(BasePlayer player, int returnPage)
        {
            var sel = GetAdminSelection(player.userID);
            if (sel.Count == 0) return;
            CuiHelper.DestroyUi(player, ConfirmLayer);

            var container = new CuiElementContainer();
            var c = config.Colors;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.6", FadeIn = 0.15f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", ConfirmLayer);

            string dialogPanel = ConfirmLayer + ".Dialog";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg, FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0.34 0.40", AnchorMax = "0.66 0.60" }
            }, ConfirmLayer, dialogPanel);

            container.Add(new CuiPanel
            {
                Image = { Color = "0.85 0.32 0.32 0.95", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 1" }
            }, dialogPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("BulkDeleteTitle", player), FontSize = 13, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0 0.78", AnchorMax = "1 0.95" }
            }, dialogPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("BulkDeleteBody", player, sel.Count), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextNormal, Font = "robotocondensed-regular.ttf", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0.05 0.30", AnchorMax = "0.95 0.74" }
            }, dialogPanel);

            container.Add(new CuiButton
            {
                Button = { Color = c.ButtonSecondary, Command = "news.confirm.close", FadeIn = 0.18f },
                Text = { Text = Msg("Cancel", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0.05 0.06", AnchorMax = "0.46 0.27" }
            }, dialogPanel);

            container.Add(new CuiButton
            {
                Button = { Color = "0.85 0.28 0.28 0.95", Command = $"news.admin.bulkdel {returnPage}", FadeIn = 0.18f },
                Text = { Text = Msg("ConfirmDelete", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0.54 0.06", AnchorMax = "0.95 0.27" }
            }, dialogPanel);

            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("news.admin.delconfirm")]
        private void CmdNewsAdminDelConfirm(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            ShowDeleteConfirm(player, arg.GetString(0));
        }

        [ConsoleCommand("news.confirm.close")]
        private void CmdConfirmClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player != null) CuiHelper.DestroyUi(player, ConfirmLayer);
        }
        #endregion

        #region Theme UI
        [ConsoleCommand("news.admin.themes")]
        private void CmdNewsAdminThemes(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin && !permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin)) return;
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            ShowThemeSelection(player);
        }

        [ConsoleCommand("news.admin.settheme")]
        private void CmdNewsSetTheme(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin && !permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin)) return;
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(player, "Usage: news.admin.settheme \"ThemeName\"");
                return;
            }

            string themeName = arg.GetString(0).Trim('"');

            string matchedTheme = config.Themes.Keys.FirstOrDefault(k => string.Equals(k, themeName, StringComparison.OrdinalIgnoreCase));

            if (matchedTheme != null)
            {
                config.SelectedTheme = matchedTheme;
                SaveConfig();

                NextTick(() =>
                {
                    ShowThemeSelection(player);
                    SendReply(player, $"Theme set to: {matchedTheme} (Colors updated)");
                });
            }
            else
            {
                SendReply(player, $"Error: Theme '{themeName}' not found in configuration.");
                SendReply(player, "Available: " + string.Join(", ", config.Themes.Keys));
            }
        }

        private void ShowThemeSelection(BasePlayer player)
        {
            DestroyUI(player);
            playersWithUiOpen.Add(player.userID);

            var container = new CuiElementContainer();

            var c = config.Colors;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.85", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            string mainPanel = LayerName + ".Themes";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg, FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.25 0.25", AnchorMax = "0.75 0.75" }
            }, LayerName, mainPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" } }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.899", AnchorMax = "1 0.901" } }, mainPanel);
            container.Add(new CuiLabel { Text = { Text = Msg("SelectTheme", player), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" } }, mainPanel);

            container.Add(new CuiButton {
                Button = { Color = "0.8 0.2 0.2 0", Command = "news.admin" },
                Text = { Text = "✕", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.92 0.9", AnchorMax = "0.99 1" }
            }, mainPanel);

            int count = 0;
            float buttonHeight = 0.12f;
            float startY = 0.8f;

            foreach (var themeName in config.Themes.Keys)
            {
                bool isSelected = config.SelectedTheme == themeName;
                string buttonColor = isSelected ? c.ButtonPrimary : c.ButtonSecondary;
                float themeFade = 0.20f + count * 0.04f;

                float top = startY - (count * (buttonHeight + 0.02f));
                float bottom = top - buttonHeight;

                container.Add(new CuiButton
                {
                    Button = { Color = buttonColor, Command = $"news.admin.settheme \"{themeName}\"", FadeIn = themeFade },
                    Text = { Text = themeName.ToUpper() + (isSelected ? $"  {Msg("Active", player)}" : ""), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = isSelected ? "1 1 1 1" : c.TextNormal, Font = "robotocondensed-bold.ttf", FadeIn = themeFade },
                    RectTransform = { AnchorMin = $"0.1 {bottom}", AnchorMax = $"0.9 {top}" }
                }, mainPanel);

                count++;
            }

            CuiHelper.AddUi(player, container);
        }
        #endregion

        #region Rewards
        private void GiveRewards(BasePlayer player, RewardBundle bundle, string langKey)
        {
            if (player == null || bundle == null) return;

            var grantedNames = new List<string>();

            if (bundle.Items != null)
            {
                foreach (var r in bundle.Items)
                {
                    if (r == null || string.IsNullOrEmpty(r.Shortname) || r.Amount <= 0) continue;

                    var item = ItemManager.CreateByName(r.Shortname, r.Amount, r.SkinId);
                    if (item == null)
                    {
                        PrintWarning($"Reward item '{r.Shortname}' could not be created — invalid shortname?");
                        continue;
                    }

                    if (!player.inventory.GiveItem(item))
                    {
                        item.Drop(player.transform.position + Vector3.up, Vector3.up * 2f);
                    }

                    string label = item.info?.displayName?.translated;
                    if (string.IsNullOrEmpty(label)) label = r.Shortname;
                    grantedNames.Add($"{r.Amount}× {label}");
                }
            }

            if (bundle.Points > 0)
            {
                if (ServerRewards != null && ServerRewards.IsLoaded)
                {
                    ServerRewards.Call("AddPoints", player.userID, bundle.Points);
                    grantedNames.Add($"{bundle.Points} {config.Rewards.PointsLabel}");
                }
                else
                {
                    PrintWarning("Reward configured Points but ServerRewards plugin is not loaded — skipping.");
                }
            }

            if (bundle.Currency > 0)
            {
                if (Economics != null && Economics.IsLoaded)
                {

                    Economics.Call("Deposit", player.UserIDString, bundle.Currency);
                    grantedNames.Add($"{bundle.Currency.ToString("0.##", CultureInfo.InvariantCulture)} {config.Rewards.CurrencyLabel}");
                }
                else
                {
                    PrintWarning("Reward configured Currency but Economics plugin is not loaded — skipping.");
                }
            }

            if (grantedNames.Count > 0 && config.Rewards.NotifyOnReward)
            {
                SendReply(player, Msg(langKey, player, string.Join(", ", grantedNames)));
            }
        }

        private void CancelReadRewardTimer(ulong userId)
        {
            if (readRewardTimers.TryGetValue(userId, out var state))
            {
                state.Timer?.Destroy();
                readRewardTimers.Remove(userId);
            }
        }

        private void ScheduleReadCompletion(BasePlayer player, Announcement ann)
        {
            if (player == null || ann == null || string.IsNullOrEmpty(ann.Id)) return;
            if (ann.ReadByPlayers == null) ann.ReadByPlayers = new HashSet<ulong>();

            if (readRewardTimers.TryGetValue(player.userID, out var existing)
                && string.Equals(existing.AnnId, ann.Id, StringComparison.Ordinal)
                && existing.Timer != null && !existing.Timer.Destroyed)
                return;

            bool alreadyRead = ann.ReadByPlayers.Contains(player.userID);
            bool rewardsActive = config.Rewards != null && config.Rewards.EnableReadReward;
            bool rewardAlreadyGranted = ann.ReadRewardedPlayers != null && ann.ReadRewardedPlayers.Contains(player.userID);
            if (alreadyRead && (!rewardsActive || rewardAlreadyGranted)) return;

            CancelReadRewardTimer(player.userID);

            int delay = Math.Max(1, config.Rewards?.ReadDelaySeconds ?? 5);
            string targetId = ann.Id;
            ulong userId = player.userID;

            var t = timer.Once(delay, () =>
            {
                readRewardTimers.Remove(userId);
                if (player == null || !player.IsConnected) return;
                if (!playersWithUiOpen.Contains(userId)) return;

                var current = FindById(targetId);
                if (current == null) return;
                if (current.ReadByPlayers == null) current.ReadByPlayers = new HashSet<ulong>();
                if (current.ReadRewardedPlayers == null) current.ReadRewardedPlayers = new HashSet<ulong>();

                bool firstRead = current.ReadByPlayers.Add(userId);
                bool grantReward = config.Rewards != null && config.Rewards.EnableReadReward
                                   && current.ReadRewardedPlayers.Add(userId);

                if (firstRead || grantReward) SaveAnnouncements();
                if (firstRead) Interface.CallHook("OnNewsRead", player, BuildHookData(current));
                if (grantReward) GiveRewards(player, config.Rewards.ReadRewards, "RewardRead");
            });

            readRewardTimers[userId] = new ReadRewardState { AnnId = targetId, Timer = t };
        }
        #endregion

        #region Discord Webhooks
        private void SendToDiscord(Announcement ann)
        {
            if (!config.Discord.Enabled || string.IsNullOrEmpty(config.Discord.WebhookUrl)) return;

            int embedColor = 3447003;
            switch(ann.Type)
            {
                case AnnouncementType.Alert: embedColor = 15158332; break;
                case AnnouncementType.Warning: embedColor = 15105570; break;
                case AnnouncementType.Update: embedColor = 3066993; break;
                case AnnouncementType.Event: embedColor = 10181046; break;
            }

            string content = string.IsNullOrEmpty(config.Discord.RoleMention) ? "" : config.Discord.RoleMention;
            string discordBody = NormalizeBodyText(ann.Text);
            if (!string.IsNullOrEmpty(discordBody) && discordBody.Length > DiscordEmbedDescriptionLimit)
            {
                const string truncatedSuffix = "\n\n[Message truncated on Discord]";
                int keepLength = Math.Max(0, DiscordEmbedDescriptionLimit - truncatedSuffix.Length);
                discordBody = discordBody.Substring(0, Math.Min(keepLength, discordBody.Length)).TrimEnd() + truncatedSuffix;
            }

            var payload = new
            {
                username = config.Discord.BotName,
                content = content,

                allowed_mentions = new { parse = new[] { "roles" } },
                embeds = new[]
                {
                    new
                    {
                        title = ann.Title,
                        description = discordBody,
                        color = embedColor,
                        footer = new { text = $"Posted by {ann.Author} • {ann.Date}" },
                        image = string.IsNullOrEmpty(ann.ImageUrl) ? null : new { url = ann.ImageUrl }
                    }
                }
            };

            string json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            webrequest.Enqueue(config.Discord.WebhookUrl, json, (code, response) =>
            {
                if (code < 200 || code > 299)
                {
                    PrintError($"Discord Webhook failed! Code: {code} - Response: {response}");
                }
            }, this, RequestMethod.POST, new Dictionary<string, string> { { "Content-Type", "application/json" } });
        }
        #endregion
    }
}
 