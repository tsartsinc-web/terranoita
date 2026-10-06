# Terranoita — brief for agents

Terraria 1.4.5 mod (own launcher + Harmony, no tModLoader) that brings Noita's creatures into the player's Terraria,
read from the player's Noita at runtime. Published on Melty as "Terranoita: Invasion" (modId
c68ad4c6-f9db-40f5-802c-a4f9d7713e69, players have 0.1.0). Author writes Russian: reply in simple Russian, short,
answer questions immediately (yes/no first). Decisions in README are final: do not ask again. Nothing on Melty without
the author's explicit permission.

Work branch: `claude/dazzling-carson-h8mi9n`. Current step and history: last sections of `MODLOG.md`
(read "Checklist for adding creatures" before touching creatures).

## Layout
- `design/sheets/*.json` — source of truth (rows; `_unverified` = open, `_sources` = evidence). Sheets first, then code.
- `design/sources/noita_facts.json` — facts + component dump + `_component_docs` read from the author's Noita by
  `tncli facts` (PC only). Query it with python, never print it whole (3.5 MB).
- `tools/apply_facts.py` (facts -> sheets by rules; numbers from tools, never by hand), `tools/preflight.py --gate 1b`
  (open cells), `tools/gen_cs.py --gate <stage>` (C# tables; `*.g.cs` are generated, do not edit).
- `src/Terranoita.Core` (Noita readers, Ai/Brain.cs, generated tables; builds anywhere),
  `src/Terranoita` (game patches; builds only against Terraria.exe on the PC), `src/Terranoita.Cli` (tncli),
  `src/Terranoita.Launcher`.
- `tools/pc_step.ps1` — everything that needs the games, one command on the author's PC (facts, build, autotest);
  writes `design/sources/pc_check.txt` and `pc_autotest_<stage>.txt`, pushes.

## Cloud sessions (no games)
- .NET 8: `curl -sSfL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh -o /tmp/di.sh &&
  bash /tmp/di.sh --channel 8.0 --install-dir $HOME/.dotnet` then `export PATH=$HOME/.dotnet:$PATH`.
- Checks: `dotnet test tests/Terranoita.Core.Tests`, `python3 -m unittest discover -s tests/tools`,
  `python3 tools/preflight.py --gate 1a -q` must stay CLEAN (stage 1a rows must not change).
- Anything needing the games goes to MODLOG as "проверить на ПК" and into the next pc_step run.

## Save tokens
Targeted greps and python one-liners with short output; no full-file dumps; no subagents/workflows unless the author
asks; short status lines.
