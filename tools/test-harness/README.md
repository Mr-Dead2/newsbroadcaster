# NewsBroadcaster test harness

There is no Rust server here, so this folder lets you check the plugin before deploying it:

1. **Strict compile.** `NewsBroadcaster.cs` is compiled the way Oxide sees it: C# 6 and C# 7 language level, .NET Framework 4.8 API surface. It builds against stand-ins (`Stubs/`) that mirror the real signatures of Oxide (`RustPlugin`, timers, lang, permissions, data files, web requests), Rust (`BasePlayer` with the `EncryptedValue<ulong>` user id, items, effects, console args) and Oxide's CUI classes. The unmodified 1.2.0 plugin compiles cleanly against them, which is how the stand-ins were checked.
2. **Functional tests** (`Harness/Tests.cs`). They drive the plugin like a server would: hooks, console/chat commands, button commands, timers, data/config migration, rewards, Discord payloads. A simulated client keeps the live CUI tree per player and flags elements with unknown parents or duplicate names, and translucent rounded-corner pieces (the game blends those differently from panels, so they show as light squares).
3. **Screenshots** (`render/`). CUI snapshots are drawn in Chromium on a 1280x720-unit canvas scaled to 1920x1080, with Roboto Condensed, rich text, scroll views and outlines, to show what players will see.

4. **Animation video** (`render/anim.js`). The harness records one player's UI at points in time (`Harness.dll animation`). The player redraws each video frame applying every element's `fadeIn`/`fadeOut` the way the client does. A cursor shows the clicks, and ffmpeg encodes MP4 + GIF.

```bash
./run.sh            # compile checks + tests
./run.sh previews   # + screenshots in out/png
./run.sh video      # + animation preview in out/anim (needs ffmpeg)
```

Needs the .NET 8 SDK (`apt install dotnet-sdk-8.0`). Screenshots also need Node with Playwright/Chromium and the `fonts-roboto-unhinted` package. To check another copy of the plugin: `dotnet build Plugin/Plugin.csproj -p:PluginFile=/path/to/NewsBroadcaster.cs`.

The stand-ins are not the real game. Always do a final check on a server: `oxide.reload NewsBroadcaster`.
