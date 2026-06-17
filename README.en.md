# CuSaveManager V1.1.3 for Casualties: Unknown

> Multi-save, death rollback, and deterministic world mod for Casualties: Unknown
> Compatible with: KrokMP multiplayer, QoL Unknown (their absence does not break core features)

中文说明：见 [README.md](README.md)

## Overview

On top of the vanilla single save, this mod adds file-level multi-save management: copy the current run into independent slots, auto-backup on a timer, roll back to the latest backup after death, and support deterministic world generation (terrain reproduces after rolling back within the same run). It soft-links with QoL Unknown or KrokMP when present, and the mod's core features still work when those mods are absent.

Singleplayer is tested: saving and rollback both work.
Multiplayer is tested on the host side and works.

## Changelog

Full release history lives in [changes.en.md](changes.en.md) (中文：[changes.md](changes.md)). Builds are published at [Releases](https://github.com/huanxin996/Cu-SaveManager/releases).

## How to use

### Install

Place the `CuSaveManager` folder under `BepInEx/plugins/`, containing:

- `CuSaveManager.dll`

After launch, a "Save Manager" button is injected on the main menu; in-game you can open the panel via that button or a hotkey.

### Panel

The panel has four tabs:

| Tab | Purpose |
|------|---------|
| Settings | Auto-backup interval/count, death rollback, world engine and seed, position mode, hotkeys, misc |
| Saves | Browse all slots (grouped by run/date), manual save, load, delete |
| Rollback | View recent slots and roll back now; the death-rollback countdown is also cancelled here |
| About | Version, repo, author, dependencies |

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
| Skip starting supplies on load | Fixes a vanilla bug: stops layer-1 reload/rollback from reissuing emergency light or the full starter kit per `startingsupplies`. Turn off to keep vanilla behaviour |
| UI language | Auto / Chinese / English; auto picks by game Locale keyword |

## Soft dependencies

- **QoL Unknown**: when present, deterministic world can be delegated to it; choosing this mod's engine temporarily disables its world/save involvement.
- **KrokMP**: when present, saves are managed as a multiplayer save (`mp_save`), and rollback uses the host's two-stage reload.
- When either is absent or its field signatures change, standalone save/rollback is unaffected; only "position restore / seed restore / multiplayer broadcast / multiplayer death detection" degrade accordingly.

## Related

- [CasualtiesUnknown-SkinEditor](https://github.com/huanxin996/CasualtiesUnknown-SkinEditor): live preview and animation preview.
- [huanxin996/Cu-Hotbar](https://github.com/huanxin996/Cu-Hotbar): customizable hotbar with item swapping and quick-use.
- [huanxin996/Cu-Stats](https://github.com/huanxin996/Cu-Stats): block / item / combat / movement / kill statistics panel.
