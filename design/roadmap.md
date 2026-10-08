# Terranoita roadmap to 1.0 (cloud draft 2026-10-08; the author decides open points)

Principles: ship small and often (a release when a milestone's "Must" is done; "Later" moves on);
Noita's own data and code; each milestone has a "Done when" checked by a test or by the author playing.
Order is the author's: enemies -> physics -> wands/spells -> items -> what is left of enemies (1c).

## M1 0.3.1 — FPS, cave pools, cave-ins (built). Done when: the author presses Play on the draft in Melty.

## M2 0.4.0 — Magic, first release
Most of stage 3 already works in game, so it ships soon and grows in 0.4.x.
- Must: magic test summary all OK or known (PC-1); no errors in a sandbox hour; save/load keeps wands, spells, uses;
  spell/wand drops in caves and chests; FPS ok with ~20 scripted shots (PC-5).
- Later (0.4.x): traders (place: author decides later); effect interactions — electricity first
  (design/effect_interactions.md; author: conducts only what conducts in Noita, player stun = Terraria Electrified);
  lasers/clouds/fields (34 projectiles without ProjectileComponent); curse/petrify/gravity hit effects;
  statuses without a Terraria buff; balance from the author's play notes.
- Done when: the author plays the sandbox and a normal world for an hour and approves.

## M3 0.5.0 — Flasks and items (stage 4)
- Must: 4 flask slots; flasks with Noita's contents and chances (facts via `tncli items`); used as in Noita (author):
  held, left click pours a stream, right click throws (breaks into the liquid), a key drinks (ingestion effects
  already in liquids.json); filled from pools of our liquids and Terraria's water only (not lava/honey/shimmer);
  found by depth like wands.
- Later: special items (stones, orbs) with their Noita scripts; progress tab "Items".
- Done when: every flask material can be drunk/thrown/poured in the sandbox without errors; the author approves.

## M4 0.6.0 — Perks (author 2026-10-08)
- Must: Noita's perks (facts: data/scripts/perks/perk_list.lua — names, icons, what they do) as items that drop from
  bosses, Terraria's and Noita's; a stronger boss drops more/better perks (e.g. the Wall of Flesh: 3); used like a
  potion / mana star / life crystal, active until death, can be kept as items. Perk effects by kind, the simple ones
  first (stats, immunities), scripted ones through the Core entity runtime.
- Needs from the author later: the boss -> number/quality table (cloud drafts it from Terraria's boss order).
- Done when: each Terraria boss drops its perks in the sandbox, a used perk works and ends at death.

## M5 0.7.0 — Bosses and the rest (stage 1c: 50 rows, 13 need facts)
- Must: facts for the 13; minions and special creatures; Noita bosses with their own scripts (Core entity runtime),
  called by a summoning item like Terraria's bosses (author); they drop perks too (M4).
- Done when: each boss can be summoned and fought in the sandbox to the end.

## M6 1.0 — Polish
- Balance through Terraria's progression; FPS budget in big fights; our UI lines in Russian and English;
  old saves load; Melty page and changelog (cloud drafts, author publishes).
- Done when: a full playthrough by the author with no blocking notes.

## Risks to watch
- FPS (physics + spells + creatures together): SlowFrames line in every test summary.
- Saves: every file of ours atomic; a broken file never destroys a world.
- Noita updates: everything is read from the player's Noita, so a Noita update can change data; facts runs catch it.

## Author decisions (2026-10-08, also in README)
- Electricity: only what conducts in Noita; player stun = Terraria's Electrified buff.
- Flasks: as in Noita (LMB pour, RMB throw, key drink); fill from our liquids and Terraria water only.
- Noita bosses: summoning items. Perks: drop from all bosses, stronger boss = more/better, used like a potion,
  until death, storable.
Open: trader place (later); perk table per boss (cloud drafts, author checks); multiplayer after 1.0.

## Split
CLOUD: Core runtimes, fact tools, sheets via tools, golden tests, code reviews, plans, Melty texts.
PC: game side, facts runs on the author's games, game tests, releases with the author.
