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
- Melty: 0.3.1 LIVE (146 players). 0.4.0 not uploaded: build/terranoita-0.4.0.zip (9f0e8e5) is OLD, rebuild (PC-16).
- Stage 3 magic in game: all spells via Noita's gun.lua, shot scripts, all wands, wand window (U), progress window (O),
  16 spell slots; Terraria magic bonuses apply (mana cost/damage/crit/regen/Mana Flower); max mana 600 (ManaCap.cs).
- Spells test: 416/422 OK (design/sources/magic_baseline.txt), left in PC-6.
- 2026-10-08 (PC, most untested by hand, PC-15): flasks (Magic/Flasks.cs), wand/potion altars (WorldLoot), Noita
  main menu (sky, music, "PRESS F TO KICK GID!", no RE-LOGIC intro), wooden start cart with tip-over physics,
  size-scaled kick with Noita's sounds, liquids sink/mix by density, oceans + Underworld lava protected,
  hostile surface spawns only at night, new worlds' chests filled again (WorldLoot save bug).
- PC: no page file, 16 GB, the game is 32-bit (~4 GB): a test with little free memory can hang the PC (11:33 today).
  game_test has no memory check any more (author). Launch with `-ExecutionPolicy Bypass`.
- Roadmap: design/roadmap.md. Worldgen plan: design/worldgen_plan.md (PC-10 after CLOUD-7).

## PC queue
- PC-15 new: check by hand/tests what 2026-10-08 added (one `game_test -Mode play` session with the author, or
  `-Mode tour` / `-Mode cart`): kick distance (<= 5 tiles, sound), cart (wooden, kick flies a few tiles and tips over,
  riding), flasks (LMB spray, RMB throw, down drink, dip to fill), wand/potion altars in caves, menu (sky, music
  event:/music/mountain/enter - loop it if it stops, text), intro skipped (log "RE-LOGIC intro skipped"), 15 mana
  stars, magic bonuses, ocean protection, lake animals near water (PC-11). Fix what fails; MODLOG.
- PC-16 new: rebuild the 0.4.0 package (MODLOG "0.4.0 package" steps) with everything since 9f0e8e5; upload to
  Melty only when the author says so (the author signs in: browser pane / Claude in Chrome; recipe
  design/melty.recipe.draft.json, mod c68ad4c6-f9db-40f5-802c-a4f9d7713e69).
- PC-6 doing: spells left: MINE_DEATH_TRIGGER, EXPLODING_DEER, BOMB_CART, DEATH_CROSS (moving/summoned entities,
  cross lasers); PIPE_BOMB* go off only in another blast (Noita), the test should expect that. Giga spells cost
  500-600: castable now with 600 max mana.
- PC-4 new: Physics/Electricity.cs on Core's Conduction (design/effect_interactions.md 0b, section 3) + pool test.
- PC-10 waits CLOUD-7: worldgen per design/worldgen_plan.md sections 1, 3, 4, 5. Check: game_test -Mode worldgen.
- Known gaps: flask powders (gunpowder_unstable, purifying_powder) do not pour (no Fluids kind); multiplayer still
  clamps synced mana at 400 (MessageBuffer.GetData).

## CLOUD queue
- CLOUD-7 new: Core NoitaBiomeSpawns + chest_random + pixel scene decode + biome_spawns.json by tools
  (design/worldgen_plan.md section 2). Check: Core tests; `tncli biome-spawns` for the PC.
- (CLOUD-6 dropped: the PC did night-only surface spawns in Spawning.cs without a sheet column.)

## Author (questions; agents do not wait for answers)
- Roadmap decisions: design/roadmap.md "Open author decisions" (trader, flasks, perks, bosses).
- Answered: only what conducts in Noita conducts; the player's stun is Terraria's Electrified; max mana 600 (15 stars).
- Optional: a signatures-only reference of Terraria.exe so the cloud can compile the game code (licence: your call,
  never in a public repo).

## Done (last ~8)
- done (PC, 2026-10-08) flasks: Noita's potion.lua fills them (LuaWandMaker.MakePotion, tncli lua-potion), spray /
  throw / drink / suck, starting flask, 1 in 4 chests, sandbox chest; WandData.Flask carries them.
- done (PC) wand and potion altars (biome_impl/*_altar_visual.png at 3 px per Noita px), loot file version 3.
- done (PC) menu: Noita sky layers, menu music, kick text; RE-LOGIC intro skipped (SkipSplash.cs, patched at launch).
- done (PC) new worlds' chests were never filled (world-gen save wrote an empty .wld.magic): fixed, old files refilled.
- done (PC) liquids sink/mix by Noita density; oceans + Underworld lava protected (Fluids.Protected).
- done (PC) mana 600 (ManaCap.cs), Terraria magic bonuses on wands, PC-9 night-only hostile surface spawns.
- done (PC) PC-14 liquid pairs in the physics test; PC-12/13 build checks; PC-7 golden check runs in game_test.
- done (PC) game_test modes: play (just the game), tour (new medium world, chests, death, flask chest), cart.
