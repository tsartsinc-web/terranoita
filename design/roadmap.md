# Terranoita roadmap: from now to 1.0 (cloud draft 2026-10-08; the author decides open points)

Order stays the author's: enemies -> physics -> wands/spells -> items, then what is left of enemies (1c).
Every milestone ends with a Melty release only after the author presses Play/approves. Rows go into design/tasks.md.

## M1 — 0.3.1 (now): FPS, cave pools, cave-ins. Waits only for the author's Play in the Melty app.

## M2 — 0.4.0 "Magic" (stage 3 complete)
Done already: all spells through Noita's gun.lua, shot scripts, all 48 wand files, spells from any entity file,
wand window, 16 spell slots, progress window, cave wands on pedestals, spells in chests.
Left:
- PC: traders (author: a spell trader and a wand trader for Noita gold) — Core can run Noita's shop scripts
  (CLOUD: like LuaWandMaker; prices/levels from the player's Noita), game: NPC/stall + window.
- PC+CLOUD: what spells still cannot do: 34 projectiles without ProjectileComponent (lasers, clouds, fields),
  HitEffect LOAD_CHILD_ENTITY (curse, petrify, gravity field), statuses without a Terraria buff, the 61 spells
  marked port=hand that the magic test shows wrong (list from PC-1's summary, fixed by kind, not one by one).
- PC+CLOUD: effect interactions (design/effect_interactions.md): electricity through water/metal, freeze x liquid.
- Balance pass: mana cost vs Terraria mana, spell damage vs Terraria tiers (spells are x25, enemies x tier mult):
  the author plays the sandbox, notes go to tasks.
- Tests: PC-1 summaries + baseline, Core golden tests (CLOUD-1). Release when the magic test is all OK/known.

## M3 — 0.5.0 "Flasks and items" (stage 4)
- Facts (PC, `tncli items` written by the CLOUD): data/entities/items/** pickups (flasks, powder pouches, orbs,
  stones, rings...), potion contents and chances from Noita's potion scripts, their pictures/names.
- 4 flask slots (author: README), flask = item with a material + amount; drink (Status: ingestion effects already in
  liquids.json), throw (breaks: Fluids.Add at the spot), pour/spray with the use key, fill from pools (Fluids).
  Most of it is already there: 128 liquids, reactions, statuses, touch/ingestion effects.
- Items with special effects (stones: kiuaskivi heat, ukkoskivi thunder, vuoksikivi water...; orbs): each from its
  entity file + LuaShotScripts-like runtime for its scripts (Core already has the entity runtime).
- Where they come from: chests/pedestals by depth like wands (author decides), progress tab "Items".

## M4 — 0.6.0 "Bosses and the rest" (stage 1c: 50 rows, 13 still need facts)
- Facts for the 13 (PC), then bosses' scripts/phases (Noita bosses are scripted: Core entity runtime + Brain).
- Author decides where bosses come from in Terraria: own arenas/structures, summoning items, or replacing nothing
  and appearing in biomes after Terraria bosses (README has no decision yet).
- Minions and special creatures (mimics done; summoned by bosses/nests).

## M5 — 1.0 polish
- Balance across Terraria's progression (pre-hardmode / hardmode / post-Plantera tiers) with the author's play notes.
- Performance budget: SlowFrames clean in the sandbox and in big fights (physics + spells + 20 creatures).
- Text: mod's own UI lines in Russian and English (names already come from Noita's translations).
- Melty page: description, pictures, changelog per release (CLOUD drafts, the author publishes).
- Save safety: all our files atomic, old saves load (0.3.0 worlds, characters).

## Open author decisions (blocking only their milestone)
1. M2: trader look/place (NPC in town? stall in caves like Noita's Holy Mountain?), prices in Noita gold as Noita.
2. M2: electricity details (lava/honey/shimmer conduct? stun = Terraria Electrified buff or our status?).
3. M3: flasks: how they are found; keys for drink/throw; fill from Terraria water too?
4. M3/M5: Noita perks (Holy Mountain perk altars) — in the mod or not? Not in README yet.
5. M4: how bosses appear in a Terraria world.
6. After 1.0: multiplayer (README: later, separate decision).

## How the two agents split it
- CLOUD: Core runtimes (shop scripts, item scripts, boss scripts), facts tools (`tncli items`), sheets via tools,
  tests (golden files), reviews of game code, plans, Melty texts.
- PC: game side (UI, NPCs, hooks), facts runs on the author's Noita, game tests, releases with the author.
