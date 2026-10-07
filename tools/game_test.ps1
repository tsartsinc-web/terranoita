# One minimized autotest run of the game on the author's PC (author: only in the background, minimized, one at a time,
# short; tell the author first). Copies the fresh build next to Terraria.exe, runs, prints the interesting log lines.
#   powershell -File tools/game_test.ps1 -Mode magic            # MAGIC: wands, casting, screenshots
#   powershell -File tools/game_test.ps1 -Mode fps -World test1 # PERF: physics time by cave pools
#   powershell -File tools/game_test.ps1 -Mode enemies -Only worm,eel
# Modes: magic, fps, physics, gallery, audit, enemies. Screenshots: %LOCALAPPDATA%/Terranoita/shots/*.png
param(
    [string]$Mode = "magic",
    [string]$World = "",
    [string]$Only = "",
    [int]$Minutes = 4,
    [string]$Terraria = "D:\steam\steamapps\common\Terraria",
    [string]$Noita = "D:\steam\steamapps\common\Noita"
)
$bin = Join-Path $PSScriptRoot "..\src\Terranoita\bin\Release\net48"
Get-Process | Where-Object { $_.Name -match 'Terranoita' } | ForEach-Object { "a test game was still running: closed"; $_.Kill() }
Copy-Item (Join-Path $bin "Terranoita.Game.dll"), (Join-Path $bin "Terranoita.Core.dll"), (Join-Path $bin "MoonSharp.Interpreter.dll") -Destination $Terraria -Force -ErrorAction Stop
$data = Join-Path $env:LOCALAPPDATA "Terranoita"
$log = Join-Path $data "logs\latest.log"
if (Test-Path $log) { [IO.File]::Delete($log) }
$env:TERRANOITA_AUTOTEST = "1"; $env:TERRANOITA_AUTOTEST_EXIT = "1"
switch ($Mode) {
    "magic"   { $env:TERRANOITA_AUTOTEST_MAGIC = "1"; $filter = "MAGIC|world loot|starting wands|screenshot" }
    "fps"     { $env:TERRANOITA_AUTOTEST_FPS = "1"; $filter = "PERF|cave pools|fluids:" }
    "physics" { $env:TERRANOITA_AUTOTEST_PHYSICS = "1"; $filter = "PHYSICS" }
    "gallery" { $env:TERRANOITA_AUTOTEST_LIQUIDS = "1"; $env:TERRANOITA_AUTOTEST_EXIT = ""; $filter = "GALLERY" }
    "audit"   { $env:TERRANOITA_AUTOTEST_LIQUIDS = "1"; $env:TERRANOITA_AUTOTEST_AUDIT = "1"; $filter = "AUDIT" }
    "enemies" { $env:TERRANOITA_AUTOTEST_PLACES = "1"; $env:TERRANOITA_AUTOTEST_SECONDS = "8"; $filter = "AUTOTEST|loot of|worm " }
}
if ($World) { $env:TERRANOITA_AUTOTEST_WORLD = $World }
if ($Only) { $env:TERRANOITA_AUTOTEST_ONLY = $Only }
$p = Start-Process -FilePath (Join-Path $Terraria "Terranoita.exe") -WorkingDirectory $Terraria -PassThru -WindowStyle Minimized `
    -ArgumentList @("--noita-dir", "`"$Noita`"", "-savedirectory", "`"$data\testsave`"")
if (-not $p.WaitForExit($Minutes * 60000)) { Stop-Process -Id $p.Id -Force; "TIMEOUT" }
Get-Process | Where-Object { $_.Name -match 'Terranoita' } | ForEach-Object { "LEFT RUNNING: closed"; $_.Kill() }
Get-Content $log -Encoding UTF8 | Select-String "$filter|ERROR|Exception|hook MISSING" |
    ForEach-Object { $_.Line.Substring(0, [math]::Min(400, $_.Line.Length)) } | Select-Object -First 120
