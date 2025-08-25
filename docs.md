# NewsBroadcaster Plugin Documentation

## 📋 Overview

NewsBroadcaster is an advanced Rust plugin that provides modern, customizable popup announcements with sophisticated text scaling, accessibility features, and a sleek user interface. Perfect for server announcements, updates, events, and important notices.

### ✨ Key Features
- **Modern UI Design**: Sleek popups with glassmorphism effects
- **Advanced Text Scaling**: Global scaling, responsive sizing, and accessibility options
- **Multiple Announcement Types**: Info, Warning, Alert, Update, Event
- **Priority System**: 4-level priority system with visual indicators
- **Player Preferences**: Individual customization options
- **Accessibility Support**: High contrast, color-blind friendly options
- **Performance Optimized**: Optional performance mode for lower-end systems

---

## 🚀 Installation

1. Download the `NewsBroadcaster.cs` file
2. Place it in your `oxide/plugins/` directory
3. The plugin will auto-generate configuration files on first load
4. Restart your server or use `oxide.reload NewsBroadcaster`

---

## 🎮 Commands

### Admin Commands (Console)

#### `news.show "Title" "ImageURL" "Text" [type] [priority] [author]`
Creates and broadcasts a new announcement to all online players.

**Parameters:**
- `Title` (required): Announcement title (string)
- `ImageURL` (required): Image URL or "-" for no image (string)
- `Text` (required): Announcement content, use \\n for line breaks (string)
- `type` (optional): info, warning, alert, update, event (default: info)
- `priority` (optional): 1-4 (1=Low, 2=Normal, 3=High, 4=Critical, default: 2)
- `author` (optional): Author name (default: "Server Admin")

**Examples:**
```bash
news.show "Server Restart" "-" "Server will restart in 30 minutes for updates."
news.show "New Event" "https://example.com/event.jpg" "Halloween event is now live!\\nJoin now for exclusive rewards!" event 3 "Event Manager"
news.show "Critical Alert" "-" "Emergency maintenance in progress." alert 4
```

#### `news.clear`
Removes all stored announcements.

#### `news.config <setting> <value>`
Modify plugin settings on-the-fly.

**Available Settings:**
```bash
news.config textscale 1.5           # Global text scaling
news.config fontsize.title 20       # Title font size
news.config position TopCenter       # UI position preset
news.config popupsize Large         # Popup size preset
```

#### `news.reload`
Reloads configuration and announcement data.

#### `news.test`
Sends a test announcement (to command executor or all players if console).

#### `news.stats`
Displays plugin statistics and current settings.

#### `news.export`
Exports all announcements to a JSON file.

### Player Commands (Chat)

#### `/news`
Opens the news history interface showing all past announcements.

#### `/news.settings`
Views current personal settings (if player preferences enabled).

#### `/news.settings <setting> <value>`
Customizes personal news display preferences.

**Available Settings:**
```bash
/news.settings textscale 1.2        # Personal text scaling
/news.settings size large           # UI size preference
/news.settings contrast true        # High contrast mode
/news.settings animations false     # Disable animations
/news.settings autoclose false      # Disable auto-close
```

### Console Commands (Players)

#### `news.close`
Closes any open news interfaces.

#### `news.page <number>`
Navigates to specific page in news history.

#### `news.view <index>`
Views a specific announcement by index.

---

## ⚙️ Configuration

The plugin creates a comprehensive configuration file with multiple sections:

### General Settings
```json
{
  "General": {
    "AnnouncementsPerPage": 4,
    "MaxStoredAnnouncements": 100,
    "EnableDebugMode": false,
    "AllowedCommands": ["news", "announcements", "updates"]
  }
}
```

### Text Scaling Settings
```json
{
  "TextScaling": {
    "GlobalTextScale": 1.0,
    "FontSizes": {
      "BaseTitleSize": 18,
      "BaseHeaderSize": 14,
      "BaseParagraphSize": 12,
      "BaseSmallSize": 10,
      "BaseTinySize": 8,
      "PrimaryFont": "robotocondensed-bold.ttf",
      "SecondaryFont": "robotocondensed-regular.ttf",
      "MonospaceFont": "droidsansmono.ttf"
    },
    "Responsive": {
      "EnableResponsiveText": true,
      "ScreenSizeMultipliers": {
        "small": 0.8,
        "normal": 1.0,
        "large": 1.2,
        "xlarge": 1.4
      },
      "DefaultScreenSize": "normal"
    },
    "Accessibility": {
      "HighContrastMode": false,
      "BoldText": false,
      "LineSpacing": 1.0,
      "MaxTextWidth": 50,
      "UseAccessibleColors": false
    }
  }
}
```

### UI Settings
```json
{
  "UI": {
    "Position": {
      "Preset": "Center",
      "CustomAnchorMin": "0.3 0.3",
      "CustomAnchorMax": "0.7 0.7",
      "Presets": {
        "Center": {"AnchorMin": "0.3 0.3", "AnchorMax": "0.7 0.7"},
        "TopCenter": {"AnchorMin": "0.3 0.7", "AnchorMax": "0.7 0.95"},
        "BottomCenter": {"AnchorMin": "0.3 0.05", "AnchorMax": "0.7 0.3"},
        "LeftSide": {"AnchorMin": "0.05 0.3", "AnchorMax": "0.45 0.7"},
        "RightSide": {"AnchorMin": "0.55 0.3", "AnchorMax": "0.95 0.7"},
        "FullScreen": {"AnchorMin": "0.1 0.1", "AnchorMax": "0.9 0.9"}
      }
    },
    "Layout": {
      "PopupSize": "Medium",
      "ShowImagePreviews": true,
      "ShowTypeIcons": true,
      "ShowPriorityIndicators": true,
      "ContentPadding": 0.02,
      "ElementSpacing": 0.01
    },
    "Behavior": {
      "AutoCloseEnabled": true,
      "AutoCloseSeconds": 15,
      "ShowCloseTimer": true,
      "EnableSounds": true,
      "EnableAnimations": true,
      "ShowOnPlayerConnect": false,
      "RememberPlayerPreferences": false
    }
  }
}
```

### Color Customization
```json
{
  "Colors": {
    "Panels": {
      "Backdrop": "0 0 0 0.6",
      "PopupPanel": "0.1 0.1 0.12 0.95",
      "PopupBorder": "0.95 0.5 0.2 0.8"
    },
    "Text": {
      "TitleText": "1 1 1 1",
      "BodyText": "0.9 0.9 0.9 1",
      "MutedText": "0.7 0.7 0.7 1"
    },
    "Priority": {
      "InfoColor": "0.2 0.7 0.9 1",
      "WarningColor": "0.9 0.7 0.2 1",
      "AlertColor": "0.9 0.3 0.2 1",
      "EventColor": "0.6 0.3 0.9 1",
      "UpdateColor": "0.3 0.9 0.6 1"
    }
  }
}
```

---

## 🎯 Announcement Types & Priorities

### Types
- **Info** (Blue): General information, updates, notices
- **Warning** (Orange): Important warnings, maintenance notices
- **Alert** (Red): Critical alerts, emergency notices
- **Update** (Green): Server updates, patch notes
- **Event** (Purple): Special events, competitions

### Priority Levels
1. **Low (1)**: Regular announcements, minimal visual emphasis
2. **Normal (2)**: Standard announcements, default priority
3. **High (3)**: Important announcements, enhanced visual indicators
4. **Critical (4)**: Emergency announcements, maximum visual impact + alert sound

---

## 🎨 Customization Guide

### Text Scaling Options

#### Global Scaling
```bash
news.config textscale 1.5  # 50% larger text everywhere
news.config textscale 0.8  # 20% smaller text everywhere
```

#### Individual Font Sizes
```bash
news.config fontsize.title 24      # Larger titles
news.config fontsize.paragraph 10  # Smaller body text
```

#### Responsive Scaling
Configure different scales for different UI sizes:
- **Small (0.8x)**: Compact interfaces
- **Normal (1.0x)**: Default scaling
- **Large (1.2x)**: Better readability
- **XLarge (1.4x)**: Accessibility focused

### Position Presets

Choose from predefined positions or create custom ones:

```json
{
  "Position": {
    "Preset": "TopCenter",  // Use predefined preset
    // OR
    "CustomAnchorMin": "0.2 0.8",  // Custom position
    "CustomAnchorMax": "0.8 0.95"
  }
}
```

### Popup Size Presets

- **Compact**: Smaller popups (0.35-0.65 screen space)
- **Medium**: Standard size (0.3-0.7 screen space) 
- **Large**: Larger popups (0.25-0.75 screen space)
- **FullWidth**: Maximum size (0.1-0.9 screen space)

---

## ♿ Accessibility Features

### High Contrast Mode
```json
{
  "Accessibility": {
    "UseHighContrast": true,
    "HighContrastBackground": "0 0 0 1",
    "HighContrastText": "1 1 1 1"
  }
}
```

### Color-Blind Support
```json
{
  "Accessibility": {
    "UseAccessibleColors": true
  }
}
```

### Text Accessibility
```json
{
  "Accessibility": {
    "BoldText": true,        // Use bold fonts
    "LineSpacing": 1.2,      // Increase line spacing
    "MaxTextWidth": 40       // Limit text width for readability
  }
}
```

---

## 🔧 Performance Optimization

### Performance Mode
```json
{
  "Advanced": {
    "EnablePerformanceMode": true,
    "UIUpdateRate": 15,
    "MaxConcurrentPopups": 1
  }
}
```

When enabled, performance mode:
- Disables animations and transitions
- Reduces UI update rate
- Limits concurrent popups
- Optimizes rendering calls

---

## 📊 Data Management

### Storage
- **Announcements**: Stored in `oxide/data/NewsBroadcaster_Data.json`
- **Player Preferences**: Stored in `oxide/data/NewsBroadcaster_PlayerPrefs.json`
- **Exports**: Saved as timestamped JSON files

### Automatic Cleanup
- Announcements are automatically limited to `MaxStoredAnnouncements`
- Oldest announcements are removed when limit is exceeded
- Player UI states are cleaned on disconnect

---

## 🎵 Sound Effects

### Default Sounds
- **Popup Sound**: `assets/bundled/prefabs/fx/notice/item.select.fx.prefab`
- **Close Sound**: `assets/bundled/prefabs/fx/notice/item.deselect.fx.prefab`  
- **Alert Sound**: `assets/bundled/prefabs/fx/explosions/explosion_01.prefab`

### Custom Sounds
Replace with any valid Rust sound asset path:
```json
{
  "Sounds": {
    "PopupSound": "assets/path/to/your/sound.prefab",
    "AlertSound": "assets/path/to/alert/sound.prefab"
  }
}
```

---

## 🐛 Troubleshooting

### Common Issues

#### Text Too Small/Large
```bash
news.config textscale 1.2  # Adjust global scaling
# OR modify individual font sizes in config
```

#### UI Positioning Problems
```bash
news.config position Center    # Reset to center
# OR use custom anchors in config
```

#### Performance Issues
```bash
news.config performancemode true  # Enable performance mode
```

#### Accessibility Issues
```json
{
  "TextScaling": {
    "Accessibility": {
      "HighContrastMode": true,
      "BoldText": true,
      "UseAccessibleColors": true
    }
  }
}
```

### Debug Mode
Enable debug logging to troubleshoot issues:
```json
{
  "General": {
    "EnableDebugMode": true
  }
}
```

---

## 📈 Statistics & Monitoring

Use `news.stats` to monitor:
- Total announcements stored
- Active UI sessions
- Memory usage
- Configuration status
- Type distribution

---

## 🔄 Migration & Updates

### From Previous Versions
The plugin automatically migrates old configurations and data. If issues occur:
1. Backup your data files
2. Delete the old config
3. Restart the plugin to generate new defaults
4. Reconfigure as needed

### Regular Maintenance
- Use `news.clear` periodically to remove old announcements
- Monitor storage limits with `news.stats`
- Export important announcements with `news.export`

---

## 📝 Example Usage Scenarios

### Server Maintenance
```bash
news.show "Scheduled Maintenance" "-" "Server will restart at 3:00 AM UTC for maintenance.\\n\\nExpected downtime: 30 minutes\\nThank you for your patience!" warning 3 "Server Team"
```

### Event Announcement
```bash
news.show "Halloween Event" "https://yourserver.com/halloween.jpg" "🎃 Halloween Event is LIVE!\\n\\n• Zombie hordes spawn every hour\\n• Special Halloween loot\\n• Exclusive skins available\\n\\nEvent runs until November 1st!" event 3 "Event Team"
```

### Critical Alert
```bash
news.show "Security Alert" "-" "Suspicious activity detected. Please secure your bases and report any unusual behavior to admins immediately." alert 4 "Security Team"
```

### Regular Update
```bash
news.show "Weekly Updates" "-" "This week's changes:\\n\\n• Fixed building stability issues\\n• Added new weapons to loot tables\\n• Performance optimizations\\n\\nEnjoy the improvements!" update 2 "Development Team"
```

---

## 🤝 Support & Community

### Getting Help
1. Check this documentation first
2. Enable debug mode to identify issues
3. Use `news.stats` to gather information
4. Check plugin logs for error messages

### Feature Requests
The plugin is designed to be highly customizable. Most features can be achieved through configuration changes or minor modifications.

---

## 📄 License & Credits

NewsBroadcaster Plugin for Rust/Oxide
- Modern UI design with accessibility focus
- Advanced text scaling and responsive design
- Comprehensive customization options

*Version 3.6.0 - Enhanced with advanced text scaling and improved accessibility*
