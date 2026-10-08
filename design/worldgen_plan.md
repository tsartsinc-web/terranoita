# World generation: Noita inside Terraria's worldgen (plan, 2026-10-08)

Author: "checked many chests, no wands, no spells; worldgen is the big problem". Goal: Noita's content is placed by
Noita's own biome code during Terraria's own world generation, not sprinkled over a finished world.

## 0. Why chests were empty (fix first, PC-8)
- `Entry.Stage` defaults to "1b" (commit 4e17e91, 2026-10-06). The release and the recipe set no TERRANOITA_STAGE, so
  every hook with stage 2/3 is off for players: `world_load` (2) never binds -> `WorldLoot.Load` never runs -> no
  cave wands, no chest spells. Same for all `magic_*` hooks (3). The 0.4.0 zip built from 026b8bb has this too:
  do not upload it as is.
- `WorldLoot.Load` returns silently when `NoitaArt.Ready` is false at world load and never retries; when the
  `.wld.magic` file exists it never fills chests again (new chests, worlds from older versions).
- 0.3.1 (live) has no magic at all.

## 1. Where it hooks in Terraria (PC)
- Terraria builds a world as a list of passes (`WorldGen.GenerateWorld` -> `AddGenerationPass(name, ...)`; names
  like "Buried Chests", "Micro Biomes", "Final Cleanup"). Find the exact API with
  `tncli tr-methods <Terraria.exe> WorldGen "AddGenerationPass|GenerateWorld"` and
  `tncli tr-methods <Terraria.exe> Terraria.WorldBuilding.WorldGenerator`.
- Add our passes by a Harmony postfix that inserts after a named pass (never replace Terraria's):
  - "Terranoita: scenes" after "Micro Biomes" (structures, section 3).
  - "Terranoita: loot" after "Final Cleanup" (wands, flasks, chest spells, section 4; all chests exist by then).
- All randomness from `WorldGen.genRand` (the world seed): same seed -> same Noita content.
- Old worlds (no `.wld.magic`, or its version < current): run only the loot pass once on load (retrofit), write the
  file with a version line. Never touch an old world's tiles.

## 2. What Noita's code gives us (Core, CLOUD)
Noita places content per biome by Lua: `data/scripts/biomes/<biome>.lua` defines spawn tables (`g_small_enemies`,
`g_items`, `g_props`, `g_pixel_scenes_01`, ...) and functions `spawn_items(x,y)`, `spawn_wands`, `spawn_potions`,
`spawn_chest`, `spawn_pixel_scene_*` that the engine calls for coloured pixels of the wang tiles. We run those
scripts like LuaWandMaker does (MoonSharp + LuaCulture + Prelude), with a recording host:
- `NoitaBiomeSpawns` (new, Core): `Load(biomeFile)`, `Call(fn, x, y)` -> a list of `Placement {kind, file, x, y}`
  recorded from `EntityLoad`, `EntityLoadCameraBound`, `LoadPixelScene`, `CreateItemActionEntity`,
  `spawn_from_list`/`SpawnActionItem`, `EntityLoad("data/entities/items/pickup/chest_random.xml")`. No world needed.
- Which functions to call and how often: count the coloured spawn pixels per function in the biome's wang tile
  images (`data/wang_tiles/<biome>.png`, colour -> function from the biome xml `<Materials ...>`/`RegisterSpawnFunction`
  in the lua). Density per Terraria tile area = Noita's count / Noita's biome area (one number per biome, tools
  compute it, into a sheet `biome_spawns.json`: biome, function, per_10k_tiles, `_sources`).
- Chest contents: run Noita's `chest_random.lua` / `chest_random_super.lua` drop functions with the recorder
  (they call `EntityLoad` of wands, potions, spells, gold) -> Terraria chest items.
- Pixel scenes: `LoadPixelScene(materials.png, visual.png, x, y, background, ...)` -> read the PNG with the materials
  colour table (materials sheet `color` -> material -> Terraria tile/liquid by the existing mapping); `visual` gives
  paint/wall where useful. Core returns a tile grid; the game stamps it.
- Cloud check: unit tests with stub scripts (like LuaWandMaker tests); a `tncli biome-spawns <noita> <biome> --count 100`
  on the PC prints the placements histogram (one line per kind).

## 3. Structures pass (PC)
For each Terraria zone with a Noita biome (biome_map.json: Mines -> underground_dirt, Coal Pits -> cavern, ...):
- pick N spots by density (section 2) in that zone, only where the scene fits (bounding box mostly solid ground for
  buried scenes, mostly air for cave scenes; skip chests, dungeon, temple, ores Terraria needs - keep `WorldGen`
  structures map `GenVars.structures.CanPlace` when it exists);
- stamp the pixel scene (tiles, liquids from Noita materials, our liquids by Fluids), record its Noita entities
  (`EntityLoad` inside the scene) as placements for the loot pass.
- Start small: Noita's altars/ruins with a wand or flask (coalmine/excavationsite pixel scenes), oil tanks, 2-3
  scenes per zone; grow by the sheet.

## 4. Loot pass (PC)
- Every Terraria chest: Noita level by chest kind (current `ChestLevel`), then `chest_random.lua` with that level ->
  1-2 items (spells/wands/flasks/gold) into empty slots; keep Terraria's own items.
- Cave wands and flasks: `spawn_wands` / `spawn_potions` / `spawn_items` placements of the zone's biome at Terraria
  cave floor spots (air with solid below), density from the sheet. Wands stay as now (WorldLoot spots, made when the
  player comes near). Flasks: item when M3 lands; until then skip.
- Every spell Noita never spawns by level: once in a deep chest (current rule, keep).
- Log one line: `worldgen: <n> scenes, <n> wands, <n> spells in <n> chests, <n> flasks`.

## 5. Checks
- Cloud: Core tests for the recorder, chest_random with stub scripts, pixel scene decode (tiny PNGs).
- PC game test `game_test.ps1 -Mode worldgen`: generate a small world with a fixed seed, read the log line: wands > 0,
  chests with spells >= 50 %, scenes > 0; screenshot one scene; same seed twice -> same counts.
- Play check (author): new small world, first 3 wooden chests, at least one has a spell.
