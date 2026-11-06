using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
    [Info("NewsBroadcaster", "DEDA", "2.0.0")]
    [Description("In-game news announcements with notifications, archive, polls, likes, rewards and Discord webhooks")]
    public class NewsBroadcaster : RustPlugin
    {
        #region Fields & Constants
        [PluginReference] private Plugin ImageLibrary, Notify, ServerRewards, Economics;

        private const string LayerName = "NewsBroadcasterUI";
        private const string NotificationLayer = "NewsNotificationUI";
        private const string ConfirmLayer = "NewsConfirmUI";
        private const string PopupPanel = LayerName + ".Main";
        private const string LikeButtonName = LayerName + ".Like";
        private const string PollPanelName = LayerName + ".Poll";
        private const string AutoCloseName = LayerName + ".AutoClose";
        private const string BlurMaterial = "assets/content/ui/uibackgroundblur-ingamemenu.mat";
        private const string DataFile = "NewsBroadcaster_Data";

        private const string PermAdmin = "newsbroadcaster.admin";
        private const string PermView = "newsbroadcaster.view";

        private static readonly Regex CommandSplitRegex = new Regex(@"[\""].+?[\""]|[^ ]+", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"(https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "/n" is accepted as a line break unless it is the start of a word ("w/new", "and/no" stay intact).
        private static readonly Regex SlashNewlineRegex = new Regex(@"/n(?![a-z])", RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new Regex(@"[ \t]{2,}", RegexOptions.Compiled);
        private static readonly Regex BlankLinesRegex = new Regex(@"\n{3,}", RegexOptions.Compiled);

        private ConfigData config;
        private StoredData storedData;
        private List<Announcement> announcements = new List<Announcement>();

        // Per-player state. Everything keyed by SteamID is cleaned up in OnPlayerDisconnected and Unload.
        private readonly Dictionary<ulong, Timer> autoCloseTimers = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, Timer> notificationTimers = new Dictionary<ulong, Timer>();
        private readonly HashSet<ulong> playersWithUiOpen = new HashSet<ulong>();
        private readonly Dictionary<ulong, EditorSession> editors = new Dictionary<ulong, EditorSession>();
        private readonly Dictionary<ulong, ReadRewardState> readRewardTimers = new Dictionary<ulong, ReadRewardState>();
        private readonly Dictionary<ulong, HashSet<string>> adminSelections = new Dictionary<ulong, HashSet<string>>();
        private readonly Dictionary<ulong, float> likeCooldowns = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> voteCooldowns = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, PopupState> openPopups = new Dictionary<ulong, PopupState>();
        private readonly Dictionary<ulong, int> archivePages = new Dictionary<ulong, int>();
        private readonly HashSet<ulong> remindedPlayers = new HashSet<ulong>();
        private readonly Dictionary<ulong, UiScreen> currentScreens = new Dictionary<ulong, UiScreen>();
        private readonly Dictionary<ulong, string> toastNames = new Dictionary<ulong, string>();
        private int toastCounter;

        // Rounded-corner textures (TL, TR, BL, BR) generated at startup and kept in the server FileStorage.
        private string[] cornerFill;
        private string[] cornerMask;

        // Likes, read marks and "last seen" are saved with a short delay so a busy
        // server does not rewrite the whole data file on every click.
        private bool dataDirty;
        private Timer saveTimer;

        private static readonly string InvariantDateFormat = "yyyy-MM-dd HH:mm";
        private const int MaxContentChars = 32768;
        private const int MaxTitleChars = 120;
        private const int MaxImageUrlChars = 2048;
        private const int DiscordEmbedDescriptionLimit = 4000;
        private const float LikeCooldownSeconds = 1.25f;
        private const float VoteCooldownSeconds = 0.75f;
        private const int MaxPollOptions = 4;
        private const int MaxPollOptionChars = 60;
        private const float SaveDebounceSeconds = 15f;
        private const int ThemesPerPage = 8;

        // Popup body sizing. CUI units are based on a 1280x720 reference canvas.
        private const int BodyWrapWithImage = 52;
        private const int BodyWrapFullWidth = 104;
        private const float BodyLineUnits = 18f;
        private const float PopupWidthUnits = 832f;   // 0.65 of 1280
        private const float PopupHeightUnits = 468f;  // 0.65 of 720
        private const float EditorWidthUnits = 742.4f;  // 0.58 of 1280
        private const float EditorHeightUnits = 532.8f; // 0.74 of 720

        private const string AdminBackdrop = "0.015 0.018 0.022 0.74";
        private const string AdminSurface = "0.055 0.062 0.073 0.99";
        private const string AdminSurfaceRaised = "0.082 0.090 0.104 0.98";
        private const string AdminDanger = "0.82 0.24 0.27 0.95";
        private const string PinGold = "0.95 0.70 0.20 0.95";
        private const string PinGoldText = "0.05 0.05 0.05 1";
        // Modern look: flat surfaces with 1-unit outlines instead of heavy filled bars.
        private const string Hairline = "1 1 1 0.07";
        private const string Ghost = "1 1 1 0.055";
        private const string PanelRim = "1 1 1 0.09";
        private const float RadiusPanel = 12f;
        private const float RadiusCard = 8f;
        private const float RadiusButton = 6f;
        private const float RadiusPill = 4f;
        private const int CornerTextureSize = 32;
        private const float BodyTop = 0.71f;
        private const float BodyBottomDefault = 0.135f;
        private const string FontBold = "robotocondensed-bold.ttf";
        private const string FontRegular = "robotocondensed-regular.ttf";
        #endregion

        #region Data Structures
        class StoredData
        {
            public List<Announcement> Announcements = new List<Announcement>();
            public Dictionary<ulong, long> LastSeenNews = new Dictionary<ulong, long>();
            // "Mark all read": every post with Timestamp <= this value counts as read for that player.
            public Dictionary<ulong, long> ReadAllUpTo = new Dictionary<ulong, long>();
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
            public string EditedDate;
            public HashSet<ulong> LikedPlayers = new HashSet<ulong>();
            public HashSet<ulong> ReadByPlayers = new HashSet<ulong>();
            public HashSet<ulong> ReadRewardedPlayers = new HashSet<ulong>();
            public HashSet<ulong> LikeRewardedPlayers = new HashSet<ulong>();
            // Optional poll: 2-4 options, one vote per player (option index).
            public List<string> PollOptions = new List<string>();
            public Dictionary<ulong, int> PollVotes = new Dictionary<ulong, int>();
            public bool PollClosed;
        }

        class ReadRewardState
        {
            public string AnnId;
            public Timer Timer;
        }

        class EditorSession
        {
            public Announcement Draft;
            public string EditingId = string.Empty;
            public bool Notify = true;
            public bool Discord = true;
            public bool Poll;
            public bool PollClosed;
            public string[] PollOptions = new string[MaxPollOptions];
            public bool IsNew => string.IsNullOrEmpty(EditingId);
        }

        enum PopupOrigin { Broadcast, Direct, Archive, Preview }

        // Which full-screen view a player has open; drives how much a redraw animates.
        enum UiScreen { None, Popup, Archive, Admin, Editor, Themes }

        class PopupState
        {
            public string AnnId;
            public PopupOrigin Origin;
        }

        enum AnnouncementType { Info, Warning, Alert, Event, Update }

        class ConfigData
        {
            public GeneralSettings General { get; set; } = new GeneralSettings();
            public NotificationSettings Notification { get; set; } = new NotificationSettings();
            public DiscordSettings Discord { get; set; } = new DiscordSettings();
            public RewardSettings Rewards { get; set; } = new RewardSettings();
            public PollSettings Polls { get; set; } = new PollSettings();

            public string SelectedTheme { get; set; } = "Default";
            public Dictionary<string, UIColors> Themes { get; set; } = new Dictionary<string, UIColors>();

            [JsonIgnore]
            public UIColors Colors
            {
                get
                {
                    if (Themes == null || Themes.Count == 0) return new UIColors();
                    UIColors theme;
                    if (Themes.TryGetValue(SelectedTheme, out theme)) return theme;
                    return Themes.Values.First();
                }
            }
        }

        class GeneralSettings
        {
            public int AutoCloseSeconds { get; set; } = 15;
            public bool EnableAutoClose { get; set; } = true;
            public bool ShowNewsOnConnect { get; set; } = true;
            public bool UnreadReminderOnConnect { get; set; } = true;
            public bool RequireViewPermission { get; set; } = true;
            public string ServerName { get; set; } = "SERVER NEWS";
            public int AnnouncementsPerPage { get; set; } = 5;
            public int MaxStoredAnnouncements { get; set; } = 50;
            public bool StripLinksFromBody { get; set; } = true;
            public string PopupImageFrame { get; set; } = "Tall";
            public bool ArchiveThumbnails { get; set; } = true;
            public bool RoundedCorners { get; set; } = true;
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
            public string AvatarUrl { get; set; } = "";
            public string RoleMention { get; set; } = "";
        }

        class PollSettings
        {
            public bool AllowVoteChange { get; set; } = true;
            public bool ShowResultsBeforeVoting { get; set; } = false;
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
                ["MarkAllRead"] = "MARK ALL READ",
                ["MarkedAllRead"] = "Marked {0} announcement(s) as read.",
                ["NothingUnread"] = "You have no unread announcements.",
                ["NewPoll"] = "NEW POLL",
                ["PollLabel"] = "POLL",
                ["PollVotes"] = "{0} VOTES",
                ["PollClickToVote"] = "CLICK AN OPTION TO VOTE",
                ["PollVotedChange"] = "YOU VOTED  •  CLICK TO CHANGE",
                ["PollVoted"] = "THANKS FOR VOTING",
                ["PollClosedLabel"] = "VOTING CLOSED",
                ["PollAlreadyVoted"] = "You have already voted in this poll.",
                ["OptionPoll"] = "ADD POLL",
                ["OptionPollClosed"] = "CLOSE VOTING",
                ["PollOptionsLabel"] = "POLL OPTIONS (2-4, empty ones are skipped)",
                ["PollNeedsOptions"] = "A poll needs at least 2 options.",
                ["PollDiscordField"] = "Poll - vote in-game with /news",
                ["ConsolePollUsage"] = "Usage: news.poll \"Title\" \"Text\" \"Option 1|Option 2|...\" [Type]",
                ["AdminStats"] = "{0} POSTS  •  {1} PINNED  •  {2} READS  •  {3} LIKES",
                ["NoPermissionView"] = "You do not have permission to view news.",
                ["NoNewsHistory"] = "No news history available.",
                ["NewsBroadcasted"] = "News broadcasted!",
                ["ArchiveTitle"] = "ARCHIVE",
                ["ReadMore"] = "READ >",
                ["ViewArchive"] = "VIEW ARCHIVE",
                ["BackToArchive"] = "< BACK TO ARCHIVE",
                ["BackToEditor"] = "< BACK TO EDITOR",
                ["PostedBy"] = "POSTED BY",
                ["EditedTag"] = "EDITED",
                ["NewAnnouncement"] = "NEW ANNOUNCEMENT",
                ["AutoCloseIn"] = "CLOSES IN %TIME_LEFT%S",
                ["ClickToRead"] = "CLICK TO READ",
                ["Close"] = "CLOSE",
                ["Previous"] = "< PREVIOUS",
                ["Next"] = "NEXT >",
                ["Page"] = "PAGE {0} / {1}",
                ["PositionOf"] = "{0} / {1}",
                ["UnreadBadge"] = "NEW",
                ["UnreadCount"] = "{0} UNREAD",
                ["PreviewBadge"] = "PREVIEW",
                ["AdminControl"] = "NEWS MANAGEMENT",
                ["NewPost"] = "NEW ANNOUNCEMENT",
                ["Themes"] = "APPEARANCE",
                ["NoPostsYet"] = "No announcements yet.\nClick 'NEW ANNOUNCEMENT' to create one.",
                ["CreateAnnouncement"] = "CREATE NEW ANNOUNCEMENT",
                ["EditAnnouncement"] = "EDIT ANNOUNCEMENT",
                ["AnnouncementTitle"] = "ANNOUNCEMENT TITLE",
                ["ImageUrl"] = "IMAGE URL (leave empty for no image)",
                ["TypeLabel"] = "ANNOUNCEMENT TYPE",
                ["Options"] = "OPTIONS",
                ["OptionPin"] = "PIN TO TOP",
                ["OptionNotify"] = "NOTIFY ONLINE PLAYERS",
                ["OptionNotifyEdit"] = "RE-NOTIFY PLAYERS",
                ["OptionDiscord"] = "POST TO DISCORD",
                ["ContentBody"] = "CONTENT BODY",
                ["BodyHint"] = "Press Enter or use \\n for a new line. Long posts scroll in-game.",
                ["SaveBroadcast"] = "PUBLISH ANNOUNCEMENT",
                ["SaveChanges"] = "SAVE CHANGES",
                ["Preview"] = "PREVIEW",
                ["Cancel"] = "CANCEL",
                ["Back"] = "< BACK",
                ["SelectTheme"] = "SELECT THEME",
                ["ActiveBadge"] = "ACTIVE",
                ["Unknown"] = "UNKNOWN",
                ["NoTitle"] = "(no title)",
                ["EditButton"] = "EDIT",
                ["DelButton"] = "DEL",
                ["ResendButton"] = "RESEND",
                ["ReadsLikes"] = "{0} READS  •  {1} LIKES",
                ["ReadsLikesVotes"] = "{0} READS  •  {1} LIKES  •  {2} VOTES",
                ["PostsCount"] = "{0} POSTS",
                ["DeleteAnnouncement"] = "DELETE ANNOUNCEMENT",
                ["DeleteConfirmBody"] = "\"{0}\"\nThis cannot be undone.",
                ["ConfirmDelete"] = "DELETE",
                ["EditTargetGone"] = "The announcement you were editing was removed before you could save.",
                ["TitleRequired"] = "Please enter a title before publishing.",
                ["InvalidImageUrl"] = "That image URL is not valid. Use a direct http(s) link to an image, or leave it empty.",
                ["PublishedNoNotify"] = "Announcement published (online players were not notified).",
                ["AnnouncementSavedNotified"] = "Announcement published and sent to {0} online player(s).",
                ["AnnouncementUpdated"] = "Announcement updated.",
                ["Resent"] = "Notification re-sent to {0} online player(s).",
                ["RewardRead"] = "Thanks for reading the news! Reward: {0}",
                ["RewardLike"] = "Thanks for the like! Reward: {0}",
                ["UnreadReminder"] = "You have {0} unread announcement(s). Type /news to read them.",
                ["UnreadReminderMore"] = "You have {0} more unread announcement(s). Type /news to read them.",
                ["ChatHelp"] = "/news - open the news archive\n/news latest - show the newest announcement\n/news read - mark everything as read",
                ["ChatHelpAdmin"] = "\n/news admin - open news management",
                ["PinButton"] = "PIN",
                ["UnpinButton"] = "UNPIN",
                ["PinnedBadge"] = "PINNED",
                ["SelectedCount"] = "{0} SELECTED",
                ["BulkDelete"] = "DELETE",
                ["BulkPin"] = "PIN ALL",
                ["BulkUnpin"] = "UNPIN ALL",
                ["ClearSelection"] = "CLEAR",
                ["SelectPageToggle"] = "SELECT PAGE",
                ["BulkDeleteTitle"] = "DELETE SELECTED",
                ["BulkDeleteBody"] = "Delete {0} announcement(s)?\nThis cannot be undone.",
                ["BulkDeleted"] = "Deleted {0} announcement(s).",
                ["BulkPinned"] = "Pinned {0} announcement(s).",
                ["BulkUnpinned"] = "Unpinned {0} announcement(s).",
                ["ThemeSet"] = "Theme set to: {0}",
                ["ThemeNotFound"] = "Theme '{0}' not found. Available: {1}",
                ["ThemeUsage"] = "Usage: news.admin.settheme \"ThemeName\"",
                ["ConsoleShowUsage"] = "Usage: news.show \"Title\" \"ImageURL or -\" \"Text\" [Info|Warning|Alert|Event|Update]",
                ["ConsoleTriggerUsage"] = "Usage: news.trigger <SteamID/Name> [index]",
                ["ConsoleDeleteUsage"] = "Usage: news.delete <index|id>",
                ["ConsoleResendUsage"] = "Usage: news.resend <index|id>",
                ["ConsoleInvalidIndex"] = "Invalid index or id. Use 'news.list' to see all announcements.",
                ["ConsoleDeleted"] = "Deleted announcement: '{0}'",
                ["ConsoleNoStored"] = "No announcements stored.",
                ["ConsoleListHeader"] = "=== Stored Announcements ({0}) ===",
                ["PlayerNotFound"] = "Player not found.",
                ["PopupTriggered"] = "News popup triggered for {0}",
                ["NoAnnouncementsAvailable"] = "No announcements available."
            }, this);
        }

        private string Msg(string key, BasePlayer player = null, params object[] args)
        {
            var message = lang.GetMessage(key, this, player?.UserIDString);
            return args != null && args.Length > 0 ? string.Format(message, args) : message;
        }
        #endregion

        #region Helpers
        private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private static string Anchor(float x, float y) => F(x) + " " + F(y);

        private string NormalizeBodyText(string value, bool stripLinks)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            value = value.Replace("\r\n", "\n").Replace("\r", "\n");
            value = value.Replace("\\n", "\n");
            value = SlashNewlineRegex.Replace(value, "\n");
            if (stripLinks) value = LinkRegex.Replace(value, string.Empty);
            value = MultiSpaceRegex.Replace(value, " ");
            value = BlankLinesRegex.Replace(value, "\n\n");

            return value.Trim();
        }

        private string NormalizeInputBody(string value) => NormalizeBodyText(LimitText(value, MaxContentChars), config.General.StripLinksFromBody);

        private static string LimitText(string value, int maxLength)
        {
            value = value ?? string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        private static string Ellipsize(string value, int maxChars)
        {
            value = value ?? string.Empty;
            if (maxChars < 4 || value.Length <= maxChars) return value;
            return value.Substring(0, maxChars - 3).TrimEnd() + "...";
        }

        private static string NormalizeImageUrl(string value)
        {
            value = LimitText(value, MaxImageUrlChars).Trim();
            if (string.IsNullOrEmpty(value)) return string.Empty;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return string.Empty;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ? value : string.Empty;
        }

        private static List<string> BuildBodyDisplayLines(string text, int wrapChars)
        {
            if (wrapChars < 16) wrapChars = 16;
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                lines.Add(string.Empty);
                return lines;
            }

            foreach (var rawLine in text.Split('\n'))
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

            return lines;
        }

        // Word-wrap line count for short single-paragraph strings (titles).
        private static int CountWrappedLines(string text, int perLine)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            if (perLine < 1) perLine = 1;
            int lines = 1, current = 0;
            foreach (var word in text.Split(' '))
            {
                int len = word.Length;
                if (current == 0) current = len;
                else if (current + 1 + len <= perLine) current += 1 + len;
                else { lines++; current = len; }
                while (current > perLine) { lines++; current -= perLine; }
            }
            return lines;
        }

        // Largest font size that lets the (upper-case, bold) title fit the box without being cut off.
        private static int PickTitleFontSize(string title, float widthUnits, float heightUnits)
        {
            int[] sizes = { 32, 28, 26, 24, 22, 20, 18, 16 };
            foreach (int size in sizes)
            {
                int perLine = Math.Max(4, (int)(widthUnits / (size * 0.55f)));
                if (CountWrappedLines(title, perLine) * size * 1.2f <= heightUnits) return size;
            }
            return 14;
        }

        private static string NewAnnouncementId() => Guid.NewGuid().ToString("N");

        private static string Now() => DateTime.Now.ToString(InvariantDateFormat, CultureInfo.InvariantCulture);

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

        // Console commands accept either the list index shown by news.list or an announcement id.
        private int ResolveIndex(string indexOrId)
        {
            int index;
            if (int.TryParse(indexOrId, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                return index >= 0 && index < announcements.Count ? index : -1;
            return FindIndexById(indexOrId);
        }

        private HashSet<string> GetAdminSelection(ulong userId)
        {
            HashSet<string> set;
            if (!adminSelections.TryGetValue(userId, out set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                adminSelections[userId] = set;
            }
            return set;
        }

        private void PruneAdminSelection(ulong userId)
        {
            HashSet<string> set;
            if (!adminSelections.TryGetValue(userId, out set) || set.Count == 0) return;
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

        private bool IsUnread(Announcement ann, ulong userId)
        {
            if (ann.ReadByPlayers != null && ann.ReadByPlayers.Contains(userId)) return false;
            long readAll;
            return !(storedData.ReadAllUpTo.TryGetValue(userId, out readAll) && ann.Timestamp <= readAll);
        }

        private static bool HasPoll(Announcement ann) => ann != null && ann.PollOptions != null && ann.PollOptions.Count >= 2;

        private static int[] CountVotes(Announcement ann)
        {
            var counts = new int[ann.PollOptions.Count];
            foreach (var vote in ann.PollVotes.Values)
                if (vote >= 0 && vote < counts.Length) counts[vote]++;
            return counts;
        }

        private static int VoteOf(Announcement ann, ulong userId)
        {
            int vote;
            return ann.PollVotes != null && ann.PollVotes.TryGetValue(userId, out vote) ? vote : -1;
        }

        // Non-empty, trimmed poll options (max MaxPollOptions).
        private static List<string> CleanPollOptions(IEnumerable<string> options)
        {
            return options
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => LimitText(o.Trim(), MaxPollOptionChars))
                .Take(MaxPollOptions)
                .ToList();
        }

        private int CountUnread(ulong userId)
        {
            int count = 0;
            foreach (var ann in announcements) if (IsUnread(ann, userId)) count++;
            return count;
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
                ["pollOptions"] = HasPoll(ann) ? ann.PollOptions.ToArray() : new string[0],
                ["pollVotes"] = ann.PollVotes?.Count ?? 0
            };
        }

        private bool CanAdmin(BasePlayer player)
        {
            return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAdmin));
        }

        private bool CanView(BasePlayer player)
        {
            if (player == null) return false;
            if (!config.General.RequireViewPermission) return true;
            return CanAdmin(player) || permission.UserHasPermission(player.UserIDString, PermView);
        }

        private bool RequireView(BasePlayer player, bool notify = false)
        {
            if (CanView(player)) return true;
            if (notify && player != null) SendReply(player, Msg("NoPermissionView", player));
            return false;
        }

        // Server console / RCON or an in-game admin.
        private bool IsAdminArg(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null) return true;
            if (arg.IsAdmin) return true;
            return permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin);
        }

        // In-game admin issuing a UI command; null when the caller is not allowed.
        private BasePlayer AdminPlayer(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            return CanAdmin(player) ? player : null;
        }

        private bool DiscordConfigured => config.Discord.Enabled && !string.IsNullOrEmpty(config.Discord.WebhookUrl);

        private void PlaySound(BasePlayer player)
        {
            string prefab = config.Notification.NotificationSound;
            if (player == null || string.IsNullOrEmpty(prefab) || player.net?.connection == null) return;
            // Sent only to this player, so people standing nearby do not hear everyone's notifications.
            EffectNetwork.Send(new Effect(prefab, player.transform.position, Vector3.zero), player.net.connection);
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
                catch { }

                if (raw != null && MigrateLegacyRewardArrays(raw)) needsSave = true;

                if (raw != null && raw["Rewards"] == null) needsSave = true;

                config = raw != null ? raw.ToObject<ConfigData>() : Config.ReadObject<ConfigData>();
                if (config == null) throw new Exception("config is empty");

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
                    config.Themes = BuildDefaultThemes();
                    if (legacyColors != null) config.Themes["Default"] = legacyColors;
                    config.SelectedTheme = "Default";
                    needsSave = true;
                }

                if (ValidateConfig()) needsSave = true;

                // Write back when the file is missing options added in newer versions.
                if (raw != null && !needsSave && !JToken.DeepEquals(raw, JObject.FromObject(config))) needsSave = true;

                if (needsSave) SaveConfig();
            }
            catch (Exception ex)
            {
                PrintWarning($"Configuration was invalid and has been reset: {ex.Message}");
                LoadDefaultConfig();
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
                var legacyItems = rewards[key] as JArray;
                if (legacyItems != null)
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

        private bool ValidateConfig()
        {
            bool changed = false;

            if (config.General == null) { config.General = new GeneralSettings(); changed = true; }
            if (config.Notification == null) { config.Notification = new NotificationSettings(); changed = true; }
            if (config.Discord == null) { config.Discord = new DiscordSettings(); changed = true; }
            if (config.Rewards == null) { config.Rewards = new RewardSettings(); changed = true; }
            if (config.Polls == null) { config.Polls = new PollSettings(); changed = true; }

            int perPage = Math.Max(1, Math.Min(8, config.General.AnnouncementsPerPage));
            int maxStored = Math.Max(1, Math.Min(500, config.General.MaxStoredAnnouncements));
            int autoClose = Math.Max(1, Math.Min(3600, config.General.AutoCloseSeconds));
            int notificationDuration = Math.Max(1, Math.Min(300, config.Notification.Duration));
            int readDelay = Math.Max(1, Math.Min(3600, config.Rewards.ReadDelaySeconds));

            if (config.General.AnnouncementsPerPage != perPage) { config.General.AnnouncementsPerPage = perPage; changed = true; }
            if (config.General.MaxStoredAnnouncements != maxStored) { config.General.MaxStoredAnnouncements = maxStored; changed = true; }
            if (config.General.AutoCloseSeconds != autoClose) { config.General.AutoCloseSeconds = autoClose; changed = true; }
            if (config.Notification.Duration != notificationDuration) { config.Notification.Duration = notificationDuration; changed = true; }
            if (config.Rewards.ReadDelaySeconds != readDelay) { config.Rewards.ReadDelaySeconds = readDelay; changed = true; }

            if (config.General.ServerName == null) { config.General.ServerName = "SERVER NEWS"; changed = true; }
            string frame = string.Equals(config.General.PopupImageFrame, "Wide", StringComparison.OrdinalIgnoreCase) ? "Wide" : "Tall";
            if (config.General.PopupImageFrame != frame) { config.General.PopupImageFrame = frame; changed = true; }

            if (config.Rewards.ReadRewards == null) { config.Rewards.ReadRewards = new RewardBundle(); changed = true; }
            if (config.Rewards.LikeRewards == null) { config.Rewards.LikeRewards = new RewardBundle(); changed = true; }
            if (config.Rewards.ReadRewards.Items == null) { config.Rewards.ReadRewards.Items = new List<RewardItem>(); changed = true; }
            if (config.Rewards.LikeRewards.Items == null) { config.Rewards.LikeRewards.Items = new List<RewardItem>(); changed = true; }

            if (config.Themes == null) { config.Themes = new Dictionary<string, UIColors>(); changed = true; }
            foreach (var key in config.Themes.Where(x => string.IsNullOrWhiteSpace(x.Key) || x.Value == null).Select(x => x.Key).ToList())
            {
                config.Themes.Remove(key);
                changed = true;
            }

            if (config.Themes.Count == 0)
            {
                config.Themes["Default"] = new UIColors();
                changed = true;
            }

            if (string.IsNullOrEmpty(config.SelectedTheme) || !config.Themes.ContainsKey(config.SelectedTheme))
            {
                config.SelectedTheme = config.Themes.Keys.First();
                changed = true;
            }

            return changed;
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData { Themes = BuildDefaultThemes() };
        }

        private static Dictionary<string, UIColors> BuildDefaultThemes()
        {
            return new Dictionary<string, UIColors>
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
        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermView, this);
            LoadAnnouncements();
        }

        private void OnServerInitialized()
        {
            GenerateCornerTextures();
            RegisterAllImages();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "ImageLibrary") NextTick(RegisterAllImages);
        }

        private void OnServerSave()
        {
            if (dataDirty) SaveAnnouncements();
        }

        private void RegisterAllImages()
        {
            if (ImageLibrary == null || !ImageLibrary.IsLoaded) return;

            var imageUrls = announcements
                .Where(x => !string.IsNullOrEmpty(x.ImageUrl))
                .Select(x => x.ImageUrl)
                .Distinct()
                .ToDictionary(x => x, x => x);

            if (imageUrls.Count > 0)
                ImageLibrary.Call("ImportImageList", Title, imageUrls, 0UL, false);
        }

        private void RegisterImage(string url)
        {
            if (!string.IsNullOrEmpty(url) && ImageLibrary != null && ImageLibrary.IsLoaded)
                ImageLibrary.Call("AddImage", url, url, 0UL);
        }

        private void SaveAnnouncements()
        {
            int max = config.General.MaxStoredAnnouncements;
            if (storedData.Announcements.Count > max)
            {
                // Pinned announcements are kept first, then the newest ones.
                var keep = storedData.Announcements
                    .OrderByDescending(x => x.Pinned)
                    .ThenByDescending(x => x.Timestamp)
                    .Take(max)
                    .ToList();
                var kept = new HashSet<Announcement>(keep);
                var dropped = storedData.Announcements.Where(x => !kept.Contains(x)).ToList();
                storedData.Announcements = keep.OrderByDescending(x => x.Timestamp).ToList();
                foreach (var old in dropped) Interface.CallHook("OnNewsDeleted", BuildHookData(old));
            }

            announcements = storedData.Announcements;
            dataDirty = false;
            if (saveTimer != null)
            {
                saveTimer.Destroy();
                saveTimer = null;
            }
            Interface.Oxide.DataFileSystem.WriteObject(DataFile, storedData);
        }

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
            if (storedData.ReadAllUpTo == null) storedData.ReadAllUpTo = new Dictionary<ulong, long>();

            announcements = storedData.Announcements;

            long baseTime = DateTime.UtcNow.Ticks;
            bool changed = false;
            int removedNulls = announcements.RemoveAll(x => x == null);
            if (removedNulls > 0)
            {
                PrintWarning($"Removed {removedNulls} invalid null announcement record(s) from stored data.");
                changed = true;
            }
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < announcements.Count; i++)
            {
                var ann = announcements[i];
                if (ann.Timestamp == 0)
                {
                    ann.Timestamp = baseTime - (i * 10000);
                    changed = true;
                }

                if (ann.LikedPlayers == null) { ann.LikedPlayers = new HashSet<ulong>(); changed = true; }
                if (ann.ReadRewardedPlayers == null) { ann.ReadRewardedPlayers = new HashSet<ulong>(); changed = true; }
                if (ann.LikeRewardedPlayers == null) { ann.LikeRewardedPlayers = new HashSet<ulong>(); changed = true; }
                if (ann.ReadByPlayers == null)
                {
                    ann.ReadByPlayers = new HashSet<ulong>(ann.ReadRewardedPlayers);
                    changed = true;
                }
                if (ann.PollOptions == null) { ann.PollOptions = new List<string>(); changed = true; }
                if (ann.PollVotes == null) { ann.PollVotes = new Dictionary<ulong, int>(); changed = true; }

                if (string.IsNullOrEmpty(ann.Id) || !seenIds.Add(ann.Id))
                {
                    ann.Id = NewAnnouncementId();
                    seenIds.Add(ann.Id);
                    changed = true;
                }

                // Links are only stripped when posts are written, never retroactively on load.
                string normalizedText = NormalizeBodyText(ann.Text, false);
                if (ann.Text != normalizedText)
                {
                    ann.Text = normalizedText;
                    changed = true;
                }
            }

            // Keep the list newest-first so index 0 is always the latest announcement.
            for (int i = 1; i < announcements.Count; i++)
            {
                if (announcements[i].Timestamp > announcements[i - 1].Timestamp)
                {
                    storedData.Announcements = announcements.OrderByDescending(x => x.Timestamp).ToList();
                    announcements = storedData.Announcements;
                    changed = true;
                    break;
                }
            }

            if (changed) SaveAnnouncements();
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null || !player.IsConnected || announcements.Count == 0 || !CanView(player)) return;

            ulong userId = player.userID;
            long seenBefore;
            storedData.LastSeenNews.TryGetValue(userId, out seenBefore);

            // Popup: only for a post this player has not been shown yet (live broadcasts count as shown).
            bool wantPopup = config.General.ShowNewsOnConnect && announcements[0].Timestamp > seenBefore;
            // Chat reminder: once per session, about posts published since the last visit.
            bool wantReminder = config.General.UnreadReminderOnConnect && remindedPlayers.Add(userId);
            if (!wantPopup && !wantReminder) return;

            timer.Once(2f, () =>
            {
                if (player == null || !player.IsConnected || announcements.Count == 0 || !CanView(player)) return;

                var current = announcements[0];
                bool showPopup = wantPopup && !playersWithUiOpen.Contains(userId);

                if (wantReminder)
                {
                    int fresh = announcements.Count(a => a.Timestamp > seenBefore && IsUnread(a, userId));
                    if (showPopup && current.Timestamp > seenBefore && IsUnread(current, userId)) fresh--;
                    if (fresh > 0) SendReply(player, Msg(showPopup ? "UnreadReminderMore" : "UnreadReminder", player, fresh));
                }

                MarkSeen(userId, current.Timestamp);
                if (showPopup) ShowPopup(player, current, PopupOrigin.Broadcast);
            });
        }

        private void MarkSeen(ulong userId, long timestamp)
        {
            long previous;
            if (storedData.LastSeenNews.TryGetValue(userId, out previous) && previous >= timestamp) return;
            storedData.LastSeenNews[userId] = timestamp;
            MarkDataDirty();
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUI(player);
                DestroyNotification(player);
            }

            foreach (var t in autoCloseTimers.Values) t?.Destroy();
            foreach (var t in notificationTimers.Values) t?.Destroy();
            foreach (var s in readRewardTimers.Values) s?.Timer?.Destroy();
            autoCloseTimers.Clear();
            notificationTimers.Clear();
            readRewardTimers.Clear();
            playersWithUiOpen.Clear();
            editors.Clear();
            adminSelections.Clear();
            likeCooldowns.Clear();
            voteCooldowns.Clear();
            openPopups.Clear();
            archivePages.Clear();
            remindedPlayers.Clear();
            currentScreens.Clear();
            toastNames.Clear();

            if (dataDirty && storedData != null) SaveAnnouncements();
            saveTimer?.Destroy();
            saveTimer = null;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            ulong id = player.userID;
            editors.Remove(id);
            playersWithUiOpen.Remove(id);
            adminSelections.Remove(id);
            likeCooldowns.Remove(id);
            voteCooldowns.Remove(id);
            openPopups.Remove(id);
            archivePages.Remove(id);
            remindedPlayers.Remove(id);
            currentScreens.Remove(id);
            toastNames.Remove(id);
            CancelReadRewardTimer(id);
            CancelAutoClose(id);

            Timer nt;
            if (notificationTimers.TryGetValue(id, out nt))
            {
                nt?.Destroy();
                notificationTimers.Remove(id);
            }
        }
        #endregion

        #region Publishing
        // Shows the new announcement to every online player who may view it. Returns how many were reached.
        private int NotifyPlayers(Announcement ann)
        {
            int reached = 0;
            foreach (var p in BasePlayer.activePlayerList.ToList())
            {
                if (p == null || !p.IsConnected || !CanView(p)) continue;
                reached++;
                MarkSeen(p.userID, ann.Timestamp);
                // Players already in a NewsBroadcaster screen (e.g. an admin mid-edit) get the toast, never a forced popup.
                if (config.Notification.Enabled || playersWithUiOpen.Contains(p.userID))
                    ShowNotification(p, ann);
                else
                    ShowPopup(p, ann, PopupOrigin.Broadcast, true);
            }
            return reached;
        }

        private Announcement CreateAnnouncement(string title, string imageUrl, string text, AnnouncementType type, string author, bool pinned, List<string> pollOptions = null)
        {
            var ann = new Announcement
            {
                Id = NewAnnouncementId(),
                Title = LimitText(title, MaxTitleChars).Trim(),
                ImageUrl = NormalizeImageUrl(imageUrl),
                Text = NormalizeInputBody(text),
                Date = Now(),
                Author = string.IsNullOrEmpty(author) ? config.General.ServerName : author,
                Type = type,
                Timestamp = DateTime.UtcNow.Ticks,
                Pinned = pinned
            };
            if (pollOptions != null && pollOptions.Count >= 2) ann.PollOptions = pollOptions;

            announcements.Insert(0, ann);
            SaveAnnouncements();
            RegisterImage(ann.ImageUrl);
            Interface.CallHook("OnNewsBroadcast", BuildHookData(ann));
            return ann;
        }
        #endregion

        #region Commands
        [ConsoleCommand("news.show")]
        private void CmdNewsShow(ConsoleSystem.Arg arg)
        {
            if (!IsAdminArg(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            string fullCommand = arg.FullString ?? string.Empty;
            var rawMatches = CommandSplitRegex.Matches(fullCommand).Cast<Match>().ToList();
            var values = rawMatches.Select(m => m.Value.Trim('"')).ToList();

            if (values.Count < 3)
            {
                SendReply(arg, Msg("ConsoleShowUsage"));
                return;
            }

            string title = values[0];
            string img = values[1] == "-" ? string.Empty : values[1];

            string text;
            AnnouncementType type = AnnouncementType.Info;
            AnnouncementType parsedType;

            bool lastWasUnquoted = rawMatches.Count > 0 && !rawMatches[rawMatches.Count - 1].Value.StartsWith("\"");
            if (values.Count > 3 && lastWasUnquoted && Enum.TryParse(values[values.Count - 1], true, out parsedType))
            {
                type = parsedType;
                text = string.Join(" ", values.GetRange(2, values.Count - 3).ToArray());
            }
            else
            {
                text = string.Join(" ", values.GetRange(2, values.Count - 2).ToArray());
            }

            string authorName = arg.Connection != null ? arg.Connection.username : config.General.ServerName;
            var ann = CreateAnnouncement(title, img, text, type, authorName, false);
            SendToDiscord(ann);
            NotifyPlayers(ann);

            SendReply(arg, Msg("NewsBroadcasted"));
        }

        [ConsoleCommand("news.poll")]
        private void CmdNewsPoll(ConsoleSystem.Arg arg)
        {
            if (!IsAdminArg(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            var rawMatches = CommandSplitRegex.Matches(arg.FullString ?? string.Empty).Cast<Match>().ToList();
            var values = rawMatches.Select(m => m.Value.Trim('"')).ToList();
            if (values.Count < 3)
            {
                SendReply(arg, Msg("ConsolePollUsage"));
                return;
            }

            AnnouncementType type = AnnouncementType.Info;
            AnnouncementType parsedType;
            bool lastWasUnquoted = !rawMatches[rawMatches.Count - 1].Value.StartsWith("\"");
            if (values.Count > 3 && lastWasUnquoted && Enum.TryParse(values[values.Count - 1], true, out parsedType))
                type = parsedType;

            var options = CleanPollOptions(values[2].Split('|'));
            if (options.Count < 2)
            {
                SendReply(arg, Msg("PollNeedsOptions"));
                return;
            }

            string authorName = arg.Connection != null ? arg.Connection.username : config.General.ServerName;
            var ann = CreateAnnouncement(values[0], string.Empty, values[1], type, authorName, false, options);
            SendToDiscord(ann);
            NotifyPlayers(ann);
            SendReply(arg, Msg("NewsBroadcasted"));
        }

        [ConsoleCommand("news.trigger")]
        private void CmdNewsTrigger(ConsoleSystem.Arg arg)
        {
            if (!IsAdminArg(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, Msg("ConsoleTriggerUsage"));
                return;
            }

            var player = BasePlayer.Find(arg.GetString(0));
            if (player == null || !player.IsConnected)
            {
                SendReply(arg, Msg("PlayerNotFound"));
                return;
            }

            if (announcements.Count == 0)
            {
                SendReply(arg, Msg("NoAnnouncementsAvailable"));
                return;
            }

            int index = arg.GetInt(1, 0);
            if (index < 0 || index >= announcements.Count) index = 0;

            ShowPopup(player, announcements[index], PopupOrigin.Direct);
            SendReply(arg, Msg("PopupTriggered", null, player.displayName));
        }

        [ConsoleCommand("news.delete")]
        private void CmdNewsDelete(ConsoleSystem.Arg arg)
        {
            if (!IsAdminArg(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, Msg("ConsoleDeleteUsage"));
                return;
            }

            int index = ResolveIndex(arg.GetString(0));
            if (index < 0)
            {
                SendReply(arg, Msg("ConsoleInvalidIndex"));
                return;
            }

            var removed = announcements[index];
            announcements.RemoveAt(index);
            SaveAnnouncements();
            Interface.CallHook("OnNewsDeleted", BuildHookData(removed));
            SendReply(arg, Msg("ConsoleDeleted", null, removed.Title));
        }

        [ConsoleCommand("news.resend")]
        private void CmdNewsResend(ConsoleSystem.Arg arg)
        {
            if (!IsAdminArg(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, Msg("ConsoleResendUsage"));
                return;
            }

            int index = ResolveIndex(arg.GetString(0));
            if (index < 0)
            {
                SendReply(arg, Msg("ConsoleInvalidIndex"));
                return;
            }

            SendReply(arg, Msg("Resent", null, NotifyPlayers(announcements[index])));
        }

        [ConsoleCommand("news.list")]
        private void CmdNewsList(ConsoleSystem.Arg arg)
        {
            if (!IsAdminArg(arg))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }

            if (announcements.Count == 0)
            {
                SendReply(arg, Msg("ConsoleNoStored"));
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(Msg("ConsoleListHeader", null, announcements.Count));
            for (int i = 0; i < announcements.Count; i++)
            {
                var a = announcements[i];
                sb.Append($"[{i}] [{a.Type}] \"{a.Title}\" — {a.Date} by {a.Author}");
                if (a.Pinned) sb.Append(" [PINNED]");
                sb.AppendLine($"  (id {a.Id}, {a.ReadByPlayers?.Count ?? 0} reads, {a.LikedPlayers?.Count ?? 0} likes)");
            }
            SendReply(arg, sb.ToString().TrimEnd());
        }

        [ChatCommand("news")]
        private void CmdChatNews(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            string sub = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;

            switch (sub)
            {
                case "admin":
                case "manage":
                    if (!CanAdmin(player))
                    {
                        SendReply(player, Msg("NoPermissionCommand", player));
                        return;
                    }
                    ShowAdminList(player, 0);
                    return;

                case "latest":
                case "last":
                    if (!RequireView(player, true)) return;
                    if (announcements.Count == 0)
                    {
                        SendReply(player, Msg("NoNewsHistory", player));
                        return;
                    }
                    ShowPopup(player, announcements[0], PopupOrigin.Direct);
                    return;

                case "read":
                    if (!RequireView(player, true)) return;
                    MarkAllRead(player);
                    return;

                case "help":
                    SendReply(player, Msg("ChatHelp", player) + (CanAdmin(player) ? Msg("ChatHelpAdmin", player) : string.Empty));
                    return;

                default:
                    if (!RequireView(player, true)) return;
                    ShowHistory(player, 0);
                    return;
            }
        }

        [ConsoleCommand("news.page")]
        private void CmdConsolePage(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (!RequireView(player)) return;

            ShowHistory(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.view")]
        private void CmdConsoleView(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (!RequireView(player)) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null) return;

            var origin = string.Equals(arg.GetString(1), "archive", StringComparison.OrdinalIgnoreCase) ? PopupOrigin.Archive : PopupOrigin.Direct;
            ShowPopup(player, ann, origin);
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

        [ConsoleCommand("news.markread")]
        private void CmdMarkRead(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (!RequireView(player)) return;

            MarkAllRead(player);
            ShowHistory(player, GetArchivePage(player.userID));
        }

        // Marks every current announcement as read for this player without touching per-post read stats.
        private int MarkAllRead(BasePlayer player)
        {
            ulong userId = player.userID;
            int unread = CountUnread(userId);
            if (unread == 0)
            {
                SendReply(player, Msg("NothingUnread", player));
                return 0;
            }

            storedData.ReadAllUpTo[userId] = announcements.Max(a => a.Timestamp);
            MarkDataDirty();
            SendReply(player, Msg("MarkedAllRead", player, unread));
            return unread;
        }

        [ConsoleCommand("news.vote")]
        private void CmdNewsVote(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (!RequireView(player)) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null || !HasPoll(ann) || ann.PollClosed) return;

            int option = arg.GetInt(1, -1);
            if (option < 0 || option >= ann.PollOptions.Count) return;

            ulong userId = player.userID;
            float now = UnityEngine.Time.realtimeSinceStartup;
            float nextAllowed;
            if (voteCooldowns.TryGetValue(userId, out nextAllowed) && now < nextAllowed) return;
            voteCooldowns[userId] = now + VoteCooldownSeconds;

            int previous = VoteOf(ann, userId);
            if (previous == option) return;
            if (previous >= 0 && !config.Polls.AllowVoteChange)
            {
                SendReply(player, Msg("PollAlreadyVoted", player));
                return;
            }

            ann.PollVotes[userId] = option;
            MarkDataDirty();
            Interface.CallHook("OnNewsPollVote", player, BuildHookData(ann), option);
            StopAutoClose(player);

            PopupState state;
            if (playersWithUiOpen.Contains(userId) && openPopups.TryGetValue(userId, out state) && state.AnnId == ann.Id)
            {
                // Only the poll block is redrawn, so the reader keeps their scroll position.
                var container = new CuiElementContainer();
                AddPollBlock(container, player, ann, false);
                CuiHelper.DestroyUi(player, PollPanelName);
                CuiHelper.AddUi(player, container);
            }
            else
            {
                ShowPopup(player, ann, PopupOrigin.Direct);
            }
        }

        [ConsoleCommand("news.like")]
        private void CmdNewsLike(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (!RequireView(player)) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null) return;

            ulong userId = player.userID;
            float now = UnityEngine.Time.realtimeSinceStartup;
            float nextAllowed;
            if (likeCooldowns.TryGetValue(userId, out nextAllowed) && now < nextAllowed) return;
            likeCooldowns[userId] = now + LikeCooldownSeconds;

            bool addedLike = !ann.LikedPlayers.Remove(userId);
            if (addedLike) ann.LikedPlayers.Add(userId);

            bool rewarded = false;
            if (addedLike && config.Rewards.EnableLikeReward)
            {
                if (ann.LikeRewardedPlayers == null) ann.LikeRewardedPlayers = new HashSet<ulong>();
                if (!ann.LikeRewardedPlayers.Contains(userId) && GiveRewards(player, config.Rewards.LikeRewards, "RewardLike"))
                {
                    ann.LikeRewardedPlayers.Add(userId);
                    rewarded = true;
                }
            }

            if (rewarded) SaveAnnouncements();
            else MarkDataDirty();

            Interface.CallHook("OnNewsLiked", player, BuildHookData(ann), addedLike);
            StopAutoClose(player);

            PopupState state;
            if (playersWithUiOpen.Contains(userId) && openPopups.TryGetValue(userId, out state) && state.AnnId == ann.Id)
            {
                // Only swap the like button so the reader keeps their scroll position.
                var container = new CuiElementContainer();
                AddLikeButton(container, player, ann);
                CuiHelper.DestroyUi(player, LikeButtonName);
                CuiHelper.AddUi(player, container);
            }
            else
            {
                ShowPopup(player, ann, PopupOrigin.Direct);
            }
        }

        [ConsoleCommand("news.admin")]
        private void CmdNewsAdmin(ConsoleSystem.Arg arg)
        {
            if (!IsAdminArg(arg))
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
            if (!CanAdmin(player))
            {
                SendReply(arg, Msg("NoPermissionCommand"));
                return;
            }
            ShowAdminList(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.create")]
        private void CmdNewsAdminCreate(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            editors[player.userID] = new EditorSession
            {
                Draft = new Announcement
                {
                    Title = string.Empty,
                    Text = string.Empty,
                    Type = AnnouncementType.Info,
                    Author = player.displayName,
                    Date = Now(),
                    ImageUrl = string.Empty
                },
                EditingId = string.Empty,
                Notify = true,
                Discord = DiscordConfigured
            };
            ShowEditor(player);
        }

        [ConsoleCommand("news.admin.edit")]
        private void CmdNewsAdminEdit(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            var original = FindById(arg.GetString(0));
            if (original == null) return;

            editors[player.userID] = new EditorSession
            {
                Draft = new Announcement
                {
                    Id = original.Id,
                    Title = original.Title,
                    ImageUrl = original.ImageUrl,
                    Text = original.Text,
                    Date = original.Date,
                    EditedDate = original.EditedDate,
                    Author = original.Author,
                    Type = original.Type,
                    Timestamp = original.Timestamp,
                    Pinned = original.Pinned,
                    LikedPlayers = new HashSet<ulong>(original.LikedPlayers)
                },
                EditingId = original.Id,
                Notify = false,
                Discord = false,
                Poll = HasPoll(original),
                PollClosed = original.PollClosed
            };
            for (int i = 0; i < MaxPollOptions && i < original.PollOptions.Count; i++)
                editors[player.userID].PollOptions[i] = original.PollOptions[i];
            ShowEditor(player);
        }

        [ConsoleCommand("news.admin.togglepin")]
        private void CmdNewsAdminTogglePin(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null) return;

            ann.Pinned = !ann.Pinned;
            SaveAnnouncements();
            Interface.CallHook("OnNewsEdited", BuildHookData(ann));
            ShowAdminList(player, arg.GetInt(1, 0));
        }

        [ConsoleCommand("news.admin.resend")]
        private void CmdNewsAdminResend(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null) return;

            int page = arg.GetInt(1, 0);
            int reached = NotifyPlayers(ann);
            SendReply(player, Msg("Resent", player, reached));
            ShowAdminList(player, page);
        }

        [ConsoleCommand("news.admin.toggleselect")]
        private void CmdNewsAdminToggleSelect(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            string id = arg.GetString(0);
            if (string.IsNullOrEmpty(id) || FindById(id) == null) return;

            var sel = GetAdminSelection(player.userID);
            if (!sel.Remove(id)) sel.Add(id);

            ShowAdminList(player, arg.GetInt(1, 0));
        }

        [ConsoleCommand("news.admin.selectpage")]
        private void CmdNewsAdminSelectPage(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

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
            var player = AdminPlayer(arg);
            if (player == null) return;

            adminSelections.Remove(player.userID);
            ShowAdminList(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.bulkpin")]
        private void CmdNewsAdminBulkPin(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

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
            var player = AdminPlayer(arg);
            if (player == null) return;

            PruneAdminSelection(player.userID);
            ShowBulkDeleteConfirm(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.bulkdel")]
        private void CmdNewsAdminBulkDel(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

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
            ShowAdminList(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.delconfirm")]
        private void CmdNewsAdminDelConfirm(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            ShowDeleteConfirm(player, arg.GetString(0), arg.GetInt(1, 0));
        }

        [ConsoleCommand("news.admin.del")]
        private void CmdNewsAdminDelete(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            int index = FindIndexById(arg.GetString(0));
            if (index >= 0)
            {
                var removed = announcements[index];
                announcements.RemoveAt(index);
                SaveAnnouncements();
                Interface.CallHook("OnNewsDeleted", BuildHookData(removed));
            }
            CuiHelper.DestroyUi(player, ConfirmLayer);
            ShowAdminList(player, arg.GetInt(1, 0));
        }

        [ConsoleCommand("news.confirm.close")]
        private void CmdConfirmClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player != null) CuiHelper.DestroyUi(player, ConfirmLayer);
        }

        // Poll options from the editor slots; slotMap maps slot index -> option index (slots may be left empty).
        private static List<string> BuildPollOptions(EditorSession session, out Dictionary<int, int> slotMap)
        {
            slotMap = new Dictionary<int, int>();
            var options = new List<string>();
            for (int i = 0; i < session.PollOptions.Length; i++)
            {
                string option = session.PollOptions[i];
                if (string.IsNullOrWhiteSpace(option)) continue;
                slotMap[i] = options.Count;
                options.Add(LimitText(option.Trim(), MaxPollOptionChars));
            }
            return options;
        }

        private static void ApplyPollEdit(Announcement ann, EditorSession session, List<string> options, Dictionary<int, int> slotMap)
        {
            if (options == null || options.Count < 2)
            {
                ann.PollOptions = new List<string>();
                ann.PollVotes = new Dictionary<ulong, int>();
                ann.PollClosed = false;
                return;
            }

            // Votes follow their option when empty slots are removed; votes for removed options are dropped.
            var votes = new Dictionary<ulong, int>();
            foreach (var vote in ann.PollVotes)
            {
                int mapped;
                if (slotMap.TryGetValue(vote.Value, out mapped)) votes[vote.Key] = mapped;
            }
            ann.PollOptions = options;
            ann.PollVotes = votes;
            ann.PollClosed = session.PollClosed;
        }

        private EditorSession GetEditor(BasePlayer player)
        {
            EditorSession session;
            return player != null && editors.TryGetValue(player.userID, out session) ? session : null;
        }

        [ConsoleCommand("news.editor.input")]
        private void CmdEditorInput(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            var session = GetEditor(player);
            if (session == null || arg.Args == null || arg.Args.Length < 1) return;

            string field = arg.GetString(0).ToLowerInvariant();

            string fullStr = arg.FullString ?? string.Empty;
            string value;
            if (fullStr.StartsWith(field, StringComparison.OrdinalIgnoreCase))
            {
                value = fullStr.Substring(field.Length).TrimStart();
                if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
                    value = value.Substring(1, value.Length - 2);
            }
            else
            {
                value = string.Join(" ", arg.Args.Skip(1).ToArray());
            }

            var ann = session.Draft;
            switch (field)
            {
                case "title":
                    ann.Title = LimitText(value, MaxTitleChars).Trim();
                    break;
                case "text":
                    ann.Text = NormalizeInputBody(value);
                    break;
                case "poll1":
                case "poll2":
                case "poll3":
                case "poll4":
                    session.PollOptions[field[4] - '1'] = LimitText(value, MaxPollOptionChars).Trim();
                    break;
                case "image":
                    ann.ImageUrl = NormalizeImageUrl(value);
                    if (string.IsNullOrEmpty(ann.ImageUrl) && !string.IsNullOrEmpty(value.Trim()))
                    {
                        // Redraw so the rejected value disappears from the field.
                        SendReply(player, Msg("InvalidImageUrl", player));
                        ShowEditor(player);
                    }
                    break;
            }
            // The field already shows what was typed, so the editor is not rebuilt here:
            // the admin can click straight into the next field without losing focus.
        }

        [ConsoleCommand("news.editor.type")]
        private void CmdEditorType(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            var session = GetEditor(player);
            if (session == null) return;

            int next = ((int)session.Draft.Type + 1) % Enum.GetValues(typeof(AnnouncementType)).Length;
            session.Draft.Type = (AnnouncementType)next;
            ShowEditor(player);
        }

        [ConsoleCommand("news.editor.settype")]
        private void CmdEditorSetType(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            var session = GetEditor(player);
            if (session == null) return;

            AnnouncementType type;
            if (Enum.TryParse(arg.GetString(0), true, out type)) session.Draft.Type = type;
            ShowEditor(player);
        }

        [ConsoleCommand("news.editor.toggle")]
        private void CmdEditorToggle(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            var session = GetEditor(player);
            if (session == null) return;

            switch (arg.GetString(0).ToLowerInvariant())
            {
                case "pin": session.Draft.Pinned = !session.Draft.Pinned; break;
                case "notify": session.Notify = !session.Notify; break;
                case "discord": session.Discord = !session.Discord && DiscordConfigured; break;
                case "poll": session.Poll = !session.Poll; break;
                case "pollclosed": session.PollClosed = !session.PollClosed; break;
            }
            ShowEditor(player);
        }

        [ConsoleCommand("news.editor.preview")]
        private void CmdEditorPreview(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            var session = GetEditor(player);
            if (session == null) return;

            Dictionary<int, int> unusedMap;
            session.Draft.PollOptions = session.Poll ? BuildPollOptions(session, out unusedMap) : new List<string>();
            session.Draft.PollClosed = session.PollClosed;
            session.Draft.PollVotes = new Dictionary<ulong, int>();
            ShowPopup(player, session.Draft, PopupOrigin.Preview);
        }

        [ConsoleCommand("news.editor.back")]
        private void CmdEditorBack(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (GetEditor(player) == null) return;
            ShowEditor(player);
        }

        [ConsoleCommand("news.editor.save")]
        private void CmdEditorSave(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            var session = GetEditor(player);
            if (session == null) return;

            var draft = session.Draft;
            draft.Title = LimitText(draft.Title, MaxTitleChars).Trim();
            if (string.IsNullOrEmpty(draft.Title))
            {
                SendReply(player, Msg("TitleRequired", player));
                ShowEditor(player);
                return;
            }

            Dictionary<int, int> slotMap = null;
            List<string> pollOptions = null;
            if (session.Poll) pollOptions = BuildPollOptions(session, out slotMap);
            if (session.Poll && pollOptions.Count < 2)
            {
                SendReply(player, Msg("PollNeedsOptions", player));
                ShowEditor(player);
                return;
            }

            Announcement ann;
            if (session.IsNew)
            {
                ann = CreateAnnouncement(draft.Title, draft.ImageUrl, draft.Text, draft.Type, draft.Author ?? player.displayName, draft.Pinned, pollOptions);
                if (session.Discord) SendToDiscord(ann);
            }
            else
            {
                ann = FindById(session.EditingId);
                if (ann == null)
                {
                    SendReply(player, Msg("EditTargetGone", player));
                    editors.Remove(player.userID);
                    ShowAdminList(player, 0);
                    return;
                }

                ann.Title = draft.Title;
                ann.ImageUrl = NormalizeImageUrl(draft.ImageUrl);
                ann.Text = NormalizeInputBody(draft.Text);
                ann.Type = draft.Type;
                ann.Pinned = draft.Pinned;
                ann.EditedDate = Now();
                ApplyPollEdit(ann, session, pollOptions, slotMap);
                SaveAnnouncements();
                RegisterImage(ann.ImageUrl);
                Interface.CallHook("OnNewsEdited", BuildHookData(ann));
            }

            editors.Remove(player.userID);

            int reached = session.Notify ? NotifyPlayers(ann) : 0;
            ShowAdminList(player, 0);

            if (!session.IsNew) SendReply(player, Msg("AnnouncementUpdated", player));
            else if (session.Notify) SendReply(player, Msg("AnnouncementSavedNotified", player, reached));
            else SendReply(player, Msg("PublishedNoNotify", player));
        }

        [ConsoleCommand("news.editor.cancel")]
        private void CmdEditorCancel(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            editors.Remove(player.userID);
            ShowAdminList(player, 0);
        }
        #endregion

        #region Notification UI
        private void DestroyNotification(BasePlayer player)
        {
            string toast;
            if (toastNames.TryGetValue(player.userID, out toast))
            {
                CuiHelper.DestroyUi(player, toast);
                toastNames.Remove(player.userID);
            }
            Timer t;
            if (notificationTimers.TryGetValue(player.userID, out t))
            {
                t?.Destroy();
                notificationTimers.Remove(player.userID);
            }
        }

        private void ShowNotification(BasePlayer player, Announcement ann)
        {
            if (!CanView(player) || ann == null) return;
            string safeTitle = ann.Title ?? string.Empty;

            if (config.Notification.UseNotifyPlugin && Notify != null && Notify.IsLoaded)
            {
                Notify.Call("SendNotify", player.UserIDString, config.Notification.NotifyType, safeTitle);
                return;
            }

            DestroyNotification(player);

            bool isLeft = string.Equals(config.Notification.Position, "left", StringComparison.OrdinalIgnoreCase);
            string anchorMin = isLeft ? "0.01 0.845" : "0.79 0.845";
            string anchorMax = isLeft ? "0.20 0.93" : "0.98 0.93";

            var container = new CuiElementContainer();
            var c = config.Colors;
            string typeColor = GetTypeColor(ann.Type);
            // Every toast gets its own name, so a new one can fade in while the previous one fades out.
            string root = NotificationLayer + "." + (++toastCounter).ToString(CultureInfo.InvariantCulture);
            string card = root + ".Card";

            // Transparent root so the soft shadow can sit behind the card.
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, "Hud", root);
            AddPanelShadow(container, root, "0 0", "1 1", true, RadiusCard);
            AddSurface(container, root, "0 0", "1 1", "0.055 0.064 0.08 1", RadiusCard, $"news.view {ann.Id}", card, PanelRim);
            AddAccentBar(container, card, typeColor, 0f, 7f);

            string kicker = Msg(HasPoll(ann) ? "NewPoll" : "NewAnnouncement", player);
            container.Add(new CuiLabel
            {
                Text = { Text = $"{kicker}  <color={RgbaToHex(typeColor)}>•  {ann.Type.ToString().ToUpper()}</color>", FontSize = 9, Align = TextAnchor.LowerLeft, Color = c.ButtonPrimary, Font = FontBold },
                RectTransform = { AnchorMin = "0.06 0.64", AnchorMax = "0.88 0.92" }
            }, card);

            container.Add(new CuiLabel
            {
                Text = { Text = Ellipsize(safeTitle, 28), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                RectTransform = { AnchorMin = "0.06 0.30", AnchorMax = "0.97 0.66" }
            }, card);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("ClickToRead", player), FontSize = 8, Align = TextAnchor.UpperLeft, Color = c.TextMuted, Font = FontRegular },
                RectTransform = { AnchorMin = "0.06 0.06", AnchorMax = "0.9 0.30" }
            }, card);

            container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "news.close.notif" },
                Text = { Text = "✕", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.89 0.6", AnchorMax = "0.98 0.94" }
            }, card);

            ApplyFadeRamp(container, 0, 0.22f, 0.45f);
            foreach (var element in container) element.FadeOut = 0.35f;
            CuiHelper.AddUi(player, container);
            toastNames[player.userID] = root;
            PlaySound(player);

            ulong userId = player.userID;
            notificationTimers[userId] = timer.Once(config.Notification.Duration, () =>
            {
                notificationTimers.Remove(userId);
                string current;
                if (toastNames.TryGetValue(userId, out current) && current == root)
                {
                    toastNames.Remove(userId);
                    if (player != null && player.IsConnected) CuiHelper.DestroyUi(player, root);
                }
            });
        }
        #endregion

        #region UI Generation
        private void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, LayerName);
            CuiHelper.DestroyUi(player, ConfirmLayer);
            playersWithUiOpen.Remove(player.userID);
            currentScreens.Remove(player.userID);
            openPopups.Remove(player.userID);
            CancelAutoClose(player.userID);
            CancelReadRewardTimer(player.userID);
        }

        // The player is interacting with a broadcast popup: keep it open and remove the countdown.
        private void StopAutoClose(BasePlayer player)
        {
            if (!autoCloseTimers.ContainsKey(player.userID)) return;
            CancelAutoClose(player.userID);
            CuiHelper.DestroyUi(player, AutoCloseName);
        }

        private void CancelAutoClose(ulong userId)
        {
            Timer t;
            if (autoCloseTimers.TryGetValue(userId, out t))
            {
                t?.Destroy();
                autoCloseTimers.Remove(userId);
            }
        }

        // Animation. Rust CUI fades graphics in when they appear (FadeIn) and out when destroyed (FadeOut);
        // both run on the client, so they are smooth and cost no extra network traffic.

        // Opens a full-screen view. Returns the animation strength: 1 when switching to a new view,
        // sameScreen when redrawing the view that is already open (0 = instant redraw).
        private float OpenScreen(BasePlayer player, UiScreen screen, float sameScreen, out bool fresh)
        {
            UiScreen previous;
            currentScreens.TryGetValue(player.userID, out previous);
            DestroyUI(player);
            currentScreens[player.userID] = screen;
            playersWithUiOpen.Add(player.userID);
            fresh = previous == UiScreen.None;
            return previous == screen ? sameScreen : 1f;
        }

        // Staggered reveal: elements added later fade in a little slower, so a screen builds up
        // top-to-bottom (header, content, footer; archive rows one after another).
        // Elements that already have an explicit fade keep it.
        private static void ApplyFadeRamp(CuiElementContainer container, int fromIndex, float first, float last)
        {
            if (last <= 0f) return;
            int span = Math.Max(1, container.Count - fromIndex - 1);
            for (int i = fromIndex; i < container.Count; i++)
            {
                float seconds = first + (last - first) * (i - fromIndex) / span;
                foreach (var component in container[i].Components)
                {
                    var image = component as CuiImageComponent;
                    if (image != null) { if (image.FadeIn <= 0f) image.FadeIn = seconds; continue; }
                    var raw = component as CuiRawImageComponent;
                    if (raw != null) { if (raw.FadeIn <= 0f) raw.FadeIn = seconds; continue; }
                    var text = component as CuiTextComponent;
                    if (text != null) { if (text.FadeIn <= 0f) text.FadeIn = seconds; continue; }
                    var button = component as CuiButtonComponent;
                    if (button != null && button.FadeIn <= 0f) button.FadeIn = seconds;
                }
            }
        }

        private void AddBackdrop(CuiElementContainer container, string color, float fade = 0f)
        {
            // Dimmed + blurred game view behind every full-screen panel. It only fades in when nothing
            // was open before, so moving between screens never flashes the game view.
            container.Add(new CuiPanel
            {
                Image = { Color = color, Material = BlurMaterial, FadeIn = fade },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName);
        }

        // ---- Rounded corners ---------------------------------------------------------------
        // CUI has no rounded rectangles. Small anti-aliased quarter-circle textures are drawn in code at
        // startup and stored in the server's FileStorage (the same store ImageLibrary uses). A rounded
        // rectangle is then three non-overlapping bands plus four corner pieces, so translucent colours stay
        // even. If the textures are unavailable (or RoundedCorners is off) the helpers draw plain rectangles.
        private bool Rounded => cornerFill != null && config.General.RoundedCorners;

        private void GenerateCornerTextures()
        {
            cornerFill = null;
            cornerMask = null;
            if (!config.General.RoundedCorners) return;
            try
            {
                var entity = CommunityEntity.ServerInstance;
                if (entity == null || entity.net == null) return;
                var fill = new string[4];
                var mask = new string[4];
                for (int corner = 0; corner < 4; corner++)
                {
                    fill[corner] = FileStorage.server.Store(BuildCornerPng(corner, false), FileStorage.Type.png, entity.net.ID).ToString();
                    mask[corner] = FileStorage.server.Store(BuildCornerPng(corner, true), FileStorage.Type.png, entity.net.ID).ToString();
                }
                cornerFill = fill;
                cornerMask = mask;
            }
            catch (Exception ex)
            {
                cornerFill = null;
                cornerMask = null;
                PrintWarning($"Rounded corners are disabled (could not store the corner textures): {ex.Message}");
            }
        }

        // Quarter circle for corner 0=TL, 1=TR, 2=BL, 3=BR: white with 4x4 supersampled alpha.
        // inverse = white outside the circle (used to round off the corners of pictures).
        private static byte[] BuildCornerPng(int corner, bool inverse)
        {
            int n = CornerTextureSize;
            float cx = corner == 0 || corner == 2 ? n : 0f;
            float cy = corner == 0 || corner == 1 ? n : 0f;   // image rows run top to bottom
            var rgba = new byte[n * n * 4];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float dx = x + (sx + 0.5f) / 4f - cx, dy = y + (sy + 0.5f) / 4f - cy;
                            if (dx * dx + dy * dy <= n * n) inside++;
                        }
                    float alpha = inside / 16f;
                    if (inverse) alpha = 1f - alpha;
                    int k = (y * n + x) * 4;
                    rgba[k] = 255; rgba[k + 1] = 255; rgba[k + 2] = 255;
                    rgba[k + 3] = (byte)Mathf.RoundToInt(alpha * 255f);
                }
            }
            return EncodePng(n, n, rgba);
        }

        // Minimal PNG writer (RGBA, zlib "stored" blocks). Plugins can't use System.IO, so it works on byte lists.
        private static byte[] EncodePng(int width, int height, byte[] rgba)
        {
            var raw = new byte[height * (width * 4 + 1)];
            for (int y = 0; y < height; y++)
            {
                raw[y * (width * 4 + 1)] = 0;
                Array.Copy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
            }

            var zlib = new List<byte>(raw.Length + 64) { 0x78, 0x01 };
            int pos = 0;
            do
            {
                int len = Math.Min(65535, raw.Length - pos);
                zlib.Add((byte)(pos + len >= raw.Length ? 1 : 0));
                zlib.Add((byte)(len & 0xFF)); zlib.Add((byte)(len >> 8));
                zlib.Add((byte)(~len & 0xFF)); zlib.Add((byte)((~len >> 8) & 0xFF));
                for (int i = 0; i < len; i++) zlib.Add(raw[pos + i]);
                pos += len;
            }
            while (pos < raw.Length);
            uint a = 1, b = 0;
            foreach (byte value in raw) { a = (a + value) % 65521; b = (b + a) % 65521; }
            AddUInt32(zlib, (b << 16) | a);

            var png = new List<byte> { 137, 80, 78, 71, 13, 10, 26, 10 };
            var header = new List<byte>();
            AddUInt32(header, (uint)width);
            AddUInt32(header, (uint)height);
            header.AddRange(new byte[] { 8, 6, 0, 0, 0 });
            AddPngChunk(png, "IHDR", header);
            AddPngChunk(png, "IDAT", zlib);
            AddPngChunk(png, "IEND", new List<byte>());
            return png.ToArray();
        }

        private static void AddUInt32(List<byte> bytes, uint value)
        {
            bytes.Add((byte)(value >> 24)); bytes.Add((byte)(value >> 16)); bytes.Add((byte)(value >> 8)); bytes.Add((byte)value);
        }

        private static void AddPngChunk(List<byte> png, string type, List<byte> data)
        {
            AddUInt32(png, (uint)data.Count);
            var body = new List<byte>(data.Count + 4);
            foreach (char ch in type) body.Add((byte)ch);
            body.AddRange(data);
            png.AddRange(body);
            uint crc = 0xFFFFFFFF;
            foreach (byte value in body)
            {
                crc ^= value;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
            AddUInt32(png, crc ^ 0xFFFFFFFF);
        }

        private static CuiPanel Piece(string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax, float fade)
        {
            var panel = new CuiPanel { Image = { Color = color, FadeIn = fade } };
            panel.RectTransform.AnchorMin = anchorMin;
            panel.RectTransform.AnchorMax = anchorMax;
            panel.RectTransform.OffsetMin = offsetMin;
            panel.RectTransform.OffsetMax = offsetMax;
            return panel;
        }

        // Rounded rectangle filling its parent, inset by `inset` units (negative = larger than the parent).
        private void AddRoundedFill(CuiElementContainer container, string parent, string color, float radius, float inset = 0f, float fade = 0f)
        {
            string a = F(inset), na = F(-inset);
            if (!Rounded || radius < 0.5f)
            {
                container.Add(Piece(color, "0 0", "1 1", $"{a} {a}", $"{na} {na}", fade), parent);
                return;
            }

            string b = F(inset + radius), nb = F(-(inset + radius));
            container.Add(Piece(color, "0 0", "1 1", $"{b} {a}", $"{nb} {na}", fade), parent);
            container.Add(Piece(color, "0 0", "0 1", $"{a} {b}", $"{b} {nb}", fade), parent);
            container.Add(Piece(color, "1 0", "1 1", $"{nb} {b}", $"{na} {nb}", fade), parent);
            AddCorners(container, parent, cornerFill, color, a, na, b, nb, fade);
        }

        private static void AddCorners(CuiElementContainer container, string parent, string[] textures, string color, string a, string na, string b, string nb, float fade)
        {
            string[] anchors = { "0 1", "1 1", "0 0", "1 0" };
            string[] mins = { $"{a} {nb}", $"{nb} {nb}", $"{a} {a}", $"{nb} {a}" };
            string[] maxs = { $"{b} {na}", $"{na} {na}", $"{b} {b}", $"{na} {b}" };
            for (int k = 0; k < 4; k++)
            {
                container.Add(new CuiElement
                {
                    Parent = parent,
                    Components =
                    {
                        new CuiRawImageComponent { Png = textures[k], Color = color, FadeIn = fade },
                        new CuiRectTransformComponent { AnchorMin = anchors[k], AnchorMax = anchors[k], OffsetMin = mins[k], OffsetMax = maxs[k] }
                    }
                });
            }
        }

        // Rounds off the corners of a picture by covering them with the colour behind it.
        private void AddCornerMasks(CuiElementContainer container, string parent, string backgroundColor, float radius)
        {
            if (!Rounded) return;
            AddCorners(container, parent, cornerMask, backgroundColor, "0", "0", F(radius), F(-radius), 0f);
        }

        // Rounded card / button. Content added to the returned element draws on top of the fill.
        // rim: a border colour drawn just outside the fill (use with opaque fills).
        private string AddSurface(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string color, float radius, string command = null, string name = null, string rim = null, float rimWidth = 1f)
        {
            string holder = command != null
                ? container.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = command }, Text = { Text = "" }, RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax } }, parent, name)
                : container.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax } }, parent, name);
            if (rim != null) AddRoundedFill(container, holder, rim, radius + rimWidth, -rimWidth);
            AddRoundedFill(container, holder, color, radius);
            return holder;
        }

        // Faint light line just inside the top edge of a panel (a "glass" highlight).
        private void AddTopSheen(CuiElementContainer container, string parent, float radius)
        {
            container.Add(Piece("1 1 1 0.07", "0 1", "1 1", $"{F(radius)} -2", $"{F(-radius)} -1", 0f), parent);
        }

        private string AddLabelIn(CuiElementContainer container, string parent, string text, int fontSize, string color, TextAnchor align = TextAnchor.MiddleCenter, string font = FontBold)
        {
            return container.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = fontSize, Align = align, Color = color, Font = font },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, parent);
        }

        // Rounded label ("pill"): type tags, badges, counters.
        private string AddPill(CuiElementContainer container, string parent, string text, string anchorMin, string anchorMax, string background, string textColor, int fontSize = 9)
        {
            string pill = AddSurface(container, parent, anchorMin, anchorMax, background, RadiusPill);
            AddLabelIn(container, pill, text, fontSize, textColor);
            return pill;
        }

        // Filled rounded button for the main action of a screen.
        private string AddPrimaryButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string color, string textColor = "1 1 1 1", int fontSize = 11, string name = null)
        {
            string button = AddSurface(container, parent, anchorMin, anchorMax, color, RadiusButton, command, name);
            AddLabelIn(container, button, text, fontSize, textColor);
            return button;
        }

        // Opaque version of a colour as seen on top of `under` (both "r g b a").
        private static string Blend(string top, string under)
        {
            float[] t = ParseColor(top), u = ParseColor(under);
            float a = t[3];
            return $"{F(t[0] * a + u[0] * (1 - a))} {F(t[1] * a + u[1] * (1 - a))} {F(t[2] * a + u[2] * (1 - a))} 1";
        }

        private static string Opaque(string rgba)
        {
            float[] c = ParseColor(rgba);
            return $"{F(c[0])} {F(c[1])} {F(c[2])} 1";
        }

        private static float[] ParseColor(string rgba)
        {
            var result = new float[] { 1f, 1f, 1f, 1f };
            var parts = (rgba ?? string.Empty).Split(' ');
            for (int i = 0; i < 4 && i < parts.Length; i++)
            {
                float value;
                if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value)) result[i] = value;
            }
            return result;
        }

        // Soft rounded shadow (three layers) around a panel. Add it before the panel itself.
        private void AddPanelShadow(CuiElementContainer container, string parent, string anchorMin, string anchorMax, bool small = false, float radius = RadiusPanel)
        {
            float[] spreads = small ? new[] { 6f, 2.5f } : new[] { 18f, 9f, 3.5f };
            string[] colors = small ? new[] { "0 0 0 0.10", "0 0 0 0.20" } : new[] { "0 0 0 0.06", "0 0 0 0.11", "0 0 0 0.20" };
            for (int i = 0; i < spreads.Length; i++)
            {
                string layer = container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = $"{F(-spreads[i])} {F(-spreads[i])}", OffsetMax = $"{F(spreads[i])} {F(spreads[i])}" }
                }, parent);
                AddRoundedFill(container, layer, colors[i], radius + spreads[i]);
            }
        }

        // 1-unit horizontal divider.
        private void AddHairline(CuiElementContainer container, string parent, float y, float left = 0f, float right = 1f)
        {
            container.Add(new CuiPanel { Image = { Color = Hairline }, RectTransform = { AnchorMin = Anchor(left, y), AnchorMax = Anchor(right, y), OffsetMin = "0 0", OffsetMax = "0 1" } }, parent);
        }

        // Soft rounded secondary button.
        private string AddGhostButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string textColor, int fontSize = 10, string name = null)
        {
            string button = AddSurface(container, parent, anchorMin, anchorMax, Ghost, RadiusButton, command, name);
            AddLabelIn(container, button, text, fontSize, textColor);
            return button;
        }

        // Same colour with a different alpha ("r g b a").
        private static string MixAlpha(string rgba, float alpha)
        {
            var parts = (rgba ?? string.Empty).Split(' ');
            if (parts.Length < 3) return $"1 1 1 {F(alpha)}";
            return $"{parts[0]} {parts[1]} {parts[2]} {F(alpha)}";
        }

        private void AddLikeButton(CuiElementContainer container, BasePlayer player, Announcement ann)
        {
            bool liked = ann.LikedPlayers.Contains(player.userID);
            string button = container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = $"news.like {ann.Id}" },
                Text = { Text = "" },
                RectTransform = { AnchorMin = "0.03 0.028", AnchorMax = "0.135 0.082" }
            }, PopupPanel, LikeButtonName);
            // Pops in (explicit fade) whenever it is redrawn after a like.
            AddRoundedFill(container, button, liked ? "0.90 0.30 0.35 0.16" : Ghost, RadiusButton, 0f, 0.25f);
            container.Add(new CuiLabel
            {
                Text = { Text = $"❤  {ann.LikedPlayers.Count}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = liked ? "0.96 0.40 0.44 1" : "0.62 0.63 0.68 1", Font = FontBold, FadeIn = 0.25f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, button);
        }

        private void ShowPopup(BasePlayer player, Announcement ann, PopupOrigin origin, bool playSound = false)
        {
            if (!CanView(player) || ann == null) return;
            bool preview = origin == PopupOrigin.Preview;

            bool fresh;
            float anim = OpenScreen(player, UiScreen.Popup, 0.5f, out fresh);
            DestroyNotification(player);
            if (!preview) openPopups[player.userID] = new PopupState { AnnId = ann.Id, Origin = origin };

            var container = new CuiElementContainer();
            var c = config.Colors;
            string accentHex = RgbaToHex(c.ButtonPrimary);
            string typeColor = GetTypeColor(ann.Type);

            AddBackdrop(container, "0 0 0 0.62", fresh ? 0.25f : 0f);
            int contentStart = container.Count;
            AddPanelShadow(container, LayerName, "0.175 0.175", "0.825 0.825");

            // Rounded panel with a faint rim (gold for pinned posts) and a glass highlight along the top.
            string panelColor = Opaque(c.PanelBg);
            string mainPanel = AddSurface(container, LayerName, "0.175 0.175", "0.825 0.825", panelColor, RadiusPanel, null, PopupPanel,
                ann.Pinned ? PinGold : PanelRim, ann.Pinned ? 1.5f : 1f);
            AddTopSheen(container, mainPanel, RadiusPanel);

            // Header: type pill (+ POLL chip) and server name on the left, navigation and close on the right.
            float pillLeft = 0.03f;
            pillLeft = AddHeaderPill(container, mainPanel, ann.Type.ToString().ToUpper(), typeColor, "1 1 1 1", pillLeft);
            if (HasPoll(ann))
                pillLeft = AddHeaderPill(container, mainPanel, Msg("PollLabel", player), MixAlpha(c.ButtonPrimary, 0.22f), c.ButtonPrimary, pillLeft);
            container.Add(new CuiLabel
            {
                Text = { Text = config.General.ServerName, FontSize = 12, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = Anchor(pillLeft + 0.004f, 0.91f), AnchorMax = "0.70 0.99" }
            }, mainPanel);

            if (ann.Pinned)
                AddPill(container, mainPanel, Msg("PinnedBadge", player), "0.755 0.928", "0.84 0.972", PinGold, PinGoldText, 10);

            if (preview)
                AddPill(container, mainPanel, Msg("PreviewBadge", player), "0.852 0.928", "0.938 0.972", c.ButtonPrimary, "1 1 1 1", 10);
            else
            {
                AddPopupNavigation(container, player, ann, origin, mainPanel, c);
            }

            AddGhostButton(container, mainPanel, "✕", preview ? "news.editor.back" : "news.close", "0.947 0.928", "0.972 0.972", c.TextNormal, 11);
            AddHairline(container, mainPanel, 0.905f, 0.03f, 0.97f);

            // Image column: a framed card; the picture fills it.
            bool hasImage = !string.IsNullOrEmpty(ann.ImageUrl);
            float contentLeft = hasImage ? 0.5f : 0.03f;

            if (hasImage)
            {
                bool wide = config.General.PopupImageFrame == "Wide";
                // "Wide" keeps a 16:9 frame (no stretching of widescreen images); "Tall" fills the column.
                float frameBottom = wide ? 0.29f : BodyBottomDefault;
                float frameTop = wide ? 0.73f : 0.885f;
                string imgPanel = mainPanel + ".Img";

                // Picture with rounded corners: drawn square, then the corners are masked with the panel colour.
                AddSurface(container, mainPanel, Anchor(0.03f, frameBottom), Anchor(0.47f, frameTop), Blend("0 0 0 0.35", panelColor), RadiusCard, null, imgPanel);
                container.Add(new CuiElement
                {
                    Parent = imgPanel,
                    Components =
                    {
                        BuildRawImage(ann.ImageUrl),
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                });
                AddCornerMasks(container, imgPanel, panelColor, RadiusCard);
            }

            // Title, then "posted by" line: the font shrinks for long titles so they wrap instead of being cut off.
            string titleText = (string.IsNullOrEmpty(ann.Title) ? Msg("NoTitle", player) : ann.Title).ToUpper();
            int titleSize = PickTitleFontSize(titleText, (0.97f - contentLeft) * PopupWidthUnits, 0.13f * PopupHeightUnits);
            container.Add(new CuiElement
            {
                Name = mainPanel + ".TitleText",
                Parent = mainPanel,
                Components =
                {
                    new CuiTextComponent { Text = titleText, FontSize = titleSize, Align = TextAnchor.LowerLeft, Color = c.TextTitle, Font = FontBold },
                    new CuiOutlineComponent { Color = "0 0 0 0.45", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = Anchor(contentLeft, 0.755f), AnchorMax = "0.97 0.885" }
                }
            });

            string author = string.IsNullOrEmpty(ann.Author) ? Msg("Unknown", player) : ann.Author;
            string meta = $"{Msg("PostedBy", player)} <color={accentHex}>{author.ToUpper()}</color>   •   {ann.Date}";
            if (!string.IsNullOrEmpty(ann.EditedDate)) meta += $"   •   {Msg("EditedTag", player)}";
            container.Add(new CuiLabel
            {
                Text = { Text = meta, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = FontRegular },
                RectTransform = { AnchorMin = Anchor(contentLeft, 0.722f), AnchorMax = "0.97 0.752" }
            }, mainPanel);

            // Poll block sits under the body and shortens it.
            float bodyBottom = BodyBottomDefault;
            if (HasPoll(ann))
            {
                float pollBottom;
                PollLayout(ann, out bodyBottom, out pollBottom);
                AddPollBlock(container, player, ann, preview);
            }

            // Body text sits directly on the panel, aligned with the title, in a native scroll view.
            // Content height is estimated from wrapped line count.
            string bodyText = ann.Text ?? string.Empty;
            int wrap = hasImage ? BodyWrapWithImage : BodyWrapFullWidth;
            int estLines = BuildBodyDisplayLines(bodyText, wrap).Count + 1;
            float viewportUnits = (BodyTop - bodyBottom) * PopupHeightUnits;
            int extra = Mathf.Max(0, Mathf.CeilToInt(estLines * BodyLineUnits - viewportUnits));

            string bodyScroll = mainPanel + ".Body";
            container.Add(new CuiElement
            {
                Name = bodyScroll,
                Parent = mainPanel,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = Anchor(contentLeft, bodyBottom), AnchorMax = Anchor(0.97f, BodyTop) },
                    new CuiScrollViewComponent
                    {
                        Horizontal = false,
                        Vertical = true,
                        MovementType = UnityEngine.UI.ScrollRect.MovementType.Clamped,
                        Inertia = true,
                        DecelerationRate = 0.1f,
                        ScrollSensitivity = 26f,
                        ContentTransform = new CuiRectTransform { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 -{extra}", OffsetMax = "0 0" },
                        VerticalScrollbar = new CuiScrollbar { Size = 4f, AutoHide = true, HandleColor = c.ButtonPrimary, HighlightColor = c.ButtonPrimary, PressedColor = c.ButtonPrimary, TrackColor = "1 1 1 0.04", HandleSprite = "assets/content/ui/ui.background.tile.psd", TrackSprite = "assets/content/ui/ui.background.tile.psd" }
                    }
                }
            });

            container.Add(new CuiLabel
            {
                Text = { Text = bodyText, FontSize = 15, Align = TextAnchor.UpperLeft, Color = c.TextNormal, Font = FontRegular },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.97 0.985" }
            }, bodyScroll);

            // Footer: like on the left, main action on the right.
            AddHairline(container, mainPanel, 0.105f, 0.03f, 0.97f);

            if (preview)
            {
                string likePreview = AddSurface(container, mainPanel, "0.03 0.028", "0.135 0.082", Ghost, RadiusButton);
                AddLabelIn(container, likePreview, $"❤  {ann.LikedPlayers?.Count ?? 0}", 12, "0.62 0.63 0.68 0.6");
            }
            else if (!string.IsNullOrEmpty(ann.Id))
            {
                AddLikeButton(container, player, ann);
            }

            string footerCommand, footerText;
            if (preview) { footerCommand = "news.editor.back"; footerText = Msg("BackToEditor", player); }
            else if (origin == PopupOrigin.Archive) { footerCommand = $"news.page {GetArchivePage(player.userID)}"; footerText = Msg("BackToArchive", player); }
            else { footerCommand = "news.page 0"; footerText = Msg("ViewArchive", player); }

            AddPrimaryButton(container, mainPanel, footerText, footerCommand, "0.78 0.028", "0.97 0.082", c.ButtonPrimary);

            if (origin == PopupOrigin.Broadcast && config.General.EnableAutoClose)
            {
                // Live "closes in 12s" countdown, counted down by the client itself.
                container.Add(new CuiElement
                {
                    Name = AutoCloseName,
                    Parent = mainPanel,
                    Components =
                    {
                        new CuiTextComponent { Text = Msg("AutoCloseIn", player), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = FontBold },
                        new CuiCountdownComponent { StartTime = config.General.AutoCloseSeconds, EndTime = 0, Step = 1 },
                        new CuiRectTransformComponent { AnchorMin = "0.35 0.028", AnchorMax = "0.65 0.082" }
                    }
                });
            }

            ApplyFadeRamp(container, contentStart, 0.16f * anim, 0.62f * anim);
            CuiHelper.AddUi(player, container);

            if (playSound) PlaySound(player);

            if (origin == PopupOrigin.Broadcast && config.General.EnableAutoClose)
            {
                ulong userId = player.userID;
                CancelAutoClose(userId);
                autoCloseTimers[userId] = timer.Once(config.General.AutoCloseSeconds, () =>
                {
                    autoCloseTimers.Remove(userId);
                    PopupState state;
                    if (player != null && player.IsConnected && openPopups.TryGetValue(userId, out state) && state.AnnId == ann.Id)
                        DestroyUI(player);
                });
            }

            if (!preview) ScheduleReadCompletion(player, ann);
        }

        // Small filled label in the popup header; returns the x where the next item can start.
        private float AddHeaderPill(CuiElementContainer container, string parent, string text, string background, string textColor, float left)
        {
            float width = (text.Length * 5.4f + 20f) / PopupWidthUnits;
            AddPill(container, parent, text, Anchor(left, 0.928f), Anchor(left + width, 0.972f), background, textColor, 10);
            return left + width + 0.008f;
        }

        private static float PollBlockHeight(Announcement ann)
        {
            int n = ann.PollOptions.Count;
            return 0.05f + n * 0.056f + (n - 1) * 0.01f + 0.014f;
        }

        // With a poll the body box only grows as tall as its text needs (the poll follows right under it);
        // long text still gets all the room down to the poll at the bottom.
        private static void PollLayout(Announcement ann, out float bodyBottom, out float pollBottom)
        {
            float pollH = PollBlockHeight(ann);
            int wrap = string.IsNullOrEmpty(ann.ImageUrl) ? BodyWrapFullWidth : BodyWrapWithImage;
            float needed = Math.Max(0.09f, (BuildBodyDisplayLines(ann.Text ?? string.Empty, wrap).Count * BodyLineUnits + 16f) / PopupHeightUnits);
            bodyBottom = Math.Max(BodyBottomDefault + pollH + 0.018f, BodyTop - needed);
            pollBottom = bodyBottom - 0.018f - pollH;
        }

        // The poll lives in its own named panel so a vote can redraw just this block.
        private void AddPollBlock(CuiElementContainer container, BasePlayer player, Announcement ann, bool preview)
        {
            var c = config.Colors;
            float left = string.IsNullOrEmpty(ann.ImageUrl) ? 0.03f : 0.5f;
            float h = PollBlockHeight(ann);
            float bodyBottom, pollBottom;
            PollLayout(ann, out bodyBottom, out pollBottom);

            AddSurface(container, PopupPanel, Anchor(left, pollBottom), Anchor(0.97f, pollBottom + h), "1 1 1 0.03", RadiusCard, null, PollPanelName);
            float rowWidthUnits = (0.97f - left) * PopupWidthUnits * 0.95f;

            ulong userId = player.userID;
            int myVote = preview ? -1 : VoteOf(ann, userId);
            int[] counts = CountVotes(ann);
            int total = counts.Sum();
            bool canVote = !preview && !ann.PollClosed && (myVote < 0 || config.Polls.AllowVoteChange);
            bool showResults = !preview && (myVote >= 0 || ann.PollClosed || config.Polls.ShowResultsBeforeVoting || CanAdmin(player));

            float headBottom = 1f - 0.05f / h;
            container.Add(new CuiLabel
            {
                Text = { Text = $"<color={RgbaToHex(c.ButtonPrimary)}>{Msg("PollLabel", player)}</color>   {Msg("PollVotes", player, total)}", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = Anchor(0.03f, headBottom), AnchorMax = "0.55 1" }
            }, PollPanelName);

            string hint = ann.PollClosed ? Msg("PollClosedLabel", player)
                : myVote >= 0 ? Msg(config.Polls.AllowVoteChange ? "PollVotedChange" : "PollVoted", player)
                : Msg("PollClickToVote", player);
            container.Add(new CuiLabel
            {
                Text = { Text = hint, FontSize = 9, Align = TextAnchor.MiddleRight, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = Anchor(0.45f, headBottom), AnchorMax = "0.975 1" }
            }, PollPanelName);

            for (int i = 0; i < ann.PollOptions.Count; i++)
            {
                bool mine = i == myVote;
                float rowTop = 1f - (0.05f + i * 0.066f) / h;
                float rowBottom = rowTop - 0.056f / h;
                string row = PollPanelName + ".Opt" + i;
                string background = mine ? MixAlpha(c.ButtonPrimary, 0.12f) : "1 1 1 0.04";
                string rowMin = Anchor(0.025f, rowBottom), rowMax = Anchor(0.975f, rowTop);

                AddSurface(container, PollPanelName, rowMin, rowMax, background, RadiusButton, canVote && !mine ? $"news.vote {ann.Id} {i}" : null, row);

                float share = total > 0 ? (float)counts[i] / total : 0f;
                if (showResults && share > 0f)
                {
                    // Rounded result bar (at least as wide as its rounded ends); fades in after voting.
                    float width = Math.Min(1f, Math.Max(share, RadiusButton * 2f / rowWidthUnits));
                    string bar = container.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0 0", AnchorMax = Anchor(width, 1f) } }, row);
                    AddRoundedFill(container, bar, MixAlpha(c.ButtonPrimary, mine ? 0.45f : 0.2f), RadiusButton, 0f, 0.5f);
                }

                container.Add(new CuiLabel
                {
                    Text = { Text = (mine ? "✓  " : "") + ann.PollOptions[i], FontSize = 12, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = mine ? FontBold : FontRegular },
                    RectTransform = { AnchorMin = "0.025 0", AnchorMax = "0.80 1" }
                }, row);

                if (showResults)
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"{Mathf.RoundToInt(share * 100f)}%  <color={RgbaToHex(c.TextMuted)}>({counts[i]})</color>", FontSize = 11, Align = TextAnchor.MiddleRight, Color = c.TextTitle, Font = FontBold },
                        RectTransform = { AnchorMin = "0.70 0", AnchorMax = "0.975 1" }
                    }, row);
                }
            }
        }

        private void AddPopupNavigation(CuiElementContainer container, BasePlayer player, Announcement ann, PopupOrigin origin, string mainPanel, UIColors c)
        {
            var order = GetDisplayOrder();
            int index = order.FindIndex(a => a.Id == ann.Id);
            if (index < 0 || order.Count < 2) return;

            string originArg = origin == PopupOrigin.Archive ? "archive" : "direct";
            string prevCmd = index > 0 ? $"news.view {order[index - 1].Id} {originArg}" : null;
            string nextCmd = index < order.Count - 1 ? $"news.view {order[index + 1].Id} {originArg}" : null;

            AddNavArrow(container, mainPanel, "<", prevCmd, "0.852 0.928", "0.877 0.972", c);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("PositionOf", player, index + 1, order.Count), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = "0.877 0.91", AnchorMax = "0.913 0.99" }
            }, mainPanel);

            AddNavArrow(container, mainPanel, ">", nextCmd, "0.913 0.928", "0.938 0.972", c);
        }

        private void AddNavArrow(CuiElementContainer container, string parent, string glyph, string command, string anchorMin, string anchorMax, UIColors c)
        {
            if (command == null)
            {
                // At the first/last post: drawn dimmed and not clickable.
                string box = AddSurface(container, parent, anchorMin, anchorMax, "1 1 1 0.02", RadiusButton);
                AddLabelIn(container, box, glyph, 12, "1 1 1 0.15");
                return;
            }

            AddGhostButton(container, parent, glyph, command, anchorMin, anchorMax, c.TextTitle, 12);
        }

        private int GetArchivePage(ulong userId)
        {
            int page;
            return archivePages.TryGetValue(userId, out page) ? page : 0;
        }

        private CuiRawImageComponent BuildRawImage(string url)
        {
            var img = new CuiRawImageComponent { Color = "1 1 1 1" };
            string imgId = GetImage(url);
            if (!string.IsNullOrEmpty(imgId))
                img.Png = imgId;
            else
                img.Url = url;
            return img;
        }

        private void ShowHistory(BasePlayer player, int page)
        {
            if (!CanView(player)) return;

            if (announcements.Count == 0)
            {
                DestroyUI(player);
                SendReply(player, Msg("NoNewsHistory", player));
                return;
            }

            bool fresh;
            float anim = OpenScreen(player, UiScreen.Archive, 0.6f, out fresh);

            var container = new CuiElementContainer();
            var c = config.Colors;
            ulong userId = player.userID;

            var displayList = GetDisplayOrder();
            int perPage = config.General.AnnouncementsPerPage;
            int totalPages = Math.Max(1, Mathf.CeilToInt((float)displayList.Count / perPage));
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;
            archivePages[userId] = page;

            AddBackdrop(container, "0 0 0 0.62", fresh ? 0.25f : 0f);
            int contentStart = container.Count;
            AddPanelShadow(container, LayerName, "0.15 0.1", "0.85 0.9");

            string panelColor = Opaque(c.PanelBg);
            string mainPanel = AddSurface(container, LayerName, "0.15 0.1", "0.85 0.9", panelColor, RadiusPanel, null, LayerName + ".List", PanelRim);
            AddTopSheen(container, mainPanel, RadiusPanel);

            // Header
            container.Add(new CuiLabel
            {
                Text = { Text = $"{Msg("ArchiveTitle", player)}  <color={RgbaToHex(c.TextMuted)}>/  {config.General.ServerName}</color>", FontSize = 17, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                RectTransform = { AnchorMin = "0.025 0.91", AnchorMax = "0.6 0.99" }
            }, mainPanel);

            int unread = CountUnread(userId);
            if (unread > 0)
            {
                AddGhostButton(container, mainPanel, Msg("MarkAllRead", player), "news.markread", "0.655 0.927", "0.79 0.973", c.TextNormal, 10);
                AddPill(container, mainPanel, Msg("UnreadCount", player, unread), "0.80 0.927", "0.928 0.973", c.ButtonPrimary, "1 1 1 1", 10);
            }

            AddGhostButton(container, mainPanel, "✕", "news.close", "0.945 0.927", "0.975 0.973", c.TextNormal, 11);
            AddHairline(container, mainPanel, 0.905f, 0.025f, 0.975f);

            // Rows: the whole card is the button.
            int start = page * perPage;
            int count = 0;
            float rowHeight = 0.78f / perPage;
            float padding = 0.009f;

            for (int i = start; i < displayList.Count && count < perPage; i++)
            {
                var ann = displayList[i];
                bool isUnread = IsUnread(ann, userId);
                float top = 0.89f - (count * rowHeight) - padding;
                float bottom = top - rowHeight + (padding * 2);

                string itemPanel = mainPanel + $".{i}";
                string typeColor = GetTypeColor(ann.Type);

                // Rounded, clickable card. Colours are blended to opaque so rims and picture corners stay crisp.
                string rowColor = Blend(c.ContentBg, panelColor);
                if (ann.Pinned) rowColor = Blend("0.95 0.70 0.20 0.07", rowColor);
                else if (isUnread) rowColor = Blend("1 1 1 0.025", rowColor);
                string rim = ann.Pinned ? "0.95 0.70 0.20 0.40" : isUnread ? MixAlpha(c.ButtonPrimary, 0.45f) : null;
                AddSurface(container, mainPanel, Anchor(0.025f, bottom), Anchor(0.975f, top), rowColor, RadiusCard, $"news.view {ann.Id} archive", itemPanel, rim);
                AddAccentBar(container, itemPanel, typeColor);

                float textLeft = 0.03f;
                if (config.General.ArchiveThumbnails && !string.IsNullOrEmpty(ann.ImageUrl))
                {
                    string thumb = itemPanel + ".Thumb";
                    AddSurface(container, itemPanel, "0.022 0.13", "0.127 0.87", Blend("0 0 0 0.45", rowColor), RadiusButton, null, thumb);
                    container.Add(new CuiElement
                    {
                        Parent = thumb,
                        Components = { BuildRawImage(ann.ImageUrl), new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } }
                    });
                    AddCornerMasks(container, thumb, rowColor, RadiusButton);
                    textLeft = 0.142f;
                }

                string title = Ellipsize((string.IsNullOrEmpty(ann.Title) ? Msg("NoTitle", player) : ann.Title).ToUpper(), textLeft > 0.1f ? 40 : 52);
                container.Add(new CuiLabel
                {
                    Text = { Text = title, FontSize = 13, Align = TextAnchor.MiddleLeft, Color = isUnread ? c.TextTitle : c.TextNormal, Font = FontBold },
                    RectTransform = { AnchorMin = Anchor(textLeft, 0.52f), AnchorMax = "0.58 0.88" }
                }, itemPanel);

                // Badges sit to the left of the date: NEW closest to the date, PINNED before it.
                float badgeRight = 0.765f;
                if (isUnread)
                {
                    AddBadge(container, itemPanel, Msg("UnreadBadge", player), c.ButtonPrimary, "1 1 1 1", badgeRight - 0.06f, badgeRight);
                    badgeRight -= 0.07f;
                }
                if (ann.Pinned)
                {
                    AddBadge(container, itemPanel, Msg("PinnedBadge", player), PinGold, PinGoldText, badgeRight - 0.085f, badgeRight);
                }

                string rawPreview = (ann.Text ?? "").Replace("\n", " ");
                int previewChars = textLeft > 0.1f ? 70 : 88;
                string pollTag = string.Empty;
                if (HasPoll(ann))
                {
                    pollTag = $"<color={RgbaToHex(c.ButtonPrimary)}>{Msg("PollLabel", player)} · {Msg("PollVotes", player, ann.PollVotes.Count)}</color>   ";
                    previewChars -= 16;
                }
                string preview = $"<color={RgbaToHex(typeColor)}>{ann.Type.ToString().ToUpper()}</color>   {pollTag}{Ellipsize(rawPreview, previewChars)}";
                container.Add(new CuiLabel
                {
                    Text = { Text = preview, FontSize = 11, Align = TextAnchor.UpperLeft, Color = c.TextMuted, Font = FontRegular },
                    RectTransform = { AnchorMin = Anchor(textLeft, 0.1f), AnchorMax = "0.82 0.5" }
                }, itemPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = ann.Date, FontSize = 10, Align = TextAnchor.MiddleRight, Color = c.TextMuted, Font = FontRegular },
                    RectTransform = { AnchorMin = "0.77 0.52", AnchorMax = "0.93 0.88" }
                }, itemPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = $"❤ {ann.LikedPlayers?.Count ?? 0}", FontSize = 10, Align = TextAnchor.MiddleRight, Color = c.TextMuted, Font = FontBold },
                    RectTransform = { AnchorMin = "0.82 0.12", AnchorMax = "0.93 0.48" }
                }, itemPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = "›", FontSize = 22, Align = TextAnchor.MiddleCenter, Color = isUnread ? c.ButtonPrimary : c.TextMuted, Font = FontBold },
                    RectTransform = { AnchorMin = "0.94 0", AnchorMax = "0.99 1" }
                }, itemPanel);

                count++;
            }

            // Footer
            AddHairline(container, mainPanel, 0.095f, 0.025f, 0.975f);
            container.Add(new CuiLabel
            {
                Text = { Text = Msg("Page", player, page + 1, totalPages), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = "0.4 0.015", AnchorMax = "0.6 0.08" }
            }, mainPanel);

            if (page > 0)
                AddGhostButton(container, mainPanel, Msg("Previous", player), $"news.page {page - 1}", "0.025 0.022", "0.15 0.073", c.TextTitle, 10);

            if (page < totalPages - 1)
                AddGhostButton(container, mainPanel, Msg("Next", player), $"news.page {page + 1}", "0.85 0.022", "0.975 0.073", c.TextTitle, 10);

            ApplyFadeRamp(container, contentStart, 0.14f * anim, 0.6f * anim);
            CuiHelper.AddUi(player, container);
        }

        private void AddBadge(CuiElementContainer container, string parent, string text, string background, string textColor, float left, float right)
        {
            AddPill(container, parent, text, Anchor(left, 0.6f), Anchor(right, 0.82f), background, textColor, 9);
        }

        // Short rounded type-colour bar inside the left edge of a card.
        private void AddAccentBar(CuiElementContainer container, string parent, string color, float anchorX = 0f, float leftUnits = 8f)
        {
            string bar = container.Add(Piece("0 0 0 0", Anchor(anchorX, 0f), Anchor(anchorX, 1f), $"{F(leftUnits)} 14", $"{F(leftUnits + 3f)} -14", 0f), parent);
            AddRoundedFill(container, bar, color, 1.5f);
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

        // ImageLibrary's cached copy when it is ready; null means "let the client load the URL itself".
        private string GetImage(string url)
        {
            if (ImageLibrary == null || !ImageLibrary.IsLoaded || string.IsNullOrEmpty(url)) return null;
            if (!ImageLibrary.Call<bool>("HasImage", url, 0UL)) return null;
            return ImageLibrary.Call<string>("GetImage", url);
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
            bool fresh;
            float anim = OpenScreen(player, UiScreen.Admin, 0f, out fresh);

            var container = new CuiElementContainer();
            var c = config.Colors;

            AddBackdrop(container, AdminBackdrop, fresh ? 0.22f : 0f);
            int contentStart = container.Count;
            AddPanelShadow(container, LayerName, "0.16 0.12", "0.84 0.88");

            string surface = Opaque(AdminSurface);
            string mainPanel = AddSurface(container, LayerName, "0.16 0.12", "0.84 0.88", surface, RadiusPanel, null, LayerName + ".Admin", PanelRim);
            AddTopSheen(container, mainPanel, RadiusPanel);

            AddHairline(container, mainPanel, 0.866f, 0.025f, 0.975f);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{Msg("AdminControl", player)}  <color={RgbaToHex(c.TextMuted)}>/  {(config.General.ServerName ?? "SERVER").ToUpper()}</color>", FontSize = 17, Align = TextAnchor.LowerLeft, Color = c.TextTitle, Font = FontBold },
                RectTransform = { AnchorMin = "0.035 0.925", AnchorMax = "0.57 0.975" }
            }, mainPanel);

            int totalReads = announcements.Sum(a => a.ReadByPlayers?.Count ?? 0);
            int totalLikes = announcements.Sum(a => a.LikedPlayers?.Count ?? 0);
            container.Add(new CuiLabel
            {
                Text = { Text = Msg("AdminStats", player, announcements.Count, announcements.Count(a => a.Pinned), totalReads, totalLikes), FontSize = 9, Align = TextAnchor.UpperLeft, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = "0.035 0.875", AnchorMax = "0.57 0.92" }
            }, mainPanel);

            AddPrimaryButton(container, mainPanel, Msg("NewPost", player), "news.admin.create", "0.735 0.907", "0.925 0.961", c.ButtonPrimary, "1 1 1 1", 10);


            AddGhostButton(container, mainPanel, Msg("Themes", player), "news.admin.themes", "0.59 0.907", "0.725 0.961", c.TextNormal, 9);
            AddGhostButton(container, mainPanel, "✕", "news.close", "0.94 0.907", "0.975 0.961", c.TextNormal, 12);

            var displayList = GetDisplayOrder();
            int perPage = config.General.AnnouncementsPerPage;
            int totalPages = Math.Max(1, Mathf.CeilToInt((float)displayList.Count / perPage));
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;

            PruneAdminSelection(player.userID);
            var selection = GetAdminSelection(player.userID);
            int start = page * perPage;
            int count = 0;
            float rowHeight = 0.68f / perPage;
            float padding = 0.007f;

            if (displayList.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("NoPostsYet", player), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = FontRegular },
                    RectTransform = { AnchorMin = "0.1 0.3", AnchorMax = "0.9 0.7" }
                }, mainPanel);
            }

            for (int i = start; i < displayList.Count && count < perPage; i++)
            {
                var ann = displayList[i];
                float top = 0.845f - (count * rowHeight) - padding;
                float bottom = top - rowHeight + (padding * 2);

                string itemPanel = mainPanel + $".{i}";
                string typeColor = GetTypeColor(ann.Type);
                bool selected = selection.Contains(ann.Id);

                string rowColor = Blend(AdminSurfaceRaised, surface);
                if (selected) rowColor = Blend(MixAlpha(c.ButtonPrimary, 0.12f), rowColor);
                else if (ann.Pinned) rowColor = Blend("0.95 0.70 0.20 0.05", rowColor);
                string rowRim = selected ? MixAlpha(c.ButtonPrimary, 0.55f) : ann.Pinned ? "0.95 0.70 0.20 0.35" : null;
                AddSurface(container, mainPanel, Anchor(0.025f, bottom), Anchor(0.975f, top), rowColor, RadiusCard, null, itemPanel, rowRim);

                string check = AddSurface(container, itemPanel, "0.012 0.32", "0.034 0.68", selected ? c.ButtonPrimary : rowColor, RadiusPill,
                    $"news.admin.toggleselect {ann.Id} {page}", null, selected ? null : "1 1 1 0.22", 1.5f);
                if (selected) AddLabelIn(container, check, "✓", 11, "1 1 1 1");
                AddAccentBar(container, itemPanel, typeColor, 0.045f, 0f);


                container.Add(new CuiLabel
                {
                    Text = { Text = Ellipsize((string.IsNullOrEmpty(ann.Title) ? Msg("NoTitle", player) : ann.Title).ToUpper(), 52), FontSize = 12, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                    RectTransform = { AnchorMin = "0.065 0.45", AnchorMax = "0.545 0.9" }
                }, itemPanel);

                string meta = $"{ann.Date ?? ""}  /  {ann.Author ?? Msg("Unknown", player)}  /  <color={RgbaToHex(typeColor)}>{ann.Type.ToString().ToUpper()}</color>";
                if (!string.IsNullOrEmpty(ann.EditedDate)) meta += $"  /  {Msg("EditedTag", player)}";
                container.Add(new CuiLabel
                {
                    Text = { Text = meta, FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted },
                    RectTransform = { AnchorMin = "0.065 0.25", AnchorMax = "0.545 0.48" }
                }, itemPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = HasPoll(ann) ? Msg("ReadsLikesVotes", player, ann.ReadByPlayers?.Count ?? 0, ann.LikedPlayers?.Count ?? 0, ann.PollVotes.Count) : Msg("ReadsLikes", player, ann.ReadByPlayers?.Count ?? 0, ann.LikedPlayers?.Count ?? 0), FontSize = 8, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = FontRegular },
                    RectTransform = { AnchorMin = "0.065 0.04", AnchorMax = "0.545 0.27" }
                }, itemPanel);

                AddGhostButton(container, itemPanel, Msg("ResendButton", player), $"news.admin.resend {ann.Id} {page}", "0.56 0.24", "0.655 0.76", c.TextNormal, 9);

                if (ann.Pinned)
                {
                    AddPrimaryButton(container, itemPanel, Msg("UnpinButton", player), $"news.admin.togglepin {ann.Id} {page}", "0.665 0.24", "0.76 0.76", PinGold, PinGoldText, 9);
                }
                else
                {
                    AddGhostButton(container, itemPanel, Msg("PinButton", player), $"news.admin.togglepin {ann.Id} {page}", "0.665 0.24", "0.76 0.76", c.TextNormal, 9);
                }

                AddPrimaryButton(container, itemPanel, Msg("EditButton", player), $"news.admin.edit {ann.Id}", "0.77 0.24", "0.865 0.76", c.ButtonPrimary, "1 1 1 1", 9);


                AddGhostButton(container, itemPanel, Msg("DelButton", player), $"news.admin.delconfirm {ann.Id} {page}", "0.875 0.24", "0.97 0.76", "0.95 0.48 0.50 1", 9);

                count++;
            }

            if (selection.Count > 0)
            {
                string bulkPanel = mainPanel + ".Bulk";
                AddSurface(container, mainPanel, "0.025 0.108", "0.975 0.165", Blend(MixAlpha(c.ButtonPrimary, 0.08f), surface), RadiusCard, null, bulkPanel, MixAlpha(c.ButtonPrimary, 0.35f));


                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("SelectedCount", player, selection.Count), FontSize = 11, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.18 1" }
                }, bulkPanel);

                AddPrimaryButton(container, bulkPanel, Msg("BulkDelete", player), $"news.admin.bulkdelconfirm {page}", "0.19 0.17", "0.32 0.83", AdminDanger, "1 1 1 1", 10);
                AddPrimaryButton(container, bulkPanel, Msg("BulkPin", player), $"news.admin.bulkpin 1 {page}", "0.33 0.17", "0.46 0.83", c.ButtonPrimary, "1 1 1 1", 10);


                AddGhostButton(container, bulkPanel, Msg("BulkUnpin", player), $"news.admin.bulkpin 0 {page}", "0.47 0.17", "0.60 0.83", c.TextTitle, 10);
                AddGhostButton(container, bulkPanel, Msg("ClearSelection", player), $"news.admin.clearsel {page}", "0.61 0.17", "0.74 0.83", c.TextTitle, 10);
            }

            AddHairline(container, mainPanel, 0.095f, 0.025f, 0.975f);

            container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = $"news.admin.selectpage {page}" },
                Text = { Text = Msg("SelectPageToggle", player), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = c.TextMuted },
                RectTransform = { AnchorMin = "0.02 0.01", AnchorMax = "0.18 0.07" }
            }, mainPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{Msg("PostsCount", player, displayList.Count)}  /  {Msg("Page", player, page + 1, totalPages)}", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = FontRegular },
                RectTransform = { AnchorMin = "0.39 0", AnchorMax = "0.61 0.08" }
            }, mainPanel);

            if (page > 0)
            {
                AddGhostButton(container, mainPanel, "<", $"news.admin.page {page - 1}", "0.345 0.02", "0.38 0.075", c.TextNormal, 11);
            }
            if (page < totalPages - 1)
            {
                AddGhostButton(container, mainPanel, ">", $"news.admin.page {page + 1}", "0.62 0.02", "0.655 0.075", c.TextNormal, 11);
            }

            ApplyFadeRamp(container, contentStart, 0.12f * anim, 0.5f * anim);
            CuiHelper.AddUi(player, container);
        }

        private void AddEditorLabel(CuiElementContainer container, string parent, string text, float bottom, float top, UIColors c)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = Anchor(0.05f, bottom), AnchorMax = Anchor(0.95f, top) }
            }, parent);
        }

        private void AddEditorInput(CuiElementContainer container, string parent, string field, string value, int limit, float bottom, float top, bool multiline, float left = 0.05f, float right = 0.95f, string prefix = null)
        {
            AddSurface(container, parent, Anchor(left, bottom), Anchor(right, top), "1 1 1 0.045", RadiusButton);


            float inputLeft = left + 0.01f;
            if (prefix != null)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = prefix, FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.35", Font = FontBold },
                    RectTransform = { AnchorMin = Anchor(left, bottom), AnchorMax = Anchor(left + 0.03f, top) }
                }, parent);
                inputLeft = left + 0.032f;
            }

            var input = new CuiInputFieldComponent
            {
                Text = value ?? string.Empty,
                FontSize = 12,
                Align = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft,
                Command = $"news.editor.input {field}",
                Color = "1 1 1 1",
                NeedsKeyboard = true,
                CharsLimit = limit
            };
            if (multiline)
            {
                input.LineType = UnityEngine.UI.InputField.LineType.MultiLineNewline;
                input.Font = FontRegular;
            }

            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    input,
                    new CuiRectTransformComponent { AnchorMin = Anchor(inputLeft, bottom + (multiline ? 0.01f : 0f)), AnchorMax = Anchor(right - 0.01f, top - (multiline ? 0.01f : 0f)) }
                }
            });
        }

        private void AddEditorToggle(CuiElementContainer container, string parent, string text, string command, bool on, float left, float right, UIColors c, float bottom = 0.478f, float top = 0.535f)
        {
            string button = AddSurface(container, parent, Anchor(left, bottom), Anchor(right, top), on ? MixAlpha(c.ButtonPrimary, 0.14f) : Ghost, RadiusButton, command);

            // Square check box sized from the button width so it stays square at any toggle width.
            float boxW = 0.56f * (top - bottom) * EditorHeightUnits / (Math.Max(0.05f, right - left) * EditorWidthUnits);
            AddSurface(container, button, Anchor(0.035f, 0.22f), Anchor(0.035f + boxW, 0.78f), on ? c.ButtonPrimary : "1 1 1 0.14", RadiusPill - 1f);

            if (on)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = "✓", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = FontBold },
                    RectTransform = { AnchorMin = Anchor(0.035f, 0.22f), AnchorMax = Anchor(0.035f + boxW, 0.78f) }
                }, button);
            }
            container.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = on ? c.TextTitle : c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = Anchor(0.06f + boxW, 0f), AnchorMax = "1 1" }
            }, button);
        }

        private void ShowEditor(BasePlayer player)
        {
            var session = GetEditor(player);
            if (session == null) return;
            var ann = session.Draft;

            bool fresh;
            float anim = OpenScreen(player, UiScreen.Editor, 0f, out fresh);

            var container = new CuiElementContainer();
            var c = config.Colors;

            AddBackdrop(container, AdminBackdrop, fresh ? 0.22f : 0f);
            int contentStart = container.Count;
            AddPanelShadow(container, LayerName, "0.21 0.13", "0.79 0.87");

            string editorPanel = AddSurface(container, LayerName, "0.21 0.13", "0.79 0.87", Opaque(AdminSurface), RadiusPanel, null, LayerName + ".Editor", PanelRim);
            AddTopSheen(container, editorPanel, RadiusPanel);

            AddHairline(container, editorPanel, 0.91f, 0.04f, 0.96f);
            container.Add(new CuiLabel { Text = { Text = session.IsNew ? Msg("CreateAnnouncement", player) : Msg("EditAnnouncement", player), FontSize = 14, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold }, RectTransform = { AnchorMin = "0.04 0.91", AnchorMax = "0.9 1" } }, editorPanel);

            AddGhostButton(container, editorPanel, "✕", "news.editor.cancel", "0.932 0.932", "0.962 0.978", c.TextNormal, 12);

            AddEditorLabel(container, editorPanel, Msg("AnnouncementTitle", player), 0.84f, 0.895f, c);
            AddEditorInput(container, editorPanel, "title", ann.Title, MaxTitleChars, 0.785f, 0.84f, false);

            AddEditorLabel(container, editorPanel, Msg("ImageUrl", player), 0.735f, 0.785f, c);
            AddEditorInput(container, editorPanel, "image", ann.ImageUrl, MaxImageUrlChars, 0.68f, 0.735f, false);

            // Type chips: every type is visible, the selected one is filled with its colour.
            AddEditorLabel(container, editorPanel, Msg("TypeLabel", player), 0.63f, 0.68f, c);
            var types = (AnnouncementType[])Enum.GetValues(typeof(AnnouncementType));
            float chipGap = 0.01f;
            float chipWidth = (0.90f - chipGap * (types.Length - 1)) / types.Length;
            for (int i = 0; i < types.Length; i++)
            {
                bool selected = types[i] == ann.Type;
                float left = 0.05f + i * (chipWidth + chipGap);
                string typeColor = GetTypeColor(types[i]);
                // Selected chip is filled with its type colour; the others show the colour in their text.
                string chip = AddSurface(container, editorPanel, Anchor(left, 0.575f), Anchor(left + chipWidth, 0.63f), selected ? typeColor : Ghost, RadiusButton, $"news.editor.settype {types[i]}");
                AddLabelIn(container, chip, types[i].ToString().ToUpper(), 10, selected ? "1 1 1 1" : typeColor);
            }

            // Option toggles share the row evenly.
            AddEditorLabel(container, editorPanel, Msg("Options", player), 0.535f, 0.575f, c);
            var toggles = new List<string[]>
            {
                new[] { Msg("OptionPin", player), "pin", ann.Pinned ? "1" : "0" },
                new[] { Msg(session.IsNew ? "OptionNotify" : "OptionNotifyEdit", player), "notify", session.Notify ? "1" : "0" }
            };
            if (session.IsNew && DiscordConfigured) toggles.Add(new[] { Msg("OptionDiscord", player), "discord", session.Discord ? "1" : "0" });
            toggles.Add(new[] { Msg("OptionPoll", player), "poll", session.Poll ? "1" : "0" });
            const float toggleGap = 0.0125f;
            float toggleWidth = (0.90f - toggleGap * (toggles.Count - 1)) / toggles.Count;
            for (int i = 0; i < toggles.Count; i++)
            {
                float left = 0.05f + i * (toggleWidth + toggleGap);
                AddEditorToggle(container, editorPanel, toggles[i][0], "news.editor.toggle " + toggles[i][1], toggles[i][2] == "1", left, left + toggleWidth, c);
            }

            AddEditorLabel(container, editorPanel, Msg("ContentBody", player), 0.43f, 0.475f, c);
            container.Add(new CuiLabel { Text = { Text = Msg("BodyHint", player), FontSize = 9, Align = TextAnchor.LowerRight, Color = c.TextMuted, Font = FontRegular }, RectTransform = { AnchorMin = "0.36 0.43", AnchorMax = "0.95 0.475" } }, editorPanel);
            AddEditorInput(container, editorPanel, "text", ann.Text, MaxContentChars, session.Poll ? 0.28f : 0.135f, 0.43f, true);

            if (session.Poll)
            {
                // 2x2 grid of option fields under the body.
                AddEditorLabel(container, editorPanel, Msg("PollOptionsLabel", player), 0.232f, 0.272f, c);
                if (!session.IsNew)
                    AddEditorToggle(container, editorPanel, Msg("OptionPollClosed", player), "news.editor.toggle pollclosed", session.PollClosed, 0.74f, 0.95f, c, 0.234f, 0.272f);
                for (int i = 0; i < MaxPollOptions; i++)
                {
                    float left = i % 2 == 0 ? 0.05f : 0.505f;
                    float top = i < 2 ? 0.227f : 0.18f;
                    AddEditorInput(container, editorPanel, "poll" + (i + 1), session.PollOptions[i], MaxPollOptionChars, top - 0.042f, top, false, left, left + 0.445f, (i + 1).ToString());
                }
            }

            string footerPanel = editorPanel + ".Footer";
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.115" } }, editorPanel, footerPanel);
            AddHairline(container, editorPanel, 0.115f, 0.04f, 0.96f);

            AddGhostButton(container, footerPanel, Msg("Cancel", player), "news.editor.cancel", "0.05 0.2", "0.29 0.8", c.TextNormal, 11);
            AddGhostButton(container, footerPanel, Msg("Preview", player), "news.editor.preview", "0.31 0.2", "0.53 0.8", c.TextTitle, 11);

            AddPrimaryButton(container, footerPanel, session.IsNew ? Msg("SaveBroadcast", player) : Msg("SaveChanges", player), "news.editor.save", "0.55 0.2", "0.95 0.8", c.ButtonPrimary);

            ApplyFadeRamp(container, contentStart, 0.12f * anim, 0.38f * anim);
            CuiHelper.AddUi(player, container);
        }

        private void AddConfirmDialog(CuiElementContainer container, BasePlayer player, string title, string body, string confirmCommand)
        {
            var c = config.Colors;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.45", Material = BlurMaterial },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", ConfirmLayer);

            AddPanelShadow(container, ConfirmLayer, "0.355 0.385", "0.645 0.615");

            string dialogPanel = AddSurface(container, ConfirmLayer, "0.355 0.385", "0.645 0.615", Opaque(AdminSurface), RadiusPanel, null, ConfirmLayer + ".Dialog", PanelRim);
            AddTopSheen(container, dialogPanel, RadiusPanel);

            // Round warning icon above the title.
            string icon = AddSurface(container, dialogPanel, "0.465 0.75", "0.535 0.907", Blend(MixAlpha(AdminDanger, 0.22f), Opaque(AdminSurface)), 13f);
            AddLabelIn(container, icon, "!", 15, "0.98 0.52 0.54 1");

            container.Add(new CuiLabel
            {
                Text = { Text = title, FontSize = 13, Align = TextAnchor.MiddleCenter, Color = c.TextTitle, Font = FontBold },
                RectTransform = { AnchorMin = "0 0.58", AnchorMax = "1 0.73" }
            }, dialogPanel);
            container.Add(new CuiLabel
            {
                Text = { Text = body, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = c.TextNormal, Font = FontRegular },
                RectTransform = { AnchorMin = "0.05 0.29", AnchorMax = "0.95 0.58" }
            }, dialogPanel);

            AddGhostButton(container, dialogPanel, Msg("Cancel", player), "news.confirm.close", "0.05 0.08", "0.46 0.26", c.TextTitle, 11);

            AddPrimaryButton(container, dialogPanel, Msg("ConfirmDelete", player), confirmCommand, "0.54 0.08", "0.95 0.26", AdminDanger);
        }

        private void ShowDeleteConfirm(BasePlayer player, string id, int returnPage)
        {
            var ann = FindById(id);
            if (ann == null) return;
            CuiHelper.DestroyUi(player, ConfirmLayer);

            var container = new CuiElementContainer();
            AddConfirmDialog(container, player, Msg("DeleteAnnouncement", player), Msg("DeleteConfirmBody", player, Ellipsize(ann.Title, 32)), $"news.admin.del {ann.Id} {returnPage}");
            ApplyFadeRamp(container, 0, 0.1f, 0.26f);
            CuiHelper.AddUi(player, container);
        }

        private void ShowBulkDeleteConfirm(BasePlayer player, int returnPage)
        {
            var sel = GetAdminSelection(player.userID);
            if (sel.Count == 0) return;
            CuiHelper.DestroyUi(player, ConfirmLayer);

            var container = new CuiElementContainer();
            AddConfirmDialog(container, player, Msg("BulkDeleteTitle", player), Msg("BulkDeleteBody", player, sel.Count), $"news.admin.bulkdel {returnPage}");
            ApplyFadeRamp(container, 0, 0.1f, 0.26f);
            CuiHelper.AddUi(player, container);
        }
        #endregion

        #region Theme UI
        [ConsoleCommand("news.admin.themes")]
        private void CmdNewsAdminThemes(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;
            ShowThemeSelection(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.settheme")]
        private void CmdNewsSetTheme(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(player, Msg("ThemeUsage", player));
                return;
            }

            string themeName = arg.GetString(0).Trim('"');
            int page = arg.GetInt(1, 0);
            string matchedTheme = config.Themes.Keys.FirstOrDefault(k => string.Equals(k, themeName, StringComparison.OrdinalIgnoreCase));

            if (matchedTheme == null)
            {
                SendReply(player, Msg("ThemeNotFound", player, themeName, string.Join(", ", config.Themes.Keys.ToArray())));
                return;
            }

            config.SelectedTheme = matchedTheme;
            SaveConfig();

            NextTick(() =>
            {
                if (player == null || !player.IsConnected) return;
                ShowThemeSelection(player, page);
                SendReply(player, Msg("ThemeSet", player, matchedTheme));
            });
        }

        private void ShowThemeSelection(BasePlayer player, int page)
        {
            bool fresh;
            float anim = OpenScreen(player, UiScreen.Themes, 0f, out fresh);

            var container = new CuiElementContainer();
            var c = config.Colors;

            AddBackdrop(container, AdminBackdrop, fresh ? 0.22f : 0f);
            int contentStart = container.Count;

            var names = config.Themes.Keys.ToList();
            int totalPages = Math.Max(1, Mathf.CeilToInt((float)names.Count / ThemesPerPage));
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;

            // The panel grows with the number of theme rows on this page (2 cards per row, max 4 rows).
            int onPage = Math.Min(ThemesPerPage, names.Count - page * ThemesPerPage);
            int rows = Math.Max(1, Math.Min(4, (onPage + 1) / 2));
            const float headerH = 0.066f, padTop = 0.022f, cardH = 0.118f, gapY = 0.014f;
            float padBottom = totalPages > 1 ? 0.064f : 0.026f;
            float panelH = headerH + padTop + rows * cardH + (rows - 1) * gapY + padBottom;

            AddPanelShadow(container, LayerName, Anchor(0.27f, 0.5f - panelH / 2f), Anchor(0.73f, 0.5f + panelH / 2f));

            string surface = Opaque(AdminSurface);
            string mainPanel = AddSurface(container, LayerName, Anchor(0.27f, 0.5f - panelH / 2f), Anchor(0.73f, 0.5f + panelH / 2f), surface, RadiusPanel, null, LayerName + ".Themes", PanelRim);
            AddTopSheen(container, mainPanel, RadiusPanel);

            float headerBottom = 1f - headerH / panelH;
            AddHairline(container, mainPanel, headerBottom, 0.05f, 0.95f);
            container.Add(new CuiLabel { Text = { Text = Msg("SelectTheme", player), FontSize = 16, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold }, RectTransform = { AnchorMin = Anchor(0.05f, headerBottom), AnchorMax = "0.7 1" } }, mainPanel);

            AddGhostButton(container, mainPanel, Msg("Back", player), "news.admin", Anchor(0.80f, 1f - headerH * 0.78f / panelH), Anchor(0.95f, 1f - headerH * 0.22f / panelH), c.TextNormal, 10);

            const float gridLeft = 0.05f, gridRight = 0.95f, gap = 0.02f;
            float cardWidth = (gridRight - gridLeft - gap) / 2f;
            float cardHeight = cardH / panelH;

            int start = page * ThemesPerPage;
            for (int i = start; i < names.Count && i < start + ThemesPerPage; i++)
            {
                string themeName = names[i];
                var theme = config.Themes[themeName];
                bool isSelected = config.SelectedTheme == themeName;
                int slot = i - start;
                int col = slot % 2, row = slot / 2;
                float left = gridLeft + col * (cardWidth + gap);
                float top = 1f - (headerH + padTop + row * (cardH + gapY)) / panelH;

                string cardColor = Blend(isSelected ? MixAlpha(theme.ButtonPrimary, 0.10f) : "1 1 1 0.04", surface);
                string card = AddSurface(container, mainPanel, Anchor(left, top - cardHeight), Anchor(left + cardWidth, top), cardColor, RadiusCard,
                    $"news.admin.settheme \"{themeName}\" {page}", null, isSelected ? Opaque(theme.ButtonPrimary) : null, 2f);

                container.Add(new CuiLabel
                {
                    Text = { Text = themeName.ToUpper(), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                    RectTransform = { AnchorMin = "0.06 0.55", AnchorMax = "0.70 0.92" }
                }, card);

                if (isSelected)
                    AddPill(container, card, Msg("ActiveBadge", player), "0.72 0.62", "0.94 0.86", theme.ButtonPrimary, "1 1 1 1", 9);

                // Colour swatches in a rounded tray: panel, header, content, primary button, secondary button, title text.
                string tray = AddSurface(container, card, "0.06 0.13", "0.94 0.47", Blend("0 0 0 0.35", cardColor), RadiusPill);
                string[] swatches = { theme.PanelBg, theme.HeaderBg, theme.ContentBg, theme.ButtonPrimary, theme.ButtonSecondary, theme.TextTitle };
                float swatchWidth = (0.96f - 0.012f * (swatches.Length - 1)) / swatches.Length;
                for (int s = 0; s < swatches.Length; s++)
                {
                    float sx = 0.02f + s * (swatchWidth + 0.012f);
                    container.Add(new CuiPanel { Image = { Color = OpaqueColor(swatches[s]) }, RectTransform = { AnchorMin = Anchor(sx, 0.18f), AnchorMax = Anchor(sx + swatchWidth, 0.82f) } }, tray);
                }
            }

            if (totalPages > 1)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("Page", player, page + 1, totalPages), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = FontRegular },
                    RectTransform = { AnchorMin = Anchor(0.35f, 0.012f / panelH), AnchorMax = Anchor(0.65f, 0.05f / panelH) }
                }, mainPanel);
                if (page > 0)
                    AddGhostButton(container, mainPanel, "<", $"news.admin.themes {page - 1}", Anchor(0.28f, 0.014f / panelH), Anchor(0.34f, 0.048f / panelH), c.TextNormal, 11);
                if (page < totalPages - 1)
                    AddGhostButton(container, mainPanel, ">", $"news.admin.themes {page + 1}", Anchor(0.66f, 0.014f / panelH), Anchor(0.72f, 0.048f / panelH), c.TextNormal, 11);
            }

            ApplyFadeRamp(container, contentStart, 0.12f * anim, 0.45f * anim);
            CuiHelper.AddUi(player, container);
        }

        // Swatches are drawn fully opaque so translucent theme colours are still readable.
        private static string OpaqueColor(string rgba)
        {
            var parts = (rgba ?? string.Empty).Split(' ');
            if (parts.Length < 3) return "1 1 1 1";
            return $"{parts[0]} {parts[1]} {parts[2]} 1";
        }
        #endregion

        #region Rewards
        private bool CanGrantRewardBundle(RewardBundle bundle, out string reason)
        {
            reason = null;
            if (bundle == null)
            {
                reason = "reward bundle is missing";
                return false;
            }

            bool hasReward = false;
            if (bundle.Items != null)
            {
                foreach (var reward in bundle.Items)
                {
                    if (reward == null || string.IsNullOrWhiteSpace(reward.Shortname) || reward.Amount <= 0) continue;
                    hasReward = true;
                    if (ItemManager.FindItemDefinition(reward.Shortname) == null)
                    {
                        reason = $"invalid item shortname '{reward.Shortname}'";
                        return false;
                    }
                }
            }

            if (bundle.Points > 0)
            {
                hasReward = true;
                if (ServerRewards == null || !ServerRewards.IsLoaded)
                {
                    reason = "ServerRewards is not loaded";
                    return false;
                }
            }

            if (bundle.Currency > 0)
            {
                hasReward = true;
                if (Economics == null || !Economics.IsLoaded)
                {
                    reason = "Economics is not loaded";
                    return false;
                }
            }

            if (!hasReward) reason = "reward bundle is empty";
            return hasReward;
        }

        private bool GiveRewards(BasePlayer player, RewardBundle bundle, string langKey)
        {
            if (player == null) return false;
            string reason;
            if (!CanGrantRewardBundle(bundle, out reason))
            {
                PrintWarning($"Reward for {player.UserIDString} was not recorded: {reason}.");
                return false;
            }

            var grantedNames = new List<string>();

            if (bundle.Items != null)
            {
                foreach (var r in bundle.Items)
                {
                    if (r == null || string.IsNullOrEmpty(r.Shortname) || r.Amount <= 0) continue;

                    var item = ItemManager.CreateByName(r.Shortname, r.Amount, r.SkinId);
                    if (item == null)
                    {
                        PrintWarning($"Reward for {player.UserIDString} was not recorded because item '{r.Shortname}' could not be created.");
                        return false;
                    }

                    string label = item.info?.displayName?.translated;
                    if (string.IsNullOrEmpty(label)) label = r.Shortname;

                    if (!player.inventory.GiveItem(item))
                    {
                        item.Drop(player.transform.position + Vector3.up, Vector3.up * 2f);
                    }

                    grantedNames.Add($"{r.Amount}× {label}");
                }
            }

            if (bundle.Points > 0)
            {
                // Plain ulong: newer Rust builds wrap userID in a type ServerRewards cannot read.
                ServerRewards.Call("AddPoints", (ulong)player.userID, bundle.Points);
                grantedNames.Add($"{bundle.Points} {config.Rewards.PointsLabel}");
            }

            if (bundle.Currency > 0)
            {
                Economics.Call("Deposit", player.UserIDString, bundle.Currency);
                grantedNames.Add($"{bundle.Currency.ToString("0.##", CultureInfo.InvariantCulture)} {config.Rewards.CurrencyLabel}");
            }

            if (grantedNames.Count > 0 && config.Rewards.NotifyOnReward)
            {
                SendReply(player, Msg(langKey, player, string.Join(", ", grantedNames.ToArray())));
            }

            return grantedNames.Count > 0;
        }

        private void CancelReadRewardTimer(ulong userId)
        {
            ReadRewardState state;
            if (readRewardTimers.TryGetValue(userId, out state))
            {
                state.Timer?.Destroy();
                readRewardTimers.Remove(userId);
            }
        }

        private void ScheduleReadCompletion(BasePlayer player, Announcement ann)
        {
            if (player == null || ann == null || string.IsNullOrEmpty(ann.Id)) return;
            if (ann.ReadByPlayers == null) ann.ReadByPlayers = new HashSet<ulong>();

            ulong userId = player.userID;
            ReadRewardState existing;
            if (readRewardTimers.TryGetValue(userId, out existing)
                && string.Equals(existing.AnnId, ann.Id, StringComparison.Ordinal)
                && existing.Timer != null && !existing.Timer.Destroyed)
                return;

            bool alreadyRead = ann.ReadByPlayers.Contains(userId);
            bool rewardsActive = config.Rewards.EnableReadReward;
            bool rewardAlreadyGranted = ann.ReadRewardedPlayers != null && ann.ReadRewardedPlayers.Contains(userId);
            if (alreadyRead && (!rewardsActive || rewardAlreadyGranted)) return;

            CancelReadRewardTimer(userId);

            int delay = Math.Max(1, config.Rewards.ReadDelaySeconds);
            string targetId = ann.Id;

            var t = timer.Once(delay, () =>
            {
                readRewardTimers.Remove(userId);
                if (player == null || !player.IsConnected) return;

                PopupState state;
                if (!playersWithUiOpen.Contains(userId) || !openPopups.TryGetValue(userId, out state) || state.AnnId != targetId) return;

                var current = FindById(targetId);
                if (current == null) return;
                if (current.ReadByPlayers == null) current.ReadByPlayers = new HashSet<ulong>();
                if (current.ReadRewardedPlayers == null) current.ReadRewardedPlayers = new HashSet<ulong>();

                bool firstRead = current.ReadByPlayers.Add(userId);
                bool rewardDue = config.Rewards.EnableReadReward && !current.ReadRewardedPlayers.Contains(userId);
                bool grantReward = rewardDue && GiveRewards(player, config.Rewards.ReadRewards, "RewardRead");
                if (grantReward) current.ReadRewardedPlayers.Add(userId);

                if (grantReward) SaveAnnouncements();
                else if (firstRead) MarkDataDirty();
                if (firstRead) Interface.CallHook("OnNewsRead", player, BuildHookData(current));
            });

            readRewardTimers[userId] = new ReadRewardState { AnnId = targetId, Timer = t };
        }
        #endregion

        #region Discord Webhooks
        private void SendToDiscord(Announcement ann)
        {
            if (!DiscordConfigured || ann == null) return;

            int embedColor = 3447003;
            switch (ann.Type)
            {
                case AnnouncementType.Alert: embedColor = 15158332; break;
                case AnnouncementType.Warning: embedColor = 15105570; break;
                case AnnouncementType.Update: embedColor = 3066993; break;
                case AnnouncementType.Event: embedColor = 10181046; break;
            }

            string mention = (config.Discord.RoleMention ?? string.Empty).Trim();
            var parse = new List<string> { "roles", "users" };
            if (mention.Contains("@everyone") || mention.Contains("@here")) parse.Add("everyone");

            string discordBody = ann.Text ?? string.Empty;
            if (discordBody.Length > DiscordEmbedDescriptionLimit)
            {
                const string truncatedSuffix = "\n\n[Message truncated on Discord]";
                int keepLength = Math.Max(0, DiscordEmbedDescriptionLimit - truncatedSuffix.Length);
                discordBody = discordBody.Substring(0, Math.Min(keepLength, discordBody.Length)).TrimEnd() + truncatedSuffix;
            }

            string timestamp = ann.Timestamp > 0 ? new DateTime(ann.Timestamp, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture) : null;
            var fields = HasPoll(ann)
                ? new[] { new { name = Msg("PollDiscordField"), value = LimitText(string.Join("\n", ann.PollOptions.Select(o => "• " + o).ToArray()), 1024), inline = false } }
                : null;

            var payload = new
            {
                username = string.IsNullOrWhiteSpace(config.Discord.BotName) ? null : LimitText(config.Discord.BotName, 80),
                avatar_url = string.IsNullOrWhiteSpace(config.Discord.AvatarUrl) ? null : config.Discord.AvatarUrl,
                content = mention,
                allowed_mentions = new { parse = parse.ToArray() },
                embeds = new[]
                {
                    new
                    {
                        author = new { name = LimitText($"{ann.Type.ToString().ToUpper()}  •  {config.General.ServerName}", 256) },
                        title = LimitText(ann.Title, 256),
                        description = discordBody,
                        color = embedColor,
                        footer = new { text = $"Posted by {ann.Author}" },
                        timestamp = timestamp,
                        fields = fields,
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
