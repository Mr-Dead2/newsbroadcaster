# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

NewsBroadcaster is a single-file **Oxide/uMod plugin for the game Rust** (`NewsBroadcaster.cs`). It lets server admins post rich in-game news announcements (title, image, body, type badge) via a CUI, with player notifications, an archive viewer, rewards, Discord webhooks, and themes. Author is "DEDA"; the plugin is distributed on Codefling.

There is no build system, package manager, or test suite. The repo is the plugin source plus documentation:
- `NewsBroadcaster.cs` — the entire plugin (~2700 lines, one class).
- `PLUGIN_DOCS.md` — end-user/admin reference (commands, permissions, config, API hooks).
- `CODEFLING_DESCRIPTION.md` — marketing copy for the plugin store listing.

## Building / running / testing

There is no local compile step. The plugin is compiled at runtime by the Oxide/uMod framework on a Rust dedicated server. To "test" a change you deploy and run it on a server:
- Drop `NewsBroadcaster.cs` into the server's `oxide/plugins/` directory; Oxide hot-reloads it and reports compile errors to the server console.
- Reload manually with `oxide.reload NewsBroadcaster`.
- Grant permissions to exercise features: `oxide.grant user <SteamID> newsbroadcaster.admin` / `newsbroadcaster.view`.

Because there's no compiler available in this repo, validate C# changes by careful reading and by keeping syntax consistent with the existing Oxide API usage. Do not introduce dependencies beyond what the runtime provides (`Oxide.*`, `UnityEngine`, `Newtonsoft.Json`).

## Code architecture

Everything lives in `public class NewsBroadcaster : RustPlugin` inside `namespace Oxide.Plugins`. The file is organized into `#region` blocks — navigate by region:

- **Fields & Constants** — layer names, permission constants (`PermAdmin`, `PermView`), `DataFile`, regexes, and the many per-player state dictionaries keyed by `ulong` SteamID (open-UI tracking, auto-close timers, active editors, scroll offsets, reward timers, admin multi-select selections).
- **Data Structures** — nested classes: `StoredData` (persisted `Announcement` list + `LastSeenNews`), `Announcement`, `ReadRewardState`, and the config tree (`ConfigData` → `GeneralSettings`, `NotificationSettings`, `DiscordSettings`, `RewardSettings`/`RewardItem`/`RewardBundle`, `UIColors` themes).
- **Localization** — `Msg(...)` keys; all player-facing strings go through the lang system.
- **Configuration** — `LoadConfig` does **manual migration** of older config shapes via `JObject` before deserializing to `ConfigData`, injecting missing default themes and saving if `needsSave`. Preserve this migration path when changing config schema.
- **Data Management** — JSON persistence through `Interface.Oxide.DataFileSystem` (`DataFile` = `NewsBroadcaster_Data`), including a legacy-format read fallback.
- **Commands** — console/RCON commands (`news.show`, `news.trigger`, `news.delete`, `news.list`, `news.admin`) and chat command (`/news`). All admin commands gate on `PermAdmin`/`IsAdmin`.
- **Notification UI / UI Generation / Admin UI / Theme UI** — CUI construction (`CuiElementContainer`, manual anchor math). This is the bulk of the file. UI is built fresh and pushed per player; state needed across rebuilds lives in the per-player dictionaries above.
- **Rewards** — read/like payouts via optional `[PluginReference]` plugins, once per player per announcement (tracked in `Announcement`/`StoredData`).
- **Discord Webhooks** — webhook POST with per-type embed colors.

### Cross-cutting things to know

- **Optional integrations** are referenced via `[PluginReference] private Plugin ImageLibrary, Notify, ServerRewards, Economics;` — always null-check before calling; the plugin must degrade gracefully when they're absent.
- **API hooks** the plugin fires for other plugins (via `Interface.CallHook`): `OnNewsBroadcast`, `OnNewsEdited`, `OnNewsDeleted`, `OnNewsRead`, `OnNewsLiked`. Payloads come from `BuildHookData(ann)`. Keep these stable — they are a documented public contract (see `PLUGIN_DOCS.md`).
- **Per-player cleanup**: timers and UI state dictionaries are keyed by SteamID and must be torn down on `OnPlayerDisconnected`/UI close to avoid leaks. When adding new per-player state, add corresponding cleanup.
- **Version number** appears in the `[Info("NewsBroadcaster", "DEDA", "x.y.z")]` attribute and should be bumped there for releases; note the docs and the attribute can drift, so update both.

## Conventions

- Match the existing C#/Oxide idioms already in the file (regions, manual CUI anchor math, `Msg()` for all user-facing text, defensive null checks around plugin references).
- User-facing strings belong in the Localization region, not inline.
- The CUI uses string color tuples like `"0.10 0.08 0.07 0.97"` (RGBA, space-separated) consistent with Rust's CUI; follow that format for theme colors.
