using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Pool = Facepunch.Pool;

namespace Oxide.Plugins
{
    [Info("NewsBroadcaster", "DEDA", "1.0.0")]
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
        private const string CountdownName = LayerName + ".Countdown";
        private const string EditorPanelName = LayerName + ".Editor";
        private const string ScreenName = LayerName + ".Screen";
        private const float SwitchFade = 0.3f;
        private const string BlurMaterial = "assets/content/ui/uibackgroundblur-ingamemenu.mat";
        private const string DataFile = "NewsBroadcaster_Data";

        private const string PermAdmin = "newsbroadcaster.admin";
        private const string PermView = "newsbroadcaster.view";

        private static readonly Regex CommandSplitRegex = new Regex(@"[\""].+?[\""]|[^ ]+", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"(https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SlashNewlineRegex = new Regex(@"/n(?![a-z])", RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new Regex(@"[ \t]{2,}", RegexOptions.Compiled);
        private static readonly Regex BlankLinesRegex = new Regex(@"\n{3,}", RegexOptions.Compiled);

        private ConfigData config;
        private StoredData storedData;
        private List<Announcement> announcements = new List<Announcement>();

        private readonly Dictionary<ulong, Timer> autoCloseTimers = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, Timer> notificationTimers = new Dictionary<ulong, Timer>();
        private readonly HashSet<ulong> playersWithUiOpen = new HashSet<ulong>();
        private readonly Dictionary<ulong, EditorSession> editors = new Dictionary<ulong, EditorSession>();
        private readonly Dictionary<ulong, Timer> readRewardTimers = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, HashSet<string>> adminSelections = new Dictionary<ulong, HashSet<string>>();
        private readonly Dictionary<ulong, float> likeCooldowns = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> voteCooldowns = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, string> openPopups = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> archivePages = new Dictionary<ulong, int>();
        private readonly HashSet<ulong> remindedPlayers = new HashSet<ulong>();
        private readonly Dictionary<ulong, UiScreen> currentScreens = new Dictionary<ulong, UiScreen>();
        private readonly Dictionary<ulong, string> toastNames = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, ArchiveFilter> archiveFilters = new Dictionary<ulong, ArchiveFilter>();
        private readonly Dictionary<ulong, AdminView> adminViews = new Dictionary<ulong, AdminView>();
        private readonly Dictionary<ulong, float> redrawCooldowns = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, Timer> redrawTimers = new Dictionary<ulong, Timer>();
        private int toastCounter;

        private Timer scheduleTimer;
        private long scheduledFor;
        private bool maxStoredWarned;

        private readonly List<Announcement> displayOrder = new List<Announcement>();
        private bool displayOrderDirty = true;

        private string[] cornerFill;
        private string[] cornerMask;
        private readonly Dictionary<string, float[]> parsedColors = new Dictionary<string, float[]>();
        private readonly HashSet<string> warnedRewardProblems = new HashSet<string>();
        private const int MaxCachedColors = 1024;

        private bool dataDirty;

        private const string DateFormat = "yyyy-MM-dd HH:mm";
        private const int MaxContentChars = 32768;
        private const int MaxTitleChars = 120;
        private const int MaxImageUrlChars = 2048;
        private const int DiscordEmbedDescriptionLimit = 4000;
        private const float LikeCooldownSeconds = 1.25f;
        private const float VoteCooldownSeconds = 0.75f;
        private const float RedrawCooldownSeconds = 0.4f;
        private const int MaxPollOptions = 4;
        private const int MaxPollOptionChars = 60;
        private const int ThemesPerPage = 8;
        private const float MinScheduleSeconds = 1f;
        private const float MaxScheduleSeconds = 3600f;
        private const int TitleLongChars = 80;
        private const int BodyLongChars = 1200;
        private const int MaxSearchChars = 40;
        private const int MaxCountdownLabelChars = 32;
        private const float CountdownBlockHeight = 0.09f;
        private static readonly int[] ExpirePresets = { 0, 1, 3, 7, 14, 30 };
        private static readonly int[] PollTimerPresets = { 0, 1, 6, 12, 24, 72, 168 };

        private const int BodyWrapWithImage = 52;
        private const int BodyWrapFullWidth = 104;
        private const float BodyLineUnits = 18f;
        private const float PopupWidthUnits = 832f;
        private const float PopupHeightUnits = 468f;
        private const float EditorWidthUnits = 947.2f;
        private const float EditorHeightUnits = 576f;
        private const float ArchiveWidthUnits = 896f;
        private const float AdminWidthUnits = 947.2f;
        private const float AdminHeightUnits = 619.2f;
        private const float AdminSidePad = 28f;
        private static readonly string[] AdminTabs = { "all", "published", "drafts", "pinned", "polls" };
        private static readonly string[] AdminSorts = { "newest", "oldest", "reads", "likes" };
        private const int AutosaveKeepDays = 30;

        private const string AdminBackdrop = "0.015 0.018 0.022 0.74";
        private const string AdminSurface = "0.055 0.062 0.073 0.99";
        private const string AdminSurfaceRaised = "0.082 0.090 0.104 0.98";
        private const string AdminDanger = "0.82 0.24 0.27 0.95";
        private const string PinGold = "0.95 0.70 0.20 0.95";
        private const string PinGoldText = "0.05 0.05 0.05 1";
        private const string Hairline = "1 1 1 0.07";
        private const string Ghost = "1 1 1 0.055";
        private const string PanelRim = "1 1 1 0.09";
        private const float RadiusPanel = 12f;
        private const float RadiusCard = 8f;
        private const float RadiusButton = 6f;
        private const int CornerTextureSize = 32;
        private const float SeamOverlap = 1f;
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
            public Dictionary<ulong, long> ReadAllUpTo = new Dictionary<ulong, long>();
            public Dictionary<ulong, PlayerPrefs> PlayerSettings = new Dictionary<ulong, PlayerPrefs>();
            public Dictionary<ulong, EditorSession> EditorAutosaves = new Dictionary<ulong, EditorSession>();
        }

        class PlayerPrefs
        {
            public bool Sound = true;
            public bool Popups = true;
            public bool JoinPopup = true;
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
            public List<string> PollOptions = new List<string>();
            public Dictionary<ulong, int> PollVotes = new Dictionary<ulong, int>();
            public bool PollClosed;
            public bool Draft;
            public string Group = string.Empty;
            public int ExpireDays;
            public long ExpiresAt;
            public int PollHours;
            public long PollEndsAt;
            public long CountdownTo;
            public string CountdownLabel = string.Empty;
        }

        class ArchiveFilter
        {
            public string Type = "all";
            public string Query = string.Empty;
        }

        class AdminView
        {
            public string Tab = "all";
            public string Query = string.Empty;
            public int Sort;
            public int Page;
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
            [JsonIgnore] public bool IsNew => string.IsNullOrEmpty(EditingId);
            public bool IsDraft;
            public bool ExpireChanged;
            public bool PollTimerChanged;
            public string CountdownInput = string.Empty;
            [JsonIgnore] public bool Publishes => IsNew || IsDraft;
            public long SavedAt;

            public void LoadPollOptions(Announcement from)
            {
                for (int i = 0; i < PollOptions.Length && i < from.PollOptions.Count; i++)
                    PollOptions[i] = from.PollOptions[i];
            }
        }

        enum PopupOrigin { Broadcast, Direct, Archive, Preview }

        enum UiScreen { None, Popup, Archive, Admin, Editor, Themes }

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
                    if (SelectedTheme != null && Themes.TryGetValue(SelectedTheme, out theme)) return theme;
                    foreach (var first in Themes.Values) return first;
                    return new UIColors();
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
            public string Position { get; set; } = "TopRight";
            public float OffsetX { get; set; } = 0f;
            public float OffsetY { get; set; } = 0f;
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
            public bool PostGroupOnlyPosts { get; set; } = false;
            public bool PostPollResults { get; set; } = true;
        }

        class PollSettings
        {
            public bool AllowVoteChange { get; set; } = true;
            public bool ShowResultsBeforeVoting { get; set; } = false;
            public bool AnnounceResults { get; set; } = true;
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
                ["DiscordPostedBy"] = "Posted by {0}",
                ["DiscordTruncated"] = "[Message truncated on Discord]",
                ["ConsolePollUsage"] = "Usage: news.poll \"Title\" \"Text\" \"Option 1|Option 2|...\" [Type]",
                ["NoPermissionView"] = "You do not have permission to view news.",
                ["NoNewsHistory"] = "No news history available.",
                ["NewsBroadcasted"] = "News broadcasted!",
                ["ArchiveTitle"] = "ARCHIVE",
                ["ViewArchive"] = "VIEW ARCHIVE",
                ["BackToArchive"] = "< BACK TO ARCHIVE",
                ["BackToEditor"] = "< BACK TO EDITOR",
                ["PostedBy"] = "POSTED BY",
                ["EditedTag"] = "EDITED",
                ["NewAnnouncement"] = "NEW ANNOUNCEMENT",
                ["AutoCloseIn"] = "CLOSES IN %TIME_LEFT%S",
                ["ClickToRead"] = "CLICK TO READ",
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
                ["BodyHintShort"] = "Enter or \\n makes a new line",
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
                ["ConsoleTriggerUsage"] = "Usage: news.trigger <SteamID/Name> [index|id]",
                ["ConsoleTriggerHidden"] = "{0} cannot see that announcement (draft, expired, another group or no view permission).",
                ["ConsoleDeleteUsage"] = "Usage: news.delete <index|id>",
                ["ConsoleResendUsage"] = "Usage: news.resend <index|id>",
                ["ConsoleInvalidIndex"] = "Invalid index or id. Use 'news.list' to see all announcements.",
                ["ConsoleDeleted"] = "Deleted announcement: '{0}'",
                ["ConsoleNoStored"] = "No announcements stored.",
                ["ConsoleListHeader"] = "=== Stored Announcements ({0}) ===",
                ["PlayerNotFound"] = "Player not found.",
                ["PopupTriggered"] = "News popup triggered for {0}",
                ["NoAnnouncementsAvailable"] = "No announcements available.",
                ["ChatHelpSettings"] = "\n/news settings - sound and pop-up options",

                ["AdminTabAll"] = "ALL",
                ["AdminTabPublished"] = "PUBLISHED",
                ["AdminTabDrafts"] = "DRAFTS",
                ["AdminTabPinned"] = "PINNED",
                ["AdminTabPolls"] = "POLLS",
                ["SortNewest"] = "NEWEST FIRST",
                ["SortOldest"] = "OLDEST FIRST",
                ["SortReads"] = "MOST READ",
                ["SortLikes"] = "MOST LIKED",
                ["StatPublished"] = "PUBLISHED",
                ["StatDrafts"] = "DRAFTS",
                ["StatReads"] = "READS",
                ["StatLikes"] = "LIKES",
                ["StatVotes"] = "POLL VOTES",
                ["StatPinnedHint"] = "{0} PINNED",
                ["StatDraftsHint"] = "HIDDEN FROM PLAYERS",
                ["StatReadsHint"] = "{0} PER POST",
                ["StatLikesHint"] = "{0}% OF READS",
                ["StatVotesHint"] = "{0} OPEN",
                ["ColReads"] = "READS",
                ["ColLikes"] = "LIKES",
                ["ColVotes"] = "VOTES",

                ["UnsavedBadge"] = "UNSAVED",
                ["UnsavedPost"] = "\"{0}\"  •  last change {1} ago",
                ["ContinueEditing"] = "CONTINUE EDITING",
                ["Discard"] = "DISCARD",
                ["UnsavedDiscarded"] = "Unsaved post discarded.",
                ["AutosaveHint"] = "CHANGES ARE SAVED AUTOMATICALLY",

                ["SaveDraft"] = "SAVE DRAFT",
                ["DraftSaved"] = "Draft saved. Publish it from News Management when it's ready.",
                ["DraftBadge"] = "DRAFT",
                ["PublishButton"] = "PUBLISH",
                ["CopyButton"] = "COPY",
                ["Copied"] = "Copied \"{0}\" into a new post. Edit it, then publish or save it as a draft.",
                ["DraftPublished"] = "Draft published and sent to {0} online player(s).",
                ["ConsolePublishUsage"] = "Usage: news.publish <index|id>",
                ["ConsoleNotDraft"] = "That announcement is already published.",
                ["ConsolePublished"] = "Published '{0}'",

                ["AudienceLabel"] = "WHO CAN SEE IT",
                ["AudienceEveryone"] = "EVERYONE",
                ["GroupOnly"] = "{0} ONLY",
                ["ExpireLabel"] = "AUTO-DELETE",
                ["ExpireNever"] = "NEVER",
                ["ExpireAfterDay"] = "AFTER 1 DAY",
                ["ExpireAfterDays"] = "AFTER {0} DAYS",
                ["ExpireInfo"] = "Deletes {0} (in {1})",
                ["ExpireCountsFromPublish"] = "Counts from when it is published",
                ["PollTimerLabel"] = "POLL CLOSES",
                ["PollTimerManual"] = "MANUALLY",
                ["PollTimerHour"] = "AFTER 1 HOUR",
                ["PollTimerHours"] = "AFTER {0} HOURS",
                ["PollTimerDays"] = "AFTER {0} DAYS",
                ["PollTimerDay"] = "AFTER 1 DAY",
                ["CountdownTitleField"] = "COUNTDOWN TITLE",
                ["CountdownTimeField"] = "COUNTS DOWN TO",
                ["CountdownNone"] = "e.g. 2d 4h, 90m or 2026-11-12 19:00 (server time)",
                ["CountdownEnds"] = "Ends {0} (in {1})",
                ["CountdownInvalid"] = "Not a time: use e.g. 2d 4h, 90m or 2026-11-12 19:00",
                ["CountdownPast"] = "That time has already passed",
                ["CountdownDefaultLabel"] = "STARTS IN",
                ["CountdownDone"] = "STARTED",
                ["CountdownDay"] = "1 DAY  ",
                ["CountdownDays"] = "{0} DAYS  ",
                ["TitleCounter"] = "{0} / {1}",
                ["BodyCounter"] = "{0} CHARACTERS",
                ["BodyCounterLong"] = "{0} CHARACTERS  •  LONG, PLAYERS WILL SCROLL",

                ["ImageOk"] = "✓ Direct image link",
                ["ImageHintImgur"] = "That's an imgur page, not the picture. Right-click the image > Copy image address (i.imgur.com/....png)",
                ["ImageHintPage"] = "That's a web page, not the picture. Use the direct image link (ends in .png or .jpg)",
                ["ImageHintDiscord"] = "Discord image links stop working after about a day. Upload the picture to imgur instead",
                ["ImageHintFormat"] = "Rust only shows PNG and JPG pictures",
                ["ImageHintNoExtension"] = "Doesn't end in .png or .jpg, so it may not show in game",
                ["ImageWarning"] = "Image link warning: {0}",

                ["FilterAll"] = "ALL",
                ["FilterPolls"] = "POLLS",
                ["SearchLabel"] = "SEARCH",
                ["NoMatches"] = "No posts match this filter.",
                ["ClearFilter"] = "SHOW ALL",

                ["SettingsButton"] = "SETTINGS",
                ["SettingsTitle"] = "NEWS SETTINGS",
                ["SettingSound"] = "NOTIFICATION SOUND",
                ["SettingPopups"] = "POP-UP NOTIFICATIONS",
                ["SettingPopupsHint"] = "Off: you get a short chat message instead",
                ["SettingJoin"] = "SHOW NEW POSTS WHEN I JOIN",
                ["SettingOn"] = "ON",
                ["SettingOff"] = "OFF",
                ["Done"] = "DONE",
                ["NewPostChat"] = "New announcement: {0} - type /news to read it.",

                ["PollClosesIn"] = "CLOSES IN {0}",
                ["PollResultWinner"] = "Poll closed: {0} - winner: {1} ({2}%, {3} votes).",
                ["PollResultWinnerOne"] = "Poll closed: {0} - winner: {1} ({2}%, 1 vote).",
                ["PollResultTie"] = "Poll closed: {0} - tie between {1} ({2} votes each).",
                ["PollResultNoVotes"] = "Poll closed: {0} - no votes were cast.",
                ["PollDiscordResults"] = "Poll results",

                ["SpanDaysHours"] = "{0}d {1}h",
                ["SpanHoursMinutes"] = "{0}h {1}m",
                ["SpanMinutes"] = "{0}m",
                ["SpanNow"] = "under a minute",
                ["AdminDeletesIn"] = "DELETES IN {0}",
                ["AdminPollClosesIn"] = "POLL CLOSES IN {0}"
            }, this);
        }

        private string Msg(string key, BasePlayer player = null, params object[] args)
        {
            var message = lang.GetMessage(key, this, player?.UserIDString);
            if (args == null || args.Length == 0) return message;
            try { return string.Format(message, args); }
            catch (FormatException) { return message; }
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

        private static int CountBodyLines(string text, int wrapChars)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            if (wrapChars < 16) wrapChars = 16;

            int lines = 0, pos = 0;
            while (pos <= text.Length)
            {
                int end = text.IndexOf('\n', pos);
                if (end < 0) end = text.Length;

                while (end - pos > wrapChars)
                {
                    int lastSpace = text.LastIndexOf(' ', pos + wrapChars - 1, wrapChars);
                    pos = lastSpace - pos > 15 ? lastSpace : pos + wrapChars;
                    while (pos < end && char.IsWhiteSpace(text[pos])) pos++;
                    lines++;
                }

                lines++;
                pos = end + 1;
            }
            return lines;
        }

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

        private static string Now() => DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture);

        private Announcement FindById(string id)
        {
            int index = FindIndexById(id);
            return index < 0 ? null : announcements[index];
        }

        private int FindIndexById(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < announcements.Count; i++)
                if (string.Equals(announcements[i].Id, id, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private int ResolveIndex(string indexOrId)
        {
            int index;
            if (int.TryParse(indexOrId, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                return index >= 0 && index < announcements.Count ? index : -1;
            return FindIndexById(indexOrId);
        }

        private static bool TryParseType(string value, out AnnouncementType type)
        {
            type = AnnouncementType.Info;
            return !string.IsNullOrEmpty(value) && char.IsLetter(value[0])
                && Enum.TryParse(value, true, out type) && Enum.IsDefined(typeof(AnnouncementType), type);
        }

        private static List<string> SplitCommandLine(ConsoleSystem.Arg arg, int required, out AnnouncementType type)
        {
            type = AnnouncementType.Info;
            var matches = CommandSplitRegex.Matches(arg.FullString.ToString());
            var values = new List<string>(matches.Count);
            foreach (Match match in matches) values.Add(match.Value.Trim('"'));

            if (values.Count > required && !matches[matches.Count - 1].Value.StartsWith("\""))
            {
                AnnouncementType parsed;
                if (TryParseType(values[values.Count - 1], out parsed))
                {
                    type = parsed;
                    values.RemoveAt(values.Count - 1);
                }
            }
            return values;
        }

        private string AuthorOf(ConsoleSystem.Arg arg) => arg.Connection != null ? arg.Connection.username : config.General.ServerName;

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
            set.RemoveWhere(id => FindById(id) == null);
        }

        private List<Announcement> GetDisplayOrder()
        {
            if (!displayOrderDirty && displayOrder.Count == announcements.Count) return displayOrder;
            displayOrder.Clear();
            foreach (var ann in announcements) if (ann.Pinned) displayOrder.Add(ann);
            foreach (var ann in announcements) if (!ann.Pinned) displayOrder.Add(ann);
            displayOrderDirty = false;
            return displayOrder;
        }

        private bool IsUnread(Announcement ann, ulong userId)
        {
            if (ann.ReadByPlayers.Contains(userId)) return false;
            long readAll;
            return !(storedData.ReadAllUpTo.TryGetValue(userId, out readAll) && ann.Timestamp <= readAll);
        }

        private static bool HasPoll(Announcement ann) => ann != null && ann.PollOptions.Count >= 2;

        private static bool IsPollOpen(Announcement ann) => HasPoll(ann) && !ann.PollClosed && (ann.PollEndsAt <= 0 || NowTicks < ann.PollEndsAt);

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
            return ann.PollVotes.TryGetValue(userId, out vote) ? vote : -1;
        }

        private static List<string> CleanPollOptions(IEnumerable<string> options)
        {
            var cleaned = new List<string>(MaxPollOptions);
            foreach (string option in options)
            {
                if (string.IsNullOrWhiteSpace(option)) continue;
                cleaned.Add(LimitText(option.Trim(), MaxPollOptionChars));
                if (cleaned.Count == MaxPollOptions) break;
            }
            return cleaned;
        }

        private static int Sum(int[] values)
        {
            int total = 0;
            foreach (int value in values) total += value;
            return total;
        }

        private int CountUnread(BasePlayer player)
        {
            ulong userId = player.userID;
            int count = 0;
            foreach (var ann in announcements)
                if (IsUnread(ann, userId) && CanSee(player, ann)) count++;
            return count;
        }

        private static long NowTicks => DateTime.UtcNow.Ticks;

        private static bool IsExpired(Announcement ann) => ann.ExpiresAt > 0 && NowTicks >= ann.ExpiresAt;

        private bool CanSee(BasePlayer player, Announcement ann)
        {
            if (player == null || ann == null || ann.Draft || IsExpired(ann)) return false;
            if (string.IsNullOrEmpty(ann.Group) || CanAdmin(player)) return true;
            return permission.UserHasGroup(player.UserIDString, ann.Group);
        }

        private Announcement NewestFor(BasePlayer player)
        {
            foreach (var ann in announcements) if (CanSee(player, ann)) return ann;
            return null;
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

        private static bool MatchesFilter(Announcement ann, ArchiveFilter filter)
        {
            if (filter == null) return true;
            if (filter.Type == "polls") { if (!HasPoll(ann)) return false; }
            else if (filter.Type != "all" && !string.Equals(ann.Type.ToString(), filter.Type, StringComparison.OrdinalIgnoreCase)) return false;
            return MatchesQuery(ann, filter.Query);
        }

        private static bool MatchesQuery(Announcement ann, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            return (ann.Title ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (ann.Text ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static readonly PlayerPrefs DefaultPrefs = new PlayerPrefs();

        private PlayerPrefs GetPrefs(ulong userId)
        {
            PlayerPrefs prefs;
            return storedData.PlayerSettings.TryGetValue(userId, out prefs) ? prefs : DefaultPrefs;
        }

        private List<string> AudienceGroups()
        {
            var groups = new List<string>();
            foreach (string group in permission.GetGroups())
                if (!string.IsNullOrEmpty(group) && !string.Equals(group, "default", StringComparison.OrdinalIgnoreCase)) groups.Add(group);
            groups.Sort(StringComparer.OrdinalIgnoreCase);
            return groups;
        }

        private string FormatSpan(long ticks, BasePlayer player = null)
        {
            var span = TimeSpan.FromTicks(Math.Max(0L, ticks));
            if (span.TotalDays >= 1) return Msg("SpanDaysHours", player, (int)span.TotalDays, span.Hours);
            if (span.TotalHours >= 1) return Msg("SpanHoursMinutes", player, (int)span.TotalHours, span.Minutes);
            if (span.TotalMinutes >= 1) return Msg("SpanMinutes", player, (int)span.TotalMinutes);
            return Msg("SpanNow", player);
        }

        private static string LocalTime(long utcTicks) => new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString(DateFormat, CultureInfo.InvariantCulture);

        private static readonly Regex SpanPartRegex = new Regex(@"(\d+)\s*([dhm])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SpanOnlyRegex = new Regex(@"^\s*(\d+\s*[dhm]\s*)+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static long ParseCountdown(string value, long nowTicks)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length == 0 || value == "-") return 0;

            if (SpanOnlyRegex.IsMatch(value))
            {
                long ticks = 0;
                foreach (Match m in SpanPartRegex.Matches(value))
                {
                    long n;
                    if (!long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return -1;
                    n = Math.Min(n, 36500L);
                    char unit = char.ToLowerInvariant(m.Groups[2].Value[0]);
                    ticks += n * (unit == 'd' ? TimeSpan.TicksPerDay : unit == 'h' ? TimeSpan.TicksPerHour : TimeSpan.TicksPerMinute);
                }
                return ticks > 0 ? nowTicks + Math.Min(ticks, 3650L * TimeSpan.TicksPerDay) : -1;
            }

            DateTime local;
            string[] formats = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd", "dd.MM.yyyy HH:mm", "dd.MM.yyyy" };
            if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out local))
                return local.ToUniversalTime().Ticks;

            TimeSpan clock;
            if (TimeSpan.TryParseExact(value, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out clock) && clock.TotalHours < 24)
            {
                var now = new DateTime(nowTicks, DateTimeKind.Utc).ToLocalTime();
                var at = now.Date + clock;
                if (at <= now) at = at.AddDays(1);
                return at.ToUniversalTime().Ticks;
            }
            return -1;
        }

        private static string ImageLinkProblem(string url)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri)) return null;
            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();
            string query = uri.Query.ToLowerInvariant();

            if (host == "imgur.com" || host == "www.imgur.com" || host == "m.imgur.com") return "ImageHintImgur";
            if (host.EndsWith("discordapp.com") || host.EndsWith("discordapp.net") || host.EndsWith("discord.com")) return "ImageHintDiscord";
            if (host == "prnt.sc" || host.EndsWith("prntscr.com") || host == "gyazo.com" || host == "www.gyazo.com"
                || host.EndsWith("drive.google.com") || host.EndsWith("photos.google.com")
                || (host.EndsWith("dropbox.com") && !query.Contains("raw=1") && !query.Contains("dl=1")))
                return "ImageHintPage";
            if (path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".jpeg")) return null;
            if (path.EndsWith(".gif") || path.EndsWith(".webp") || path.EndsWith(".svg") || path.EndsWith(".bmp") || path.EndsWith(".avif")) return "ImageHintFormat";
            return "ImageHintNoExtension";
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
                ["likes"] = ann.LikedPlayers.Count,
                ["pinned"] = ann.Pinned,
                ["pollOptions"] = HasPoll(ann) ? ann.PollOptions.ToArray() : new string[0],
                ["pollVotes"] = ann.PollVotes.Count,
                ["group"] = ann.Group ?? string.Empty,
                ["expiresAt"] = ann.ExpiresAt,
                ["pollEndsAt"] = ann.PollEndsAt,
                ["countdownTo"] = ann.CountdownTo
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

        private bool IsAdminArg(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null) return true;
            if (arg.IsAdmin) return true;
            return permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin);
        }

        private bool RequireAdmin(ConsoleSystem.Arg arg)
        {
            if (IsAdminArg(arg)) return true;
            SendReply(arg, Msg("NoPermissionCommand"));
            return false;
        }

        private BasePlayer AdminPlayer(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            return CanAdmin(player) ? player : null;
        }

        private bool DiscordConfigured => config.Discord.Enabled && !string.IsNullOrEmpty(config.Discord.WebhookUrl);

        private void PlaySound(BasePlayer player)
        {
            string prefab = config.Notification.NotificationSound;
            if (player == null || string.IsNullOrEmpty(prefab) || player.net?.connection == null || !GetPrefs(player.userID).Sound) return;
            EffectNetwork.Send(new Effect(prefab, player.transform.position, Vector3.zero), player.net.connection);
        }
        #endregion

        #region Configuration
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null) throw new JsonException("the file is empty");
                SaveConfig();
            }
            catch (Exception ex)
            {
                PrintError($"The configuration file could not be read, using the default settings: {ex.Message}");
                LoadDefaultConfig();
            }
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
            CheckScheduled();
        }

        private static bool HasExpiry(Announcement ann) => !ann.Draft && ann.ExpiresAt > 0;

        private static bool HasPollTimer(Announcement ann) => !ann.Draft && !ann.PollClosed && ann.PollEndsAt > 0 && HasPoll(ann);

        private void CheckScheduled()
        {
            long now = NowTicks;
            bool changed = false;

            for (int i = announcements.Count - 1; i >= 0; i--)
            {
                if (i >= announcements.Count) continue;
                var ann = announcements[i];
                if (HasExpiry(ann) && now >= ann.ExpiresAt)
                {
                    RemoveAnnouncement(ann);
                    changed = true;
                }
                else if (HasPollTimer(ann) && now >= ann.PollEndsAt)
                {
                    ann.PollClosed = true;
                    changed = true;
                    AnnouncePollResults(ann);
                }
            }

            if (changed) SaveAnnouncements();
            ScheduleNextCheck();
        }

        private void ScheduleNextCheck()
        {
            long next = 0;
            foreach (var ann in announcements)
            {
                if (HasExpiry(ann) && (next == 0 || ann.ExpiresAt < next)) next = ann.ExpiresAt;
                if (HasPollTimer(ann) && (next == 0 || ann.PollEndsAt < next)) next = ann.PollEndsAt;
            }

            bool running = scheduleTimer != null && !scheduleTimer.Destroyed;
            if (next == scheduledFor && running == (next != 0)) return;

            scheduleTimer?.Destroy();
            scheduleTimer = null;
            scheduledFor = next;
            if (next == 0) return;

            double seconds = (next - NowTicks) / (double)TimeSpan.TicksPerSecond;
            float delay = (float)Math.Max(MinScheduleSeconds, Math.Min(MaxScheduleSeconds, seconds));
            scheduleTimer = timer.Once(delay, () =>
            {
                scheduleTimer = null;
                CheckScheduled();
            });
        }

        private void AnnouncePollResults(Announcement ann)
        {
            Interface.CallHook("OnNewsPollClosed", BuildHookData(ann));
            if (!config.Polls.AnnounceResults) return;

            foreach (var p in BasePlayer.activePlayerList)
                if (p != null && p.IsConnected && CanView(p) && CanSee(p, ann))
                    SendReply(p, PollResultText(ann, p));

            if (DiscordConfigured && config.Discord.PostPollResults && (string.IsNullOrEmpty(ann.Group) || config.Discord.PostGroupOnlyPosts))
                SendPollResultsToDiscord(ann);
        }

        private string PollResultText(Announcement ann, BasePlayer player)
        {
            int[] counts = CountVotes(ann);
            int total = 0, best = 0, winner = -1, winners = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                total += counts[i];
                if (counts[i] > best) { best = counts[i]; winner = i; winners = 1; }
                else if (counts[i] == best) winners++;
            }
            if (total == 0) return Msg("PollResultNoVotes", player, ann.Title);
            if (winners == 1) return Msg(best == 1 ? "PollResultWinnerOne" : "PollResultWinner", player, ann.Title, ann.PollOptions[winner], Mathf.RoundToInt(100f * best / total), best);

            var tied = new StringBuilder();
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] != best) continue;
                if (tied.Length > 0) tied.Append(" / ");
                tied.Append(ann.PollOptions[i]);
            }
            return Msg("PollResultTie", player, ann.Title, tied.ToString(), best);
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

            var imageUrls = new Dictionary<string, string>();
            foreach (var ann in announcements)
                if (!string.IsNullOrEmpty(ann.ImageUrl)) imageUrls[ann.ImageUrl] = ann.ImageUrl;

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
            TrimToMaxStored();
            displayOrderDirty = true;
            ScheduleNextCheck();

            dataDirty = false;
            Interface.Oxide.DataFileSystem.WriteObject(DataFile, storedData);
        }

        private void TrimToMaxStored()
        {
            int over = announcements.Count - config.General.MaxStoredAnnouncements;
            if (over <= 0) return;

            int newest = -1;
            for (int i = 0; i < announcements.Count && newest < 0; i++)
                if (!announcements[i].Draft && !announcements[i].Pinned) newest = i;

            var dropped = Pool.Get<List<Announcement>>();
            for (int i = announcements.Count - 1; i > newest && over > 0 && newest >= 0; i--)
            {
                var old = announcements[i];
                if (old.Draft || old.Pinned) continue;
                announcements.RemoveAt(i);
                dropped.Add(old);
                over--;
            }
            foreach (var old in dropped) Interface.CallHook("OnNewsDeleted", BuildHookData(old));
            Pool.FreeUnmanaged(ref dropped);

            if (over > 0 && !maxStoredWarned)
            {
                maxStoredWarned = true;
                PrintWarning($"MaxStoredAnnouncements ({config.General.MaxStoredAnnouncements}) is used up by pinned posts and drafts, which are never removed automatically. Only the newest other post is kept until some are unpinned or deleted, or the limit is raised.");
            }
        }

        private void MarkDataDirty()
        {
            dataDirty = true;
        }

        private void LoadAnnouncements()
        {
            try { storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFile); }
            catch (Exception ex)
            {
                PrintError($"The data file could not be read, starting without stored announcements: {ex.Message}");
            }

            if (storedData == null) storedData = new StoredData();
            announcements = storedData.Announcements;

            if (PruneStaleMarkers() + PruneOldAutosaves() > 0) MarkDataDirty();
        }

        private int PruneStaleMarkers()
        {
            long oldest = long.MaxValue;
            foreach (var ann in announcements)
                if (ann.Timestamp < oldest) oldest = ann.Timestamp;
            return PruneBelow(storedData.LastSeenNews, oldest) + PruneBelow(storedData.ReadAllUpTo, oldest);
        }

        private static int PruneBelow(Dictionary<ulong, long> markers, long oldest)
        {
            var stale = Pool.Get<List<ulong>>();
            foreach (var marker in markers)
                if (marker.Value < oldest) stale.Add(marker.Key);
            int removed = stale.Count;
            foreach (ulong userId in stale) markers.Remove(userId);
            Pool.FreeUnmanaged(ref stale);
            return removed;
        }

        private int PruneOldAutosaves()
        {
            long oldest = NowTicks - AutosaveKeepDays * TimeSpan.TicksPerDay;
            var stale = Pool.Get<List<ulong>>();
            foreach (var entry in storedData.EditorAutosaves)
                if (entry.Value.SavedAt < oldest) stale.Add(entry.Key);
            int removed = stale.Count;
            foreach (ulong userId in stale) storedData.EditorAutosaves.Remove(userId);
            Pool.FreeUnmanaged(ref stale);
            return removed;
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !CanView(player)) return;
            var newest = NewestFor(player);
            if (newest == null) return;

            ulong userId = player.userID;
            long seenBefore;
            storedData.LastSeenNews.TryGetValue(userId, out seenBefore);

            bool wantPopup = config.General.ShowNewsOnConnect && GetPrefs(userId).JoinPopup && newest.Timestamp > seenBefore;
            bool wantReminder = config.General.UnreadReminderOnConnect && remindedPlayers.Add(userId);
            if (!wantPopup && !wantReminder) return;

            timer.Once(2f, () =>
            {
                if (player == null || !player.IsConnected || !CanView(player)) return;
                var current = NewestFor(player);
                if (current == null) return;

                bool showPopup = wantPopup && !playersWithUiOpen.Contains(userId);

                if (wantReminder)
                {
                    int fresh = 0;
                    foreach (var a in announcements)
                        if (a.Timestamp > seenBefore && CanSee(player, a) && IsUnread(a, userId)) fresh++;
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
            foreach (var t in readRewardTimers.Values) t?.Destroy();
            foreach (var t in redrawTimers.Values) t?.Destroy();
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
            archiveFilters.Clear();
            adminViews.Clear();
            redrawCooldowns.Clear();
            redrawTimers.Clear();
            surfaceColors.Clear();

            if (dataDirty) SaveAnnouncements();
            scheduleTimer?.Destroy();
            scheduleTimer = null;
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
            archiveFilters.Remove(id);
            adminViews.Remove(id);
            redrawCooldowns.Remove(id);
            CancelRedraw(id);
            CancelReadRewardTimer(id);
            CancelAutoClose(id);
            CancelNotificationTimer(id);
        }
        #endregion

        #region Publishing
        private int NotifyPlayers(Announcement ann)
        {
            if (ann == null || ann.Draft) return 0;
            int reached = 0;
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null || !p.IsConnected || !CanView(p) || !CanSee(p, ann)) continue;
                reached++;
                MarkSeen(p.userID, ann.Timestamp);
                if (!GetPrefs(p.userID).Popups)
                {
                    SendReply(p, Msg("NewPostChat", p, ann.Title));
                    continue;
                }
                if (config.Notification.Enabled || playersWithUiOpen.Contains(p.userID))
                    ShowNotification(p, ann);
                else
                    ShowPopup(p, ann, PopupOrigin.Broadcast, true);
            }
            return reached;
        }

        private static void ApplyPublishTimes(Announcement ann, long now)
        {
            ann.Timestamp = now;
            ann.Date = Now();
            ann.ExpiresAt = ann.ExpireDays > 0 ? now + ann.ExpireDays * TimeSpan.TicksPerDay : 0;
            ann.PollEndsAt = HasPoll(ann) && ann.PollHours > 0 ? now + ann.PollHours * TimeSpan.TicksPerHour : 0;
        }

        private void AddAnnouncement(Announcement ann, bool draft)
        {
            ann.Draft = draft;
            if (draft) { ann.Timestamp = NowTicks; ann.Date = Now(); }
            else ApplyPublishTimes(ann, NowTicks);
            announcements.Insert(0, ann);
            SaveAnnouncements();
            RegisterImage(ann.ImageUrl);
            if (!draft) Interface.CallHook("OnNewsBroadcast", BuildHookData(ann));
        }

        private void RemoveAnnouncement(Announcement ann)
        {
            if (!announcements.Remove(ann)) return;
            displayOrderDirty = true;
            Interface.CallHook("OnNewsDeleted", BuildHookData(ann));
        }

        private void PublishDraft(Announcement ann)
        {
            ann.Draft = false;
            ApplyPublishTimes(ann, NowTicks);
            announcements.Remove(ann);
            announcements.Insert(0, ann);
            SaveAnnouncements();
            RegisterImage(ann.ImageUrl);
            Interface.CallHook("OnNewsBroadcast", BuildHookData(ann));
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
                Timestamp = NowTicks,
                Pinned = pinned
            };
            if (pollOptions != null && pollOptions.Count >= 2) ann.PollOptions = pollOptions;

            AddAnnouncement(ann, false);
            return ann;
        }
        #endregion

        #region Commands
        [ConsoleCommand("news.show")]
        private void CmdNewsShow(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            AnnouncementType type;
            var values = SplitCommandLine(arg, 3, out type);
            if (values.Count < 3)
            {
                SendReply(arg, Msg("ConsoleShowUsage"));
                return;
            }

            string image = values[1] == "-" ? string.Empty : values[1];
            string text = values[2];
            for (int i = 3; i < values.Count; i++) text += " " + values[i];

            var ann = CreateAnnouncement(values[0], image, text, type, AuthorOf(arg), false);
            SendToDiscord(ann);
            NotifyPlayers(ann);

            SendReply(arg, Msg("NewsBroadcasted"));
            string problem = ImageLinkProblem(ann.ImageUrl);
            if (problem != null) SendReply(arg, Msg("ImageWarning", null, Msg(problem)));
        }

        [ConsoleCommand("news.poll")]
        private void CmdNewsPoll(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            AnnouncementType type;
            var values = SplitCommandLine(arg, 3, out type);
            if (values.Count < 3)
            {
                SendReply(arg, Msg("ConsolePollUsage"));
                return;
            }

            var options = CleanPollOptions(values[2].Split('|'));
            if (options.Count < 2)
            {
                SendReply(arg, Msg("PollNeedsOptions"));
                return;
            }

            var ann = CreateAnnouncement(values[0], string.Empty, values[1], type, AuthorOf(arg), false, options);
            SendToDiscord(ann);
            NotifyPlayers(ann);
            SendReply(arg, Msg("NewsBroadcasted"));
        }

        [ConsoleCommand("news.trigger")]
        private void CmdNewsTrigger(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

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

            Announcement ann;
            if (arg.Args.Length < 2) ann = NewestFor(player);
            else
            {
                int index = ResolveIndex(arg.GetString(1));
                if (index < 0)
                {
                    SendReply(arg, Msg("ConsoleInvalidIndex"));
                    return;
                }
                ann = announcements[index];
            }

            if (ann == null || !CanView(player) || !CanSee(player, ann))
            {
                SendReply(arg, Msg(announcements.Count == 0 ? "NoAnnouncementsAvailable" : "ConsoleTriggerHidden", null, player.displayName));
                return;
            }

            ShowPopup(player, ann, PopupOrigin.Direct);
            SendReply(arg, Msg("PopupTriggered", null, player.displayName));
        }

        [ConsoleCommand("news.delete")]
        private void CmdNewsDelete(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

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
            RemoveAnnouncement(removed);
            SaveAnnouncements();
            SendReply(arg, Msg("ConsoleDeleted", null, removed.Title));
        }

        [ConsoleCommand("news.resend")]
        private void CmdNewsResend(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

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

        [ConsoleCommand("news.publish")]
        private void CmdNewsPublish(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;
            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, Msg("ConsolePublishUsage"));
                return;
            }

            int index = ResolveIndex(arg.GetString(0));
            if (index < 0)
            {
                SendReply(arg, Msg("ConsoleInvalidIndex"));
                return;
            }
            var ann = announcements[index];
            if (!ann.Draft)
            {
                SendReply(arg, Msg("ConsoleNotDraft"));
                return;
            }

            PublishDraft(ann);
            SendToDiscord(ann);
            NotifyPlayers(ann);
            SendReply(arg, Msg("ConsolePublished", null, ann.Title));
        }

        [ConsoleCommand("news.list")]
        private void CmdNewsList(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

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
                if (a.Draft) sb.Append(" [DRAFT]");
                if (a.Pinned) sb.Append(" [PINNED]");
                if (!string.IsNullOrEmpty(a.Group)) sb.Append($" [GROUP {a.Group}]");
                if (a.ExpiresAt > 0) sb.Append($" [DELETES {LocalTime(a.ExpiresAt)}]");
                if (HasPoll(a) && !a.PollClosed && a.PollEndsAt > 0) sb.Append($" [POLL CLOSES {LocalTime(a.PollEndsAt)}]");
                sb.AppendLine($"  (id {a.Id}, {a.ReadByPlayers.Count} reads, {a.LikedPlayers.Count} likes)");
            }
            SendReply(arg, sb.ToString().TrimEnd());
        }

        [ChatCommand("news")]
        private void CmdChatNews(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            string sub = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
            if (sub != "settings" && sub != "options" && sub != "read" && sub != "help") currentScreens.Remove(player.userID);

            switch (sub)
            {
                case "admin":
                case "manage":
                    if (!CanAdmin(player))
                    {
                        SendReply(player, Msg("NoPermissionCommand", player));
                        return;
                    }
                    adminViews.Remove(player.userID);
                    ShowAdminList(player, 0);
                    return;

                case "latest":
                case "last":
                    if (!RequireView(player, true)) return;
                    var newest = NewestFor(player);
                    if (newest == null)
                    {
                        SendReply(player, Msg("NoNewsHistory", player));
                        return;
                    }
                    ShowPopup(player, newest, PopupOrigin.Direct);
                    return;

                case "settings":
                case "options":
                    if (!RequireView(player, true)) return;
                    ShowPlayerSettings(player);
                    return;

                case "read":
                    if (!RequireView(player, true)) return;
                    MarkAllRead(player);
                    return;

                case "help":
                    SendReply(player, Msg("ChatHelp", player) + Msg("ChatHelpSettings", player) + (CanAdmin(player) ? Msg("ChatHelpAdmin", player) : string.Empty));
                    return;

                default:
                    if (!RequireView(player, true)) return;
                    archiveFilters.Remove(player.userID);
                    ShowHistory(player, 0);
                    return;
            }
        }

        [ConsoleCommand("news.page")]
        private void CmdConsolePage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!RequireView(player)) return;

            if (string.Equals(arg.GetString(1), "reset", StringComparison.OrdinalIgnoreCase)) archiveFilters.Remove(player.userID);
            archivePages[player.userID] = arg.GetInt(0, 0);
            RedrawThrottled(player, UiScreen.Archive);
        }

        [ConsoleCommand("news.filter")]
        private void CmdArchiveFilter(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!RequireView(player)) return;

            string value = arg.GetString(0).ToLowerInvariant();
            AnnouncementType type;
            var filter = GetArchiveFilter(player.userID);
            if (value == "polls") filter.Type = "polls";
            else filter.Type = TryParseType(value, out type) ? type.ToString() : "all";
            archivePages[player.userID] = 0;
            RedrawThrottled(player, UiScreen.Archive);
        }

        [ConsoleCommand("news.search")]
        private void CmdArchiveSearch(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!RequireView(player)) return;

            string query = arg.Args == null ? string.Empty : string.Join(" ", arg.Args).Trim();
            GetArchiveFilter(player.userID).Query = LimitText(query, MaxSearchChars).Trim();
            archivePages[player.userID] = 0;
            RedrawThrottled(player, UiScreen.Archive);
        }

        [ConsoleCommand("news.search.clear")]
        private void CmdArchiveSearchClear(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!RequireView(player)) return;

            archiveFilters.Remove(player.userID);
            archivePages[player.userID] = 0;
            RedrawThrottled(player, UiScreen.Archive);
        }

        private void RedrawThrottled(BasePlayer player, UiScreen screen)
        {
            ulong userId = player.userID;
            float now = UnityEngine.Time.realtimeSinceStartup;
            float nextAllowed;
            if (!redrawCooldowns.TryGetValue(userId, out nextAllowed) || now >= nextAllowed)
            {
                CancelRedraw(userId);
                redrawCooldowns[userId] = now + RedrawCooldownSeconds;
                Redraw(player, screen);
                return;
            }

            CancelRedraw(userId);
            redrawTimers[userId] = timer.Once(nextAllowed - now, () =>
            {
                redrawTimers.Remove(userId);
                if (player == null || !player.IsConnected) return;
                redrawCooldowns[userId] = UnityEngine.Time.realtimeSinceStartup + RedrawCooldownSeconds;
                Redraw(player, screen);
            });
        }

        private void Redraw(BasePlayer player, UiScreen screen)
        {
            if (screen == UiScreen.Archive) ShowHistory(player, GetArchivePage(player.userID));
            else if (CanAdmin(player)) ShowAdminList(player, GetAdminView(player.userID).Page);
        }

        private void FlushRedraw(BasePlayer player)
        {
            ulong userId = player.userID;
            if (!redrawTimers.ContainsKey(userId)) return;
            CancelRedraw(userId);
            UiScreen screen;
            if (currentScreens.TryGetValue(userId, out screen) && (screen == UiScreen.Archive || screen == UiScreen.Admin)) Redraw(player, screen);
        }

        private void CancelRedraw(ulong userId)
        {
            Timer t;
            if (redrawTimers.TryGetValue(userId, out t))
            {
                t?.Destroy();
                redrawTimers.Remove(userId);
            }
        }

        [ConsoleCommand("news.view")]
        private void CmdConsoleView(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!RequireView(player)) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null || !CanSee(player, ann))
            {
                DestroyNotification(player);
                return;
            }

            var origin = string.Equals(arg.GetString(1), "archive", StringComparison.OrdinalIgnoreCase) ? PopupOrigin.Archive : PopupOrigin.Direct;
            ShowPopup(player, ann, origin);
        }

        [ConsoleCommand("news.close")]
        private void CmdConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) DestroyUI(player);
        }

        [ConsoleCommand("news.close.notif")]
        private void CmdCloseNotif(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) DestroyNotification(player);
        }

        [ConsoleCommand("news.markread")]
        private void CmdMarkRead(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!RequireView(player)) return;

            MarkAllRead(player);
            ShowHistory(player, GetArchivePage(player.userID));
        }

        private int MarkAllRead(BasePlayer player)
        {
            ulong userId = player.userID;
            int unread = CountUnread(player);
            if (unread == 0)
            {
                SendReply(player, Msg("NothingUnread", player));
                return 0;
            }

            storedData.ReadAllUpTo[userId] = NewestFor(player).Timestamp;
            MarkDataDirty();
            SendReply(player, Msg("MarkedAllRead", player, unread));
            return unread;
        }

        [ConsoleCommand("news.vote")]
        private void CmdNewsVote(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!RequireView(player)) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null || !CanSee(player, ann) || !IsPollOpen(ann)) return;

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

            if (IsPopupOpen(userId, ann.Id))
            {
                var container = new CuiElementContainer();
                AddPollBlock(container, player, ann, false, true);
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
            var player = arg.Player();
            if (!RequireView(player)) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null || !CanSee(player, ann)) return;

            ulong userId = player.userID;
            float now = UnityEngine.Time.realtimeSinceStartup;
            float nextAllowed;
            if (likeCooldowns.TryGetValue(userId, out nextAllowed) && now < nextAllowed) return;
            likeCooldowns[userId] = now + LikeCooldownSeconds;

            bool addedLike = !ann.LikedPlayers.Remove(userId);
            if (addedLike) ann.LikedPlayers.Add(userId);

            if (addedLike && config.Rewards.EnableLikeReward && !ann.LikeRewardedPlayers.Contains(userId)
                && GiveRewards(player, config.Rewards.LikeRewards, "RewardLike"))
                ann.LikeRewardedPlayers.Add(userId);

            MarkDataDirty();

            Interface.CallHook("OnNewsLiked", player, BuildHookData(ann), addedLike);
            StopAutoClose(player);

            if (IsPopupOpen(userId, ann.Id))
            {
                var container = new CuiElementContainer();
                AddLikeButton(container, player, ann, true);
                CuiHelper.DestroyUi(player, LikeButtonName);
                CuiHelper.AddUi(player, container);
            }
            else
            {
                ShowPopup(player, ann, PopupOrigin.Direct);
            }
        }

        [ConsoleCommand("news.countdown")]
        private void CmdNewsCountdown(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!RequireView(player)) return;

            var ann = FindById(arg.GetString(0));
            if (ann == null || ann.CountdownTo <= 0 || !IsPopupOpen(player.userID, ann.Id)) return;

            var container = new CuiElementContainer();
            AddCountdownBlock(container, player, ann, string.IsNullOrEmpty(ann.ImageUrl) ? 0.03f : 0.5f);
            CuiHelper.DestroyUi(player, CountdownName);
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("news.settings")]
        private void CmdNewsSettings(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!RequireView(player)) return;
            ShowPlayerSettings(player);
        }

        [ConsoleCommand("news.settings.toggle")]
        private void CmdNewsSettingsToggle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!RequireView(player)) return;

            PlayerPrefs prefs;
            if (!storedData.PlayerSettings.TryGetValue(player.userID, out prefs))
            {
                prefs = new PlayerPrefs();
                storedData.PlayerSettings[player.userID] = prefs;
            }
            switch (arg.GetString(0).ToLowerInvariant())
            {
                case "sound": prefs.Sound = !prefs.Sound; break;
                case "popups": prefs.Popups = !prefs.Popups; break;
                case "join": prefs.JoinPopup = !prefs.JoinPopup; break;
                default: return;
            }
            MarkDataDirty();
            ShowPlayerSettings(player, true);
        }

        [ConsoleCommand("news.admin")]
        private void CmdNewsAdmin(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            var player = arg.Player();
            if (player == null) return;

            currentScreens.Remove(player.userID);
            adminViews.Remove(player.userID);
            ShowAdminList(player, 0);
        }

        [ConsoleCommand("news.admin.filter")]
        private void CmdNewsAdminFilter(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            string tab = arg.GetString(0).ToLowerInvariant();
            var view = GetAdminView(player.userID);
            if (tab == "reset") view.Query = string.Empty;
            view.Tab = Array.IndexOf(AdminTabs, tab) >= 0 ? tab : "all";
            view.Page = 0;
            RedrawThrottled(player, UiScreen.Admin);
        }

        [ConsoleCommand("news.admin.search")]
        private void CmdNewsAdminSearch(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            string query = arg.Args == null ? string.Empty : string.Join(" ", arg.Args);
            var view = GetAdminView(player.userID);
            view.Query = LimitText(query.Trim(), MaxSearchChars).Trim();
            view.Page = 0;
            RedrawThrottled(player, UiScreen.Admin);
        }

        [ConsoleCommand("news.admin.sort")]
        private void CmdNewsAdminSort(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            var view = GetAdminView(player.userID);
            view.Sort = (view.Sort + 1) % AdminSorts.Length;
            view.Page = 0;
            RedrawThrottled(player, UiScreen.Admin);
        }

        [ConsoleCommand("news.admin.resume")]
        private void CmdNewsAdminResume(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            EditorSession session;
            if (!storedData.EditorAutosaves.TryGetValue(player.userID, out session))
            {
                ShowAdminList(player, 0);
                return;
            }

            if (!session.IsNew)
            {
                var target = FindById(session.EditingId);
                if (target != null) session.IsDraft = target.Draft;
                else
                {
                    session.EditingId = string.Empty;
                    session.IsDraft = false;
                    session.Notify = true;
                    session.Discord = DiscordConfigured;
                }
            }
            editors[player.userID] = session;
            ShowEditor(player);
        }

        [ConsoleCommand("news.admin.discard")]
        private void CmdNewsAdminDiscard(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            if (storedData.EditorAutosaves.Remove(player.userID))
            {
                MarkDataDirty();
                SendReply(player, Msg("UnsavedDiscarded", player));
            }
            editors.Remove(player.userID);
            ShowAdminList(player, arg.GetInt(0, 0));
        }

        [ConsoleCommand("news.admin.page")]
        private void CmdNewsAdminPage(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            GetAdminView(player.userID).Page = arg.GetInt(0, 0);
            RedrawThrottled(player, UiScreen.Admin);
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

            var session = new EditorSession
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
                    LikedPlayers = new HashSet<ulong>(original.LikedPlayers),
                    Draft = original.Draft,
                    Group = original.Group,
                    ExpireDays = original.ExpireDays,
                    ExpiresAt = original.ExpiresAt,
                    PollHours = original.PollHours,
                    PollEndsAt = original.PollEndsAt,
                    CountdownTo = original.CountdownTo,
                    CountdownLabel = original.CountdownLabel
                },
                EditingId = original.Id,
                IsDraft = original.Draft,
                Notify = original.Draft,
                Discord = original.Draft && DiscordConfigured,
                Poll = HasPoll(original),
                PollClosed = original.PollClosed,
                CountdownInput = original.CountdownTo > 0 ? LocalTime(original.CountdownTo) : string.Empty
            };
            session.LoadPollOptions(original);
            editors[player.userID] = session;
            ShowEditor(player);
        }

        [ConsoleCommand("news.admin.copy")]
        private void CmdNewsAdminCopy(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            var original = FindById(arg.GetString(0));
            if (original == null) return;

            bool futureCountdown = original.CountdownTo > NowTicks;
            var session = new EditorSession
            {
                Draft = new Announcement
                {
                    Title = original.Title,
                    ImageUrl = original.ImageUrl,
                    Text = original.Text,
                    Type = original.Type,
                    Author = player.displayName,
                    Date = Now(),
                    Group = original.Group,
                    ExpireDays = original.ExpireDays,
                    PollHours = original.PollHours,
                    CountdownTo = futureCountdown ? original.CountdownTo : 0,
                    CountdownLabel = original.CountdownLabel
                },
                Discord = DiscordConfigured,
                Poll = HasPoll(original),
                CountdownInput = futureCountdown ? LocalTime(original.CountdownTo) : string.Empty
            };
            session.LoadPollOptions(original);
            editors[player.userID] = session;
            SendReply(player, Msg("Copied", player, Ellipsize(original.Title, 40)));
            ShowEditor(player);
        }

        [ConsoleCommand("news.admin.publish")]
        private void CmdNewsAdminPublish(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            var ann = FindById(arg.GetString(0));
            if (ann != null && ann.Draft)
            {
                PublishDraft(ann);
                SendToDiscord(ann);
                SendReply(player, Msg("DraftPublished", player, NotifyPlayers(ann)));
            }
            ShowAdminList(player, arg.GetInt(1, 0));
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
            if (ann == null || ann.Draft) return;

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
            var displayList = Pool.Get<List<Announcement>>();
            FillAdminOrder(player.userID, displayList);
            var sel = GetAdminSelection(player.userID);

            int start = Math.Max(0, page * perPage);
            int end = Math.Min(start + perPage, displayList.Count);

            bool allSelected = true;
            for (int i = start; i < end; i++) { if (!sel.Contains(displayList[i].Id)) { allSelected = false; break; } }

            for (int i = start; i < end; i++)
            {
                if (allSelected) sel.Remove(displayList[i].Id);
                else sel.Add(displayList[i].Id);
            }
            Pool.FreeUnmanaged(ref displayList);

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

            var edited = Pool.Get<List<Announcement>>();
            foreach (string id in sel)
            {
                var ann = FindById(id);
                if (ann == null || ann.Pinned == pin) continue;
                ann.Pinned = pin;
                edited.Add(ann);
            }

            int changed = edited.Count;
            if (changed > 0)
            {
                SaveAnnouncements();
                foreach (var ann in edited) Interface.CallHook("OnNewsEdited", BuildHookData(ann));
                SendReply(player, Msg(pin ? "BulkPinned" : "BulkUnpinned", player, changed));
            }
            Pool.FreeUnmanaged(ref edited);
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
            int removed = sel.Count;
            if (removed > 0)
            {
                foreach (string id in sel) RemoveAnnouncement(FindById(id));
                adminSelections.Remove(player.userID);
                SaveAnnouncements();
                SendReply(player, Msg("BulkDeleted", player, removed));
            }
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

            var ann = FindById(arg.GetString(0));
            if (ann != null)
            {
                RemoveAnnouncement(ann);
                SaveAnnouncements();
            }
            ShowAdminList(player, arg.GetInt(1, 0));
        }

        [ConsoleCommand("news.confirm.close")]
        private void CmdConfirmClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CuiHelper.DestroyUi(player, ConfirmLayer);
        }

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

        private void AutosaveEditor(BasePlayer player, EditorSession session)
        {
            session.SavedAt = NowTicks;
            storedData.EditorAutosaves[player.userID] = session;
            MarkDataDirty();
        }

        private void CloseEditor(ulong userId)
        {
            EditorSession session, saved;
            if (editors.TryGetValue(userId, out session) && storedData.EditorAutosaves.TryGetValue(userId, out saved) && ReferenceEquals(session, saved))
            {
                storedData.EditorAutosaves.Remove(userId);
                MarkDataDirty();
            }
            editors.Remove(userId);
        }

        [ConsoleCommand("news.editor.input")]
        private void CmdEditorInput(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            var session = GetEditor(player);
            if (session == null || arg.Args == null || arg.Args.Length < 1) return;

            string field = arg.GetString(0).ToLowerInvariant();

            string fullStr = arg.FullString.ToString();
            string value;
            if (fullStr.StartsWith(field, StringComparison.OrdinalIgnoreCase))
            {
                value = fullStr.Substring(field.Length).TrimStart();
                if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
                    value = value.Substring(1, value.Length - 2);
            }
            else
            {
                value = string.Join(" ", arg.Args, 1, arg.Args.Length - 1);
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
                        SendReply(player, Msg("InvalidImageUrl", player));
                        ShowEditor(player);
                        return;
                    }
                    break;
                case "cdlabel":
                    ann.CountdownLabel = LimitText(value, MaxCountdownLabelChars).Trim();
                    break;
                case "cdtime":
                    session.CountdownInput = LimitText(value, 40).Trim();
                    long target = ParseCountdown(session.CountdownInput, NowTicks);
                    ann.CountdownTo = target > 0 ? target : 0;
                    if (target > 0) session.CountdownInput = LocalTime(target);
                    break;
            }
            AutosaveEditor(player, session);
            RefreshEditorHint(player, session, field);
        }

        [ConsoleCommand("news.editor.settype")]
        private void CmdEditorSetType(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            var session = GetEditor(player);
            if (session == null) return;

            AnnouncementType type;
            if (TryParseType(arg.GetString(0), out type))
            {
                session.Draft.Type = type;
                AutosaveEditor(player, session);
            }
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
                case "audience":
                {
                    var choices = new List<string> { string.Empty };
                    choices.AddRange(AudienceGroups());
                    int at = choices.FindIndex(g => string.Equals(g, session.Draft.Group ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    session.Draft.Group = choices[(at + 1) % choices.Count];
                    break;
                }
                case "expire":
                    session.Draft.ExpireDays = NextPreset(ExpirePresets, session.Draft.ExpireDays);
                    session.ExpireChanged = true;
                    break;
                case "polltimer":
                    session.Draft.PollHours = NextPreset(PollTimerPresets, session.Draft.PollHours);
                    session.PollTimerChanged = true;
                    break;
                default:
                    return;
            }
            AutosaveEditor(player, session);
            ShowEditor(player);
        }

        private static int NextPreset(int[] presets, int current)
        {
            int at = Array.IndexOf(presets, current);
            return presets[(at + 1) % presets.Length];
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
        private void CmdEditorSave(ConsoleSystem.Arg arg) => SaveEditor(AdminPlayer(arg), false);

        [ConsoleCommand("news.editor.savedraft")]
        private void CmdEditorSaveDraft(ConsoleSystem.Arg arg) => SaveEditor(AdminPlayer(arg), true);

        private void SaveEditor(BasePlayer player, bool asDraft)
        {
            var session = GetEditor(player);
            if (session == null) return;
            if (!session.Publishes) asDraft = false;

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
            bool published;
            bool pollJustClosed = false;
            if (session.IsNew)
            {
                ann = new Announcement
                {
                    Id = NewAnnouncementId(),
                    Title = draft.Title,
                    ImageUrl = NormalizeImageUrl(draft.ImageUrl),
                    Text = NormalizeInputBody(draft.Text),
                    Author = string.IsNullOrEmpty(draft.Author) ? player.displayName : draft.Author,
                    Type = draft.Type,
                    Pinned = draft.Pinned
                };
                if (pollOptions != null && pollOptions.Count >= 2) ann.PollOptions = pollOptions;
                ApplyEditorSettings(draft, ann);
                AddAnnouncement(ann, asDraft);
                published = !asDraft;
            }
            else
            {
                ann = FindById(session.EditingId);
                if (ann == null)
                {
                    SendReply(player, Msg("EditTargetGone", player));
                    CloseEditor(player.userID);
                    ShowAdminList(player, 0);
                    return;
                }

                bool wasClosed = ann.PollClosed;
                ann.Title = draft.Title;
                ann.ImageUrl = NormalizeImageUrl(draft.ImageUrl);
                ann.Text = NormalizeInputBody(draft.Text);
                ann.Type = draft.Type;
                ann.Pinned = draft.Pinned;
                ApplyPollEdit(ann, session, pollOptions, slotMap);
                ApplyEditorSettings(draft, ann);

                if (ann.Draft && !asDraft)
                {
                    PublishDraft(ann);
                    published = true;
                }
                else
                {
                    published = false;
                    if (!ann.Draft)
                    {
                        long now = NowTicks;
                        ann.EditedDate = Now();
                        if (session.ExpireChanged) ann.ExpiresAt = ann.ExpireDays > 0 ? now + ann.ExpireDays * TimeSpan.TicksPerDay : 0;
                        if (!HasPoll(ann)) ann.PollEndsAt = 0;
                        else if (session.PollTimerChanged) ann.PollEndsAt = ann.PollHours > 0 ? now + ann.PollHours * TimeSpan.TicksPerHour : 0;
                        pollJustClosed = !wasClosed && ann.PollClosed && HasPoll(ann);
                    }
                    SaveAnnouncements();
                    RegisterImage(ann.ImageUrl);
                    if (!ann.Draft) Interface.CallHook("OnNewsEdited", BuildHookData(ann));
                }
            }

            CloseEditor(player.userID);
            if (pollJustClosed) AnnouncePollResults(ann);

            if (asDraft)
            {
                ShowAdminList(player, 0);
                SendReply(player, Msg("DraftSaved", player));
                return;
            }

            if (published && session.Discord) SendToDiscord(ann);
            int reached = session.Notify ? NotifyPlayers(ann) : 0;
            ShowAdminList(player, 0);

            if (!published) SendReply(player, Msg("AnnouncementUpdated", player));
            else if (session.Notify) SendReply(player, Msg("AnnouncementSavedNotified", player, reached));
            else SendReply(player, Msg("PublishedNoNotify", player));
        }

        private static void ApplyEditorSettings(Announcement from, Announcement to)
        {
            to.Group = from.Group;
            to.ExpireDays = from.ExpireDays;
            to.PollHours = HasPoll(to) ? from.PollHours : 0;
            to.CountdownTo = from.CountdownTo;
            to.CountdownLabel = from.CountdownLabel;
        }

        [ConsoleCommand("news.editor.cancel")]
        private void CmdEditorCancel(ConsoleSystem.Arg arg)
        {
            var player = AdminPlayer(arg);
            if (player == null) return;

            CloseEditor(player.userID);
            ShowAdminList(player, 0);
        }
        #endregion

        #region API

        private string API_PostNews(string title, string text, string type = "Info", string imageUrl = "", string author = "")
        {
            return API_CreateNews(new Dictionary<string, object>
            {
                ["title"] = title,
                ["text"] = text,
                ["type"] = type,
                ["imageUrl"] = imageUrl,
                ["author"] = author
            });
        }

        private string API_CreateNews(Dictionary<string, object> options)
        {
            if (options == null) return null;
            string title = LimitText(OptionText(options, "title"), MaxTitleChars).Trim();
            if (title.Length == 0) return null;

            string group = OptionText(options, "group").Trim();
            if (group.Length > 0 && !permission.GroupExists(group))
            {
                PrintWarning($"API_CreateNews: \"{title}\" was not posted, the group '{group}' does not exist.");
                return null;
            }

            AnnouncementType type;
            TryParseType(OptionText(options, "type"), out type);
            string author = OptionText(options, "author").Trim();
            var ann = new Announcement
            {
                Id = NewAnnouncementId(),
                Title = title,
                ImageUrl = NormalizeImageUrl(OptionText(options, "imageUrl")),
                Text = NormalizeInputBody(OptionText(options, "text")),
                Author = author.Length > 0 ? author : config.General.ServerName,
                Type = type,
                Pinned = OptionFlag(options, "pinned", false),
                Group = group,
                ExpireDays = (int)Math.Max(0L, Math.Min(365L, OptionNumber(options, "expireDays"))),
                CountdownLabel = LimitText(OptionText(options, "countdownLabel"), MaxCountdownLabelChars).Trim()
            };

            object value;
            if (options.TryGetValue("countdownTo", out value) && value is DateTime) ann.CountdownTo = ((DateTime)value).ToUniversalTime().Ticks;
            else ann.CountdownTo = Math.Max(0L, OptionNumber(options, "countdownTo"));

            var pollOptions = options.TryGetValue("pollOptions", out value) ? value as System.Collections.IEnumerable : null;
            if (pollOptions != null && !(value is string))
            {
                var texts = Pool.Get<List<string>>();
                foreach (object option in pollOptions) texts.Add(option?.ToString());
                var cleaned = CleanPollOptions(texts);
                Pool.FreeUnmanaged(ref texts);
                if (cleaned.Count >= 2)
                {
                    ann.PollOptions = cleaned;
                    ann.PollHours = (int)Math.Max(0L, Math.Min(720L, OptionNumber(options, "pollHours")));
                }
            }

            bool draft = OptionFlag(options, "draft", false);
            AddAnnouncement(ann, draft);
            if (!draft)
            {
                if (OptionFlag(options, "discord", true)) SendToDiscord(ann);
                if (OptionFlag(options, "notify", true)) NotifyPlayers(ann);
            }
            return ann.Id;
        }

        private bool API_DeleteNews(string id)
        {
            var ann = FindById(id);
            if (ann == null) return false;
            RemoveAnnouncement(ann);
            SaveAnnouncements();
            return true;
        }

        private Dictionary<string, object> API_GetNews(string id) => BuildHookData(FindById(id));

        private List<Dictionary<string, object>> API_GetNewsList()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var ann in announcements)
                if (!ann.Draft && !IsExpired(ann)) list.Add(BuildHookData(ann));
            return list;
        }

        private int API_GetUnreadCount(BasePlayer player) => player != null && CanView(player) ? CountUnread(player) : 0;

        private bool API_ShowNews(BasePlayer player, string id)
        {
            var ann = FindById(id);
            if (player == null || !player.IsConnected || ann == null || !CanView(player) || !CanSee(player, ann)) return false;
            ShowPopup(player, ann, PopupOrigin.Direct);
            return true;
        }

        private static string OptionText(Dictionary<string, object> options, string key)
        {
            object value;
            return options.TryGetValue(key, out value) && value != null ? value.ToString() : string.Empty;
        }

        private static bool OptionFlag(Dictionary<string, object> options, string key, bool fallback)
        {
            object value;
            if (!options.TryGetValue(key, out value) || value == null) return fallback;
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) ? parsed : fallback;
        }

        private static long OptionNumber(Dictionary<string, object> options, string key)
        {
            object value;
            if (!options.TryGetValue(key, out value) || value == null) return 0;
            double parsed;
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? (long)parsed : 0;
        }
        #endregion

        #region Notification UI
        private void ToastPlacement(out string anchorMin, out string anchorMax, out string offset)
        {
            const float width = 0.19f, height = 0.085f;
            string position = config.Notification.Position ?? string.Empty;
            float x = position.EndsWith("Left", StringComparison.OrdinalIgnoreCase) ? 0.01f
                : position.EndsWith("Center", StringComparison.OrdinalIgnoreCase) ? 0.5f - width / 2f : 0.98f - width;
            float y = position.StartsWith("Bottom", StringComparison.OrdinalIgnoreCase) ? 0.17f : 0.845f;
            anchorMin = Anchor(x, y);
            anchorMax = Anchor(x + width, y + height);
            offset = $"{F(config.Notification.OffsetX)} {F(config.Notification.OffsetY)}";
        }

        private void DestroyNotification(BasePlayer player)
        {
            string toast;
            if (toastNames.TryGetValue(player.userID, out toast))
            {
                CuiHelper.DestroyUi(player, toast);
                toastNames.Remove(player.userID);
            }
            CancelNotificationTimer(player.userID);
        }

        private void CancelNotificationTimer(ulong userId)
        {
            Timer t;
            if (notificationTimers.TryGetValue(userId, out t))
            {
                t?.Destroy();
                notificationTimers.Remove(userId);
            }
        }

        private void ShowNotification(BasePlayer player, Announcement ann)
        {
            if (!CanView(player) || ann == null || !CanSee(player, ann)) return;
            string safeTitle = ann.Title ?? string.Empty;

            if (config.Notification.UseNotifyPlugin && Notify != null && Notify.IsLoaded)
            {
                Notify.Call("SendNotify", player.UserIDString, config.Notification.NotifyType, safeTitle);
                return;
            }

            DestroyNotification(player);

            string anchorMin, anchorMax, offset;
            ToastPlacement(out anchorMin, out anchorMax, out offset);

            var container = new CuiElementContainer();
            var c = config.Colors;
            string typeColor = GetTypeColor(ann.Type);
            string root = NotificationLayer + "." + (++toastCounter).ToString(CultureInfo.InvariantCulture);
            string card = root + ".Card";

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offset, OffsetMax = offset }
            }, "Hud", root);
            AddPanelShadow(container, root, "0 0", "1 1", true, RadiusCard);
            AddSurface(container, root, "0 0", "1 1", "0.055 0.064 0.08 1", RadiusCard, $"news.view {ann.Id}", card, PanelRim);
            AddAccentBar(container, card, typeColor, 7f);

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
            CancelRedraw(player.userID);
        }

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

        private float OpenScreen(BasePlayer player, UiScreen screen, out bool fresh)
        {
            UiScreen previous;
            currentScreens.TryGetValue(player.userID, out previous);
            fresh = previous == UiScreen.None;
            CuiHelper.DestroyUi(player, ConfirmLayer);
            openPopups.Remove(player.userID);
            CancelAutoClose(player.userID);
            CancelReadRewardTimer(player.userID);
            CancelRedraw(player.userID);
            currentScreens[player.userID] = screen;
            playersWithUiOpen.Add(player.userID);
            return fresh ? 1f : previous == screen ? 0f : SwitchFade;
        }

        private void BeginScreen(CuiElementContainer container, bool fresh, string backdrop)
        {
            if (fresh) AddBackdrop(container, backdrop, 0.25f);
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, LayerName, ScreenName, ScreenName);
        }

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

        private void AddBackdrop(CuiElementContainer container, string color, float fade)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = color, Material = BlurMaterial, FadeIn = fade },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LayerName, LayerName);
        }

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
                    uint fillId = FileStorage.server.StoreByCrc(entity.net.ID, FileStorage.Type.png, BuildCornerPng(corner, false));
                    uint maskId = FileStorage.server.StoreByCrc(entity.net.ID, FileStorage.Type.png, BuildCornerPng(corner, true));
                    fill[corner] = fillId.ToString();
                    mask[corner] = maskId.ToString();
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

        private static byte[] BuildCornerPng(int corner, bool inverse)
        {
            int n = CornerTextureSize, height = inverse ? n + 2 : n, oy = inverse ? 1 : 0;
            bool right = corner == 1 || corner == 3, bottom = corner == 2 || corner == 3;
            float cx = right ? 0f : n;
            float cy = bottom ? 0f : n;
            var rgba = new byte[n * height * 4];
            for (int py = 0; py < height; py++)
            {
                for (int x = 0; x < n; x++)
                {
                    int k = (py * n + x) * 4;
                    rgba[k] = 255; rgba[k + 1] = 255; rgba[k + 2] = 255;
                    int y = py - oy;
                    if (y < 0 || y >= n) continue;
                    int inside = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float dx = x + (sx + 0.5f) / 4f - cx, dy = y + (sy + 0.5f) / 4f - cy;
                            if (dx * dx + dy * dy <= n * n) inside++;
                        }
                    float alpha = inside / 16f;
                    if (inverse) alpha = 1f - alpha;
                    rgba[k + 3] = (byte)Mathf.RoundToInt(alpha * 255f);
                }
            }
            return EncodePng(n, height, rgba);
        }

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

        private void AddRoundedFill(CuiElementContainer container, string parent, string color, float radius, float inset = 0f, float fade = 0f)
        {
            if (!IsOpaque(color)) color = SolidOn(color, ColorUnder(container, parent));
            string a = F(inset), na = F(-inset);
            if (!Rounded || radius < 0.5f)
            {
                container.Add(Piece(color, "0 0", "1 1", $"{a} {a}", $"{na} {na}", fade), parent);
                if (inset <= 0f) SetSurface(container, parent, color);
                return;
            }
            if (!IsOpaque(color))
            {
                AddSteppedFill(container, parent, "0 0", "1 1", color, radius, inset, 4, fade);
                return;
            }

            AddCorners(container, parent, cornerFill, color, inset, radius, fade);
            float overlap = radius < 2f ? 0f : Math.Min(SeamOverlap, radius * 0.25f);
            string b = F(inset + radius), nb = F(-(inset + radius));
            string bo = F(inset + radius - overlap), nbo = F(-(inset + radius - overlap));
            container.Add(Piece(color, "0 0", "1 1", $"{b} {a}", $"{nb} {na}", fade), parent);
            container.Add(Piece(color, "0 0", "0 1", $"{a} {bo}", $"{b} {nbo}", fade), parent);
            container.Add(Piece(color, "1 0", "1 1", $"{nb} {bo}", $"{na} {nbo}", fade), parent);
            if (inset <= 0f) SetSurface(container, parent, color);
        }

        private static void AddSteppedFill(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string color, float radius, float inset, int steps, float fade = 0f)
        {
            int space = anchorMin.IndexOf(' '), spaceMax = anchorMax.IndexOf(' ');
            string left = anchorMin.Substring(0, space), bottom = anchorMin.Substring(space + 1);
            string right = anchorMax.Substring(0, spaceMax), top = anchorMax.Substring(spaceMax + 1);
            string topMin = left + " " + top, bottomMax = right + " " + bottom;

            string a = F(inset), na = F(-inset);
            container.Add(Piece(color, anchorMin, anchorMax, $"{a} {F(inset + radius)}", $"{na} {F(-(inset + radius))}", fade), parent);
            for (int i = 0; i < steps; i++)
            {
                float t0 = Mathf.PI / 2f * i / steps, t1 = Mathf.PI / 2f * (i + 1) / steps;
                float y0 = radius * Mathf.Sin(t0), y1 = radius * Mathf.Sin(t1);
                float x = inset + radius * (1f - Mathf.Cos((t0 + t1) / 2f));
                container.Add(Piece(color, topMin, anchorMax, $"{F(x)} {F(-(inset + radius) + y0)}", $"{F(-x)} {F(-(inset + radius) + y1)}", fade), parent);
                container.Add(Piece(color, anchorMin, bottomMax, $"{F(x)} {F(inset + radius - y1)}", $"{F(-x)} {F(inset + radius - y0)}", fade), parent);
            }
        }

        private static void AddCorners(CuiElementContainer container, string parent, string[] textures, string color, float inset, float radius, float fade, bool mask = false)
        {
            float texel = mask ? radius / CornerTextureSize : 0f;
            string a = F(inset), na = F(-inset), b = F(inset + radius), nb = F(-(inset + radius));
            string ay = F(inset - texel), nay = F(-(inset - texel)), by = F(inset + radius + texel), nby = F(-(inset + radius + texel));
            string[] anchors = { "0 1", "1 1", "0 0", "1 0" };
            string[] mins = { $"{a} {nby}", $"{nb} {nby}", $"{a} {ay}", $"{nb} {ay}" };
            string[] maxs = { $"{b} {nay}", $"{na} {nay}", $"{b} {by}", $"{na} {by}" };
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

        private void AddCornerMasks(CuiElementContainer container, string parent, string backgroundColor, float radius)
        {
            if (!Rounded) return;
            AddCorners(container, parent, cornerMask, Opaque(backgroundColor), 0f, radius, 0f, true);
        }

        private string AddSurface(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string color, float radius, string command = null, string name = null, string rim = null, float rimWidth = 1f)
        {
            if (!Rounded) return AddBox(container, parent, anchorMin, anchorMax, color, command, name, rim, rimWidth);

            string under = ColorUnder(container, parent);
            string fill = SolidOn(color, under);
            string holder = command != null
                ? container.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = command }, Text = { Text = "" }, RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax } }, parent, name)
                : container.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax } }, parent, name);
            if (rim != null) AddRoundedFill(container, holder, SolidOn(rim, under ?? (IsOpaque(fill) ? fill : null)), radius + rimWidth, -rimWidth);
            AddRoundedFill(container, holder, fill, radius);
            return holder;
        }

        private string AddBox(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string color, string command = null, string name = null, string rim = null, float rimWidth = 1f)
        {
            string under = ColorUnder(container, parent);
            string fill = SolidOn(color, under);
            if (rim != null)
            {
                string w = F(rimWidth), nw = F(-rimWidth);
                container.Add(Piece(SolidOn(rim, under ?? (IsOpaque(fill) ? fill : null)), anchorMin, anchorMax, $"{nw} {nw}", $"{w} {w}", 0f), parent);
            }
            string box = command != null
                ? container.Add(new CuiButton { Button = { Color = fill, Command = command }, Text = { Text = "" }, RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax } }, parent, name)
                : container.Add(new CuiPanel { Image = { Color = fill }, RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax } }, parent, name);
            SetSurface(container, box, fill);
            return box;
        }

        private readonly List<KeyValuePair<CuiElementContainer, Dictionary<string, string>>> surfaceColors = new List<KeyValuePair<CuiElementContainer, Dictionary<string, string>>>();

        private Dictionary<string, string> SurfacesOf(CuiElementContainer container)
        {
            for (int i = surfaceColors.Count - 1; i >= 0; i--)
                if (ReferenceEquals(surfaceColors[i].Key, container)) return surfaceColors[i].Value;
            var surfaces = new Dictionary<string, string>();
            surfaceColors.Add(new KeyValuePair<CuiElementContainer, Dictionary<string, string>>(container, surfaces));
            if (surfaceColors.Count > 4) surfaceColors.RemoveAt(0);
            return surfaces;
        }

        private void SetSurface(CuiElementContainer container, string name, string color)
        {
            if (!string.IsNullOrEmpty(name) && IsOpaque(color)) SurfacesOf(container)[name] = color;
        }

        private string ColorUnder(CuiElementContainer container, string parent)
        {
            var surfaces = SurfacesOf(container);
            string name = parent;
            for (int depth = 0; depth < 32 && !string.IsNullOrEmpty(name); depth++)
            {
                string color;
                if (surfaces.TryGetValue(name, out color)) return color;
                CuiElement element = null;
                for (int i = container.Count - 1; i >= 0; i--)
                    if (container[i].Name == name) { element = container[i]; break; }
                if (element == null) return null;
                foreach (var component in element.Components)
                {
                    if (component is CuiRawImageComponent) return null;
                    var image = component as CuiImageComponent;
                    if (image != null && IsOpaque(image.Color) && string.IsNullOrEmpty(image.Png) && string.IsNullOrEmpty(image.Sprite)) return image.Color;
                    var button = component as CuiButtonComponent;
                    if (button != null && IsOpaque(button.Color) && string.IsNullOrEmpty(button.Sprite)) return button.Color;
                }
                name = element.Parent;
            }
            return null;
        }

        private bool IsOpaque(string rgba) => ParseColor(rgba)[3] >= 0.999f;

        private string SolidOn(string color, string under) => IsOpaque(color) || under == null || ParseColor(color)[3] <= 0.001f ? color : Blend(color, under);

        private void AddTopSheen(CuiElementContainer container, string parent, float radius)
        {
            container.Add(Piece(SolidOn("1 1 1 0.07", ColorUnder(container, parent)), "0 1", "1 1", $"{F(radius)} -2", $"{F(-radius)} -1", 0f), parent);
        }

        private string AddLabelIn(CuiElementContainer container, string parent, string text, int fontSize, string color, TextAnchor align = TextAnchor.MiddleCenter, string font = FontBold)
        {
            return container.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = fontSize, Align = align, Color = color, Font = font },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, parent);
        }

        private string AddPill(CuiElementContainer container, string parent, string text, string anchorMin, string anchorMax, string background, string textColor, int fontSize = 9)
        {
            string pill = AddBox(container, parent, anchorMin, anchorMax, background);
            AddLabelIn(container, pill, text, fontSize, textColor);
            return pill;
        }

        private string AddPrimaryButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string color, string textColor = "1 1 1 1", int fontSize = 11, string name = null)
        {
            string button = AddSurface(container, parent, anchorMin, anchorMax, color, RadiusButton, command, name);
            AddLabelIn(container, button, text, fontSize, textColor);
            return button;
        }

        private string Blend(string top, string under)
        {
            float[] t = ParseColor(top), u = ParseColor(under);
            float a = t[3];
            return $"{F(t[0] * a + u[0] * (1 - a))} {F(t[1] * a + u[1] * (1 - a))} {F(t[2] * a + u[2] * (1 - a))} 1";
        }

        private string Opaque(string rgba)
        {
            float[] c = ParseColor(rgba);
            return $"{F(c[0])} {F(c[1])} {F(c[2])} 1";
        }

        private float[] ParseColor(string rgba)
        {
            rgba = rgba ?? string.Empty;
            float[] result;
            if (parsedColors.TryGetValue(rgba, out result)) return result;

            result = new float[] { 1f, 1f, 1f, 1f };
            var parts = rgba.Split(' ');
            for (int i = 0; i < 4 && i < parts.Length; i++)
            {
                float value;
                if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value)) result[i] = value;
            }
            if (parsedColors.Count >= MaxCachedColors) parsedColors.Clear();
            parsedColors[rgba] = result;
            return result;
        }

        private const int ShadowSteps = 3;
        private void AddPanelShadow(CuiElementContainer container, string parent, string anchorMin, string anchorMax, bool small = false, float radius = RadiusPanel)
        {
            float outer = small ? 5f : 14f, inner = small ? 2f : 5f;
            AddShadowLayer(container, parent, anchorMin, anchorMax, small ? "0 0 0 0.10" : "0 0 0 0.08", radius, outer);
            AddShadowLayer(container, parent, anchorMin, anchorMax, "0 0 0 0.20", radius, inner);
        }

        private void AddShadowLayer(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string color, float radius, float spread)
        {
            if (Rounded) AddSteppedFill(container, parent, anchorMin, anchorMax, color, radius + spread, -spread, ShadowSteps);
            else container.Add(Piece(color, anchorMin, anchorMax, $"{F(-spread)} {F(-spread)}", $"{F(spread)} {F(spread)}", 0f), parent);
        }

        private void AddHairline(CuiElementContainer container, string parent, float y, float left = 0f, float right = 1f)
        {
            container.Add(new CuiPanel { Image = { Color = SolidOn(Hairline, ColorUnder(container, parent)) }, RectTransform = { AnchorMin = Anchor(left, y), AnchorMax = Anchor(right, y), OffsetMin = "0 0", OffsetMax = "0 1" } }, parent);
        }

        private string AddGhostButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string textColor, int fontSize = 10, string name = null)
        {
            return container.Add(new CuiButton
            {
                Button = { Color = SolidOn(Ghost, ColorUnder(container, parent)), Command = command },
                Text = { Text = text, FontSize = fontSize, Align = TextAnchor.MiddleCenter, Color = textColor, Font = FontBold },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent, name);
        }

        private static string MixAlpha(string rgba, float alpha)
        {
            var parts = (rgba ?? string.Empty).Split(' ');
            if (parts.Length < 3) return $"1 1 1 {F(alpha)}";
            return $"{parts[0]} {parts[1]} {parts[2]} {F(alpha)}";
        }

        private void AddLikeButton(CuiElementContainer container, BasePlayer player, Announcement ann, bool pop = false)
        {
            bool liked = ann.LikedPlayers.Contains(player.userID);
            float fade = pop ? 0.25f : 0f;
            container.Add(new CuiButton
            {
                Button = { Color = Blend(liked ? "0.90 0.30 0.35 0.16" : Ghost, Opaque(config.Colors.PanelBg)), Command = $"news.like {ann.Id}", FadeIn = fade },
                Text = { Text = $"❤  {ann.LikedPlayers.Count}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = liked ? "0.96 0.40 0.44 1" : "0.62 0.63 0.68 1", Font = FontBold, FadeIn = fade },
                RectTransform = { AnchorMin = "0.03 0.028", AnchorMax = "0.135 0.082" }
            }, PopupPanel, LikeButtonName);
        }

        private bool IsPopupOpen(ulong userId, string annId)
        {
            string open;
            return playersWithUiOpen.Contains(userId) && openPopups.TryGetValue(userId, out open) && open == annId;
        }

        private void ShowPopup(BasePlayer player, Announcement ann, PopupOrigin origin, bool playSound = false)
        {
            if (!CanView(player) || ann == null) return;
            bool preview = origin == PopupOrigin.Preview;
            if (!preview && !CanSee(player, ann)) return;

            bool fresh;
            float anim = OpenScreen(player, UiScreen.Popup, out fresh);
            DestroyNotification(player);
            if (!preview) openPopups[player.userID] = ann.Id;

            var container = new CuiElementContainer();
            var c = config.Colors;
            string accentHex = RgbaToHex(c.ButtonPrimary);
            string typeColor = GetTypeColor(ann.Type);

            BeginScreen(container, fresh, "0 0 0 0.62");
            int contentStart = container.Count;
            AddPanelShadow(container, ScreenName, "0.175 0.175", "0.825 0.825");

            string panelColor = Opaque(c.PanelBg);
            string mainPanel = AddSurface(container, ScreenName, "0.175 0.175", "0.825 0.825", panelColor, RadiusPanel, null, PopupPanel,
                ann.Pinned ? PinGold : PanelRim, ann.Pinned ? 1.5f : 1f);
            AddTopSheen(container, mainPanel, RadiusPanel);

            float pillLeft = 0.03f;
            pillLeft = AddHeaderPill(container, mainPanel, ann.Type.ToString().ToUpper(), typeColor, "1 1 1 1", pillLeft);
            if (HasPoll(ann))
                pillLeft = AddHeaderPill(container, mainPanel, Msg("PollLabel", player), MixAlpha(c.ButtonPrimary, 0.22f), c.ButtonPrimary, pillLeft);
            if (!string.IsNullOrEmpty(ann.Group))
                pillLeft = AddHeaderPill(container, mainPanel, Msg("GroupOnly", player, ann.Group.ToUpper()), "1 1 1 0.10", c.TextTitle, pillLeft);
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
                AddPopupNavigation(container, player, ann, origin, mainPanel, c);

            AddGhostButton(container, mainPanel, "✕", preview ? "news.editor.back" : "news.close", "0.947 0.928", "0.972 0.972", c.TextNormal, 11);
            AddHairline(container, mainPanel, 0.905f, 0.03f, 0.97f);

            bool hasImage = !string.IsNullOrEmpty(ann.ImageUrl);
            float contentLeft = hasImage ? 0.5f : 0.03f;

            if (hasImage)
            {
                bool wide = string.Equals(config.General.PopupImageFrame, "Wide", StringComparison.OrdinalIgnoreCase);
                float frameBottom = wide ? 0.29f : BodyBottomDefault;
                float frameTop = wide ? 0.73f : 0.885f;
                string imgPanel = mainPanel + ".Img";

                AddBox(container, mainPanel, Anchor(0.03f, frameBottom), Anchor(0.47f, frameTop), Blend("0 0 0 0.35", panelColor), null, imgPanel);
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

            if (ann.CountdownTo > 0) AddCountdownBlock(container, player, ann, contentLeft);
            float bodyTop = BodyTopFor(ann);

            float bodyBottom = BodyBottomDefault;
            if (HasPoll(ann))
            {
                float pollBottom;
                PollLayout(ann, out bodyBottom, out pollBottom);
                AddPollBlock(container, player, ann, preview);
            }

            string bodyText = ann.Text ?? string.Empty;
            int wrap = hasImage ? BodyWrapWithImage : BodyWrapFullWidth;
            int estLines = CountBodyLines(bodyText, wrap) + 1;
            float viewportUnits = (bodyTop - bodyBottom) * PopupHeightUnits;
            int extra = Mathf.Max(0, Mathf.CeilToInt(estLines * BodyLineUnits - viewportUnits));

            string bodyScroll = mainPanel + ".Body";
            container.Add(new CuiElement
            {
                Name = bodyScroll,
                Parent = mainPanel,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = Anchor(contentLeft, bodyBottom), AnchorMax = Anchor(0.97f, bodyTop) },
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

            AddHairline(container, mainPanel, 0.105f, 0.03f, 0.97f);

            if (preview)
            {
                string likePreview = AddBox(container, mainPanel, "0.03 0.028", "0.135 0.082", Ghost);
                AddLabelIn(container, likePreview, $"❤  {ann.LikedPlayers.Count}", 12, "0.62 0.63 0.68 0.6");
            }
            else if (!string.IsNullOrEmpty(ann.Id))
            {
                AddLikeButton(container, player, ann);
            }

            string footerCommand, footerText;
            if (preview) { footerCommand = "news.editor.back"; footerText = Msg("BackToEditor", player); }
            else if (origin == PopupOrigin.Archive) { footerCommand = $"news.page {GetArchivePage(player.userID)}"; footerText = Msg("BackToArchive", player); }
            else { footerCommand = "news.page 0 reset"; footerText = Msg("ViewArchive", player); }

            AddPrimaryButton(container, mainPanel, footerText, footerCommand, "0.78 0.028", "0.97 0.082", c.ButtonPrimary);

            if (origin == PopupOrigin.Broadcast && config.General.EnableAutoClose)
            {
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
                    if (player != null && player.IsConnected && IsPopupOpen(userId, ann.Id)) DestroyUI(player);
                });
            }

            if (!preview) ScheduleReadCompletion(player, ann);
        }

        private float AddHeaderPill(CuiElementContainer container, string parent, string text, string background, string textColor, float left)
        {
            float width = (text.Length * 5.4f + 20f) / PopupWidthUnits;
            AddPill(container, parent, text, Anchor(left, 0.928f), Anchor(left + width, 0.972f), background, textColor, 10);
            return left + width + 0.008f;
        }

        private static float BodyTopFor(Announcement ann) => ann.CountdownTo > 0 ? BodyTop - CountdownBlockHeight : BodyTop;

        private void AddCountdownBlock(CuiElementContainer container, BasePlayer player, Announcement ann, float contentLeft)
        {
            SetSurface(container, PopupPanel, Opaque(config.Colors.PanelBg));
            var c = config.Colors;
            float top = BodyTop - 0.008f, bottom = top - 0.062f;
            AddSurface(container, PopupPanel, Anchor(contentLeft, bottom), Anchor(0.97f, top), MixAlpha(c.ButtonPrimary, 0.10f), RadiusCard, null, CountdownName, MixAlpha(c.ButtonPrimary, 0.30f));

            string label = string.IsNullOrEmpty(ann.CountdownLabel) ? Msg("CountdownDefaultLabel", player) : ann.CountdownLabel.ToUpper();
            container.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = c.ButtonPrimary, Font = FontBold },
                RectTransform = { AnchorMin = "0.025 0", AnchorMax = "0.5 1" }
            }, CountdownName);

            long left = ann.CountdownTo - NowTicks;
            if (left <= TimeSpan.TicksPerSecond)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("CountdownDone", player), FontSize = 15, Align = TextAnchor.MiddleRight, Color = c.TextTitle, Font = FontBold },
                    RectTransform = { AnchorMin = "0.4 0", AnchorMax = "0.975 1" }
                }, CountdownName);
                return;
            }

            long totalSeconds = left / TimeSpan.TicksPerSecond;
            long days = totalSeconds / 86400;
            string prefix = days == 0 ? string.Empty : days == 1 ? Msg("CountdownDay", player) : Msg("CountdownDays", player, days);
            container.Add(new CuiElement
            {
                Parent = CountdownName,
                Components =
                {
                    new CuiTextComponent { Text = prefix + "%TIME_LEFT%", FontSize = 15, Align = TextAnchor.MiddleRight, Color = c.TextTitle, Font = FontBold },
                    new CuiCountdownComponent
                    {
                        StartTime = totalSeconds % 86400,
                        EndTime = 0,
                        Step = 1,
                        Interval = 1,
                        TimerFormat = TimerFormat.HoursMinutesSeconds,
                        Command = $"news.countdown {ann.Id}"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.4 0", AnchorMax = "0.975 1" }
                }
            });
        }

        private static float PollBlockHeight(Announcement ann)
        {
            int n = ann.PollOptions.Count;
            return 0.05f + n * 0.056f + (n - 1) * 0.01f + 0.014f;
        }

        private static void PollLayout(Announcement ann, out float bodyBottom, out float pollBottom)
        {
            float pollH = PollBlockHeight(ann);
            int wrap = string.IsNullOrEmpty(ann.ImageUrl) ? BodyWrapFullWidth : BodyWrapWithImage;
            float needed = Math.Max(0.09f, (CountBodyLines(ann.Text, wrap) * BodyLineUnits + 16f) / PopupHeightUnits);
            bodyBottom = Math.Max(BodyBottomDefault + pollH + 0.018f, BodyTopFor(ann) - needed);
            pollBottom = bodyBottom - 0.018f - pollH;
        }

        private void AddPollBlock(CuiElementContainer container, BasePlayer player, Announcement ann, bool preview, bool justVoted = false)
        {
            SetSurface(container, PopupPanel, Opaque(config.Colors.PanelBg));
            var c = config.Colors;
            float left = string.IsNullOrEmpty(ann.ImageUrl) ? 0.03f : 0.5f;
            float h = PollBlockHeight(ann);
            float bodyBottom, pollBottom;
            PollLayout(ann, out bodyBottom, out pollBottom);

            AddSurface(container, PopupPanel, Anchor(left, pollBottom), Anchor(0.97f, pollBottom + h), "1 1 1 0.03", RadiusCard, null, PollPanelName);
            ulong userId = player.userID;
            int myVote = preview ? -1 : VoteOf(ann, userId);
            int[] counts = CountVotes(ann);
            int total = Sum(counts);
            bool closed = ann.PollClosed || (!preview && !IsPollOpen(ann));
            bool canVote = !preview && !closed && (myVote < 0 || config.Polls.AllowVoteChange);
            bool showResults = !preview && (myVote >= 0 || closed || config.Polls.ShowResultsBeforeVoting || CanAdmin(player));

            float headBottom = 1f - 0.05f / h;
            container.Add(new CuiLabel
            {
                Text = { Text = $"<color={RgbaToHex(c.ButtonPrimary)}>{Msg("PollLabel", player)}</color>   {Msg("PollVotes", player, total)}", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = Anchor(0.03f, headBottom), AnchorMax = "0.55 1" }
            }, PollPanelName);

            string hint = closed ? Msg("PollClosedLabel", player)
                : myVote >= 0 ? Msg(config.Polls.AllowVoteChange ? "PollVotedChange" : "PollVoted", player)
                : Msg("PollClickToVote", player);
            if (!closed && !preview && ann.PollEndsAt > 0)
                hint += "   •   " + Msg("PollClosesIn", player, FormatSpan(ann.PollEndsAt - NowTicks, player).ToUpper());
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

                AddBox(container, PollPanelName, rowMin, rowMax, background, canVote && !mine ? $"news.vote {ann.Id} {i}" : null, row);

                float share = total > 0 ? (float)counts[i] / total : 0f;
                if (showResults && share > 0f)
                {
                    string barColor = SolidOn(MixAlpha(c.ButtonPrimary, mine ? 0.45f : 0.2f), ColorUnder(container, row));
                    container.Add(Piece(barColor, "0 0", Anchor(share, 1f), "0 0", "0 0", justVoted ? 0.5f : 0f), row);
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
            ArchiveFilter filter = null;
            if (origin == PopupOrigin.Archive) archiveFilters.TryGetValue(player.userID, out filter);

            int index = -1, count = 0;
            string previousId = null, nextId = null, lastId = null;
            foreach (var other in GetDisplayOrder())
            {
                if (!CanSee(player, other) || !MatchesFilter(other, filter)) continue;
                if (other == ann) { index = count; previousId = lastId; }
                else if (index >= 0 && nextId == null) nextId = other.Id;
                lastId = other.Id;
                count++;
            }
            if (index < 0 || count < 2) return;

            string originArg = origin == PopupOrigin.Archive ? "archive" : "direct";
            string prevCmd = previousId != null ? $"news.view {previousId} {originArg}" : null;
            string nextCmd = nextId != null ? $"news.view {nextId} {originArg}" : null;

            AddNavArrow(container, mainPanel, "<", prevCmd, "0.852 0.928", "0.877 0.972", c);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("PositionOf", player, index + 1, count), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = "0.877 0.91", AnchorMax = "0.913 0.99" }
            }, mainPanel);

            AddNavArrow(container, mainPanel, ">", nextCmd, "0.913 0.928", "0.938 0.972", c);
        }

        private void AddNavArrow(CuiElementContainer container, string parent, string glyph, string command, string anchorMin, string anchorMax, UIColors c)
        {
            if (command == null)
            {
                string box = AddBox(container, parent, anchorMin, anchorMax, "1 1 1 0.02");
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

            ulong userId = player.userID;
            ArchiveFilter filter;
            archiveFilters.TryGetValue(userId, out filter);

            var displayList = Pool.Get<List<Announcement>>();
            int visible = 0, unread = 0;
            foreach (var ann in GetDisplayOrder())
            {
                if (!CanSee(player, ann)) continue;
                visible++;
                if (IsUnread(ann, userId)) unread++;
                if (MatchesFilter(ann, filter)) displayList.Add(ann);
            }
            if (visible == 0)
            {
                Pool.FreeUnmanaged(ref displayList);
                DestroyUI(player);
                SendReply(player, Msg("NoNewsHistory", player));
                return;
            }

            bool fresh;
            float anim = OpenScreen(player, UiScreen.Archive, out fresh);

            var container = new CuiElementContainer();
            var c = config.Colors;
            int perPage = config.General.AnnouncementsPerPage;
            int totalPages = Math.Max(1, Mathf.CeilToInt((float)displayList.Count / perPage));
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;
            archivePages[userId] = page;

            BeginScreen(container, fresh, "0 0 0 0.62");
            int contentStart = container.Count;
            AddPanelShadow(container, ScreenName, "0.15 0.1", "0.85 0.9");

            string panelColor = Opaque(c.PanelBg);
            string mainPanel = AddSurface(container, ScreenName, "0.15 0.1", "0.85 0.9", panelColor, RadiusPanel, null, LayerName + ".List", PanelRim);
            AddTopSheen(container, mainPanel, RadiusPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{Msg("ArchiveTitle", player)}  <color={RgbaToHex(c.TextMuted)}>/  {config.General.ServerName}</color>", FontSize = 17, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                RectTransform = { AnchorMin = "0.025 0.91", AnchorMax = "0.575 0.99" }
            }, mainPanel);

            if (unread > 0)
            {
                AddGhostButton(container, mainPanel, Msg("MarkAllRead", player), "news.markread", "0.585 0.927", "0.715 0.973", c.TextNormal, 10);
                AddPill(container, mainPanel, Msg("UnreadCount", player, unread), "0.725 0.927", "0.845 0.973", c.ButtonPrimary, "1 1 1 1", 10);
            }

            AddGhostButton(container, mainPanel, Msg("SettingsButton", player), "news.settings", "0.855 0.927", "0.935 0.973", c.TextNormal, 9);
            AddGhostButton(container, mainPanel, "✕", "news.close", "0.945 0.927", "0.975 0.973", c.TextNormal, 11);
            AddHairline(container, mainPanel, 0.905f, 0.025f, 0.975f);

            AddArchiveFilters(container, player, mainPanel, filter, c);

            if (displayList.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("NoMatches", player), FontSize = 13, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = FontRegular },
                    RectTransform = { AnchorMin = "0.1 0.47", AnchorMax = "0.9 0.6" }
                }, mainPanel);
                AddGhostButton(container, mainPanel, Msg("ClearFilter", player), "news.search.clear", "0.43 0.38", "0.57 0.44", c.TextTitle, 10);
            }

            int start = page * perPage;
            int count = 0;
            float rowHeight = 0.715f / perPage;
            float padding = 0.008f;
            long now = NowTicks;

            for (int i = start; i < displayList.Count && count < perPage; i++)
            {
                var ann = displayList[i];
                bool isUnread = IsUnread(ann, userId);
                float top = 0.825f - (count * rowHeight) - padding;
                float bottom = top - rowHeight + (padding * 2);

                string itemPanel = mainPanel + $".{i}";
                string typeColor = GetTypeColor(ann.Type);

                string rowColor = Blend(c.ContentBg, panelColor);
                if (ann.Pinned) rowColor = Blend("0.95 0.70 0.20 0.07", rowColor);
                else if (isUnread) rowColor = Blend("1 1 1 0.025", rowColor);
                string rim = ann.Pinned ? "0.95 0.70 0.20 0.40" : isUnread ? MixAlpha(c.ButtonPrimary, 0.45f) : null;
                AddSurface(container, mainPanel, Anchor(0.025f, bottom), Anchor(0.975f, top), rowColor, RadiusCard, $"news.view {ann.Id} archive", itemPanel, rim);
                AddAccentBar(container, itemPanel, typeColor);

                float textLeft = 0.03f;
                if (config.General.ArchiveThumbnails && !string.IsNullOrEmpty(ann.ImageUrl))
                {
                    string thumb = AddBox(container, itemPanel, "0.022 0.13", "0.127 0.87", Blend("0 0 0 0.45", rowColor));
                    container.Add(new CuiElement
                    {
                        Parent = thumb,
                        Components = { BuildRawImage(ann.ImageUrl), new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } }
                    });
                    textLeft = 0.142f;
                }

                string title = Ellipsize((string.IsNullOrEmpty(ann.Title) ? Msg("NoTitle", player) : ann.Title).ToUpper(), textLeft > 0.1f ? 40 : 52);
                container.Add(new CuiLabel
                {
                    Text = { Text = title, FontSize = 13, Align = TextAnchor.MiddleLeft, Color = isUnread ? c.TextTitle : c.TextNormal, Font = FontBold },
                    RectTransform = { AnchorMin = Anchor(textLeft, 0.52f), AnchorMax = "0.58 0.88" }
                }, itemPanel);

                float badgeRight = 0.765f;
                if (isUnread)
                {
                    AddBadge(container, itemPanel, Msg("UnreadBadge", player), c.ButtonPrimary, "1 1 1 1", badgeRight - 0.06f, badgeRight);
                    badgeRight -= 0.07f;
                }
                if (ann.Pinned)
                {
                    AddBadge(container, itemPanel, Msg("PinnedBadge", player), PinGold, PinGoldText, badgeRight - 0.085f, badgeRight);
                    badgeRight -= 0.095f;
                }
                if (!string.IsNullOrEmpty(ann.Group))
                {
                    string groupText = Msg("GroupOnly", player, ann.Group.ToUpper());
                    float groupWidth = Math.Min(0.16f, (groupText.Length * 5f + 16f) / ArchiveWidthUnits);
                    AddBadge(container, itemPanel, groupText, Blend("1 1 1 0.12", rowColor), c.TextTitle, badgeRight - groupWidth, badgeRight);
                }

                string rawPreview = (ann.Text ?? "").Replace("\n", " ");
                int previewChars = textLeft > 0.1f ? 70 : 88;
                string pollTag = string.Empty;
                if (HasPoll(ann))
                {
                    pollTag = $"<color={RgbaToHex(c.ButtonPrimary)}>{Msg("PollLabel", player)} · {Msg("PollVotes", player, ann.PollVotes.Count)}</color>   ";
                    previewChars -= 16;
                }
                if (ann.CountdownTo > now)
                {
                    string label = string.IsNullOrEmpty(ann.CountdownLabel) ? Msg("CountdownDefaultLabel", player) : ann.CountdownLabel.ToUpper();
                    pollTag += $"<color={RgbaToHex(c.ButtonPrimary)}>{label} {FormatSpan(ann.CountdownTo - now, player).ToUpper()}</color>   ";
                    previewChars -= label.Length + 9;
                }
                string preview = $"<color={RgbaToHex(typeColor)}>{ann.Type.ToString().ToUpper()}</color>   {pollTag}{Ellipsize(rawPreview, Math.Max(12, previewChars))}";
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
                    Text = { Text = $"❤ {ann.LikedPlayers.Count}", FontSize = 10, Align = TextAnchor.MiddleRight, Color = c.TextMuted, Font = FontBold },
                    RectTransform = { AnchorMin = "0.82 0.12", AnchorMax = "0.93 0.48" }
                }, itemPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = "›", FontSize = 22, Align = TextAnchor.MiddleCenter, Color = isUnread ? c.ButtonPrimary : c.TextMuted, Font = FontBold },
                    RectTransform = { AnchorMin = "0.94 0", AnchorMax = "0.99 1" }
                }, itemPanel);

                count++;
            }

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
            Pool.FreeUnmanaged(ref displayList);
        }

        private static readonly AnnouncementType[] AllTypes = (AnnouncementType[])Enum.GetValues(typeof(AnnouncementType));

        private void AddArchiveFilters(CuiElementContainer container, BasePlayer player, string mainPanel, ArchiveFilter filter, UIColors c)
        {
            string current = filter?.Type ?? "all";
            float left = 0.025f;
            left = AddFilterChip(container, mainPanel, "all", Msg("FilterAll", player), c.ButtonPrimary, current, left);
            foreach (var type in AllTypes)
                left = AddFilterChip(container, mainPanel, type.ToString(), type.ToString().ToUpper(), GetTypeColor(type), current, left);
            AddFilterChip(container, mainPanel, "polls", Msg("FilterPolls", player), c.ButtonPrimary, current, left);

            AddSearchField(container, player, mainPanel, Anchor(0.66f, FilterRowBottom), Anchor(0.975f, FilterRowTop), filter?.Query, "news.search", "news.search.clear", mainPanel + ".Search");
        }

        private const float FilterRowBottom = 0.843f, FilterRowTop = 0.889f;

        private float AddFilterChip(CuiElementContainer container, string mainPanel, string key, string label, string color, string current, float left)
        {
            bool selected = string.Equals(key, current, StringComparison.OrdinalIgnoreCase);
            float width = (label.Length * 5.6f + 22f) / ArchiveWidthUnits;
            string box = AddBox(container, mainPanel, Anchor(left, FilterRowBottom), Anchor(left + width, FilterRowTop), selected ? color : Ghost, "news.filter " + key.ToLowerInvariant());
            AddLabelIn(container, box, label, 9, selected ? "1 1 1 1" : color);
            return left + width + 0.006f;
        }

        private void AddSearchField(CuiElementContainer container, BasePlayer player, string parent, string anchorMin, string anchorMax, string query, string command, string clearCommand, string name)
        {
            var c = config.Colors;
            query = query ?? string.Empty;
            string field = AddBox(container, parent, anchorMin, anchorMax, "1 1 1 0.045", null, name,
                string.IsNullOrEmpty(query) ? null : MixAlpha(c.ButtonPrimary, 0.55f));
            container.Add(new CuiLabel
            {
                Text = { Text = Msg("SearchLabel", player), FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = "0.035 0", AnchorMax = "0.24 1" }
            }, field);
            container.Add(new CuiElement
            {
                Parent = field,
                Components =
                {
                    new CuiInputFieldComponent { Text = query, FontSize = 11, Align = TextAnchor.MiddleLeft, Command = command, Color = c.TextTitle, CharsLimit = MaxSearchChars, NeedsKeyboard = true, Font = FontRegular },
                    new CuiRectTransformComponent { AnchorMin = "0.24 0", AnchorMax = "0.89 1" }
                }
            });
            if (!string.IsNullOrEmpty(query))
                AddGhostButton(container, field, "✕", clearCommand, "0.9 0.15", "0.985 0.85", c.TextNormal, 9);
        }

        private void AddBadge(CuiElementContainer container, string parent, string text, string background, string textColor, float left, float right)
        {
            AddPill(container, parent, text, Anchor(left, 0.6f), Anchor(right, 0.82f), background, textColor, 9);
        }

        private void AddAccentBar(CuiElementContainer container, string parent, string color, float leftUnits = 8f)
        {
            container.Add(Piece(color, "0 0", "0 1", $"{F(leftUnits)} 14", $"{F(leftUnits + 3f)} -14", 0f), parent);
        }

        private static string GetTypeColor(AnnouncementType type)
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
            if (ImageLibrary == null || !ImageLibrary.IsLoaded || string.IsNullOrEmpty(url)) return null;
            if (!ImageLibrary.Call<bool>("HasImage", url, 0UL)) return null;
            return ImageLibrary.Call<string>("GetImage", url);
        }

        private string RgbaToHex(string rgba)
        {
            float[] c = ParseColor(rgba);
            return $"#{ToByte(c[0]):X2}{ToByte(c[1]):X2}{ToByte(c[2]):X2}";
        }

        private static int ToByte(float channel) => Mathf.Clamp(Mathf.RoundToInt(channel * 255f), 0, 255);
        #endregion

        #region Admin UI
        struct Grid
        {
            private readonly float width, height;

            public Grid(float width, float height)
            {
                this.width = width;
                this.height = height;
            }

            public string Min(float left, float bottom) => Anchor(left / width, 1f - bottom / height);
            public string Max(float right, float top) => Anchor(right / width, 1f - top / height);
        }

        private AdminView GetAdminView(ulong userId)
        {
            AdminView view;
            if (!adminViews.TryGetValue(userId, out view))
            {
                view = new AdminView();
                adminViews[userId] = view;
            }
            return view;
        }

        private static bool InAdminTab(Announcement ann, string tab)
        {
            switch (tab)
            {
                case "published": return !ann.Draft;
                case "drafts": return ann.Draft;
                case "pinned": return ann.Pinned;
                case "polls": return HasPoll(ann);
                default: return true;
            }
        }

        private void FillAdminOrder(ulong userId, List<Announcement> list)
        {
            AdminView view;
            if (!adminViews.TryGetValue(userId, out view)) view = DefaultAdminView;
            string sort = AdminSorts[view.Sort];

            var source = sort == "newest" ? GetDisplayOrder() : announcements;
            if (sort == "oldest")
            {
                for (int i = source.Count - 1; i >= 0; i--)
                    if (InAdminTab(source[i], view.Tab) && MatchesQuery(source[i], view.Query)) list.Add(source[i]);
                return;
            }

            foreach (var ann in source)
                if (InAdminTab(ann, view.Tab) && MatchesQuery(ann, view.Query)) list.Add(ann);
            if (sort == "reads") list.Sort(ByReads);
            else if (sort == "likes") list.Sort(ByLikes);
        }

        private static readonly AdminView DefaultAdminView = new AdminView();

        private static readonly Comparison<Announcement> ByReads = (a, b) => CompareCounts(a, b, a.ReadByPlayers.Count, b.ReadByPlayers.Count);
        private static readonly Comparison<Announcement> ByLikes = (a, b) => CompareCounts(a, b, a.LikedPlayers.Count, b.LikedPlayers.Count);

        private static int CompareCounts(Announcement a, Announcement b, int countA, int countB)
        {
            if (countA != countB) return countB.CompareTo(countA);
            if (a.Timestamp != b.Timestamp) return b.Timestamp.CompareTo(a.Timestamp);
            return string.CompareOrdinal(a.Id, b.Id);
        }

        private void ShowAdminList(BasePlayer player, int page)
        {
            bool fresh;
            float anim = OpenScreen(player, UiScreen.Admin, out fresh);

            var container = new CuiElementContainer();
            var c = config.Colors;
            ulong userId = player.userID;
            var view = GetAdminView(userId);
            var g = new Grid(AdminWidthUnits, AdminHeightUnits);
            const float left = AdminSidePad, right = AdminWidthUnits - AdminSidePad;

            BeginScreen(container, fresh, AdminBackdrop);
            int contentStart = container.Count;
            AddPanelShadow(container, ScreenName, "0.13 0.07", "0.87 0.93");

            string surface = Opaque(AdminSurface);
            string panel = AddSurface(container, ScreenName, "0.13 0.07", "0.87 0.93", surface, RadiusPanel, null, LayerName + ".Admin", PanelRim);
            AddTopSheen(container, panel, RadiusPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{Msg("AdminControl", player)}  <color={RgbaToHex(c.TextMuted)}>/  {(config.General.ServerName ?? string.Empty).ToUpper()}</color>", FontSize = 17, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                RectTransform = { AnchorMin = g.Min(left, 48f), AnchorMax = g.Max(560f, 14f) }
            }, panel);
            AddGhostButton(container, panel, Msg("Themes", player), "news.admin.themes", g.Min(right - 334f, 46f), g.Max(right - 222f, 16f), c.TextNormal, 9);
            AddPrimaryButton(container, panel, Msg("NewPost", player), "news.admin.create", g.Min(right - 214f, 46f), g.Max(right - 40f, 16f), c.ButtonPrimary, "1 1 1 1", 10);
            AddGhostButton(container, panel, "✕", "news.close", g.Min(right - 30f, 46f), g.Max(right, 16f), c.TextNormal, 12);
            AddHairline(container, panel, 1f - 58f / AdminHeightUnits, left / AdminWidthUnits, right / AdminWidthUnits);

            AddAdminStats(container, player, panel, g, surface);
            AddAdminToolbar(container, player, panel, g, view);

            float rowsTop = 180f;
            EditorSession unsaved;
            if (storedData.EditorAutosaves.TryGetValue(userId, out unsaved) && unsaved != null)
            {
                AddUnsavedBanner(container, player, panel, g, unsaved, surface);
                rowsTop = 218f;
            }
            float rowsBottom = AdminHeightUnits - 64f;

            var list = Pool.Get<List<Announcement>>();
            FillAdminOrder(userId, list);
            int perPage = config.General.AnnouncementsPerPage;
            int totalPages = Math.Max(1, Mathf.CeilToInt((float)list.Count / perPage));
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;
            view.Page = page;

            PruneAdminSelection(userId);
            var selection = GetAdminSelection(userId);

            if (list.Count == 0)
            {
                bool nothingStored = announcements.Count == 0;
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg(nothingStored ? "NoPostsYet" : "NoMatches", player), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = FontRegular },
                    RectTransform = { AnchorMin = g.Min(left, rowsTop + 190f), AnchorMax = g.Max(right, rowsTop + 110f) }
                }, panel);
                if (!nothingStored)
                    AddGhostButton(container, panel, Msg("ClearFilter", player), "news.admin.filter reset", g.Min(AdminWidthUnits / 2f - 60f, rowsTop + 226f), g.Max(AdminWidthUnits / 2f + 60f, rowsTop + 196f), c.TextTitle, 10);
            }

            int start = page * perPage, end = Math.Min(start + perPage, list.Count);
            float slot = (rowsBottom - rowsTop) / perPage;
            float gap = slot > 56f ? 8f : 5f;
            for (int i = start; i < end; i++)
                AddAdminRow(container, player, panel, g, list[i], panel + "." + i, rowsTop + (i - start) * slot, slot - gap, page, selection.Contains(list[i].Id), surface);

            AddHairline(container, panel, 56f / AdminHeightUnits, left / AdminWidthUnits, right / AdminWidthUnits);
            AddAdminFooter(container, player, panel, g, page, totalPages, list, start, end, selection, surface);

            ApplyFadeRamp(container, contentStart, 0.12f * anim, 0.5f * anim);
            CuiHelper.AddUi(player, container);
            Pool.FreeUnmanaged(ref list);
        }

        private void AddAdminStats(CuiElementContainer container, BasePlayer player, string panel, Grid g, string surface)
        {
            var c = config.Colors;
            int published = 0, drafts = 0, pinned = 0, reads = 0, likes = 0, votes = 0, openPolls = 0;
            foreach (var ann in announcements)
            {
                if (ann.Draft) { drafts++; continue; }
                published++;
                if (ann.Pinned) pinned++;
                reads += ann.ReadByPlayers.Count;
                likes += ann.LikedPlayers.Count;
                votes += ann.PollVotes.Count;
                if (IsPollOpen(ann)) openPolls++;
            }

            string perPost = published > 0 ? ((float)reads / published).ToString("0.#", CultureInfo.InvariantCulture) : "0";
            int likeRate = reads > 0 ? Math.Min(100, Mathf.RoundToInt(100f * likes / reads)) : 0;
            var cards = new[]
            {
                new[] { Msg("StatPublished", player), published.ToString(), Msg("StatPinnedHint", player, pinned), c.ButtonPrimary },
                new[] { Msg("StatDrafts", player), drafts.ToString(), Msg("StatDraftsHint", player), "0.62 0.63 0.68 1" },
                new[] { Msg("StatReads", player), reads.ToString(), Msg("StatReadsHint", player, perPost), "0.2 0.7 0.9 1" },
                new[] { Msg("StatLikes", player), likes.ToString(), Msg("StatLikesHint", player, likeRate), "0.96 0.40 0.44 1" },
                new[] { Msg("StatVotes", player), votes.ToString(), Msg("StatVotesHint", player, openPolls), "0.6 0.3 0.8 1" }
            };

            const float top = 70f, bottom = 126f, between = 10f;
            float width = (AdminWidthUnits - 2f * AdminSidePad - between * (cards.Length - 1)) / cards.Length;
            string cardColor = Blend(AdminSurfaceRaised, surface);
            for (int i = 0; i < cards.Length; i++)
            {
                float x = AdminSidePad + i * (width + between);
                string card = AddSurface(container, panel, g.Min(x, bottom), g.Max(x + width, top), cardColor, RadiusCard);
                container.Add(Piece(cards[i][3], "0 0", "0 1", "8 14", "11 -14", 0f), card);
                container.Add(new CuiLabel
                {
                    Text = { Text = cards[i][0], FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = FontBold },
                    RectTransform = { AnchorMin = "0.12 0.62", AnchorMax = "0.95 0.9" }
                }, card);
                container.Add(new CuiLabel
                {
                    Text = { Text = cards[i][1], FontSize = 20, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                    RectTransform = { AnchorMin = "0.12 0.1", AnchorMax = "0.5 0.64" }
                }, card);
                container.Add(new CuiLabel
                {
                    Text = { Text = cards[i][2], FontSize = 8, Align = TextAnchor.LowerRight, Color = c.TextMuted, Font = FontBold },
                    RectTransform = { AnchorMin = "0.4 0.16", AnchorMax = "0.93 0.6" }
                }, card);
            }
        }

        private void AddAdminToolbar(CuiElementContainer container, BasePlayer player, string panel, Grid g, AdminView view)
        {
            var c = config.Colors;
            const float top = 138f, bottom = 168f;
            string mutedHex = RgbaToHex(c.TextMuted);

            var counts = new int[AdminTabs.Length];
            foreach (var ann in announcements)
                for (int i = 0; i < AdminTabs.Length; i++)
                    if (InAdminTab(ann, AdminTabs[i])) counts[i]++;

            float x = AdminSidePad;
            for (int t = 0; t < AdminTabs.Length; t++)
            {
                string tab = AdminTabs[t];
                bool selected = tab == view.Tab;
                string label = Msg("AdminTab" + char.ToUpperInvariant(tab[0]) + tab.Substring(1), player);
                string count = counts[t].ToString();
                float width = (label.Length + count.Length) * 5.6f + 30f;
                string chip = AddBox(container, panel, g.Min(x, bottom), g.Max(x + width, top), selected ? c.ButtonPrimary : Ghost, "news.admin.filter " + tab);
                AddLabelIn(container, chip, selected ? $"{label}  {count}" : $"{label}  <color={mutedHex}>{count}</color>", 9, selected ? "1 1 1 1" : c.TextNormal);
                x += width + 6f;
            }

            float right = AdminWidthUnits - AdminSidePad;
            AddCycleButton(container, panel, Msg("Sort" + char.ToUpperInvariant(AdminSorts[view.Sort][0]) + AdminSorts[view.Sort].Substring(1), player), "news.admin.sort",
                (right - 388f) / AdminWidthUnits, (right - 258f) / AdminWidthUnits, 1f - bottom / AdminHeightUnits, 1f - top / AdminHeightUnits, c, view.Sort != 0);
            AddSearchField(container, player, panel, g.Min(right - 250f, bottom), g.Max(right, top), view.Query, "news.admin.search", "news.admin.search", null);
        }

        private void AddUnsavedBanner(CuiElementContainer container, BasePlayer player, string panel, Grid g, EditorSession unsaved, string surface)
        {
            var c = config.Colors;
            const float width = AdminWidthUnits - 2f * AdminSidePad, height = 30f;
            var b = new Grid(width, height);
            string color = Blend(MixAlpha(c.ButtonPrimary, 0.10f), surface);
            string banner = AddSurface(container, panel, g.Min(AdminSidePad, 210f), g.Max(AdminWidthUnits - AdminSidePad, 180f), color, RadiusCard, null, panel + ".Unsaved", MixAlpha(c.ButtonPrimary, 0.40f));

            AddPill(container, banner, Msg("UnsavedBadge", player), b.Min(10f, 22f), b.Max(70f, 8f), c.ButtonPrimary, "1 1 1 1", 8);
            string title = string.IsNullOrEmpty(unsaved.Draft?.Title) ? Msg("NoTitle", player) : Ellipsize(unsaved.Draft.Title, 44);
            container.Add(new CuiLabel
            {
                Text = { Text = Msg("UnsavedPost", player, title, FormatSpan(NowTicks - unsaved.SavedAt, player)), FontSize = 10, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                RectTransform = { AnchorMin = b.Min(80f, height), AnchorMax = b.Max(width - 250f, 0f) }
            }, banner);
            AddPrimaryButton(container, banner, Msg("ContinueEditing", player), "news.admin.resume", b.Min(width - 240f, 25f), b.Max(width - 96f, 5f), c.ButtonPrimary, "1 1 1 1", 9);
            AddGhostButton(container, banner, Msg("Discard", player), "news.admin.discard", b.Min(width - 88f, 25f), b.Max(width - 8f, 5f), c.TextNormal, 9);
        }

        private void AddAdminRow(CuiElementContainer container, BasePlayer player, string panel, Grid g, Announcement ann, string name, float top, float height, int page, bool selected, string surface)
        {
            var c = config.Colors;
            const float width = AdminWidthUnits - 2f * AdminSidePad;
            var r = new Grid(width, height);
            float mid = height / 2f;
            bool compact = height < 46f;
            string typeColor = GetTypeColor(ann.Type);

            string rowColor = Blend(AdminSurfaceRaised, surface);
            if (selected) rowColor = Blend(MixAlpha(c.ButtonPrimary, 0.12f), rowColor);
            else if (ann.Pinned) rowColor = Blend("0.95 0.70 0.20 0.05", rowColor);
            string rim = selected ? MixAlpha(c.ButtonPrimary, 0.55f) : ann.Pinned ? "0.95 0.70 0.20 0.35" : ann.Draft ? "1 1 1 0.16" : null;
            AddSurface(container, panel, g.Min(AdminSidePad, top + height), g.Max(AdminWidthUnits - AdminSidePad, top), rowColor, RadiusCard, null, name, rim);

            string check = AddBox(container, name, r.Min(12f, mid + 9f), r.Max(30f, mid - 9f), selected ? c.ButtonPrimary : rowColor,
                $"news.admin.toggleselect {ann.Id} {page}", null, selected ? null : "1 1 1 0.22", 1.5f);
            if (selected) AddLabelIn(container, check, "✓", 11, "1 1 1 1");

            float textLeft = 54f;
            if (config.General.ArchiveThumbnails && !string.IsNullOrEmpty(ann.ImageUrl))
            {
                float thumbHeight = Math.Min(54f, height - 14f), thumbWidth = thumbHeight * 1.6f;
                string thumb = AddBox(container, name, r.Min(42f, mid + thumbHeight / 2f), r.Max(42f + thumbWidth, mid - thumbHeight / 2f), Blend("0 0 0 0.45", rowColor));
                container.Add(new CuiElement
                {
                    Parent = thumb,
                    Components = { BuildRawImage(ann.ImageUrl), new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } }
                });
                textLeft = 42f + thumbWidth + 12f;
            }
            else
            {
                float inset = Math.Min(14f, height * 0.22f);
                container.Add(Piece(typeColor, r.Min(42f, height - inset), r.Max(45f, inset), "0 0", "0 0", 0f), name);
            }

            const float buttonWidth = 46f, buttonGap = 5f, statWidth = 40f;
            float actionsLeft = width - 12f - (5f * buttonWidth + 4f * buttonGap);
            float statsLeft = actionsLeft - 14f - 3f * statWidth;
            float textRight = statsLeft - 10f;

            string title = Ellipsize((string.IsNullOrEmpty(ann.Title) ? Msg("NoTitle", player) : ann.Title).ToUpper(), (int)((textRight - textLeft) / 6.9f));
            container.Add(new CuiLabel
            {
                Text = { Text = title, FontSize = compact ? 12 : 13, Align = TextAnchor.LowerLeft, Color = ann.Draft ? c.TextNormal : c.TextTitle, Font = FontBold },
                RectTransform = { AnchorMin = r.Min(textLeft, mid), AnchorMax = r.Max(textRight, Math.Max(1f, mid - 22f)) }
            }, name);

            float lineTop = mid + (compact ? 2f : 4f), pillBottom = Math.Min(height - 2f, lineTop + 14f);
            float x = textLeft;
            x = AddRowPill(container, name, r, ann.Type.ToString().ToUpper(), typeColor, "1 1 1 1", x, lineTop, pillBottom);
            if (ann.Pinned) x = AddRowPill(container, name, r, Msg("PinnedBadge", player), PinGold, PinGoldText, x, lineTop, pillBottom);
            if (ann.Draft) x = AddRowPill(container, name, r, Msg("DraftBadge", player), Blend("1 1 1 0.16", rowColor), c.TextTitle, x, lineTop, pillBottom);
            if (HasPoll(ann)) x = AddRowPill(container, name, r, Msg("PollLabel", player), Blend(MixAlpha(c.ButtonPrimary, 0.22f), rowColor), c.ButtonPrimary, x, lineTop, pillBottom);
            if (!string.IsNullOrEmpty(ann.Group)) x = AddRowPill(container, name, r, Msg("GroupOnly", player, ann.Group.ToUpper()), Blend("1 1 1 0.12", rowColor), c.TextTitle, x, lineTop, pillBottom);

            long now = NowTicks;
            string meta = $"{ann.Date}  •  {Ellipsize(string.IsNullOrEmpty(ann.Author) ? Msg("Unknown", player) : ann.Author, 18)}";
            if (!string.IsNullOrEmpty(ann.EditedDate)) meta += $"  •  {Msg("EditedTag", player)}";
            if (!ann.Draft && ann.ExpiresAt > now) meta += $"  •  <color={RgbaToHex(HintWarn)}>{Msg("AdminDeletesIn", player, FormatSpan(ann.ExpiresAt - now, player).ToUpper())}</color>";
            if (!ann.Draft && IsPollOpen(ann) && ann.PollEndsAt > 0) meta += $"  •  <color={RgbaToHex(HintWarn)}>{Msg("AdminPollClosesIn", player, FormatSpan(ann.PollEndsAt - now, player).ToUpper())}</color>";
            container.Add(new CuiLabel
            {
                Text = { Text = meta, FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = FontRegular },
                RectTransform = { AnchorMin = r.Min(x + 3f, pillBottom), AnchorMax = r.Max(textRight, lineTop) }
            }, name);

            string[] values =
            {
                ann.Draft ? "–" : ann.ReadByPlayers.Count.ToString(),
                ann.Draft ? "–" : ann.LikedPlayers.Count.ToString(),
                HasPoll(ann) && !ann.Draft ? ann.PollVotes.Count.ToString() : "–"
            };
            string[] captions = { Msg("ColReads", player), Msg("ColLikes", player), Msg("ColVotes", player) };
            for (int i = 0; i < values.Length; i++)
            {
                float columnLeft = statsLeft + i * statWidth;
                bool empty = values[i] == "–";
                container.Add(new CuiLabel
                {
                    Text = { Text = values[i], FontSize = 13, Align = TextAnchor.LowerCenter, Color = empty ? c.TextMuted : c.TextTitle, Font = FontBold },
                    RectTransform = { AnchorMin = r.Min(columnLeft, mid + 1f), AnchorMax = r.Max(columnLeft + statWidth, Math.Max(1f, mid - 19f)) }
                }, name);
                container.Add(new CuiLabel
                {
                    Text = { Text = captions[i], FontSize = 7, Align = TextAnchor.UpperCenter, Color = c.TextMuted, Font = FontBold },
                    RectTransform = { AnchorMin = r.Min(columnLeft, Math.Min(height - 1f, mid + 14f)), AnchorMax = r.Max(columnLeft + statWidth, mid + 2f) }
                }, name);
            }

            float buttonHeight = Math.Min(28f, height - 10f);
            float buttonTop = mid - buttonHeight / 2f, buttonBottom = mid + buttonHeight / 2f;
            float step = buttonWidth + buttonGap;
            if (ann.Draft)
                AddPrimaryButton(container, name, Msg("PublishButton", player), $"news.admin.publish {ann.Id} {page}", r.Min(actionsLeft, buttonBottom), r.Max(actionsLeft + buttonWidth, buttonTop), c.ButtonPrimary, "1 1 1 1", 8);
            else
                AddGhostButton(container, name, Msg("ResendButton", player), $"news.admin.resend {ann.Id} {page}", r.Min(actionsLeft, buttonBottom), r.Max(actionsLeft + buttonWidth, buttonTop), c.TextNormal, 8);
            AddGhostButton(container, name, Msg(ann.Pinned ? "UnpinButton" : "PinButton", player), $"news.admin.togglepin {ann.Id} {page}", r.Min(actionsLeft + step, buttonBottom), r.Max(actionsLeft + step + buttonWidth, buttonTop), ann.Pinned ? PinGold : c.TextNormal, 8);
            AddGhostButton(container, name, Msg("CopyButton", player), $"news.admin.copy {ann.Id}", r.Min(actionsLeft + 2f * step, buttonBottom), r.Max(actionsLeft + 2f * step + buttonWidth, buttonTop), c.TextNormal, 8);
            AddGhostButton(container, name, Msg("EditButton", player), $"news.admin.edit {ann.Id}", r.Min(actionsLeft + 3f * step, buttonBottom), r.Max(actionsLeft + 3f * step + buttonWidth, buttonTop), c.ButtonPrimary, 8);
            AddGhostButton(container, name, Msg("DelButton", player), $"news.admin.delconfirm {ann.Id} {page}", r.Min(actionsLeft + 4f * step, buttonBottom), r.Max(actionsLeft + 4f * step + buttonWidth, buttonTop), "0.95 0.48 0.50 1", 8);
        }

        private float AddRowPill(CuiElementContainer container, string row, Grid r, string text, string background, string textColor, float left, float top, float bottom)
        {
            float width = text.Length * 4.6f + 12f;
            AddPill(container, row, text, r.Min(left, bottom), r.Max(left + width, top), background, textColor, 8);
            return left + width + 5f;
        }

        private void AddAdminFooter(CuiElementContainer container, BasePlayer player, string panel, Grid g, int page, int totalPages, List<Announcement> list, int start, int end, HashSet<string> selection, string surface)
        {
            var c = config.Colors;
            const float top = AdminHeightUnits - 44f, bottom = AdminHeightUnits - 14f;
            const float left = AdminSidePad, right = AdminWidthUnits - AdminSidePad;

            bool pageSelected = end > start;
            for (int i = start; i < end && pageSelected; i++) pageSelected = selection.Contains(list[i].Id);

            string select = container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = $"news.admin.selectpage {page}" },
                Text = { Text = "" },
                RectTransform = { AnchorMin = g.Min(left, bottom), AnchorMax = g.Max(left + 112f, top) }
            }, panel);
            var s = new Grid(112f, 30f);
            string box = AddBox(container, select, s.Min(12f, 24f), s.Max(30f, 6f), pageSelected ? c.ButtonPrimary : surface, null, null, pageSelected ? null : "1 1 1 0.22", 1.5f);
            if (pageSelected) AddLabelIn(container, box, "✓", 11, "1 1 1 1");
            container.Add(new CuiLabel
            {
                Text = { Text = Msg("SelectPageToggle", player), FontSize = 9, Align = TextAnchor.MiddleLeft, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = s.Min(38f, 30f), AnchorMax = s.Max(112f, 0f) }
            }, select);

            if (selection.Count > 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Msg("SelectedCount", player, selection.Count), FontSize = 10, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                    RectTransform = { AnchorMin = g.Min(left + 124f, bottom), AnchorMax = g.Max(left + 206f, top) }
                }, panel);
                float x = left + 210f;
                AddGhostButton(container, panel, Msg("BulkPin", player), $"news.admin.bulkpin 1 {page}", g.Min(x, bottom - 2f), g.Max(x + 70f, top + 2f), c.TextTitle, 9);
                AddGhostButton(container, panel, Msg("BulkUnpin", player), $"news.admin.bulkpin 0 {page}", g.Min(x + 76f, bottom - 2f), g.Max(x + 154f, top + 2f), c.TextTitle, 9);
                AddPrimaryButton(container, panel, Msg("BulkDelete", player), $"news.admin.bulkdelconfirm {page}", g.Min(x + 160f, bottom - 2f), g.Max(x + 230f, top + 2f), AdminDanger, "1 1 1 1", 9);
                AddGhostButton(container, panel, Msg("ClearSelection", player), $"news.admin.clearsel {page}", g.Min(x + 236f, bottom - 2f), g.Max(x + 296f, top + 2f), c.TextNormal, 9);
            }

            container.Add(new CuiLabel
            {
                Text = { Text = $"{Msg("PostsCount", player, list.Count)}  /  {Msg("Page", player, page + 1, totalPages)}", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = g.Min(right - 196f, bottom), AnchorMax = g.Max(right - 36f, top) }
            }, panel);
            if (page > 0)
                AddGhostButton(container, panel, "<", $"news.admin.page {page - 1}", g.Min(right - 230f, bottom - 2f), g.Max(right - 200f, top + 2f), c.TextNormal, 11);
            if (page < totalPages - 1)
                AddGhostButton(container, panel, ">", $"news.admin.page {page + 1}", g.Min(right - 30f, bottom - 2f), g.Max(right, top + 2f), c.TextNormal, 11);
        }

        private void AddEditorLabel(CuiElementContainer container, string parent, string text, float bottom, float top, UIColors c, float left = 0.05f, float right = 0.95f)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = 10, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = Anchor(left, bottom), AnchorMax = Anchor(right, top) }
            }, parent);
        }

        private void AddCycleButton(CuiElementContainer container, string parent, string value, string command, float left, float right, float bottom, float top, UIColors c, bool active)
        {
            string button = AddBox(container, parent, Anchor(left, bottom), Anchor(right, top), active ? MixAlpha(c.ButtonPrimary, 0.14f) : Ghost, command);
            container.Add(new CuiLabel
            {
                Text = { Text = value, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = active ? c.TextTitle : c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = "0.07 0", AnchorMax = "0.86 1" }
            }, button);
            container.Add(new CuiLabel
            {
                Text = { Text = "›", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = active ? c.ButtonPrimary : c.TextMuted, Font = FontBold },
                RectTransform = { AnchorMin = "0.86 0", AnchorMax = "0.97 1" }
            }, button);
        }

        private const string HintWarn = "0.97 0.74 0.32 1";
        private const string HintBad = "0.96 0.45 0.45 1";
        private const string HintGood = "0.47 0.82 0.55 1";

        private void AddEditorHint(CuiElementContainer container, string name, string text, string color, TextAnchor align, string anchorMin, string anchorMax)
        {
            container.Add(new CuiElement
            {
                Name = EditorPanelName + name,
                Parent = EditorPanelName,
                Components =
                {
                    new CuiTextComponent { Text = text, FontSize = 9, Align = align, Color = color, Font = FontBold },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax }
                }
            });
        }

        private void AddEditorHints(CuiElementContainer container, BasePlayer player, EditorSession session, string only = null)
        {
            var c = config.Colors;
            var ann = session.Draft;

            if (only == null || only == "title")
            {
                int length = (ann.Title ?? string.Empty).Length;
                string color = length >= MaxTitleChars ? HintBad : length > TitleLongChars ? HintWarn : c.TextMuted;
                AddEditorHint(container, ".TitleCount", Msg("TitleCounter", player, length, MaxTitleChars), color, TextAnchor.LowerRight, "0.4 0.855", "0.62 0.895");
            }

            if (only == null || only == "image")
            {
                string problem = ImageLinkProblem(ann.ImageUrl);
                string text = string.IsNullOrEmpty(ann.ImageUrl) ? string.Empty : problem == null ? Msg("ImageOk", player) : Msg(problem, player);
                AddEditorHint(container, ".ImageHint", text, problem == null ? HintGood : HintWarn, TextAnchor.UpperLeft, "0.037 0.66", "0.62 0.698");
            }

            if (only == null || only == "text")
            {
                int length = (ann.Text ?? string.Empty).Length;
                bool longPost = length > BodyLongChars;
                AddEditorHint(container, ".BodyCount", Msg(longPost ? "BodyCounterLong" : "BodyCounter", player, length), longPost ? HintWarn : c.TextMuted, TextAnchor.LowerRight, "0.3 0.62", "0.62 0.66");
            }

            if (only == null || only == "cdtime")
            {
                string text, color;
                long now = NowTicks;
                if (string.IsNullOrEmpty(session.CountdownInput)) { text = Msg("CountdownNone", player); color = c.TextMuted; }
                else if (ann.CountdownTo <= 0) { text = Msg("CountdownInvalid", player); color = HintBad; }
                else if (ann.CountdownTo <= now) { text = Msg("CountdownPast", player); color = HintWarn; }
                else { text = Msg("CountdownEnds", player, LocalTime(ann.CountdownTo), FormatSpan(ann.CountdownTo - now, player)); color = HintGood; }
                AddEditorHint(container, ".CountdownHint", text, color, TextAnchor.UpperLeft, "0.652 0.262", "0.965 0.298");
            }
        }

        private void RefreshEditorHint(BasePlayer player, EditorSession session, string field)
        {
            string name = field == "title" ? ".TitleCount" : field == "image" ? ".ImageHint" : field == "text" ? ".BodyCount" : field == "cdtime" ? ".CountdownHint" : null;
            UiScreen screen;
            if (name == null || player == null || !currentScreens.TryGetValue(player.userID, out screen) || screen != UiScreen.Editor) return;

            var container = new CuiElementContainer();
            AddEditorHints(container, player, session, field);
            CuiHelper.DestroyUi(player, EditorPanelName + name);
            CuiHelper.AddUi(player, container);
        }

        private void AddEditorInput(CuiElementContainer container, string parent, string field, string value, int limit, float bottom, float top, bool multiline, float left = 0.05f, float right = 0.95f, string prefix = null)
        {
            AddBox(container, parent, Anchor(left, bottom), Anchor(right, top), "1 1 1 0.045");

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
            string button = AddBox(container, parent, Anchor(left, bottom), Anchor(right, top), on ? MixAlpha(c.ButtonPrimary, 0.14f) : Ghost, command);

            float boxW = 0.56f * (top - bottom) * EditorHeightUnits / (Math.Max(0.05f, right - left) * EditorWidthUnits);
            string check = AddBox(container, button, Anchor(0.035f, 0.22f), Anchor(0.035f + boxW, 0.78f), on ? c.ButtonPrimary : "1 1 1 0.14");
            if (on) AddLabelIn(container, check, "✓", 11, "1 1 1 1");
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
            float anim = OpenScreen(player, UiScreen.Editor, out fresh);

            var container = new CuiElementContainer();
            var c = config.Colors;

            BeginScreen(container, fresh, AdminBackdrop);
            int contentStart = container.Count;
            AddPanelShadow(container, ScreenName, "0.13 0.1", "0.87 0.9");

            string editorPanel = AddSurface(container, ScreenName, "0.13 0.1", "0.87 0.9", Opaque(AdminSurface), RadiusPanel, null, EditorPanelName, PanelRim);
            AddTopSheen(container, editorPanel, RadiusPanel);

            string heading = session.IsNew ? Msg("CreateAnnouncement", player) : Msg("EditAnnouncement", player);
            container.Add(new CuiLabel { Text = { Text = heading, FontSize = 14, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold }, RectTransform = { AnchorMin = "0.035 0.915", AnchorMax = "0.7 1" } }, editorPanel);
            if (session.IsDraft)
            {
                float headingWidth = (heading.Length * 8.4f + 14f) / EditorWidthUnits;
                AddPill(container, editorPanel, Msg("DraftBadge", player), Anchor(0.035f + headingWidth, 0.935f), Anchor(0.035f + headingWidth + 0.055f, 0.975f), "1 1 1 0.14", c.TextTitle, 9);
            }
            container.Add(new CuiLabel { Text = { Text = Msg("AutosaveHint", player), FontSize = 9, Align = TextAnchor.MiddleRight, Color = c.TextMuted, Font = FontBold }, RectTransform = { AnchorMin = "0.6 0.932", AnchorMax = "0.925 0.978" } }, editorPanel);
            AddGhostButton(container, editorPanel, "✕", "news.editor.cancel", "0.94 0.932", "0.965 0.978", c.TextNormal, 12);
            AddHairline(container, editorPanel, 0.91f, 0.035f, 0.965f);

            const float L = 0.035f, LR = 0.62f;
            AddEditorLabel(container, editorPanel, Msg("AnnouncementTitle", player), 0.855f, 0.895f, c, L, 0.4f);
            AddEditorInput(container, editorPanel, "title", ann.Title, MaxTitleChars, 0.8f, 0.855f, false, L, LR);

            AddEditorLabel(container, editorPanel, Msg("ImageUrl", player), 0.755f, 0.795f, c, L, LR);
            AddEditorInput(container, editorPanel, "image", ann.ImageUrl, MaxImageUrlChars, 0.7f, 0.755f, false, L, LR);

            AddEditorLabel(container, editorPanel, Msg("ContentBody", player), 0.62f, 0.66f, c, L, 0.2f);
            container.Add(new CuiLabel { Text = { Text = Msg("BodyHintShort", player), FontSize = 9, Align = TextAnchor.LowerLeft, Color = c.TextMuted, Font = FontRegular }, RectTransform = { AnchorMin = "0.2 0.62", AnchorMax = "0.4 0.66" } }, editorPanel);
            AddEditorInput(container, editorPanel, "text", ann.Text, MaxContentChars, session.Poll ? 0.315f : 0.15f, 0.62f, true, L, LR);

            if (session.Poll)
            {
                AddEditorLabel(container, editorPanel, Msg("PollOptionsLabel", player), 0.265f, 0.305f, c, L, LR);
                for (int i = 0; i < MaxPollOptions; i++)
                {
                    float left = i % 2 == 0 ? L : 0.333f;
                    float top = i < 2 ? 0.255f : 0.198f;
                    AddEditorInput(container, editorPanel, "poll" + (i + 1), session.PollOptions[i], MaxPollOptionChars, top - 0.048f, top, false, left, left + 0.287f, (i + 1).ToString());
                }
            }

            const float R = 0.65f, RR = 0.965f, RM = 0.8075f;

            AddEditorLabel(container, editorPanel, Msg("TypeLabel", player), 0.855f, 0.895f, c, R, RR);
            var types = AllTypes;
            const float chipGap = 0.006f;
            float chipWidth = (RR - R - chipGap * (types.Length - 1)) / types.Length;
            for (int i = 0; i < types.Length; i++)
            {
                bool selected = types[i] == ann.Type;
                float left = R + i * (chipWidth + chipGap);
                string typeColor = GetTypeColor(types[i]);
                string chip = AddBox(container, editorPanel, Anchor(left, 0.8f), Anchor(left + chipWidth, 0.855f), selected ? typeColor : Ghost, $"news.editor.settype {types[i]}");
                AddLabelIn(container, chip, types[i].ToString().ToUpper(), 9, selected ? "1 1 1 1" : typeColor);
            }

            AddEditorLabel(container, editorPanel, Msg("Options", player), 0.755f, 0.795f, c, R, RR);
            var toggles = new List<string[]>
            {
                new[] { Msg("OptionPin", player), "pin", ann.Pinned ? "1" : "0" },
                new[] { Msg(session.Publishes ? "OptionNotify" : "OptionNotifyEdit", player), "notify", session.Notify ? "1" : "0" }
            };
            if (session.Publishes && DiscordConfigured) toggles.Add(new[] { Msg("OptionDiscord", player), "discord", session.Discord ? "1" : "0" });
            toggles.Add(new[] { Msg("OptionPoll", player), "poll", session.Poll ? "1" : "0" });
            for (int i = 0; i < toggles.Count; i++)
            {
                float left = i % 2 == 0 ? R : RM + 0.0075f;
                float right = i % 2 == 0 ? RM - 0.0075f : RR;
                float top = i < 2 ? 0.75f : 0.695f;
                AddEditorToggle(container, editorPanel, toggles[i][0], "news.editor.toggle " + toggles[i][1], toggles[i][2] == "1", left, right, c, top - 0.05f, top);
            }

            AddEditorLabel(container, editorPanel, Msg("AudienceLabel", player), 0.595f, 0.635f, c, R, RM);
            AddEditorLabel(container, editorPanel, Msg("ExpireLabel", player), 0.595f, 0.635f, c, RM + 0.0075f, RR);
            string audience = string.IsNullOrEmpty(ann.Group) ? Msg("AudienceEveryone", player) : Msg("GroupOnly", player, ann.Group.ToUpper());
            AddCycleButton(container, editorPanel, audience, "news.editor.toggle audience", R, RM - 0.0075f, 0.54f, 0.595f, c, !string.IsNullOrEmpty(ann.Group));
            string expire = ann.ExpireDays <= 0 ? Msg("ExpireNever", player) : ann.ExpireDays == 1 ? Msg("ExpireAfterDay", player) : Msg("ExpireAfterDays", player, ann.ExpireDays);
            AddCycleButton(container, editorPanel, expire, "news.editor.toggle expire", RM + 0.0075f, RR, 0.54f, 0.595f, c, ann.ExpireDays > 0);

            string expireInfo = string.Empty;
            long now = NowTicks;
            if (ann.ExpireDays > 0)
            {
                if (!session.Publishes && !session.ExpireChanged && ann.ExpiresAt > now)
                    expireInfo = Msg("ExpireInfo", player, LocalTime(ann.ExpiresAt), FormatSpan(ann.ExpiresAt - now, player));
                else if (!session.Publishes)
                    expireInfo = Msg("ExpireInfo", player, LocalTime(now + ann.ExpireDays * TimeSpan.TicksPerDay), FormatSpan(ann.ExpireDays * TimeSpan.TicksPerDay, player));
                else
                    expireInfo = Msg("ExpireCountsFromPublish", player);
            }
            container.Add(new CuiLabel { Text = { Text = expireInfo, FontSize = 9, Align = TextAnchor.UpperLeft, Color = c.TextMuted, Font = FontRegular }, RectTransform = { AnchorMin = "0.817 0.49", AnchorMax = "0.965 0.537" } }, editorPanel);

            AddEditorLabel(container, editorPanel, Msg("CountdownTitleField", player), 0.455f, 0.495f, c, R, RR);
            AddEditorInput(container, editorPanel, "cdlabel", ann.CountdownLabel, MaxCountdownLabelChars, 0.4f, 0.455f, false, R, RR);
            AddEditorLabel(container, editorPanel, Msg("CountdownTimeField", player), 0.355f, 0.395f, c, R, RR);
            AddEditorInput(container, editorPanel, "cdtime", session.CountdownInput, 40, 0.3f, 0.355f, false, R, RR);

            if (session.Poll)
            {
                AddEditorLabel(container, editorPanel, Msg("PollTimerLabel", player), 0.215f, 0.255f, c, R, RR);
                string pollTimer = ann.PollHours <= 0 ? Msg("PollTimerManual", player)
                    : ann.PollHours == 1 ? Msg("PollTimerHour", player)
                    : ann.PollHours == 24 ? Msg("PollTimerDay", player)
                    : ann.PollHours % 24 == 0 ? Msg("PollTimerDays", player, ann.PollHours / 24)
                    : Msg("PollTimerHours", player, ann.PollHours);
                AddCycleButton(container, editorPanel, pollTimer, "news.editor.toggle polltimer", R, RM - 0.0075f, 0.16f, 0.215f, c, ann.PollHours > 0);
                if (!session.Publishes)
                    AddEditorToggle(container, editorPanel, Msg("OptionPollClosed", player), "news.editor.toggle pollclosed", session.PollClosed, RM + 0.0075f, RR, c, 0.16f, 0.215f);
            }

            AddEditorHints(container, player, session);

            string footerPanel = editorPanel + ".Footer";
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.115" } }, editorPanel, footerPanel);
            AddHairline(container, editorPanel, 0.115f, 0.035f, 0.965f);

            AddGhostButton(container, footerPanel, Msg("Cancel", player), "news.editor.cancel", "0.035 0.2", "0.16 0.8", c.TextNormal, 11);
            AddGhostButton(container, footerPanel, Msg("Preview", player), "news.editor.preview", "0.17 0.2", "0.3 0.8", c.TextTitle, 11);
            if (session.Publishes)
            {
                AddGhostButton(container, footerPanel, Msg("SaveDraft", player), "news.editor.savedraft", "0.31 0.2", "0.47 0.8", c.TextTitle, 11);
                AddPrimaryButton(container, footerPanel, Msg("SaveBroadcast", player), "news.editor.save", "0.48 0.2", "0.965 0.8", c.ButtonPrimary);
            }
            else
            {
                AddPrimaryButton(container, footerPanel, Msg("SaveChanges", player), "news.editor.save", "0.31 0.2", "0.965 0.8", c.ButtonPrimary);
            }

            ApplyFadeRamp(container, contentStart, 0.12f * anim, 0.38f * anim);
            CuiHelper.AddUi(player, container);
        }

        private void ShowConfirmDialog(BasePlayer player, string title, string body, string confirmCommand)
        {
            FlushRedraw(player);
            var container = new CuiElementContainer();
            var c = config.Colors;

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.45", Material = BlurMaterial },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", ConfirmLayer, ConfirmLayer);

            AddPanelShadow(container, ConfirmLayer, "0.355 0.385", "0.645 0.615");

            string dialogPanel = AddSurface(container, ConfirmLayer, "0.355 0.385", "0.645 0.615", Opaque(AdminSurface), RadiusPanel, null, ConfirmLayer + ".Dialog", PanelRim);
            AddTopSheen(container, dialogPanel, RadiusPanel);

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

            ApplyFadeRamp(container, 0, 0.1f, 0.26f);
            CuiHelper.AddUi(player, container);
        }

        private void ShowPlayerSettings(BasePlayer player, bool redraw = false)
        {
            var c = config.Colors;
            var prefs = GetPrefs(player.userID);

            var container = new CuiElementContainer();
            if (!redraw)
            {
                FlushRedraw(player);
                container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0.45", Material = BlurMaterial },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    CursorEnabled = true
                }, "Overlay", ConfirmLayer, ConfirmLayer);
            }
            int contentStart = container.Count;
            string body = ConfirmLayer + ".Body";
            container.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, ConfirmLayer, body, body);

            const string min = "0.355 0.31", max = "0.645 0.69";
            AddPanelShadow(container, body, min, max);
            string panel = AddSurface(container, body, min, max, Opaque(c.PanelBg), RadiusPanel, null, ConfirmLayer + ".Settings", PanelRim);
            AddTopSheen(container, panel, RadiusPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = Msg("SettingsTitle", player), FontSize = 14, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                RectTransform = { AnchorMin = "0.06 0.86", AnchorMax = "0.8 0.97" }
            }, panel);
            AddGhostButton(container, panel, "✕", "news.confirm.close", "0.885 0.885", "0.945 0.955", c.TextNormal, 11);
            AddHairline(container, panel, 0.845f, 0.06f, 0.94f);

            var rows = new[]
            {
                new[] { Msg("SettingSound", player), "sound", prefs.Sound ? "1" : "0", null },
                new[] { Msg("SettingPopups", player), "popups", prefs.Popups ? "1" : "0", Msg("SettingPopupsHint", player) },
                new[] { Msg("SettingJoin", player), "join", prefs.JoinPopup ? "1" : "0", null }
            };
            for (int i = 0; i < rows.Length; i++)
            {
                float top = 0.8f - i * 0.185f, bottom = top - 0.15f;
                bool on = rows[i][2] == "1";
                bool hasHint = rows[i][3] != null;
                container.Add(new CuiLabel
                {
                    Text = { Text = rows[i][0], FontSize = 11, Align = hasHint ? TextAnchor.LowerLeft : TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold },
                    RectTransform = { AnchorMin = Anchor(0.06f, hasHint ? bottom + 0.075f : bottom), AnchorMax = Anchor(0.7f, top) }
                }, panel);
                if (hasHint)
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = rows[i][3], FontSize = 9, Align = TextAnchor.UpperLeft, Color = c.TextMuted, Font = FontRegular },
                        RectTransform = { AnchorMin = Anchor(0.06f, bottom), AnchorMax = Anchor(0.7f, bottom + 0.07f) }
                    }, panel);
                }

                string toggle = AddBox(container, panel, Anchor(0.76f, bottom + 0.03f), Anchor(0.94f, top - 0.03f), on ? c.ButtonPrimary : Ghost,
                    "news.settings.toggle " + rows[i][1], null, on ? null : "1 1 1 0.14");
                AddLabelIn(container, toggle, Msg(on ? "SettingOn" : "SettingOff", player), 10, on ? "1 1 1 1" : c.TextMuted);
            }

            AddPrimaryButton(container, panel, Msg("Done", player), "news.confirm.close", "0.06 0.06", "0.94 0.16", c.ButtonPrimary);

            if (!redraw) ApplyFadeRamp(container, contentStart, 0.1f, 0.26f);
            CuiHelper.AddUi(player, container);
        }

        private void ShowDeleteConfirm(BasePlayer player, string id, int returnPage)
        {
            var ann = FindById(id);
            if (ann == null) return;
            ShowConfirmDialog(player, Msg("DeleteAnnouncement", player), Msg("DeleteConfirmBody", player, Ellipsize(ann.Title, 32)), $"news.admin.del {ann.Id} {returnPage}");
        }

        private void ShowBulkDeleteConfirm(BasePlayer player, int returnPage)
        {
            var sel = GetAdminSelection(player.userID);
            if (sel.Count == 0) return;
            ShowConfirmDialog(player, Msg("BulkDeleteTitle", player), Msg("BulkDeleteBody", player, sel.Count), $"news.admin.bulkdel {returnPage}");
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
            string matchedTheme = null;
            foreach (string key in config.Themes.Keys)
                if (string.Equals(key, themeName, StringComparison.OrdinalIgnoreCase)) { matchedTheme = key; break; }

            if (matchedTheme == null)
            {
                SendReply(player, Msg("ThemeNotFound", player, themeName, string.Join(", ", config.Themes.Keys)));
                return;
            }

            config.SelectedTheme = matchedTheme;
            SaveConfig();
            ShowThemeSelection(player, page);
            SendReply(player, Msg("ThemeSet", player, matchedTheme));
        }

        private void ShowThemeSelection(BasePlayer player, int page)
        {
            bool fresh;
            float anim = OpenScreen(player, UiScreen.Themes, out fresh);

            var container = new CuiElementContainer();
            var c = config.Colors;

            BeginScreen(container, fresh, AdminBackdrop);
            int contentStart = container.Count;

            var names = Pool.Get<List<string>>();
            names.AddRange(config.Themes.Keys);
            int totalPages = Math.Max(1, Mathf.CeilToInt((float)names.Count / ThemesPerPage));
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;

            int onPage = Math.Min(ThemesPerPage, names.Count - page * ThemesPerPage);
            int rows = Math.Max(1, Math.Min(4, (onPage + 1) / 2));
            const float headerH = 0.066f, padTop = 0.022f, cardH = 0.118f, gapY = 0.014f;
            float padBottom = totalPages > 1 ? 0.064f : 0.026f;
            float panelH = headerH + padTop + rows * cardH + (rows - 1) * gapY + padBottom;

            AddPanelShadow(container, ScreenName, Anchor(0.27f, 0.5f - panelH / 2f), Anchor(0.73f, 0.5f + panelH / 2f));

            string surface = Opaque(AdminSurface);
            string mainPanel = AddSurface(container, ScreenName, Anchor(0.27f, 0.5f - panelH / 2f), Anchor(0.73f, 0.5f + panelH / 2f), surface, RadiusPanel, null, LayerName + ".Themes", PanelRim);
            AddTopSheen(container, mainPanel, RadiusPanel);

            float headerBottom = 1f - headerH / panelH;
            AddHairline(container, mainPanel, headerBottom, 0.05f, 0.95f);
            container.Add(new CuiLabel { Text = { Text = Msg("SelectTheme", player), FontSize = 16, Align = TextAnchor.MiddleLeft, Color = c.TextTitle, Font = FontBold }, RectTransform = { AnchorMin = Anchor(0.05f, headerBottom), AnchorMax = "0.7 1" } }, mainPanel);

            AddGhostButton(container, mainPanel, Msg("Back", player), "news.admin.page 0", Anchor(0.80f, 1f - headerH * 0.78f / panelH), Anchor(0.95f, 1f - headerH * 0.22f / panelH), c.TextNormal, 10);

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

                string tray = AddBox(container, card, "0.06 0.13", "0.94 0.47", Blend("0 0 0 0.35", cardColor));
                string[] swatches = { theme.PanelBg, theme.HeaderBg, theme.ContentBg, theme.ButtonPrimary, theme.ButtonSecondary, theme.TextTitle };
                float swatchWidth = (0.96f - 0.012f * (swatches.Length - 1)) / swatches.Length;
                for (int s = 0; s < swatches.Length; s++)
                {
                    float sx = 0.02f + s * (swatchWidth + 0.012f);
                    container.Add(new CuiPanel { Image = { Color = Opaque(swatches[s]) }, RectTransform = { AnchorMin = Anchor(sx, 0.18f), AnchorMax = Anchor(sx + swatchWidth, 0.82f) } }, tray);
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
            Pool.FreeUnmanaged(ref names);
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
                if (warnedRewardProblems.Add(reason))
                    PrintWarning($"Rewards are not being paid (and not recorded, so they can be paid later): {reason}. This is only reported once.");
                return false;
            }

            var grantedNames = Pool.Get<List<string>>();

            if (bundle.Items != null)
            {
                foreach (var r in bundle.Items)
                {
                    if (r == null || string.IsNullOrWhiteSpace(r.Shortname) || r.Amount <= 0) continue;

                    var item = ItemManager.CreateByName(r.Shortname, r.Amount, r.SkinId);
                    string label = item.info.displayName.translated;
                    if (string.IsNullOrEmpty(label)) label = r.Shortname;
                    player.GiveItem(item);

                    grantedNames.Add($"{r.Amount}× {label}");
                }
            }

            if (bundle.Points > 0)
            {
                ServerRewards.Call("AddPoints", (ulong)player.userID, bundle.Points);
                grantedNames.Add($"{bundle.Points} {config.Rewards.PointsLabel}");
            }

            if (bundle.Currency > 0)
            {
                Economics.Call("Deposit", player.UserIDString, bundle.Currency);
                grantedNames.Add($"{bundle.Currency.ToString("0.##", CultureInfo.InvariantCulture)} {config.Rewards.CurrencyLabel}");
            }

            bool granted = grantedNames.Count > 0;
            if (granted && config.Rewards.NotifyOnReward)
                SendReply(player, Msg(langKey, player, string.Join(", ", grantedNames)));

            Pool.FreeUnmanaged(ref grantedNames);
            return granted;
        }

        private void CancelReadRewardTimer(ulong userId)
        {
            Timer t;
            if (readRewardTimers.TryGetValue(userId, out t))
            {
                t?.Destroy();
                readRewardTimers.Remove(userId);
            }
        }

        private void ScheduleReadCompletion(BasePlayer player, Announcement ann)
        {
            if (player == null || ann == null || string.IsNullOrEmpty(ann.Id)) return;

            ulong userId = player.userID;
            bool rewardDone = !config.Rewards.EnableReadReward || ann.ReadRewardedPlayers.Contains(userId);
            if (ann.ReadByPlayers.Contains(userId) && rewardDone) return;

            CancelReadRewardTimer(userId);
            int delay = Math.Max(1, config.Rewards.ReadDelaySeconds);
            string targetId = ann.Id;

            readRewardTimers[userId] = timer.Once(delay, () =>
            {
                readRewardTimers.Remove(userId);
                if (player == null || !player.IsConnected || !IsPopupOpen(userId, targetId)) return;

                var current = FindById(targetId);
                if (current == null) return;

                bool firstRead = current.ReadByPlayers.Add(userId);
                bool rewardDue = config.Rewards.EnableReadReward && !current.ReadRewardedPlayers.Contains(userId);
                bool grantReward = rewardDue && GiveRewards(player, config.Rewards.ReadRewards, "RewardRead");
                if (grantReward) current.ReadRewardedPlayers.Add(userId);

                if (grantReward || firstRead) MarkDataDirty();
                if (firstRead) Interface.CallHook("OnNewsRead", player, BuildHookData(current));
            });
        }
        #endregion

        #region Discord Webhooks
        private void SendToDiscord(Announcement ann)
        {
            if (!DiscordConfigured || ann == null || ann.Draft) return;
            if (!string.IsNullOrEmpty(ann.Group) && !config.Discord.PostGroupOnlyPosts) return;

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
                string truncatedSuffix = "\n\n" + Msg("DiscordTruncated");
                int keepLength = Math.Max(0, DiscordEmbedDescriptionLimit - truncatedSuffix.Length);
                discordBody = discordBody.Substring(0, Math.Min(keepLength, discordBody.Length)).TrimEnd() + truncatedSuffix;
            }

            string timestamp = ann.Timestamp > 0 ? new DateTime(ann.Timestamp, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture) : null;
            var fields = HasPoll(ann)
                ? new[] { new { name = Msg("PollDiscordField"), value = LimitText("• " + string.Join("\n• ", ann.PollOptions), 1024), inline = false } }
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
                        footer = new { text = Msg("DiscordPostedBy", null, ann.Author) },
                        timestamp = timestamp,
                        fields = fields,
                        image = string.IsNullOrEmpty(ann.ImageUrl) ? null : new { url = ann.ImageUrl }
                    }
                }
            };

            PostWebhook(payload);
        }

        private void SendPollResultsToDiscord(Announcement ann)
        {
            if (!DiscordConfigured || ann == null || !HasPoll(ann)) return;
            int[] counts = CountVotes(ann);
            int total = Sum(counts);
            int best = -1;
            if (total > 0) foreach (int count in counts) best = Math.Max(best, count);
            var lines = new StringBuilder();
            for (int i = 0; i < ann.PollOptions.Count; i++)
            {
                int pct = total > 0 ? Mathf.RoundToInt(100f * counts[i] / total) : 0;
                string line = $"{ann.PollOptions[i]}  -  {pct}% ({counts[i]})";
                lines.Append('\n').Append(counts[i] == best ? "**" + line + "**" : line);
            }

            var payload = new
            {
                username = string.IsNullOrWhiteSpace(config.Discord.BotName) ? null : LimitText(config.Discord.BotName, 80),
                avatar_url = string.IsNullOrWhiteSpace(config.Discord.AvatarUrl) ? null : config.Discord.AvatarUrl,
                allowed_mentions = new { parse = new string[0] },
                embeds = new[]
                {
                    new
                    {
                        author = new { name = LimitText($"{Msg("PollDiscordResults").ToUpper()}  •  {config.General.ServerName}", 256) },
                        title = LimitText(ann.Title, 256),
                        description = LimitText(PollResultText(ann, null) + "\n" + lines, DiscordEmbedDescriptionLimit),
                        color = 3447003,
                        timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                    }
                }
            };
            PostWebhook(payload);
        }

        private void PostWebhook(object payload)
        {
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
