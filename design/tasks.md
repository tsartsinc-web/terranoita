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
- Open plans: cheaper magic tests (PC-1), effect interactions / electricity (design/effect_interactions.md).
- Roadmap to 1.0: design/roadmap.md. Next release M2 0.4.0 "Magic": Must = PC-1 summary OK, PC-5 FPS, an hour of
  play without errors; traders/electricity/lasers come in 0.4.x.

## PC queue
- PC-1 new: cheaper magic tests. game_test -Mode magic/wands end with ONE summary (OK / no shot / error / not done
  counts) + distinct problems only (max 20 lines); per spell/wand summary in design/sources/magic_baseline.txt,
  later runs print only the diff; OK/FAIL decided from the sheets (projectile -> fires, damage -> target hurt,
  uses -> spent, wand -> mana spent + recharge); screenshots only on FAIL and not opened unless asked.
  Check: a run prints <= 25 lines; a second run without changes prints one "no change" line.
- PC-4 waits PC-3, CLOUD-2: Physics/Electricity.cs (section 3) + the pool test (section 4).
- PC-5 new: FPS with ~20 scripted shots (Core is ~1 us per script run now): if still slow, profile GameShotHost
  (InRadiusWithTag, positions). Check: SlowFrames shows no mod part over 8 ms with 20 shots.

## CLOUD queue
- CLOUD-4 new: status_effects ELECTROCUTION row from design/sources/electricity_facts.json (GameEffect, 40 frames,
  disable_movement) via a tool (seed_status_effects.py reads the facts file); Conduction (CLOUD-2) energy = the
  ElectricityComponent energy of the loaded file (facts). Check: preflight 1a CLEAN, tools tests.
- CLOUD-1 new: Core tests comparing LuaGun casts with lua_cast_golden.txt. Check: dotnet test green.
- CLOUD-2 new: Core/Physics/Conduction.cs (grid-agnostic flood fill through conducting cells, charge timers, caps,
  no allocations) + tests (one pool, two pools, cap, decay, refresh). Check: dotnet test green, preflight 1a CLEAN.
- CLOUD-3 waits PC-1: tools script that diffs a magic summary against magic_baseline.txt (if PC-1 does not do it
  in the game). Check: unit test in tests/tools.

## Author (questions; agents do not wait for answers)
- Roadmap decisions: design/roadmap.md "Open author decisions" (trader, flasks, perks, bosses).
- Noita sets no electrical_conductivity on water/blood/acid: we assume every liquid conducts (oil/glue do not). Right?
- Electricity: do Terraria's lava, honey, shimmer conduct? Player stun: Terraria's Electrified buff or our status?
- Optional: a signatures-only reference of Terraria.exe so the cloud can compile the game code (licence: your call,
  never in a public repo).

## Done (last ~8)
- done (this commit) PC: PC-3 conducts column (extract_liquids.py), electricity facts file, plan section 0b.
- done f070180 PC: PC-2 golden file design/sources/lua_cast_golden.txt (tncli lua-golden, seeded LuaGun, 422 lines).
- done bbfc632 CLOUD: SpellProjectileFromEntity (cloud_task_core_3).
- done 63cdbfd CLOUD: faster LuaShotScripts + ProgressInfo (cloud_task_core_2).
- done 2015bd5 CLOUD: physics review fixes (cloud_task_physics_fixes).
- done 4472669 PC: spell shots from any entity file in game.
- done 37fd01c PC: tests in their own world; wands test skips passed wands.
