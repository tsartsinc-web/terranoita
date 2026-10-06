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
