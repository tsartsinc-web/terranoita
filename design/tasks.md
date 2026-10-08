# Terranoita tasks — read this first (then only the MODLOG section a task points to)

How it works (author, 2026-10-08): the author only says "работай" to either agent; nobody relays messages.
- Start: `git pull`, read "State" and your queue (CLOUD = cloud session, PC = local session on the author's PC).
- Take your rows top to bottom. A row is done only when its Check passes. Mark it `done <commit>` (move it to Done,
  keep Done to the last ~8 rows), add a MODLOG section (UTF-8, at the end), commit, `git pull --rebase`, push.
- Work for the other agent: add a row to its queue (what, files, Check). Questions for the author: "Author" list;
  do not block on them, take the next row.
- Ownership: CLOUD = src/Terranoita.Core, tests, tools/*.py, sheets, design docs (no games here).
  PC = src/Terranoita (game), anything that needs Terraria/Noita, game tests, releases. Do not edit the other's files
  without a row asking for it.
- Status words: new, doing, waits <row>, done <commit>.

## State (keep to ~12 lines; update when it changes)
- Melty: 0.3.1 LIVE (146 players). 0.4.0 zip built but NOT uploaded and must be rebuilt (PC-8: release runs stage 1b).
- Stage 3 magic in game: all spells via Noita's gun.lua (LuaGun), shot scripts (LuaShotScripts), all 48 wand files
  pass the wands test, spell shots from any entity file, progress window (key O), 16 spell slots.
- Physics review fixes in (atomic saves under a lock, no pool regeneration on a broken file).
- Cheaper magic tests in (summary + baseline). Open plan: electricity (design/effect_interactions.md 0b, PC-4).
- Roadmap to 1.0: design/roadmap.md (M2 magic, M3 flasks, M4 perks, M5 bosses, M6 polish). Next release M2 0.4.0 "Magic": Must = PC-1 summary OK, PC-5 FPS, an hour of
  play without errors; traders/electricity/lasers come in 0.4.x.

## PC queue
- PC-14 code done (PC), needs `game_test -Mode physics`: basins x0+84.. with water+radioactive_liquid, blood+poison,
  lava+blood_cold, water+cement (Noita has no plain water+lava row: Terraria's obsidian); log "liquid pairs: ...". Was: physics test gets liquid + liquid cases (none today: only acid on dirt, burning oil, slime
  on oil): Noita water + lava -> steam/rock, acid + water, blood + lava, toxic sludge + water (rows in
  reactions.json); log `Fluids.Fired` per case, run in release mode. Check: each case fires its reaction.
- PC-9 waits CLOUD-6: night spawns on the surface (author: crawlers/shooters/bombers kill him in the first minutes by day).
  Needs CLOUD-6. Spawning: a creature whose zone row says `time: night` spawns only when !Main.dayTime (blood moon
  and eclipse count as night); passive ones (sheet says `time: any`) keep spawning by day. Check: autotest day 10 min
  -> 0 hostile Noita spawns on the surface, night -> spawns.
- PC-11 code done (PC), needs an in-game check: Zones.NearWater(x, y, 12) in the surface_water row. Was: lake animals never spawn (author never saw a duck/deer/sheep/elk/wolf): surface_water check is
  "spawn tile in water", but walkers need ground there. Add Zones.NearWater(x, y, 12) (surface water within 12
  tiles) and set terraria_zones surface_water.terraria_check to it (sheet edit allowed by this row; gen_cs);
  swimmers keep the in-water rule. Check: autotest near a surface lake spawns a walker from Lake.
- PC-10 new, after CLOUD-7: worldgen per design/worldgen_plan.md sections 1, 3, 4, 5 (our passes in
  Terraria's worldgen, scenes, loot by Noita's chest_random.lua). Check: game_test -Mode worldgen (section 5).
- PC-6 (PC) 30 of 39 fixed (spells run 2026-10-08: target 3 tiles, window up to 5 s while shots fly, test mana 1000).
  Left (baseline): MINE, MINE_DEATH_TRIGGER, PIPE_BOMB, PIPE_BOMB_DEATH_TRIGGER (no explosion in 5 s), EXPLODING_DEER,
  BOMB_CART, CURSED_ORB (moving entities), DEATH_CROSS, DEATH_CROSS_BIG. Author question: NUKE_GIGA, BOMB_HOLY_GIGA,
  BLACK/WHITE_HOLE_GIGA, ALL_NUKES, ALL_SPELLS cost 500-600 mana, Terraria caps player mana at 400: never castable.
- PC-4 new: Physics/Electricity.cs (section 3) on Core's Conduction (Emit/Tick/ForEach; IConductGrid = liquids'
  and Terraria water's `conducts` + metal tiles; energy = the loaded file's ElectricityComponent energy, 1 = 1 tile,
  tune) + the pool test (section 4). ELECTROCUTION row is in status_effects (0.667 s, creatures cannot move; the
  player gets Terraria's Electrified, author). Check: the pool test.
- PC-7 new: golden check: `TERRANOITA_NOITA_DIR=<noita> dotnet test tests/Terranoita.Core.Tests --filter LuaGolden`
  or `tncli lua-golden <noita> --check design/sources/lua_cast_golden.txt` (exit 1 + the diff); add it to pc_step
  or game_test. Check: "golden: no change".

## CLOUD queue
- CLOUD-6 new: column `time` (night|any) for surface spawns: terraria_zones surface_* rows = night; enemies with a
  passive ai (duck, sheep, deer, elk, fish, eel) and rat (author: rats stay by day) = any (enemies.time overrides
  zone); gen_cs; tests.
  Check: preflight 1a CLEAN, gen_cs ok, unit tests.
- CLOUD-7 new: Core NoitaBiomeSpawns + chest_random + pixel scene decode + biome_spawns.json by tools
  (design/worldgen_plan.md section 2). Check: Core tests; `tncli biome-spawns` for the PC.

## Author (questions; agents do not wait for answers)
- Roadmap decisions: design/roadmap.md "Open author decisions" (trader, flasks, perks, bosses).
- Noita sets no electrical_conductivity on water/blood/acid: we assume every liquid conducts (oil/glue do not). Right?
- Electricity: do Terraria's lava, honey, shimmer conduct? Player stun: Terraria's Electrified buff or our status?
- Optional: a signatures-only reference of Terraria.exe so the cloud can compile the game code (licence: your call,
  never in a public repo).

## Done (last ~8)
- not a bug (PC): PC-8. Entry.Stage only picks creatures (spawns, art preload); Harmony PatchAll applies EVERY patch
  whatever the stage (log: "patches applied: 44 methods" with 14 "hook ok" lines). Sandbox run without
  TERRANOITA_STAGE had chests, wands, cart. The hook log now lists all hooks. The 0.4.0 zip is fine on this.
  Author's empty chests were most likely the Melty 0.3.x build (no magic there).
- done (PC) PC-12: Shots.cs did not build (CS0136: `n` reused in the NPC loop) -> renamed to npc; builds.
- done (PC) PC-13: builds; Player.GetBestPickaxe() and GetPickaxeDamage exist (tr-methods); RequiredPick values ok.
- done (this commit) CLOUD-5: BeamFromEntity (LaserEmitter + ConfigLaser, Lightning + config_explosion) + LightningPath.
- done (this commit) CLOUD-1: LuaGolden (Core: lines, Diff) + tncli lua-golden --check + Core test on the PC.
- done (this commit) CLOUD-2: Core/Physics/Conduction.cs + ConductionTests.
- done (this commit) CLOUD-4: ELECTROCUTION row (seed_status_effects.py --electricity).
- done (this commit) PC: memory leak (Lua state per wand -> max 6), fast shots hit along their path, spells test +
  baseline 383/422 OK.
- done 78d3f25 PC: PC-1 cheaper magic tests (summary + problems + baseline diff in game_test.ps1; magic run prints 3
  lines, a second run "baseline: no change"); CLOUD-3 not needed (the diff is in game_test.ps1).
- done (this commit) PC: PC-5 shot scripts never over 8 ms in SlowFrames (77-289 scripted shots); "spell shots" up to
  8.7 ms on explosions. Memory grows ~15 MB per set in the magic test (a Lua state per wand?) - watch.
- done (this commit) PC: PC-3 conducts column (extract_liquids.py), electricity facts file, plan section 0b.
- done f070180 PC: PC-2 golden file design/sources/lua_cast_golden.txt (tncli lua-golden, seeded LuaGun, 422 lines).
- done bbfc632 CLOUD: SpellProjectileFromEntity (cloud_task_core_3).
- done 63cdbfd CLOUD: faster LuaShotScripts + ProgressInfo (cloud_task_core_2).
- done 2015bd5 CLOUD: physics review fixes (cloud_task_physics_fixes).
- done 4472669 PC: spell shots from any entity file in game.
- done 37fd01c PC: tests in their own world; wands test skips passed wands.
