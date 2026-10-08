# Progress window (author, 2026-10-08) — for the local session

Like Noita's Progress menu, per character (author). Core part is done: `src/Terranoita.Core/Progress/ProgressBook.cs`
(+ ProgressBookTests). The game part below is for the PC session.

## When something becomes known (author)
- Creature: when the player **kills** it. Count `kills`.
- Liquid / gas: when the player **touches** it (today's `Fluids.Learn` call sites).
- Wand: when the player **takes** it (cave wand picked up, wand from a chest/shop/drop into the inventory).
- Spell: when the player takes it (spell item into the inventory or a wand slot; spells in a taken wand count too).
  Count `casts` in Casting (optional, shown in the tooltip).

## Ids
- creature: enemies.json id (`n.Def.Id`); spell: gun_actions id; liquid: liquids.json id.
- wand: WandData has no file name, so the id is the wand's **sprite** path (each Noita wand picture = one entry;
  unique wands have their own picture). Full list = data/items_gfx/wands/*.png + custom/*.png (+ sprites in
  data/entities/items/wand_*.xml) from NoitaArt/data.wak.

## Game hooks (suggested places)
- Kill: Loot.cs npc_loot postfix (already knows `n.Def`) -> `Book.Count("creature", id, "kills")`.
  Worm segments: count only the head (Loot already does).
- Liquid: `Fluids.Learn(id)` -> also `Book.See("liquid", id)`. Migrate once: lines of the old
  known_<name>.txt into the book, then HoverName reads the book (one source).
- Wand taken: WorldLoot pickup, and an Item pickup / OnPickup patch for wand items -> `See("wand", sprite)` and
  `See("spell", s)` for every slot and always-cast.
- Spell taken: spell item picked up or put into a wand slot (WandWindow) -> `See("spell", id)`.
- Book life: load on entering a world with the character (`ProgressBook.FileFor(<LocalAppData>/Terranoita,
  Main.ActivePlayerFileData.Path, player.name)`), save when Dirty on world save, on window close and every ~2 min;
  `Save` is atomic (tmp + replace). Main thread only.
- `Discovered` event: optional Noita-style "New!" popup text.

## Window
- Open: a button in the inventory next to the Bestiary/emote buttons, and key **O** (check it is free with tr-methods /
  Terraria's keybind list; make it a setting like U). Does not pause, like the map.
- Tabs: Spells, Creatures, Wands, Liquids (later Items, Perks). Header "Spells 45 / 393".
- Grid of icons in the game's order (`Book.Page(category, fullList)`): spells in gun_actions order, creatures in
  enemies.json order (stage-filtered), liquids in liquids.json order, wands by sprite. Unknown: dark silhouette
  (icon drawn black, alpha 0.6) or "?"; no name in the tooltip.
- Tooltip (known): spell — name, description, mana, damage, uses, casts; creature — name, Terraria life (tier),
  where it lives (biome_map), attacks, kills; wand — name, picture; liquid — name, colour, what it does (status).
- Pictures and names from the player's Noita (NoitaArt), Noita UI look by default, Terraria look switch like WandWindow.
