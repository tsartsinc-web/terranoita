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
- Melty: 0.3.1 LIVE; 0.4.2 submitted as a draft 2026-10-08 (PC-16), live after the author's Play in Melty.
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
- PC-21 new (FIRST; author 2026-10-09: magic is the core, most spells do not work as in Noita): follow
  design/magic_plan.md phase by phase (0: no silent failure, 1: a test that compares with Noita, 2: component
  runtime, 3: missing components by impact, 4: author plays). Check: the plan's section 5.
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
