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
    [Info("NewsBroadcaster", "DEDA", "1.7.0")]
    [Description("Clean, modern news broadcaster with notifications")]
    public class NewsBroadcaster : RustPlugin
    {
        #region Fields & Constants
        [PluginReference] private Plugin ImageLibrary, Notify, ServerRewards, Economics;

        private const string LayerName = "NewsBroadcasterUI";
        private const string NotificationLayer = "NewsNotificationUI";
        private const string ConfirmLayer = "NewsConfirmUI";
        private const string DataFile = "NewsBroadcaster_Data";
        private const string ConfigBackupFile = "NewsBroadcaster_ConfigBackup";

        private const string PermPrefix = "newsbroadcaster.";
        private const string PermAdmin = PermPrefix + "admin";
        private const string PermView = PermPrefix + "view";

        private static readonly Regex CommandSplitRegex = new Regex(@"[\""].+?[\""]|[^ ]+", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"(https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MultiSpaceRegex = new Regex(@"[ \t]{2,}", RegexOptions.Compiled);
        private static readonly Regex BlankLinesRegex = new Regex(@"\n{3,}", RegexOptions.Compiled);
        private static readonly Regex PermSuffixRegex = new Regex(@"^[a-z0-9_]{1,32}$", RegexOptions.Compiled);
        private static readonly Regex FileNameRegex = new Regex(@"[^A-Za-z0-9_\-]", RegexOptions.Compiled);
        private static readonly Regex RelativeTimeRegex = new Regex(@"^\+\s*(\d+)\s*([mhdw])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
        private Dictionary<ulong, ArchiveFilter> archiveFilters = new Dictionary<ulong, ArchiveFilter>();

        // Permissions created on demand by per-announcement audience gating.
        private readonly HashSet<string> registeredCustomPerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Writes to the data file are debounced: bulk edits and incidental
        // updates (likes, read marks, last-seen) set the flag, a timer flushes.
        private bool dataDirty;
        private Timer saveTimer;
        private Timer scheduleTimer;

        private static readonly string InvariantDateFormat = "yyyy-MM-dd HH:mm";
        private const int MaxContentChars = 32768;
        private const int MaxTitleChars = 120;
        private const int MaxUrlChars = 512;
        private const int BodyWrapCharacters = 64;
        private const int DiscordEmbedDescriptionLimit = 4000;
        private const int DiscordEmbedTitleLimit = 250;
        private const float SaveDebounceSeconds = 20f;
        private const float ScheduleTickSeconds = 30f;
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

            // 0 = publish immediately. Otherwise the announcement stays hidden
            // from players until this tick is reached, then broadcasts once.
            public long PublishAt;

            // 0 = never expires. Otherwise the announcement drops out of the
            // player-facing archive once this tick passes (admins still see it).
            public long ExpiresAt;

            // Drafts are never broadcast and never visible to players.
            public bool Draft;

            // Set to mark this announcement as already broadcast, so the
            // scheduler does not re-announce it after a restart.
            public bool Broadcast;

            // Empty = every player with newsbroadcaster.view. Otherwise a
            // permission suffix: only holders of "newsbroadcaster.<suffix>" see it.
            public string Audience = "";
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

        // Per-player archive view state: which type, unread-only, and free-text search.
        class ArchiveFilter
        {
            public AnnouncementType? Type;
            public bool UnreadOnly;
            public string Search = "";

            public bool IsDefault => Type == null && !UnreadOnly && string.IsNullOrEmpty(Search);
        }

        enum AnnouncementType { Info, Warning, Alert, Event, Update }

        // Lifecycle state derived from Draft / PublishAt / ExpiresAt.
        enum AnnouncementStatus { Live, Draft, Scheduled, Expired }

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

            // Send an unread-count chat summary on connect instead of forcing
            // the popup open. Requires ShowNewsOnConnect.
            public bool UnreadSummaryOnConnect { get; set; } = false;
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
                ["StatReads"] = "READS",
                ["UnreadBadge"] = "UNREAD",

                ["UnreadSummary"] = "You have {0} unread announcement(s). Type /news to read them.",
                ["MarkedAllRead"] = "Marked {0} announcement(s) as read.",
                ["NothingToMark"] = "You have no unread announcements.",

                ["StatusLive"] = "LIVE",
                ["StatusDraft"] = "DRAFT",
                ["StatusScheduled"] = "SCHEDULED",
                ["StatusExpired"] = "EXPIRED",

                ["PublishAtLabel"] = "PUBLISH AT (empty = now)",
                ["ExpiresAtLabel"] = "EXPIRES AT (empty = never)",
                ["AudienceLabel"] = "AUDIENCE (empty = everyone)",
                ["DraftLabel"] = "DRAFT",
                ["DraftOn"] = "DRAFT — NOT VISIBLE",
                ["DraftOff"] = "PUBLISHED",
                ["ScheduleHint"] = "Use 2026-01-31 18:00 or a relative offset like +2h, +3d, +1w.",
                ["AudienceHint"] = "A permission suffix, e.g. \"vip\" grants newsbroadcaster.vip.",
                ["InvalidDate"] = "Could not read the date '{0}'. Use 2026-01-31 18:00 or +2h / +3d / +1w.",
                ["InvalidAudience"] = "Invalid audience '{0}'. Use letters, digits and underscores only ('admin' and 'view' are reserved).",
                ["ExpiryBeforePublish"] = "The expiry time must be later than the publish time.",
                ["SavedScheduled"] = "Announcement scheduled for {0}.",
                ["SavedDraft"] = "Draft saved. It is not visible to players yet.",

                ["FilterAll"] = "ALL",
                ["FilterUnread"] = "UNREAD",
                ["ClearFilters"] = "CLEAR",
                ["MarkAllRead"] = "MARK ALL READ",
                ["SearchPlaceholder"] = "SEARCH TITLE OR TEXT...",
                ["NoMatches"] = "No announcements match this filter.",
                ["ShowingCount"] = "{0} OF {1}",

                ["PlayerNotFound"] = "Player not found.",
                ["NoAnnouncementsStored"] = "No announcements stored.",
                ["DeletedAnnouncement"] = "Deleted announcement: '{0}'",
                ["InvalidIndex"] = "Invalid index. Use 'news.list' to see all announcements with their indices.",
                ["TriggeredFor"] = "News popup triggered for {0}",
                ["NoAnnouncementsAvailable"] = "No announcements available.",
                ["ThemeSet"] = "Theme set to: {0}",
                ["ThemeNotFound"] = "Theme '{0}' was not found in the configuration.",
                ["ThemeAvailable"] = "Available themes: {0}",
                ["UsageShow"] = "Usage: news.show \"Title\" \"ImageURL\" \"Text\" [Type]",
                ["UsageTrigger"] = "Usage: news.trigger <SteamID/Name> [NewsIndex]",
                ["UsageDelete"] = "Usage: news.delete <index>",
                ["UsageSetTheme"] = "Usage: news.admin.settheme \"ThemeName\"",
                ["UsageImport"] = "Usage: news.import <filename> [merge|replace]",
                ["ExportDone"] = "Exported {0} announcement(s) to oxide/data/{1}.json",
                ["ImportDone"] = "Imported {0} announcement(s) ({1} skipped as duplicates).",
                ["ImportEmpty"] = "No announcements found in oxide/data/{0}.json",
                ["ImportFailed"] = "Could not read oxide/data/{0}.json — {1}"
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

            // Only the documented "\n" escape becomes a line break. The old "/n"
            // alias also matched ordinary text ("w/newbies", "and/not") and
            // corrupted it, so it is no longer honoured.
            value = value.Replace("\\n", "\n");
            value = LinkRegex.Replace(value, string.Empty);
            value = MultiSpaceRegex.Replace(value, " ");
            value = BlankLinesRegex.Replace(value, "\n\n");

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
                ["pinned"] = ann.Pinned,
                ["draft"] = ann.Draft,
                ["status"] = StatusOf(ann).ToString(),
                ["publishAt"] = ann.PublishAt,
                ["expiresAt"] = ann.ExpiresAt,
                ["audience"] = ann.Audience ?? string.Empty
            };
        }

        // CUI anchors are parsed with invariant formatting. Interpolating a float
        // directly picks up the server's culture, so a comma-decimal locale
        // (de/fr/lv/ru hosts) would emit "0,51 0.8" and break the layout.
        private static string A(float x, float y)
        {
            return x.ToString("0.#####", CultureInfo.InvariantCulture) + " " +
                   y.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? string.Empty;
            return max <= 3 ? value.Substring(0, max) : value.Substring(0, max - 3).TrimEnd() + "...";
        }

        #region Access helpers
        private BasePlayer PlayerFrom(ConsoleSystem.Arg arg) => arg?.Connection?.player as BasePlayer;

        private bool HasAdmin(BasePlayer player)
        {
            if (player == null) return false;
            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        // Server console and RCON (no connection) are always allowed.
        private bool HasAdmin(ConsoleSystem.Arg arg)
        {
            if (arg?.Connection == null) return true;
            return arg.IsAdmin || permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin);
        }

        private bool HasView(BasePlayer player)
        {
            if (player == null) return false;
            return permission.UserHasPermission(player.UserIDString, PermView) || HasAdmin(player);
        }

        // Rejects the plugin's own reserved suffixes so a custom audience can
        // never widen itself into newsbroadcaster.admin.
        private static bool IsValidAudience(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return true;
            suffix = suffix.ToLowerInvariant();
            if (suffix == "admin" || suffix == "view") return false;
            return PermSuffixRegex.IsMatch(suffix);
        }

        private static string FullPermission(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return null;
            suffix = suffix.Trim().ToLowerInvariant();
            return IsValidAudience(suffix) ? PermPrefix + suffix : null;
        }

        private void EnsurePermissionRegistered(string suffix)
        {
            string full = FullPermission(suffix);
            if (full == null) return;
            if (!registeredCustomPerms.Add(full)) return;
            permission.RegisterPermission(full, this);
        }
        #endregion

        #region Lifecycle helpers
        private static AnnouncementStatus StatusOf(Announcement ann, long now)
        {
            if (ann == null) return AnnouncementStatus.Expired;
            if (ann.Draft) return AnnouncementStatus.Draft;
            if (ann.PublishAt > 0 && ann.PublishAt > now) return AnnouncementStatus.Scheduled;
            if (ann.ExpiresAt > 0 && ann.ExpiresAt <= now) return AnnouncementStatus.Expired;
            return AnnouncementStatus.Live;
        }

        private static AnnouncementStatus StatusOf(Announcement ann) => StatusOf(ann, DateTime.UtcNow.Ticks);

        // Pinned first, then newest first. Used for every list the plugin renders.
        private static int CompareForDisplay(Announcement a, Announcement b)
        {
            if (a.Pinned != b.Pinned) return a.Pinned ? -1 : 1;
            return b.Timestamp.CompareTo(a.Timestamp);
        }

        private bool CanSee(BasePlayer player, Announcement ann, long now)
        {
            if (player == null || ann == null) return false;
            if (StatusOf(ann, now) != AnnouncementStatus.Live) return false;

            string perm = FullPermission(ann.Audience);
            if (perm == null) return true;
            return permission.UserHasPermission(player.UserIDString, perm) || HasAdmin(player);
        }

        // Everything this player is allowed to read right now, in display order.
        private List<Announcement> GetVisibleFor(BasePlayer player)
        {
            long now = DateTime.UtcNow.Ticks;
            var list = new List<Announcement>();
            for (int i = 0; i < announcements.Count; i++)
                if (CanSee(player, announcements[i], now)) list.Add(announcements[i]);
            list.Sort(CompareForDisplay);
            return list;
        }

        private int CountUnread(BasePlayer player)
        {
            int unread = 0;
            var visible = GetVisibleFor(player);
            for (int i = 0; i < visible.Count; i++)
                if (!(visible[i].ReadByPlayers?.Contains(player.userID) ?? false)) unread++;
            return unread;
        }

        private static readonly string[] AcceptedDateFormats =
        {
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd"
        };

        // Accepts "" / "-" (unset), a relative offset ("+2h", "+3d", "+1w"),
        // or an absolute server-local timestamp ("2026-09-20 18:00").
        private bool TryParseWhen(string value, out long ticks)
        {
            ticks = 0;
            if (string.IsNullOrEmpty(value)) return true;
            value = value.Trim();
            if (value.Length == 0 || value == "-" || value == "0") return true;

            var rel = RelativeTimeRegex.Match(value);
            if (rel.Success)
            {
                int amount;
                if (!int.TryParse(rel.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount <= 0)
                    return false;

                TimeSpan span;
                switch (rel.Groups[2].Value.ToLowerInvariant())
                {
                    case "m": span = TimeSpan.FromMinutes(amount); break;
                    case "h": span = TimeSpan.FromHours(amount); break;
                    case "d": span = TimeSpan.FromDays(amount); break;
                    default: span = TimeSpan.FromDays(amount * 7d); break;
                }

                try { ticks = DateTime.UtcNow.Add(span).Ticks; }
                catch { return false; }
                return true;
            }

            DateTime parsed;
            if (DateTime.TryParseExact(value, AcceptedDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                try { ticks = DateTime.SpecifyKind(parsed, DateTimeKind.Local).ToUniversalTime().Ticks; }
                catch { return false; }
                return true;
            }

            return false;
        }

        // Renders a UTC tick count as server-local wall time.
        private static string FormatWhen(long ticks)
        {
            if (ticks <= 0) return string.Empty;
            try { return new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString(InvariantDateFormat, CultureInfo.InvariantCulture); }
            catch { return string.Empty; }
        }

        // Prefers the stored timestamp so the date always matches the clock;
        // falls back to the legacy pre-formatted string for old data.
        private static string DisplayDate(Announcement ann)
        {
            if (ann == null) return string.Empty;
            string formatted = FormatWhen(ann.Timestamp);
            return string.IsNullOrEmpty(formatted) ? (ann.Date ?? string.Empty) : formatted;
        }

        private ArchiveFilter GetArchiveFilter(ulong userId)
        {
            ArchiveFilter filter;
            if (!archiveFilters.TryGetValue(userId, out filter))
            {
                filter = new ArchiveFilter();
                archiveFilters[userId] = filter;
            }
            return filter;
        }
        #endregion
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

                if (ClampConfig()) needsSave = true;

                if (needsSave) SaveConfig();
            }
            catch (Exception ex)
            {
                // Never silently replace an admin's config: stash whatever was on
                // disk into oxide/data so the values can be recovered by hand.
                try
                {
                    var broken = Config.ReadObject<JObject>();
                    if (broken != null)
                    {
                        Interface.Oxide.DataFileSystem.WriteObject(ConfigBackupFile, broken);
                        PrintError($"Could not read the config ({ex.Message}). A copy was saved to oxide/data/{ConfigBackupFile}.json and defaults were applied.");
                    }
                    else PrintError($"Could not read the config ({ex.Message}). Defaults were applied.");
                }
                catch
                {
                    PrintError($"Could not read the config ({ex.Message}). Defaults were applied.");
                }

                LoadDefaultConfig();
                ClampConfig();
                SaveConfig();
            }
        }

        // Values that would divide by zero, wipe stored data, or create
        // zero-length timers are pulled back into a usable range.
        private bool ClampConfig()
        {
            bool changed = false;

            if (config.General == null) { config.General = new GeneralSettings(); changed = true; }
            if (config.Notification == null) { config.Notification = new NotificationSettings(); changed = true; }
            if (config.Discord == null) { config.Discord = new DiscordSettings(); changed = true; }
            if (config.Rewards == null) { config.Rewards = new RewardSettings(); changed = true; }

            changed |= ClampInt(config.General.AnnouncementsPerPage, 1, 12, v => config.General.AnnouncementsPerPage = v, "General.AnnouncementsPerPage");
            changed |= ClampInt(config.General.MaxStoredAnnouncements, 1, 5000, v => config.General.MaxStoredAnnouncements = v, "General.MaxStoredAnnouncements");
            changed |= ClampInt(config.General.AutoCloseSeconds, 1, 3600, v => config.General.AutoCloseSeconds = v, "General.AutoCloseSeconds");
            changed |= ClampInt(config.Notification.Duration, 1, 3600, v => config.Notification.Duration = v, "Notification.Duration");
            changed |= ClampInt(config.Rewards.ReadDelaySeconds, 1, 3600, v => config.Rewards.ReadDelaySeconds = v, "Rewards.ReadDelaySeconds");

            return changed;
        }

        private bool ClampInt(int current, int min, int max, Action<int> assign, string label)
        {
            int clamped = Mathf.Clamp(current, min, max);
            if (clamped == current) return false;
            assign(clamped);
            PrintWarning($"Config value {label} was {current}, which is out of range — using {clamped} instead (allowed: {min}-{max}).");
            return true;
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
            RegisterAudiencePermissions();

            // Publishes anything whose scheduled time has arrived and retires
            // anything that has expired while the server was down.
            scheduleTimer = timer.Every(ScheduleTickSeconds, ProcessScheduled);
            ProcessScheduled();
        }

        // ImageLibrary may load after this plugin; re-import when it appears.
        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "ImageLibrary") RegisterAllImages();
        }

        private void RegisterAudiencePermissions()
        {
            for (int i = 0; i < announcements.Count; i++)
                EnsurePermissionRegistered(announcements[i].Audience);
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

        // Writes the data file immediately. Use for anything an admin would
        // expect to survive a crash straight away (create / edit / delete).
        private void SaveAnnouncements()
        {
            dataDirty = false;
            if (saveTimer != null && !saveTimer.Destroyed) saveTimer.Destroy();
            saveTimer = null;

            TrimStoredAnnouncements();
            PruneLastSeen();

            announcements = storedData.Announcements;
            Interface.Oxide.DataFileSystem.WriteObject(DataFile, storedData);
        }

        // Queues a write instead of performing one. Used by high-frequency,
        // low-value updates (likes, read marks, last-seen) so a busy server
        // does not re-serialise the whole data file dozens of times a minute.
        private void MarkDataDirty()
        {
            dataDirty = true;
            if (saveTimer != null && !saveTimer.Destroyed) return;
            saveTimer = timer.Once(SaveDebounceSeconds, () =>
            {
                saveTimer = null;
                if (dataDirty) SaveAnnouncements();
            });
        }

        // Keeps the newest N, but never culls a pinned announcement to make
        // room — pinned posts (rules, wipe schedule) are the ones that must stay.
        private void TrimStoredAnnouncements()
        {
            int max = config.General.MaxStoredAnnouncements;
            if (max < 1) max = 1;
            if (storedData.Announcements.Count <= max) return;

            var ordered = new List<Announcement>(storedData.Announcements);
            ordered.Sort((a, b) =>
            {
                int ap = TrimPriority(a), bp = TrimPriority(b);
                if (ap != bp) return bp.CompareTo(ap);
                return b.Timestamp.CompareTo(a.Timestamp);
            });

            var keep = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ordered.Count && keep.Count < max; i++)
                keep.Add(ordered[i].Id);

            // Rebuild in the original order so "newest first" insertion still holds.
            storedData.Announcements = storedData.Announcements.Where(a => keep.Contains(a.Id)).ToList();
        }

        // Pinned posts, drafts and anything still queued to publish survive the
        // cull: silently deleting a scheduled announcement before it fires, or
        // the server rules post, is never what the admin meant by a size cap.
        private static int TrimPriority(Announcement a)
        {
            if (a.Pinned) return 3;
            if (a.Draft) return 2;
            if (a.PublishAt > 0 && !a.Broadcast) return 2;
            return 0;
        }

        // Drops last-seen markers that sit behind every retained announcement:
        // those players are already treated as fully behind, so the entry is
        // dead weight that otherwise grows by one row per player, forever.
        private void PruneLastSeen()
        {
            if (storedData.LastSeenNews == null || storedData.LastSeenNews.Count == 0) return;

            long oldest = long.MaxValue;
            for (int i = 0; i < storedData.Announcements.Count; i++)
            {
                long ts = storedData.Announcements[i].Timestamp;
                if (ts > 0 && ts < oldest) oldest = ts;
            }
            if (oldest == long.MaxValue) return;

            List<ulong> stale = null;
            foreach (var kv in storedData.LastSeenNews)
            {
                if (kv.Value >= oldest) continue;
                (stale ?? (stale = new List<ulong>())).Add(kv.Key);
            }
            if (stale == null) return;
            for (int i = 0; i < stale.Count; i++) storedData.LastSeenNews.Remove(stale[i]);
        }

        // Broadcasts an announcement to everyone currently allowed to read it.
        private void PublishToPlayers(Announcement ann)
        {
            if (ann == null) return;
            long now = DateTime.UtcNow.Ticks;

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null || !p.IsConnected) continue;
                if (!CanSee(p, ann, now)) continue;

                if (config.Notification.Enabled) ShowNotification(p, ann);
                else ShowPopup(p, ann, false, true);
            }
        }

        // Fires scheduled announcements once their time arrives.
        private void ProcessScheduled()
        {
            if (announcements == null || announcements.Count == 0) return;

            long now = DateTime.UtcNow.Ticks;
            bool changed = false;

            for (int i = 0; i < announcements.Count; i++)
            {
                var ann = announcements[i];
                if (ann.Draft || ann.Broadcast) continue;
                if (ann.PublishAt <= 0 || ann.PublishAt > now) continue;

                ann.Broadcast = true;
                changed = true;

                // Missed its whole window while the server was down — retire it quietly.
                if (ann.ExpiresAt > 0 && ann.ExpiresAt <= now) continue;

                Interface.CallHook("OnNewsBroadcast", BuildHookData(ann));
                SendToDiscord(ann);
                PublishToPlayers(ann);
            }

            if (changed) SaveAnnouncements();
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

                if (announcements[i].Audience == null)
                {
                    announcements[i].Audience = string.Empty;
                    changed = true;
                }

                // Legacy posts were broadcast when they were created; without
                // this the scheduler would treat them as still pending.
                if (!announcements[i].Broadcast && announcements[i].PublishAt <= 0)
                {
                    announcements[i].Broadcast = true;
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

        // Newest announcement this player may read, ignoring pin order.
        private Announcement NewestVisible(BasePlayer player)
        {
            long now = DateTime.UtcNow.Ticks;
            Announcement newest = null;
            for (int i = 0; i < announcements.Count; i++)
            {
                var a = announcements[i];
                if (!CanSee(player, a, now)) continue;
                if (newest == null || a.Timestamp > newest.Timestamp) newest = a;
            }
            return newest;
        }

        void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null || !config.General.ShowNewsOnConnect) return;

            var latest = NewestVisible(player);
            if (latest == null) return;

            long lastSeen;
            if (storedData.LastSeenNews.TryGetValue(player.userID, out lastSeen) && lastSeen >= latest.Timestamp)
                return;

            timer.Once(2f, () =>
            {
                if (player == null || !player.IsConnected) return;

                var current = NewestVisible(player);
                if (current == null) return;

                storedData.LastSeenNews[player.userID] = current.Timestamp;
                MarkDataDirty();

                // Quieter alternative to hijacking the screen on spawn.
                if (config.General.UnreadSummaryOnConnect)
                {
                    int unread = CountUnread(player);
                    if (unread > 0) SendReply(player, Msg("UnreadSummary", player, unread));
                    return;
                }

                ShowPopup(player, current, false);
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

            saveTimer?.Destroy();
            saveTimer = null;
            scheduleTimer?.Destroy();
            scheduleTimer = null;

            // Anything the debounce timer was still holding must not be lost.
            if (dataDirty) SaveAnnouncements();

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
            archiveFilters.Clear();
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            ulong id = player.userID;
            activeEditors.Remove(id);
            activeEditorIds.Remove(id);
            playersWithUiOpen.Remove(id);
            adminSelections.Remove(id);
            archiveFilters.Remove(id);
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
            if (!HasAdmin(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            string fullCommand = arg.FullString.ToString() ?? string.Empty;
            var rawMatches = CommandSplitRegex.Matches(fullCommand).Cast<Match>().ToList();
            var values = rawMatches.Select(m => m.Value.Trim('"')).ToList();

            if (values.Count < 3)
            {
                SendReply(arg, Msg("UsageShow"));
                return;
            }

            string title = values[0];
            string img = values[1];
            if (img == "-") img = "";

            string text;
            AnnouncementType type = AnnouncementType.Info;

            bool lastWasUnquoted = rawMatches.Count > 0 && !rawMatches[rawMatches.Count - 1].Value.StartsWith("\"");
            if (values.Count > 3 && lastWasUnquoted && TryParseType(values[values.Count - 1], out AnnouncementType parsedType))
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
                Timestamp = DateTime.UtcNow.Ticks,
                Broadcast = true
            };

            if (!string.IsNullOrEmpty(img) && ImageLibrary != null)
            {
                ImageLibrary.Call("AddImage", img, img, 0UL);
            }

            announcements.Insert(0, ann);
            SaveAnnouncements();

            Interface.CallHook("OnNewsBroadcast", BuildHookData(ann));
            SendToDiscord(ann);

            PublishToPlayers(ann);

            SendReply(arg, Msg("NewsBroadcasted"));
        }

        [ConsoleCommand("news.trigger")]
        private void CmdNewsTrigger(ConsoleSystem.Arg arg)
        {
            if (!HasAdmin(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, Msg("UsageTrigger"));
                return;
            }

            var player = BasePlayer.Find(arg.GetString(0));
            if (player == null || !player.IsConnected)
            {
                SendReply(arg, Msg("PlayerNotFound"));
                return;
            }

            int index = arg.GetInt(1, 0);
            if (index < 0 || index >= announcements.Count) index = 0;

            if (announcements.Count > 0)
            {
                ShowPopup(player, announcements[index], true);
                SendReply(arg, Msg("TriggeredFor", null, player.displayName));
            }
            else
            {
                SendReply(arg, Msg("NoAnnouncementsAvailable"));
            }
        }

        [ConsoleCommand("news.delete")]
        private void CmdNewsDelete(ConsoleSystem.Arg arg)
        {
            if (!HasAdmin(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, Msg("UsageDelete"));
                return;
            }

            int index = arg.GetInt(0, -1);
            if (index >= 0 && index < announcements.Count)
            {
                var removed = announcements[index];
                announcements.RemoveAt(index);
                SaveAnnouncements();
                Interface.CallHook("OnNewsDeleted", BuildHookData(removed));
                SendReply(arg, Msg("DeletedAnnouncement", null, removed.Title));
            }
            else
            {
                SendReply(arg, Msg("InvalidIndex"));
            }
        }

        [ConsoleCommand("news.list")]
        private void CmdNewsList(ConsoleSystem.Arg arg)
        {
            if (!HasAdmin(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            if (announcements.Count == 0)
            {
                SendReply(arg, Msg("NoAnnouncementsStored"));
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== Stored Announcements ({announcements.Count}) ===");
            for (int i = 0; i < announcements.Count; i++)
            {
                var a = announcements[i];
                sb.AppendLine($"[{i}] [{StatusOf(a)}] [{a.Type}] \"{a.Title}\" — {DisplayDate(a)} by {a.Author}");
            }
            SendReply(arg, sb.ToString().TrimEnd());
        }

        [ChatCommand("news")]
        private void CmdChatNews(BasePlayer player, string cmd, string[] args)
        {
            if (!HasView(player))
            {
                SendReply(player, Msg("NoPermissionView", player));
                return;
            }

            if (args == null || args.Length == 0)
            {
                ShowHistory(player, 0);
                return;
            }

            string sub = args[0].ToLowerInvariant();

            if (sub == "read")
            {
                int marked = MarkAllRead(player);
                SendReply(player, marked > 0 ? Msg("MarkedAllRead", player, marked) : Msg("NothingToMark", player));
                return;
            }

            var filter = GetArchiveFilter(player.userID);

            if (sub == "unread")
            {
                filter.UnreadOnly = true;
                ShowHistory(player, 0);
                return;
            }

            int page;
            bool numeric = int.TryParse(sub, NumberStyles.Integer, CultureInfo.InvariantCulture, out page);
            if (numeric && page > 0)
            {
                ShowHistory(player, page - 1);
                return;
            }

            AnnouncementType parsedType;
            if (!numeric && TryParseType(sub, out parsedType))
            {
                filter.Type = parsedType;
                ShowHistory(player, 0);
                return;
            }

            // Anything else is treated as a search term.
            filter.Search = Truncate(string.Join(" ", args), 64);
            ShowHistory(player, 0);
        }

        // Strict announcement-type parsing: names only, and only real members.
        private static bool TryParseType(string value, out AnnouncementType type)
        {
            type = AnnouncementType.Info;
            if (string.IsNullOrEmpty(value)) return false;

            int ignored;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ignored)) return false;

            AnnouncementType parsed;
            if (!Enum.TryParse(value, true, out parsed)) return false;
            if (!Enum.IsDefined(typeof(AnnouncementType), parsed)) return false;

            type = parsed;
            return true;
        }

        // Marks everything currently readable as read. Deliberately does NOT
        // pay read rewards — those require actually opening the announcement.
        private int MarkAllRead(BasePlayer player)
        {
            var visible = GetVisibleFor(player);
            int marked = 0;

            for (int i = 0; i < visible.Count; i++)
            {
                var ann = visible[i];
                if (ann.ReadByPlayers == null) ann.ReadByPlayers = new HashSet<ulong>();
                if (!ann.ReadByPlayers.Add(player.userID)) continue;

                marked++;
                Interface.CallHook("OnNewsRead", player, BuildHookData(ann));
            }

            if (marked > 0) MarkDataDirty();
            return marked;
        }

        // Console/CUI input fields deliver their value as the whole argument
        // string, quoted when it contains spaces.
        private static string ReadFullArg(ConsoleSystem.Arg arg)
        {
            string value = (arg?.FullString ?? string.Empty).Trim();
            if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
                value = value.Substring(1, value.Length - 2);
            return value;
        }

        [ConsoleCommand("news.markread")]
        private void CmdNewsMarkRead(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasView(player)) return;

            bool uiOpen = playersWithUiOpen.Contains(player.userID);
            int marked = MarkAllRead(player);

            SendReply(player, marked > 0 ? Msg("MarkedAllRead", player, marked) : Msg("NothingToMark", player));
            if (uiOpen) ShowHistory(player, 0);
        }

        [ConsoleCommand("news.filter.type")]
        private void CmdNewsFilterType(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasView(player)) return;

            var filter = GetArchiveFilter(player.userID);
            string raw = arg.GetString(0, "all");

            if (string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase))
            {
                filter.Type = null;
            }
            else
            {
                AnnouncementType parsed;
                if (!TryParseType(raw, out parsed)) return;
                filter.Type = parsed;
            }

            ShowHistory(player, 0);
        }

        [ConsoleCommand("news.filter.unread")]
        private void CmdNewsFilterUnread(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasView(player)) return;

            var filter = GetArchiveFilter(player.userID);
            filter.UnreadOnly = !filter.UnreadOnly;
            ShowHistory(player, 0);
        }

        [ConsoleCommand("news.filter.search")]
        private void CmdNewsFilterSearch(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasView(player)) return;

            GetArchiveFilter(player.userID).Search = Truncate(ReadFullArg(arg), 64);
            ShowHistory(player, 0);
        }

        [ConsoleCommand("news.filter.clear")]
        private void CmdNewsFilterClear(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasView(player)) return;

            archiveFilters.Remove(player.userID);
            ShowHistory(player, 0);
        }

        // Strips anything that could escape oxide/data (path separators, dots).
        private static string SanitizeFileName(string name) => FileNameRegex.Replace(name ?? string.Empty, string.Empty);

        [ConsoleCommand("news.export")]
        private void CmdNewsExport(ConsoleSystem.Arg arg)
        {
            if (!HasAdmin(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            string file = SanitizeFileName(arg.GetString(0, string.Empty));
            if (string.IsNullOrEmpty(file))
                file = "NewsBroadcaster_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);

            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(file, announcements);
                SendReply(arg, Msg("ExportDone", null, announcements.Count, file));
            }
            catch (Exception ex)
            {
                SendReply(arg, Msg("ImportFailed", null, file, ex.Message));
            }
        }

        [ConsoleCommand("news.import")]
        private void CmdNewsImport(ConsoleSystem.Arg arg)
        {
            if (!HasAdmin(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            string file = SanitizeFileName(arg.GetString(0, string.Empty));
            if (string.IsNullOrEmpty(file))
            {
                SendReply(arg, Msg("UsageImport"));
                return;
            }

            List<Announcement> incoming;
            try { incoming = Interface.Oxide.DataFileSystem.ReadObject<List<Announcement>>(file); }
            catch (Exception ex)
            {
                SendReply(arg, Msg("ImportFailed", null, file, ex.Message));
                return;
            }

            if (incoming == null || incoming.Count == 0)
            {
                SendReply(arg, Msg("ImportEmpty", null, file));
                return;
            }

            if (string.Equals(arg.GetString(1, "merge"), "replace", StringComparison.OrdinalIgnoreCase))
                announcements.Clear();

            var known = new HashSet<string>(announcements.Select(a => a.Id), StringComparer.Ordinal);
            int added = 0, skipped = 0;

            foreach (var inc in incoming)
            {
                if (inc == null || string.IsNullOrEmpty(inc.Title)) { skipped++; continue; }

                // Same id already present: treat as a duplicate rather than overwriting.
                if (!string.IsNullOrEmpty(inc.Id) && !known.Add(inc.Id)) { skipped++; continue; }
                if (string.IsNullOrEmpty(inc.Id))
                {
                    inc.Id = NewAnnouncementId();
                    known.Add(inc.Id);
                }

                if (inc.LikedPlayers == null) inc.LikedPlayers = new HashSet<ulong>();
                if (inc.ReadByPlayers == null) inc.ReadByPlayers = new HashSet<ulong>();
                if (inc.ReadRewardedPlayers == null) inc.ReadRewardedPlayers = new HashSet<ulong>();
                if (inc.LikeRewardedPlayers == null) inc.LikeRewardedPlayers = new HashSet<ulong>();
                if (inc.Audience == null) inc.Audience = string.Empty;
                if (!IsValidAudience(inc.Audience)) inc.Audience = string.Empty;
                if (inc.Timestamp <= 0) inc.Timestamp = DateTime.UtcNow.Ticks;
                inc.Text = NormalizeBodyText(inc.Text);
                inc.Title = Truncate(inc.Title.Trim(), MaxTitleChars);

                // Imported posts never re-broadcast to players.
                inc.Broadcast = true;

                EnsurePermissionRegistered(inc.Audience);
                announcements.Add(inc);
                added++;
            }

            announcements.Sort(CompareForDisplay);
            SaveAnnouncements();
            RegisterAllImages();

            SendReply(arg, Msg("ImportDone", null, added, skipped));
        }



        [ConsoleCommand("news.page")]
        private void CmdConsolePage(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasView(player))
            {
                SendReply(player, Msg("NoPermissionView", player));
                return;
            }

            ShowHistory(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.view")]
        private void CmdConsoleView(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasView(player))
            {
                SendReply(player, Msg("NoPermissionView", player));
                return;
            }

            var ann = FindById(arg.GetString(0));
            if (ann == null) return;

            // Stops players from opening drafts, scheduled or restricted posts
            // by guessing an id in the F1 console.
            if (!CanSee(player, ann, DateTime.UtcNow.Ticks)) return;

            ShowPopup(player, ann, true);
        }

        [ConsoleCommand("news.close")]
        private void CmdConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player != null) DestroyUI(player);
        }

        [ConsoleCommand("news.close.notif")]
        private void CmdCloseNotif(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player != null) DestroyNotification(player);
        }

        [ConsoleCommand("news.admin")]
        private void CmdNewsAdmin(ConsoleSystem.Arg arg)
        {
            if (!HasAdmin(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            var player = PlayerFrom(arg);
            if (player == null) return;

            ShowAdminList(player, 0);
        }

        [ConsoleCommand("news.admin.page")]
        private void CmdNewsAdminPage(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }
            ShowAdminList(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.create")]
        private void CmdNewsAdminCreate(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

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
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

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
                PublishAt = original.PublishAt,
                ExpiresAt = original.ExpiresAt,
                Draft = original.Draft,
                Broadcast = original.Broadcast,
                Audience = original.Audience ?? string.Empty,
                LikedPlayers = new HashSet<ulong>(original.LikedPlayers)
            };
            activeEditorIds[player.userID] = original.Id;
            ShowEditor(player);
        }

        [ConsoleCommand("news.admin.togglepin")]
        private void CmdNewsAdminTogglePin(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

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
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

            string id = arg.GetString(0);
            if (string.IsNullOrEmpty(id) || FindById(id) == null) return;

            var sel = GetAdminSelection(player.userID);
            if (!sel.Remove(id)) sel.Add(id);

            ShowAdminList(player, arg.GetInt(1, 0));
        }

        [ConsoleCommand("news.admin.selectpage")]
        private void CmdNewsAdminSelectPage(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

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
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

            adminSelections.Remove(player.userID);
            ShowAdminList(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.bulkpin")]
        private void CmdNewsAdminBulkPin(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

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
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

            PruneAdminSelection(player.userID);
            ShowBulkDeleteConfirm(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.bulkdel")]
        private void CmdNewsAdminBulkDel(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

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
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

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
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;
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
                case "title": ann.Title = Truncate(value.Trim(), MaxTitleChars); break;
                case "text": ann.Text = NormalizeBodyText(value); break;
                case "image": ann.ImageUrl = Truncate(value.Trim(), MaxUrlChars); break;

                case "publish":
                {
                    long ticks;
                    if (!TryParseWhen(value, out ticks)) SendReply(player, Msg("InvalidDate", player, value));
                    else ann.PublishAt = ticks;
                    break;
                }

                case "expires":
                {
                    long ticks;
                    if (!TryParseWhen(value, out ticks)) SendReply(player, Msg("InvalidDate", player, value));
                    else ann.ExpiresAt = ticks;
                    break;
                }

                case "audience":
                {
                    string suffix = (value ?? string.Empty).Trim().ToLowerInvariant();
                    if (suffix == "-") suffix = string.Empty;

                    if (!IsValidAudience(suffix)) SendReply(player, Msg("InvalidAudience", player, value));
                    else
                    {
                        ann.Audience = suffix;
                        EnsurePermissionRegistered(suffix);
                    }
                    break;
                }
            }

            ShowEditor(player);
        }

        [ConsoleCommand("news.editor.draft")]
        private void CmdEditorDraft(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;
            if (!activeEditors.ContainsKey(player.userID)) return;

            activeEditors[player.userID].Draft = !activeEditors[player.userID].Draft;
            ShowEditor(player);
        }


        [ConsoleCommand("news.editor.type")]
        private void CmdEditorType(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;
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
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;
            if (!activeEditors.ContainsKey(player.userID)) return;

            var ann = activeEditors[player.userID];
            ann.Text = NormalizeBodyText(ann.Text);
            ann.Title = Truncate(ann.Title?.Trim(), MaxTitleChars);
            ann.Audience = (ann.Audience ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(ann.Title))
            {
                SendReply(player, Msg("TitleRequired", player));
                return;
            }

            if (!IsValidAudience(ann.Audience))
            {
                SendReply(player, Msg("InvalidAudience", player, ann.Audience));
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            long effectivePublish = ann.PublishAt > 0 ? ann.PublishAt : now;
            if (ann.ExpiresAt > 0 && ann.ExpiresAt <= effectivePublish)
            {
                SendReply(player, Msg("ExpiryBeforePublish", player));
                return;
            }

            // Live right now = not a draft, and either unscheduled or already due.
            bool liveNow = !ann.Draft && (ann.PublishAt <= 0 || ann.PublishAt <= now);

            activeEditorIds.TryGetValue(player.userID, out string editingId);
            bool isNew = string.IsNullOrEmpty(editingId);

            Announcement target;
            if (isNew)
            {
                ann.Id = NewAnnouncementId();
                ann.Timestamp = now;
                ann.Date = DateTime.Now.ToString(InvariantDateFormat, CultureInfo.InvariantCulture);
                announcements.Insert(0, ann);
                target = ann;
            }
            else
            {
                target = FindById(editingId);
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
                target.PublishAt = ann.PublishAt;
                target.ExpiresAt = ann.ExpiresAt;
                target.Draft = ann.Draft;
                target.Audience = ann.Audience;
            }

            // Announce once, the first time it actually becomes live. Ordinary
            // edits to an already-published post never re-broadcast; moving one
            // back to draft or into the future re-arms it for the scheduler.
            bool announce = liveNow && !target.Broadcast;
            target.Broadcast = liveNow;

            EnsurePermissionRegistered(target.Audience);
            SaveAnnouncements();

            if (!string.IsNullOrEmpty(target.ImageUrl) && ImageLibrary != null)
                ImageLibrary.Call("AddImage", target.ImageUrl, target.ImageUrl, 0UL);

            if (announce)
            {
                Interface.CallHook("OnNewsBroadcast", BuildHookData(target));
                SendToDiscord(target);
                PublishToPlayers(target);
            }
            else if (!isNew)
            {
                Interface.CallHook("OnNewsEdited", BuildHookData(target));
            }

            activeEditors.Remove(player.userID);
            activeEditorIds.Remove(player.userID);

            ShowAdminList(player, 0);

            if (target.Draft) SendReply(player, Msg("SavedDraft", player));
            else if (!liveNow) SendReply(player, Msg("SavedScheduled", player, FormatWhen(target.PublishAt)));
            else SendReply(player, isNew ? Msg("AnnouncementSavedNew", player) : Msg("AnnouncementUpdated", player));
        }

        [ConsoleCommand("news.editor.cancel")]
        private void CmdEditorCancel(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

            activeEditors.Remove(player.userID);
            activeEditorIds.Remove(player.userID);
            ShowAdminList(player, 0);
        }
        #endregion

        #region Notification UI
        [ConsoleCommand("news.like")]
        private void CmdNewsLike(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasView(player)) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null) return;
            if (!CanSee(player, ann, DateTime.UtcNow.Ticks)) return;

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

            MarkDataDirty();
            Interface.CallHook("OnNewsLiked", player, BuildHookData(ann), addedLike);
            ShowPopup(player, ann, true, false);
        }

        // Effect.server.Run(prefab, position) is a world effect: every player in
        // earshot hears it, so a group in one base heard the chime once per member.
        // Passing the target connection with broadcast:false keeps it private.
        private void PlayNotificationSound(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;

            string prefab = config.Notification.NotificationSound;
            if (string.IsNullOrEmpty(prefab)) return;

            Effect.server.Run(prefab, player.transform.position, Vector3.zero, player.net?.connection, false);
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

            PlayNotificationSound(player);

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
                RectTransform = { AnchorMin = A(contentLeft, 0.80f), AnchorMax = "0.98 0.90" }
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
                 RectTransform = { AnchorMin = A(contentLeft, 0.79f), AnchorMax = A(contentLeft + 0.15f, 0.795f) }
            }, mainPanel);

            string bodyScroll = mainPanel + ".Body";

            // The body column is far wider without an image, so a single wrap
            // width mis-sized the scroll content and could strand the tail of a
            // long post out of reach. 300 is the panel height in CUI reference
            // units; the +40 is slack for font metric differences.
            int wrapChars = hasImage ? 52 : 104;
            int estLines = BuildBodyDisplayLines(ann.Text, wrapChars).Count;
            int extra = Mathf.Max(0, estLines * 20 + 40 - 300);

            container.Add(new CuiElement
            {
                Name = bodyScroll,
                Parent = mainPanel,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.18" },
                    new CuiRectTransformComponent { AnchorMin = A(contentLeft, 0.12f), AnchorMax = "0.965 0.76" },
                    new CuiScrollViewComponent
                    {
                        Horizontal = false,
                        Vertical = true,
                        MovementType = UnityEngine.UI.ScrollRect.MovementType.Clamped,
                        Inertia = true,
                        DecelerationRate = 0.1f,
                        ScrollSensitivity = 26f,
                        ContentTransform = new CuiRectTransform { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = A(0f, -extra), OffsetMax = "0 0" },
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
                Text = { Text = $"{Msg("PostedBy", player)} <color={RgbaToHex(c.ButtonPrimary)}>{(ann.Author ?? Msg("Unknown", player)).ToUpper()}</color>  •  {DisplayDate(ann)}", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
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

            if (playSound) PlayNotificationSound(player);

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

        // Narrows an already permission-filtered list by the player's archive filter.
        private List<Announcement> ApplyFilter(BasePlayer player, List<Announcement> source, ArchiveFilter filter)
        {
            if (filter == null || filter.IsDefault) return source;

            bool hasSearch = !string.IsNullOrEmpty(filter.Search);
            var result = new List<Announcement>();

            for (int i = 0; i < source.Count; i++)
            {
                var a = source[i];
                if (filter.Type.HasValue && a.Type != filter.Type.Value) continue;
                if (filter.UnreadOnly && (a.ReadByPlayers?.Contains(player.userID) ?? false)) continue;

                if (hasSearch)
                {
                    bool hit = (!string.IsNullOrEmpty(a.Title) && a.Title.IndexOf(filter.Search, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (!string.IsNullOrEmpty(a.Text) && a.Text.IndexOf(filter.Search, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!hit) continue;
                }

                result.Add(a);
            }

            return result;
        }

        private void AddFilterChip(CuiElementContainer container, string parent, UIColors c, string label,
                                   string command, bool active, float xMin, float xMax, float yMin, float yMax)
        {
            container.Add(new CuiButton
            {
                Button = { Color = active ? c.ButtonPrimary : c.ButtonSecondary, Command = command },
                Text = { Text = label, FontSize = 9, Align = TextAnchor.MiddleCenter, Color = active ? "1 1 1 1" : c.TextMuted, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = A(xMin, yMin), AnchorMax = A(xMax, yMax) }
            }, parent);
        }

        private void ShowHistory(BasePlayer player, int page)
        {
            DestroyUI(player);

            var allVisible = GetVisibleFor(player);
            if (allVisible.Count == 0)
            {
                SendReply(player, Msg("NoNewsHistory", player));
                return;
            }

            playersWithUiOpen.Add(player.userID);

            var container = new CuiElementContainer();
            var c = config.Colors;

            var filter = GetArchiveFilter(player.userID);
            var displayList = ApplyFilter(player, allVisible, filter);

            int perPage = config.General.AnnouncementsPerPage;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)displayList.Count / perPage));
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

            string countLabel = displayList.Count == allVisible.Count
                ? allVisible.Count.ToString()
                : Msg("ShowingCount", player, displayList.Count, allVisible.Count);

            container.Add(new CuiLabel {
                Text = { Text = $"{config.General.ServerName} <color={RgbaToHex(c.ButtonPrimary)}>//</color> {Msg("ArchiveTitle", player)} <color={RgbaToHex(c.ButtonPrimary)}>({countLabel})</color>", FontSize = 17, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.03 0.925", AnchorMax = "0.9 1" }
            }, mainPanel);

            container.Add(new CuiButton {
                Button = { Color = "0 0 0 0", Command = "news.close" },
                Text = { Text = "✕", FontSize = 17, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.95 0.925", AnchorMax = "0.99 1" }
            }, mainPanel);

            container.Add(new CuiPanel { Image = { Color = c.HeaderBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.075" } }, mainPanel);
            container.Add(new CuiPanel { Image = { Color = "1 1 1 0.06", FadeIn = 0.20f }, RectTransform = { AnchorMin = "0 0.075", AnchorMax = "1 0.077" } }, mainPanel);

            // ----- Filter bar -----
            const float fbBottom = 0.855f, fbTop = 0.905f;
            var types = (AnnouncementType[])Enum.GetValues(typeof(AnnouncementType));

            float chipLeft = 0.025f, chipWidth = 0.0855f, chipGap = 0.004f;
            AddFilterChip(container, mainPanel, c, Msg("FilterAll", player), "news.filter.type all",
                !filter.Type.HasValue, chipLeft, chipLeft + chipWidth, fbBottom, fbTop);

            for (int t = 0; t < types.Length; t++)
            {
                float x = chipLeft + (t + 1) * (chipWidth + chipGap);
                AddFilterChip(container, mainPanel, c, types[t].ToString().ToUpper(), $"news.filter.type {types[t]}",
                    filter.Type.HasValue && filter.Type.Value == types[t], x, x + chipWidth, fbBottom, fbTop);
            }

            AddFilterChip(container, mainPanel, c, Msg("FilterUnread", player), "news.filter.unread",
                filter.UnreadOnly, 0.575f, 0.665f, fbBottom, fbTop);

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.45" },
                RectTransform = { AnchorMin = A(0.675f, fbBottom), AnchorMax = A(0.895f, fbTop) }
            }, mainPanel);
            container.Add(new CuiElement
            {
                Parent = mainPanel,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = filter.Search ?? string.Empty,
                        FontSize = 10,
                        Align = TextAnchor.MiddleLeft,
                        Command = "news.filter.search",
                        Color = "1 1 1 1",
                        NeedsKeyboard = true,
                        CharsLimit = 64
                    },
                    new CuiRectTransformComponent { AnchorMin = A(0.683f, fbBottom), AnchorMax = A(0.89f, fbTop) }
                }
            });
            if (string.IsNullOrEmpty(filter.Search))
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("SearchPlaceholder", player), FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = A(0.686f, fbBottom), AnchorMax = A(0.89f, fbTop) }
                }, mainPanel);
            }

            AddFilterChip(container, mainPanel, c, Msg("ClearFilters", player), "news.filter.clear",
                false, 0.905f, 0.975f, fbBottom, fbTop);

            // ----- Rows -----
            int start = page * perPage;
            int count = 0;
            float listTop = 0.845f;
            float rowHeight = 0.755f / perPage;
            float padding = 0.012f;

            if (displayList.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("NoMatches", player), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.05 0.3", AnchorMax = "0.95 0.7" }
                }, mainPanel);
            }

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
                    RectTransform = { AnchorMin = A(0.025f, bottom), AnchorMax = A(0.975f, top) }
                }, mainPanel, itemPanel);

                if (unread)
                {
                    // Soft accent wash across the whole card
                    container.Add(new CuiPanel
                    {
                        Image = { Color = WithAlpha(c.ButtonPrimary, 0.12f) },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }, itemPanel);

                    // Accent border around the card (top / bottom / right; left handled by stripe below)
                    container.Add(new CuiPanel { Image = { Color = c.ButtonPrimary }, RectTransform = { AnchorMin = "0 0.97",  AnchorMax = "1 1"      } }, itemPanel);
                    container.Add(new CuiPanel { Image = { Color = c.ButtonPrimary }, RectTransform = { AnchorMin = "0 0",     AnchorMax = "1 0.03"   } }, itemPanel);
                    container.Add(new CuiPanel { Image = { Color = c.ButtonPrimary }, RectTransform = { AnchorMin = "0.995 0", AnchorMax = "1 1"      } }, itemPanel);
                }

                if (ann.Pinned)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.95 0.70 0.20 0.10" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }, itemPanel);
                }

                // Type stripe on the very left, plus a brighter accent stripe next to it when unread
                container.Add(new CuiPanel {
                    Image = { Color = typeColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.008 1" }
                }, itemPanel);
                if (unread)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = c.ButtonPrimary },
                        RectTransform = { AnchorMin = "0.008 0", AnchorMax = "0.020 1" }
                    }, itemPanel);
                }

                if (unread)
                {
                    // Bright UNREAD pill sitting just before the title
                    container.Add(new CuiPanel
                    {
                        Image = { Color = c.ButtonPrimary },
                        RectTransform = { AnchorMin = "0.03 0.60", AnchorMax = "0.145 0.87" }
                    }, itemPanel);
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"<b>● {Msg("UnreadBadge", player)}</b>", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                        RectTransform = { AnchorMin = "0.03 0.60", AnchorMax = "0.145 0.87" }
                    }, itemPanel);
                }

                container.Add(new CuiLabel
                {
                    Text = { Text = (ann.Title ?? "(no title)").ToUpper(), FontSize = 15, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = unread ? "0.16 0.55" : "0.03 0.55", AnchorMax = "0.585 0.92" }
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
                    Text = { Text = DisplayDate(ann), FontSize = 11, Align = TextAnchor.MiddleRight, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.74 0.54", AnchorMax = "0.87 0.92" }
                }, itemPanel);

                string preview = Truncate((ann.Text ?? "").Replace("\n", " "), 90);
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

            // ----- Footer -----
            container.Add(new CuiLabel
            {
                Text = { Text = Msg("Page", player, page + 1, totalPages), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0.42 0", AnchorMax = "0.58 0.075" }
            }, mainPanel);

            if (CountUnread(player) > 0)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonSecondary, Command = "news.markread" },
                    Text = { Text = Msg("MarkAllRead", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.60 0.012", AnchorMax = "0.78 0.063" }
                }, mainPanel);
            }

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

        private static string GetStatusColor(AnnouncementStatus status)
        {
            switch (status)
            {
                case AnnouncementStatus.Draft: return "0.75 0.55 0.15 0.95";
                case AnnouncementStatus.Scheduled: return "0.25 0.52 0.80 0.95";
                case AnnouncementStatus.Expired: return "0.45 0.45 0.50 0.95";
                default: return "0.30 0.78 0.45 0.95";
            }
        }

        private string GetStatusLabel(AnnouncementStatus status, BasePlayer player)
        {
            switch (status)
            {
                case AnnouncementStatus.Draft: return Msg("StatusDraft", player);
                case AnnouncementStatus.Scheduled: return Msg("StatusScheduled", player);
                case AnnouncementStatus.Expired: return Msg("StatusExpired", player);
                default: return Msg("StatusLive", player);
            }
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
                container.Add(new CuiPanel { Image = { Color = "1 1 1 0.05", FadeIn = 0.20f }, RectTransform = { AnchorMin = A(0f, bottomY), AnchorMax = A(1f, topY) } }, parent);
                container.Add(new CuiPanel { Image = { Color = c.ButtonPrimary, FadeIn = 0.20f }, RectTransform = { AnchorMin = A(0f, bottomY), AnchorMax = A(0.02f, topY) } }, parent);
            }

            container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = command },
                Text = { Text = label, FontSize = 14, Align = TextAnchor.MiddleLeft, Color = active ? c.TextTitle : c.TextMuted, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = A(0.1f, bottomY), AnchorMax = A(0.95f, topY) }
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

            container.Add(new CuiPanel { Image = { Color = c.ContentBg, FadeIn = 0.20f }, RectTransform = { AnchorMin = A(xMin, 0.85f), AnchorMax = A(xMax, 0.90f) } }, parent);
            container.Add(new CuiLabel { Text = { Text = value, FontSize = 18, Align = TextAnchor.MiddleCenter, Color = c.ButtonPrimary, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f }, RectTransform = { AnchorMin = A(xMin, 0.871f), AnchorMax = A(xMax, 0.899f) } }, parent);
            container.Add(new CuiLabel { Text = { Text = label, FontSize = 9, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f }, RectTransform = { AnchorMin = A(xMin, 0.852f), AnchorMax = A(xMax, 0.872f) } }, parent);
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
            long nowTicks = DateTime.UtcNow.Ticks;
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
                var status = StatusOf(ann, nowTicks);
                bool showStatus = status != AnnouncementStatus.Live;

                float adminRowFade = 0.18f + count * 0.04f;
                container.Add(new CuiPanel
                {
                    Image = { Color = c.ContentBg, FadeIn = adminRowFade },
                    RectTransform = { AnchorMin = A(0.285f, bottom), AnchorMax = A(0.975f, top) }
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
                    Text = { Text = (ann.Title ?? "(no title)").ToUpper(), FontSize = 12, Align = TextAnchor.MiddleLeft, Color = showStatus ? c.TextMuted : c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.06 0.52", AnchorMax = showStatus ? "0.375 0.9" : "0.46 0.9" }
                }, itemPanel);

                if (showStatus)
                {
                    container.Add(new CuiPanel { Image = { Color = GetStatusColor(status) }, RectTransform = { AnchorMin = "0.38 0.55", AnchorMax = "0.465 0.87" } }, itemPanel);
                    container.Add(new CuiLabel
                    {
                        Text = { Text = GetStatusLabel(status, player), FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                        RectTransform = { AnchorMin = "0.38 0.55", AnchorMax = "0.465 0.87" }
                    }, itemPanel);
                }

                string adminMeta = string.IsNullOrEmpty(ann.Author)
                    ? DisplayDate(ann)
                    : Msg("ByAuthorDate", player, ann.Author, DisplayDate(ann));

                // Append whatever schedule window is set so admins can see it at a glance.
                if (ann.PublishAt > 0) adminMeta += $"  ▶ {FormatWhen(ann.PublishAt)}";
                if (ann.ExpiresAt > 0) adminMeta += $"  ⏳ {FormatWhen(ann.ExpiresAt)}";
                if (!string.IsNullOrEmpty(ann.Audience)) adminMeta += $"  🔒 {ann.Audience}";
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
                RectTransform = { AnchorMin = A(0.285f, agBottom), AnchorMax = A(0.40f, agTop) }
            }, mainPanel);

            if (selection.Count > 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("SelectedCount", player, selection.Count), FontSize = 11, Align = TextAnchor.MiddleLeft, Color = c.ButtonPrimary, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = A(0.41f, agBottom), AnchorMax = A(0.52f, agTop) }
                }, mainPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonPrimary, Command = $"news.admin.bulkpin 1 {page}" },
                    Text = { Text = Msg("BulkPin", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = A(0.525f, agBottom), AnchorMax = A(0.64f, agTop) }
                }, mainPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.35 0.35 0.4 0.9", Command = $"news.admin.bulkpin 0 {page}" },
                    Text = { Text = Msg("BulkUnpin", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = A(0.645f, agBottom), AnchorMax = A(0.76f, agTop) }
                }, mainPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = "0.65 0.12 0.12 1", Command = $"news.admin.bulkdelconfirm {page}" },
                    Text = { Text = Msg("BulkDelete", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = A(0.765f, agBottom), AnchorMax = A(0.86f, agTop) }
                }, mainPanel);

                container.Add(new CuiButton
                {
                    Button = { Color = c.ButtonSecondary, Command = $"news.admin.clearsel {page}" },
                    Text = { Text = Msg("ClearSelection", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = A(0.865f, agBottom), AnchorMax = A(0.975f, agTop) }
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

        // One labelled text input in the editor column.
        private void AddEditorField(CuiElementContainer container, string parent, UIColors c, string label,
                                    string command, string value, int charsLimit,
                                    float xMin, float xMax, float labelBottom, float fieldBottom, float fieldTop)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = A(xMin, labelBottom), AnchorMax = A(xMax, labelBottom + 0.03f) }
            }, parent);

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.45", FadeIn = 0.20f },
                RectTransform = { AnchorMin = A(xMin, fieldBottom), AnchorMax = A(xMax, fieldTop) }
            }, parent);

            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = value ?? string.Empty,
                        FontSize = 12,
                        Align = TextAnchor.MiddleLeft,
                        Command = command,
                        Color = "1 1 1 1",
                        NeedsKeyboard = true,
                        CharsLimit = charsLimit
                    },
                    new CuiRectTransformComponent { AnchorMin = A(xMin + 0.012f, fieldBottom), AnchorMax = A(xMax - 0.008f, fieldTop) }
                }
            });
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

            const float colLeft = 0.30f, colRight = 0.96f;

            // ----- Title -----
            AddEditorField(container, mainPanel, c, Msg("AnnouncementTitle", player), "news.editor.input title",
                ann.Title, MaxTitleChars, colLeft, colRight, 0.855f, 0.80f, 0.85f);

            // ----- Image URL -----
            AddEditorField(container, mainPanel, c, Msg("ImageUrl", player), "news.editor.input image",
                ann.ImageUrl, MaxUrlChars, colLeft, colRight, 0.755f, 0.70f, 0.75f);

            // ----- Row: type | draft toggle | audience -----
            container.Add(new CuiLabel
            {
                Text = { Text = Msg("AnnouncementType", player), FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = A(colLeft, 0.655f), AnchorMax = A(0.48f, 0.685f) }
            }, mainPanel);
            container.Add(new CuiButton
            {
                Button = { Color = GetTypeColor(ann.Type), Command = "news.editor.type" },
                Text = { Text = $"◀  {ann.Type.ToString().ToUpper()}  ▶", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = A(colLeft, 0.60f), AnchorMax = A(0.48f, 0.65f) }
            }, mainPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("DraftLabel", player), FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = A(0.49f, 0.655f), AnchorMax = A(0.62f, 0.685f) }
            }, mainPanel);
            container.Add(new CuiButton
            {
                Button = { Color = ann.Draft ? "0.75 0.55 0.15 0.95" : "0.30 0.78 0.45 0.95", Command = "news.editor.draft" },
                Text = { Text = ann.Draft ? Msg("DraftOn", player) : Msg("DraftOff", player), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = A(0.49f, 0.60f), AnchorMax = A(0.62f, 0.65f) }
            }, mainPanel);

            AddEditorField(container, mainPanel, c, Msg("AudienceLabel", player), "news.editor.input audience",
                ann.Audience, 32, 0.63f, colRight, 0.655f, 0.60f, 0.65f);

            // ----- Row: publish at | expires at -----
            AddEditorField(container, mainPanel, c, Msg("PublishAtLabel", player), "news.editor.input publish",
                FormatWhen(ann.PublishAt), 32, colLeft, 0.62f, 0.545f, 0.49f, 0.54f);
            AddEditorField(container, mainPanel, c, Msg("ExpiresAtLabel", player), "news.editor.input expires",
                FormatWhen(ann.ExpiresAt), 32, 0.63f, colRight, 0.545f, 0.49f, 0.54f);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{Msg("ScheduleHint", player)}   {Msg("AudienceHint", player)}", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = A(colLeft, 0.45f), AnchorMax = A(colRight, 0.48f) }
            }, mainPanel);

            // ----- Body -----
            container.Add(new CuiLabel
            {
                Text = { Text = Msg("ContentBody", player), FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = A(colLeft, 0.405f), AnchorMax = A(0.55f, 0.435f) }
            }, mainPanel);
            container.Add(new CuiLabel
            {
                Text = { Text = Msg("ContentBodyHint", player), FontSize = 9, Align = TextAnchor.LowerRight, Color = c.TextMuted, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = A(0.55f, 0.405f), AnchorMax = A(colRight, 0.435f) }
            }, mainPanel);
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.45", FadeIn = 0.20f },
                RectTransform = { AnchorMin = A(colLeft, 0.115f), AnchorMax = A(colRight, 0.395f) }
            }, mainPanel);
            container.Add(new CuiElement
            {
                Parent = mainPanel,
                Components =
                {
                    new CuiInputFieldComponent { Text = ann.Text ?? "", FontSize = 13, Align = TextAnchor.UpperLeft, Command = "news.editor.input text", Color = "1 1 1 1", NeedsKeyboard = true, CharsLimit = MaxContentChars, LineType = UnityEngine.UI.InputField.LineType.MultiLineNewline },
                    new CuiRectTransformComponent { AnchorMin = A(colLeft + 0.012f, 0.125f), AnchorMax = A(colRight - 0.008f, 0.385f) }
                }
            });

            // ----- Footer buttons -----
            container.Add(new CuiButton
            {
                Button = { Color = c.ButtonSecondary, Command = "news.editor.cancel", FadeIn = 0.20f },
                Text = { Text = Msg("Cancel", player), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = c.TextNormal, Font = "robotocondensed-bold.ttf", FadeIn = 0.20f },
                RectTransform = { AnchorMin = A(colLeft, 0.04f), AnchorMax = A(0.61f, 0.105f) }
            }, mainPanel);
            container.Add(new CuiButton
            {
                Button = { Color = "0.30 0.78 0.45 0.95", Command = "news.editor.save", FadeIn = 0.20f },
                Text = { Text = Msg("SaveBroadcast", player), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf", FadeIn = 0.20f },
                RectTransform = { AnchorMin = A(0.63f, 0.04f), AnchorMax = A(colRight, 0.105f) }
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
            var player = PlayerFrom(arg);
            if (player == null) return;
            if (!HasAdmin(player)) return;

            ShowDeleteConfirm(player, arg.GetString(0));
        }

        [ConsoleCommand("news.confirm.close")]
        private void CmdConfirmClose(ConsoleSystem.Arg arg)
        {
            var player = PlayerFrom(arg);
            if (player != null) CuiHelper.DestroyUi(player, ConfirmLayer);
        }
        #endregion

        #region Theme UI
        [ConsoleCommand("news.admin.themes")]
        private void CmdNewsAdminThemes(ConsoleSystem.Arg arg)
        {
            if (!HasAdmin(arg)) return;
            var player = PlayerFrom(arg);
            if (player == null) return;
            ShowThemeSelection(player);
        }

        [ConsoleCommand("news.admin.settheme")]
        private void CmdNewsSetTheme(ConsoleSystem.Arg arg)
        {
            if (!HasAdmin(arg)) return;
            var player = PlayerFrom(arg);
            if (player == null) return;

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(player, Msg("UsageSetTheme", player));
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
                    SendReply(player, Msg("ThemeSet", player, matchedTheme));
                });
            }
            else
            {
                SendReply(player, Msg("ThemeNotFound", player, themeName));
                SendReply(player, Msg("ThemeAvailable", player, string.Join(", ", config.Themes.Keys)));
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
                    RectTransform = { AnchorMin = A(xMin, bottom), AnchorMax = A(xMax, top) }
                }, mainPanel);

                string card = mainPanel + $".theme{idx}";
                container.Add(new CuiPanel
                {
                    Image = { Color = tc.PanelBg, FadeIn = fade },
                    RectTransform = { AnchorMin = A(xMin + 0.004f, bottom + 0.007f), AnchorMax = A(xMax - 0.004f, top - 0.007f) }
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
                        RectTransform = { AnchorMin = A(sMin, 0.13f), AnchorMax = A(sMin + 0.20f, 0.40f) }
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

                if (firstRead || grantReward) MarkDataDirty();
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

            object payload = BuildDiscordEmbed(ann);
            string json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            webrequest.Enqueue(config.Discord.WebhookUrl, json, (code, response) =>
            {
                if (code < 200 || code > 299)
                    PrintError($"Discord Webhook failed! Code: {code} - Response: {response}");
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

        private string DiscordTypeEmoji(AnnouncementType type)
        {
            switch (type)
            {
                case AnnouncementType.Alert: return "🚨";
                case AnnouncementType.Warning: return "⚠️";
                case AnnouncementType.Update: return "🛠️";
                case AnnouncementType.Event: return "🎉";
                default: return "📰";
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
            string typeName = ann.Type.ToString().ToUpper();
            string typeEmoji = DiscordTypeEmoji(ann.Type);
            int likes = ann.LikedPlayers?.Count ?? 0;
            int reads = ann.ReadByPlayers?.Count ?? 0;

            var fields = new List<object>();
            fields.Add(new { name = "Type", value = $"{typeEmoji} {typeName}", inline = true });
            if (ann.Pinned)
                fields.Add(new { name = "Status", value = "📌 Pinned", inline = true });
            if (likes > 0 || reads > 0)
                fields.Add(new { name = "Engagement", value = $"❤ {likes}   👁 {reads}", inline = true });

            string isoTimestamp = null;
            if (ann.Timestamp > 0)
            {
                try { isoTimestamp = new DateTime(ann.Timestamp, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture); }
                catch { isoTimestamp = null; }
            }

            return new
            {
                username = config.Discord.BotName,
                content = content,
                allowed_mentions = new { parse = new[] { "roles" } },
                embeds = new[]
                {
                    new
                    {
                        author = string.IsNullOrEmpty(ann.Author) ? null : new { name = $"Posted by {ann.Author}" },
                        title = Truncate($"{typeEmoji} {ann.Title}", DiscordEmbedTitleLimit),
                        description = BuildDiscordBody(ann),
                        color = DiscordEmbedColor(ann),
                        fields = fields.Count > 0 ? fields.ToArray() : null,
                        image = string.IsNullOrEmpty(ann.ImageUrl) ? null : new { url = ann.ImageUrl },
                        footer = new { text = config.General.ServerName },
                        timestamp = isoTimestamp
                    }
                }
            };
        }
        #endregion
    }
}
 