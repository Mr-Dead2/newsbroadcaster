using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NewsBroadcaster", "YourName", "3.6.0")]
    [Description("Modern popup news broadcaster with sleek pop-up design and advanced text scaling")]
    public class NewsBroadcaster : RustPlugin
    {
        private const string PanelName = "NewsBroadcasterUI";
        private const string HistoryPanel = "NewsHistoryUI";
        private const string BackdropName = "NewsBackdropUI";
        private const string DataFile = "NewsBroadcaster_Data";

        private ConfigData config;
        private readonly List<Announcement> announcements = new List<Announcement>();
        private readonly Dictionary<ulong, HashSet<string>> playerUIs = new Dictionary<ulong, HashSet<string>>();
        private readonly Dictionary<ulong, Timer> autoCloseTimers = new Dictionary<ulong, Timer>();

        #region Data
        class Announcement
        {
            public string Title;
            public string ImageUrl;
            public string Text;
            public string Date;
            public string Author;
            public AnnouncementType Type;
            public int Priority; // 1=Low, 2=Normal, 3=High, 4=Critical
        }

        enum AnnouncementType
        {
            Info,
            Warning,
            Alert,
            Update,
            Event
        }
        #endregion

        #region Enhanced Config
        class ConfigData
        {
            public GeneralSettings General { get; set; } = new GeneralSettings();
            public UISettings UI { get; set; } = new UISettings();
            public TextScalingSettings TextScaling { get; set; } = new TextScalingSettings();
            public SoundSettings Sounds { get; set; } = new SoundSettings();
            public AnimationSettings Animations { get; set; } = new AnimationSettings();
            public AdvancedSettings Advanced { get; set; } = new AdvancedSettings();
        }

        class GeneralSettings
        {
            public int AnnouncementsPerPage { get; set; } = 4;
            public int MaxStoredAnnouncements { get; set; } = 100;
            public bool EnableDebugMode { get; set; } = false;
            public List<string> AllowedCommands { get; set; } = new List<string> { "news", "announcements", "updates" };
        }

        class UISettings
        {
            public PositionSettings Position { get; set; } = new PositionSettings();
            public BrandingSettings Branding { get; set; } = new BrandingSettings();
            public BehaviorSettings Behavior { get; set; } = new BehaviorSettings();
            public ColorSettings Colors { get; set; } = new ColorSettings();
            public LayoutSettings Layout { get; set; } = new LayoutSettings();
        }

        class TextScalingSettings
        {
            public float GlobalTextScale { get; set; } = 1.0f; // Global multiplier for all text
            public FontSizeSettings FontSizes { get; set; } = new FontSizeSettings();
            public ResponsiveSettings Responsive { get; set; } = new ResponsiveSettings();
            public AccessibilitySettings Accessibility { get; set; } = new AccessibilitySettings();
        }

        class FontSizeSettings
        {
            public int BaseTitleSize { get; set; } = 18;
            public int BaseHeaderSize { get; set; } = 14;
            public int BaseParagraphSize { get; set; } = 12;
            public int BaseSmallSize { get; set; } = 10;
            public int BaseTinySize { get; set; } = 8;
            
            // Font family preferences
            public string PrimaryFont { get; set; } = "robotocondensed-bold.ttf";
            public string SecondaryFont { get; set; } = "robotocondensed-regular.ttf";
            public string MonospaceFont { get; set; } = "droidsansmono.ttf";
        }

        class ResponsiveSettings
        {
            public bool EnableResponsiveText { get; set; } = true;
            public Dictionary<string, float> ScreenSizeMultipliers { get; set; } = new Dictionary<string, float>
            {
                ["small"] = 0.8f,   // For smaller UI scales
                ["normal"] = 1.0f,  // Default
                ["large"] = 1.2f,   // For better readability
                ["xlarge"] = 1.4f   // For accessibility
            };
            public string DefaultScreenSize { get; set; } = "normal";
        }

        class AccessibilitySettings
        {
            public bool HighContrastMode { get; set; } = false;
            public bool BoldText { get; set; } = false;
            public float LineSpacing { get; set; } = 1.0f;
            public int MaxTextWidth { get; set; } = 50; // Characters per line
            public bool UseAccessibleColors { get; set; } = false;
        }

        class LayoutSettings
        {
            public PopupSizePresets PopupSize { get; set; } = PopupSizePresets.Medium;
            public bool ShowImagePreviews { get; set; } = true;
            public bool ShowTypeIcons { get; set; } = true;
            public bool ShowPriorityIndicators { get; set; } = true;
            public float ContentPadding { get; set; } = 0.02f;
            public float ElementSpacing { get; set; } = 0.01f;
        }

        enum PopupSizePresets
        {
            Compact,    // 0.35-0.65
            Medium,     // 0.3-0.7
            Large,      // 0.25-0.75
            FullWidth   // 0.1-0.9
        }

        class PositionSettings
        {
            public string Preset { get; set; } = "Center";
            public string CustomAnchorMin { get; set; } = "0.3 0.3";
            public string CustomAnchorMax { get; set; } = "0.7 0.7";
            public Dictionary<string, PositionPreset> Presets { get; set; } = new Dictionary<string, PositionPreset>
            {
                ["Center"] = new PositionPreset { AnchorMin = "0.3 0.3", AnchorMax = "0.7 0.7" },
                ["TopCenter"] = new PositionPreset { AnchorMin = "0.3 0.7", AnchorMax = "0.7 0.95" },
                ["BottomCenter"] = new PositionPreset { AnchorMin = "0.3 0.05", AnchorMax = "0.7 0.3" },
                ["LeftSide"] = new PositionPreset { AnchorMin = "0.05 0.3", AnchorMax = "0.45 0.7" },
                ["RightSide"] = new PositionPreset { AnchorMin = "0.55 0.3", AnchorMax = "0.95 0.7" },
                ["FullScreen"] = new PositionPreset { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" }
            };
        }

        class PositionPreset
        {
            public string AnchorMin { get; set; }
            public string AnchorMax { get; set; }
        }

        class BrandingSettings
        {
            public string LogoUrl { get; set; } = "";
            public string HeaderImageUrl { get; set; } = "";
            public string ServerName { get; set; } = "Server News";
            public string WatermarkText { get; set; } = "";
            public bool ShowServerLogo { get; set; } = true;
            public LogoPosition LogoPosition { get; set; } = LogoPosition.HeaderLeft;
        }

        enum LogoPosition
        {
            HeaderLeft,
            HeaderCenter,
            HeaderRight,
            BottomLeft,
            BottomRight
        }

        class BehaviorSettings
        {
            public bool AutoCloseEnabled { get; set; } = true;
            public int AutoCloseSeconds { get; set; } = 15;
            public bool ShowCloseTimer { get; set; } = true;
            public bool EnableSounds { get; set; } = true;
            public bool EnableAnimations { get; set; } = true;
            public bool BlockGameInput { get; set; } = false;
            public bool ShowOnPlayerConnect { get; set; } = false;
            public bool RememberPlayerPreferences { get; set; } = false;
        }

        class ColorSettings
        {
            // Base UI colors with better organization
            public UIPanelColors Panels { get; set; } = new UIPanelColors();
            public TextColors Text { get; set; } = new TextColors();
            public ButtonColors Buttons { get; set; } = new ButtonColors();
            public PriorityColors Priority { get; set; } = new PriorityColors();
            public AccessibilityColors Accessibility { get; set; } = new AccessibilityColors();
        }

        class UIPanelColors
        {
            public string Backdrop { get; set; } = "0 0 0 0.6";
            public string PopupPanel { get; set; } = "0.1 0.1 0.12 0.95";
            public string PopupBorder { get; set; } = "0.95 0.5 0.2 0.8";
            public string HeaderGradient { get; set; } = "0.2 0.2 0.25 0.9";
            public string ContentPanel { get; set; } = "0.08 0.08 0.1 0.85";
            public string HistoryPanel { get; set; } = "0.12 0.12 0.15 0.95";
            public string CardPanel { get; set; } = "0.15 0.15 0.18 0.9";
            public string AccentBar { get; set; } = "0.95 0.5 0.2 1";
        }

        class TextColors
        {
            public string TitleText { get; set; } = "1 1 1 1";
            public string BodyText { get; set; } = "0.9 0.9 0.9 1";
            public string MutedText { get; set; } = "0.7 0.7 0.7 1";
            public string AccentText { get; set; } = "0.95 0.6 0.3 1";
            public string ErrorText { get; set; } = "0.9 0.3 0.3 1";
            public string SuccessText { get; set; } = "0.3 0.9 0.3 1";
            public string WarningText { get; set; } = "0.9 0.9 0.3 1";
        }

        class ButtonColors
        {
            public string PrimaryButton { get; set; } = "0.25 0.6 0.9 0.9";
            public string SecondaryButton { get; set; } = "0.5 0.5 0.5 0.8";
            public string DangerButton { get; set; } = "0.8 0.3 0.3 0.9";
            public string CloseButton { get; set; } = "0.9 0.4 0.4 0.95";
            public string ButtonText { get; set; } = "1 1 1 1";
            public string ButtonHover { get; set; } = "0.3 0.7 1 0.95";
        }

        class PriorityColors
        {
            public string InfoColor { get; set; } = "0.2 0.7 0.9 1";
            public string WarningColor { get; set; } = "0.9 0.7 0.2 1";
            public string AlertColor { get; set; } = "0.9 0.3 0.2 1";
            public string EventColor { get; set; } = "0.6 0.3 0.9 1";
            public string UpdateColor { get; set; } = "0.3 0.9 0.6 1";
        }

        class AccessibilityColors
        {
            public bool UseHighContrast { get; set; } = false;
            public string HighContrastBackground { get; set; } = "0 0 0 1";
            public string HighContrastText { get; set; } = "1 1 1 1";
            public string HighContrastAccent { get; set; } = "1 1 0 1";
        }

        class SoundSettings
        {
            public string PopupSound { get; set; } = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab";
            public string CloseSound { get; set; } = "assets/bundled/prefabs/fx/notice/item.deselect.fx.prefab";
            public string AlertSound { get; set; } = "assets/bundled/prefabs/fx/explosions/explosion_01.prefab";
            public string WarningSound { get; set; } = "assets/bundled/prefabs/fx/notice/item.notice.fx.prefab";
            public float SoundVolume { get; set; } = 1.0f;
            public bool EnableSpatialAudio { get; set; } = false;
        }

        class AnimationSettings
        {
            public float SlideInDuration { get; set; } = 0.3f;
            public float FadeInDuration { get; set; } = 0.2f;
            public float SlideOutDuration { get; set; } = 0.2f;
            public string AnimationType { get; set; } = "SlideFromTop"; // SlideFromTop, SlideFromBottom, FadeIn, ScaleIn
            public bool EnableTransitions { get; set; } = true;
        }

        class AdvancedSettings
        {
            public int UIUpdateRate { get; set; } = 30; // Updates per second for animations
            public bool UseAdvancedRendering { get; set; } = false;
            public int MaxConcurrentPopups { get; set; } = 3;
            public bool EnablePerformanceMode { get; set; } = false;
            public Dictionary<string, object> CustomSettings { get; set; } = new Dictionary<string, object>();
        }
        #endregion

        #region Config I/O
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null) throw new Exception("Null config");
                EnsureConfigDefaults();
            }
            catch
            {
                PrintError("[NewsBroadcaster] Invalid config. Creating defaults.");
                config = new ConfigData();
            }
            SaveConfig();
        }

        private void EnsureConfigDefaults()
        {
            // Ensure all nested objects are initialized
            if (config.General == null) config.General = new GeneralSettings();
            if (config.UI == null) config.UI = new UISettings();
            if (config.TextScaling == null) config.TextScaling = new TextScalingSettings();
            if (config.Sounds == null) config.Sounds = new SoundSettings();
            if (config.Animations == null) config.Animations = new AnimationSettings();
            if (config.Advanced == null) config.Advanced = new AdvancedSettings();

            // UI sub-objects
            if (config.UI.Position == null) config.UI.Position = new PositionSettings();
            if (config.UI.Branding == null) config.UI.Branding = new BrandingSettings();
            if (config.UI.Behavior == null) config.UI.Behavior = new BehaviorSettings();
            if (config.UI.Colors == null) config.UI.Colors = new ColorSettings();
            if (config.UI.Layout == null) config.UI.Layout = new LayoutSettings();

            // TextScaling sub-objects
            if (config.TextScaling.FontSizes == null) config.TextScaling.FontSizes = new FontSizeSettings();
            if (config.TextScaling.Responsive == null) config.TextScaling.Responsive = new ResponsiveSettings();
            if (config.TextScaling.Accessibility == null) config.TextScaling.Accessibility = new AccessibilitySettings();

            // Color sub-objects
            if (config.UI.Colors.Panels == null) config.UI.Colors.Panels = new UIPanelColors();
            if (config.UI.Colors.Text == null) config.UI.Colors.Text = new TextColors();
            if (config.UI.Colors.Buttons == null) config.UI.Colors.Buttons = new ButtonColors();
            if (config.UI.Colors.Priority == null) config.UI.Colors.Priority = new PriorityColors();
            if (config.UI.Colors.Accessibility == null) config.UI.Colors.Accessibility = new AccessibilityColors();

            // Initialize dictionaries if null
            if (config.UI.Position.Presets == null) 
            {
                config.UI.Position.Presets = new Dictionary<string, PositionPreset>
                {
                    ["Center"] = new PositionPreset { AnchorMin = "0.3 0.3", AnchorMax = "0.7 0.7" },
                    ["TopCenter"] = new PositionPreset { AnchorMin = "0.3 0.7", AnchorMax = "0.7 0.95" },
                    ["BottomCenter"] = new PositionPreset { AnchorMin = "0.3 0.05", AnchorMax = "0.7 0.3" }
                };
            }

            if (config.TextScaling.Responsive.ScreenSizeMultipliers == null)
            {
                config.TextScaling.Responsive.ScreenSizeMultipliers = new Dictionary<string, float>
                {
                    ["small"] = 0.8f,
                    ["normal"] = 1.0f,
                    ["large"] = 1.2f,
                    ["xlarge"] = 1.4f
                };
            }
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);
        protected override void LoadDefaultConfig() => config = new ConfigData();
        #endregion

        #region Text Scaling Helpers
        private int GetScaledFontSize(FontType fontType, BasePlayer player = null)
        {
            var fontSizes = config.TextScaling.FontSizes;
            var globalScale = config.TextScaling.GlobalTextScale;
            var responsiveScale = GetResponsiveScale(player);
            var accessibilityScale = config.TextScaling.Accessibility.BoldText ? 1.1f : 1.0f;
            
            int baseSize = fontType switch
            {
                FontType.Title => fontSizes.BaseTitleSize,
                FontType.Header => fontSizes.BaseHeaderSize,
                FontType.Paragraph => fontSizes.BaseParagraphSize,
                FontType.Small => fontSizes.BaseSmallSize,
                FontType.Tiny => fontSizes.BaseTinySize,
                _ => fontSizes.BaseParagraphSize
            };

            float finalScale = globalScale * responsiveScale * accessibilityScale;
            return Mathf.RoundToInt(baseSize * finalScale);
        }

        private float GetResponsiveScale(BasePlayer player = null)
        {
            if (!config.TextScaling.Responsive.EnableResponsiveText)
                return 1.0f;

            var screenSize = config.TextScaling.Responsive.DefaultScreenSize;
            
            // You could implement player-specific scaling here based on their preferences
            // For now, we'll use the default screen size
            
            if (config.TextScaling.Responsive.ScreenSizeMultipliers.TryGetValue(screenSize, out float multiplier))
                return multiplier;
                
            return 1.0f;
        }

        private string GetScaledFont(FontStyle style)
        {
            var fonts = config.TextScaling.FontSizes;
            var accessibility = config.TextScaling.Accessibility;
            
            // Use bold fonts if accessibility mode is enabled
            if (accessibility.BoldText && style == FontStyle.Regular)
                style = FontStyle.Bold;

            return style switch
            {
                FontStyle.Bold => fonts.PrimaryFont,
                FontStyle.Regular => fonts.SecondaryFont,
                FontStyle.Monospace => fonts.MonospaceFont,
                _ => fonts.SecondaryFont
            };
        }

        private string GetAccessibleTextColor(string defaultColor)
        {
            if (!config.UI.Colors.Accessibility.UseHighContrast)
                return defaultColor;
                
            return config.UI.Colors.Accessibility.HighContrastText;
        }

        private string GetPositionAnchors(string type)
        {
            var position = config.UI.Position;
            var layout = config.UI.Layout;
            
            if (type == "popup")
            {
                // Handle popup size presets
                return layout.PopupSize switch
                {
                    PopupSizePresets.Compact => "0.35 0.35|0.65 0.65",
                    PopupSizePresets.Medium => "0.3 0.3|0.7 0.7",
                    PopupSizePresets.Large => "0.25 0.25|0.75 0.75",
                    PopupSizePresets.FullWidth => "0.1 0.1|0.9 0.9",
                    _ => "0.3 0.3|0.7 0.7"
                };
            }
            
            // Handle position presets
            if (position.Presets.TryGetValue(position.Preset, out var preset))
                return $"{preset.AnchorMin}|{preset.AnchorMax}";
                
            return $"{position.CustomAnchorMin}|{position.CustomAnchorMax}";
        }

        enum FontType
        {
            Title,
            Header,
            Paragraph,
            Small,
            Tiny
        }

        enum FontStyle
        {
            Regular,
            Bold,
            Monospace
        }
        #endregion

        #region Data File
        private void SaveAnnouncements() 
        {
            // Limit stored announcements to prevent excessive memory usage
            while (announcements.Count > config.General.MaxStoredAnnouncements)
            {
                announcements.RemoveAt(0);
            }
            Interface.Oxide.DataFileSystem.WriteObject(DataFile, announcements);
        }

        private void LoadAnnouncements()
        {
            announcements.Clear();
            var list = Interface.Oxide.DataFileSystem.ReadObject<List<Announcement>>(DataFile);
            if (list != null) announcements.AddRange(list);
        }
        #endregion

        #region Hooks
        void Init() 
        {
            LoadAnnouncements();
            
            // Register additional chat commands from config
            foreach (var command in config.General.AllowedCommands)
            {
                cmd.AddChatCommand(command, this, "CmdNewsHistory");
            }
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null)
            {
                playerUIs.Remove(player.userID);
                if (autoCloseTimers.ContainsKey(player.userID))
                {
                    autoCloseTimers[player.userID]?.Destroy();
                    autoCloseTimers.Remove(player.userID);
                }
            }
        }

        void OnPlayerConnected(BasePlayer player)
        {
            if (config.UI.Behavior.ShowOnPlayerConnect && announcements.Count > 0)
            {
                timer.Once(2f, () => 
                {
                    if (player?.IsConnected == true)
                    {
                        var latestAnnouncement = announcements.OrderByDescending(a => a.Date).FirstOrDefault();
                        if (latestAnnouncement != null)
                            ShowPopupAnnouncement(player, latestAnnouncement);
                    }
                });
            }
        }
        #endregion

        #region Commands
        [ConsoleCommand("news.show")]
        private void CmdNewsShow(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleOrAdmin(arg)) return;

            if (arg.Args == null || arg.Args.Length < 3)
            {
                SendReply(arg, "Usage: news.show \"Title\" \"ImageURL or -\" \"Text\" [type] [priority] [author]");
                SendReply(arg, "Types: info, warning, alert, update, event");
                SendReply(arg, "Priority: 1-4 (1=Low, 2=Normal, 3=High, 4=Critical)");
                return;
            }

            string title = arg.GetString(0) ?? "";
            string img = arg.GetString(1);
            if (img == "-") img = string.Empty;
            string text = (arg.GetString(2) ?? "").Replace("\\n", "\n");
            
            // Parse optional parameters
            var typeStr = arg.GetString(3, "info").ToLowerInvariant();
            var priority = arg.GetInt(4, 2);
            var author = arg.GetString(5, "Server Admin");

            if (!Enum.TryParse(typeStr, true, out AnnouncementType type))
                type = AnnouncementType.Info;

            priority = Mathf.Clamp(priority, 1, 4);

            var ann = new Announcement
            {
                Title = title,
                ImageUrl = img,
                Text = text,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Author = author,
                Type = type,
                Priority = priority
            };

            announcements.Add(ann);
            SaveAnnouncements();

            foreach (var player in BasePlayer.activePlayerList)
                ShowPopupAnnouncement(player, ann);
                
            SendReply(arg, $"Announcement '{title}' sent to {BasePlayer.activePlayerList.Count} players.");
        }

        [ChatCommand("news")]
        private void CmdNewsHistory(BasePlayer player, string cmd, string[] args)
        {
            if (announcements.Count == 0)
            {
                SendReply(player, "No announcements available.");
                return;
            }
            ShowHistory(player, 0);
        }

        [ConsoleCommand("news.page")]
        private void CmdNewsPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            int page = arg.GetInt(0, 0);
            ShowHistory(player, page);
        }

        [ConsoleCommand("news.view")]
        private void CmdNewsView(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            int index = arg.GetInt(0, -1);
            if (index < 0 || index >= announcements.Count) return;
            ShowPopupAnnouncement(player, announcements[index]);
        }

        [ConsoleCommand("news.clear")]
        private void CmdNewsClear(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleOrAdmin(arg)) return;
            announcements.Clear();
            SaveAnnouncements();
            SendReply(arg, "All announcements have been cleared.");
        }

        [ConsoleCommand("news.close")]
        private void CmdNewsClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            CloseAllUI(player);
        }

        [ConsoleCommand("news.config")]
        private void CmdNewsConfig(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleOrAdmin(arg)) return;
            
            if (arg.Args == null || arg.Args.Length < 2)
            {
                SendReply(arg, "Usage: news.config <setting> <value>");
                SendReply(arg, "Examples:");
                SendReply(arg, "news.config textscale 1.2");
                SendReply(arg, "news.config fontsize.title 20");
                SendReply(arg, "news.config position TopCenter");
                return;
            }

            string setting = arg.GetString(0).ToLowerInvariant();
            string value = arg.GetString(1);

            bool changed = false;

            switch (setting)
            {
                case "textscale":
                case "globalscale":
                    if (float.TryParse(value, out float scale))
                    {
                        config.TextScaling.GlobalTextScale = Mathf.Clamp(scale, 0.5f, 3.0f);
                        changed = true;
                    }
                    break;
                    
                case "fontsize.title":
                    if (int.TryParse(value, out int titleSize))
                    {
                        config.TextScaling.FontSizes.BaseTitleSize = Mathf.Clamp(titleSize, 10, 50);
                        changed = true;
                    }
                    break;
                    
                case "position":
                    if (config.UI.Position.Presets.ContainsKey(value))
                    {
                        config.UI.Position.Preset = value;
                        changed = true;
                    }
                    break;
                    
                case "popupsize":
                    if (Enum.TryParse<PopupSizePresets>(value, true, out var size))
                    {
                        config.UI.Layout.PopupSize = size;
                        changed = true;
                    }
                    break;
            }

            if (changed)
            {
                SaveConfig();
                SendReply(arg, $"Setting '{setting}' changed to '{value}'. Changes will apply to new announcements.");
            }
            else
            {
                SendReply(arg, $"Invalid setting or value. Setting: {setting}, Value: {value}");
            }
        }
        #endregion

        #region Enhanced Modern Popup UI
        private void ShowPopupAnnouncement(BasePlayer player, Announcement ann)
        {
            CloseAllUI(player);

            var colors = config.UI.Colors;
            var elements = new CuiElementContainer();

            // Play sound effect
            if (config.UI.Behavior.EnableSounds)
            {
                string soundToPlay = ann.Priority >= 4 ? config.Sounds.AlertSound : config.Sounds.PopupSound;
                Effect.server.Run(soundToPlay, player.transform.position);
            }

            // Backdrop with blur effect
            elements.Add(new CuiPanel
            {
                Image = { Color = GetAccessibleBackgroundColor(colors.Panels.Backdrop) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", BackdropName);

            // Get dynamic positioning based on config
            var anchors = GetPositionAnchors("popup").Split('|');
            string popupAnchorMin = anchors[0];
            string popupAnchorMax = anchors[1];

            // Modern popup container with scaled positioning
            string popupContainer = CuiHelper.GetGuid();
            elements.Add(new CuiPanel
            {
                Image = { Color = GetAccessibleBackgroundColor(colors.Panels.PopupPanel) },
                RectTransform = { AnchorMin = popupAnchorMin, AnchorMax = popupAnchorMax },
                CursorEnabled = true
            }, BackdropName, popupContainer);

            // Elegant border effect
            elements.Add(new CuiPanel
            {
                Image = { Color = colors.Panels.PopupBorder },
                RectTransform = { AnchorMin = "-0.002 -0.003", AnchorMax = "1.002 1.003" }
            }, popupContainer, CuiHelper.GetGuid());

            // Priority indicator bar (left side) - only show if enabled
            if (config.UI.Layout.ShowPriorityIndicators)
            {
                string priorityColor = GetPriorityColor(ann.Type, ann.Priority);
                elements.Add(new CuiPanel
                {
                    Image = { Color = priorityColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.008 1" }
                }, popupContainer, CuiHelper.GetGuid());
            }

            // Header section with gradient
            string headerSection = CuiHelper.GetGuid();
            elements.Add(new CuiPanel
            {
                Image = { Color = GetAccessibleBackgroundColor(colors.Panels.HeaderGradient) },
                RectTransform = { AnchorMin = "0.008 0.82", AnchorMax = "1 1" }
            }, popupContainer, headerSection);

            // Type badge - only show if enabled
            if (config.UI.Layout.ShowTypeIcons)
            {
                string priorityColor = GetPriorityColor(ann.Type, ann.Priority);
                string typeBadge = CuiHelper.GetGuid();
                elements.Add(new CuiPanel
                {
                    Image = { Color = priorityColor },
                    RectTransform = { AnchorMin = "0.02 0.15", AnchorMax = "0.25 0.85" }
                }, headerSection, typeBadge);

                elements.Add(new CuiLabel
                {
                    Text = { 
                        Text = ann.Type.ToString().ToUpper(), 
                        FontSize = GetScaledFontSize(FontType.Small, player), 
                        Align = TextAnchor.MiddleCenter, 
                        Color = GetAccessibleTextColor(colors.Text.TitleText),
                        Font = GetScaledFont(FontStyle.Bold)
                    },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, typeBadge, CuiHelper.GetGuid());
            }

            // Title with server branding
            string serverName = !string.IsNullOrEmpty(config.UI.Branding.ServerName) ? config.UI.Branding.ServerName : "Server News";
            float titleStartX = config.UI.Layout.ShowTypeIcons ? 0.28f : 0.05f;
            
            elements.Add(new CuiLabel
            {
                Text = { 
                    Text = $"<color=#FFA500>{serverName}</color> • {SafeTrim(ann.Title, GetDynamicTextLength(35))}", 
                    FontSize = GetScaledFontSize(FontType.Title, player), 
                    Align = TextAnchor.MiddleLeft, 
                    Color = GetAccessibleTextColor(colors.Text.TitleText),
                    Font = GetScaledFont(FontStyle.Bold)
                },
                RectTransform = { AnchorMin = $"{titleStartX} 0", AnchorMax = "0.92 1" }
            }, headerSection, CuiHelper.GetGuid());

            // Close button (X) in top right
            elements.Add(new CuiButton
            {
                Button = { Color = colors.Buttons.CloseButton, Command = "news.close" },
                Text = { 
                    Text = "✕", 
                    FontSize = GetScaledFontSize(FontType.Header, player), 
                    Align = TextAnchor.MiddleCenter, 
                    Color = GetAccessibleTextColor(colors.Buttons.ButtonText),
                    Font = GetScaledFont(FontStyle.Bold)
                },
                RectTransform = { AnchorMin = "0.92 0.1", AnchorMax = "0.98 0.9" }
            }, headerSection, CuiHelper.GetGuid());

            // Content area with configurable padding
            string contentArea = CuiHelper.GetGuid();
            float padding = config.UI.Layout.ContentPadding;
            elements.Add(new CuiPanel
            {
                Image = { Color = GetAccessibleBackgroundColor(colors.Panels.ContentPanel) },
                RectTransform = { AnchorMin = $"{padding} 0.15", AnchorMax = $"{1-padding} 0.8" }
            }, popupContainer, contentArea);

            // Image handling (if provided and enabled)
            bool hasImage = !string.IsNullOrEmpty(ann.ImageUrl) && config.UI.Layout.ShowImagePreviews;
            if (hasImage)
            {
                elements.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = contentArea,
                    Components =
                    {
                        new CuiRawImageComponent { Url = ann.ImageUrl },
                        new CuiRectTransformComponent { AnchorMin = "0.03 0.45", AnchorMax = "0.45 0.95" }
                    }
                });
            }

            // Main text content with advanced text scaling
            float textStartX = hasImage ? 0.48f : 0.05f;
            int textWidth = GetDynamicTextLength(hasImage ? 25 : 35);
            
            elements.Add(new CuiLabel
            {
                Text = { 
                    Text = WrapText(ann.Text ?? "No content provided.", textWidth), 
                    FontSize = GetScaledFontSize(FontType.Paragraph, player), 
                    Align = TextAnchor.UpperLeft, 
                    Color = GetAccessibleTextColor(colors.Text.BodyText),
                    Font = GetScaledFont(FontStyle.Regular)
                },
                RectTransform = { AnchorMin = $"{textStartX} 0.1", AnchorMax = "0.95 0.95" }
            }, contentArea, CuiHelper.GetGuid());

            // Bottom info bar
            string bottomBar = CuiHelper.GetGuid();
            elements.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.05 0.05 0.8" },
                RectTransform = { AnchorMin = $"{padding} 0.02", AnchorMax = $"{1-padding} 0.13" }
            }, popupContainer, bottomBar);

            // Author info
            elements.Add(new CuiLabel
            {
                Text = { 
                    Text = $"By: {ann.Author ?? "Server Admin"}", 
                    FontSize = GetScaledFontSize(FontType.Small, player), 
                    Align = TextAnchor.MiddleLeft, 
                    Color = GetAccessibleTextColor(colors.Text.MutedText),
                    Font = GetScaledFont(FontStyle.Regular)
                },
                RectTransform = { AnchorMin = "0.03 0", AnchorMax = "0.4 1" }
            }, bottomBar, CuiHelper.GetGuid());

            // Date stamp
            elements.Add(new CuiLabel
            {
                Text = { 
                    Text = ann.Date ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm"), 
                    FontSize = GetScaledFontSize(FontType.Small, player), 
                    Align = TextAnchor.MiddleCenter, 
                    Color = GetAccessibleTextColor(colors.Text.MutedText),
                    Font = GetScaledFont(FontStyle.Regular)
                },
                RectTransform = { AnchorMin = "0.4 0", AnchorMax = "0.7 1" }
            }, bottomBar, CuiHelper.GetGuid());

            // History button
            elements.Add(new CuiButton
            {
                Button = { Color = colors.Buttons.SecondaryButton, Command = "news.page 0" },
                Text = { 
                    Text = "History", 
                    FontSize = GetScaledFontSize(FontType.Small, player), 
                    Align = TextAnchor.MiddleCenter, 
                    Color = GetAccessibleTextColor(colors.Buttons.ButtonText),
                    Font = GetScaledFont(FontStyle.Regular)
                },
                RectTransform = { AnchorMin = "0.75 0.15", AnchorMax = "0.97 0.85" }
            }, bottomBar, CuiHelper.GetGuid());

            // Auto-close timer display
            if (config.UI.Behavior.ShowCloseTimer && config.UI.Behavior.AutoCloseEnabled)
            {
                StartAutoCloseTimer(player, config.UI.Behavior.AutoCloseSeconds);
            }

            CuiHelper.AddUi(player, elements);
            TrackUI(player, BackdropName);
            TrackUI(player, popupContainer);

            // Auto-close functionality
            if (config.UI.Behavior.AutoCloseEnabled)
            {
                if (autoCloseTimers.ContainsKey(player.userID))
                    autoCloseTimers[player.userID]?.Destroy();

                autoCloseTimers[player.userID] = timer.Once(config.UI.Behavior.AutoCloseSeconds, () =>
                {
                    if (player?.IsConnected == true) 
                    {
                        CloseAllUI(player);
                        if (config.UI.Behavior.EnableSounds)
                            Effect.server.Run(config.Sounds.CloseSound, player.transform.position);
                    }
                });
            }
        }

        private void StartAutoCloseTimer(BasePlayer player, int seconds)
        {
            // Enhanced timer with better scaling
            var timerElement = new CuiElementContainer();
            
            timerElement.Add(new CuiLabel
            {
                Text = { 
                    Text = $"Auto-close in {seconds}s", 
                    FontSize = GetScaledFontSize(FontType.Tiny, player), 
                    Align = TextAnchor.MiddleCenter, 
                    Color = GetAccessibleTextColor("0.7 0.7 0.7 0.8"),
                    Font = GetScaledFont(FontStyle.Regular)
                },
                RectTransform = { AnchorMin = "0.75 0.85", AnchorMax = "0.98 0.9" }
            }, BackdropName, "TimerDisplay");

            CuiHelper.AddUi(player, timerElement);
        }

        private string GetPriorityColor(AnnouncementType type, int priority)
        {
            var colors = config.UI.Colors.Priority;
            
            // Priority overrides type for critical alerts
            if (priority >= 4) return colors.AlertColor; // Critical
            if (priority >= 3) return colors.WarningColor; // High
            
            // Use type-based colors for normal/low priority
            return type switch
            {
                AnnouncementType.Alert => colors.AlertColor,
                AnnouncementType.Warning => colors.WarningColor,
                AnnouncementType.Event => colors.EventColor,
                AnnouncementType.Update => colors.UpdateColor,
                _ => colors.InfoColor
            };
        }

        private void ShowHistory(BasePlayer player, int page)
        {
            if (!HasUI(player, BackdropName))
            {
                var backdropElements = new CuiElementContainer();
                backdropElements.Add(new CuiPanel
                {
                    Image = { Color = GetAccessibleBackgroundColor(config.UI.Colors.Panels.Backdrop) },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    CursorEnabled = true
                }, "Overlay", BackdropName);
                CuiHelper.AddUi(player, backdropElements);
                TrackUI(player, BackdropName);
            }

            CuiHelper.DestroyUi(player, HistoryPanel);
            UntrackUI(player, HistoryPanel);

            var colors = config.UI.Colors;
            var elements = new CuiElementContainer();

            // History panel with dynamic sizing
            var historyAnchors = GetPositionAnchors("history").Split('|');
            if (historyAnchors.Length < 2)
                historyAnchors = new[] { "0.15 0.1", "0.85 0.9" };

            elements.Add(new CuiPanel
            {
                Image = { Color = GetAccessibleBackgroundColor(colors.Panels.HistoryPanel) },
                RectTransform = { AnchorMin = historyAnchors[0], AnchorMax = historyAnchors[1] },
                CursorEnabled = true
            }, BackdropName, HistoryPanel);

            // Header with close button
            string headerPanel = CuiHelper.GetGuid();
            elements.Add(new CuiPanel
            {
                Image = { Color = GetAccessibleBackgroundColor(colors.Panels.HeaderGradient) },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" }
            }, HistoryPanel, headerPanel);

            // Logo in header (if enabled and provided)
            if (config.UI.Branding.ShowServerLogo && !string.IsNullOrEmpty(config.UI.Branding.LogoUrl))
            {
                var logoPos = GetLogoPosition();
                elements.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = headerPanel,
                    Components =
                    {
                        new CuiRawImageComponent { Url = config.UI.Branding.LogoUrl },
                        new CuiRectTransformComponent { AnchorMin = logoPos.min, AnchorMax = logoPos.max }
                    }
                });
            }

            // Pagination setup
            int perPage = config.General.AnnouncementsPerPage;
            var ordered = announcements.OrderByDescending(a => a.Date).ToList();
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(ordered.Count / (float)perPage));
            page = Mathf.Clamp(page, 0, totalPages - 1);

            // Header title with improved scaling
            elements.Add(new CuiLabel
            {
                Text = { 
                    Text = $"News Archive - Page {page + 1} of {totalPages} ({ordered.Count} total)", 
                    FontSize = GetScaledFontSize(FontType.Title, player), 
                    Align = TextAnchor.MiddleLeft, 
                    Color = GetAccessibleTextColor(colors.Text.TitleText),
                    Font = GetScaledFont(FontStyle.Bold)
                },
                RectTransform = { AnchorMin = "0.15 0", AnchorMax = "0.85 1" }
            }, headerPanel, CuiHelper.GetGuid());

            // Close button
            elements.Add(new CuiButton
            {
                Button = { Color = colors.Buttons.CloseButton, Command = "news.close" },
                Text = { 
                    Text = "✕", 
                    FontSize = GetScaledFontSize(FontType.Header, player), 
                    Align = TextAnchor.MiddleCenter, 
                    Color = GetAccessibleTextColor(colors.Buttons.ButtonText),
                    Font = GetScaledFont(FontStyle.Bold)
                },
                RectTransform = { AnchorMin = "0.92 0.1", AnchorMax = "0.98 0.9" }
            }, headerPanel, CuiHelper.GetGuid());

            // Content area
            string contentArea = CuiHelper.GetGuid();
            float padding = config.UI.Layout.ContentPadding;
            elements.Add(new CuiPanel
            {
                Image = { Color = GetAccessibleBackgroundColor(colors.Panels.ContentPanel) },
                RectTransform = { AnchorMin = $"{padding} 0.12", AnchorMax = $"{1-padding} 0.9" }
            }, HistoryPanel, contentArea);

            // News cards with improved scaling
            int start = page * perPage;
            int end = Mathf.Min(start + perPage, ordered.Count);
            float cardHeight = 0.22f;
            float cardSpacing = config.UI.Layout.ElementSpacing;
            float startY = 0.96f;

            for (int i = start; i < end; i++)
            {
                var ann = ordered[i];
                int realIndex = announcements.IndexOf(ann);
                float cardY = startY - (i - start) * (cardHeight + cardSpacing);
                
                CreateNewsCard(elements, contentArea, ann, realIndex, colors, player, 
                    0f, cardY - cardHeight, 1f, cardY);
            }

            // Navigation buttons with better styling
            string buttonArea = CuiHelper.GetGuid();
            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = $"{padding} 0.02", AnchorMax = $"{1-padding} 0.1" }
            }, HistoryPanel, buttonArea);

            // Previous button
            if (page > 0)
            {
                AddStyledButton(elements, buttonArea, colors.Buttons.SecondaryButton, colors.Buttons.ButtonText, 
                    "◀ Previous", $"news.page {page - 1}", GetScaledFontSize(FontType.Header, player), 
                    0f, 0.2f, 0.25f, 0.8f, player);
            }

            // Page info
            elements.Add(new CuiLabel
            {
                Text = { 
                    Text = $"{start + 1}-{end} of {ordered.Count}", 
                    FontSize = GetScaledFontSize(FontType.Small, player), 
                    Align = TextAnchor.MiddleCenter, 
                    Color = GetAccessibleTextColor(colors.Text.MutedText),
                    Font = GetScaledFont(FontStyle.Regular)
                },
                RectTransform = { AnchorMin = "0.35 0.2", AnchorMax = "0.65 0.8" }
            }, buttonArea, CuiHelper.GetGuid());

            // Next button
            if (page < totalPages - 1)
            {
                AddStyledButton(elements, buttonArea, colors.Buttons.SecondaryButton, colors.Buttons.ButtonText, 
                    "Next ▶", $"news.page {page + 1}", GetScaledFontSize(FontType.Header, player), 
                    0.75f, 0.2f, 1f, 0.8f, player);
            }

            CuiHelper.AddUi(player, elements);
            TrackUI(player, HistoryPanel);
        }

        private void CreateNewsCard(CuiElementContainer elements, string parent, Announcement ann, int index, 
            ColorSettings colors, BasePlayer player, float minX, float minY, float maxX, float maxY)
        {
            string card = CuiHelper.GetGuid();
            
            // Card background with accessibility support
            elements.Add(new CuiPanel
            {
                Image = { Color = GetAccessibleBackgroundColor(colors.Panels.CardPanel) },
                RectTransform = { AnchorMin = $"{minX} {minY}", AnchorMax = $"{maxX} {maxY}" }
            }, parent, card);

            // Priority indicator and type badge
            if (config.UI.Layout.ShowPriorityIndicators)
            {
                string priorityColor = GetPriorityColor(ann.Type, ann.Priority);
                elements.Add(new CuiPanel
                {
                    Image = { Color = priorityColor },
                    RectTransform = { AnchorMin = "0 0.85", AnchorMax = "1 1" }
                }, card, CuiHelper.GetGuid());

                // Type badge in corner
                if (config.UI.Layout.ShowTypeIcons)
                {
                    string typeBadge = CuiHelper.GetGuid();
                    elements.Add(new CuiPanel
                    {
                        Image = { Color = priorityColor },
                        RectTransform = { AnchorMin = "0.02 0.65", AnchorMax = "0.18 0.83" }
                    }, card, typeBadge);

                    elements.Add(new CuiLabel
                    {
                        Text = { 
                            Text = ann.Type.ToString().ToUpper(), 
                            FontSize = GetScaledFontSize(FontType.Tiny, player), 
                            Align = TextAnchor.MiddleCenter, 
                            Color = GetAccessibleTextColor(colors.Text.TitleText),
                            Font = GetScaledFont(FontStyle.Bold)
                        },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }, typeBadge, CuiHelper.GetGuid());
                }
            }

            // Title with priority indicator
            string priorityIcon = ann.Priority >= 4 ? "🚨 " : ann.Priority >= 3 ? "⚠️ " : "";
            float titleStartX = config.UI.Layout.ShowTypeIcons ? 0.22f : 0.03f;
            
            elements.Add(new CuiLabel
            {
                Text = { 
                    Text = $"{priorityIcon}{SafeTrim(ann.Title ?? "Untitled", GetDynamicTextLength(35))}", 
                    FontSize = GetScaledFontSize(FontType.Header, player), 
                    Align = TextAnchor.UpperLeft, 
                    Color = GetAccessibleTextColor(colors.Text.AccentText),
                    Font = GetScaledFont(FontStyle.Bold)
                },
                RectTransform = { AnchorMin = $"{titleStartX} 0.55", AnchorMax = "0.97 0.83" }
            }, card, CuiHelper.GetGuid());

            // Preview text with proper line wrapping
            string preview = MakePreview(ann.Text ?? "", GetDynamicTextLength(85));
            elements.Add(new CuiLabel
            {
                Text = { 
                    Text = preview, 
                    FontSize = GetScaledFontSize(FontType.Paragraph, player), 
                    Align = TextAnchor.UpperLeft, 
                    Color = GetAccessibleTextColor(colors.Text.BodyText),
                    Font = GetScaledFont(FontStyle.Regular)
                },
                RectTransform = { AnchorMin = "0.03 0.25", AnchorMax = "0.67 0.53" }
            }, card, CuiHelper.GetGuid());

            // Author and date info
            elements.Add(new CuiLabel
            {
                Text = { 
                    Text = $"By {ann.Author ?? "Admin"} • {ann.Date ?? ""}", 
                    FontSize = GetScaledFontSize(FontType.Small, player), 
                    Align = TextAnchor.LowerLeft, 
                    Color = GetAccessibleTextColor(colors.Text.MutedText),
                    Font = GetScaledFont(FontStyle.Regular)
                },
                RectTransform = { AnchorMin = "0.03 0.05", AnchorMax = "0.6 0.23" }
            }, card, CuiHelper.GetGuid());

            // View button with modern styling
            AddStyledButton(elements, card, colors.Buttons.PrimaryButton, colors.Buttons.ButtonText, 
                "Read Full", $"news.view {index}", GetScaledFontSize(FontType.Small, player), 
                0.72f, 0.1f, 0.97f, 0.4f, player);
        }

        private void AddStyledButton(CuiElementContainer elements, string parent, string bgColor, string textColor, 
            string text, string command, int fontSize, float minX, float minY, float maxX, float maxY, BasePlayer player = null)
        {
            elements.Add(new CuiButton
            {
                Button = { Color = bgColor, Command = command },
                Text = { 
                    Text = text, 
                    FontSize = fontSize, 
                    Align = TextAnchor.MiddleCenter, 
                    Color = GetAccessibleTextColor(textColor),
                    Font = GetScaledFont(FontStyle.Regular)
                },
                RectTransform = { AnchorMin = $"{minX} {minY}", AnchorMax = $"{maxX} {maxY}" }
            }, parent, CuiHelper.GetGuid());
        }
        #endregion

        #region Enhanced Helper Methods
        private string GetAccessibleBackgroundColor(string defaultColor)
        {
            if (!config.UI.Colors.Accessibility.UseHighContrast)
                return defaultColor;
                
            return config.UI.Colors.Accessibility.HighContrastBackground;
        }

        private (string min, string max) GetLogoPosition()
        {
            return config.UI.Branding.LogoPosition switch
            {
                LogoPosition.HeaderLeft => ("0.02 0.1", "0.12 0.9"),
                LogoPosition.HeaderCenter => ("0.45 0.1", "0.55 0.9"),
                LogoPosition.HeaderRight => ("0.88 0.1", "0.98 0.9"),
                LogoPosition.BottomLeft => ("0.02 0.02", "0.12 0.08"),
                LogoPosition.BottomRight => ("0.88 0.02", "0.98 0.08"),
                _ => ("0.02 0.1", "0.12 0.9")
            };
        }

        private int GetDynamicTextLength(int baseLength)
        {
            // Adjust text length based on scaling and accessibility settings
            float scale = config.TextScaling.GlobalTextScale;
            int maxWidth = config.TextScaling.Accessibility.MaxTextWidth;
            
            if (scale > 1.2f)
                baseLength = Mathf.RoundToInt(baseLength * 0.8f); // Reduce for larger text
            
            return Mathf.Min(baseLength, maxWidth);
        }

        private bool IsConsoleOrAdmin(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null) return true;
            var player = arg.Connection.player as BasePlayer;
            return player?.IsAdmin == true;
        }

        private static string SafeTrim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 3) + "...";
        }

        private static string MakePreview(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "").Replace("\n", " ");
            if (s.Length <= maxChars) return s;
            return s.Substring(0, maxChars - 3) + "...";
        }

        private string WrapText(string text, int lineLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            
            // Apply line spacing if accessibility is enabled
            float lineSpacing = config.TextScaling.Accessibility.LineSpacing;
            string lineBreak = lineSpacing > 1.0f ? "\n\n" : "\n";
            
            var words = text.Split(' ');
            var result = new System.Text.StringBuilder();
            var currentLine = new System.Text.StringBuilder();
            
            foreach (var word in words)
            {
                if (currentLine.Length + word.Length + 1 > lineLength)
                {
                    if (currentLine.Length > 0)
                    {
                        result.Append(currentLine.ToString().TrimEnd());
                        result.Append(lineBreak);
                        currentLine.Clear();
                    }
                }
                
                if (currentLine.Length > 0)
                    currentLine.Append(" ");
                currentLine.Append(word);
            }
            
            if (currentLine.Length > 0)
                result.Append(currentLine.ToString());
                
            return result.ToString();
        }

        private void TrackUI(BasePlayer player, string uiName)
        {
            if (!playerUIs.ContainsKey(player.userID))
                playerUIs[player.userID] = new HashSet<string>();
            playerUIs[player.userID].Add(uiName);
        }

        private void UntrackUI(BasePlayer player, string uiName)
        {
            if (playerUIs.ContainsKey(player.userID))
                playerUIs[player.userID].Remove(uiName);
        }

        private bool HasUI(BasePlayer player, string name)
        {
            return playerUIs.ContainsKey(player.userID) && playerUIs[player.userID].Contains(name);
        }

        private void CloseAllUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, PanelName);
            CuiHelper.DestroyUi(player, HistoryPanel);
            CuiHelper.DestroyUi(player, BackdropName);
            CuiHelper.DestroyUi(player, "TimerDisplay");
            
            if (playerUIs.ContainsKey(player.userID))
                playerUIs[player.userID].Clear();

            if (autoCloseTimers.ContainsKey(player.userID))
            {
                autoCloseTimers[player.userID]?.Destroy();
                autoCloseTimers.Remove(player.userID);
            }
        }

        // Debug helper for development
        private void DebugLog(string message)
        {
            if (config.General.EnableDebugMode)
                PrintWarning($"[NewsBroadcaster Debug] {message}");
        }

        // Performance optimization helper
        private void OptimizeForPerformance()
        {
            if (!config.Advanced.EnablePerformanceMode) return;
            
            // Disable animations and effects for better performance
            config.UI.Behavior.EnableAnimations = false;
            config.Animations.EnableTransitions = false;
            
            // Reduce UI update rate
            config.Advanced.UIUpdateRate = Mathf.Min(config.Advanced.UIUpdateRate, 15);
            
            DebugLog("Performance mode enabled - some visual features disabled");
        }

        // Accessibility helper for color vision deficiency
        private string GetColorBlindFriendlyColor(string originalColor, AnnouncementType type)
        {
            if (!config.TextScaling.Accessibility.UseAccessibleColors) 
                return originalColor;

            // Return high-contrast, color-blind friendly alternatives
            return type switch
            {
                AnnouncementType.Alert => "0.8 0.1 0.1 1",      // Dark red
                AnnouncementType.Warning => "0.9 0.6 0.1 1",    // Orange
                AnnouncementType.Info => "0.1 0.4 0.8 1",       // Blue
                AnnouncementType.Event => "0.5 0.1 0.8 1",      // Purple
                AnnouncementType.Update => "0.1 0.6 0.3 1",     // Green
                _ => originalColor
            };
        }
        #endregion

        #region Admin Helper Commands
        [ConsoleCommand("news.reload")]
        private void CmdNewsReload(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleOrAdmin(arg)) return;
            
            LoadConfig();
            LoadAnnouncements();
            
            // Close all open UIs to force refresh with new settings
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (HasUI(player, BackdropName) || HasUI(player, HistoryPanel))
                    CloseAllUI(player);
            }
            
            SendReply(arg, "NewsBroadcaster configuration and data reloaded successfully.");
        }

        [ConsoleCommand("news.test")]
        private void CmdNewsTest(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleOrAdmin(arg)) return;
            
            var testAnn = new Announcement
            {
                Title = "Test Announcement - Text Scaling Demo",
                ImageUrl = "",
                Text = "This is a test announcement to demonstrate the new text scaling features. The text should adapt based on your configuration settings including global scale, responsive sizing, and accessibility options.",
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Author = "System Test",
                Type = AnnouncementType.Info,
                Priority = 2
            };

            var player = arg.Connection?.player as BasePlayer;
            if (player != null)
            {
                ShowPopupAnnouncement(player, testAnn);
                SendReply(arg, "Test announcement sent to your screen.");
            }
            else
            {
                // Send to all players if run from console
                foreach (var p in BasePlayer.activePlayerList)
                    ShowPopupAnnouncement(p, testAnn);
                SendReply(arg, $"Test announcement sent to {BasePlayer.activePlayerList.Count} players.");
            }
        }

        [ConsoleCommand("news.stats")]
        private void CmdNewsStats(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleOrAdmin(arg)) return;
            
            SendReply(arg, "=== NewsBroadcaster Statistics ===");
            SendReply(arg, $"Total Announcements: {announcements.Count}");
            SendReply(arg, $"Max Storage: {config.General.MaxStoredAnnouncements}");
            SendReply(arg, $"Players with open UIs: {playerUIs.Count}");
            SendReply(arg, $"Active auto-close timers: {autoCloseTimers.Count}");
            SendReply(arg, $"Text Scale: {config.TextScaling.GlobalTextScale:F2}");
            SendReply(arg, $"Performance Mode: {config.Advanced.EnablePerformanceMode}");
            SendReply(arg, $"Accessibility Mode: {config.TextScaling.Accessibility.UseAccessibleColors}");
            
            var typeStats = announcements.GroupBy(a => a.Type)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();
            
            if (typeStats.Count > 0)
            {
                SendReply(arg, "Type Distribution: " + string.Join(", ", typeStats));
            }
        }

        [ConsoleCommand("news.export")]
        private void CmdNewsExport(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleOrAdmin(arg)) return;
            
            try
            {
                var exportData = new
                {
                    ExportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ServerName = config.UI.Branding.ServerName,
                    TotalAnnouncements = announcements.Count,
                    Announcements = announcements.Select(a => new
                    {
                        a.Title,
                        a.Text,
                        a.Date,
                        a.Author,
                        Type = a.Type.ToString(),
                        a.Priority
                    }).ToList()
                };
                
                string fileName = $"NewsBroadcaster_Export_{DateTime.Now:yyyyMMdd_HHmmss}";
                Interface.Oxide.DataFileSystem.WriteObject(fileName, exportData);
                
                SendReply(arg, $"Announcements exported to {fileName}.json");
            }
            catch (Exception ex)
            {
                SendReply(arg, $"Export failed: {ex.Message}");
            }
        }
        #endregion

        #region Player Preference System (Optional Enhancement)
        private class PlayerPreferences
        {
            public float TextScale { get; set; } = 1.0f;
            public string PreferredSize { get; set; } = "normal";
            public bool HighContrast { get; set; } = false;
            public bool DisableAnimations { get; set; } = false;
            public bool AutoClose { get; set; } = true;
        }

        private readonly Dictionary<ulong, PlayerPreferences> playerPrefs = new Dictionary<ulong, PlayerPreferences>();

        [ChatCommand("news.settings")]
        private void CmdNewsSettings(BasePlayer player, string cmd, string[] args)
        {
            if (!config.UI.Behavior.RememberPlayerPreferences)
            {
                SendReply(player, "Player preferences are disabled on this server.");
                return;
            }

            if (args.Length == 0)
            {
                ShowPlayerSettings(player);
                return;
            }

            if (args.Length < 2)
            {
                SendReply(player, "Usage: /news.settings <setting> <value>");
                SendReply(player, "Available settings: textscale, size, contrast, animations, autoclose");
                return;
            }

            if (!playerPrefs.ContainsKey(player.userID))
                playerPrefs[player.userID] = new PlayerPreferences();

            var prefs = playerPrefs[player.userID];
            string setting = args[0].ToLowerInvariant();
            string value = args[1].ToLowerInvariant();

            switch (setting)
            {
                case "textscale":
                case "scale":
                    if (float.TryParse(value, out float scale))
                    {
                        prefs.TextScale = Mathf.Clamp(scale, 0.5f, 2.0f);
                        SendReply(player, $"Text scale set to {prefs.TextScale:F1}");
                    }
                    else
                    {
                        SendReply(player, "Invalid scale value. Use 0.5 to 2.0");
                    }
                    break;

                case "size":
                    var validSizes = new[] { "small", "normal", "large", "xlarge" };
                    if (validSizes.Contains(value))
                    {
                        prefs.PreferredSize = value;
                        SendReply(player, $"UI size set to {value}");
                    }
                    else
                    {
                        SendReply(player, "Valid sizes: small, normal, large, xlarge");
                    }
                    break;

                case "contrast":
                    if (bool.TryParse(value, out bool contrast))
                    {
                        prefs.HighContrast = contrast;
                        SendReply(player, $"High contrast {(contrast ? "enabled" : "disabled")}");
                    }
                    break;

                case "animations":
                    if (bool.TryParse(value, out bool animations))
                    {
                        prefs.DisableAnimations = !animations;
                        SendReply(player, $"Animations {(animations ? "enabled" : "disabled")}");
                    }
                    break;

                case "autoclose":
                    if (bool.TryParse(value, out bool autoclose))
                    {
                        prefs.AutoClose = autoclose;
                        SendReply(player, $"Auto-close {(autoclose ? "enabled" : "disabled")}");
                    }
                    break;

                default:
                    SendReply(player, "Unknown setting. Available: textscale, size, contrast, animations, autoclose");
                    return;
            }

            SavePlayerPreferences();
        }

        private void ShowPlayerSettings(BasePlayer player)
        {
            if (!playerPrefs.TryGetValue(player.userID, out var prefs))
                prefs = new PlayerPreferences();

            SendReply(player, "=== Your News Settings ===");
            SendReply(player, $"Text Scale: {prefs.TextScale:F1}");
            SendReply(player, $"UI Size: {prefs.PreferredSize}");
            SendReply(player, $"High Contrast: {prefs.HighContrast}");
            SendReply(player, $"Animations: {!prefs.DisableAnimations}");
            SendReply(player, $"Auto-close: {prefs.AutoClose}");
            SendReply(player, "Use '/news.settings <setting> <value>' to change");
        }

        private void SavePlayerPreferences()
        {
            if (config.UI.Behavior.RememberPlayerPreferences)
                Interface.Oxide.DataFileSystem.WriteObject("NewsBroadcaster_PlayerPrefs", playerPrefs);
        }

        private void LoadPlayerPreferences()
        {
            if (config.UI.Behavior.RememberPlayerPreferences)
            {
                var prefs = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerPreferences>>("NewsBroadcaster_PlayerPrefs");
                if (prefs != null)
                {
                    foreach (var kvp in prefs)
                        playerPrefs[kvp.Key] = kvp.Value;
                }
            }
        }

        // Enhanced scaling that considers player preferences
        private int GetPlayerScaledFontSize(FontType fontType, BasePlayer player)
        {
            float baseScale = GetScaledFontSize(fontType, player);
            
            if (config.UI.Behavior.RememberPlayerPreferences && 
                playerPrefs.TryGetValue(player.userID, out var prefs))
            {
                baseScale *= prefs.TextScale;
            }
            
            return Mathf.RoundToInt(baseScale);
        }
        #endregion
    }
}
