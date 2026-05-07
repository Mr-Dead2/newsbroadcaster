# NewsBroadcaster 1.1.0 — Changelog

A big quality-of-life update focused on admin workflow, player engagement and integrations.

## New
- **Pinned announcements** — keep server rules / wipe info / events at the top of the archive. Gold tint in the list, gold "PINNED" chip on the popup, gold frame around the popup.
- **Bulk admin operations** — multi-select rows with checkboxes, then delete / pin / unpin in one click. Selection survives paging.
- **Read & Like rewards** — pay players in items, RP (ServerRewards) and/or currency (Economics) for reading/liking news. Once-per-player per announcement.
- **API hooks** for other plugins: `OnNewsBroadcast`, `OnNewsEdited`, `OnNewsDeleted`, `OnNewsRead`, `OnNewsLiked`.
- **`news.list`** console command — lists every stored announcement with its index, id and pin state.
- **Delete confirmation dialog** in the admin UI — no more misclick disasters.
- **Stable hex GUID** per announcement — UI buttons now act on a stable id instead of a list position, so concurrent edits no longer mis-target rows.

## Fixed
- Notification toast now opens the **correct** announcement (it always opened #0 before).
- Editing an existing announcement no longer re-broadcasts a popup/sound to every online player.
- `LikedPlayers` is now deep-copied on edit (no more shared HashSet between old and edited copies).
- `news.admin.page` permission denial now sends a reply (was silent before).
- Per-player UI state is cleaned up on disconnect (fixes a slow memory leak on high-pop servers).

## Changed
- Theme palette tweaked for better contrast on the default theme.
- Discord/UI dates use an invariant-culture format for consistency across regions.
- Old reward configs (bare item arrays) are migrated automatically into the new `Items / Points / Currency` bundle format on first load.

## Compatibility
- Drop-in upgrade from 1.0.x — config and data files are migrated on first load. No manual steps required.
