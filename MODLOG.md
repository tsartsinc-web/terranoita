# MODLOG — Terranoita

Journal for whoever (person or agent) continues this. Anything not here is lost at the next context reset.

## 2026-10-06 — cloud session (no games available)

**Agreed with the author** (see README): Terraria 1.4.5 is the host, unmodified files; Noita enemies first (all),
then block-level physics (untouched caves/islands/clouds never collapse), then wands/spells with 4 wand slots,
then flasks/items with 4 slots. Single player first. Noita art/names read from the player's Noita at runtime.

**Melty facts** (game_info, 2026-10-06):
- terraria: engine xna; no loader Melty installs; tModLoader = Steam app 1281930, players install by hand → not used.
  Mod folders known to Melty: tModLoader/Mods, ResourcePacks, Worlds. Mode "standalone" also available.
- noita: engine other; no loader; mods folder {game}/mods. 0 mashups on Melty.
- Closest existing mashup: "Terraria+Peak" (terrariapeak, PEAK host). Nothing Terraria×Noita.
- `mashup_info terrariapeak` failed several times ("couldn't check your token") — retry later for a recipe example.
- Draft recipe (design/melty.recipe.draft.json): validate_recipe = valid; one_click_check = yes, publishable.
  Review finding: "unverified-game-integration" → must play it once through Melty before publishing.

**Route**: no community loader we can depend on → own launcher. `Terranoita.exe` (net48 x86) next to
`Terraria.exe`, loads it in-process, `Terranoita.Game.dll` applies Harmony patches, then calls
`Terraria.WindowsLaunch.Main`. Same idea as TerrariaModder (Harmony at runtime, game files untouched), which
proves the approach on 1.4.5. New NPC types: vanilla has fixed NPC arrays, so Noita enemies ride on a vanilla
"carrier" NPC slot + side table; patches replace AI/draw/name/loot for carriers.

**Unverified assumptions** (each is a sheet cell or a note in systems/hooks):
1. Windows Terraria 1.4.5 = .NET Framework 4 + XNA 4, x86 (web sources say XNA on Windows; bitness unconfirmed).
2. Entry point `Terraria.WindowsLaunch.Main(string[])`; embedded libraries resolved from manifest resources.
3. Steam: setting `SteamAppId=105600` lets SteamAPI init when we start the process (ownership still checked).
4. XNA content paths: we run from the Terraria folder (exe placed there), so relative `Content/` works.
5. data.wak layout: `u32 0, u32 count, u32 tocEnd, u32 0` then `{u32 offset, u32 size, u32 nameLen, name}`.
6. Noita XML: `<Base file>` overrides merge onto the first same-named component; `AnimalAIComponent`
   attribute names (`attack_ranged_frames_between`, `attack_melee_frames_between`, `attack_ranged_max_distance`,
   `attack_ranged_entity_count_min/max`) and `AIAttackComponent.frames_between` — confirm on real files.
7. Units: 1 Noita px = 2 Terraria px; Noita speeds px/s, gravity px/s².
8. Balance multipliers in `balance.json` are first guesses.

**Tools built and tested here**: preflight (172 open items for gate 1a, all needing the games), gen_cs (draft
compiles), Core (8 tests pass), tncli + apply_facts tested end-to-end on a synthetic data.wak, launcher builds.

**Next**: README "Продолжение на ПК".

## 2026-10-06 — author's PC (Terraria 1.4.5.8 x86, Noita main; both in D:\steam\steamapps\common)

Installed: Git, .NET 8 SDK 8.0.425, ilspycmd 8.2.0.7535 (needs `DOTNET_ROLL_FORWARD=Major`). Decompile in
`%USERPROFILE%\terraria-decomp` (outside the repo).

**Stage 1a runs in the real game** (autotest + author watching): all 12 enemies spawn (also naturally), walk/fly/hop,
melee, lunge, shoot; Noita sprites, names (player's language, from Noita's common.csv) and **Noita's own sounds**.
`preflight --gate 1a` is CLEAN, 18 Core tests pass.

Facts confirmed: x86 + LAA Terraria.exe; `WindowsLaunch.Main` + embedded-resource resolver; Steam start works (steam_appid.txt);
data.wak layout; component defaults from Noita's `tools_modding/component_documentation.txt`.

Hard-won launcher lessons (all in systems/hooks sheets):
- Load Terraria with `Assembly.Load("Terraria")` (default context), not LoadFrom, or the mod patches a second copy.
- Patching JIT-compiles Terraria methods, which runs beforefieldinit static ctors (Main → SavePath, CaptureManager →
  Main.instance). So: set `Program.SavePath` exactly like LaunchGame, then patch from `Main.OnEngineLoad`.
- Carrier NPC = type 146 (NPCID.None3, unused, hidden in bestiary). `aiStyle` must be 0, never -1 (UpdateNPC indexes
  NoMultiplayerSmoothingByAI[aiStyle]; the exception is swallowed and the NPC silently reset).
- Enemy shots are the mod's own list (no unused projectile type).
- Noita sounds: FMOD Studio 2.1.5 x86 dlls + banks loaded from the player's Noita (`NoitaFmod`); event list via
  `Terranoita.exe --list-noita-sounds "<Noita>" build/noita_sounds.txt`, used by apply_facts.

Author decisions today: Noita px → **3** Terraria px (was 2; "normal size"); per-enemy `size` column (hiisi shotgunner ×2);
Noita sounds, not Terraria's; ranged cooldown = frames_between + attack state duration (author: "1 shot per 2 s").
Balance set from hits-to-kill reasoning (balance.json `_sources`), first autotest only — tune with the author.

Testing: `TERRANOITA_AUTOTEST=1` + `-savedirectory %LOCALAPPDATA%\Terranoita\testsave` (copy of one world, fresh
"Terranoita Test" character) — enters the world, spawns each enemy in turn (removing the previous), logs status every
second to `%LOCALAPPDATA%\Terranoita\logs\latest.log`. Ctrl+Shift+N spawns the next enemy in normal play.
Window capture without focus: scratchpad `grab.ps1` (PrintWindow) — fails when the window is minimized.

**Open**: check foot alignment + size in game; author said enemies looked "semi-transparent" (added a brightness floor of 70,
not yet confirmed); licence + remix choice (ask); Melty packaging (step 8). Mod files currently copied into the
Terraria folder for testing: Terranoita.exe, Terranoita.Game.dll, Terranoita.Core.dll, 0Harmony.dll.

### Melty (same day)
- Draft **"Terranoita: Invasion"**, modId `c68ad4c6-f9db-40f5-802c-a4f9d7713e69`, slug `terranoita-invasion`,
  Studio https://melty.gg/studio/c68ad4c6-f9db-40f5-802c-a4f9d7713e69 , linked to GitHub tsartsinc-web/terranoita.
- Author: title "Terranoita: Invasion", English description, licence **All rights reserved**, remix **allowed** (allowRemix default true).
- Release 0.1.0 submitted (draft, publishable, one click: yes; review findings: unverified-game-integration, executable-code).
  Package = build/package (Release build) zipped; recipe = design/melty.recipe.draft.json with exact fileName.
- Screenshot uploaded (showcase capture: TERRANOITA_AUTOTEST=1 + TERRANOITA_SHOWCASE=1, noon + 5 enemies).
- **Waiting for the author**: press Test on the mashup's page in the Melty app; publish only with the author's permission.
- Local commits not pushed: `git push` needs the author's GitHub login. melty.json at repo root not added yet (needs consent).
- After the screenshot: thrower/shooter range hysteresis; **shrink_by_one_pixel** fixed (cells are frame_width apart,
  drawn 1 px smaller — the old +1 step made the miner's walk slide and snap); melee reach is center-to-center like Noita
  (was edge-to-edge: enemies bit from afar); rat `hitbox_mult` 0.5 (author). Author confirmed: miner walks normally.
  Release 0.1.0 re-uploaded with these fixes (still draft, publishable, one click: yes).

**Next session, in order**: (1) author presses Test in the Melty app → mod_status → publish only with the author's
"yes"; (2) author runs `git push` (needs their GitHub login), then ask about committing melty.json; (3) stage 1b
(all ~170 regular enemies): `python tools/apply_facts.py build/noita_facts.json --stage 1b`, then preflight --gate 1b
and fill what it lists (new archetypes: ghost_phase, wall_climber, worm, swimmer, static_turret, ...).

## Checklist for adding creatures (lessons from stage 1a — read before 1b)

Sprites
- Sprite rects: cells are frame_width x frame_height apart; `shrink_by_one_pixel` draws 1 px less (not a 1 px gap).
  Check each new sprite XML for other attributes (has_offset, other names) before trusting NoitaSprite.
- Draw: feet = lowest opaque pixel of the "stand" frame on the hitbox bottom (NoitaArt.Foot). Scale = 3 x `size`.
- Animation names differ per creature (stand/walk/run/fly/attack/attack_ranged/throw/jump_up/jump_fall); Carriers.AnimNames
  has the fallbacks — add names it does not know. Some creatures have several SpriteComponents (emissive/light layers
  are skipped; bodies made of several sprites or verlet limbs are not drawn yet — note them).
- Textures are premultiplied on load; Noita PNGs have binary alpha. Dark Noita sprites get a light floor of 70.

Numbers (all via tools: never type them by hand)
- Component defaults come from Noita's tools_modding/component_documentation.txt (AnimalAiDefaults etc.); an absent
  attribute is NOT zero (gravity 400, ranged max distance 160, dash 120 frames...).
- Base files: values often come from `<Base file>` chains (base_humanoid, base_enemy_flying...). NoitaEntity merges them.
- AnimalAIComponent uses attack_ranged_* names; AIAttackComponent uses min_distance/max_distance/frames_between.
- Ranged cooldown = frames_between + attack state duration (Noita stays in its attack state).
- Melee reach is center to center. Lunges: walkers leap in an arc at the target (Brain.Aim), flyers dart straight.
- Throws (physics projectiles like TNT) get the speed needed to reach the target.
- Hitbox from HitboxComponent; the author can change `size` (sprite+hitbox) and `hitbox_mult` (hitbox only).
- Sounds: pick the folder with a `death` event, most specific first (not animals/generic); attack events
  attack_melee / attack_dash / attack_shoot / voc_shoot / _throw / _voc_attack; check names against noita_sounds.txt.
- Some creatures can fly although the wiki calls them crawlers (acid/slime shooters): trust can_fly/can_walk from data.

Game side
- Carrier NPC 146 with aiStyle 0 (never -1). Terraria swallows exceptions in UpdateNPC and draw and silently removes the
  NPC: wrap new patch code in try/catch + Entry.Error, and watch the log for "ERROR".
- Every patch has a hooks.json row and a [Hook] class; patches are applied from Main.OnEngineLoad (not earlier).
- Debuffs: keep to early-game strength for tier t1/t2 (Poisoned, not Venom).
- Test each new creature with the autotest (TERRANOITA_AUTOTEST=1) on the PC: it must stand on the ground, walk without
  sliding, attack only when close, and its log must show attacks and no ERROR.

## 2026-10-06 — cloud session: stage 1b prepared without the games (branch claude/dazzling-carson-h8mi9n)

Cloud sessions can only push to their own branch: this one is **claude/dazzling-carson-h8mi9n** (it contains all of
claude/relaxed-lamport-enplwi, merged). Continue from it. Melty: nothing published or uploaded; players have 0.1.0.

**Done (all tested here: 39 Core tests, 7 tools tests, preflight --gate 1a CLEAN, launcher builds):**
- `design/sources/noita_facts.json` committed (.gitignore had kept it out). `apply_facts --stage 1b`: gate 1b open
  cells 3600 -> 889. Stage 1a rows unchanged (checked row by row).
- apply_facts fixes/additions: a projectile row shared by enemies firing different data.wak files is split (it had
  overwritten 1a's slimeball and tnt); ranged attacks matched by squashed names and 1-to-1 leftovers; data.wak-only
  ranged attacks get rows (frog_big.tongue, lurker.lurkershot); heal/support shots get projectile rows (healshot,
  shieldshot, invisshot); attack start sounds by kind (worm attack_bite, lukki limb_attack, fungus death_buildup...).
  Absent ProjectileComponent damage is left open (default from component_documentation.txt), never 0.
- Sheet corrections from data.wak: shaman.cursed_sphere and tentacler_small's circle are AnimalAI shots (kind
  projectile); lurker has no dash (its wiki lunge is lurkershot, row removed); pebble (giant's Rock Spirit) -> 1b.
- New columns: ai_archetypes `flees` (helpless_walker), `wake_tiles` (mimic 4: design value, tune in playtest);
  attacks `summons`. New rows nest_fly/nest_longleg/nest_firebug.release (summon; count/cooldown/range are guesses,
  marked unverified until the nests' spawn scripts are read).
- **tncli facts (newer format)**, for the next PC run: finds wiki-seeded ids (EntityLookup: guess, file name, name key,
  English name via common.csv, file words — cook, sentry, soldier, turret, traps, nests, hidden...), dumps behaviour
  components of every enemy, child entity and projectile (EntityDump: WormComponent, PhysicsAIComponent, IK limbs,
  AreaDamage, GameEffect, LuaComponent paths...), entity files named in spawn scripts (`script_entities`), sprite
  animation names, physics-body images as sprites, and Noita's component documentation for the components seen
  (`_component_docs`). apply_facts then fills: projectile effects (EFFECT rules), static creatures, empty attack
  lists, found entity files, summons.
- **Brain** (Terranoita.Core/Ai): ITerrain; climb (lukki on walls/ceilings, falls when it lets go), burrow (worm steers
  in ground, arcs out under gravity, Trail for its body), swim (stays in liquid, flops on land), phase/burrow pass
  tiles, flees, wake_tiles, auras on their own clock (per_frames "NF"), summon/heal/support, Hurt (wakes, retaliates),
  Died (death explosions) via ISpecialAttackSink. Tests: Brain1bTests.
- **Game side (NOT compiled: needs Terraria.exe)**: Carriers sets noTileCollide from Brain.PassesTiles, Terrain over
  Main.tile (active/inActive/tileSolid/liquid), Attacks.Special (aura hits, summons with a cap of 12 nearby, heals
  allies, death explosions), HitEffect calls Hurt/Died inside try/catch. Entry.Stage reads TERRANOITA_STAGE (default
  1a). Autotest: TERRANOITA_AUTOTEST_STAGE=1b (only that stage) and TERRANOITA_AUTOTEST_EXIT=1 (closes when done).
- **tools/pc_step.ps1**: all PC-only work in one command (see "Next" below). Parse-checked with PowerShell 7 and
  dry-run here on fake game folders; the -AutoTest part has not run yet.

**Assumptions to confirm (design values, not Noita numbers — replace from data when the dump has them):**
Brain.WormTurn 0.05 rad/frame (-> WormAIComponent direction_adjust_speed), GripMargin 6 px, death explosion radius
falls back to 2 tiles (-> the creature's explosion config), summon cap 12, mimic wake 4 tiles, nest release values.
Several scavengers list acidshot.xml as an AnimalAI ranged attack next to their AIAttackComponent ones (scavenger_mine,
_poison, _clusterbomb): check in the dump whether AnimalAI's own ranged attack is really used there.
14 creatures have only animals/generic sounds in data.wak (wolf, ant, thundermage, ...): that is correct, not a bug.

**Проверить на ПК (each 1b creature, autotest + watching):** stands on the ground (or clings/floats/swims as its kind
does), walks without sliding, attacks only when close/in range, log shows its attacks and no ERROR. Per kind also:
- ghost_phase (weakspirit, slimespirit, confusespirit, berserkspirit, wraith_glowing, ghost, wraith, ethereal_being,
  wisp, wraith_storm, lurker): drifts through blocks; aura/curse hurts only nearby; wraiths fire back when hit.
- wall_climber (lukki, lukki_longleg, lukki_tiny, lukki_dark, lukki_creepy_long): walks up walls and along ceilings;
  legs (IK limbs) are not drawn yet — only the body sprite.
- worm (worm_end, worm_big, worm_skull, meatmaggot, worm, eel, worm_tiny): moves inside the ground, leaps out and
  falls back; only the head is drawn (body segments along Brain.Trail: to do); eel in water.
- swimmer (fish, fish_large): stays in water, flops on land. helpless_walker (duck, sheep, deer, elk, scorpion): runs away.
- mimic (mimic_potion, chest_leggy, chest_mimic, dark_alchemist, shaman_wind): still until close or hit, then attacks.
- static_turret (hpcrystal, death_orb_lab, snowcrystal, ghost_crystal, crystal_physics, pebble, shooterflower,
  neutralizer, skycrystal_physics, sentry, turret, trap_acid, trap_arrow, trap_fire, trap_thunder, bloodcrystal_physics)
  and spawner_nest (nest_fly, nest_longleg, nest_firebug): never move; fire/release only with the player in range.
- tank (tank_rocket, tank, tank_super), drone_flyer (drone_lasership, healerdrone_physics, drone_physics, spearbot,
  drone_shield), hopper (missilecrab, frog_big, blob), flyer_lunge (iceskull, bigbat, firebug, bigfirebug): as 1a's.
- mage_levitate, floater_caster, walker_shooter, walker_thrower, walker_melee, slime_crawler: as their 1a kin
  (the 1a checklist above). Death explosions (fungi, tanks, drones, giantshooters, turret): radius and damage feel right.

**Next, in order:**
1. PC (no agent needed; or tell the PC agent just this): in the repo folder
   `git fetch origin claude/dazzling-carson-h8mi9n; git checkout claude/dazzling-carson-h8mi9n` then
   `powershell -ExecutionPolicy Bypass -File tools\pc_step.ps1` (~5 min; paths default to D:\steam\steamapps\common).
   It pushes new facts, sheets and design/sources/pc_check.txt (compiler errors of the game side, if any).
2. Cloud: read pc_check.txt and fix compile errors; fill from the dump: movement of worms/lukki/ghosts/fish/mimics/
   pebble from their components (+ _component_docs for units), unmatched attacks (list printed by apply_facts),
   undecided effects, worm body/limb drawing, AnimNames from sprite_animations; preflight --gate 1b clean;
   gen_cs --gate 1b.
3. PC: `powershell -ExecutionPolicy Bypass -File tools\pc_step.ps1 -AutoTest 1b` (~20 min, the game window opens and
   closes by itself; log pushed as design/sources/pc_autotest_1b.txt).
4. Cloud: fix what the log shows; set Entry.Stage default to 1b; version 0.2.0.
5. PC: the author watches a few creatures (list above), screenshot; Melty: package, Test in the Melty app, publish
   only with the author's "yes".

## PC step (2026-10-06, author's PC, branch claude/dazzling-carson-h8mi9n)
- Ran tools/pc_step.ps1 on 3dc58db (+ fixes below). Facts re-read from Noita, apply_facts 1b, Core tests ok,
  Terranoita.exe and Terranoita.Game.dll **build against Terraria 1.4.5.8 with no compile errors** (cloud code compiled as is).
- Fixes: tncli JSON output needs `TypeInfoResolver = DefaultJsonTypeInfoResolver()` (.NET 8 refused, facts step failed);
  pc_step.ps1 read the enemy count as a string (autotest wait became 0 min, game killed at once, a stale log passed) —
  now [int], and the old latest.log is deleted before the run.
- preflight --gate 1b: 576 open items (attacks 147, enemies 268, projectiles 161) — for the cloud.
- Autotest 1a (real run, 2 min, game exited by itself): 12 spawned, 45 hits on the player, 0 ERROR/WARN
  (design/sources/pc_autotest_1a.txt).
- Author: the autotest now removes Terraria's own hostile NPCs and stops natural Noita spawns while it runs.
  Rerun: 12 spawned, 50 hits, **all 12 enemies hit the player** (shotgunner and miner too: vanilla mobs had been
  eating the player's immunity frames), 0 errors.

## 2026-10-06 — cloud: stage 1b sheets from the facts dump (gate 1b: 576 -> 53 open, all waiting for the next facts run)

**Rules added to apply_facts (numbers from tools, defaults from _component_docs):** worms (WormAIComponent speeds are
Noita px/frame -> x3; WormComponent gravity/acceleration are px/frame per second -> x3/60; turn rates rad/frame; new
enemies columns roam_speed/turn_rate/roam_turn_rate; hitbox 2 x hitbox_radius; bite reach target_kill_radius, damage
bite_damage); PhysicsAI movers (lukki, chest_leggy): speed = min(force_coeff x target_vec_max_len, force_max) /
force_balancing_coeff px/s, accel 1-exp(-k_d/60), gravity 0 when levitate; IK limb reach; auras from
AreaDamageComponent / DamageNearbyEntitiesComponent (also on child entities; missing ones get a .touch row); death
explosions from ExplodeOnDamageComponent config_explosion; summons from ProjectileComponent spawn_entity (bigbat) and
spawn scripts (nests); script attacks (Lua loads a projectile: cooldown = execute_every_n_frame, on damage/touch =
retaliate); damage-based pairing of shots; AnimalAI's base acidshot ignored when AIAttackComponents exist; extra
data.wak shots get rows; static = no mover but plain physics (crawler speed 0 counts as still); dmg_mult defaults 1;
projectile sprite "none" when the file has no image; effect rules ignore visual/motion components.
Placeholders (in _sources, "design placeholder"): worm bite and lukki limb cooldown 40, script attack count/range,
nest release count/range, fungus death explosion radius 2 tiles, ghost hitbox 48x48.

**Sheet changes:** archetypes worm_water (eel, move burrow_liquid) and leggy_mimic (chest_leggy, climb + wake);
lukki.melee = limb hit 12.5; lukki_dark.melee = jaws aura; lukki_tiny.touch aura; lukki_longleg faction spider;
giant.rock_spirit = its pebble.xml throw (the Rock Spirit it becomes comes in 1c).
**Moved to 1c (need data not in facts yet):** trap_fire, trap_thunder, trap_acid (lookup had picked their shots/a prop),
trap_arrow, death_orb_lab, nest_longleg, pebble, ghost_crystal, snowcrystal, wisp, enlightened_alchemist, wand_ghost.

**tncli for the next run:** lookup prefers animals/ and buildings/ over projectiles/; script path prefixes;
script_projectiles (projectile facts of files scripts load); ranged_disabled (AnimalAI shots a script switches on);
_remove_from_base honoured. The 53 open cells (monk, statue_physics, bloodcrystal, wraith_glowing/storm, fungus_giga
pollen, neutralizer shot, coward) fill from those.

**Brain/game:** burrow speed steps by accel toward hunt/roam speed, turn rate from the sheet; burrow_liquid (eel) only
inside liquid; levitating climbers cross the open; Carriers draws plain .png sprites (eel was invisible), worm "eat"
animation for attacks. 44 Core + 7 tools tests pass; gate 1a clean.

**Next:** PC: `tools\pc_step.ps1 -AutoTest 1b` (facts -> apply -> preflight; if clean: gen_cs --gate 1b, build,
autotest 1b; else it stops and lists the open cells). Cloud: read pc_check.txt / pc_autotest_1b.txt, fix, set
Entry.Stage default 1b, version 0.2.0. Проверить на ПК also: worm leap heights and lukki speeds (unit inferences above).

## PC step 2 (2026-10-06, author's PC, 6f0ae5c)
- pc_step.ps1 -AutoTest 1b: facts ok, apply_facts ok, Core tests ok, both builds ok (no compile fixes needed).
- outcome **gate not clean**: one cell left, projectiles.neutralizer_shot.effect (UNVERIFIED). Autotest 1b skipped by the script.
- Fact from data.wak for that cell (not edited here, per instructions): neutralizershot.xml has ProjectileComponent damage 0;
  HitEffectComponent effect_hit=LOAD_CHILD_ENTITY -> data/entities/misc/neutralizer_target.xml, which has
  GameEffectComponent effect=MOVEMENT_SLOWER_2X frames=-1 (a slow on the target, no damage).
- Fixed CLAUDE.md: the mashup is a Melty DRAFT (0.1.0 submitted, not published), not "players have 0.1.0".

## PC step 3 (2026-10-06, author's PC) — stage 1b plays
- Author: "you are the local session" -> filled the last 1b cell here: neutralizer_shot.effect verified from data.wak
  (same file as neutralize_shot); Shots.cs: effect "neutralize" = BuffID.Slow 300 frames (MOVEMENT_SLOWER_2X).
- pc_step -AutoTest 1b: gate 1b CLEAN, tables generated, tests + both builds ok, **140 creatures spawned in Terraria**,
  256 hits on the player, 0 ERROR. 3 WARN: support attacks with no Terraria effect yet (scavenger_invis invisibility,
  scavenger_shield / drone_shield shield_buff).
- 94 creatures hit the player within their 6 s. 32 armed ones did not (some expected: nests, healers, shields, support):
  giantshooter giantshooter_weak nest_fly scavenger_invis giant wraith_glowing statue_physics drone_lasership worm_big
  lukki_dark scavenger_shield crystal_physics healerdrone_physics playerghost slimeshooter shaman scavenger_poison eel
  scavenger_heal tentacler_small bloom coward thunderskull cook sentry miner_fire nest_firebug drone_shield barfer
  wraith_storm wizard_swapper hidden — next: look at these (range/cooldown/LOS), maybe a longer autotest per creature.
- ("gone after 360 brain frames" lines are the autotest removing the previous creature — expected.)
- Not visually checked by a person yet (sprites, feet, sizes of the 140): ask the author to watch a run.

## Author watched the 1b autotest (for the cloud to analyse; log: design/sources/pc_autotest_1b.txt)
1. Some laser-gun creatures fired nothing. Leads: laser_spear sprite "none" (log: "sprite none failed"; 9 sprites failed
   of 251); intense_concentrated_light_variant speed 0.05 (megalaser_blue.xml is a beam, not a flying shot);
   laserbeam / laser_turret are probably LaserEmitterComponent beams in Noita — check how they deliver damage.
2. Worms: only flying heads, no body; the head does not face its target (only flips left/right); no hit damage seen.
   Noita worm.xml: WormComponent + WormAIComponent + CellEaterComponent, 6 SpriteComponents (worm_head, worm_body x?,
   worm_tail), audio animals/worm. Needs: body segments drawn along Brain's trail, head rotated to its velocity,
   worm_bite on contact. Later (stage 2 physics) CellEater = eats blocks. Note: the autotest's "remove other NPCs" only
   removes non-carrier NPCs, so segments are not the cause unless they are separate NPCs.
3. Ghosts (weakspirit, slimespirit, confusespirit, berserkspirit): no sound, no attack, no damage; they just rub against
   the player. Sheet has attacks = []. Noita weakspirit.xml: GameEffectComponent PROTECTION_FREEZE, LuaComponent
   data/scripts/animals/spirit_aura_weak.lua (the aura is in Lua), AudioLoopComponent, audio animals/ghost.
4. Author: not all creatures were seen on screen (140 spawned per the log) — check spawn spots/visibility.

## Cloud: fixes after the author watched the 1b autotest
- Lasers: Noita's laser shots are ordinary projectiles drawn only by particles and sped up by negative air_friction
  (laserbeam -10, laser_spear -0.4, wraith laser -1). New projectiles columns drag / max_speed / particle (from
  VelocityComponent air_friction, terminal_velocity, first ParticleEmitter material). Shots.cs: negative drag speeds a
  shot up to max_speed (positive drag not applied yet: Brain.Aim ignores it, so 1a shots are unchanged); shots without an
  image leave a dust trail in their material's colour; NoitaArt ignores sprite "none" (the 9 "sprite none failed").
  Not fixed: drone_lasership's megalaser_blue.xml is a spawner (megalaser_blue_spawn.lua), its beam is not in the facts.
- Worms: enemies columns body_sprite / tail_sprite / segments / segment_spacing (the worm's own SpriteComponents and
  WormComponent part_distance); Carriers draws tail and body along Brain.Trail and the head turned to its velocity;
  Brain: a burrower's melee bites on contact (bodies overlap), not only within target_kill_radius.
- Spirits: aura rows <id>.aura from their LuaComponent spirit_aura_<effect>.lua (every 101 frames, data); attacks
  column effect (weak/slime/confuse -> BuffID.Weak/Slimed/Confused 3 s; berserk has no player effect yet, WARN);
  aura reach 6 tiles is a placeholder. Their movement_loop sound (AudioLoopComponent) is not played yet.
- Nests released nothing: spawned 10.2 tiles away, they noticed the player only at 9.4. Brain: a creature notices the
  player at least as far as its attacks reach; nest release range placeholder 15 tiles. Test added.
- Support: shield_buff gives the nearest Noita creature a shield (its next hit does nothing, Damage.cs); invisibility
  (scavenger_invis) makes it nearly invisible for 10 s.
- Autotest: TERRANOITA_AUTOTEST_SECONDS / pc_step -AutoTestSeconds (default 6); status lines mark OFFSCREEN (off the
  screen or inside tiles); "<id> starts <attack> at N tiles" lines show attacks that start but miss.
- Next (PC): `tools\pc_step.ps1 -AutoTest 1b -AutoTestSeconds 10`; then look at the 32 no-hit creatures with the new
  "starts" and OFFSCREEN lines.
- Wolf flew 150 tiles after a lunge: Noita's dash is 40 px/frame and in Terraria the leap never registered a landing.
  Brain: a lunge ends after its distance (the attack's range) or a few frames after it hits, then slows down. Test
  reproduces it (242 tiles before the fix).
- Creatures a hurt script releases (LuaComponent script_damage_received naming animals): giantshooter(_weak) ->
  slimeshooter, blob -> miniblob (miniblob moved to 1b), scavenger_leader -> its helpers. Rows <id>.split (retaliate
  with summons; cooldown 60 / count 1 are placeholders); Brain.Hurt releases them, Carriers.Summon spawns them.
- Sounds: 10 1b creatures have no sound folder in data.wak (duck, deer, elk, fish, fish_large, eel, hpcrystal,
  ethereal_being, nests) — silent in Noita too. Not done yet: looping sounds (AudioLoopComponent movement_loop of
  spirits, wraiths, drones, worms, tanks) — next.

## Handoff to a new cloud session (state at 7a1a880)
- Gate 1b CLEAN, gate 1a CLEAN; 46 Core + 7 tools tests pass; tables generated for 1b. Game side not compiled since
  the last fixes (lasers, worm drawing, auras, support, lunge end, splits) — the next PC run builds it.
- Waiting for the PC: `tools\pc_step.ps1 -AutoTest 1b -AutoTestSeconds 10`, then read pc_check.txt and
  pc_autotest_1b.txt (new lines: "<id> starts <attack> at N tiles", OFFSCREEN).
- Open: the 32 creatures that hit nothing in the last run; drone_lasership megalaser (spawn script, not in facts);
  looping sounds (AudioLoopComponent) not played; berserk aura has no player effect; 50 design placeholders listed in
  design/sources/placeholders_to_check.md (author decides what to do with them); 13 rows moved to 1c need facts.
- Melty: draft only, nothing to publish without the author.

## Cloud: tough enemies wait for hardmode (author's rule)
- Author: enemies with more than 1000 HP spawn only after the Wall of Flesh. Terraria life = noita_hp x tier hp_mult;
  `Spawning.Build` now skips pre-hardmode zones for them until `Main.hardMode` (`PreHardmodeMaxLife = 1000`).
  systems.json enemy_spawning notes the rule. Gate 1a/1b CLEAN, tools tests OK.
- Affected (11, all 1b): worm_big 14000, worm_skull 7500, ghost 6000, wraith 4425, crystal_physics 2400,
  lasershooter 1800, wraith_glowing 1475, skullfly 1260, wizard_hearty 1200, barfer 1200, skullrat 1110.
  Most are dungeon (Temple of the Art, tier t3) — the pre-hardmode dungeon gets fewer Noita enemies.
- проверить на ПК: game side builds (Spawning.cs changed), next pc_step run.

## PC step 4 (2026-10-06): stage 1b shipped by default, autotest 10 s each
- Entry.Stage default "1b". pc_step -AutoTest 1b -AutoTestSeconds 10: gate clean, builds ok, 141 spawned, 470 hits,
  0 ERROR, 1 WARN (berserkspirit aura: no Terraria effect yet). Commit 98e4a79.
- Author watched it: no damage / no visible shots from scavenger_invis (Хяйвехииси), scavenger_shield (Кильпихииси),
  coward (Раукка), cook (Коккихииси), miner_fire (Тулихийси, molotov), tentacler (Турсо), barfer (Турвонну вельхо).
  Log confirms 0 hits for all 7. Leads: invis/shield have only support attacks (Noita gives them guns? check);
  coward.teleportation; cook.sausages speed 3 lifetime -1; miner_fire.cocktail potion.png speed 3 lifetime -1;
  tentacler smalltentacle speed 0.4, freeze_circle sprite none speed 0; barfer.toxic_sludge_spit sprite none. For the cloud.
- Release 0.2.0 (terranoita-0.2.0.zip, Release build of 98e4a79+, stage 1b default) uploaded and submitted to the Melty
  draft: status draft, publishable, one click yes. Waiting for the author's Test in the Melty app, then publish.

## Loot, worms (2026-10-06, local session)
- Author: besides gold, the loot of a similar Terraria enemy from the same place. drops.json terraria_twin (stage 1b),
  Loot.cs: bestiary biome of where the player is (special biomes first, else Surface/Underground/Caverns), no bosses,
  critters, town, rare (npc.rarity > 0) or event/invasion enemies; pre-hardmode only damage < 40; one of the 3 nearest
  by max life; rolled with Terraria's ItemDropResolver on a stand-in NPC. Log: "loot of X like Y (id): items".
- Autotest kills each enemy before the next (loot tested); TERRANOITA_AUTOTEST_PLACES=1 moves the player through 12
  places (forest, snow, desert, jungle, caverns, ice, mushroom, marble, granite, underground desert, dungeon,
  underworld); test character has 1000 hp (author). Run: 107 kills, 101 loot rolls, 0 ERROR. Author saw Tattered Cloth
  (Goblin Scout) everywhere before places/filters; then umbrella slime/nimbus (rain), nymph, Doctor Bones: filtered.
- Worms (Move burrow, not eels): half of Noita's speed (author), Terraria's worm dig sound (sound 15 style 1, vanilla
  delay 10-20 by distance) while in the ground. To hear/feel: author in game.

## Stage 2 block physics, first part (2026-10-06, local session)
- Author: physics now (bosses later; laser guns after spells; cloud catalogues spells and wands). Decisions: loose
  blocks fall (dirt, clay, mud, ash, snow, silt, slush, grass as its soil); wood burns incl. the player's houses; the
  player's buildings without support collapse; clouds are weightless. After physics: all Noita liquids and gases with
  their effects.
- design/sheets/materials.json (tools/seed_materials.py; facts from the author's Noita data/materials.xml: powders =
  liquid_sand without liquid_static; burn_seconds = fire_hp / 100: wood 6 s, grass 1 s, fungi 0.6 s; [fire]+ice/snow ->
  water). hooks stage 2: tile_kill, tile_place, world_load, world_save, projectile_update. systems block_physics rule.
- src/Terranoita/Physics: Mats (tile -> material), Placed (player-placed tiles in <world>.wld.terranoita), Falling
  (disturbance queue; powders fall and slide down free diagonals; unsupported placed groups fall as one body; crush
  damage), Fire (spread, melt, lava ignites, water puts out, OnFire on player/NPCs), Blast (Noita explosions break
  tiles like Terraria bombs could, fiery ones ignite), PhysicsTest (TERRANOITA_AUTOTEST_PHYSICS=1). On by default;
  TERRANOITA_PHYSICS=0 turns it off. Torches/campfires do not ignite.
- Physics autotest: loose dirt 14/14 landed and piled (5 wide), hut on a pillar fell as one body and stands, wooden
  box burned (5/16 left), ice and snow melted to water, blast 18/36. Fixed: grains landing in the same cell vanished.
- Next: author plays it; then walls burning, Noita liquids and gases (Terraria has only 4 liquid types: needs a plan).
## Cloud: stage 3 groundwork (spells and wands), while the local session does stage 2 physics
- `tncli spells <noitaDir> <out.json>`: reads data/scripts/gun/gun_actions.lua (`Noita/GunActions.cs`, small Lua table
  reader) -> every spell's static fields + what its action function does (add_projectile, add_projectile_trigger_*,
  draw_actions, c.x +=/*= n, current_reload_time, extra_entities); anything else -> `unparsed`/`calls` (hand work).
  Also the projectile facts of every file the spells fire, and wand entities under data/entities/items/ (AbilityComponent
  + gun_config + gunaction_config + LuaComponent scripts). Checked here only on a made-up data.wak.
- `tools/apply_spells.py` -> new sheets `spells.json` / `wands.json` (stage 3, empty until the PC run). Spells whose
  function does more get `port = hand` + `_unverified` (gate 3 stays open for them). Wand stats missing from the entity
  (set by a script) stay unfilled with the script named.
- `Spells/Gun.cs`: Noita's deck/hand/discard cast loop, pure logic: spells per cast, mana (skip if not enough), uses,
  modifiers/multicast share one shot, triggers carry a payload shot, wrap mid-cast + recharge, shuffle (seeded),
  always-cast free, cast delay = max(cast delay, recharge). `Spells/FromSheets.cs`: sheet rows -> Spell/Wand.
  Tests: SpellTests (8), test_apply_spells (5). 55 Core + 12 tools tests pass; gate 1a/1b CLEAN.
- pc_step.ps1: new non-fatal step "spell facts from Noita" + "apply spells"; commits design/sources/noita_spells.json.
- проверить на ПК (next pc_step): spell facts run on the real gun_actions.lua; then the cloud reads noita_spells.json:
  how many spells are `data` vs `hand`, which wands have fixed stats.
- To check against the player's gun.lua (`tncli wak-cat <noita> data/scripts/gun/gun.lua`, not copied into the repo):
  skipped-card redraw rule, payload cast delay adding to the wand, wrap order, cast delay vs recharge.
- Not done (needs author decisions): wand slots UI, how Terraria gets wands (drops/shops/start), Noita projectiles as
  Terraria projectiles for the player.
- Burning player and NPCs (onFire) light the burnable tiles they touch (author: a burning player did not set wood alight).
- Background walls burn (materials.terraria_walls: wooden, grass, flower, jungle, mushroom, leaf walls): blocks and the walls behind them light each other, fire creeps along walls at 0.7x. Physics autotest: wooden wall 47/49 burned with the box, the rest as before.
- Noita liquids and gases catalogued: tools/extract_liquids.py (from the author's materials.xml) -> design/sheets/
  liquids.json (128: 102 liquids, 26 gases, colours, density, burnable, touch/ingestion effects, freeze/melt) and
  reactions.json (260 reactions involving them). Not simulated yet.
- Author asked to import Noita's buffs/debuffs: design/sheets/status_effects.json (tools/seed_status_effects.py from
  status_list.lua: 33 effects with Noita icon, name, description, harmful, protects_from_fire, our mechanic).
  Physics/Status.cs: Noita icons after Terraria's buff icons (hover: Noita name and description in the game's
  language, seconds left), effects by our code (flags in Player.UpdateBuffs postfix, damage/heal over time in
  UpdateLifeRegen prefix). Enemy fire/poison shots and burning tiles now give Noita's ON_FIRE / POISONED. Wet, oiled,
  slimy... put out and keep off fire. Test: 10 effects shown with icons, hp and speed change as expected.
- Noita liquids and gases simulated (Physics/Fluids.cs): own sparse layer over Terraria tiles (0-255 per tile);
  liquids fall and level out, heavier sink under lighter, gases rise (~7 tiles/s) and hang as clouds, fading by
  lifetime as a share of what is there; burnable ones (oil, alcohol) catch fire from burning tiles/liquids and lava
  and burn down; reactions.json run between cells, Terraria water/lava, blocks (as their Noita material:
  noita_solids.json, materials.noita_material, other tiles = rock_static) and air, a portion (48) at a time; eating
  a block uses 24 of an unchanged liquid (acid). Touch: Noita status effects (player), Terraria buffs (NPCs);
  viscous liquids slow the player. Author: gases reaching space and liquids reaching the underworld vanish.
- Sources: Noita creatures bleed their material (enemy_blood.json from facts DamageModelComponent; default
  blood_fading): a little per hit, a pool on death (lava blood only on death, 128). Shots of a liquid/gas material
  leave 90 where they land. Ctrl+Shift+K picks a liquid, Ctrl+Shift+L pours it at the mouse.
- Author: obsidian, metals (ores, bars-bricks, plating), lihzahrd brick are not eaten by liquids (materials row
  dense = Noita rock_hard, no [corrodible]); glass neither (Noita glass). Test scenes now have a background wall.
- Physics test: acid ate 9 dirt and the stone floor under it and ran down (25-33 blocks per 1020 acid), oil basin
  burned out in < 12 s, slime sank under oil, smoke hung ~12 s. Fixed: gases jumped a whole column per tick (now a
  cell moves once per tick), thin gas vanished (fade by share).
- Author's idea (asked, not done): acid slowly turning what it touches into a block it cannot eat, which poisons
  on touch. Not in Noita's data (lava + toxic sludge/poison make toxic/poison rock, no touch effect, corrodible).
- Liquid gallery (TERRANOITA_AUTOTEST_LIQUIDS=1): 99 closed obsidian boxes in the caverns, one Noita liquid/gas
  each (+ Terraria water and lava), signs with names, torches, stone background, Nightmare Pickaxe; nothing applied
  to the player, game stays open (author plays it). Author: our liquids looked opaque and behind the player; now
  drawn after the players (hook fluids_draw) and see-through like Terraria's water (alpha <= 150).
- Author: some effects cancel others in Noita. Noita's data has no such table (status_list.lua only has
  protects_from_fire and remove_cells_that_cause_when_activated; exclusivity_group is used by one creature): stains
  are pixels of material on the sprite (SpriteStainsSystem in noita.exe), so a new liquid covers the old. Done:
  a new stain ends the other stains (water washes off oil, slime, sludge); protects_from_fire was already in.
- Author's gallery notes, fixed: Noita's "fire" (liquid_fire, en "fire", oil's CellDataChild with on_fire=1) took
  oil's OILED stain, which kept its own fire off: on_fire liquids now burn forever (no burn-down at fire_hp 1e6) and
  touch = ON_FIRE (a burning liquid burns off fire-proof stains). Freezing liquid (blood_cold, vapour): Noita acts
  only when drunk; author wants a slowdown on touch: new effect CHILLED (50% speed, Noita's freezing icon).
  Invisibility: the player is not drawn at all and Noita enemies lose sight of them. Polymorph: drawn as a Noita
  sheep (random creature; unstable changes every 3 s), and the liquid that caused it is used up (Noita
  remove_cells_that_cause_when_activated, sheet column removes_cause). Empty boxes were gases and fading liquids
  (Noita lifetimes): the gallery tops boxes up every 10 s. Teleportatium took the author out of the gallery:
  Ctrl+Shift+H brings them back.
- Liquid audit (TERRANOITA_AUTOTEST_AUDIT=1, design/sources/pc_liquid_audit.txt): the player sits 1.2 s in each of
  the 99 gallery boxes; one AUDIT line per material (effects, hp, teleported, form, died, position, touching), then
  leaks. Found and fixed: pouring into a full cell spread through walls (gases "through blocks"): now a flood fill
  over connected open tiles; reactions took a bare material name for a tag (magic liquids carry [water] and reacted
  as water: invisibility vanished): names now match only the material; Terraria's water gives WET; the audit
  clock counts world updates (Terraria pauses an inactive window) and fills each box just before going in.
- Noita touch damage (player_base.xml materials_that_damage, units/frame x 25 hp): acid, lava, cursed liquid,
  poison, freezing liquid, toxic gas hurt, healing gas heals; toxic/poison/cursed rocks have it too (noita_solids).
  Author: instant deathium kills. Hurt sound (Terraria's) when effects or liquids take hp, at most twice a second.
- Author: only opposite effects replace each other, the rest stay together (status_effects.cancels: fire vs
  wet/cold, water washes oil/blood/slime/urine, speed vs slow, healing vs poison, berserk/protection vs weakness,
  one polymorph, one teleportitis). Hovering a liquid shows its Noita name if the player touched it, else ???
  (known list per character in %LOCALAPPDATA%/Terranoita/known_<name>.txt). Random polymorph: walking/flying
  creatures only. Fluid update reuses one key list (no garbage per tick).
- Hover name: computed under the mouse (log shows it) but not visible in game after three tries (MouseText,
  MouseTextHackZoom in DrawMouseOver, DrawBorderString after DrawInterface_36_Cursor). Author: leave it for now.
- Author: Noita liquid pools in the caves, kept simple. design/sheets/cave_pools.json (ground snow/jungle/desert or
  depth dirt/cavern/deep -> liquids, pools per 1000 tiles of width); Physics/CavePools.cs fills a hollow's floor wall
  to wall, up to 4 rows. Done once per world: the first load without <world>.wld.fluids (new or old worlds). Our
  liquids are now kept with the world in that file (material names). Not run in game yet.
- Release 0.3.0 (author: post the update): 0.2.0 turned out to be live already (published by the author; 53 gets,
  40 players), so this is 0.3.0: terranoita-0.3.0.zip (Release builds, README), sha256 2d3706c5...; submitted as a
  draft, one click yes. Before: world load guarded (our files and cave pools can never stop a world loading), cave
  pool tries x80 (small world: 38 pools, 1340 cells; scales with world width), physics autotest clean. Next: the
  author presses Test in the Melty app, then publish.
- Melty page updated (author: the description must say liquids, effects, gravity): update_mod tagline + description
  (v0.3: creatures, block physics, liquids and gases, Noita effects, debug keys N/K/L, back up worlds). It describes
  0.3.0 while 0.2.0 is still the live release, until the author tests and publishes 0.3.0.

## 2026-10-07 FPS drops near caves (author, 0.3.0)
- Liquids: reaction lookups no longer make new strings and tag sets every tick (cached); cells two screens away from the player move every 8th pass.
- Per-hit creature/shot logs only in tests.
- Built, copied to the Terraria folder; the author checks FPS by the caves, then 0.3.1 on Melty with permission.


## 2026-10-07 0.3.1 (small update)
- FPS by the caves measured on the author's world test1 (PERF test, TERRANOITA_AUTOTEST_FPS=1; TERRANOITA_AUTOTEST_WORLD picks a world): liquids were ~3.9 ms per frame and ~40 garbage collections a second; after caching reaction lookups ~0.8 ms and ~7.
- Far cells (over two screens away) only fall and fade every 32nd pass, scaled; no reactions there (acid and poison pools were boiling off into thousands of thin gas cells).
- Cave pools x3 (cave_pools.json per_1000_tiles; author saw none). Pools version kept in the .fluids file: 0.3.0 worlds get the extra pools once (test1: +187 pools, ~6000 cells).
- Loose soil caves in only around the broken block: 3 tiles up over it, each column further out a random 0 or 1 lower (a ragged staircase, author), Falling.Reach.
- Per-hit logs only in tests.
- 0.3.1 submitted to Melty as a draft (terranoita-0.3.1.zip, 934983 bytes, sha256 2fd57554...f85f2); live once the author presses Play on it in the Melty app.


## 2026-10-07 bugs from the author
- Fish and lampreys spawn only in water (Spawning.Swims/Water).
- Respawn clears all Noita status effects (hook player_spawn).
- Not in the 0.3.1 draft yet.


## PC: spell facts (2026-10-07)
- tncli spells on the author's Noita: 422 spells, 204 projectile files, 113 wand entities (design/sources/noita_spells.json).
- Parser (Noita/GunActions.cs) now reads Noita's common shapes: shot_effects.x +=/= n (recoil, 51 spells), the guards
  after a multiplier (if c.x >= 20 ... / if c.x < 0 ... -> clamps), gun.lua constants (ACTION_DRAW_RELOAD_TIME_INCREASE);
  apply_spells: numeric c.x = n -> config_set, game_effect_entities -> game_effects, mana nil -> 10 (ACTION_MANA_DRAIN_DEFAULT).
  Test ReadsRecoilGuardsAndConstants (made-up snippet).
- spells.json: 361 by data, 61 by hand (real logic: random, recursion, deck tricks, entity calls).
- wands.json: WAND_ATTRS names match the real entities; 18 wands with fixed stats (base_wand_level_1, wand_001..017),
  the rest are made by scripts (stats unfilled, script named). spread/speed_multiplier are set in only 18 entities.
- Gun.cs vs gun.lua, fixed: the wand's own draws do not wrap (instant_reload_if_empty false: the cast ends);
  recharge time adds up across casts until a recharge (current_reload_time); uses spent at the end of the cast and only
  if it fired something or the spell is other/utility, a spell with no uses leaves the deck (move_hand_to_discarded);
  always-cast: no mana except mana-giving ones, a modifier's draw_actions(1) draws nothing (SPECIAL RULE).
  Kept: skipped-card redraw (same as draw_actions), payload cast delay adds to the wand (gun.lua passes every shot's
  state to the game), wait = max(cast delay, recharge). Not in gun.lua: when the wand's own draw finds the deck empty
  gun.lua sets reloading and the game recharges; we recharge after that cast. Tests: 4 new (60 Core, 12 tools pass).

## 2026-10-07 worms and toxic ground (author)
- Worm bodies are NPCs (carrier type, HeadOf table, realLife = head): every segment can be hit, the hit goes to the
  head (Damage.Strike), buffs caught by the body go to the head, one health bar (npc_health_bars), body named as the
  worm, segments go with their head (Carriers.Sweep). Only the head bites (Noita). Autotest TERRANOITA_AUTOTEST_ONLY:
  all 7 worms have their segments, a hit on the middle one takes the head's life.
- Worm sprites face left in Noita: head and body turned half round (author: heads back to front). Not seen in game yet.
- Toxic ground (author's Noita screenshot): noita_solids.touch_effects; the natural blocks around toxic sludge and
  poison pools become rock_static_radioactive / rock_static_poison (ToxicGround, kept in the .fluids file; 0.3.x worlds
  get them once, pools version 3), also lava + sludge. Green glow on open sides; touching gives RADIOACTIVE / POISONED
  and Noita's touch damage. test1 copy: 1541 toxic blocks. Not seen in game yet.

## 2026-10-07 stage 3: Noita's own spell code in Terraria (author: "take Noita's system, its code")
- MoonSharp (MIT, author allowed the download) runs the player's data/scripts/gun/gun.lua + gun_actions.lua as is:
  Core/Noita/LuaGun.cs plays Noita's engine (BeginProjectile, triggers, RegisterGunAction, StartReload...), LuaWorld
  answers the scripts' world questions (enemies near, caster hp, gold, held wand). All 422 spells run (tncli lua-all).
  Gun.cs and the hand-port list are no longer the plan.
- LuaWandMaker runs Noita's wand scripts (starting_wand.lua, wand_level_XX.lua -> gun_procedural.lua). Two traps:
  numbers must be printed with the invariant culture (Russian Windows: "183,33"), and pairs() must walk the list part
  first as LuaJIT does (get_gun_probs relied on it). LuaCulture.cs.
- Items: spells and wands are Terraria items on unused item types (spike: deprecated types are dropped on load unless
  ItemID.Sets.Deprecated is cleared; type + stack survive inventory and chests; the prefix byte is patched to hold
  the spell/wand number). Wands in %LOCALAPPDATA%/Terranoita/wands.txt, spell numbers in spell_numbers.txt.
- Casting (Magic/Casting.cs): held wand + use button -> LuaGun.Cast with Terraria mana (author); SpellShots from
  spell_projectiles.json + the shot config. Wand window: key U, Noita's UI pictures, held wand + 4 wand slots,
  [Noita]/[Terraria] look switch (author). New characters get Noita's two starting wands.
- MAGIC autotest: starting wands given, bolt staff kills a zombie, bomb/trigger/divide/homing/black hole/grenade/
  fireball/acid wands cast without errors. Wand window not seen yet (minimized).

## 2026-10-07 HANDOFF (local session -> new chat)
Done today, in order: 0.3.1 draft on Melty (FPS, 3x pools, staircase cave-ins; NOT yet with the fixes below), fish only
in water, respawn clears effects, worm bodies (hittable segments), worm sprites turned round, toxic ground, spell facts
+ gun.lua comparison (cloud task), then stage 3 on Noita's own Lua (see the section above).

Stage 3 state:
- Works (MAGIC autotest): starting wands (Noita's starting_wand.lua / starting_bomb_wand.lua) given to new characters,
  casting through gun.lua with Terraria mana, spell shots hit and kill, bomb/trigger/divide/homing/black hole/grenade/
  fireball/acid cast without errors. World loot on first load: 25 cave wands (levels 1-6 by depth), 223 spells in
  167 chests (test world).
- Fixed, NOT yet verified in a run: wand number lost on load. Cause: Item.FixAgainstExploit (on every loaded item)
  calls ResetPrefix when !CanRollPrefix -> patch magic_item_rollprefix. Also both Item.Prefix overloads are patched
  (the second takes out bool). The MAGIC test now saves via Main.ActivePlayerFileData.Player = p (the file's player
  is the menu copy). Check: run scratch magictest twice; 2nd run's "hotbar raw" must show 6143:N with N > 0.
- New, NOT yet run: Screenshot.cs (tests): one frame drawn into our render target (Reach profile cannot read the back
  buffer), saved to %LOCALAPPDATA%/Terranoita/shots/*.png; MagicTest asks for held_wand, window_noita,
  window_terraria, black_hole. Look at them (Read the png) before asking the author.
- Damage: projectiles add damage_by_type (fire, ice, slice...), fireball/grenade explosions hurt the caster
  (explosion_dont_damage_shooter = 0, Noita); creatures touching damaging liquids (acid...) take Noita's touch damage.
- Held wand: composite front arm stretched to the aim, wand drawn in that hand (author said it looked silly; check
  the held_wand screenshot).
- Black hole: eats the ground (CellEaterComponent) — Noita's small black hole does not damage creatures; big/giga
  do (AreaDamageComponent, BlackHoleComponent). Told the author; waiting whether to add damage to the small one.
- Lua state per wand is made in the background (Task) — the FPS drop mid-test was gun_actions.lua being parsed on the
  game thread.

Next steps (author's order: finish magic, then shops):
1. Verify the two items above (wand number after reload, screenshots); fix what the pictures show.
2. Wand window by eye (key U, Noita look / Terraria look switch); wand slots behave like ammo/coin slots.
3. Uses of limited spells (UsesChanged) shown in the window; spells with 0 uses greyed.
4. Extra entities beyond homing (extra_entities: trails, explosions on hit...), lasers/clouds (34 projectiles without
   ProjectileComponent), LuaWorld.Load (EntityLoad: summons, ALL_SPELLS).
5. Later, author: two traders (spells, wands) for Noita gold.
6. A new Melty release only with the author's permission (0.3.1 draft is still waiting for Play).

## 2026-10-07 late: magic test run 1 (new chat)
- Wand number "lost" after reload: the test's own wands were never written to wands.txt (WandStore.Save missing in
  MagicTest), so the saved character pointed at numbers the store did not have; the test crashed on it. Fixed (test
  saves the store; the probe wand is no longer added to the store; log line null-safe). The prefix itself survives
  (hotbar raw 6143:9, round trip OK).
- Wand window: limited spells show uses left; used-up spells greyed. Not seen in game yet.
- Run 1 worries (check next): sets 2 and 3 cast (mana spent) but "spell shots 0" and the zombie not hurt.
- Screenshots saved (held_wand, window_noita, window_terraria, black_hole): NOT looked at yet.
- Next: run 2 (hotbar raw must show 6143:N and the test wand found), look at the 4 pictures, then HANDOFF steps 2-4.

## 2026-10-08 all of Noita's wands (author: all magic, all wands, edit spells any time)
- LuaWandMaker.MakeEntity(wand xml): runs the scripts of the Base chain, then the wand's own; unique wands (ruusu,
  kiekurakeppi, leukaluu, valtikka, vasta, vihta, petri, arpaluu, varpuluuta) take name/picture from ItemComponent.
  ComponentGetValue returns "" for unset fields (Noita: always a string) -> gun_procedural_better.lua and petri run.
  tncli lua-wand takes a .xml too. All checked offline with tncli.
- Cave wands: level by depth; 30% unshuffle, 10% better, 4% a unique wand (base level <= level+1), level 10 /
  unshuffle 10 in the underworld (30%).
- Spells: all 422 run, every projectile file has a row. Gap: 125 extra_entities of modifiers (only homing done);
  70 of them are LuaComponent scripts (63 scripts, 42 API calls: transform, velocity, components, tags) -> plan: run
  Noita's own shot scripts per projectile (one Lua state, entity = shot). Rest: HitEffect 19, particles/trails 16,
  MagicConvertMaterial 8, Arc 4, Lightning, EnergyShield, AreaDamage, CellEater. Plus material/utility spells and
  EntityLoad summons (LuaWorld.Load not done in game).
- проверить на ПК: unique wand pictures (data/items_gfx/wands/custom/*.png) and names in game.

## 2026-10-08 spell modifiers, game side (while the cloud builds LuaShotScripts)
- SpellShots.Extras.cs: components of a shot's extra_entities files and of its own projectile file (read at run time
  from the player's Noita): HomingComponent (all variants: anti, boomerang/target_who_shot, homing_wand, rotate),
  SineWave, Arc (between arc shots of one cast; lightning hurts, fire burns, poison/gunpowder spilled),
  MagicConvertMaterial (Fluids.ConvertMaterial, Terraria water/lava too), particle emitters (coloured dust),
  Light, CellEater, AreaDamage, EnergyShield (stops enemy shots, Shots.StopNear), BlackHole (pull + hurt),
  MaterialSeaSpawner (seas), TeleportProjectile (caster appears where it ends), HitEffect CRITICAL_HIT_BOOST
  (wet/oiled/burning/bloody), game_effect_entities statuses on hit (fire, wet, oil, poison, frozen, bloody),
  trail_material (fire/water/oil/acid/poison/gunpowder...).
- EntityLoad from spell scripts (TerrariaWorld.Load -> SpellShots.LoadEntity): projectile files -> shots,
  data/entities/animals/* -> our creatures. Shot entity ids = 100000 + shot id.
- Not yet (logged once in game as "not done yet"): Lua scripts of shots (cloud task), HitEffect LOAD_CHILD_ENTITY
  (curse, petrify, gravity field...), statuses without a Terraria buff (necromancy, disintegrated...), lasers.
- проверить на ПК: none of this has run in the game yet.

## Cloud: LuaShotScripts — Noita's per-projectile scripts in Core (task design/cloud_task_shot_scripts.md)
- `Core/Noita/NoitaEntityXml.cs`: entity file -> XmlEntity (name, tags, `_Transform`, components, children) on top of
  NoitaEntity's Base merge; components flattened (`_tags`, `_enabled`, nested objects -> "config_explosion.damage").
  `ComponentFieldTypes` reads field types from tools_modding/component_documentation.txt (bool / number / string / vec2):
  without it "0" of a bool field comes back as the number 0 (true in Lua), so the game should pass the docs.
- `Core/Noita/LuaShotScripts.cs`: entity/component store + one MoonSharp Script (LuaCulture.Enter + Prelude, each file
  compiled once, run as a chunk per execution). `IShotHost` (+ `ShotHostBase` with no-op defaults) backs root shots:
  position, velocity (VelocityComponent.mVelocity of a host entity), host fields, Kill, InRadiusWithTag, HitboxCenter,
  Raytrace, Load, HerdRelation, FrameNum, Screenshake, CameraPos.
  Game API: `CreateShot(projectileFile)`, `AttachExtra(shot, xml)`, `Spawn`, `Update(frame)`, `Fire(entity,
  "script_death", args...)` (calls the event function: death, collision_trigger, item_pickup, else the field name
  without "script_"), `Components(entity, type)` incl. children, `ChildrenOf`, `Alive`, `Forget`, `Missing`, `Errors`, `Log`.
  Rules: script_source_file every execute_every_n_frame (first run n frames after added; -1 = only on added),
  execute_on_added (at once; a CreateShot's own ones on the next Update, after the game placed it), execute_times,
  remove_after_executed, mTimesExecuted/mLastExecutionFrame, `_enabled`; LifetimeComponent kills at
  creation + lifetime; dead entity -> children dead; script error -> logged once per file, component disabled.
  Children with InheritTransformComponent report the parent's transform.
  All APIs of the task list are implemented (plus Randomf, EntityRemoveTag, EntityGetIsAlive, EntityGetFilename,
  EntityGetTags, ComponentGetValueInt/Float/Bool, ComponentGetEntity/TypeName/HasTag, RaytraceSurfaces/Platforms).
- tncli `shot-script <noita> <extra_entity.xml> [frames] [projectile.xml]` (projectile default
  deck/light_bullet.xml): fake shot right at 300 px/s, position/velocity every 10 frames, host events, missing APIs,
  errors (exit 1 on errors). Docs from <noita>/tools_modding/component_documentation.txt when present.
- Tests: LuaShotScriptsTests (9): sine wave via mVelocity, every-n/execute_times/remove, EntityAddComponent lifetime
  kills shot + children, broken script disables only its component, missing API, Base merge + object fields, Fire,
  doc-typed bool, host fields/transform, on-added waits for the game. 69 Core tests pass; gate 1a CLEAN.
- проверить на ПК: `shot-script` over the 125 extra_entities files (Missing list, errors); event function names for
  rarer script_* fields against Noita's docs; whether execute_on_added counts toward execute_times in Noita;
  Random(a) range (0..a assumed).

## Cloud: review of the magic commits (design/review_magic_2026-10-08.md)
- 9 findings in game code (not changed by the cloud): spell uses refill via the hand memory, stale NPC status marks,
  crit-on-status condition, cave wand files unchecked, flat extra_entities reader, homing on critters, EntityLoad
  velocity, ConvertMaterial to unknown material, a moved doc comment.
- LuaShotScripts: own ids from 1,000,000; game ids (creatures, shots) go to the host. 70 Core tests pass.

## Cloud: review of the live code (design/review_physics_2026-10-08.md)
- Most important: fluids/placed save is not atomic and can race Terraria's background autosave; a truncated
  .fluids file makes the next load regenerate cave pools on top of the old ones. Then: big building landing ->
  repeated 4000-tile flood fills (freeze), placed blocks lost without items, fire NPC check O(tiles x NPCs).

## Cloud: progress book (Noita's Progress menu, per character) — design/progress_window.md
- Author: known per character; creature when killed, liquid when touched, wand/spell when taken.
- Core `Progress/ProgressBook.cs`: categories spell/creature/liquid/wand/item/perk, See/Count/Has/CountOf, Page (the
  game's full list in order + unknown flags), text file (tab separated, bad lines skipped), FileFor (by player file
  name, Windows-safe), atomic Save (tmp + File.Replace), Discovered event. ProgressBookTests (4); 74 Core tests pass.
- Game part (hooks, window, key O, migration of known_<name>.txt) is for the PC session: see the design file.
## 2026-10-08 PC: Noita's shot scripts in the game (after the cloud's LuaShotScripts)
- Sweep (tncli shot-script, 120 frames): all 125 extra_entities files run with no errors; 80 spell projectile files
  with Lua: errors came from EntityGetWithTag returning nil (Noita: empty table), unset fields returning nil (Noita:
  the documented default -> ComponentFieldTypes.Default from component_documentation.txt), and missing
  GameShootProjectile, GameGetSkyVisibility, GamePlaySound, ProceduralRandomi, RandomDistribution(f),
  EntityGetClosestWithTag (+ no-op PhysicsApplyForce(OnArea), inventory, worm attractors). All added
  (LuaShotScriptsApiTests). Store entities start at LuaShotScripts.FirstEntity (1000000), lower = the game's;
  EntityLoad: the game first, else the file lives in the store, max 3000 (wall spells copy themselves).
- Checked against Noita's docs: event function names (death, collision_trigger, item_pickup, shot, ... = field
  without "script_") as the cloud did; Random(a) = int 0..a; execute_on_added vs execute_times not documented (kept).
- Game (SpellShots.Scripts.cs, GameShotHost): a shot whose projectile file or extra entity carries a LuaComponent
  becomes CreateShot + AttachExtra; Update each frame; script_death + Forget when it ends; scripts can kill shots,
  move them, read/write ProjectileComponent lifetime/damage/bounces_left/mWhoShot. Entities: 1 = caster,
  1000 + whoAmI = creatures. EntityLoad: spell projectile -> shot (in the store so GameShootProjectile can aim it),
  creature -> our creature, data/entities/items/wand* -> a wand made by LuaWandMaker.MakeEntity, dropped.
  Component docs read from <Noita>/tools_modding/component_documentation.txt.
- MAGIC test: new sets (sine wave + fire trail, electric arc, orbit discs + spiraling (scripts), wall, teleport);
  screenshots arc, orbit_scripts.

## Cloud: physics review fixes (design/cloud_task_physics_fixes.md), game code not built here
- 1 Save/Load: `Physics/SaveSync.cs` (Gate lock + WriteAtomic: tmp then File.Replace/Move + SetAside .bad).
  Fluids/Placed/ToxicGround change under Gate (UpdatePatch body, Fluids.Add/ConvertMaterial/Ignite/Clear/Load,
  Placed.Add/Remove/Load, ToxicGround.Mark/Remove/Clear); saves copy under Gate, write outside it.
  Fluids.Load: PoolsVersion only after a complete read; a broken file -> kept as .bad, cells read so far kept,
  PoolsVersion = CavePools.Version (no pools poured again). Placed.Load: same, keeps what was read.
- 2 Falling: `Checked` set (tiles found held this frame; cleared each frame, on Start and Land).
- 3 Falling: placed blocks that land on a taken tile, fail to replace one, or fall out of the world drop their item
  (tile -> item table built once from every item's createTile/placeStyle; platforms by frameY/18; none -> lost).
- 4 Fire: burnable NPCs collected once per tick (active, !friendly, !dontTakeDamage, !OnFire immune), a caught one
  is not checked again that tick.
- 5 PlaceTilePatch in try/catch; Land moves Terraria liquid up (first free tiles, up to 4) instead of deleting it.
- Magic review 8-9: ConvertMaterial changes nothing when `to` is unknown (not a liquid, water, lava or air);
  Ignite and ConvertMaterial have their own summaries.
- Checked here: C# 7.3 syntax of the changed files (Roslyn), no build (needs Terraria.exe).
- проверить на ПК: build; game_test -Mode physics; autosave while liquids flow (no "Collection was modified" in the
  log); a cut .fluids file -> .bad and no new pools; a building landing on blocks drops items.
- вопрос локальной сессии: confirm WorldGen.saveAndPlay runs WorldFile.SaveWorld on the ThreadPool
  (`TN_IL=1 tncli tr-methods Terraria.exe WorldGen saveAndPlay`).

## 2026-10-08 PC: magic review, spell slots, progress window, physics fixes checked
- Answer to the cloud: WorldGen.saveAndPlay and SaveAndQuit call ThreadPool.QueueUserWorkItem -> *CallBack ->
  WorldFile.SaveWorld (tr-methods IL). The physics test now calls WorldGen.saveAndPlay() while liquids flow.
- PHYSICS run (cloud's fixes built here): a .fluids cut in half -> "ERROR in fluids load" + kept as .bad, no pools
  poured again; autosave with 6708 cells -> no "Collection was modified", new file written, no .tmp left. Physics
  checks as before (dirt/pile/hut/box/smoke/statuses).
- Magic review: 1 uses ride on the spell item (ConditionalWeakTable; tooltip "Uses n/max"), 2 marks keyed by NPC
  type and dropped when the NPC is gone, 3 crit conditions each on its own ("" / NONE = none), 4 cave wand file
  falls back to wand_level_0N, 5 extras read by NoitaEntityXml, 6 no homing/black-hole pull on critters or
  untouchables, 7 EntityLoad'ed projectiles start at rest; 8-9 done by the cloud.
- Wand window right of the coin/ammo slots (x 580, author); 16 spell slots in 2 columns left of the equipment
  (players/<name>.spells, "ID:uses"), shown with the inventory.
- Progress window (ProgressWindow.cs): key O (free in Terraria's defaults) + button by the Bestiary; tabs spells,
  creatures, wands (data/items_gfx/wands pictures), liquids; Noita's progress_menu boxes, unknown = dark silhouette;
  kills counted in npc_loot when the player hit it, liquids from Fluids.Learn (old known_*.txt moved in), wands and
  spells by a scan of what the player holds every 30 frames; "+ name" text on discovery; saved every 2 min, on close
  and on leaving the world. Drawn as a prefix of DrawInterface_33_MouseText (layer 31 is not always drawn).
- MAGIC run: wand number kept after reload (test 11 found); zombies killed in sets 1,2,4,6,8,9; no errors; game at
  30 FPS from the black hole set to set 7 (ground eaten -> support checks; cloud's Falling fix was not in that run).
  Not done yet (logged): EntityLoad of orbit_discs_disc.xml, wall_builder/piece/sound.xml (not in spell_projectiles).
- Cloud task now: design/cloud_task_core_2.md (faster LuaShotScripts, progress tooltip lines).
