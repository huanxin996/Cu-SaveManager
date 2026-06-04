# SaveManager V1.0.6 for Casualties: Unknown

> Multi-save, death rollback, and deterministic world mod for Casualties: Unknown
> Compatible with: KrokMP multiplayer, QoL Unknown (their absence does not break core features)

中文说明：见 [README.md](README.md)

## Overview

On top of the vanilla single save, this mod adds file-level multi-save management: copy the current run into independent slots, auto-backup on a timer, roll back to the latest backup after death, and support deterministic world generation (terrain reproduces after rolling back within the same run). It soft-links with QoL Unknown or KrokMP when present, and the mod's core features still work when those mods are absent.

Singleplayer is tested: saving and rollback both work.
Multiplayer is tested on the host side and works.

## What changed in 1.0.6

A systematic pass around the "deterministic world + rollback" core:

- **Pick a world engine**: choose `QoL (preferred)` or `This mod` in the panel. With QoL it defers world determinism to QoL; with this mod its own engine takes over and temporarily disables QoL's world involvement. When QoL is not installed, the `QoL` button is disabled and only this mod's engine is available.
- **Multiplayer support**: a multiplayer save is packed as a slot from KrokMP's whole `mp_save` directory; rollback uses the host's two-stage reload (return to main menu, then auto-reload), and everyone reconnects into the backup point.
- **Unified logging**: all logs go through one sink; enable "Show mod logs in game console" in settings to view them in the console opened with the ` key.
- **Update check**: compares against the latest GitHub release on startup and shows a red notice at the top-left when a new version exists; can be turned off in settings.

## How to use

### Install

Place the `SaveManager` folder under `BepInEx/plugins/`, containing:

- `SaveManager.dll`

After launch, a "Save Manager" button is injected on the main menu; in-game you can open the panel via that button or a hotkey.

### Panel

The panel has three tabs:

| Tab | Purpose |
|------|---------|
| Settings | Auto-backup interval/count, death rollback, world engine and seed, position mode, hotkeys, misc |
| Saves | Browse all slots (grouped by run/date), manual save, load, delete |
| Rollback | View recent slots and roll back now; the death-rollback countdown is also cancelled here |

### Deterministic world

Under Settings → Deterministic World:

- **Engine**: `QoL (preferred)` (greyed out when QoL is absent) or `This mod`.
- **Seed**: blank auto-derives; a number or text uses a manual seed (this mod's engine only).
- **Position mode**: `Last position` (saved coordinates) or `Fixed position` (configured coordinates).

Rolling back within the same run reproduces the terrain; a new layer is generated only when advancing to the next one.

### Death rollback

Once enabled under Settings → Auto Rollback on Death, dying starts a countdown and rolls back to the latest auto/manual backup; cancel it on the Rollback tab during the countdown. In multiplayer, the host triggers it by the configured dead-player threshold.

### Misc

| Option | Description |
|--------|-------------|
| Show mod logs in game console | Mirrors mod logs to the ` console |
| Accept update notifications | Checks GitHub for a new release on startup and notifies in-game |

## Soft dependencies

- **QoL Unknown**: when present, deterministic world can be delegated to it; choosing this mod's engine temporarily disables its world/save involvement.
- **KrokMP**: when present, saves are managed as a multiplayer save (`mp_save`), and rollback uses the host's two-stage reload.
- When either is absent or its field signatures change, standalone save/rollback is unaffected; only "position restore / seed restore / multiplayer broadcast / multiplayer death detection" degrade accordingly.

## Related

- [CasualtiesUnknown-SkinEditor](https://github.com/huanxin996/CasualtiesUnknown-SkinEditor): live preview and animation preview.
- [huanxin996/Cu-Hotbar](https://github.com/huanxin996/Cu-Hotbar): customizable hotbar with item swapping and quick-use.
