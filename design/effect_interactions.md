# Effect interactions (author: "lightning + water = electrified water") — algorithm for the local session

Rule of the project: Noita's own data and rules, read from the player's Noita; numbers never invented (unknown ->
`_unverified`, ask the author). Core logic testable without the game (the cloud can write it, see the end).

## 0. What Noita does (to confirm on the PC from data.wak, not from memory)
- Materials have `electrical_conductivity` in data/materials.xml (water, brine, blood, metals... conduct).
- Electricity enters the world from `ElectricitySourceComponent` (docs: radius 5 px, emission_interval_frames 15) on
  lightning shots/explosions/ARC_ELECTRIC/THUNDER spells, and from entities with `ElectricChargeComponent`
  (ELECTRIC_CHARGE modifier: charge_time_frames 120, electricity_emission_interval_frames 5).
- Electrified conductive cells hurt what touches them with electricity damage (status ELECTROCUTION / the
  "electrocution" game effect: stun). Check data/scripts/status_effects/status_list.lua and the electricity
  explosion configs (config_explosion `electricity_count`, ...) for the numbers.
- Steps on the PC: `tncli wak-cat <noita> data/materials.xml | findstr electrical_conductivity` -> list; status
  ELECTROCUTION row (name, icon, seconds) from status_list.lua; which spell projectile files carry
  ElectricitySourceComponent / ElectricChargeComponent / damage_by_type electricity (noita_spells.json components).

## 1. Sheets first
- liquids.json + noita_solids.json: new column `conducts` (bool, from materials.xml electrical_conductivity).
  Terraria's own liquids: water conducts (Noita water does); lava/honey/shimmer -> ask the author.
- materials.json (Terraria tiles mapped to Noita materials): `conducts` for metal tiles (ores, bars, metal bricks)
  from the mapped Noita material.
- status_effects.json: ELECTROCUTION row from status_list.lua.
- spell_projectiles.json: `electricity_radius`, `electricity_every` (ElectricitySourceComponent),
  `charge_frames` (ElectricChargeComponent); apply_spells.py + SpellProjectileFromEntity (Core, cloud) read them.
- new sheet `effect_rules.json` for the generic table below (source, target, result, numbers, _sources).

## 2. Generic interaction model (one place for all such rules)
An "effect" is something a cell, tile, creature or shot carries: fire (Fire.Burning), electricity (new), freeze,
wet/oiled/bloody (Status / NPC marks), and the materials themselves (Fluids cells, reactions.json).
Rules come in three kinds, each already half there:
- material x material -> materials: reactions.json (Fluids.React) — done.
- effect x material -> material or field: fire x burnable -> burning (Fire, Fluids.Burn — done);
  freeze x liquid -> its `freezes_to` solid (FROZEN shots/ice spells: Fluids cell -> tile, Terraria water too);
  heat x solid -> `melts_to` (Fire.Melt — done for ice/snow);
  **electricity x conductive -> charged field (new, section 3)**.
- effect x creature status -> damage/status: WET + electricity -> more (Noita: wet creatures conduct), WET cancels
  ON_FIRE (status_effects.cancels — done), crit-on-wet/oiled/burning (HitEffect — done).

## 3. Electricity field (new: Physics/Electricity.cs, like Fire.cs)
State: `Dictionary<int cellKey, int framesLeft> Charged` (one entry per tile), cap MaxCharged (e.g. 3000, perf).
1. Sources each frame:
   - spell shots with electricity (ElectricitySourceComponent: every `electricity_every` frames, radius
     `electricity_radius` Noita px x 3 / 16 tiles) and their explosions; ELECTRIC_CHARGE shots every 5 frames while
     charged; ARC_ELECTRIC arcs (Extras.Arc LIGHTNING) along their segment; Noita creatures' electric attacks.
   - Emit(x, y, radius): every tile within radius that conducts (Fluids cell of a conducting liquid, Terraria water
     with liquid > 32, a conducting tile) starts a flood fill.
2. Flood fill (BFS, 4-neighbours) through connected conducting tiles from each start tile, up to MaxSpread tiles
   (e.g. 400) and MaxDistance (e.g. 60 tiles) from the source; every reached tile: Charged[k] = max(old, ChargeFrames).
   Reuse one queue/HashSet (no garbage per tick). Skip tiles already charged this tick (stamp) -> one fill per pool.
3. Every 6 frames (like Fire): for each charged tile on/near screen: dust (DustID.Electric) + light; creatures whose
   hitbox overlaps it: electricity damage (Noita number x 25 hp, x tier for our creatures as elsewhere) once per
   tick per creature (collect NPC hitboxes once per tick, as in Fire.HurtNpcs after the fix), ELECTROCUTION status
   (stun: velocity *= 0, or Terraria's Electrified buff for players if the author agrees). The player: Status.Apply
   ("ELECTROCUTION") + damage; WET (status or standing in water) -> the multiplier from Noita (status_list / damage
   multipliers), else none.
4. framesLeft -= 6; 0 -> removed. A charged cell that stops conducting (liquid moved/dried) -> removed.
   Fluids moving a charged liquid: simplest is to keep the charge on the tile, not the liquid (re-emission refreshes).
5. Hooks: Physics/Patches UpdatePatch (inside the save lock, after Fluids.Update), SpellShots (sources),
   world load -> Clear. Not saved (transient).

## 4. Order of work for the local session
1. PC facts (section 0) -> sheets (section 1) via apply_facts / apply_spells (numbers from tools).
2. Electricity.cs (section 3) + sources in SpellShots; then freeze x liquid (same shape: Emit -> convert cells).
3. game_test -Mode physics: a pool of water, a creature standing in it and one outside, cast LIGHTNING / spark bolt
   with ELECTRIC_CHARGE into the pool: the one inside is hurt and stunned, the one outside not; the charge ends
   ~charge frames after the last emission; FPS with a big pool (MaxSpread cap holds).
4. MODLOG + progress: liquids show "conducts electricity" in the tooltip (ProgressInfo, cloud can add).

## 5. What the cloud can do in Core (ask it with a cloud_task file)
- `Core/Physics/Conduction.cs`: grid-agnostic flood fill + charge timers behind an interface (`IConductGrid:
  Conducts(x,y)`), caps, stamps, no allocations; tests (pool, two pools, cap, decay, refresh).
- apply_spells.py / SpellProjectileFromEntity: the electricity columns; ProgressInfo: "conducts" line.
