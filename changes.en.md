# CuSaveManager Changelog

> 中文更新日志：见 [changes.md](changes.md)

## 1.1.6

**Fixed the layer number not advancing (single-player / KrokMP-installed solo).**

- Root cause: the game advances layers via save→load. `SaveSystem.SaveGame` writes `biome = biomeDepth + 1` (the next layer) and `TryLoadGame` sets `biomeDepth = biome`, so saving at the bottom of a layer and continuing loads the next layer. CuSaveManager's "persist current save for continue" overwrote the `biome` field with the live 0-based `biomeDepth`, erasing the `+1`, so reloading returned to the same layer (the layer number never changed). This affected single-player (and KrokMP installed but not hosting), where the layer transition is a `SaveGame`→`LoadGame` cycle — matching "reach the bottom, choose next layer, still the same layer". It existed since 1.1.3 and disappeared when the mod was removed.
- Fix: when saving at a layer boundary (player at the bottom = advancing), the game's `biome = biomeDepth + 1` is now preserved instead of being normalized back to the current layer; biome normalization only runs for mid-layer saves (resume in place). The redundant biome rewrite on the main-menu Continue path (which has no in-world player position) was removed so it can no longer undo the advance. The same boundary guard was applied to the multiplayer `mp_save` normalization.

## 1.1.5

Determinism hardening for this mod's own fixed-world engine (self): added reseeding at all synchronous execution points inside the worldgen coroutines.

- Why: Harmony prefixes on the coroutine worldgen phases (`IEnumerator`, e.g. `WorldGenerateTerrain`/`WorldPlaceEntities`/`WorldGenerateStructures`) run only once when the coroutine object is created (before the first `yield`). The actual random draws happen on later frames after `yield`, by which time `UnityEngine.Random` may have been disturbed by other systems. So phase-level reseeding alone is unreliable and terrain/entities may not reproduce from the seed.
- Added per-method, just-in-time reseeding of the **synchronous** sub-methods called inside those coroutines: the `FastNoiseLite` constructor (terrain/cave noise, stepped by `_noiseGenStep`), `DistributeEntities` (enemies/traps/crates/corpses, keyed by name hash + params + call index), `PlaceLiquids`, `GenerateLifePods`/`GenerateDropCapsules`/`GenerateCollapsedPods`, and `ApplyLayerModifiers` (coexists with the load/rollback restore patch).
- Counter/seed resets on layer advance / regen / clear: `ContinueRun`, `RegenerateWorld`, `Clear`, plus noise-counter reset at the start of `GenerateWorld`/`WorldGenerateTerrain`.
- Loot determinism: `TraderScript.GenerateInventory` and `Openable.OnUse` (save/restore `Random.state` around them to avoid polluting the global stream).
- **Fixed "stuck layer" with the self engine when advancing layers (multiplayer host)**: on a layer transition the game increments `totalTraveled` in memory before regenerating, but the on-disk save.sv still holds the previous layer's value. `MpWorldSeedInjector` read only the disk value, so every new layer was seeded with the previous layer's `totalTraveled` and regenerated an identical-looking map. It now takes the max of in-memory and disk values (disk for initial load, in-memory for live layer transitions), consistent with `SeededWorldPatcher.LayerSeed`.
- The save/load-exact path (native save + restore patches) is unchanged.

## 1.1.4

Fixes three issues that appeared when running alongside the KrokMP multiplayer mod with no QoL installed:

- **Map mode always defaulted**: after this mod took over `PreRunScript.StartRun`, it dropped the `WorldGeneration.runSettings = <chosen preset>` write that KrokMP's own patch performed, so a host's chosen map mode (Desolate / Unchipped / Custom) was overwritten by the `normal` preset in `WorldGeneration.Awake`. The write is now restored in the takeover.
- **Same seed every restart (map identical to last time)**: with the default `PreferredEngine=qol` and QoL absent, the old logic fell through to this mod's deterministic world engine and force-injected a fixed seed; `EnsureFreshSeed` also returned early when already active, reusing the previous run's seed. The default now hands world generation back to KrokMP (random maps) whenever KrokMP is present; the fixed-world engine is opt-in (pick "This mod" in the panel). `EnsureFreshSeed` now re-rolls the seed for every fresh run.
- **Cannot advance to the next layer**: the deterministic engine recomputed the seed from the save's stale `totalTraveled` when advancing, regenerating a world identical to the current layer, so "walk to the bottom and choose next layer" kept the same layer. Fixed by handing layer progression back to KrokMP per the item above.

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
