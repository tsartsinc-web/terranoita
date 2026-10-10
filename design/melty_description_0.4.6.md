# Melty description for 0.4.6 (to paste BELOW the author's "!!!" header, which stays first and unchanged)

Counted 2026-10-10 from the sheets (design/sheets/*.json), Noita's own files (perk_list.lua, data.wak) and the probe
(design/sources/pc_probe_compare.txt); nothing guessed.

## What's inside
- Spells: 422 of Noita's spells (143 modifiers, 122 projectiles, 45 static projectiles, 26 materials, 25 utility,
  14 multicast, 5 passive, 42 other), run by Noita's own spell code (gun.lua) read from your Noita.
- Wands: Noita's own wand scripts make the wands (78 Noita wand files, 1016 wand pictures for procedural wands).
- Perks: 106 Noita perks; 100 can drop from Terraria bosses (Noita's own pool is 103; 3 need Noita's Holy Mountain
  or gods and are left out).
- Liquids and gases: 128 (102 liquids, 26 gases), 298 Noita reactions between them.
- Creatures: 153 Noita creatures.
- Status effects: 35 Noita status effects, with Noita's icons.
- Magic tested against real Noita: 86% of the tested casts behave as in Noita (781 of 910: single spells 364 of
  419, modifiers 358 of 419, combos 24 of 24, random Noita wands 35 of 48). A test mod casts the same thing in Noita
  and in Terranoita and compares what happens.

## New in 0.4.6
- Noita's perks: every Terraria boss drops random perk items (more on Expert, Master and For the Worthy). Use one
  from the hand and the perk is yours, run by Noita's own perk code. Your perks are shown as icons under the buffs
  and in the progress window (O, Perks tab). On death all perks are lost; with more than 10, a quarter drop where
  you died.
- Noita's creatures drop a random spell 1 time in 100.
- Magic: grenades and bouncing shots roll on the ground as in Noita; glitter bomb shards, spore pod, crumbling
  earth, the wall spells and the BOUNCE_* spark, lightning and laser modifiers work.

## If the install fails
- If the install fails on 0Harmony.dll, your antivirus removed it: add the Melty folder to its exclusions and
  install again.
