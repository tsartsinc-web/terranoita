# The Noita probe run with nobody at the PC (design/magic_plan.md PC-22/PC-28; author allowed running Noita 2026-10-09):
#   powershell -ExecutionPolicy Bypass -File tools/noita_probe/run_probe.ps1 [-Minutes 240]
# 1. Backs up Noita's settings (save_shared/config.xml, save00/mod_config.xml) and the player's current run (save00/world)
#    to %LOCALAPPDATA%\Terranoita\noita_probe_backup, installs the probe mod, enables it, allows its file output
#    (mods_sandbox_enabled 0) and keeps Noita running when unfocused.
# 2. Starts Noita; Enter presses skip the intro and start a new game (an empty save00/world: no Continue entry).
#    The mod fires by itself (PlatformShooterPlayerComponent.mForceFireOnNextUpdate, verified in Noita) and resumes
#    after a restart (tests already in probe_out.jsonl are skipped). Noita restarts its own process once: the run is
#    watched through the output file, not the process.
# 3. Stops when every test is written, the status file has an error, or nothing new comes for 10 minutes; closes Noita,
#    puts the settings and the player's run back, copies the output to design/sources/noita_probe.jsonl.
param([int]$Minutes = 240)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$noita = "D:\steam\steamapps\common\Noita"
$saves = Join-Path $env:USERPROFILE "AppData\LocalLow\Nolla_Games_Noita"
$config = Join-Path $saves "save_shared\config.xml"
$modConfig = Join-Path $saves "save00\mod_config.xml"
$world = Join-Path $saves "save00\world"
$backup = Join-Path $env:LOCALAPPDATA "Terranoita\noita_probe_backup"
$mod = Join-Path $noita "mods\terranoita_probe"
$out = Join-Path $mod "probe_out.jsonl"
$status = Join-Path $mod "probe_status.txt"

if (Get-Process | Where-Object { $_.ProcessName -eq "noita" }) { "Noita is running (the author's?): no probe run now"; exit 1 }

# 1. back up and prepare
if (Test-Path $backup) { "a backup from an unfinished run is still in ${backup}: restore it first"; exit 1 }
New-Item -ItemType Directory -Force (Join-Path $backup "world") | Out-Null
Copy-Item $config (Join-Path $backup "config.xml")
Copy-Item $modConfig (Join-Path $backup "mod_config.xml")
Get-ChildItem $world -Force | Move-Item -Destination (Join-Path $backup "world")
Copy-Item (Join-Path $backup "world\steam_autocloud.vdf") $world -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $mod | Out-Null
Copy-Item -Recurse -Force (Join-Path $PSScriptRoot "terranoita_probe\*") $mod
Remove-Item $status -ErrorAction SilentlyContinue
$c = Get-Content $config -Raw
$c = $c -replace 'mods_sandbox_enabled="1"', 'mods_sandbox_enabled="0"' -replace 'mods_disclaimer_accepted="0"', 'mods_disclaimer_accepted="1"' `
        -replace 'mods_sandbox_warning_done="0"', 'mods_sandbox_warning_done="1"' -replace 'application_pause_when_unfocused="1"', 'application_pause_when_unfocused="0"'
[IO.File]::WriteAllText($config, $c, (New-Object Text.UTF8Encoding($false)))   # no BOM (PowerShell 5.1 UTF8 adds one)
$m = Get-Content $modConfig -Raw
if ($m -notmatch 'name="terranoita_probe"') {
    $m = $m -replace '</Mods>', "  <Mod enabled=`"1`" name=`"terranoita_probe`" settings_fold_open=`"0`" workshop_item_id=`"0`" >`r`n  </Mod>`r`n</Mods>"
} else { $m = $m -replace 'enabled="0" name="terranoita_probe"', 'enabled="1" name="terranoita_probe"' }
[IO.File]::WriteAllText($modConfig, $m, (New-Object Text.UTF8Encoding($false)))

try {
    # 2. start Noita into a new game
    $p = Start-Process -FilePath (Join-Path $noita "noita.exe") -WorkingDirectory $noita -PassThru
    $ws = New-Object -ComObject WScript.Shell
    Start-Sleep -Seconds 12
    for ($i = 0; $i -lt 6; $i++) { [void]$ws.AppActivate($p.Id); $ws.SendKeys("{ENTER}"); Start-Sleep -Seconds 4 }
    # 3. wait
    $deadline = (Get-Date).AddMinutes($Minutes); $last = -1; $lastChange = Get-Date
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 30
        if ((Test-Path $out) -and (Select-String -Path $out -Pattern '"done":true' -Quiet)) { "probe done"; break }
        if ((Test-Path $status) -and (Select-String -Path $status -Pattern 'ERROR|stopped' -Quiet)) { "probe stopped: see $status"; break }
        $n = $(if (Test-Path $out) { (Get-Content $out).Count } else { 0 })
        if ($n -ne $last) { $last = $n; $lastChange = Get-Date }
        if (((Get-Date) - $lastChange).TotalMinutes -gt 10) { "stalled at $n lines"; break }
        if (-not (Get-Process | Where-Object { $_.ProcessName -eq "noita" })) { "Noita is gone at $n lines"; break }
    }
}
finally {
    Get-Process | Where-Object { $_.ProcessName -eq "noita" } | Stop-Process -Force
    Start-Sleep -Seconds 3
    # put everything back: settings, and the player's run in place of the probe's
    Copy-Item (Join-Path $backup "config.xml") $config -Force
    Copy-Item (Join-Path $backup "mod_config.xml") $modConfig -Force
    Get-ChildItem $world -Force | Remove-Item -Recurse -Force
    Get-ChildItem (Join-Path $backup "world") -Force | Move-Item -Destination $world
    Remove-Item -Recurse -Force $backup
    if (Test-Path $out) { Copy-Item $out (Join-Path $repo "design\sources\noita_probe.jsonl") -Force }
    "lines: " + $(if (Test-Path $out) { (Get-Content $out).Count } else { 0 })
    if (Test-Path $status) { Get-Content $status | Select-Object -Last 3 }
}
