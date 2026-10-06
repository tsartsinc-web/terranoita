<#
Terranoita: the work that needs the author's PC (the games), done by one script so no agent time is spent on it.
Run it from the repository folder, in PowerShell:

    powershell -ExecutionPolicy Bypass -File tools\pc_step.ps1                 # facts + build check (about 5 minutes)
    powershell -ExecutionPolicy Bypass -File tools\pc_step.ps1 -AutoTest 1b    # also plays that stage's enemies in Terraria

What it does, in order (it stops at the first failure and says which step):
  1. updates the branch from GitHub
  2. reads facts from the player's Noita into design/sources/noita_facts.json (tncli facts; read-only)
  3. applies them to the sheets (tools/apply_facts.py) and runs preflight for the stage; when it is clean, generates
     the stage's tables (tools/gen_cs.py --gate); -AutoTest stops here when it is not
  4. runs the Core tests and builds Terranoita.exe and Terranoita.Game.dll against the player's Terraria.exe
  5. with -AutoTest: copies the mod next to Terraria.exe, starts it with the test character on a copy of the save
     folder, spawns that stage's enemies one by one, closes the game when done and keeps the log
  6. writes design/sources/pc_check.txt (what passed, compiler errors, preflight summary) and pushes all of it
A cloud session continues from what it pushed: compiler errors, the autotest log and the facts are all in the repo.
Nothing here needs a person except approving the run; the game window opens only with -AutoTest.
#>
param(
    [string]$Noita = "D:\steam\steamapps\common\Noita",
    [string]$Terraria = "D:\steam\steamapps\common\Terraria",
    [string]$Branch = "claude/dazzling-carson-h8mi9n",
    [string]$Stage = "1b",
    [string]$AutoTest = "",
    [int]$AutoTestMinutes = 0,
    [int]$AutoTestSeconds = 6,
    [switch]$NoPush,
    [switch]$NoUpdate
)

$ErrorActionPreference = "Continue"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$report = New-Object System.Collections.Generic.List[string]
$started = Get-Date
New-Item -ItemType Directory -Force -Path (Join-Path $repo "build") | Out-Null

function Say([string]$text) {
    Write-Host $text -ForegroundColor Cyan
    $report.Add($text) | Out-Null
}

# Runs a native command, keeps its output in build\<log>.log and returns its exit code.
function Run([string]$name, [string]$log, [scriptblock]$body) {
    Say "== $name"
    $path = Join-Path $repo ("build/" + $log + ".log")
    & $body *>&1 | Tee-Object -FilePath $path | Out-Host
    $code = $LASTEXITCODE
    if ($code -ne 0) { Say "   FAILED (exit $code), output: build/$log.log" } else { Say "   ok" }
    return $code
}

function Finish([string]$outcome) {
    $report.Add("") | Out-Null
    $report.Add("outcome: $outcome") | Out-Null
    $report.Add("took: " + [int]((Get-Date) - $started).TotalMinutes + " min") | Out-Null
    $out = Join-Path $repo "design/sources/pc_check.txt"
    [System.IO.File]::WriteAllLines($out, $report)
    Write-Host "report: design/sources/pc_check.txt" -ForegroundColor Yellow
    if ($NoPush) { exit 0 }
    $paths = @("design/sources/noita_facts.json", "design/sources/pc_check.txt", "design/sheets", "src/Terranoita.Core/Generated", "src/Terranoita/Generated") +
             @(Get-ChildItem design/sources -Filter "pc_autotest_*.txt" | ForEach-Object { "design/sources/" + $_.Name })
    if (Test-Path design/sources/noita_spells.json) { $paths += "design/sources/noita_spells.json" }
    git add -- $paths
    git commit -m "PC step ($outcome): facts from Noita, build check$(if ($AutoTest) { ", autotest $AutoTest" })" | Out-Host
    for ($i = 0; $i -lt 4; $i++) {
        git push origin $Branch | Out-Host
        if ($LASTEXITCODE -eq 0) { Write-Host "pushed to $Branch" -ForegroundColor Green; exit 0 }
        Start-Sleep -Seconds ([math]::Pow(2, $i + 1))
    }
    Write-Host "push failed: run 'git push origin $Branch' yourself" -ForegroundColor Red
    exit 1
}

$python = (Get-Command python -ErrorAction SilentlyContinue)
if (-not $python) { $python = (Get-Command py -ErrorAction SilentlyContinue) }
if (-not $python) { Say "Python 3 is missing (https://www.python.org/downloads/): install it and run again"; Finish "blocked" }
$python = $python.Source
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Say ".NET 8 SDK is missing (https://dotnet.microsoft.com/download/dotnet/8.0)"; Finish "blocked" }
if (-not (Test-Path (Join-Path $Noita "data/data.wak"))) { Say "Noita not found at $Noita (pass -Noita <folder>)"; Finish "blocked" }
if (-not (Test-Path (Join-Path $Terraria "Terraria.exe"))) { Say "Terraria not found at $Terraria (pass -Terraria <folder>)"; Finish "blocked" }

Say ("Terranoita PC step " + (Get-Date -Format "yyyy-MM-dd HH:mm") + ", branch $Branch, stage $Stage")
Say ("dotnet " + (dotnet --version) + ", " + (& $python --version 2>&1))

if (-not $NoUpdate -and (Run "update branch" "pc_git" { git fetch origin $Branch; git checkout $Branch; git pull --ff-only origin $Branch }) -ne 0) { Finish "git failed" }
$head = (git rev-parse --short HEAD)
Say "commit $head"

if ((Run "facts from Noita" "pc_facts" { dotnet run --project src/Terranoita.Cli -c Release -- facts $Noita design/sheets/enemies.json design/sources/noita_facts.json }) -ne 0) { Finish "facts failed" }
if ((Run "apply facts" "pc_apply" { & $python tools/apply_facts.py design/sources/noita_facts.json --stage $Stage --sounds design/sources/noita_sounds.txt }) -ne 0) { Finish "apply_facts failed" }
# stage 3 groundwork: spells and wands from the player's Noita (a failure here does not stop the step)
if ((Run "spell facts from Noita" "pc_spells" { dotnet run --project src/Terranoita.Cli -c Release -- spells $Noita design/sources/noita_spells.json }) -eq 0) {
    Run "apply spells" "pc_apply_spells" { & $python tools/apply_spells.py design/sources/noita_spells.json } | Out-Null
}
$gate = Run "preflight $Stage" "pc_preflight" { & $python tools/preflight.py --gate $Stage -q }
Get-Content build/pc_preflight.log | Select-Object -Last 5 | ForEach-Object { $report.Add("   " + $_) | Out-Null }
if ($gate -eq 0) {
    # the stage's sheets are complete: build its tables (Core and game) from them
    if ((Run "generate tables for $Stage" "pc_gen" { & $python tools/gen_cs.py --gate $Stage }) -ne 0) { Finish "gen_cs failed" }
} elseif ($AutoTest -eq $Stage) {
    & $python tools/preflight.py --gate $Stage | Select-String "^(UNFILLED|UNVERIFIED|BAD)" | Select-Object -First 40 |
        ForEach-Object { $report.Add("   " + $_.Line.Substring(0, [math]::Min(110, $_.Line.Length))) | Out-Null }
}

$tests = Run "Core tests" "pc_tests" { dotnet test tests/Terranoita.Core.Tests -c Release }
$launcher = Run "build Terranoita.exe" "pc_launcher" { dotnet build src/Terranoita.Launcher -c Release }
$game = Run "build Terranoita.Game.dll" "pc_game" { dotnet build src/Terranoita/Terranoita.Game.csproj -c Release "-p:TerrariaDir=$Terraria" }
if ($game -ne 0) {
    $report.Add("   compiler errors:") | Out-Null
    $prefix = [regex]::Escape($repo) + "[\\/]"
    Select-String -Path build/pc_game.log -Pattern "error CS" |
        ForEach-Object { (($_.Line.Trim() -replace $prefix, "") -replace "\s*\[[^\]]*\]$", "") } |
        Sort-Object -Unique | Select-Object -First 80 | ForEach-Object { $report.Add("   " + $_) | Out-Null }
}
if ($tests -ne 0 -or $launcher -ne 0 -or $game -ne 0) { Finish "build or tests failed" }

if ($AutoTest -and $AutoTest -eq $Stage -and $gate -ne 0) {
    Say "== autotest $AutoTest skipped: preflight --gate $AutoTest is not clean (open cells listed above)"
    Finish "gate not clean"
}
if ($AutoTest) {
    Say "== autotest $AutoTest"
    $files = @()
    $files += Get-ChildItem -Recurse src/Terranoita.Launcher/bin/Release -Include Terranoita.exe, Terranoita.exe.config, Terranoita.Core.dll
    $files += Get-ChildItem -Recurse src/Terranoita/bin/Release -Include Terranoita.Game.dll, 0Harmony.dll
    foreach ($f in $files) { Copy-Item $f.FullName -Destination $Terraria -Force }
    Say ("   copied next to Terraria.exe: " + (($files | ForEach-Object { $_.Name } | Sort-Object -Unique) -join ", "))

    $data = Join-Path $env:LOCALAPPDATA "Terranoita"
    $save = Join-Path $data "testsave"
    if (-not (Test-Path (Join-Path $save "Worlds"))) {
        # a copy of the player's worlds, so the test never touches the real ones
        $docs = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "My Games/Terraria"
        New-Item -ItemType Directory -Force -Path (Join-Path $save "Worlds"), (Join-Path $save "Players") | Out-Null
        Get-ChildItem (Join-Path $docs "Worlds") -Filter *.wld | Select-Object -First 1 | Copy-Item -Destination (Join-Path $save "Worlds")
        Say "   made a test save folder with a copy of one world"
    }
    $count = [int](& $python -c "import json;print(sum(1 for e in json.load(open('design/sheets/enemies.json',encoding='utf-8'))['rows'] if e['stage']=='$AutoTest'))")
    if ($AutoTestMinutes -le 0) { $AutoTestMinutes = [int][math]::Ceiling(($count * $AutoTestSeconds + 120) / 60.0) + 3 }
    Say "   $count enemies, $AutoTestSeconds s each; waiting up to $AutoTestMinutes min"

    # a stale log from an earlier run must not pass for this one
    Remove-Item (Join-Path $data "logs/latest.log") -Force -ErrorAction SilentlyContinue
    $env:TERRANOITA_AUTOTEST = "1"
    $env:TERRANOITA_STAGE = $AutoTest
    $env:TERRANOITA_AUTOTEST_STAGE = $AutoTest
    $env:TERRANOITA_AUTOTEST_EXIT = "1"
    $env:TERRANOITA_AUTOTEST_SECONDS = "$AutoTestSeconds"
    $p = Start-Process -FilePath (Join-Path $Terraria "Terranoita.exe") -WorkingDirectory $Terraria -PassThru `
        -ArgumentList @("--noita-dir", "`"$Noita`"", "-savedirectory", "`"$save`"")
    if (-not $p.WaitForExit($AutoTestMinutes * 60 * 1000)) { Stop-Process -Id $p.Id -Force; Say "   stopped after $AutoTestMinutes min (did not finish)" }
    foreach ($v in "TERRANOITA_AUTOTEST", "TERRANOITA_STAGE", "TERRANOITA_AUTOTEST_STAGE", "TERRANOITA_AUTOTEST_EXIT", "TERRANOITA_AUTOTEST_SECONDS") { Remove-Item "env:$v" -ErrorAction SilentlyContinue }

    $log = Join-Path $data "logs/latest.log"
    if (Test-Path $log) {
        Copy-Item $log ("design/sources/pc_autotest_" + $AutoTest + ".txt") -Force
        $lines = Get-Content $log
        $errors = @($lines | Where-Object { $_ -match "ERROR|FATAL|WARN" })
        $spawned = @($lines | Where-Object { $_ -match "AUTOTEST: spawning" }).Count
        $hits = @($lines | Where-Object { $_ -match " hits for " }).Count
        Say "   spawned $spawned, hits on the player $hits, errors/warnings $($errors.Count) (log: design/sources/pc_autotest_$AutoTest.txt)"
        $errors | Select-Object -First 40 | ForEach-Object { $report.Add("   " + $_) | Out-Null }
        if (-not ($lines -match "AUTOTEST: done")) { Finish "autotest did not finish" }
    } else { Say "   no log at $log"; Finish "autotest gave no log" }
}

Finish "ok"
