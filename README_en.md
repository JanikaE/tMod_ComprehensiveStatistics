# Comprehensive Statistics

[中文（主文档）](README.md)

Comprehensive Statistics adds a searchable, filterable, sortable statistics window for Terraria. View character career totals, activity in the current world, and selected world-wide statistics. Supports vanilla and modded content, single-player and multiplayer, with Chinese and English interfaces.

## Current version

| Item | Version |
| --- | --- |
| Comprehensive Statistics | **1.1.0** |
| Target environment | tModLoader 2026.8 Stable · Terraria 1.4.4.9 |
| Optional compatibility | ImproveGame **>= 1.8.2** |

## Features and usage

- Tracks placement and removal of blocks, walls, furniture, wires and actuators, plus item pickups, drops, use, ammunition and trading.
- Tracks interactions, NPC dialogue, reforging, nurse healing, economy, fishing, bait, movement, deaths, kills and life lost.
- Open the window with the statistics button in the inventory, browse categories, search entries and switch character scopes.
- Recording starts after the mod is enabled. Earlier activity and previously missed counts cannot be recovered.

## ImproveGame compatibility

Supports ImproveGame 1.8.2 and later. Future updates are assumed compatible unless a breaking change requires adaptation; runtime signatures and session capabilities still determine which tools are enabled. ImproveGame is optional.

Covered tools include construct, create, space, blast/starburst, tech wall tools and built-in vein mining. Hover over `?` beside the Blocks title to see the capabilities and limits for the current session.

Multiplayer requires this mod on both the client and server with a compatible protocol. Only server-confirmed work is credited. A client construction window can observe up to 8192 cells, and new furniture must be no larger than 32 cells in either dimension. Oversized or unconfirmed work is not estimated.

## Changelog

### 1.1.0 · 2026-10-29

- **Added ImproveGame tool compatibility:** records actual placement, removal, clearing and batch mining through supported tools, including multiplayer ownership, deferred construction and robot work. the Blocks `?` tooltip shows bilingual status and individual tool capabilities.

### 1.0.0

- Initial statistics release: character career totals, current-world slices, world-wide statistics, categorized details, search, filtering, sorting, bilingual UI and persistence.

## Development and maintenance

- [DESIGN.md](DESIGN.md): Current design, counting rules, UI and persistence contracts.
- [MOD_COMPATIBILITY.md](MOD_COMPATIBILITY.md): Tool coverage, implementation entry points, protocol, support boundaries and regression commands.
- [INITIAL_DEVELOPMENT_PLAN.md](INITIAL_DEVELOPMENT_PLAN.md): Archived development plan for the initial release.

The mod version is defined in [build.txt](build.txt). Keep the version and this changelog synchronized for future releases. Changes to counting rules or compatibility scope should also update the design documents and both language descriptions.
