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
