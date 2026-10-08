# World generation: Noita inside Terraria's worldgen (plan, 2026-10-08, reviewed the same day)

Order: PC-8 (loot at load works for players) first; it already gives chests spells. Sections 1-4 come after, step
by step: loot pass, then a few scenes; stop when it is fun, not when every Noita scene is in.

Author: "checked many chests, no wands, no spells; worldgen is the big problem". Goal: Noita's content is placed by
Noita's own biome code during Terraria's own world generation, not sprinkled over a finished world.

## 0. Why chests were empty (fix first, PC-8)
- `Entry.Stage` defaults to "1b" (commit 4e17e91, 2026-10-06). The release and the recipe set no TERRANOITA_STAGE, so
  every hook with stage 2/3 is off for players: `world_load` (2) never binds -> `WorldLoot.Load` never runs -> no
  cave wands, no chest spells. Same for all `magic_*` hooks (3). The 0.4.0 zip built from 026b8bb has this too:
  do not upload it as is.
- `WorldLoot.Load` returns silently when `NoitaArt.Ready` is false at world load and never retries; when the
  `.wld.magic` file exists it never fills chests again (new chests, worlds from older versions).
- 0.3.1 (live) has no magic at all. Likely worse: the 14 stage-2 hooks (physics, liquids, world_load) are off for
  players since 0.3.0 too (Patches.On is true, but its hooks never bind). Verify: a Melty-installed copy's log,
  the "hooks" line. Root cause in the process: every game test sets TERRANOITA_STAGE, so no test ever ran the
  release the way Melty starts it (no env vars). PC-8 adds that test.

## 1. Where it hooks in Terraria (PC)
- Terraria builds a world as a list of passes (`WorldGen.GenerateWorld` -> `AddGenerationPass(name, ...)`; names
  like "Buried Chests", "Micro Biomes", "Final Cleanup"). Find the exact API with
  `tncli tr-methods <Terraria.exe> WorldGen "AddGenerationPass|GenerateWorld"` and
  `tncli tr-methods <Terraria.exe> Terraria.WorldBuilding.WorldGenerator`.
- Add our passes by a Harmony prefix on the generator's run (`WorldGenerator.GenerateWorld`): insert our pass
  objects into its private pass list after a named pass (check the field name and the pass type with tr-methods;
  `PassLegacy(name, method)` if it exists). Never replace or reorder Terraria's passes:
  - "Terranoita: scenes" after "Micro Biomes" (structures, section 3).
  - "Terranoita: loot" after "Final Cleanup" (wands, flasks, chest spells, section 4; all chests exist by then).
- All randomness from `WorldGen.genRand` (the world seed): same seed -> same Noita content.
- Noita's files must be read before our passes run (NoitaArt.Ready, the Lua state). If they are not, skip the passes
  and mark the world for the retrofit on first load (never block world creation, never crash it).
- Chest items are saved in the .wld by Terraria itself. Cave wand spots go to `.wld.magic`, but the world's path
  may not be final during generation: keep them in memory, write on the first world save (world_save hook).
- One source of loot: the generator writes `.wld.magic` with `version N`; the load-time fill (WorldLoot.FillChests)
  runs only when that line is missing or older, so new worlds are never filled twice.
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
- How often: start with a sheet `biome_spawns.json` (biome, function, per_10k_tiles) filled with simple numbers the
  author can tune. Later, optional: tools count the coloured spawn pixels per function in Noita's wang tiles
  (`data/wang_tiles/<biome>.png`, colour -> function via `RegisterSpawnFunction` in the biome lua) and suggest them.
- Chest contents: run Noita's `chest_random.lua` / `chest_random_super.lua` drop functions with the recorder
  (they call `EntityLoad` of wands, potions, spells, gold) -> Terraria chest items. Map what has no item yet in a
  sheet: gold nuggets -> coins by value, hearts -> life crystal/heart, potions -> skip until flasks (M3); log the rest.
- Pixel scenes: `LoadPixelScene(materials.png, visual.png, x, y, background, ...)` -> read the PNG with the materials
  colour table (materials sheet `color` -> material -> Terraria tile/liquid by the existing mapping); `visual` gives
  paint/wall where useful. Core returns a tile grid; the game stamps it.
- Scale: 1 Noita pixel = 3 Terraria pixels (apply_facts PIXEL_SCALE), so 1 tile = 16/3 = 5.33 Noita pixels. Scenes
  shrink to ~1/5: each tile takes the most common material of its 5-6 px block (air if most is air). Use only scenes
  >= 64 px wide (>= 12 tiles); small ones turn into noise.
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
- Cloud: Core tests for the recorder, chest_random with stub scripts, pixel scene decode and downscale (tiny PNGs).
- PC game test `game_test.ps1 -Mode worldgen`: generate a small world with a fixed seed, read the log line: wands > 0,
  chests with spells >= 50 %, scenes > 0; screenshot one scene; same seed twice -> same counts.
- Play check (author): new small world, first 3 wooden chests, at least one has a spell.
