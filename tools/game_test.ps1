# One minimized autotest run of the game on the author's PC (author: only in the background, minimized, one at a time,
# short; tell the author first). Copies the fresh build next to Terraria.exe, runs, prints the interesting log lines.
#   powershell -File tools/game_test.ps1 -Mode magic            # MAGIC: wands, casting, screenshots
#   powershell -File tools/game_test.ps1 -Mode fps -World test1 # PERF: physics time by cave pools
#   powershell -File tools/game_test.ps1 -Mode enemies -Only worm,eel
#   powershell -File tools/game_test.ps1 -Mode wands            # every Noita wand file: made, held, fired 1.5 s
#   powershell -File tools/game_test.ps1 -Mode spells           # every spell alone at a target, checked against the sheets
#   ... -Accept                                                   # (magic/wands/spells) take this run as the new baseline
# magic/wands/spells print ONE summary, the problems (max 20 lines) and the change against
# design/sources/magic_baseline.txt; the full log stays in %LOCALAPPDATA%/Terranoita/logs/latest.log.
#   powershell -File tools/game_test.ps1 -Mode sandbox          # the author plays: arena, chests of every spell and wand
#   powershell -File tools/game_test.ps1 -Mode tour             # a new medium world: Shine, 10 underground chests, death, flasks
# Modes: magic, fps, physics, gallery, audit, enemies. Screenshots: %LOCALAPPDATA%/Terranoita/shots/*.png
param(
    [string]$Mode = "magic",
    [string]$World = "",
    [string]$Only = "",
    [int]$Minutes = 0,
    [switch]$Accept,
    [string]$Terraria = "D:\steam\steamapps\common\Terraria",
    [string]$Noita = "D:\steam\steamapps\common\Noita"
)
$bin = Join-Path $PSScriptRoot "..\src\Terranoita\bin\Release\net48"
# logic first, without the game: every spell through Noita's gun.lua against the golden file (one line when unchanged)
if ($Mode -in @("magic", "spells")) {
    $cli = Get-ChildItem (Join-Path $PSScriptRoot "..\src\Terranoita.Cli\bin\Release") -Recurse -Filter tncli.exe | Select-Object -First 1
    if ($cli) { & $cli.FullName lua-golden $Noita --check (Join-Path $PSScriptRoot "..\design\sources\lua_cast_golden.txt") | Select-Object -Last 20 }
}
Get-Process | Where-Object { $_.Name -match 'Terranoita' } | ForEach-Object { "a test game was still running: closed"; $_.Kill() }
# the PC has no page file: when Windows' commit runs out the game gets OutOfMemory and the whole PC hangs (2026-10-08)
$os = Get-CimInstance Win32_OperatingSystem
$freeGB = $os.FreeVirtualMemory / 1MB
if ($freeGB -lt 6) { "NOT STARTED: only {0:N1} GB of memory free (needs 6; close browser tabs or turn the page file on)" -f $freeGB; exit 1 }
Copy-Item (Join-Path $bin "Terranoita.Game.dll"), (Join-Path $bin "Terranoita.Core.dll"), (Join-Path $bin "MoonSharp.Interpreter.dll") -Destination $Terraria -Force -ErrorAction Stop
$data = Join-Path $env:LOCALAPPDATA "Terranoita"
$log = Join-Path $data "logs\latest.log"
if (Test-Path $log) { [IO.File]::Delete($log) }
$env:TERRANOITA_AUTOTEST = "1"; $env:TERRANOITA_AUTOTEST_EXIT = "1"
switch ($Mode) {
    "spells"  { $env:TERRANOITA_AUTOTEST_SPELLS = "1"; $filter = "SPELLS" }
    "wands"   { $env:TERRANOITA_AUTOTEST_WANDS = "1"; $filter = "WANDS|wand .* not made" }
    "sandbox" { $env:TERRANOITA_SANDBOX = "1"; $env:TERRANOITA_AUTOTEST_EXIT = "" }
    "tour"    { $env:TERRANOITA_TOUR = "1"; $env:TERRANOITA_AUTOTEST_EXIT = ""; $env:TERRANOITA_AUTOTEST_WORLDSIZE = "1"; $env:TERRANOITA_SCREENSHOTS = "1"
                $env:TERRANOITA_AUTOTEST_NEWWORLD = "Terranoita Tour " + (Get-Date -Format "MMdd-HHmm") }
    "magic"   { $env:TERRANOITA_AUTOTEST_MAGIC = "1"; $filter = "MAGIC|world loot|starting wands|screenshot" }
    "fps"     { $env:TERRANOITA_AUTOTEST_FPS = "1"; $filter = "PERF|cave pools|fluids:" }
    "physics" { $env:TERRANOITA_AUTOTEST_PHYSICS = "1"; $filter = "PHYSICS" }
    "gallery" { $env:TERRANOITA_AUTOTEST_LIQUIDS = "1"; $env:TERRANOITA_AUTOTEST_EXIT = ""; $filter = "GALLERY" }
    "audit"   { $env:TERRANOITA_AUTOTEST_LIQUIDS = "1"; $env:TERRANOITA_AUTOTEST_AUDIT = "1"; $filter = "AUDIT" }
    "enemies" { $env:TERRANOITA_AUTOTEST_PLACES = "1"; $env:TERRANOITA_AUTOTEST_SECONDS = "8"; $filter = "AUTOTEST|loot of|worm " }
}
if ($World) { $env:TERRANOITA_AUTOTEST_WORLD = $World }
# magic tests play in a world of their own, made by the game the first time (author: "test in a new world")
if ($Minutes -eq 0) { $Minutes = $(if ($Mode -eq "spells") { 12 } else { 6 }) }
if (-not $World -and $Mode -in @("magic", "wands", "spells", "sandbox")) { $env:TERRANOITA_AUTOTEST_NEWWORLD = "Terranoita Magic" }
if ($Only) { $env:TERRANOITA_AUTOTEST_ONLY = $Only }
if ($Mode -in @("sandbox", "tour")) {
    Start-Process -FilePath (Join-Path $Terraria "Terranoita.exe") -WorkingDirectory $Terraria `
        -ArgumentList @("--noita-dir", "`"$Noita`"", "-savedirectory", "`"$data\testsave`"") | Out-Null
    "$Mode started: the game stays open"
    exit 0
}
$p = Start-Process -FilePath (Join-Path $Terraria "Terranoita.exe") -WorkingDirectory $Terraria -PassThru -WindowStyle Minimized `
    -ArgumentList @("--noita-dir", "`"$Noita`"", "-savedirectory", "`"$data\testsave`"")
if (-not $p.WaitForExit($Minutes * 60000)) { Stop-Process -Id $p.Id -Force; "TIMEOUT" }
Get-Process | Where-Object { $_.Name -match 'Terranoita' } | ForEach-Object { "LEFT RUNNING: closed"; $_.Kill() }
$rowsFile = Join-Path $data "test_rows.txt"
if ($Mode -notin @("magic", "wands", "spells")) {
    Get-Content $log -Encoding UTF8 | Select-String "$filter|ERROR|Exception|hook MISSING" |
        ForEach-Object { $_.Line.Substring(0, [math]::Min(400, $_.Line.Length)) } | Select-Object -First 120
    exit 0
}
# ---- magic tests: one summary, the problems, the change against the baseline (author: an agent reads ~20 lines) ----
$rows = @()
if (Test-Path $rowsFile) { $rows = @(Get-Content $rowsFile -Encoding UTF8 | Where-Object { $_ -match '\S' }) }
function Status($row) { if ($row -match '^\S+ (.+?) shots ') { $Matches[1] } else { "?" } }
$counts = $rows | Group-Object { Status $_ } | Sort-Object Count -Descending | ForEach-Object { "$($_.Name) $($_.Count)" }
"$Mode : $($rows.Count) rows: " + ($counts -join ", ")
$rows | Where-Object { (Status $_) -ne "OK" } | Select-Object -First 12 | ForEach-Object { "  " + $_ }
$errors = Get-Content $log -Encoding UTF8 | Select-String "ERROR in [^:]+: .{0,80}|hook MISSING \S+|TIMEOUT" -AllMatches |
    ForEach-Object { $_.Matches[0].Value } | Group-Object | Sort-Object Count -Descending | Select-Object -First 5
$errors | ForEach-Object { "  x$($_.Count) $($_.Name)" }
Get-Content $log -Encoding UTF8 | Select-String "SPELLS memory|WANDS \d+ wands made|SPELLS \d+ to test" | ForEach-Object { "  " + $_.Line.Substring(13) }
$basePath = Join-Path $PSScriptRoot "..\design\sources\magic_baseline.txt"
$base = @{}
if (Test-Path $basePath) {
    Get-Content $basePath -Encoding UTF8 | Where-Object { $_ -match '^(\S+)\|(\S+) (.+)$' } | ForEach-Object {
        if ($_ -match '^(\S+)\|(\S+) (.+)$' -and $Matches[1] -eq $Mode) { $base[$Matches[2]] = $Matches[3] } }
}
$now = @{}
foreach ($r in $rows) { if ($r -match '^(\S+) ') { $now[$Matches[1]] = Status $r } }
$fixed = @(); $broken = @(); $newOnes = @()
foreach ($k in $now.Keys) {
    if (-not $base.ContainsKey($k)) { $newOnes += "$k $($now[$k])" }
    elseif ($base[$k] -ne $now[$k]) { if ($now[$k] -eq "OK") { $fixed += $k } else { $broken += "$k ($($base[$k]) -> $($now[$k]))" } }
}
if ($base.Count -eq 0) { "baseline: none for $Mode yet (run with -Accept to keep this one)" }
elseif ($fixed.Count + $broken.Count -eq 0) { "baseline: no change ($($newOnes.Count) new rows)" }
else {
    if ($fixed.Count) { "fixed: " + (($fixed | Select-Object -First 15) -join ", ") }
    if ($broken.Count) { "broken: " + (($broken | Select-Object -First 15) -join ", ") }
}
if ($Accept) {
    $keep = @()
    # rows of this mode not run now (passed before, skipped) stay as they were
    if (Test-Path $basePath) { $keep = @(Get-Content $basePath -Encoding UTF8 | Where-Object { -not ($_ -match "^$Mode\|(\S+) " -and $now.ContainsKey($Matches[1])) }) }
    $keep += ($now.Keys | Sort-Object | ForEach-Object { "$Mode|$_ $($now[$_])" })
    [IO.File]::WriteAllLines((Resolve-Path (Split-Path $basePath)).Path + "\magic_baseline.txt", $keep, (New-Object Text.UTF8Encoding $false))
    "baseline for $Mode updated ($($now.Count) rows)"
}
