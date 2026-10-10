# Multiplayer test on this PC (PC-38): our server (Terranoita.exe -server, the same as Host & Play starts) on the test
# world, port 7779, then a test client that joins it (game_test -Mode mp). Prints the client's MPTEST lines and the
# server's creature lines, then closes both. Only test games (testsave) are started and closed.
#   powershell -ExecutionPolicy Bypass -File tools/mp_test.ps1 [-TestSpawn] [-Minutes 3]
# -TestSpawn: the server also puts a weak Noita zombie next to every player every 5 s (TERRANOITA_TEST_SPAWN).
# -Two: a second test client ("Terranoita Test 2", log client2.log) joins too and casts a Spark Bolt every second;
#       the first client counts the other player's shots it shows (remote shots).
param([switch]$TestSpawn, [switch]$Two, [int]$Minutes = 3)
$T = "D:\steam\steamapps\common\Terraria"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$bin = Join-Path $repo "src\Terranoita\bin\Release\net48"
$save = Join-Path $env:LOCALAPPDATA "Terranoita\testsave"
$logs = Join-Path $env:LOCALAPPDATA "Terranoita\logs"
$theirs = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'Terranoita' -and $_.CommandLine -notmatch 'testsave' })
if ($theirs.Count -gt 0) { "the author's game is running (pid $($theirs[0].ProcessId)): no test now"; exit 1 }
Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'Terranoita' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Copy-Item (Join-Path $repo "src\Terranoita.Launcher\bin\Release\net48\Terranoita.exe"), (Join-Path $bin "Terranoita.Game.dll"), (Join-Path $bin "Terranoita.Core.dll"), (Join-Path $bin "MoonSharp.Interpreter.dll") $T -Force -ErrorAction Stop
$world = Join-Path $save "Worlds\Terranoita_Magic.wld"
if ($TestSpawn) { $env:TERRANOITA_TEST_SPAWN = "1" }
$srv = Start-Process -FilePath (Join-Path $T "Terranoita.exe") -WorkingDirectory $T -PassThru -WindowStyle Minimized -ArgumentList @(
    "-server", "--noita-dir", "`"D:\steam\steamapps\common\Noita`"", "-savedirectory", "`"$save`"", "-world", "`"$world`"", "-port", "7779")
Remove-Item Env:TERRANOITA_TEST_SPAWN -ErrorAction SilentlyContinue
$ok = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    if ((Get-NetTCPConnection -LocalPort 7779 -State Listen -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0) { $ok = $true; break }
    if ($srv.HasExited) { break }
}
"server listening: $ok after $($i * 2) s"
$c2 = $null
if ($ok -and $Two) {
    $env:TERRANOITA_AUTOTEST = "1"; $env:TERRANOITA_AUTOTEST_EXIT = "1"; $env:TERRANOITA_AUTOTEST_JOIN = "127.0.0.1:7779"
    $env:TERRANOITA_AUTOTEST_PLAYER = "Terranoita Test 2"; $env:TERRANOITA_MPTEST_CAST = "1"; $env:TERRANOITA_LOG = "client2"
    $c2 = Start-Process -FilePath (Join-Path $T "Terranoita.exe") -WorkingDirectory $T -PassThru -WindowStyle Minimized -ArgumentList @(
        "--noita-dir", "`"D:\steam\steamapps\common\Noita`"", "-savedirectory", "`"$save`"", "-mptest2")
    foreach ($v in "TERRANOITA_AUTOTEST", "TERRANOITA_AUTOTEST_EXIT", "TERRANOITA_AUTOTEST_JOIN", "TERRANOITA_AUTOTEST_PLAYER", "TERRANOITA_MPTEST_CAST", "TERRANOITA_LOG") { Remove-Item "Env:$v" -ErrorAction SilentlyContinue }
}
try {
    if ($ok) { powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "game_test.ps1") -Mode mp -Minutes ($Minutes + 1) | Out-Null }
    "== client"
    Select-String -Path (Join-Path $logs "latest.log") -Pattern "MPTEST|joining|ERROR" | Select-Object -Last 16 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(200, $_.Line.Length)) }
    if ($Two) {
        "== client 2 (casts)"
        Select-String -Path (Join-Path $logs "client2.log") -Pattern "MPTEST|joining|ERROR" | Select-Object -Last 4 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(200, $_.Line.Length)) }
        Select-String -Path (Join-Path $logs "latest.log") -Pattern "net: cast" | Select-Object -First 3 | ForEach-Object { $_.Line }
    }
    "== server"
    $s = Join-Path $logs "server.log"
    Select-String -Path $s -Pattern "ERROR|FATAL" | Select-Object -Last 5 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(200, $_.Line.Length)) }
    Select-String -Path $s -Pattern "TEST server" | Select-Object -Last 4 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(200, $_.Line.Length)) }
    $sp = @(Select-String -Path $s -Pattern " spawned ([a-z0-9_]+)" | ForEach-Object { $_.Matches[0].Groups[1].Value })
    "server spawned $($sp.Count): " + (($sp | Group-Object | Sort-Object Count -Descending | ForEach-Object { "$($_.Name) x$($_.Count)" }) -join ", ")
}
finally {
    Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'Terranoita' -and $_.CommandLine -match 'testsave' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
}
