# Cloud review of the magic commits ff892df..91bc046 (read only, nothing run)

Game files were not changed by the cloud. Most severe first.

1. **Spell uses refill (WandWindow.cs, _handSpell/_handUses).** Uses left are remembered for "the last spell taken
   out", not for the item. Take out a used-up BOMB (0 uses), take out any other spell, put the BOMB back -> full
   MaxUses. Also a fresh BOMB from a chest placed right after taking out a used one gets 0. Fix: carry uses with the
   item (like the wand number in the prefix byte, or a table keyed by the Item instance).
2. **Stale statuses on reused NPC slots (SpellShots.Extras.cs, Marks).** Keyed by whoAmI and never cleared: a new
   creature in the same slot inherits BLOODY/FROZEN... marks until they time out (crit boosts on the wrong target).
   Clear a slot's marks when an NPC spawns there (SetDefaults/OnSpawn), or store the type + spawn frame with the mark.
3. **CritBoost condition (Extras.cs:413).** `cond = S("condition_effect", S("condition_status"))`: a file with
   condition_effect="NONE" never boosts; condition_effect="" with condition_status="WET" always boosts. Check both
   fields separately, "" and "NONE" = no condition. Verify against the real crit-on-* files.
4. **Cave wand files not checked (WorldLoot.WandFile).** wand_level_0N_better / wand_unshuffle_0N / unique names are
   built without checking they exist; a missing one throws, is caught, s.Wand = -2: that cave wand never appears.
   Fall back to wand_level_0N (NoitaArt.ReadText(file) == null).
5. **extra_entities read flat (Extras.ReadExtra).** Regex over the file: no <Base> chains, nested objects ignored.
   Core now has `NoitaEntityXml.Load(path, read)` (Base merge, comments, `_tags`/`_enabled`, nested "a.b" fields):
   same input, fewer surprises; LuaShotScripts uses it too.
6. **Homing / black hole targets (Extras.Homing, BlackHole).** Critters (bunnies, butterflies) and dontTakeDamage NPCs
   are targets: homing shots chase bunnies. Skip `n.CountsAsACritter` and `n.dontTakeDamage` (Noita: homing_target tag).
7. **EntityLoad of a projectile (Extras.LoadEntity).** Fired at full speed in the owner's facing direction; Noita's
   EntityLoad makes the entity at rest and the script sets its velocity. Spells that EntityLoad then aim may fly wrong.
8. **ConvertMaterial (Fluids.cs).** When `to` is not a known liquid (kind 0, not water/lava) the source is removed and
   nothing replaces it. Fine if to_material is air/gas; otherwise material vanishes.
9. Cosmetic: Fluids.cs, Ignite's `<summary>` now sits above ConvertMaterial's (two summaries on one method, none on Ignite).

Core change made with this review: LuaShotScripts entity ids now start at 1,000,000 (`FirstEntityId`), and ids it does
not own (creatures 1000+, spell shots 100000+ from InRadiusWithTag/EntityLoad) go to the host for transform, set
transform, velocity and kill. Test: LuaShotScriptsHostIdsTests.
