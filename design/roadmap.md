# Terranoita roadmap to 1.0 (cloud draft 2026-10-08; the author decides open points)

Principles: ship small and often (a release when a milestone's "Must" is done; "Later" moves on);
Noita's own data and code; each milestone has a "Done when" checked by a test or by the author playing.
Order is the author's: enemies -> physics -> wands/spells -> items -> what is left of enemies (1c).

## M1 0.3.1 — FPS, cave pools, cave-ins (built). Done when: the author presses Play on the draft in Melty.

## M2 0.4.0 — Magic, first release
Most of stage 3 already works in game, so it ships soon and grows in 0.4.x.
- Must: magic test summary all OK or known (PC-1); no errors in a sandbox hour; save/load keeps wands, spells, uses;
  spell/wand drops in caves and chests; FPS ok with ~20 scripted shots (PC-5).
- Later (0.4.x): traders (needs decision 1); effect interactions — electricity first (design/effect_interactions.md);
  lasers/clouds/fields (34 projectiles without ProjectileComponent); curse/petrify/gravity hit effects;
  statuses without a Terraria buff; balance from the author's play notes.
- Done when: the author plays the sandbox and a normal world for an hour and approves.

## M3 0.5.0 — Flasks and items (stage 4)
- Must: 4 flask slots; flasks with Noita's contents and chances (facts via `tncli items`); drink (ingestion effects
  already in liquids.json), throw (breaks into the liquid), pour; fill from pools; found by depth like wands.
- Later: special items (stones, orbs) with their Noita scripts; progress tab "Items".
- Done when: every flask material can be drunk/thrown/poured in the sandbox without errors; the author approves.

## M4 0.6.0 — Bosses and the rest (stage 1c: 50 rows, 13 need facts)
- Must: facts for the 13; minions and special creatures; bosses with their own Noita scripts (Core entity runtime).
- Done when: each boss can be fought in the sandbox to the end; where they appear is the author's decision 4.

## M5 1.0 — Polish
- Balance through Terraria's progression; FPS budget in big fights; our UI lines in Russian and English;
  old saves load; Melty page and changelog (cloud drafts, author publishes).
- Done when: a full playthrough by the author with no blocking notes.

## Risks to watch
- FPS (physics + spells + creatures together): SlowFrames line in every test summary.
- Saves: every file of ours atomic; a broken file never destroys a world.
- Noita updates: everything is read from the player's Noita, so a Noita update can change data; facts runs catch it.

## Open author decisions (each blocks only its milestone)
1. Traders (M2 later): NPC in town or a Noita-like Holy Mountain stall in caves.
2. Electricity (M2 later): do lava/honey/shimmer conduct; player stun = Terraria Electrified buff or our status.
3. Flasks (M3): keys for drink/throw/pour; fill from Terraria water too.
4. Bosses (M4): how they appear in a Terraria world.
5. Noita perks: in the mod or not (not planned unless the author says so).
6. Multiplayer: after 1.0, separate decision.

## Split
CLOUD: Core runtimes, fact tools, sheets via tools, golden tests, code reviews, plans, Melty texts.
PC: game side, facts runs on the author's games, game tests, releases with the author.
