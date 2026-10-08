# Terranoita — brief for agents

Terraria 1.4.5 mod (own launcher + Harmony, no tModLoader): Noita's creatures, physics and magic in the player's
Terraria, read from the player's Noita at runtime. On Melty as "Terranoita: Invasion" (modId
c68ad4c6-f9db-40f5-802c-a4f9d7713e69); release state is in design/tasks.md "State".

## Rules (author)
- Author writes Russian: answers and questions in simple short Russian (yes/no first); progress and technical notes
  in English, one line or none. Decisions in README are final.
- Nothing on Melty without the author's explicit permission; the author signs in to Melty personally.
- Save tokens: targeted greps, short outputs, no full-file dumps, no subagents unless asked; no screenshots taken or
  read unless the author asks (use log lines).
- Game tests: only in the background, minimized, one at a time, after a one-line heads-up.
- Stage 3: run Noita's own code and data (Lua, art, texts, sounds), do not re-implement it.

## Start
`git pull`, then **design/tasks.md** (State, both queues, ownership, how to mark done). Read a MODLOG section only
when a task points to it. MODLOG.md: new section at the end, UTF-8 (merges by union). Branch:
`claude/dazzling-carson-h8mi9n`. Before touching creatures: MODLOG "Checklist for adding creatures".

## Layout
- `design/sheets/*.json` — source of truth (rows; `_unverified` open, `_sources` evidence). Sheets first, then code.
  New Harmony hooks need rows in hooks.json + systems.json, then `python tools/gen_cs.py --gate 1b` (`*.g.cs` are
  generated). `tools/preflight.py --gate 1a -q` must stay CLEAN.
- `design/sources/noita_facts.json` (read from the author's Noita by `tncli facts`; 3.5 MB, query with python, never
  print whole). `tools/apply_facts.py` turns facts into sheet values: numbers come from tools, never by hand.
- `tools/pc_step.ps1`: the games' steps in one command on the PC (facts, build, autotest), writes design/sources/pc_*.
- Entry.Stage only picks creatures; every Harmony patch applies whatever the stage.
- `src/Terranoita.Core` (Noita readers: LuaGun = gun.lua in MoonSharp, LuaWandMaker = wand/potion scripts,
  LuaShotScripts, NoitaFmod; builds anywhere), `src/Terranoita` (game patches; PC only: `dotnet build
  src/Terranoita/Terranoita.Game.csproj -c Release -p:TerrariaDir=D:\steam\steamapps\common\Terraria`),
  `src/Terranoita.Cli` (tncli), `src/Terranoita.Launcher` (x86).
- Game magic: src/Terranoita/Magic (MagicItems carriers, WandStore, Casting, SpellShots, WandWindow U, Flasks,
  WorldLoot altars + chests, ManaCap). Lua must run under LuaCulture.

## PC tools
- `tncli tr-methods <Terraria.exe> <Type> [regex]` (signatures; env TN_IL=1 adds IL): check every patch target first,
  an unbound patch shows a modal error in the game. Also wak-list, wak-cat, wak-get, lua-cast, lua-golden, lua-potion.
- `ilspycmd` (ILSpy CLI; install once: `dotnet tool install -g ilspycmd`, author approved): read Terraria's real code
  when tr-methods is not enough, e.g. `ilspycmd <Terraria.exe> -t Terraria.Player | grep -n ...`. Local only: never
  commit or push decompiled code. Before each commit run /code-review on the diff.
- `powershell -ExecutionPolicy Bypass -File tools/game_test.ps1 -Mode <m>`: play (the game, own saves), magic,
  spells, wands, physics, fps, cart, tour, sandbox (the last two stay open). Magic modes print a summary + a diff
  against design/sources/magic_baseline.txt.
- FMOD events can be listed from 32-bit PowerShell loading Terranoita.Core.dll (NoitaFmod.EventPaths).

## Cloud sessions (no games)
- .NET 8: `curl -sSfL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh -o /tmp/di.sh &&
  bash /tmp/di.sh --channel 8.0 --install-dir $HOME/.dotnet`, `export PATH=$HOME/.dotnet:$PATH`.
- Checks: `dotnet test tests/Terranoita.Core.Tests`, `python3 -m unittest discover -s tests/tools`, preflight 1a.
- Anything needing the games: a PC queue row in design/tasks.md.
