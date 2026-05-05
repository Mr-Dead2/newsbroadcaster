using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NewsBroadcaster", "DEDA", "1.0.3")]
    [Description("Clean, modern news broadcaster with notifications")]
    public class NewsBroadcaster : RustPlugin
    {
        #region Fields & Constants
        [PluginReference] private Plugin ImageLibrary, Notify;

        private const string LayerName = "NewsBroadcasterUI";
        private const string NotificationLayer = "NewsNotificationUI";
        private const string ConfirmLayer = "NewsConfirmUI";
        private const string DataFile = "NewsBroadcaster_Data";

        private const string PermAdmin = "newsbroadcaster.admin";
        private const string PermView = "newsbroadcaster.view";

        private static readonly Regex CommandSplitRegex = new Regex(@"[\""].+?[\""]|[^ ]+", RegexOptions.Compiled);
        private ConfigData config;
        private StoredData storedData;
        private List<Announcement> announcements = new List<Announcement>();
        private Dictionary<ulong, Timer> autoCloseTimers = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, Timer> notificationTimers = new Dictionary<ulong, Timer>();

        private HashSet<ulong> playersWithUiOpen = new HashSet<ulong>();

        private Dictionary<ulong, Announcement> activeEditors = new Dictionary<ulong, Announcement>();
        private Dictionary<ulong, int> activeEditorIndices = new Dictionary<ulong, int>();
        private Dictionary<ulong, int> historyContentScrollOffsets = new Dictionary<ulong, int>();
        private const int MaxContentChars = 32768;
        private const int BodyVisibleLineCount = 14;
        private const int BodyScrollStepLines = 3;
        private const int BodyWrapCharacters = 52;
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
            public string Title;
            public string ImageUrl;
            public string Text;
            public string Date;
            public string Author;
            public AnnouncementType Type;
            public long Timestamp;
            public HashSet<ulong> LikedPlayers = new HashSet<ulong>();
        }

        enum AnnouncementType { Info, Warning, Alert, Event, Update }

        class ConfigData
        {
            public GeneralSettings General { get; set; } = new GeneralSettings();
            public NotificationSettings Notification { get; set; } = new NotificationSettings();
            public DiscordSettings Discord { get; set; } = new DiscordSettings();

            // Replaced single Colors object with Themes dictionary
            public string SelectedTheme { get; set; } = "Default";
            public Dictionary<string, UIColors> Themes { get; set; } = new Dictionary<string, UIColors>();

            // Helper to handle legacy config or missing themes
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

        class UIColors
        {
            public string PanelBg { get; set; } = "0.08 0.08 0.10 0.96";
            public string HeaderBg { get; set; } = "0.05 0.05 0.05 0.5"; // Transparent for blur
            public string ContentBg { get; set; } = "0.15 0.15 0.17 0.5"; 
            public string ButtonPrimary { get; set; } = "0.26 0.64 0.95 0.9"; // Modern Blue Accent
            public string ButtonSecondary { get; set; } = "0.22 0.22 0.24 0.9";
            public string TextTitle { get; set; } = "1.0 1.0 1.0 1.0";
            public string TextNormal { get; set; } = "0.9 0.9 0.9 1.0";
            public string TextMuted { get; set; } = "0.6 0.6 0.6 1.0";
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
                ["Unknown"] = "UNKNOWN"
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
            value = value.Replace("\\n", "\n");
            return value;
        }

        private List<string> BuildBodyDisplayLines(string text)
        {
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
                while (working.Length > BodyWrapCharacters)
                {
                    int take = BodyWrapCharacters;
                    int lastSpace = working.LastIndexOf(' ', Math.Min(BodyWrapCharacters - 1, working.Length - 1), Math.Min(BodyWrapCharacters, working.Length));
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

        private string GetVisibleBodySlice(string text, int offset)
        {
            var lines = BuildBodyDisplayLines(text);
            offset = ClampBodyOffset(text, offset);
            int take = Math.Min(BodyVisibleLineCount, Math.Max(0, lines.Count - offset));
            return string.Join("\n", lines.Skip(offset).Take(take).ToArray());
        }

        private int GetBodyMaxOffset(string text)
        {
            var lines = BuildBodyDisplayLines(text);
            return Math.Max(0, lines.Count - BodyVisibleLineCount);
        }

        private int GetBodyPageCount(string text)
        {
            var lines = BuildBodyDisplayLines(text);
            if (lines.Count <= BodyVisibleLineCount)
                return 1;

            return Math.Max(1, (int)Math.Ceiling((double)(lines.Count - BodyVisibleLineCount) / BodyScrollStepLines) + 1);
        }

        private int GetBodyPageIndex(string text, int offset)
        {
            offset = ClampBodyOffset(text, offset);
            return Math.Max(0, offset / BodyScrollStepLines);
        }

        private int ClampBodyOffset(string text, int offset)
        {
            int maxOffset = GetBodyMaxOffset(text);
            if (offset < 0) return 0;
            if (offset > maxOffset) return maxOffset;
            return offset;
        }

        private bool CanScrollBody(string text)
        {
            return BuildBodyDisplayLines(text).Count > BodyVisibleLineCount;
        }
        #endregion

        #region Configuration
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null) throw new Exception();

                // MIGRATION: If Themes is missing or empty, populate defaults and try to save old colors
                if (config.Themes == null) config.Themes = new Dictionary<string, UIColors>();

                if (config.Themes.Count == 0)
                {
                    // Create defaults first (similar to LoadDefaultConfig)
                    config.Themes["Default"] = new UIColors(); 
                    config.Themes["Dark"] = new UIColors { PanelBg = "0.05 0.05 0.05 0.98", HeaderBg = "0.02 0.02 0.02 0.9", ContentBg = "0.1 0.1 0.1 0.8", ButtonPrimary = "0.2 0.2 0.2 1.0", ButtonSecondary = "0.15 0.15 0.15 1.0", TextTitle = "0.9 0.9 0.9 1.0", TextNormal = "0.7 0.7 0.7 1.0", TextMuted = "0.4 0.4 0.4 1.0" };
                    config.Themes["Ocean"] = new UIColors { PanelBg = "0.05 0.1 0.15 0.96", HeaderBg = "0.02 0.05 0.08 0.8", ContentBg = "0.08 0.15 0.2 0.6", ButtonPrimary = "0.0 0.6 0.8 0.9", ButtonSecondary = "0.1 0.25 0.3 0.8", TextTitle = "0.8 0.95 1.0 1.0", TextNormal = "0.8 0.9 0.9 1.0", TextMuted = "0.4 0.6 0.7 1.0" };
                    config.Themes["Rust"] = new UIColors { PanelBg = "0.15 0.12 0.1 0.96", HeaderBg = "0.1 0.08 0.06 0.9", ContentBg = "0.2 0.18 0.15 0.6", ButtonPrimary = "0.8 0.25 0.1 0.9", ButtonSecondary = "0.3 0.25 0.2 0.9", TextTitle = "0.9 0.85 0.8 1.0", TextNormal = "0.8 0.75 0.7 1.0", TextMuted = "0.6 0.5 0.4 1.0" };

                    // If we had old colors (config.Colors would need be read manually from a JObject to be perfect, 
                    // but here we just rely on the fact that if it was loaded, it might be lost if we don't handle it precisely.
                    // For simplicity in this structure: The property 'Colors' is now a getter. 
                    // If JSON had "Colors": {...}, it's now ignored by the serializer for the new property.
                    // Realistically, users will just get the new defaults.

                    config.SelectedTheme = "Default";
                    SaveConfig();
                }
            }
            catch
            {
                config = new ConfigData();
                SaveConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            config.Themes = new Dictionary<string, UIColors>
            {
                ["Default"] = new UIColors(), // Uses default values from class
                ["Dark"] = new UIColors 
                { 
                    PanelBg = "0.05 0.05 0.05 0.98", 
                    HeaderBg = "0.02 0.02 0.02 0.9", 
                    ContentBg = "0.1 0.1 0.1 0.8", 
                    ButtonPrimary = "0.2 0.2 0.2 1.0", 
                    ButtonSecondary = "0.15 0.15 0.15 1.0",
                    TextTitle = "0.9 0.9 0.9 1.0",
                    TextNormal = "0.7 0.7 0.7 1.0",
                    TextMuted = "0.4 0.4 0.4 1.0"
                },
                ["Ocean"] = new UIColors 
                { 
                    PanelBg = "0.05 0.1 0.15 0.96", 
                    HeaderBg = "0.02 0.05 0.08 0.8", 
                    ContentBg = "0.08 0.15 0.2 0.6", 
                    ButtonPrimary = "0.0 0.6 0.8 0.9", 
                    ButtonSecondary = "0.1 0.25 0.3 0.8",
                    TextTitle = "0.8 0.95 1.0 1.0",
                    TextNormal = "0.8 0.9 0.9 1.0",
                    TextMuted = "0.4 0.6 0.7 1.0"
                },
                ["Rust"] = new UIColors 
                { 
                    PanelBg = "0.15 0.12 0.1 0.96", 
                    HeaderBg = "0.1 0.08 0.06 0.9", 
                    ContentBg = "0.2 0.18 0.15 0.6", 
                    ButtonPrimary = "0.8 0.25 0.1 0.9", 
                    ButtonSecondary = "0.3 0.25 0.2 0.9",
                    TextTitle = "0.9 0.85 0.8 1.0",
                    TextNormal = "0.8 0.75 0.7 1.0",
                    TextMuted = "0.6 0.5 0.4 1.0"
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

            // Fix/Backfill timestamps for older data
            long baseTime = DateTime.UtcNow.Ticks;
            bool changed = false;
            for (int i = 0; i < announcements.Count; i++)
            {
                if (announcements[i].Timestamp == 0)
                {
                    // Assume index 0 is newest
                    announcements[i].Timestamp = baseTime - (i * 10000); 
                    changed = true;
                }

                // DATA MIGRATION: Ensure LikedPlayers is not null
                if (announcements[i].LikedPlayers == null)
                {
                    announcements[i].LikedPlayers = new HashSet<ulong>();
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
            if (config.General.ShowNewsOnConnect && announcements.Count > 0)
            {
                var latest = announcements[0];
                long lastSeen = 0;
                if (storedData.LastSeenNews.TryGetValue(player.userID, out lastSeen))
                {
                    if (lastSeen >= latest.Timestamp) return;
                }

                timer.Once(2f, () => 
                {
                    if (player != null && player.IsConnected && announcements.Count > 0)
                    {
                        ShowPopup(player, announcements[0], true);
                        storedData.LastSeenNews[player.userID] = latest.Timestamp;
                        SaveAnnouncements();
                    }
                });
            }
        }

        void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUI(player);
                DestroyNotification(player);
                CuiHelper.DestroyUi(player, ConfirmLayer);
            }
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            historyContentScrollOffsets.Remove(player.userID);
            activeEditors.Remove(player.userID);
            activeEditorIndices.Remove(player.userID);
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

            // Use the raw full string to avoid default console splitting issues
            string fullCommand = arg.FullString;

            // Regex to match quoted strings OR unquoted words
            // Matches: "content with spaces" OR content_without_spaces
            var matches = CommandSplitRegex.Matches(fullCommand)
                .Cast<Match>()
                .Select(m => m.Value.Trim('"')) 
                .ToList();

            if (matches.Count > 0 && matches[0].Equals("news.show", StringComparison.OrdinalIgnoreCase))
            {
                matches.RemoveAt(0);
            }

            if (matches.Count < 3)
            {
                SendReply(arg, $"Error: Invalid arguments. Parsed {matches.Count}, expected at least 3.\nUsage: news.show \"Title\" \"ImageURL\" \"Text\" [Type]");
                return;
            }

            string title = matches[0];
            string img = matches[1];
            if (img == "-") img = "";

            string text;
            AnnouncementType type = AnnouncementType.Info;

            string potentialType = matches[matches.Count - 1];
            if (matches.Count > 3 && Enum.TryParse(potentialType, true, out AnnouncementType parsedType))
            {
                type = parsedType;

                text = string.Join(" ", matches.GetRange(2, matches.Count - 3));
            }
            else
            {

                text = string.Join(" ", matches.GetRange(2, matches.Count - 2));
            }

            text = Regex.Replace(text, @"https?:\/\/[^\s]+", "").Trim();
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = NormalizeBodyText(text);

            string authorName = arg.Connection != null ? arg.Connection.username : config.General.ServerName;

            var ann = new Announcement
            {
                Title = title,
                ImageUrl = img,
                Text = text,
                Date = DateTime.Now.ToString("MM/dd HH:mm"),
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

            int index = arg.GetInt(0, 0);
            if (index >= 0 && index < announcements.Count)
            {
                historyContentScrollOffsets[player.userID] = 0;
                ShowPopup(player, announcements[index], true);
            }
        }

        [ConsoleCommand("news.scrollbody")]
        private void CmdScrollBody(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;

            int index = arg.GetInt(0, -1);
            int offset = arg.GetInt(1, 0);
            if (index < 0 || index >= announcements.Count) return;

            historyContentScrollOffsets[player.userID] = ClampBodyOffset(announcements[index].Text, offset);
            ShowPopup(player, announcements[index], true);
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
                Date = DateTime.Now.ToString("MM/dd HH:mm"),
                ImageUrl = ""
            };
            activeEditorIndices[player.userID] = -1;
            ShowEditor(player);
        }

        [ConsoleCommand("news.admin.edit")]
        private void CmdNewsAdminEdit(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            int index = arg.GetInt(0, -1);
            if (index >= 0 && index < announcements.Count)
            {
                var original = announcements[index];
                activeEditors[player.userID] = new Announcement
                {
                    Title = original.Title,
                    ImageUrl = original.ImageUrl,
                    Text = original.Text,
                    Date = original.Date,
                    Author = original.Author,
                    Type = original.Type,
                    Timestamp = original.Timestamp,
                    LikedPlayers = new HashSet<ulong>(original.LikedPlayers)
                };
                activeEditorIndices[player.userID] = index;
                ShowEditor(player);
            }
        }

        [ConsoleCommand("news.admin.del")]
        private void CmdNewsAdminDelete(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            int index = arg.GetInt(0, -1);
            if (index >= 0 && index < announcements.Count)
            {
                announcements.RemoveAt(index);
                SaveAnnouncements();
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

            if (arg.Args == null || arg.Args.Length < 2) return;

            string field = arg.Args[0].ToLower();

            string value = string.Join(" ", arg.Args.Skip(1));
            string fullStr = arg.FullString;

            if (!string.IsNullOrEmpty(fullStr))
            {
                if (fullStr.StartsWith("news.editor.input", StringComparison.OrdinalIgnoreCase))
                    fullStr = fullStr.Substring("news.editor.input".Length).TrimStart();

                if (fullStr.StartsWith(field, StringComparison.OrdinalIgnoreCase))
                {
                    value = fullStr.Substring(field.Length).TrimStart();
                    if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                        value = value.Substring(1, value.Length - 2);
                }
            }
            var ann = activeEditors[player.userID];

            switch (field)
            {
                case "title": ann.Title = value; break;
                case "text":
                    ann.Text = NormalizeBodyText(value);
                    break;
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
            int index = activeEditorIndices[player.userID];

            if (index == -1)
            {
                ann.Timestamp = DateTime.UtcNow.Ticks;
                ann.Date = DateTime.Now.ToString("MM/dd HH:mm");
                announcements.Insert(0, ann);
                SendToDiscord(ann);
            }
            else
            {
                if (index < announcements.Count)
                {
                    announcements[index] = ann;
                }
            }

            SaveAnnouncements();
            if (!string.IsNullOrEmpty(ann.ImageUrl) && ImageLibrary != null)
                 ImageLibrary.Call("AddImage", ann.ImageUrl, ann.ImageUrl, 0UL);

            if (index == -1)
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
            activeEditorIndices.Remove(player.userID);

            ShowAdminList(player, 0);
            SendReply(player, index == -1 ? "Announcement saved and broadcasted to all players!" : "Announcement updated.");
        }

        [ConsoleCommand("news.editor.cancel")]
        private void CmdEditorCancel(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            activeEditors.Remove(player.userID);
            activeEditorIndices.Remove(player.userID);
            ShowAdminList(player, 0);
        }
        #endregion

        #region Notification UI
        [ConsoleCommand("news.like")]
        private void CmdNewsLike(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;

            int index = arg.GetInt(0, -1);
            if (index >= 0 && index < announcements.Count)
            {
                var ann = announcements[index];
                if (ann.LikedPlayers.Contains(player.userID))
                    ann.LikedPlayers.Remove(player.userID);
                else
                    ann.LikedPlayers.Add(player.userID);

                SaveAnnouncements();
                ShowPopup(player, ann, true, false); 
            }
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
            if (config.Notification.UseNotifyPlugin && Notify != null && Notify.IsLoaded)
            {
                Notify.Call("SendNotify", player.UserIDString, config.Notification.NotifyType, ann.Title);
                return;
            }

            DestroyNotification(player);

            int annIndex = announcements.IndexOf(ann);
            if (annIndex < 0) annIndex = 0;

            string anchorMin, anchorMax;
            bool isLeft = config.Notification.Position.ToLower() == "left";

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
                Button = { Color = "0 0 0 0.85", Command = $"news.view {annIndex}" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Text = { Text = "" }
            }, "Hud", NotificationLayer);

            container.Add(new CuiPanel
            {
                Image = { Color = typeColor },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.02 1" }
            }, NotificationLayer);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("NewAnnouncement", player), FontSize = 8, Align = TextAnchor.LowerLeft, Color = c.ButtonPrimary, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.06 0.65", AnchorMax = "0.9 0.9" }
            }, NotificationLayer);

            string title = ann.Title.Length > 25 ? ann.Title.Substring(0, 22) + "..." : ann.Title;
            container.Add(new CuiLabel
            {
                Text = { Text = title, FontSize = 12, Align = TextAnchor.UpperLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.06 0.1", AnchorMax = "0.9 0.65" }
            }, NotificationLayer);

            container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "news.close.notif" },
                Text = { Text = "✕", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
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
                Image = { Color = "0 0 0 0.85" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            string mainPanel = LayerName + ".Main";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg },
                RectTransform = { AnchorMin = "0.2 0.2", AnchorMax = "0.8 0.8" }
            }, LayerName, mainPanel);

            container.Add(new CuiPanel
            {
                Image = { Color = c.HeaderBg },
                RectTransform = { AnchorMin = "0 0.88", AnchorMax = "1 1" }
            }, mainPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{config.General.ServerName} <color={RgbaToHex(c.ButtonPrimary)}>//</color> {ann.Type.ToString().ToUpper()}", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.03 0.88", AnchorMax = "0.8 1" }
            }, mainPanel);

            container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "news.close" },
                Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.94 0.88", AnchorMax = "0.99 1" }
            }, mainPanel);

            bool hasImage = !string.IsNullOrEmpty(ann.ImageUrl);

            float contentLeft = hasImage ? 0.42f : 0.05f;

            if (hasImage)
            {
                string imgPanel = mainPanel + ".Img";

                container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0.3" },
                    RectTransform = { AnchorMin = "0.02 0.12", AnchorMax = "0.40 0.84" }
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

            container.Add(new CuiLabel
            {
                Text = { Text = (ann.Title ?? "").ToUpper(), FontSize = 26, Align = TextAnchor.LowerLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = $"{contentLeft} 0.74", AnchorMax = "0.98 0.84" }
            }, mainPanel);

            container.Add(new CuiPanel 
            { 
                 Image = { Color = c.ButtonPrimary },
                 RectTransform = { AnchorMin = $"{contentLeft} 0.73", AnchorMax = $"{contentLeft + 0.15f} 0.735" }
            }, mainPanel);

            int currentOffset = 0;
            historyContentScrollOffsets.TryGetValue(player.userID, out currentOffset);
            currentOffset = ClampBodyOffset(ann.Text, currentOffset);
            historyContentScrollOffsets[player.userID] = currentOffset;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.15" },
                RectTransform = { AnchorMin = $"{contentLeft} 0.15", AnchorMax = "0.925 0.70" }
            }, mainPanel, mainPanel + ".TextViewport");

            container.Add(new CuiLabel
            {
                Text = { Text = GetVisibleBodySlice(ann.Text, currentOffset), FontSize = 14, Align = TextAnchor.UpperLeft, Color = c.TextNormal, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = $"{contentLeft + 0.01f} 0.16", AnchorMax = "0.915 0.69" }
            }, mainPanel);

            if (CanScrollBody(ann.Text))
            {
                int maxOffset = GetBodyMaxOffset(ann.Text);
                int upOffset   = ClampBodyOffset(ann.Text, currentOffset - BodyScrollStepLines);
                int downOffset = ClampBodyOffset(ann.Text, currentOffset + BodyScrollStepLines);
                int announcementIndex = announcements.IndexOf(ann);
                if (announcementIndex < 0) announcementIndex = 0;

                const float trackLeft   = 0.932f;
                const float trackRight  = 0.978f;
                const float trackBottom = 0.165f;
                const float trackTop    = 0.690f;

                // Track background
                container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0.40" },
                    RectTransform = { AnchorMin = $"{trackLeft} {trackBottom}", AnchorMax = $"{trackRight} {trackTop}" }
                }, mainPanel);

                // Clickable jump zones stacked along the track — clicking anywhere scrolls to that position
                if (maxOffset > 0)
                {
                    const int jumpZones = 8;
                    float zoneH = (trackTop - trackBottom) / jumpZones;
                    for (int z = 0; z < jumpZones; z++)
                    {
                        float zMin = trackBottom + z * zoneH;
                        float zMax = zMin + zoneH;
                        // zone 0 = bottom of track = end of text; zone N-1 = top of track = start of text
                        int targetOffset = ClampBodyOffset(ann.Text, Mathf.RoundToInt((float)(jumpZones - 1 - z) / (jumpZones - 1) * maxOffset));
                        container.Add(new CuiButton
                        {
                            Button = { Color = "0 0 0 0", Command = $"news.scrollbody {announcementIndex} {targetOffset}" },
                            Text = { Text = "" },
                            RectTransform = { AnchorMin = $"{trackLeft} {zMin:F3}", AnchorMax = $"{trackRight} {zMax:F3}" }
                        }, mainPanel);
                    }
                }

                // Scroll handle — drawn over the zones; shows position and blocks clicks in its area
                float progress = maxOffset <= 0 ? 0f : Mathf.Clamp01((float)currentOffset / maxOffset);
                float handleH  = 0.08f;
                float usable   = (trackTop - trackBottom) - handleH;
                float handleMin = trackTop - handleH - usable * progress;
                float handleMax = handleMin + handleH;
                container.Add(new CuiPanel
                {
                    Image = { Color = c.ButtonPrimary },
                    RectTransform = { AnchorMin = $"{trackLeft + 0.003f} {handleMin:F3}", AnchorMax = $"{trackRight - 0.003f} {handleMax:F3}" }
                }, mainPanel);

                // Up arrow — greyed out when already at top
                bool canUp = currentOffset > 0;
                container.Add(new CuiButton
                {
                    Button = { Color = canUp ? c.ButtonSecondary : "0.12 0.12 0.12 0.6", Command = canUp ? $"news.scrollbody {announcementIndex} {upOffset}" : "" },
                    Text = { Text = "▲", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = canUp ? c.TextTitle : c.TextMuted, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = $"{trackLeft} 0.698", AnchorMax = $"{trackRight} 0.744" }
                }, mainPanel);

                // Down arrow — greyed out when already at bottom
                bool canDown = currentOffset < maxOffset;
                container.Add(new CuiButton
                {
                    Button = { Color = canDown ? c.ButtonSecondary : "0.12 0.12 0.12 0.6", Command = canDown ? $"news.scrollbody {announcementIndex} {downOffset}" : "" },
                    Text = { Text = "▼", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = canDown ? c.TextTitle : c.TextMuted, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = $"{trackLeft} 0.108", AnchorMax = $"{trackRight} 0.154" }
                }, mainPanel);

                // Line counter shown in the footer bar on the scrollbar column
                var allLines = BuildBodyDisplayLines(ann.Text);
                int firstLine = currentOffset + 1;
                int lastLine  = Math.Min(currentOffset + BodyVisibleLineCount, allLines.Count);
                container.Add(new CuiLabel
                {
                    Text = { Text = $"{firstLine}-{lastLine}/{allLines.Count}", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.925 0.015", AnchorMax = "0.988 0.093" }
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
            int count = ann.LikedPlayers.Count;
            int index = announcements.IndexOf(ann); 

            if (index >= 0)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "0 0 0 0", Command = $"news.like {index}" },
                    Text = { Text = $"❤ {count}", FontSize = 12, Align = TextAnchor.MiddleRight, Color = heartColor, Font = "robotocondensed-bold.ttf" },
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

            int perPage = config.General.AnnouncementsPerPage;
            int totalPages = Mathf.CeilToInt((float)announcements.Count / perPage);
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.8" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            string mainPanel = LayerName + ".List";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg },
                RectTransform = { AnchorMin = "0.15 0.1", AnchorMax = "0.85 0.9" }
            }, LayerName, mainPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg }, RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" } }, mainPanel);

            container.Add(new CuiLabel { 
                Text = { Text = $"{config.General.ServerName} <color={RgbaToHex(c.ButtonPrimary)}>//</color> {Msg("ArchiveTitle", player)}", FontSize = 16, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" }, 
                RectTransform = { AnchorMin = "0.03 0.92", AnchorMax = "0.8 1" } 
            }, mainPanel);

            container.Add(new CuiButton { 
                Button = { Color = "0.8 0.2 0.2 0", Command = "news.close" }, 
                Text = { Text = Msg("Close", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextMuted }, 
                RectTransform = { AnchorMin = "0.92 0.92", AnchorMax = "1 1" } 
            }, mainPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.08" } }, mainPanel);

            int start = page * perPage;
            int count = 0;
            float rowHeight = 0.82f / perPage; 
            float padding = 0.015f; 

            for (int i = start; i < announcements.Count && count < perPage; i++)
            {
                var ann = announcements[i];
                float top = 0.90f - (count * rowHeight) - padding;
                float bottom = top - rowHeight + (padding * 2);

                string itemPanel = mainPanel + $".{i}";
                string typeColor = GetTypeColor(ann.Type);

                container.Add(new CuiPanel
                {
                    Image = { Color = c.ContentBg },
                    RectTransform = { AnchorMin = $"0.02 {bottom}", AnchorMax = $"0.98 {top}" }
                }, mainPanel, itemPanel);

                container.Add(new CuiPanel { 
                    Image = { Color = typeColor }, 
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.005 1" } 
                }, itemPanel);

                container.Add(new CuiLabel 
                { 
                    Text = { Text = (ann.Title ?? "(no title)").ToUpper(), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.025 0.55", AnchorMax = "0.75 0.9" }
                }, itemPanel);

                container.Add(new CuiLabel 
                { 
                    Text = { Text = ann.Date, FontSize = 10, Align = TextAnchor.MiddleRight, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.75 0.55", AnchorMax = "0.975 0.9" }
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
                    Button = { Color = c.ButtonPrimary, Command = $"news.view {i}" },
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
                int r = Mathf.Clamp(Mathf.RoundToInt(float.Parse(parts[0]) * 255), 0, 255);
                int g = Mathf.Clamp(Mathf.RoundToInt(float.Parse(parts[1]) * 255), 0, 255);
                int b = Mathf.Clamp(Mathf.RoundToInt(float.Parse(parts[2]) * 255), 0, 255);
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
                Image = { Color = "0 0 0 0.9" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            string mainPanel = LayerName + ".Admin";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg },
                RectTransform = { AnchorMin = "0.15 0.15", AnchorMax = "0.85 0.85" }
            }, LayerName, mainPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg }, RectTransform = { AnchorMin = "0 0.90", AnchorMax = "1 1" } }, mainPanel);

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

            int start = page * perPage;
            int count = 0;
            float rowHeight = 0.82f / perPage; 
            float padding = 0.01f;

            if (announcements.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("NoAnnouncementsYet", player), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.1 0.3", AnchorMax = "0.9 0.7" }
                }, mainPanel);
            }

            for (int i = start; i < announcements.Count && count < perPage; i++)
            {
                var ann = announcements[i];
                float top = 0.88f - (count * rowHeight) - padding;
                float bottom = top - rowHeight + (padding * 2);

                string itemPanel = mainPanel + $".{i}";
                string typeColor = GetTypeColor(ann.Type);

                container.Add(new CuiPanel
                {
                    Image = { Color = c.ContentBg },
                    RectTransform = { AnchorMin = $"0.02 {bottom}", AnchorMax = $"0.98 {top}" }
                }, mainPanel, itemPanel);

                container.Add(new CuiPanel { 
                    Image = { Color = typeColor }, 
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.005 1" } 
                }, itemPanel);

                container.Add(new CuiLabel 
                { 
                    Text = { Text = (ann.Title ?? "(no title)").ToUpper(), FontSize = 12, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.02 0.4", AnchorMax = "0.6 0.9" }
                }, itemPanel);

                container.Add(new CuiLabel 
                { 
                    Text = { Text = ann.Date ?? "", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted },
                    RectTransform = { AnchorMin = "0.02 0.1", AnchorMax = "0.6 0.4" }
                }, itemPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.2 0.4 0.6 0.8", Command = $"news.admin.edit {i}" },
                    Text = { Text = "EDIT", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.80 0.2", AnchorMax = "0.88 0.8" }
                }, itemPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.6 0.2 0.2 0.8", Command = $"news.admin.delconfirm {i}" },
                    Text = { Text = "DEL", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.89 0.2", AnchorMax = "0.97 0.8" }
                }, itemPanel);

                count++;
            }

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.08" } }, mainPanel);

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
                Image = { Color = "0 0 0 0.9" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            string editorPanel = LayerName + ".Editor";
            container.Add(new CuiPanel { Image = { Color = c.PanelBg }, RectTransform = { AnchorMin = "0.2 0.2", AnchorMax = "0.8 0.8" } }, LayerName, editorPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" } }, editorPanel);
            container.Add(new CuiLabel { Text = { Text = activeEditorIndices[player.userID] == -1 ? Msg("CreateAnnouncement", player) : Msg("EditAnnouncement", player), FontSize = 14, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.04 0.9", AnchorMax = "0.9 1" } }, editorPanel);

            container.Add(new CuiButton { 
                Button = { Color = "0 0 0 0", Command = "news.editor.cancel" }, 
                Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = c.TextMuted }, 
                RectTransform = { AnchorMin = "0.94 0.9", AnchorMax = "0.99 1" } 
            }, editorPanel);

            container.Add(new CuiLabel { Text = { Text = Msg("AnnouncementTitle", player), FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.05 0.81", AnchorMax = "0.95 0.88" } }, editorPanel);
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0.5" }, RectTransform = { AnchorMin = "0.05 0.75", AnchorMax = "0.95 0.81" } }, editorPanel);
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
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0.5" }, RectTransform = { AnchorMin = "0.05 0.63", AnchorMax = "0.95 0.69" } }, editorPanel);
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
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0.5" }, RectTransform = { AnchorMin = "0.05 0.14", AnchorMax = "0.95 0.45" } }, editorPanel);
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
            container.Add(new CuiPanel { Image = { Color = c.HeaderBg }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.12" } }, editorPanel, footerPanel);

            container.Add(new CuiButton { 
                Button = { Color = "0.18 0.55 0.18 1", Command = "news.editor.save" }, 
                Text = { Text = Msg("SaveBroadcast", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" }, 
                RectTransform = { AnchorMin = "0.55 0.15", AnchorMax = "0.95 0.85" } 
            }, footerPanel);

            container.Add(new CuiButton { 
                Button = { Color = "0.55 0.18 0.18 1", Command = "news.editor.cancel" }, 
                Text = { Text = Msg("Cancel", player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" }, 
                RectTransform = { AnchorMin = "0.05 0.15", AnchorMax = "0.45 0.85" } 
            }, footerPanel);

            CuiHelper.AddUi(player, container);
        }

        private void ShowDeleteConfirm(BasePlayer player, int index)
        {
            if (index < 0 || index >= announcements.Count) return;
            CuiHelper.DestroyUi(player, ConfirmLayer);

            var ann = announcements[index];
            var container = new CuiElementContainer();
            var c = config.Colors;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.65" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", ConfirmLayer);

            string dialogPanel = ConfirmLayer + ".Dialog";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg },
                RectTransform = { AnchorMin = "0.34 0.40", AnchorMax = "0.66 0.60" }
            }, ConfirmLayer, dialogPanel);

            container.Add(new CuiPanel
            {
                Image = { Color = "0.55 0.12 0.12 0.95" },
                RectTransform = { AnchorMin = "0 0.76", AnchorMax = "1 1" }
            }, dialogPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = "DELETE ANNOUNCEMENT", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0 0.76", AnchorMax = "1 1" }
            }, dialogPanel);

            string displayTitle = (ann.Title ?? "").Length > 32 ? ann.Title.Substring(0, 29) + "..." : (ann.Title ?? "");
            container.Add(new CuiLabel
            {
                Text = { Text = $"\"{displayTitle}\"\nThis cannot be undone.", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextNormal, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0.05 0.30", AnchorMax = "0.95 0.74" }
            }, dialogPanel);

            container.Add(new CuiButton
            {
                Button = { Color = c.ButtonSecondary, Command = "news.confirm.close" },
                Text = { Text = "✕ CANCEL", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.05 0.06", AnchorMax = "0.46 0.27" }
            }, dialogPanel);

            container.Add(new CuiButton
            {
                Button = { Color = "0.65 0.12 0.12 1", Command = $"news.admin.del {index}" },
                Text = { Text = "✓ DELETE", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
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

            int index = arg.GetInt(0, -1);
            ShowDeleteConfirm(player, index);
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

                // Force refresh of the UI
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
            // Use current theme colors for the UI itself
            var c = config.Colors;

            // Overlay
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.9" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);

            // Main Panel
            string mainPanel = LayerName + ".Themes";
            container.Add(new CuiPanel
            {
                Image = { Color = c.PanelBg },
                RectTransform = { AnchorMin = "0.25 0.25", AnchorMax = "0.75 0.75" }
            }, LayerName, mainPanel);

            // Header
            container.Add(new CuiPanel { Image = { Color = c.HeaderBg }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" } }, mainPanel);
            container.Add(new CuiLabel { Text = { Text = Msg("SelectTheme", player), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" } }, mainPanel);

            // Close Button
            container.Add(new CuiButton { 
                Button = { Color = "0.8 0.2 0.2 0", Command = "news.admin" }, 
                Text = { Text = "✕", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextMuted }, 
                RectTransform = { AnchorMin = "0.92 0.9", AnchorMax = "0.99 1" } 
            }, mainPanel);

            // Theme List
            int count = 0;
            float buttonHeight = 0.12f;
            float startY = 0.8f;

            foreach (var themeName in config.Themes.Keys)
            {
                bool isSelected = config.SelectedTheme == themeName;
                string buttonColor = isSelected ? c.ButtonPrimary : c.ButtonSecondary;

                float top = startY - (count * (buttonHeight + 0.02f));
                float bottom = top - buttonHeight;

                container.Add(new CuiButton
                {
                    Button = { Color = buttonColor, Command = $"news.admin.settheme \"{themeName}\"" },
                    Text = { Text = themeName.ToUpper() + (isSelected ? $" {Msg("Active", player)}" : ""), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = isSelected ? "1 1 1 1" : c.TextNormal, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = $"0.1 {bottom}", AnchorMax = $"0.9 {top}" }
                }, mainPanel);

                count++;
            }

            CuiHelper.AddUi(player, container);
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
  