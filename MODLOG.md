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

## 2026-10-06 — author's PC (Terraria 1.4.5.8 x86, Noita main; both in D:\steam\steamapps\common)

Installed: Git, .NET 8 SDK 8.0.425, ilspycmd 8.2.0.7535 (needs `DOTNET_ROLL_FORWARD=Major`). Decompile in
`%USERPROFILE%\terraria-decomp` (outside the repo).

**Stage 1a runs in the real game** (autotest + author watching): all 12 enemies spawn (also naturally), walk/fly/hop,
melee, lunge, shoot; Noita sprites, names (player's language, from Noita's common.csv) and **Noita's own sounds**.
`preflight --gate 1a` is CLEAN, 18 Core tests pass.

Facts confirmed: x86 + LAA Terraria.exe; `WindowsLaunch.Main` + embedded-resource resolver; Steam start works (steam_appid.txt);
data.wak layout; component defaults from Noita's `tools_modding/component_documentation.txt`.

Hard-won launcher lessons (all in systems/hooks sheets):
- Load Terraria with `Assembly.Load("Terraria")` (default context), not LoadFrom, or the mod patches a second copy.
- Patching JIT-compiles Terraria methods, which runs beforefieldinit static ctors (Main → SavePath, CaptureManager →
  Main.instance). So: set `Program.SavePath` exactly like LaunchGame, then patch from `Main.OnEngineLoad`.
- Carrier NPC = type 146 (NPCID.None3, unused, hidden in bestiary). `aiStyle` must be 0, never -1 (UpdateNPC indexes
  NoMultiplayerSmoothingByAI[aiStyle]; the exception is swallowed and the NPC silently reset).
- Enemy shots are the mod's own list (no unused projectile type).
- Noita sounds: FMOD Studio 2.1.5 x86 dlls + banks loaded from the player's Noita (`NoitaFmod`); event list via
  `Terranoita.exe --list-noita-sounds "<Noita>" build/noita_sounds.txt`, used by apply_facts.

Author decisions today: Noita px → **3** Terraria px (was 2; "normal size"); per-enemy `size` column (hiisi shotgunner ×2);
Noita sounds, not Terraria's; ranged cooldown = frames_between + attack state duration (author: "1 shot per 2 s").
Balance set from hits-to-kill reasoning (balance.json `_sources`), first autotest only — tune with the author.

Testing: `TERRANOITA_AUTOTEST=1` + `-savedirectory %LOCALAPPDATA%\Terranoita\testsave` (copy of one world, fresh
"Terranoita Test" character) — enters the world, spawns each enemy in turn (removing the previous), logs status every
second to `%LOCALAPPDATA%\Terranoita\logs\latest.log`. Ctrl+Shift+N spawns the next enemy in normal play.
Window capture without focus: scratchpad `grab.ps1` (PrintWindow) — fails when the window is minimized.

**Open**: check foot alignment + size in game; author said enemies looked "semi-transparent" (added a brightness floor of 70,
not yet confirmed); licence + remix choice (ask); Melty packaging (step 8). Mod files currently copied into the
Terraria folder for testing: Terranoita.exe, Terranoita.Game.dll, Terranoita.Core.dll, 0Harmony.dll.

### Melty (same day)
- Draft **"Terranoita: Invasion"**, modId `c68ad4c6-f9db-40f5-802c-a4f9d7713e69`, slug `terranoita-invasion`,
  Studio https://melty.gg/studio/c68ad4c6-f9db-40f5-802c-a4f9d7713e69 , linked to GitHub tsartsinc-web/terranoita.
- Author: title "Terranoita: Invasion", English description, licence **All rights reserved**, remix **allowed** (allowRemix default true).
- Release 0.1.0 submitted (draft, publishable, one click: yes; review findings: unverified-game-integration, executable-code).
  Package = build/package (Release build) zipped; recipe = design/melty.recipe.draft.json with exact fileName.
- Screenshot uploaded (showcase capture: TERRANOITA_AUTOTEST=1 + TERRANOITA_SHOWCASE=1, noon + 5 enemies).
- **Waiting for the author**: press Test on the mashup's page in the Melty app; publish only with the author's permission.
- Local commits not pushed: `git push` needs the author's GitHub login. melty.json at repo root not added yet (needs consent).
- Since the screenshot: thrower/shooter range hysteresis (author: "moves in jerks") — check in game.
