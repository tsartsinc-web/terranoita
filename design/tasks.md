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
- Melty: 0.3.0 LIVE (stage 2 physics); 0.3.1 draft waits for the author's Play in the Melty app. Stage 3 not released.
- Stage 3 magic in game: all spells via Noita's gun.lua (LuaGun), shot scripts (LuaShotScripts), all 48 wand files
  pass the wands test, spell shots from any entity file, progress window (key O), 16 spell slots.
- Physics review fixes in (atomic saves under a lock, no pool regeneration on a broken file).
- Cheaper magic tests in (summary + baseline). Open plan: electricity (design/effect_interactions.md 0b, PC-4).
- Roadmap to 1.0: design/roadmap.md (M2 magic, M3 flasks, M4 perks, M5 bosses, M6 polish). Next release M2 0.4.0 "Magic": Must = PC-1 summary OK, PC-5 FPS, an hour of
  play without errors; traders/electricity/lasers come in 0.4.x.

## PC queue
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
- CLOUD-5 new: Core for the spells without a ProjectileComponent (lasers, lightning; roadmap 0.4.x, PC-6):
  `Core/Noita/BeamFromEntity.cs`: from XmlEntity read LaserEmitterComponent (laser.* nested fields: max_length,
  beam_radius, damage_to_entities, damage_to_cells, max_cell_durability_to_destroy, beam_particle_type...,
  is_emitting, emit_until_frame) and LightningComponent (sprite_lightning_file, is_projectile, explosion_type and
  config_explosion.*, arc_lifetime) into a BeamDef (fields named after Noita's, units as the docs say; docs in
  NoitaEntityXml's ComponentFieldTypes); plus `LightningPath` (deterministic for a seed: a jagged path from a to b
  like Noita's arcs, segment count from length, IConductGrid-like `Solid(x,y)` stop). Tests with synthetic XML.
  Check: dotnet test green, preflight 1a CLEAN.

## Author (questions; agents do not wait for answers)
- Roadmap decisions: design/roadmap.md "Open author decisions" (trader, flasks, perks, bosses).
- Noita sets no electrical_conductivity on water/blood/acid: we assume every liquid conducts (oil/glue do not). Right?
- Electricity: do Terraria's lava, honey, shimmer conduct? Player stun: Terraria's Electrified buff or our status?
- Optional: a signatures-only reference of Terraria.exe so the cloud can compile the game code (licence: your call,
  never in a public repo).

## Done (last ~8)
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
