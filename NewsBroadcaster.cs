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
    [Info("NewsBroadcaster", "DEDA", "1.5.3")]
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
        private Dictionary<ulong, ReadRewardState> readRewardTimers = new Dictionary<ulong, ReadRewardState>();
        private Dictionary<ulong, HashSet<string>> adminSelections = new Dictionary<ulong, HashSet<string>>();
        private static readonly string InvariantDateFormat = "yyyy-MM-dd HH:mm";
        private const int MaxContentChars = 32768;
        private const int BodyWrapCharacters = 64;
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
            public bool UseComponentsV2 { get; set; } = false;
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
                ["TitleRequired"] = "Announcement title cannot be empty.",
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
                ["BulkUnpinned"] = "Unpinned {0} announcement(s).",
                ["NavAnnouncements"] = "ANNOUNCEMENTS",
                ["BrandTop"] = "NEWS",
                ["BrandBottom"] = "BROADCASTER",
                ["ThemeHint"] = "Pick a theme — it applies instantly to every menu.",
                ["ArchiveEmpty"] = "No announcements have been posted yet.",
                ["LikesReads"] = "<color=#e0556b>❤</color> {0}    {1} reads",
                ["ByAuthor"] = "by {0}",
                ["ByAuthorDate"] = "by {0}  ·  {1}",
                ["StatPosts"] = "POSTS",
                ["StatPinned"] = "PINNED",
                ["StatLikes"] = "LIKES",
                ["StatReads"] = "READS"
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

                if (raw != null && (raw["Discord"] == null || raw["Discord"]["UseComponentsV2"] == null)) needsSave = true;

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
            activeEditors.Clear();
            activeEditorIds.Clear();
            adminSelections.Clear();
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            ulong id = player.userID;
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

            ShowPopup(player, ann, true);
        }

        [ConsoleCommand("news.close")]
        private void CmdConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player != null) DestroyUI(player);
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
            ann.Title = ann.Title?.Trim();

            if (string.IsNullOrEmpty(ann.Title))
            {
                SendReply(player, Msg("TitleRequired", player));
                return;
            }

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
                var target = FindById(editingId);
                if (target == null)
                {
                    SendReply(player, Msg("EditTargetGone", player));
                    activeEditors.Remove(player.userID);
                    activeEditorIds.Remove(player.userID);
                    ShowAdminList(player, 0);
                    return;
                }

                // Apply only the editable fields onto the live announcement so the
                // pinned state, likes, read marks and reward tracking are preserved.
                target.Title = ann.Title;
                target.ImageUrl = ann.ImageUrl;
                target.Text = ann.Text;
                target.Type = ann.Type;
                ann = target;
                Interface.CallHook("OnNewsEdited", BuildHookData(target));
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

            float contentLeft = hasImage ? 0.51f : 0.06f;

            if (hasImage)
            {
                string imgPanel = mainPanel + ".Img";

                container.Add(new CuiPanel
                {
                    Image = { Color = "1 1 1 0.10" },
                    RectTransform = { AnchorMin = "0.033 0.113", AnchorMax = "0.477 0.902" }
                }, mainPanel);

                container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0.5" },
                    RectTransform = { AnchorMin = "0.035 0.115", AnchorMax = "0.475 0.90" }
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

                container.Add(new CuiPanel
                {
                    Image = { Color = GetTypeColor(ann.Type) },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.30 0.06" }
                }, imgPanel);
                container.Add(new CuiLabel
                {
                    Text = { Text = ann.Type.ToString().ToUpper(), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.30 0.06" }
                }, imgPanel);
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

            string bodyScroll = mainPanel + ".Body";
            int estLines = BuildBodyDisplayLines(ann.Text, 50).Count;
            int extra = Mathf.Max(0, estLines * 19 - 300);

            container.Add(new CuiElement
            {
                Name = bodyScroll,
                Parent = mainPanel,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.18" },
                    new CuiRectTransformComponent { AnchorMin = $"{contentLeft} 0.12", AnchorMax = "0.965 0.76" },
                    new CuiScrollViewComponent
                    {
                        Horizontal = false,
                        Vertical = true,
                        MovementType = UnityEngine.UI.ScrollRect.MovementType.Clamped,
                        Inertia = true,
                        DecelerationRate = 0.1f,
                        ScrollSensitivity = 26f,
                        ContentTransform = new CuiRectTransform { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 -{extra}", OffsetMax = "0 0" },
                        VerticalScrollbar = new CuiScrollbar { Size = 6f, AutoHide = true, HandleColor = c.ButtonPrimary, HighlightColor = c.ButtonPrimary, PressedColor = c.ButtonPrimary, TrackColor = "1 1 1 0.05", HandleSprite = "assets/content/ui/ui.background.tile.psd", TrackSprite = "assets/content/ui/ui.background.tile.psd" }
                    }
                }
            });

            container.Add(new CuiLabel
            {
                Text = { Text = NormalizeBodyText(ann.Text), FontSize = 15, Align = TextAnchor.UpperLeft, Color = c.TextNormal, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0.025 0", AnchorMax = "0.97 1" }
            }, bodyScroll);

             container.Add(new CuiPanel
            {
                Image = { Color = c.HeaderBg },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.095" }
            }, mainPanel);
            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.06" },
                RectTransform = { AnchorMin = "0 0.094", AnchorMax = "1 0.096" }
            }, mainPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{Msg("PostedBy", player)} <color={RgbaToHex(c.ButtonPrimary)}>{(ann.Author ?? Msg("Unknown", player)).ToUpper()}</color>  •  {ann.Date}", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0.03 0", AnchorMax = "0.55 0.095" }
            }, mainPanel);

            if (!string.IsNullOrEmpty(ann.Id))
            {
                bool liked = ann.LikedPlayers.Contains(player.userID);
                string heartCol = liked ? "0.90 0.30 0.35 1" : "0.55 0.55 0.60 1";
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0.06", Command = $"news.like {ann.Id}" },
                    Text = { Text = $"❤  {ann.LikedPlayers.Count}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = heartCol, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.615 0.02", AnchorMax = "0.75 0.078" }
                }, mainPanel);
            }

             container.Add(new CuiButton
            {
                Button = { Color = c.ButtonPrimary, Command = "news.page 0" },
                Text = { Text = Msg("ViewArchive", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.78 0.02", AnchorMax = "0.965 0.078" }
            }, mainPanel);

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
                Image = { Color = "0 0 0 0.82", FadeIn = 0.18f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            string mainPanel = LayerName + ".List";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg, FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.12 0.09", AnchorMax = "0.88 0.91" }
            }, LayerName, mainPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.925", AnchorMax = "1 1" } }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.923", AnchorMax = "1 0.925" } }, mainPanel);

            container.Add(new CuiLabel {
                Text = { Text = $"{config.General.ServerName} <color={RgbaToHex(c.ButtonPrimary)}>//</color> {Msg("ArchiveTitle", player)} <color={RgbaToHex(c.ButtonPrimary)}>({displayList.Count})</color>", FontSize = 17, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.03 0.925", AnchorMax = "0.9 1" }
            }, mainPanel);

            container.Add(new CuiButton {
                Button = { Color = "0 0 0 0", Command = "news.close" },
                Text = { Text = "✕", FontSize = 17, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.95 0.925", AnchorMax = "0.99 1" }
            }, mainPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.075" } }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.075", AnchorMax = "1 0.077" } }, mainPanel);

            int start = page * perPage;
            int count = 0;
            float listTop = 0.90f;
            float rowHeight = 0.81f / perPage;
            float padding = 0.012f;

            for (int i = start; i < displayList.Count && count < perPage; i++)
            {
                var ann = displayList[i];
                float top = listTop - (count * rowHeight) - padding;
                float bottom = top - rowHeight + (padding * 2);

                string itemPanel = mainPanel + $".{i}";
                string typeColor = GetTypeColor(ann.Type);
                bool unread = !(ann.ReadByPlayers?.Contains(player.userID) ?? false);

                float rowFade = 0.18f + count * 0.04f;

                container.Add(new CuiPanel
                {
                    Image = { Color = c.ContentBg, FadeIn = rowFade },
                    RectTransform = { AnchorMin = $"0.025 {bottom}", AnchorMax = $"0.975 {top}" }
                }, mainPanel, itemPanel);

                if (unread)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = WithAlpha(c.ButtonPrimary, 0.10f) },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }, itemPanel);
                }

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
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.008 1" }
                }, itemPanel);

                string archiveTitle = (ann.Title ?? "(no title)").ToUpper();
                if (unread) archiveTitle = $"<color={RgbaToHex(c.ButtonPrimary)}>NEW</color>  {archiveTitle}";
                container.Add(new CuiLabel
                {
                    Text = { Text = archiveTitle, FontSize = 15, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.03 0.55", AnchorMax = "0.585 0.92" }
                }, itemPanel);

                if (!string.IsNullOrEmpty(ann.Author))
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = Msg("ByAuthor", player, ann.Author), FontSize = 10, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                        RectTransform = { AnchorMin = "0.03 0.40", AnchorMax = "0.585 0.53" }
                    }, itemPanel);
                }

                if (ann.Pinned)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.95 0.70 0.20 0.95" },
                        RectTransform = { AnchorMin = "0.61 0.60", AnchorMax = "0.73 0.87" }
                    }, itemPanel);
                    container.Add(new CuiLabel
                    {
                        Text = { Text = Msg("PinnedBadge", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.05 0.05 0.05 1", Font = "robotocondensed-bold.ttf" },
                        RectTransform = { AnchorMin = "0.61 0.60", AnchorMax = "0.73 0.87" }
                    }, itemPanel);
                }
                else
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = typeColor },
                        RectTransform = { AnchorMin = "0.61 0.60", AnchorMax = "0.73 0.87" }
                    }, itemPanel);
                    container.Add(new CuiLabel
                    {
                        Text = { Text = ann.Type.ToString().ToUpper(), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                        RectTransform = { AnchorMin = "0.61 0.60", AnchorMax = "0.73 0.87" }
                    }, itemPanel);
                }

                container.Add(new CuiLabel
                {
                    Text = { Text = ann.Date ?? "", FontSize = 11, Align = TextAnchor.MiddleRight, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.74 0.54", AnchorMax = "0.87 0.92" }
                }, itemPanel);

                string rawPreview = (ann.Text ?? "").Replace("\n", " ");
                string preview = rawPreview.Length > 90 ? rawPreview.Substring(0, 87) + "..." : rawPreview;
                container.Add(new CuiLabel
                {
                    Text = { Text = preview, FontSize = 12, Align = TextAnchor.UpperLeft, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.03 0.1", AnchorMax = "0.84 0.37" }
                }, itemPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonPrimary, Command = $"news.view {ann.Id}" },
                    Text = { Text = Msg("ReadMore", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.87 0.14", AnchorMax = "0.97 0.48" }
                }, itemPanel);

                count++;
            }

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("Page", player, page + 1, totalPages), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0.42 0", AnchorMax = "0.58 0.075" }
            }, mainPanel);

            if (page > 0)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonSecondary, Command = $"news.page {page - 1}" },
                    Text = { Text = Msg("Previous", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.025 0.012", AnchorMax = "0.16 0.063" }
                }, mainPanel);
            }

            if (page < totalPages - 1)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonSecondary, Command = $"news.page {page + 1}" },
                    Text = { Text = Msg("Next", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.84 0.012", AnchorMax = "0.975 0.063" }
                }, mainPanel);
            }

            CuiHelper.AddUi(player, container);
        }

        // Returns the same RGBA color string with its alpha replaced.
        private string WithAlpha(string rgba, float alpha)
        {
            if (string.IsNullOrEmpty(rgba)) return rgba;
            var p = rgba.Split(' ');
            if (p.Length < 3) return rgba;
            return $"{p[0]} {p[1]} {p[2]} {alpha.ToString("0.###", CultureInfo.InvariantCulture)}";
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
        // Renders a left-aligned sidebar navigation entry (OXF-style hub menu).
        private void AddNavItem(CuiElementContainer container, string parent, UIColors c, string label, string command, bool active, float topY)
        {
            const float h = 0.072f;
            float bottomY = topY - h;

            if (active)
            {
                container.Add(new CuiPanel { Image = { Color = "1 1 1 0.05", FadeIn = 0.20f }, RectTransform = { AnchorMin = $"0 {bottomY}", AnchorMax = $"1 {topY}" } }, parent);
                container.Add(new CuiPanel { Image = { Color = c.ButtonPrimary, FadeIn = 0.20f }, RectTransform = { AnchorMin = $"0 {bottomY}", AnchorMax = $"0.02 {topY}" } }, parent);
            }

            container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = command },
                Text = { Text = label, FontSize = 14, Align = TextAnchor.MiddleLeft, Color = active ? c.TextTitle : c.TextMuted, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = $"0.1 {bottomY}", AnchorMax = $"0.95 {topY}" }
            }, parent);
        }

        // Shared admin shell: dark overlay, large panel, left sidebar (brand + nav),
        // and a content-area title bar. Returns the main panel name. Callers fill the
        // content region: x 0.285..0.975, y 0..0.905.
        private string BuildAdminShell(CuiElementContainer container, BasePlayer player, string activeNav, string contentTitle, int titleCount = -1, string closeCommand = "news.close")
        {
            var c = config.Colors;

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
                RectTransform = { AnchorMin = "0.06 0.09", AnchorMax = "0.94 0.91" }
            }, LayerName, mainPanel);

            // ----- Sidebar -----
            string sidePanel = mainPanel + ".Side";
            container.Add(new CuiPanel { Image = { Color = c.HeaderBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0", AnchorMax = "0.255 1" } }, mainPanel, sidePanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.254 0", AnchorMax = "0.256 1" } }, mainPanel);

            container.Add(new CuiLabel { Text = { Text = Msg("BrandTop", player), FontSize = 32, Align = TextAnchor.LowerLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.1 0.90", AnchorMax = "0.97 0.97" } }, sidePanel);
            container.Add(new CuiLabel { Text = { Text = Msg("BrandBottom", player), FontSize = 22, Align = TextAnchor.LowerLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.1 0.845", AnchorMax = "0.97 0.905" } }, sidePanel);
            container.Add(new CuiLabel { Text = { Text = $"<color={RgbaToHex(c.ButtonPrimary)}>//</color> {config.General.ServerName}", FontSize = 11, Align = TextAnchor.UpperLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.1 0.805", AnchorMax = "0.97 0.84" } }, sidePanel);

            AddNavItem(container, sidePanel, c, Msg("NavAnnouncements", player), "news.admin", activeNav == "list", 0.73f);
            AddNavItem(container, sidePanel, c, Msg("NewPost", player), "news.admin.create", activeNav == "create", 0.645f);
            AddNavItem(container, sidePanel, c, Msg("Themes", player), "news.admin.themes", activeNav == "themes", 0.56f);
            AddNavItem(container, sidePanel, c, Msg("Close", player), "news.close", false, 0.10f);

            // ----- Content title bar -----
            string titleText = titleCount >= 0
                ? $"{contentTitle} <color={RgbaToHex(c.ButtonPrimary)}>({titleCount})</color>"
                : contentTitle;
            container.Add(new CuiLabel
            {
                Text = { Text = titleText, FontSize = 17, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.285 0.915", AnchorMax = "0.85 0.985" }
            }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.285 0.905", AnchorMax = "0.975 0.907" } }, mainPanel);
            container.Add(new CuiButton
            {
                Button = { Color = "0.8 0.2 0.2 0", Command = closeCommand },
                Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.95 0.915", AnchorMax = "0.985 0.985" }
            }, mainPanel);

            return mainPanel;
        }

        // One cell of the Announcements stats bar (index 0-3 across the content width).
        private void AddStatCell(CuiElementContainer container, string parent, UIColors c, int index, string label, string value)
        {
            const float left = 0.285f, total = 0.69f;
            float stride = total / 4f;
            float xMin = left + index * stride + 0.004f;
            float xMax = left + (index + 1) * stride - 0.004f;

            container.Add(new CuiPanel { Image = { Color = c.ContentBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = $"{xMin} 0.85", AnchorMax = $"{xMax} 0.90" } }, parent);
            container.Add(new CuiLabel { Text = { Text = value, FontSize = 18, Align = TextAnchor.MiddleCenter, Color = c.ButtonPrimary, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f }, RectTransform = { AnchorMin = $"{xMin} 0.871", AnchorMax = $"{xMax} 0.899" } }, parent);
            container.Add(new CuiLabel { Text = { Text = label, FontSize = 9, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f }, RectTransform = { AnchorMin = $"{xMin} 0.852", AnchorMax = $"{xMax} 0.872" } }, parent);
        }

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

            string mainPanel = BuildAdminShell(container, player, "list", Msg("NavAnnouncements", player), announcements.Count);

            var displayList = GetDisplayOrder();

            totalPages = Mathf.CeilToInt((float)displayList.Count / perPage);
            if (totalPages == 0) totalPages = 1;
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;

            PruneAdminSelection(player.userID);
            var selection = GetAdminSelection(player.userID);
            int start = page * perPage;
            int count = 0;
            float listTop = 0.77f;
            float rowHeight = 0.66f / perPage;
            float padding = 0.008f;

            // ----- Stats bar -----
            int totalPinned = announcements.Count(a => a.Pinned);
            int totalLikes = announcements.Sum(a => a.LikedPlayers?.Count ?? 0);
            int totalReads = announcements.Sum(a => a.ReadByPlayers?.Count ?? 0);
            AddStatCell(container, mainPanel, c, 0, Msg("StatPosts", player), announcements.Count.ToString());
            AddStatCell(container, mainPanel, c, 1, Msg("StatPinned", player), totalPinned.ToString());
            AddStatCell(container, mainPanel, c, 2, Msg("StatLikes", player), totalLikes.ToString());
            AddStatCell(container, mainPanel, c, 3, Msg("StatReads", player), totalReads.ToString());

            if (displayList.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("NoAnnouncementsYet", player), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.285 0.3", AnchorMax = "0.975 0.7" }
                }, mainPanel);
            }

            for (int i = start; i < displayList.Count && count < perPage; i++)
            {
                var ann = displayList[i];
                float top = listTop - (count * rowHeight) - padding;
                float bottom = top - rowHeight + (padding * 2);

                string itemPanel = mainPanel + $".{i}";
                string typeColor = GetTypeColor(ann.Type);
                bool selected = selection.Contains(ann.Id);

                float adminRowFade = 0.18f + count * 0.04f;
                container.Add(new CuiPanel
                {
                    Image = { Color = c.ContentBg, FadeIn = adminRowFade },
                    RectTransform = { AnchorMin = $"0.285 {bottom}", AnchorMax = $"0.975 {top}" }
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
                    RectTransform = { AnchorMin = "0.06 0.52", AnchorMax = "0.46 0.9" }
                }, itemPanel);

                string adminMeta = string.IsNullOrEmpty(ann.Author)
                    ? (ann.Date ?? "")
                    : Msg("ByAuthorDate", player, ann.Author, ann.Date ?? "");
                container.Add(new CuiLabel
                {
                    Text = { Text = adminMeta, FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.06 0.12", AnchorMax = "0.46 0.48" }
                }, itemPanel);

                container.Add(new CuiPanel { Image = { Color = typeColor }, RectTransform = { AnchorMin = "0.47 0.55", AnchorMax = "0.58 0.85" } }, itemPanel);
                container.Add(new CuiLabel { Text = { Text = ann.Type.ToString().ToUpper(), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.47 0.55", AnchorMax = "0.58 0.85" } }, itemPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("LikesReads", player, ann.LikedPlayers?.Count ?? 0, ann.ReadByPlayers?.Count ?? 0), FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.47 0.15", AnchorMax = "0.62 0.48" }
                }, itemPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = ann.Pinned ? "0.95 0.70 0.20 1" : "0.35 0.35 0.4 0.9", Command = $"news.admin.togglepin {ann.Id} {page}" },
                    Text = { Text = ann.Pinned ? Msg("UnpinButton", player) : Msg("PinButton", player), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = ann.Pinned ? "0.05 0.05 0.05 1" : "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.63 0.2", AnchorMax = "0.74 0.8" }
                }, itemPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.25 0.52 0.80 0.95", Command = $"news.admin.edit {ann.Id}" },
                    Text = { Text = Msg("EditButton", player), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.75 0.2", AnchorMax = "0.85 0.8" }
                }, itemPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.78 0.26 0.26 0.95", Command = $"news.admin.delconfirm {ann.Id}" },
                    Text = { Text = Msg("DelButton", player), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.86 0.2", AnchorMax = "0.96 0.8" }
                }, itemPanel);

                count++;
            }

            // ----- Action grid (between stats bar and list) -----
            const float agTop = 0.838f, agBottom = 0.788f;

            container.Add(new CuiButton
            {
                Button = { Color = c.ButtonSecondary, Command = $"news.admin.selectpage {page}" },
                Text = { Text = Msg("SelectPageToggle", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = $"0.285 {agBottom}", AnchorMax = $"0.40 {agTop}" }
            }, mainPanel);

            if (selection.Count > 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("SelectedCount", player, selection.Count), FontSize = 11, Align = TextAnchor.MiddleLeft, Color = c.ButtonPrimary, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = $"0.41 {agBottom}", AnchorMax = $"0.52 {agTop}" }
                }, mainPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonPrimary, Command = $"news.admin.bulkpin 1 {page}" },
                    Text = { Text = Msg("BulkPin", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = $"0.525 {agBottom}", AnchorMax = $"0.64 {agTop}" }
                }, mainPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.35 0.35 0.4 0.9", Command = $"news.admin.bulkpin 0 {page}" },
                    Text = { Text = Msg("BulkUnpin", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = $"0.645 {agBottom}", AnchorMax = $"0.76 {agTop}" }
                }, mainPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.65 0.12 0.12 1", Command = $"news.admin.bulkdelconfirm {page}" },
                    Text = { Text = Msg("BulkDelete", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = $"0.765 {agBottom}", AnchorMax = $"0.86 {agTop}" }
                }, mainPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonSecondary, Command = $"news.admin.clearsel {page}" },
                    Text = { Text = Msg("ClearSelection", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = $"0.865 {agBottom}", AnchorMax = $"0.975 {agTop}" }
                }, mainPanel);
            }

            // ----- Pager (bottom of content) -----
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06" }, RectTransform = { AnchorMin = "0.285 0.095", AnchorMax = "0.975 0.097" } }, mainPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("Page", player, page + 1, totalPages), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0.56 0.02", AnchorMax = "0.70 0.085" }
            }, mainPanel);

            if (page > 0)
            {
                container.Add(new CuiButton { Button = { Color = c.ButtonSecondary, Command = $"news.admin.page {page - 1}" }, Text = { Text = Msg("Previous", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.285 0.02", AnchorMax = "0.40 0.085" } }, mainPanel);
            }
            if (page < totalPages - 1)
            {
                container.Add(new CuiButton { Button = { Color = c.ButtonSecondary, Command = $"news.admin.page {page + 1}" }, Text = { Text = Msg("Next", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.86 0.02", AnchorMax = "0.975 0.085" } }, mainPanel);
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

            bool editingExisting = activeEditorIds.TryGetValue(player.userID, out string editingId) && !string.IsNullOrEmpty(editingId);
            string mainPanel = BuildAdminShell(container, player, "create",
                editingExisting ? Msg("EditAnnouncement", player) : Msg("CreateAnnouncement", player), -1, "news.editor.cancel");

            // Title
            container.Add(new CuiLabel { Text = { Text = Msg("AnnouncementTitle", player), FontSize = 11, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.30 0.85", AnchorMax = "0.96 0.885" } }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0.45", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.30 0.785", AnchorMax = "0.96 0.845" } }, mainPanel);
            container.Add(new CuiElement
            {
                Parent = mainPanel,
                Components =
                {
                    new CuiInputFieldComponent { Text = ann.Title, FontSize = 13, Align = TextAnchor.MiddleLeft, Command = "news.editor.input title", Color = "1 1 1 1", NeedsKeyboard = true, CharsLimit = MaxContentChars },
                    new CuiRectTransformComponent { AnchorMin = "0.315 0.785", AnchorMax = "0.95 0.845" }
                }
            });

            // Image URL
            container.Add(new CuiLabel { Text = { Text = Msg("ImageUrl", player), FontSize = 11, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.30 0.725", AnchorMax = "0.96 0.76" } }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0.45", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.30 0.66", AnchorMax = "0.96 0.72" } }, mainPanel);
            container.Add(new CuiElement
            {
                Parent = mainPanel,
                Components =
                {
                    new CuiInputFieldComponent { Text = ann.ImageUrl ?? "", FontSize = 13, Align = TextAnchor.MiddleLeft, Command = "news.editor.input image", Color = "1 1 1 1", NeedsKeyboard = true, CharsLimit = MaxContentChars },
                    new CuiRectTransformComponent { AnchorMin = "0.315 0.66", AnchorMax = "0.95 0.72" }
                }
            });

            // Type
            container.Add(new CuiLabel { Text = { Text = Msg("AnnouncementType", player), FontSize = 11, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.30 0.60", AnchorMax = "0.96 0.635" } }, mainPanel);
            container.Add(new CuiButton
            {
                Button = { Color = GetTypeColor(ann.Type), Command = "news.editor.type" },
                Text = { Text = $"◀  {ann.Type.ToString().ToUpper()}  ▶", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.30 0.535", AnchorMax = "0.62 0.595" }
            }, mainPanel);

            // Body
            container.Add(new CuiLabel { Text = { Text = Msg("ContentBody", player), FontSize = 11, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.30 0.475", AnchorMax = "0.55 0.51" } }, mainPanel);
            container.Add(new CuiLabel { Text = { Text = Msg("ContentBodyHint", player), FontSize = 9, Align = TextAnchor.LowerRight, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" }, RectTransform = { AnchorMin = "0.55 0.475", AnchorMax = "0.96 0.51" } }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0.45", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0.30 0.135", AnchorMax = "0.96 0.465" } }, mainPanel);
            container.Add(new CuiElement
            {
                Parent = mainPanel,
                Components =
                {
                    new CuiInputFieldComponent { Text = ann.Text ?? "", FontSize = 13, Align = TextAnchor.UpperLeft, Command = "news.editor.input text", Color = "1 1 1 1", NeedsKeyboard = true, CharsLimit = MaxContentChars, LineType = UnityEngine.UI.InputField.LineType.MultiLineNewline },
                    new CuiRectTransformComponent { AnchorMin = "0.315 0.145", AnchorMax = "0.95 0.455" }
                }
            });

            // Footer buttons
            container.Add(new CuiButton
            {
                Button = { Color = c.ButtonSecondary, Command = "news.editor.cancel", FadeIn = 0.20f },
                Text = { Text = Msg("Cancel", player), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = c.TextNormal, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.30 0.04", AnchorMax = "0.61 0.105" }
            }, mainPanel);
            container.Add(new CuiButton
            {
                Button = { Color = "0.30 0.78 0.45 0.95", Command = "news.editor.save", FadeIn = 0.20f },
                Text = { Text = Msg("SaveBroadcast", player), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf", FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.63 0.04", AnchorMax = "0.96 0.105" }
            }, mainPanel);

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

            string mainPanel = BuildAdminShell(container, player, "themes", Msg("SelectTheme", player), config.Themes.Count);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("ThemeHint", player), FontSize = 11, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = "robotocondensed-regular.ttf", FadeIn = 0.20f },
                RectTransform = { AnchorMin = "0.285 0.862", AnchorMax = "0.975 0.895" }
            }, mainPanel);

            const float gridLeft = 0.285f, cardW = 0.335f, colGap = 0.02f;
            const float startTop = 0.83f, cardH = 0.20f, vGap = 0.025f;
            int idx = 0;

            foreach (var kv in config.Themes)
            {
                string themeName = kv.Key;
                var tc = kv.Value ?? new UIColors();
                bool isSelected = config.SelectedTheme == themeName;
                float fade = 0.20f + Mathf.Min(idx, 6) * 0.03f;

                int col = idx % 2;
                int rowN = idx / 2;
                float xMin = gridLeft + col * (cardW + colGap);
                float xMax = xMin + cardW;
                float top = startTop - rowN * (cardH + vGap);
                float bottom = top - cardH;

                // Border (accent when active, hairline otherwise) + theme-colored card
                container.Add(new CuiPanel
                {
                    Image = { Color = isSelected ? tc.ButtonPrimary : "1 1 1 0.08", FadeIn = fade },
                    RectTransform = { AnchorMin = $"{xMin} {bottom}", AnchorMax = $"{xMax} {top}" }
                }, mainPanel);

                string card = mainPanel + $".theme{idx}";
                container.Add(new CuiPanel
                {
                    Image = { Color = tc.PanelBg, FadeIn = fade },
                    RectTransform = { AnchorMin = $"{xMin + 0.004f} {bottom + 0.007f}", AnchorMax = $"{xMax - 0.004f} {top - 0.007f}" }
                }, mainPanel, card);

                container.Add(new CuiLabel
                {
                    Text = { Text = themeName.ToUpper(), FontSize = 15, Align = TextAnchor.MiddleLeft, Color = tc.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.08 0.55", AnchorMax = "0.65 0.85" }
                }, card);

                if (isSelected)
                {
                    container.Add(new CuiPanel { Image = { Color = tc.ButtonPrimary }, RectTransform = { AnchorMin = "0.65 0.60", AnchorMax = "0.92 0.82" } }, card);
                    container.Add(new CuiLabel { Text = { Text = Msg("Active", player), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.05 0.05 0.05 1", Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.65 0.60", AnchorMax = "0.92 0.82" } }, card);
                }

                string[] sw = { tc.HeaderBg, tc.ContentBg, tc.ButtonSecondary, tc.ButtonPrimary };
                for (int s = 0; s < sw.Length; s++)
                {
                    float sMin = 0.08f + s * 0.21f;
                    container.Add(new CuiPanel
                    {
                        Image = { Color = sw[s] },
                        RectTransform = { AnchorMin = $"{sMin} 0.13", AnchorMax = $"{sMin + 0.20f} 0.40" }
                    }, card);
                }

                container.Add(new CuiButton
                {
                    Button = { Color = "0 0 0 0", Command = $"news.admin.settheme \"{themeName}\"" },
                    Text = { Text = "" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, card);

                idx++;
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
            PostToDiscord(ann, config.Discord.UseComponentsV2);
        }

        private void PostToDiscord(Announcement ann, bool useComponentsV2)
        {
            object payload = useComponentsV2 ? BuildDiscordComponentsV2(ann) : BuildDiscordEmbed(ann);
            string json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            webrequest.Enqueue(config.Discord.WebhookUrl, json, (code, response) =>
            {
                if (code >= 200 && code <= 299) return;

                if (useComponentsV2)
                {
                    PrintWarning($"Discord rejected the Components V2 message (Code {code}: {response}). Falling back to a standard embed. Sending components usually requires an application-owned webhook, not a plain channel webhook.");
                    PostToDiscord(ann, false);
                }
                else
                {
                    PrintError($"Discord Webhook failed! Code: {code} - Response: {response}");
                }
            }, this, RequestMethod.POST, new Dictionary<string, string> { { "Content-Type", "application/json" } });
        }

        private int DiscordEmbedColor(Announcement ann)
        {
            switch (ann.Type)
            {
                case AnnouncementType.Alert: return 15158332;
                case AnnouncementType.Warning: return 15105570;
                case AnnouncementType.Update: return 3066993;
                case AnnouncementType.Event: return 10181046;
                default: return 3447003;
            }
        }

        private string BuildDiscordBody(Announcement ann)
        {
            string discordBody = NormalizeBodyText(ann.Text);
            if (!string.IsNullOrEmpty(discordBody) && discordBody.Length > DiscordEmbedDescriptionLimit)
            {
                const string truncatedSuffix = "\n\n[Message truncated on Discord]";
                int keepLength = Math.Max(0, DiscordEmbedDescriptionLimit - truncatedSuffix.Length);
                discordBody = discordBody.Substring(0, Math.Min(keepLength, discordBody.Length)).TrimEnd() + truncatedSuffix;
            }
            return discordBody;
        }

        private object BuildDiscordEmbed(Announcement ann)
        {
            string content = string.IsNullOrEmpty(config.Discord.RoleMention) ? "" : config.Discord.RoleMention;
            return new
            {
                username = config.Discord.BotName,
                content = content,

                allowed_mentions = new { parse = new[] { "roles" } },
                embeds = new[]
                {
                    new
                    {
                        title = ann.Title,
                        description = BuildDiscordBody(ann),
                        color = DiscordEmbedColor(ann),
                        footer = new { text = $"Posted by {ann.Author} • {ann.Date}" },
                        image = string.IsNullOrEmpty(ann.ImageUrl) ? null : new { url = ann.ImageUrl }
                    }
                }
            };
        }

        // Builds a Discord "Components V2" webhook payload (message flag 1<<15).
        // With that flag set the message must NOT use content/embeds; the whole
        // message is layout components: Container(17) wrapping Text Display(10),
        // Media Gallery(12) and Separator(14). NOTE: Discord generally only lets
        // application-owned webhooks send components; a plain channel webhook is
        // rejected with code 50006 — PostToDiscord then falls back to an embed.
        private object BuildDiscordComponentsV2(Announcement ann)
        {
            int accentColor = DiscordEmbedColor(ann);
            string discordBody = BuildDiscordBody(ann);

            var inner = new List<object>();

            if (!string.IsNullOrEmpty(config.Discord.RoleMention))
                inner.Add(new { type = 10, content = config.Discord.RoleMention });

            if (!string.IsNullOrEmpty(ann.Title))
                inner.Add(new { type = 10, content = $"## {ann.Title}" });

            inner.Add(new { type = 10, content = $"-# {ann.Type.ToString().ToUpper()}" });

            if (!string.IsNullOrEmpty(ann.ImageUrl))
                inner.Add(new { type = 12, items = new[] { new { media = new { url = ann.ImageUrl } } } });

            if (!string.IsNullOrEmpty(discordBody))
                inner.Add(new { type = 10, content = discordBody });

            inner.Add(new { type = 14 });
            inner.Add(new { type = 10, content = $"-# Posted by {ann.Author} • {ann.Date}" });

            var container = new
            {
                type = 17,
                accent_color = accentColor,
                components = inner
            };

            return new
            {
                username = config.Discord.BotName,
                flags = 1 << 15,
                allowed_mentions = new { parse = new[] { "roles" } },
                components = new List<object> { container }
            };
        }
        #endregion
    }
}
 