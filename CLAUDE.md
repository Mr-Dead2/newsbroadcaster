# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

NewsBroadcaster is a single-file **Oxide/uMod plugin for the game Rust** (`NewsBroadcaster.cs`). It lets server admins post rich in-game news announcements (title, image, body, type badge) via a CUI, with player notifications, an archive viewer with unread tracking, polls, likes, rewards, Discord webhooks, and themes. Author is "DEDA"; the plugin is distributed on Codefling.

Version 2.x is built on the author's own 1.2.0 design (the author's "known good" version). The 1.3–1.7 redesigns in git history were dropped; keep the 1.2.0 look and layout unless asked otherwise.

Repo contents:
- `NewsBroadcaster.cs` — the entire plugin (~3400 lines, one class).
- `PLUGIN_DOCS.md` — admin reference (commands, permissions, config, API hooks, what's new).
- `CHANGELOG.md` — release notes (2.0.0 = everything since the author's 1.2.0).
- `CODEFLING_DESCRIPTION.md` — marketing copy for the store listing.
- `previews/` — rendered screenshots of every screen (from the test harness).
- `tools/test-harness/` — compile checks, functional tests and the screenshot renderer (see below).
- `new-joiner-architecture.tour` — CodeTour walkthrough of the code; update line numbers when code moves.

## Building / running / testing

The plugin is compiled at runtime by Oxide on a Rust dedicated server:
- Drop `NewsBroadcaster.cs` into `oxide/plugins/`; Oxide hot-reloads it and prints compile errors to the console.
- Reload manually with `oxide.reload NewsBroadcaster`.
- Grant permissions: `oxide.grant user <SteamID> newsbroadcaster.admin` / `newsbroadcaster.view`.

**Before committing a change, run `tools/test-harness/run.sh`** (needs `dotnet-sdk-8.0`). It:
1. compiles the plugin as C# 6 and C# 7 against the .NET Framework 4.8 API, with stand-ins for Oxide/Rust/Unity/CUI (`Stubs/`, signatures mirror the real APIs; `BasePlayer.userID` is an `EncryptedValue<ulong>` like current Rust),
2. runs the functional suite (`Harness/Tests.cs`, ~185 checks). A simulated client tracks each player's live CUI tree and flags unknown parents or duplicate element names,
3. with `./run.sh previews`, renders every screen to PNG (`render/`, needs Node + Playwright + `fonts-roboto-unhinted`). Copy them to `previews/` when the UI changes.

When adding a feature, add a test in `Tests.cs`. If the plugin uses a new Oxide/Rust API, add it to the stubs with the **real** signature, so a wrong call fails here as it would on a server. The stubs are not the game; a server reload is still the final check.

## Code architecture

Everything lives in `public class NewsBroadcaster : RustPlugin` inside `namespace Oxide.Plugins`, organized into `#region` blocks:

- **Fields & Constants** — layer names (`LayerName`, `NotificationLayer`, `ConfirmLayer`, `PopupPanel`, `LikeButtonName`), permissions, regexes, layout constants, and the per-player state dictionaries keyed by `ulong` SteamID: `autoCloseTimers`, `notificationTimers`, `playersWithUiOpen`, `editors` (`EditorSession` = draft + editing id + notify/discord toggles), `readRewardTimers`, `adminSelections`, `likeCooldowns`, `voteCooldowns`, `openPopups`, `currentScreens`, `toastNames` (which post a popup shows and its `PopupOrigin`), `archivePages`, `remindedPlayers`.
- **Data Structures** — `StoredData` (announcements + `LastSeenNews` + `ReadAllUpTo` mark-all-read markers), `Announcement` (incl. `PollOptions`/`PollVotes`/`PollClosed`), `EditorSession`, `PopupState`/`PopupOrigin` (`Broadcast`, `Direct`, `Archive`, `Preview`), and the config tree (`ConfigData` → `GeneralSettings`, `NotificationSettings`, `DiscordSettings`, `RewardSettings`/`RewardBundle`/`RewardItem`, `UIColors` themes).
- **Localization** — every player-facing string, through `Msg(key, player, args)`. If a key's meaning changes, rename the key: Oxide keeps old values in existing lang files.
- **Helpers** — text normalization (`NormalizeBodyText`, `/n` handling, optional link stripping), `F()`/`Anchor()` invariant float formatting for CUI anchors (never interpolate raw floats into anchors), title auto-sizing (`PickTitleFontSize`), lookup/permission helpers (`CanView`, `CanAdmin`, `IsAdminArg`, `AdminPlayer`), `PlaySound` (sent only to the target player via `EffectNetwork`).
- **Configuration** — `LoadConfig` migrates older shapes through `JObject` (legacy reward arrays, missing themes), validates and clamps, and re-saves when the file is missing newer keys. Keep this migration path when changing the schema.
- **Data Management** — lifecycle hooks (`Init`, `OnServerInitialized`, `OnPluginLoaded`, `OnServerSave`, `OnPlayerSleepEnded`, `Unload`, `OnPlayerDisconnected`). **Two save paths:** `SaveAnnouncements()` writes immediately (admin actions, rewards paid); `MarkDataDirty()` debounces likes/reads/last-seen (15 s, flushed on server save and unload). `LoadAnnouncements` repairs old data and sorts newest-first.
- **Publishing** — `CreateAnnouncement` (insert, save, register image, `OnNewsBroadcast`) and `NotifyPlayers` (toast or popup per player; marks the post as seen for everyone reached).
- **Commands** — console/RCON (`news.show`, `news.list`, `news.delete`, `news.resend`, `news.trigger`, `news.admin`), the `/news` chat command (`latest`, `admin`, `help`), and all UI button commands (`news.view`, `news.like`, `news.admin.*`, `news.editor.*`). Admin UI commands get the player through `AdminPlayer(arg)`.
- **Notification UI / UI Generation / Admin UI / Theme UI** — CUI construction with manual anchor math. Each screen is rebuilt and pushed whole, except the like button (`AddLikeButton`) and the poll block (`AddPollBlock`, laid out by `PollLayout`), which are swapped in place so the reader's scroll position is kept. Shared look ("premium" style): `AddBackdrop` (dim + blur material), `AddPanelShadow` (layered rounded shadow, added before the panel), `AddSurface` (rounded card/button holder; optional `rim` drawn just outside an opaque fill; content goes on top), `AddRoundedFill`, `AddPill`, `AddPrimaryButton` / `AddGhostButton`, `AddCornerMasks` (rounds picture corners by covering them with the background colour), `AddTopSheen`, `AddHairline`, `AddAccentBar`. Filled buttons are reserved for the main action. Use `Blend()`/`Opaque()` to get opaque colours wherever a rim or picture mask needs to match the background exactly.
  **Rounded corners:** `GenerateCornerTextures()` (OnServerInitialized) draws 8 quarter-circle PNGs in code (`BuildCornerPng` + a minimal `EncodePng`, no System.IO) and stores them with `FileStorage.server.StoreByCrc(CommunityEntity.ServerInstance.net.ID, FileStorage.Type.png, bytes)` (the API since the November 2026 Rust update; the old `Store(bytes, type, id)` no longer compiles). Each rounded rect is 3 bands + 4 corner RawImages (~8 elements), so keep rounded elements to things that matter. If storing fails or `RoundedCorners` is off, `Rounded` is false and every helper draws plain rectangles.
  **Animations:** every full-screen view starts with `OpenScreen(player, UiScreen.X, sameScreenFactor, out fresh)` (tracks `currentScreens`, returns 0 for instant redraws) and ends with `ApplyFadeRamp(container, contentStart, first * anim, last * anim)`, which fills in `FadeIn` for every element added after `contentStart` (later elements fade slower → top-to-bottom reveal). Don't hard-code `FadeIn` unless the fade is intentional (like pop, poll bars); the background only fades when `fresh`. Toasts use a unique name per toast (`toastNames`) so `FadeOut` can cross-fade them. Full-screen layers deliberately have no `FadeOut` (a fading layer would share names with the next screen). `tools/test-harness/run.sh video` renders an MP4/GIF of the animations. The editor is not rebuilt on text input, so fields don't lose focus.
- **Rewards** — read/like payouts (items, ServerRewards points with a plain `ulong` id, Economics currency), at most once per player per announcement.
- **Discord Webhooks** — embed per post; only the configured `RoleMention` may ping.

### Cross-cutting things to know

- **Optional integrations** are `[PluginReference] private Plugin ImageLibrary, Notify, ServerRewards, Economics;` — always null-check and `IsLoaded`-check; the plugin must work without them.
- **API hooks** fired via `Interface.CallHook`: `OnNewsBroadcast`, `OnNewsEdited`, `OnNewsDeleted`, `OnNewsRead`, `OnNewsLiked`, `OnNewsPollVote`, with payloads from `BuildHookData(ann)`. These are a documented public contract — don't rename or drop keys.
- **Per-player cleanup**: every SteamID-keyed collection must be cleared in `OnPlayerDisconnected` and `Unload` (the test suite checks this by name; add new collections to that test).
- **`FileStorage` changed in the November 2026 Rust update**: use `StoreByCrc(entityId, type, data)`; `Store(...)` is gone. The stub mirrors this.
- **`ConsoleSystem.Arg.FullString` is a `StringView` on current Rust**, not a string: always write `arg.FullString.ToString()` (never `arg.FullString ?? ...`). The stub mirrors this.
- **Oxide sandbox / compatibility**: stay within C# 6/7 features and .NET Framework 4.x APIs; no `System.IO`, `System.Net`, `System.Reflection` or threading. Only `Oxide.*`, `UnityEngine`, `Newtonsoft.Json` and the game assemblies are available.
- **Version** lives in `[Info("NewsBroadcaster", "DEDA", "x.y.z")]`; bump it and the version in `PLUGIN_DOCS.md` together.

## Conventions

- Match the existing idioms: regions, manual CUI anchor math via `Anchor()`/`F()`, `Msg()` for all user-facing text, defensive null checks around plugin references.
- CUI colours are RGBA strings like `"0.10 0.08 0.07 0.97"`, consistent with Rust's CUI.
- CUI sizing assumes the 1280x720 reference canvas (font sizes and offsets are in those units; the popup is 832x468 units).
