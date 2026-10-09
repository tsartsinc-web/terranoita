# Magic like Noita: plan (2026-10-09, PC; author: "magic is the core of the mod, it must work as in Noita")

Read this first in every magic session. Status of each phase is in design/tasks.md (row PC-21).

## 1. What is broken, measured (2026-10-09)

1. **Casting is fine.** Noita's own gun.lua runs in MoonSharp (src/Terranoita.Core/Noita/LuaGun.cs). `tncli lua-cast`
   gives Noita's results for BURST_2, SCATTER_4, ADD_TRIGGER (payload on hit), DIVIDE_2, MANA_REDUCE (+30 mana),
   HOMING (extra entity homing.xml), TENTACLE, ROCKET_TIER_3. Draw-many, triggers, modifiers and mana are decided
   correctly. What goes wrong comes after the cast.
2. **Shots are silently dropped.** `SpellShots.Fire` returns without a word when 600 shots are alive
   (`if (Live.Count >= Max) return;`, src/Terranoita/Magic/SpellShots.cs). In the author's session wall spells
   (wall_piece.xml: speed 0, lifetime 400) filled the 600, and every spell cast after that made nothing:
   no shot, no log line (TENTACLE and ROCKET_TIER_3 have no line in latest.log at all).
3. **Most of Noita's projectile engine is missing.** A Noita projectile is an entity of components; the engine
   (closed C++) runs each component type. Our `Shot` (SpellShots.cs) is a hand-made class with ~25 fields; it
   handles 26 of the 72 component types that spell entities use. Not run at all (number of spells using it):
   HitEffect 22, DamageModel 29 (hittable bombs, mines), GameAreaEffect 14, PhysicsBody 12, PhysicsThrowable 11,
   ElectricCharge 8, ExplodeOnDamage 7, ExplosionComponent 6, VerletPhysics 4 + VerletWeapon 3 (TENTACLE,
   BLOODTENTACLE, WORM_SHOT), LaserEmitter 3, MaterialSucker 3, MaterialInventory 3, PhysicsBody2 3, Levitation,
   Teleport, ShotEffect, AudioLoop 62, the summons' creature components (DRONE, FISH, EXPLODING_DEER,
   SUMMON_WANDGHOST).
4. **Even ProjectileComponent is half done.** Spell files set 70 of its fields; our code reads 23. Not read
   (files using it): lob_min/lob_max 146, physics_impulse_coeff 68, damage_game_effect_entities 22 (statuses on hit),
   collide_with_shooter_frames 25, bounce_always 17, on_collision_remove_projectile 14, collide_with_entities 10,
   velocity_sets_rotation 36, damage_scaled_by_speed 5, collide_with_tag 5, bounce_at_any_angle 5,
   go_through_this_material 2, spawn_entity 2, on_collision_spawn_entity 1, ground_penetration_* 3, plus visual
   ones (muzzle flash 87, light flash 116, shell casing 23, on_death_gfx_leave_sprite 160, ragdoll/hit particles).
5. **Modifier fields not applied:** child_speed_multiplier (22 spells), dampening (8), material_amount (6),
   explosion_damage_to_materials (2), pattern_pos_offset / rad_pattern_degrees_offset (1 each).
6. **The test lies.** The spells test (src/Terranoita/Magic/SpellsTest.cs) says OK when a cast made any shot and the
   target lost any hp: 416/422 "OK" while the author finds most spells wrong. It never compares behaviour with Noita.

What is open in Noita, and what is not: the Lua (gun.lua, gun_actions.lua, ~1000 scripts), every entity XML, and
tools_modding/component_documentation.txt (every component's fields, types, defaults, some comments) are open.
The engine that runs the components is not. Component behaviour is rebuilt from those files, from what the Lua
scripts do with the fields, and from checks in Noita by the author when a rule cannot be read anywhere.

## 1b. Author's conditions (2026-10-09)
- The component path sits behind a switch; it goes into a release only when the coverage tool shows at least as many
  spells working as the current path. Players never lose anything.
- Ground truth is a Noita Lua test mod (PC-22) run in the author's Noita, not hand checks and not memory.
- Every session reports "spells matching Noita / total"; every claim is tagged verified in game / built only / assumed.

## 2. Approaches

- A. Keep fixing spells one at a time inside SpellShots. This is how it got here: every spell a special case, no
  measure of what is missing, fixes break other spells. Rejected.
- **B. A Noita component runtime (chosen).** Every shot is a Noita entity: its XML (with Base files and the
  component docs' defaults) loaded into one entity/component store, the same store the Lua scripts already use
  (LuaShotScripts in Core keeps entities, components and their fields). Behaviour lives in one C# system per
  component type that reads and writes those fields every frame, as Noita's engine does, so a Lua script that
  changes a field changes the behaviour. The game side only answers what Terraria knows: tiles and liquids,
  creatures and players, damage, drawing, sound. A component type with no system is logged by name, never ignored.
- C. Copy only how spells look (sprites, speed, damage). Rejected: interactions (triggers, statuses, physics,
  summons) are the point.

## 3. Rules for all magic work

1. Numbers and rules come from Noita's files (field, default, script), cited in a comment with the file name.
   A rule the files do not give: written as "assumed", plus a question in tasks "Author" with a test to do in Noita.
2. Nothing silent: a dropped shot, a component without a system, a missing Lua API, a file that does not load:
   one log line each (once per kind).
3. No code keyed by spell id. Behaviour hangs on component types and fields. Each exception is listed in
   section 6 with the reason.
4. Core first: systems live in src/Terranoita.Core when they do not need Terraria, with unit tests on small XML
   entities (tests/Terranoita.Core.Tests). The game side stays thin.
5. The measure (Phase 1) decides "done", not a play impression and not "it made a shot".
6. Game tests only with the author's OK, one at a time, minimized.

## 4. Phases

### Phase 0: no silent failure (small; first)
- SpellShots.Fire: at the cap, end the oldest shot instead of refusing the new one, and log once per session
  "spell shots: cap 600 reached, oldest ended". The cap (perf) stays.
- On first use of each projectile file: log the component types in it that have no system:
  "spell runtime: <file>: not run yet: VerletWeaponComponent, ..." (from the list of implemented types).
- `tncli magic-coverage <noita>`: for every spell of gun_actions.lua, its entity files, their component types
  (with Base files) and ProjectileComponent fields, marked run / not run; writes design/sources/pc_magic_coverage.json
  and prints a table sorted by number of spells affected. The implemented list comes from Core (one list, used by
  the game log and the tool).
- Check: Core tests; tncli output matches section 1 numbers; after the next game run no shot disappears without a line.

### Phase 1: the measure (a test that compares with Noita)
- Core `SpellExpectation` (built from Noita's files only): for a cast of a spell on a test wand, from gun.lua
  (LuaGun): projectile files and how many; per file from its XML: speed_min..max, lifetime (+random), damage by type,
  explosion radius and damage, on_collision_die, bounces, gravity, what it spawns (on death, on collision, Lua
  EntityLoad/Shoot), its components.
- Game recorder in SpellsTest: per shot: file, start speed, how long it lived, distance, how it ended (hit, world,
  lifetime, script), damage by type to the target, explosion radius, children made (files), components not run.
- Verdict per field with scale tolerance (1 Noita px = 3 px): a row says what differs, e.g.
  "ROCKET_TIER_3: no shot (Noita 1 rocket_tier_3.xml)", "TENTACLE: not run: VerletWeapon, VerletPhysics".
- Three suites: every spell alone; every modifier on LIGHT_BULLET, BOUNCY_ORB, GRENADE (measured change vs the
  ConfigGunActionInfo gun.lua gave); combos: BURST_2/3/4, SCATTER_*, ADD_TRIGGER/TIMER/DEATH, DIVIDE_*, ALPHA..OMEGA,
  always-cast wands, shuffle, uses.
- Baseline: design/sources/magic_baseline.txt gets the field-level rows; the number reported is
  "matching Noita: N of M spells, K modifiers, J combos".
- Check: the suite runs (author's OK), rows match the problems the author listed (draw-many, utility, TENTACLE,
  ROCKET_TIER_3) before any fix.

### Phase 2: the runtime under the shots
- Core: the entity store of LuaShotScripts becomes the shot's state (fields typed by component_documentation.txt);
  a system registry (component type -> system) with Noita's update order; IShotHost grows to the bridge the systems
  need (tile/liquid queries, creature hits, damage by type, explosions, particles, sounds, material cells).
- Move what SpellShots already does into systems one type at a time (Velocity, Projectile, Lifetime, Homing,
  SineWave, Arc, AreaDamage, CellEater, BlackHole, MagicConvertMaterial, MaterialSeaSpawner, EnergyShield,
  TeleportProjectile, Lightning, Light, emitters). Author's condition 1: the new path runs only with
  TERRANOITA_RUNTIME=components until its probe-compare number is at least the old path's; then it becomes the
  default and the old code goes in one commit.
- Check per step: Core tests; the Phase 1 suite (probe-compare) with the switch on equal or better than off.
- Steps (PC-24):
  1. Every shot in the store (switch on), VelocityComponent from the store each frame: Core ShotFlight (gravity_x/y,
     air_friction as 1 - f/60 a frame, terminal_velocity / apply_terminal_velocity; defaults from
     component_documentation.txt), tests from probe numbers (spark x0.9716 a frame, rocket x1.0833, DECELERATING_SHOT
     x0.504 per 5 frames); the host stops mirroring those fields on that path.
  2. ProjectileComponent lifetime, bounces_left / bounce_energy, collide_with_world, on_collision_die,
     penetrate_world, die_on_low_velocity from the store (the rest of the host's field mirror goes).
  3. One component type per step after that, in the order of the spells it moves in probe-compare.

### Phase 3: what is missing, by spells affected (section 1 numbers, re-sorted by magic-coverage)
1. ProjectileComponent's unread fields (damage_game_effect_entities, collide_with_*, bounce_*,
   on_collision_*, spawn_entity, damage_scaled_by_speed, physics_impulse_coeff, lob, go_through_this_material...).
2. HitEffect, GameEffect on hit, GameAreaEffect (fields and statuses).
3. Hittable projectiles: DamageModel, ExplodeOnDamage, ExplosionComponent (mines, bombs, propane, pipe bombs).
4. Physics projectiles: PhysicsBody/Body2/Throwable/ImageShape as a simple rigid body (bombs, dynamite, cart).
5. Verlet (TENTACLE family), LaserEmitter, ElectricCharge (Electricity.Emit), Levitation, MaterialSucker and
   MaterialInventory (vacuum), Teleport, ShotEffect (modifiers on the caster), AudioLoop (NoitaFmod).
6. Summons: Noita creature files through our creature system (Carriers) instead of projectiles.
7. Modifier fields: child_speed_multiplier, dampening, material_amount, explosion_damage_to_materials, pattern offsets.
8. ParticleEmitter with create_real_particles: real material cells (walls, trails) through Fluids/tiles.
- Check per item: its spells' rows in the Phase 1 suite turn OK; the not-run list in the game log shrinks.

### Phase 4: the author plays
- Sandbox (game_test -Mode sandbox): spells grouped by type in labeled chests; the author goes group by group;
  every report becomes a Phase 1 row first, then a fix.

### Running the ground truth (PC-22/PC-28, 2026-10-09)
- `tools/noita_probe/run_probe.ps1`: backs up Noita's config, mod list and the player's run (save00/world), installs and
  enables the probe mod (file output needs mods_sandbox_enabled 0), keeps Noita running unfocused, starts Noita,
  clicks into a new game (no key presses: a missed one lands in another window), waits for "done", closes Noita and
  puts everything back; only a finished run is copied to design/sources/noita_probe.jsonl (an unfinished one stays in
  the mod folder and the next run continues it). The player's PROTECTION_ALL / PROTECTION_POLYMORPH are set to
  frames -1: the effect files' 7200 frames ran out after 2 minutes (BOMB_HOLY_GIGA killed the player, MASS_POLYMORPH
  turned it); after a polymorph the player is found again by its player_unit tag. The mod fires by itself (PlatformShooterPlayerComponent.mForceFireOnNextUpdate +
  mRequireTriggerPull 0, ControlsComponent.enabled 0 + aim fields: verified in Noita) and resumes after a restart.
- Menu (screenshots the author allowed, 2026-10-09): main menu "Новая игра" at the window centre, then the mode screen
  selects nothing until the mouse is over a tile: click its first tile. run_probe.ps1 clicks both (fractions of the
  window); the player's run files in save00 (player.xml, world_state.xml) are backed up too, else Noita offers Continue.
- Terraria side: `game_test -Mode probe` (SpellProbeTest) builds the same arena (flat floor, wall at 264 px), the same
  target (16 x 20 Noita px, centre 4 px above the wand line) and aim, waits until the probe wand is in hand. Never run it
  while Noita is being driven by keys (keys reached Terraria: rows 30..84 cast the character's slot-0 wand).
- Fixed from probe data (2026-10-09): negative air_friction (rockets accelerate: 1 - f/60 per frame, measured), a blast
  hurts with explosion damage only, probe test target/aim/arena like the probe's.
- Open, needs flight paths in the probe (sample x, y, vx, vy every 5 frames): grenades (bounces_left 4, friction 0.6)
  roll and lie on the floor until ~frame 79-157 in Noita; ours spend the 4 bounces on floor contacts in a few frames.
- Comparing flights (2026-10-09 evening): Noita first sees a shot after its first move (x0 - vx0/60 is one spawn point,
  10.5 px ahead and 5.7 px above the player, for every speed); SpellRecorder samples after the move too, and
  probe-compare checks the path along/across each side's first direction (4 px + 10% of the distance flown, first 30
  frames). The Terraria arena now has Noita's heights against the shot line (floor 13.7 px under it, target centre
  4.3 px over it; the caster is held in the air): standing, our floor was 7 px under the shot line and shots Noita
  drops under the target hit ours.
- Verified from the paths: air friction is 1 - f/60 a frame, and a LuaComponent with remove_after_executed and no
  execute_times runs once (ACCELERATING_SHOT 1.7 - 3: x1.113 per 5 frames, DECELERATING_SHOT 1.7 + 6: x0.504; ours
  ran the script every frame, fixed in LuaShotScripts). Homing (HomingComponent 0.86 / 130) slows a spark to ~440 px/s
  in Noita; the engine's formula is not in Noita's files: to be fitted from probe paths.
- Known real differences seen so far: physics projectiles (bomb.xml: PhysicsThrowable, no VelocityComponent) fly at 60
  and die on the first floor contact here, Noita throws at 120 and they live 180 frames to the fuse (Phase 3.4).

## 5. Done means
- Phase 1 suite: every spell, modifier and combo matches Noita's files, or has a written reason (section 6).
- magic-coverage: every component type used by a spell has a system, or a written reason.
- The author's play pass finds nothing new in a group.

## 6. Exceptions (spell-specific code or a component left out, with the reason)
(none yet)
