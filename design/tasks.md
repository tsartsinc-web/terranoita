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
- Melty: 0.4.7 published 2026-10-10 (author: "release now", server mode NOT verified; 0.4.6 before); 0.4.6 published 2026-10-10 (in review at publish; 0.4.5 LIVE before, 2026-10-10 22:43, published by the agent with the author's permission for this version; 381 gets,
  317 players before it). Description: the author's "!!!" header first, unchanged. Uninstalls ~13% (0.4.4).
  Multiplayer: 89 games hosted, 0 joins (PC-27).
- Stage 3 magic in game: all spells via Noita's gun.lua, shot scripts, all wands, wand window (U), progress window (O),
  16 spell slots; Terraria magic bonuses apply (mana cost/damage/crit/regen/Mana Flower); max mana 600 (ManaCap.cs).
- Spells test: 416/422 OK (design/sources/magic_baseline.txt), left in PC-6.
- 2026-10-08 (PC; author: good enough, polish later): flasks (Magic/Flasks.cs), wand/potion altars (WorldLoot), Noita
  main menu (sky, music, "PRESS F TO KICK GID!", no RE-LOGIC intro), wooden start cart with tip-over physics,
  size-scaled kick with Noita's sounds, liquids sink/mix by density, oceans + Underworld lava protected,
  hostile surface spawns only at night, new worlds' chests filled again (WorldLoot save bug).
- PC: no page file, 16 GB, the game is 32-bit (~4 GB): a test with little free memory can hang the PC (11:33 today).
  game_test has no memory check any more (author). Launch with `-ExecutionPolicy Bypass`.
- Tests use ONE world (Terranoita Magic in testsave); `game_test -NewWorld` only after a worldgen change. No tour mode.
  game_test never closes a game it did not start (the author's Melty play). Modes added: reactions (117/126 OK).
- 2026-10-08 (PC, in 0.4.2): Noita music by place (NoitaMusic.cs), Steam multiplayer hosting log (Multiplayer.cs),
  Noita reactions at Noita's speed (Fluids), liquids settle, fire projectiles light blocks, spell engine rules
  (SpellShots.Physics.cs), extra entities merged into shots (Core). MODLOG "Handoff 2026-10-09".
- Roadmap: design/roadmap.md. Worldgen plan: design/worldgen_plan.md (PC-10 after CLOUD-7).

## PC queue
- PC-38 doing (author's multiplayer test 2026-10-10, top priority): ROOT CAUSE (from Terraria's code): Host & Play starts
  plain TerrariaServer.exe (Main.HostAndPlay); our mod is not in it. Item.SetDefaults turns deprecated item types to air,
  and our spell/wand/flask/perk carriers ARE deprecated types (only our process un-deprecates them): the server deletes
  them from chests and saves the world without them; Noita creatures never spawn (server); the kick's knockback is
  client-side only. Fix: Host & Play starts Terranoita.exe in server mode (launcher passes -server) so the server runs
  the mod; test locally first. Done today (built, verified in game, single player): every chest holds 2-6 spells by
  rarity (new world: 848 spells in 256 chests; older worlds topped up once: WorldLoot version 4); our physics never
  breaks chests/dressers (falling blocks, blasts, fire, acid: Mats.HoldsItems; the Dead Man's Chest loss); drinking
  checked in game: flask (alcohol 1000 -> 985, INGESTION_DRUNK) and pool both work (game_test -Mode perks).
  O key in multiplayer: not found yet (ProgressWindow needs Book, loaded on Player.Hooks.EnterWorld).
  Server mode written (built, NOT verified): launcher -server -> TERRANOITA_SERVER, Entry: Main.dedServ = true right after
  Program.SavePath (as TerrariaServer.exe's RunGame), patches applied at Main.DedServ start (OnEngineLoad fires only in
  DrawMenu); Host & Play transpiler starts Terranoita.exe -server --noita-dir (HostServer.cs); server log server.log.
  Tests so far: run 1-2 the server ran as a client (patches came after RunGame) and generated a world (moved out of the
  author's Worlds to the scratchpad); run 3 did not test the new DLL (locked by a leftover test server). Next: copy the
  DLLs with no Terranoita running, start the server on the test world with -savedirectory testsave, check it listens and
  the log has no errors, then a client joins it locally (127.0.0.1:7779). Author: kick test skipped (author), release
  after the multiplayer fix approved, the Melty token comes later; the author asked to close Terraria (do not start it
  again until asked).
- PC-39 new (author 2026-10-10, small): Wall of Flesh drops a ready-built wand (a Noita wand with spells already
  in it, a working build) with a 10% chance per kill. Which builds: ask the author (default: Noita's wand script of
  level 3-4 with its spells kept). Check with one kill test, no probe.
- RELEASE 0.4.6 done (2026-10-10, published with the author's permission for this version): upload f8569bba, release
  1a0b2d71 (one click yes, findings review-only as 0.4.5), description: the "!!!" header first unchanged, then
  "What's inside" (counted), "New in 0.4.6", magic 86% (781/910), antivirus line. publish -> Melty safety review.
  Next: the full probe run (author: after the release), game_test -Mode perks_all, PC-37 Noita run.
- RELEASE 0.4.6 FIRST (author 2026-10-10: prepare the Melty update now, before any other task): perks (PC-34), 1%
  spell drop (PC-35), last night's magic fixes. Build, game_test magic + probe (behaviour must not drop below 781 of
  910), close the games. Melty description: keep the author's "!!!" header at the top unchanged; add the line "If
  install fails on 0Harmony.dll, your antivirus removed it: add the Melty folder to its exclusions"; add a "What's
  inside" section with REAL counts from code/sheets (spells, modifiers, wands, perks in pool/total, liquids and
  gases, reactions, creatures, status effects) and the probe number as "X% of spells behave as in Noita (tested
  against real Noita)". Upload + submit_release 0.4.6 as a DRAFT (author's request for this release); tell the author
  it is ready; publish only when he says so. Then continue with PC-36.
- NIGHT PLAN (author 2026-10-09, asleep; work without pauses until the limit ends): order = release 0.4.5 if not
  done (publishing authorized) -> PC-33 -> PC-31 -> PC-32 -> PC-30 (50 wands compare) -> PC-24 -> PC-35 -> PC-34 (perks). Rules: never wait for
  the author: if a step is blocked (needs him, a login, a crash you can't fix in ~3 tries), write one line in the
  task row ("blocked: why") and take the next task. Commit + push after every step. Tests in the background,
  minimized, close the games after; Noita only for new probe rows. Save tokens: grep/ranges, summary lines only, no
  reports between steps. Before the limit runs out: MODLOG handoff with the probe behaviour number and the next step.
- RELEASE 0.4.5 done (2026-10-10, verified): game_test magic on a new world 12/12, worldgen 86 more chests (172 -> 258),
  38 empty wands, 19 spells + 16 rare; probe on the same 109 rows: behaviour 76 vs 71 (run E). The 2026-10-09 "451"
  was the test harness: Terraria paused the minimized test game when another window was active (fixed: autotest sets
  PlayWhenUnfocused). Package build/terranoita-0.4.5.zip (1307032 bytes, sha256 c8cef6db...). MP physics and
  electricity on by default (not tested with several players).

- PC-37 new (author 2026-10-10: "закончить магию, чтобы вся синергия работала как надо"; goal for magic, together
  with PC-36): SYNERGY = things working TOGETHER as in Noita, not only one by one. Today only 24 combos + 48 wands are
  checked (wands 35/48). 1) Add probe sets (new Noita rows only, PC-29): a) modifier x modifier on a few base
  projectiles (pairs of the most used modifiers: homing, speed, damage, trigger/timer, multicast, shapes, orbit,
  bounce, piercing, fire/elec/slime trails...; ~200 pairs); b) trigger chains (trigger -> trigger -> payload,
  timer inside trigger, multicast inside trigger); c) perk x spell: perks that change shots (extra projectiles,
  crits, bouncing, homing, damage kinds) cast with a basic wand; d) more Noita wands (another 50, levels 1-6).
  2) Same behaviour verdict; fix by the biggest shared cause. Done when: wands >= 90%, combos/pairs >= 90%, perk x
  spell >= 90% behaviour match, and the author's own wands from play feel right. Report all four numbers each run.
- PC-38 new (author 2026-10-10: "проверить мобов"; after PC-36/37): test EVERY Noita creature, automated, like the
  spell probe. 1) game_test -Mode creatures_all: for each creature of enemies.json (all stages in the release), in the
  test world: spawn at a fixed spot with the test player at fixed distances; record: spawned OK, sprite/animation
  loads, HP, moves/flies/swims/digs as its ai says, notices the player, each attack fires (shots, melee, explosion,
  summons) and hurts, status effects it applies, dies, blood/liquid, death effects, drops (gold, 1% spell); errors in
  the log. Verdict per creature: WORKS / PARTIAL (which part) / BROKEN. 2) Ground truth from Noita: a creatures set in
  the Noita probe mod (spawn, same distances, record attacks/projectiles/damage/movement): new rows only (PC-29), one
  Noita run. Compare attacks and behaviour, not exact numbers. 3) Also natural spawning: where each spawns
  (zones/biomes, day/night rule, hardmode >1000 HP rule) on a test world. Fix by the biggest shared cause.
  Check: "creatures: WORKS X of N, match Noita Y of N"; list PARTIAL/BROKEN in MODLOG.
- PC-36 new (author 2026-10-10: "нужно все перки проверить"; do it next): test EVERY perk (106; 12 checked so far),
  automated, same idea as the spell probe. 1) game_test -Mode perks_all: for each perk, a fresh test character in the
  test world takes only that perk, then a fixed scenario runs (stand, walk, jump, take a hit of each damage kind,
  touch water/lava/acid/fire, cast a test wand, kill a creature) and the log records what changed vs no perk (max
  life, speed, jump, immunities, damage taken, shots per cast, wand stats, spawned entities, effects/icons). Verdict
  per perk: WORKS / NO EFFECT / ERROR. 2) Ground truth from Noita: add a perks set to the Noita probe mod (perk pickup
  on the player, same scenario, same records): new rows only (PC-29 rule), one Noita run. Compare: behaviour match
  per perk. 3) Fix by group, biggest first. Also stacking (take twice) for stackable perks. Check: "perks: WORKS X of
  106, match Noita Y of 100 (pool)"; list NO EFFECT/ERROR perks in MODLOG.
  Step 1 written 2026-10-10 (built only): Magic/PerksAllTest.cs, `game_test -Mode perks_all -Minutes 20`: baseline
  without a perk, then each perk (stackable ones twice too): stand (life, mana, run, jump, flight, gravity, defense,
  immunities, the perk's Noita components), a hit of 20, five debuffs touched, one Spark Bolt cast at a target, a kill.
- PC-35 new (author 2026-10-10, small: do it with the next release): every Noita creature has a 1% chance to drop a
  random spell item on death (MagicItems.MakeSpell; spell level by the creature's tier / depth, e.g. Maker.RandomAction
  with the zone's Noita level). Not from bosses (they drop perks, PC-34), not from summoned/split creatures.
  Check: log line per drop; a quick kill test of ~300 creatures gives ~3 drops.
  Verified in game 2026-10-10 (game_test -Mode perks): 2 spell drops from 198 zombie_weak kills (BULLET_TRIGGER,
  Y_SHAPE); Loot.SpellDrop 1/100, Noita level of the depth, summoned/spell-made and boss_* excluded. Done.
- PC-34 doing (verified in game 2026-10-10, game_test -Mode perks: King Slime drops 1 perk item on a normal world, 12 perks taken with Noita's funcs, death -> 0 left and 3 dropped); design/perks.md written 2026-10-10 (106 perks, 37 adapted or changed; read it first); Core NoitaPerks.Read + `tncli perks` (106, pool 103) done (AFTER the night plan's magic tasks; author's design 2026-10-09, final): Noita perks.
  1) ALL of Noita's perks (data/scripts/perks/perk_list.lua + their scripts/effects, run Noita's own code where it
  can: stage-3 rule); study each perk's mechanics, adapt only what has no meaning in Terraria and say which.
  2) Perks are ITEMS: random perk items DROP from Terraria bosses when killed; more drops on higher difficulty
  (Expert/Master/legendary). No choice screen, no altars: the drop is a random perk.
  3) Held in the inventory; used from the hand (use button) -> the perk is applied to the character, the item is gone.
  4) Bound to the character, saved with it. On death ALL perks are lost; if the character had more than 10 perks,
  a quarter of them (random ones from the list he had) drop as perk items at the death spot, like coins.
  Check: kill a boss in a test world -> perk items drop; use one -> effect + icon; die -> perks gone, >10 -> 1/4
  drop. Write the perk list + adaptations to design/perks.md before coding.
  Built only (2026-10-10): Magic/Perks.cs: perk item (Apple Pie Slice carrier, prefix = perk number), boss drop (1 +
  expert + master + ftw + EXTRA_PERK), use from the hand (PERKS_LOTTERY 50% keep), per-character file
  %LOCALAPPDATA%/Terranoita/perks/<player file>.txt, death: all lost, >10 a quarter drop (hook perk_death), game
  effects as Terraria immunities (fire, toxic, electricity, freeze, gills, knockback; others log "not done yet").
  Core done (2026-10-10): LuaShotScripts.CreateEntity/RunPerk, GlobalsGet/SetValue, run flags, world state entity,
  ComponentGetMetaCustom; `tncli perks <noita> --run`: 102 of 106 funcs run on Noita's own player.xml (left: STRONG_KICK
  KickComponent.max_force has no default in the docs; ALWAYS_CAST/EXTRA_MANA/EXTRA_SLOTS need the held wand as an entity).
  Built only: the player's store entity = player_base.xml's components (no scripts/children), funcs run on use and
  replayed on load (one-offs except EXTRA_HP/RESPAWN not replayed), GameEffect components it gains -> immunities,
  max_hp share scales Terraria max life (adapted); ShotEffectComponent extra_modifier -> gun.lua each cast; icon row (ui_icon, hover text); carried wands as Noita wand entities for the wand perks (tncli perks --run: 105 of 106). Next: the held wand as
  (done), LuaComponents/children of perks (fields, ghosts), then the boss test.
- PC-32 new (after PC-31): fix real failures by group, biggest first (run B counts): no damage ~35 (BLACK_HOLE_GIGA,
  WHITE_HOLE_GIGA, LASER_EMITTER, METEOR, MISSILE, ORBIT_LASERS...); nothing spawned ~32 (GLITTER_BOMB, GLUE_SHOT,
  SUMMON_EGG, THUNDERBALL, SPORE_POD, CRUMBLING_EARTH, WALL_VERTICAL/SQUARE...); wrong count ~40 (TENTACLE_PORTAL
  8 vs 10, DARKFLAME, METEOR_RAIN...); big path differences ~10 (TENTACLE, FISH, EXPLODING_DEER, PHASING_ARC).
  Author: burning damage over time (bombs, nukes) is LOW priority for now; explosion SIZE as in Noita matters; small
  orbit radius / homing differences are fine if it looks the same. Check: behaviour number rises per step.
  2026-10-10: run G3 (3 casts, build 71d9f3a): behaviour 775 of 925 (design/sources/pc_probe_compare.txt). After it:
  probe-compare leaves out 15 Noita rows that did not cast (mana 0 for a deck that costs mana; strays of the previous
  test): re-probe them with the 8 fit rows in the next Noita run. Fixed (targeted run T1: CRUMBLING_EARTH, GLUE_SHOT,
  WALL_VERTICAL/SQUARE now OK): EntityLoad of script-only entities (bounce_fx_file), of LoadEntitiesComponent entities
  (glitter shards, crumbling earth), of looks-only ones (wall_sound); SetStartVelocityComponent; ProceduralRandom(f);
  Nxml: a comment between attributes no longer drops the rest (glitter_bomb.xml's load_this_entity; facts may change:
  rerun `tncli facts` + apply_facts once); shots roll on the ground instead of spending bounces (grenade lived 47
  frames, Noita ~100; RollSpeed 60 px/s assumed). Run H3 (all of it + probe waits while the target burns):
  behaviour 774 of 910 (G3 with the same compare 772): +19 (BOMB, NUKE, GLITTER_BOMB, CRUMBLING_EARTH, WALL_*, wand 1-24...)
  -17 (grenade rows: rolling took real bounces, BOUNCE_HOLE 1 vs Noita 5; POISON_TRAIL: the wait counted poison). Fixed
  after H3: bounces are spent first, only then a slow ground contact rolls; the wait is for fire only. H3 + targeted
  T2 (183 GRENADE/BOUNCE/POISON/BLOODLUST tests again, same build as now): behaviour 781 of 910 (single 364/419, mod
  358/419, combo 24/24, wand 35/48) = design/sources/pc_probe_compare.txt.
- AUTHOR 2026-10-09 (via cloud, the author is away): FOCUS. Finish magic first (PC-21..PC-25 with PC-28), then
  PC-27. Park PC-4, PC-10, PC-19, PC-17, PC-6 until then.
- PC-30 doing (author 2026-10-09: WHAT MATTERS in magic; it sets the priority of PC-23..PC-25): behaviour first,
  numbers later. Exact bullet speed, bounce height, small damage differences are LOW priority. HIGH priority, in order:
  1) every spell does what it should, as in Noita (fires its shot/effect, the shot exists and acts: digs, summons,
  teleports, heals, makes liquid, explodes, triggers its payload...);
  2) every modifier works as in Noita: on-hit/touch (touch_*), shapes/formations (I/Y/T/W/circle/pentagram, DIVIDE_*),
  homing, orbits, spirals, boosts (damage/speed/crit/fire rate), triggers/timers/expiration, multicasts, ALL draw-many;
  3) spell BUILDS work: Noita's own random wands (wand_level_0N.lua via LuaWandMaker, fixed seeds) cast their decks and
  do what the same deck does in Noita.
  So: probe-compare reports a BEHAVIOUR verdict per row (shot types spawned, count, payload fired, modifier effect seen:
  path turns for homing, circle for orbit, N shots at the right angles for shapes...), separate from numeric diffs;
  the coverage number = behaviour matches. Add a probe set "random_wands" (e.g. 50 wands, levels 1-6, fixed seeds,
  same decks in Noita and Terraria; new rows, so one extra Noita run per PC-29). Fix order: what breaks most
  behaviours first. Check: probe-compare prints "behaviour: X of 875 (+ random wands Y of 50)" and the number rises.
  Done 2026-10-09: behaviour verdict in probe-compare (f4cb119): behaviour 646 of 875. Left: random_wands probe set
  (needs one Noita run for the new rows), fixes by most behaviours broken (top: hit/no hit 67, explosion 40,
  burning 40, damage kind labels 24).
- PC-29 new (author 2026-10-09, rule for PC-22/23 and later): Noita runs ONCE, Terraria as often as needed. Noita is
  only the reference and does not change, so its answers are recorded once and kept. 1) One full hands-free probe
  run to all 875 rows (resume from the 299 done), preferably when the author is away (night), then commit it as
  design/sources/noita_probe.jsonl (the ground truth). 2) After that, no Noita launches: fixes are checked with
  `game_test -Mode probe` + `tncli probe-compare` against the committed file. 3) Run Noita again only for NEW rows
  (new combos/cases: probe only those, append) or when the probe mod changes so old rows are invalid; say which one
  in the commit. 4) Never run Noita and Terraria at the same time (memory). Check: noita_probe.jsonl has 875 rows
  in git; later commits that touch magic show a probe-compare number and no Noita run.
- PC-28 done (2026-10-09, verified by run): hands-free Noita probe: the mod fires by itself (S5), run_probe.ps1 clicks
  Noita's New game + first mode tile (no key presses), protections forever, player found again after a polymorph,
  only a finished run is copied (design/magic_plan.md "Running the ground truth").
- PC-21 doing (FIRST, author 2026-10-09; plan design/magic_plan.md; author's rules: new component path behind a
  switch, into a release only when coverage >= the current path; Noita facts only from Noita's files or the Noita
  test mod; every session report "spells matching Noita / total"; tag claims verified in game / built only / assumed).
  Phase 0 (no silent failure): done: Core SpellRuntime.cs (component/field lists, NotRun, UnreadFields; built only).
  (a) done (built only): at the 600 cap the oldest shot is Evicted (quiet), the new one fires, log once.
  (b) done (built only): first Fire of each file and its extra_entities logs "spell runtime: <file>: not run yet: ..."
  (SpellShots.Physics.cs ReportRuntime). (c) done (verified by run): `tncli magic-coverage <noita> [out.json]`:
  static coverage 311 of 422 spells (gun.lua's fired files + extra/game_effect entities; scripts' deeper children not
  followed yet) -> design/sources/pc_magic_coverage.json. Phase 0 done; game run of (a)/(b) not yet. Next: PC-22.
- PC-26 done (2026-10-09): 0.4.3 draft on Melty (see State). Off in 0.4.3 by default (not verified in game):
  multiplayer physics (TERRANOITA_MP_PHYSICS=1), electricity in liquids (TERRANOITA_ELECTRICITY=1).
- PC-22 done (2026-10-09, verified by run): design/sources/noita_probe.jsonl = all 875 tests from real Noita with
  flight paths (PC-29: Noita is not run again for these rows). Noita settings and the author's run restored (checked).
- Probe numbers (2026-10-09 evening, verified by runs; design/sources/pc_probe_compare.txt is run B): run E (one cast a
  test) behaviour 704 of 925 (752 with PC-31's compare) (single 331/422, mod 326/429, combo 22/24, random wands 25/50), numbers 613. Single-cast
  runs flip ~20 rows each way between runs (random spread): TERRANOITA_PROBE_REPEAT=3 casts each test 3 times and
  probe-compare takes the majority. Running: F3 (switch off, 3 casts, build 91753ba, DLLs saved in the scratchpad
  build_F3), then C3 with the same DLLs and TERRANOITA_RUNTIME=components (PC-24 step 1 check).
- Fixed after run E, not measured yet (built only): blast fire (create_cell_probability, assumed model), thrown physics
  bodies (assumed model), lasers (LaserEmitterComponent), blast-loaded shots, tentacles (assumed model). Open: homing
  formula (needs probe rows made for it), Verlet curl and damage by speed, laser digging, LARPA copies.
- PC-23 done (2026-10-09): probe-compare with a flight check (along/across each side's first direction, scaled to
  Noita's start speed) and a behaviour verdict (PC-30). `game_test -Mode probe` (~20 min) + `tncli probe-compare <noita>
  design/sources/noita_probe.jsonl design/sources/probe_game.jsonl design/sources/pc_probe_compare.txt`. Move
  %LOCALAPPDATA%/Terranoita/probe_game.jsonl aside before a new run (the test resumes from it).
- PC-24 doing: step 1 measured 2026-10-10 (C3, 3 casts, stopped at 594 full tests for the release): components 513 vs
  the current path 516 of the same 588 rows (H3T2): not better yet, the switch stays off. (Phase 2, steps in
  design/magic_plan.md): step 1 built only (c9f6e15): with TERRANOITA_RUNTIME=components
  every shot is in the store and VelocityComponent drives its flight (Core ShotFlight, fitted to probe paths). Next:
  a probe run with the switch on, compare with the run without it (equal or better), then step 2.
- PC-25 new (Phase 3-4): missing components by spells affected (magic_plan Phase 3 order), then the author plays.
  Check: magic_plan section 5.
- PC-20 done-in-code (multiplayer): wand window clicks threw IndexOutOfRange in NetMessage.SendData (ChestItem
  context syncs the open chest, -1): slots use InventoryItem context now (WandWindow.SlotContext; BankItem needs an open container). Check: author clicks
  wand/spell slots while hosting, no "ERROR in wand window".
- PC-19 doing (multiplayer): physics runs on our client too (Patches.Live: netMode != 2); every tile/liquid our
  physics changes is sent (Physics/NetSync.cs: TileSquare for blocks/walls, sendWater for Terraria liquid, 120 per
  frame). Our liquids (Fluids cells) stay per player; blocks dropped by blasts are client-side items. Check: author
  hosts, pours a flask (flows), sand falls, fire burns wood; no errors; a second player sees the block changes.
- PC-16 waits author: 0.4.2 submitted to Melty 2026-10-08 as a draft (upload d67636aa, 1272555 bytes, sha256
  45bd55f3ee081346..., one click yes; multiplayer maxPlayers 255 + host address "Hosting at " in latest.log, no join
  args: joining untested). Live after the author presses Play on 0.4.2 in the Melty app. Next: test Host & Play +
  a friend joining (+connect_lobby), then add connect.joinArgs ["+connect_lobby","{address}"].
- PC-17 doing: in-game checks of the author's spell list (MODLOG "spells and physics from the author's list"):
  done by test: physics (layering, flask, platforms), spells ONLY (13 OK: LIGHTNING strikes, homebringer pulls,
  DELAYED_SPELL lives 100 frames), fire projectiles, reactions 117/126. Left: BALL_LIGHTNING fan and ICEBALL range by
  eye (the spells test cannot show them; ICEBALL cause unknown), the full spells run against the baseline, the
  LIGHTNING blast damage default (5 assumed).
- PC-18 new (optional): SpellShots.Physics.cs reads Noita files at runtime; CLOUD-8 put the same values in
  spell_projectiles.json (liquid_drag, terminal_velocity, die_on_*, bounce_energy, penetrate_world, lightning_*):
  read the sheet instead, keep behaviour. Check: spells ONLY run unchanged.
- PC-6 doing: spells left: MINE_DEATH_TRIGGER, EXPLODING_DEER, BOMB_CART, DEATH_CROSS (moving/summoned entities,
  cross lasers); PIPE_BOMB* go off only in another blast (Noita), the test should expect that. Giga spells cost
  500-600: castable now with 600 max mana.
- PC-4 doing: Physics/Electricity.cs on Core's Conduction; sources as in Noita's data (effect_interactions.md 0c):
  entities with ElectricityComponent (Lua EntityLoad/shoot_projectile, blast load_this_entity) + ELECTRIC_CHARGE
  impacts; creatures hurt + held, player Electrified; physics test scene 15 (pool, 2 zombies). Left: run
  `game_test -Mode physics` (author's OK); metal tiles (no conducts column in materials.json); tanks'
  in_liquid_shooting_electrify_prob; the electrocution loop sound.
- PC-10 doing: worldgen per design/worldgen_plan.md. Step 1 written (not run): pass "Terranoita: loot" after Final
  Cleanup (WorldLoot.Gen.cs, hook worldgen_passes): altars + every chest by Noita's chest_random(_super).lua; the
  first save writes .wld.magic. Check: `game_test -Mode magic -NewWorld` (author's OK; not while the author plays):
  log "worldgen: Noita's chests: ..." with wands/spells/flasks > 0, no errors. Next: biome spawn_wands/potions per
  zone (biome_spawns.json), then scenes (section 3).
- Known gaps: flask powders (gunpowder_unstable, purifying_powder) do not pour (no Fluids kind); multiplayer still
  clamps synced mana at 400 (MessageBuffer.GetData).
- PC-27 new (after the current magic step): multiplayer joins through Melty. Melty 2026-10-09: 89 games hosted,
  0 joins. The live recipe has multiplayer (maxPlayers 255) and connect.address (log
  {localappdata}/Terranoita/logs/latest.log, after "Hosting at ", Multiplayer.cs writes the Steam lobby id) but no
  join rule, so Melty only shows the id. Add connect.joinArgs ["+connect_lobby", "{address}"] (Terraria's own
  NetClientSocialModule parameter; the launcher passes unknown args through) to design/melty.recipe.draft.json and the
  next release; validate_recipe. Melty's rule: test hosting and joining with two copies before claiming it works
  (two PCs / two Steam accounts: the author arranges it); if joining cannot be tested, leave joinArgs out. Then in
  that session verify multiplayer physics and turn TERRANOITA_MP_PHYSICS on by default if it works (off since
  0.4.3). Check: a friend presses Melty's join link and lands in the host's world; install_outcomes "Playing
  together" shows joins > 0.

## CLOUD queue
- (CLOUD-6 dropped: the PC did night-only surface spawns in Spawning.cs without a sheet column.)

## Author (questions; agents do not wait for answers)
- Roadmap decisions: design/roadmap.md "Open author decisions" (trader, flasks, perks, bosses).
- Answered: only what conducts in Noita conducts; the player's stun is Terraria's Electrified; max mana 600 (15 stars).
- Electricity (0c): plain LIGHTNING and ARC_ELECTRIC load no electricity in Noita's data, so they do not electrify
  water here (only ELECTRIC_CHARGE, ELECTROCUTION_FIELD, thunder mages...). In your Noita, does a plain lightning bolt
  electrify water? If yes, it is engine-side and we add it.
- Optional: a signatures-only reference of Terraria.exe so the cloud can compile the game code (licence: your call,
  never in a public repo).

## Done (last ~8)
- done (PC) PC-31 (2026-10-10, Core tests 140/140): probe-compare a) path = nearer of scaled/unscaled, b) "hit-miss"
  number for a direct hit <= 1.3 on one side with the same shots, c) RANDOM_*/DAMAGE_RANDOM decks: only "fired" is
  behaviour. Run E recompared: behaviour 704 -> 752 of 925 (single 346/422, mod 353/429, combo 24/24, wand 29/50);
  49 rows up, 1 down (HOMING_CURSOR: now checked past frame 5, 52 px off at 10). d) was already so (count by file).
  design/sources/pc_probe_compare.txt = run E with it.
- done (PC) PC-15: biome scripts checked on the player's Noita: SCRIPTS fixed from data/biome/*.xml + common.csv
  (Lukki Lair = rainforest_dark, Overgrown Cavern = fungiforest, Ancient Laboratory = liquidcave, Magical Temple =
  wandcave, Snowy Chasm = winter, Cloudscape = clouds, Sky = the_end.lua); probe -> design/sources/
  pc_biome_functions.json, absent functions per_10k 0; init(x,y,w,h); chest_random runs clean; color_material.
- done (CLOUD) CLOUD-7: Core NoitaPng (8-bit PNG reader), PixelScene (WangColors from materials.xml, Decode,
  Downscale 16/3 px per tile, mostly-air = air), NoitaBiomeSpawns (runs a biome script or chest_random.lua with a
  recording host: EntityLoad/LoadPixelScene/CreateItemActionEntity/LoadBackgroundSprite -> Placements,
  RegisterSpawnFunction colours, seeded Random/ProceduralRandom, GetRandomAction via LuaWandMaker, Missing/Errors);
  sheet biome_spawns.json (tools/seed_biome_spawns.py: 28 biomes x 5 functions, per_10k_tiles defaults, script
  names _unverified); tncli biome-spawns, pixel-scene. 113 Core tests, python tests.
- done (CLOUD) CLOUD-8: spell_projectiles.json from the PC's runtime rules: air_friction = documented 0.55 when
  unset (106 rows changed, nothing else), new columns liquid_drag, terminal_velocity (-1 = apply_terminal_velocity
  0), die_on_liquid, die_on_low_velocity, low_velocity_limit, bounce_energy, penetrate_world, lightning_radius,
  lightning_damage (LightningComponent is_projectile blast; 5 when unset, _unverified). die_on_hit IS
  on_collision_die (desc fixed). Defaults from noita_facts _component_docs (apply_spells.load_docs). Core
  SpellProjectileFromEntity mirrors it (102 Core tests). PC (optional): SpellShots.Physics may read these columns.
- done (CLOUD) CLOUD-9: tests/tools/test_extract_liquids.py (child inheritance, reacts_as only along
  _inherit_reactions, rule selection by name / parent / [tag] / [tag]_suffix, input3/direction/blob columns,
  fast_reaction); extract_liquids.select_reactions split out of main (same output).
- done (PC, 2026-10-08) PC-15: author accepted flasks, altars, wands, magic, cart as good enough; tour test removed;
  cart test now also kicks a bunny/slime/zombie (0.4 / 5.2 / 4.2 tiles).
- done (PC, 2026-10-08) flasks: Noita's potion.lua fills them (LuaWandMaker.MakePotion, tncli lua-potion), spray /
  throw / drink / suck, starting flask, 1 in 4 chests, sandbox chest; WandData.Flask carries them.
- done (PC) wand and potion altars (biome_impl/*_altar_visual.png at 3 px per Noita px), loot file version 3.
- done (PC) menu: Noita sky layers, menu music, kick text; RE-LOGIC intro skipped (SkipSplash.cs, patched at launch).
- done (PC) new worlds' chests were never filled (world-gen save wrote an empty .wld.magic): fixed, old files refilled.
- done (PC) liquids sink/mix by Noita density; oceans + Underworld lava protected (Fluids.Protected).
- done (PC) mana 600 (ManaCap.cs), Terraria magic bonuses on wands, PC-9 night-only hostile surface spawns.
- done (PC) PC-14 liquid pairs in the physics test; PC-12/13 build checks; PC-7 golden check runs in game_test.
- done (PC) game_test modes: play (just the game), tour (new medium world, chests, death, flask chest), cart.
