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
- Melty: 0.4.4 LIVE (2026-10-09: 349 gets, 290 players, one click yes; 0.4.3 replaced by 0.4.4). Uninstalls ~13%.
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
- AUTHOR 2026-10-09 (via cloud, the author is away): FOCUS. Finish magic first (PC-21..PC-25 with PC-28), then
  PC-27. Park PC-4, PC-10, PC-19, PC-17, PC-6 until then.
- PC-30 new (author 2026-10-09: WHAT MATTERS in magic; it sets the priority of PC-23..PC-25): behaviour first,
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
- PC-29 new (author 2026-10-09, rule for PC-22/23 and later): Noita runs ONCE, Terraria as often as needed. Noita is
  only the reference and does not change, so its answers are recorded once and kept. 1) One full hands-free probe
  run to all 875 rows (resume from the 299 done), preferably when the author is away (night), then commit it as
  design/sources/noita_probe.jsonl (the ground truth). 2) After that, no Noita launches: fixes are checked with
  `game_test -Mode probe` + `tncli probe-compare` against the committed file. 3) Run Noita again only for NEW rows
  (new combos/cases: probe only those, append) or when the probe mod changes so old rows are invalid; say which one
  in the commit. 4) Never run Noita and Terraria at the same time (memory). Check: noita_probe.jsonl has 875 rows
  in git; later commits that touch magic show a probe-compare number and no Noita run.
- PC-28 new (part of PC-22, do it first): the Noita probe runs with no input from the author (he had to press New
  Game, pick the first mode, and hold fire). 1) The probe mod fires by itself: set the player's ControlsComponent
  fire fields (mButtonDownFire etc.) each frame while a test wand is held. 2) Noita goes straight into a run: try
  noita_dev.exe startup options, then a prepared save that continues directly, last resort a script that clicks
  New Game and picks the first mode; write the way that works in design/magic_plan.md. Check: one probe run from
  start to log file with nobody touching the PC.
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
- PC-22 doing (author 2026-10-09: "разрешаю, делай": the agent runs Noita itself): probe mod tools/noita_probe
  (installed). Noita settings changed for the run, backups *.terranoita_backup next to them + scratchpad:
  save_shared/config.xml (mods_sandbox_enabled 0, disclaimer/warning done, application_pause_when_unfocused 0),
  save00/mod_config.xml (terranoita_probe enabled); RESTORE both after the run. Firing from Lua (verified in Noita):
  PlatformShooterPlayerComponent.mForceFireOnNextUpdate + mRequireTriggerPull=0, ControlsComponent.enabled=0 + aim
  fields (S5). Run: scratchpad probe_run.ps1 (Noita restarts its process once: watch the jsonl, not the pid);
  ~5 tests/min. Check: probe_out.jsonl has 875 rows + done -> copy to design/sources/noita_probe.jsonl.
- Probe status 2026-10-09 (afternoon): Noita rows 299 of 875 (design/sources/noita_probe_partial.jsonl); the hands-free
  run (tools/noita_probe/run_probe.ps1, backup/restore verified) gets stuck in Noita's menu: Enter starts the game only
  when "New game" is selected. ASKED the author: one screenshot of Noita's menu, or one click on New game. Terraria rows
  875 (design/sources/probe_game.jsonl). Matching Noita: 190 of 299 single (noise +-3: one random shot per side).
  Fixed from the data: probe test (wand in hand, target 16x20 like the probe's, arena cleared of old targets), negative
  air_friction (rockets), blast = explosion damage only. Open: grenade/bounce on floor, fire damage (DOT not recorded),
  physics projectiles (bomb), wall spells, single-sample noise (cast each test 3 times?).
- PC-23 doing: step 1 Core NoitaProbe/MiniJson (tests), step 2 SpellRecorder + SpellProbeTest (`game_test -Mode probe`,
  built only), step 3 Core ProbeCompare + `tncli probe-compare <noita> <noita_probe.jsonl> <probe_game.jsonl> [out]`
  (tests). Left: after PC-22, run game_test -Mode probe (never together with Noita: memory), compare, report the
  number. Check: "matching Noita: N of M" printed and the author's list visible in the reasons.
- PC-24 new (Phase 2): component runtime in Core behind a switch (TERRANOITA_RUNTIME=components); move existing
  behaviour type by type. Check: PC-23 number equal or better per step.
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
