# Cloud review of the live code (stage 2 physics, creatures), read only, nothing run

Files read: Physics/Patches, Fluids (sim, load/save), Placed, Falling, Fire, Status, Mats; Carriers spawn/segments.
No game file was changed by the cloud. Most severe first.

1. **Liquids file can be cut short and the caves get pools again (Fluids.Save/Load, Placed.Save).**
   - Save writes straight over `<world>.wld.fluids` (File.Create) while enumerating `Cells`. If anything throws
     mid-way (or the game dies), the file is left truncated. Placed.Save and ToxicGround.Write are the same.
   - Terraria's autosave runs WorldFile.SaveWorld on a ThreadPool thread while the game keeps updating (check:
     `TN_IL=1 tncli tr-methods Terraria.exe WorldGen saveAndPlay`). Fluids.Update changing `Cells` during that
     enumeration -> "Collection was modified" (caught, logged) -> truncated file; a Dictionary written and read at once
     can also be corrupted.
   - Load of a truncated file: the exception is caught, `PoolsVersion` stays 0, Load returns true, and LoadPatch then
     runs `CavePools.Generate()` + `Banks()` on a world that already has them: pools on pools, every time it happens.
   - Fix: on the main thread take a snapshot (`Cells.ToArray()`, Placed set copy, toxic copy) or save under a lock
     shared with Update; write to `path + ".tmp"` and `File.Replace`/`Move` it over the old file; in Load set
     PoolsVersion from the file only after a complete read, and on a failed read keep the old file aside
     (`.bad`) and do NOT regenerate pools.
2. **A big building landing can freeze the game for a while (Falling.Land -> Disturb -> CheckSupport).** After a
   group of up to 4000 placed tiles lands, every landed tile is disturbed (5 queue entries each); each placed tile
   looked at runs its own flood fill of up to 4000 tiles. 400 looks per frame x up to 4000 steps = up to 1.6 M steps
   per frame for many frames. Fix: remember the tiles of groups already found supported/unsupported this frame (a
   `checkedThisFrame` set) and skip CheckSupport for them.
3. **Placed blocks that land on a taken tile or fall out of the world are destroyed without an item** (Land:
   "this piece is lost"; Step: below maxTilesY - 10). The player's building blocks vanish. Drop them as items
   (Item.NewItem with the tile's item type) at least for `p.Placed` parts.
4. **Fire.HurtNpcs is O(burning tiles x 200 NPCs) every 6 frames**: a large fire (up to 4000 tiles) = 800k checks per
   tick. Collect the NPC hitboxes once per tick (only active, not friendly, not immune) and test tiles against them,
   or check NPCs against the burning set instead.
5. Minor: `PlaceTilePatch.Postfix` has no try/catch (an exception there reaches Terraria's placement code; the other
   patches all catch). `Land` sets `t.liquid = 0` where a piece lands: Terraria water there is deleted, not moved.

Looked fine: world-edge bounds (Mats.InWorld keeps 5 tiles, so neighbour reads are safe), Status effects/regen
order, carrier slot reuse (Table/HeadOf reset in Spawn, type check in Get), SpellShots update loop (new shots appended
while iterating backwards is safe).
