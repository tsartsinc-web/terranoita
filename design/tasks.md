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
- PC-8 new, URGENT (before any release): players get stage 1b (Entry.Stage default "1b", 4e17e91; no TERRANOITA_STAGE
  in the release/recipe) -> all stage 2 (14: physics, liquids, world_load) and stage 3 (22: magic) hooks are off ->
  empty chests, no wands (author played, found none), probably no physics since 0.3.0. Ship the release stage
  explicitly (a constant set by the release build; env still overrides) after `preflight --gate <it>` is clean;
  WorldLoot.Load: retry when NoitaArt was not Ready; `version N` line in .wld.magic, refill old worlds once.
  Add a release test: start the packaged build exactly as the recipe does (no TERRANOITA_* env), check the log's
  bound hooks count and "world loot". Rebuild 0.4.0 (the built zip has the bug).
  Check: release test passes; new world, log "world loot: N spells" with N > 0.
- PC-9 waits CLOUD-6: night spawns on the surface (author: crawlers/shooters/bombers kill him in the first minutes by day).
  Needs CLOUD-6. Spawning: a creature whose zone row says `time: night` spawns only when !Main.dayTime (blood moon
  and eclipse count as night); passive ones (sheet says `time: any`) keep spawning by day. Check: autotest day 10 min
  -> 0 hostile Noita spawns on the surface, night -> spawns.
- PC-11 new: lake animals never spawn (author never saw a duck/deer/sheep/elk/wolf): surface_water check is
  "spawn tile in water", but walkers need ground there. Add Zones.NearWater(x, y, 12) (surface water within 12
  tiles) and set terraria_zones surface_water.terraria_check to it (sheet edit allowed by this row; gen_cs);
  swimmers keep the in-water rule. Check: autotest near a surface lake spawns a walker from Lake.
- PC-12 new: build check of cloud's edit in src/Terranoita/Shots.cs (explosions of creature shots now hurt every
  hostile NPC in the radius, the thrower too, as Noita damage type "explosion" via new Damage.StrikeAs; a worm
  once, by its head; author: miner's dynamite hurt only the player and blocks). Also in play: rat (sheet only):
  bite reach 1.6 tiles, full 33x15 hitbox. Check: builds; dynamite hurts creatures next to it; rats bite only
  when touching.
- PC-13 new: build check of cloud's edit: player spell explosions and digging spells (black hole etc.) break only
  what the caster's best pickaxe could (Physics/Blast.cs RequiredPick, PickPower via Player.GetBestPickaxe; author).
  Verify GetBestPickaxe with tr-methods and RequiredPick against `TN_IL=1 tncli tr-methods Terraria.exe Player
  GetPickaxeDamage`. Creature explosions keep the explosives rule. Check: builds; copper pickaxe + bomb spell
  leaves ebonstone/hellstone/dungeon intact, dirt/stone break.
- PC-10 new, after PC-8 and CLOUD-7: worldgen per design/worldgen_plan.md sections 1, 3, 4, 5 (our passes in
  Terraria's worldgen, scenes, loot by Noita's chest_random.lua). Check: game_test -Mode worldgen (section 5).
- PC-6 new: the 39 known spell failures (design/sources/magic_baseline.txt, MODLOG "memory leak, spells test"):
  test window for delayed/caster-centred spells (or expectations by kind), giga holes no shot (3-alive tag query in
  TerrariaWorld.WithTag), lasers/lightning (roadmap 0.4.x). Check: spells run shows them fixed or marked known.
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
