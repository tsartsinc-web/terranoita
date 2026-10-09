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
- Melty: 0.4.2 LIVE (268 players); 0.4.3 and 0.4.4 submitted 2026-10-09 as DRAFTS (one click yes, recipe as 0.4.2 +
  fileName; description has the author's "Magic is a work in progress" note). 0.4.4 = 0.4.3 + starting wands/flask
  for a new character with an older one's name. Live once the author presses Play on 0.4.4 in the Melty app.
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
- Probe status 2026-10-09: Noita rows 299 of 875 (design/sources/noita_probe_partial.jsonl; Noita settings restored,
  mod disabled; resume: re-enable as PC-22 says, the probe skips done tests), Terraria rows 30
  (design/sources/probe_game_partial.jsonl; game_test -Mode probe resumes). Partial: matching Noita 17 of 30
  (design/sources/pc_probe_compare_partial.txt; most misses: projectile damage 0). Cast layer vs Noita
  (tncli probe-cast-check, first partial run): 147 of 203.
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
