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
- PC-4 waits PC-3, CLOUD-2: Physics/Electricity.cs (section 3) + the pool test (section 4).

## CLOUD queue
- CLOUD-4 new: status_effects ELECTROCUTION row from design/sources/electricity_facts.json (GameEffect, 40 frames,
  disable_movement) via a tool (seed_status_effects.py reads the facts file); Conduction (CLOUD-2) energy = the
  ElectricityComponent energy of the loaded file (facts). Check: preflight 1a CLEAN, tools tests.
- CLOUD-1 new: Core tests comparing LuaGun casts with lua_cast_golden.txt. Check: dotnet test green.
- CLOUD-2 new: Core/Physics/Conduction.cs (grid-agnostic flood fill through conducting cells, charge timers, caps,
  no allocations) + tests (one pool, two pools, cap, decay, refresh). Check: dotnet test green, preflight 1a CLEAN.

## Author (questions; agents do not wait for answers)
- Roadmap decisions: design/roadmap.md "Open author decisions" (trader, flasks, perks, bosses).
- Noita sets no electrical_conductivity on water/blood/acid: we assume every liquid conducts (oil/glue do not). Right?
- Electricity: do Terraria's lava, honey, shimmer conduct? Player stun: Terraria's Electrified buff or our status?
- Optional: a signatures-only reference of Terraria.exe so the cloud can compile the game code (licence: your call,
  never in a public repo).

## Done (last ~8)
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
