# CuSaveManager Changelog

> 中文更新日志：见 [changes.md](changes.md)

## 1.1.2

- Sidebar of the main panel now has an "About" tab, in the same SkinSync-style centered title + link buttons + name buttons layout, showing version / repo / latest release / author / dependencies.
- Settings → "Misc" section gains a UI language switch (auto / Chinese / English); written to `I18n.PreferredLanguage` in the config file and persisted across restarts. Language detection now uses a three-tier fallback (config → game Locale keyword match → PlayerPrefs).
- Aligned the semi-transparent background with the other sidebar-embedded mods (HwAssistive previously rendered noticeably darker; brought back to the saveManager baseline), so switching sidebar tabs no longer flickers between two shades of black.

## 1.1.1

- Fixed the game re-handing out starting supplies (emergency light, or lantern + dogfood + waterbottle + trashbag) every time you reload or roll back to layer 1. The game's `WorldGeneration.WorldPlacePlayer` has no `loadedRun` guard, so it reissues whatever `runSettings["startingsupplies"]` says on every layer-1 generation. Added a "Skip starting supplies on load" toggle (Settings → Misc, on by default); turn it off to restore vanilla behaviour.

## 1.1.0

- The main panel now scales uniformly to the screen resolution, so the oversized design no longer overflows; margins are kept, dragging works, and hit-testing is corrected for the scale.
- Game difficulty (RunSettings) is restored from the save on load/rollback, fixing custom difficulty reverting to default after reloading in both singleplayer and multiplayer.
- Fixed the top-right X close icon rendering wrong when the panel is scaled.

## 1.0.9

- Fixed `ImGuiImeRecovery` clearing `FocusControl` while external IMGUI (e.g. KrokMP connection form) had keyboard focus, blocking IP/username input; focus is now cleared only when this panel closes via `RequestClear()`.

## 1.0.8

- Fixed hotkeys (this mod and CuHotbar) stopping after Chinese IME input in panel text fields (nickname, seed, etc.). Closing the panel clears the stuck IMGUI keyboard focus.

## 1.0.7

A systematic pass around the "deterministic world + rollback" core:

- **Pick a world engine**: choose `QoL (preferred)` or `This mod` in the panel. With QoL it defers world determinism to QoL; with this mod its own engine takes over and temporarily disables QoL's world involvement. When QoL is not installed, the `QoL` button is disabled and only this mod's engine is available.
- **Multiplayer support**: a multiplayer save is packed as a slot from KrokMP's whole `mp_save` directory; rollback uses the host's two-stage reload (return to main menu, then auto-reload), and everyone reconnects into the backup point.
- **Unified logging**: all logs go through one sink; enable "Show mod logs in game console" in settings to view them in the console opened with the ` key.
- **Update check**: compares against the latest GitHub release on startup and shows a red notice at the top-left when a new version exists; can be turned off in settings.
