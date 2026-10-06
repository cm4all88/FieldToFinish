<#
.SYNOPSIS
  Windows smoke test for Crew Upload + the optional Survey Schedule integration.

.DESCRIPTION
  Walks a tester through the checks in deploy\SMOKE-TEST.md and records the answers. Safe on the real
  share by construction:

  - The Schedule folder is only READ (ScheduleCheck.exe and Crew Upload open it read-only). Every
    schedule file is fingerprinted (SHA-256) before and after; the result says whether any changed.
  - scheduleReportStatus is forced OFF in every test config, so pso-progress.json is never written.
  - Crew Upload runs against a TEST project list, crew list and Survey folder under -TestRoot, so the
    real project registry and real project folders are not touched.
  - Admin copies go to -AdminFolder (default: a DailyReports-SmokeTest folder beside the real one), so
    the real admin view is not filled with test reports.

  Results go to <TestRoot>\smoke-results.txt -- send that file back.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File schedule-smoke-test.ps1 -AppDir "C:\Tools\CrewUpload" -ProjectNumber 554-1800-119 -Dates 2026-07-13,2026-07-14
#>
param(
    # Folder holding CrewUpload.exe, CrewUpload.Schedule.dll, ScheduleCheck.exe and job-folders.json.
    [string]$AppDir = (Split-Path -Parent $MyInvocation.MyCommand.Path),
    [string]$ScheduleFolder = '\\parametrix.com\pmx\PSO\Shared\Divisions\00Survey\OFC_RSC\Schedule',
    [string]$TestRoot = (Join-Path $env:USERPROFILE 'Documents\CrewUploadSmokeTest'),
    [string]$AdminFolder = '\\parametrix.com\pmx\PSO\Shared\Divisions\00Survey\FLD\CrewUpload\DailyReports-SmokeTest',
    # A real 3-4-3 project the tester is scheduled on today; registered only in the test project list.
    [Parameter(Mandatory = $true)][string]$ProjectNumber,
    # Dates to compare with the Schedule's board (yyyy-MM-dd). Today is always included.
    [string[]]$Dates = @()
)

$ErrorActionPreference = 'Stop'
$results = New-Object System.Collections.Generic.List[string]
function Log([string]$line) { Write-Host $line; $results.Add($line) | Out-Null }
function Save-Results { $results | Set-Content -Path (Join-Path $TestRoot 'smoke-results.txt') -Encoding UTF8 }

function Ask([string]$id, [string]$instructions) {
    Write-Host ''
    Write-Host ('[' + $id + '] ' + $instructions) -ForegroundColor Cyan
    do { $a = (Read-Host '  Result: p = pass, f = fail, s = skip') } until ($a -in 'p', 'f', 's')
    $note = Read-Host '  Notes (Enter for none)'
    $word = @{ p = 'PASS'; f = 'FAIL'; s = 'SKIP' }[$a]
    Log ("{0,-5} {1}  {2}" -f $word, $id, $note)
}

function Launch([string]$dir, [string]$config, [string[]]$extra) {
    $argList = @('--config', ('"' + $config + '"')) + $extra
    return Start-Process -FilePath (Join-Path $dir 'CrewUpload.exe') -ArgumentList $argList -PassThru
}

function Wait-Closed($p) {
    Write-Host '  ... close Crew Upload when done.' -ForegroundColor DarkGray
    $p.WaitForExit()
}

function Fingerprint() {
    $list = @{}
    Get-ChildItem -LiteralPath $ScheduleFolder -Filter '*.json' | ForEach-Object {
        $h = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        $list[$_.Name] = ('{0} {1} {2:o}' -f $h.Hash, $_.Length, $_.LastWriteTimeUtc)
    }
    return $list
}

function Write-Config([string]$name, [scriptblock]$change) {
    $c = Get-Content -LiteralPath (Join-Path $AppDir 'job-folders.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $c.registryFile = (Join-Path $TestRoot 'Config\project-registry.json')
    $c.requireUncPaths = $false
    $c.features.scheduleIntegration = $true
    $c.features.schedulePrefill = $true
    $c.features.dailyReports = $true
    $c.schedule.folder = $ScheduleFolder
    $c.dailyReport.adminFolder = $AdminFolder
    $c.dailyReport.localFolder = (Join-Path $TestRoot 'Local')
    & $change $c
    $c.features.scheduleReportStatus = $false   # never write pso-progress.json in this test
    $path = Join-Path $TestRoot ($name + '.json')
    $c | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

# ---------------------------------------------------------------- setup
New-Item -ItemType Directory -Force -Path $TestRoot, (Join-Path $TestRoot 'Config'), (Join-Path $TestRoot 'Local') | Out-Null
foreach ($f in 'CrewUpload.exe', 'CrewUpload.Core.dll', 'CrewUpload.Schedule.dll', 'ScheduleCheck.exe', 'job-folders.json') {
    if (-not (Test-Path (Join-Path $AppDir $f))) { throw "$f is not in $AppDir. Point -AppDir at the Crew Upload build folder." }
}
Log ('Crew Upload smoke test  ' + (Get-Date -Format 'yyyy-MM-dd HH:mm') + '  ' + $env:COMPUTERNAME + '\' + $env:USERNAME)
Log ('App: ' + $AppDir + '   version ' + (Get-Item (Join-Path $AppDir 'CrewUpload.exe')).VersionInfo.FileVersion)
Log ('Schedule: ' + $ScheduleFolder)
Log ('Test area: ' + $TestRoot + '   admin copies: ' + $AdminFolder)

$parts = $ProjectNumber.Split('-')
if ($parts.Count -ne 3) { throw 'Give the full 3-4-3 project number, like 554-1800-119.' }
$survey = Join-Path $TestRoot ('Clients\' + $parts[1] + '-SmokeTest\' + $ProjectNumber + ' Smoke Test\99Svcs\Survey')
New-Item -ItemType Directory -Force -Path $survey | Out-Null
Log ('Test Survey folder (register this one): ' + $survey)

$cfgOn = Write-Config 'integration-on' { param($c) }
$cfgOff = Write-Config 'integration-off' { param($c) $c.features.scheduleIntegration = $false }
$cfgAdminDown = Write-Config 'admin-unreachable' { param($c) $c.dailyReport.adminFolder = '\\no-such-server.invalid\DailyReports' }
$noDll = Join-Path $TestRoot 'app-without-schedule-dll'
if (Test-Path $noDll) { Remove-Item -Recurse -Force $noDll }
Copy-Item -Recurse -Path $AppDir -Destination $noDll
Remove-Item -Force (Join-Path $noDll 'CrewUpload.Schedule.dll')

# ---------------------------------------------------------------- read-only schedule check
$before = Fingerprint
$check = Join-Path $TestRoot 'schedule-check.txt'
$dateArgs = @('--date', (Get-Date -Format 'yyyy-MM-dd'))
foreach ($d in $Dates) { $dateArgs += @('--date', $d) }
& (Join-Path $AppDir 'ScheduleCheck.exe') $ScheduleFolder @dateArgs --assembled (Join-Path $TestRoot 'crewupload-assembled.json') --out $check | Out-Null
Log ('ScheduleCheck exit code ' + $LASTEXITCODE + ' (0 = read, nothing changed); report: ' + $check)
Write-Host ''
Write-Host 'Open schedule-check.txt next to the Schedule board for each date (see SMOKE-TEST.md, "Comparing with the board").' -ForegroundColor Yellow
Start-Process notepad.exe $check
Ask '4' 'Real Schedule files were read from the share (schedule-check.txt lists people, projects and entries, no "Schedule unavailable").'
Ask 'C1' 'People: every person on the board for those dates appears in schedule-check.txt, with the same names.'
Ask 'C2' 'Projects and dates: each person''s entries match the board (project, type, dates; multi-day entries on every day of the range).'
Ask 'C3' 'Overrides: an entry another PM changed shows as on the board (marked OVERRIDDEN in the report).'
Ask 'C4' 'Time off: OFF entries match the board. (Generated 9-80 Fridays / 4-10s are not listed; Crew Upload does not use them.)'
Ask 'C5' 'Someone with more than one entry in a day (marked "<-- more than one entry") matches the board, if there is one.'
Ask '6' 'Crew grouping: the crews listed match who the board shows together on each project; note any "withIds differs" lines.'

# ---------------------------------------------------------------- starting up
$p = Launch $noDll $cfgOn @()
Ask '3' 'With CrewUpload.Schedule.dll removed: Crew Upload starts normally, no TODAY card, no errors.'
Wait-Closed $p
$p = Launch $AppDir $cfgOff @()
Ask '2' 'With features.scheduleIntegration OFF: Crew Upload starts normally, no TODAY card, no errors.'
Wait-Closed $p

Write-Host ''
Write-Host 'Next: PM setup in the TEST project list (nothing real is changed).' -ForegroundColor Yellow
Write-Host ('  1. Project setup (PM): register ' + $ProjectNumber + ' to ' + $survey)
Write-Host '  2. Schedule project -> Link...: pick the schedule project. Check the suggested one is right.'
Write-Host '  3. Crew & work types: add yourself (initials, Windows sign-in, Schedule person) and your crew.'
Write-Host '     Map only clear activity names to work types; leave the rest unmapped.'
$p = Launch $AppDir $cfgOn @('--setup')
Ask '1' 'With the integration ON: Crew Upload starts normally (with --setup).'
Ask '7' 'Project linking shows the correct Schedule project (name in the Schedule project row and column).'
Ask '8' 'Crew & work types: the Schedule person column shows names (Jeff Bearson), not ids (jeff_bearson); saved links reopen correctly.'
Wait-Closed $p

# ---------------------------------------------------------------- the daily report
$p = Launch $AppDir $cfgOn @()
Ask '5' 'TODAY shows your assignment(s) for today, as on the board, with your name.'
Ask '9' 'Daily Report on the TODAY card opens the report window with the schedule choices at the top.'
Ask '10' 'Prefill: project, job name, task, crew and data file name are right; work type only where clearly mapped (blank otherwise).'
Ask '11' 'Every prefilled field can be changed (try project #, crew, task, work type, date).'
Ask '12' '"Not listed / Enter manually" clears the schedule values and leaves a blank report you can fill in.'
Write-Host '  Pick your assignment again, fill hours and equipment, write TEST in Comments, and Submit.'
Ask '13' 'The report submitted (summary lists Project, Admin, This PC and Record).'
Wait-Closed $p

$unprocessed = Join-Path $survey '02Field\01FLD_DR_FN_DCfile\Unprocessed'
$local = @(Get-ChildItem -LiteralPath (Join-Path $TestRoot 'Local') -Filter '*-DR*.pdf' -ErrorAction SilentlyContinue)
$crew = @(Get-ChildItem -LiteralPath $unprocessed -Recurse -Filter '*-DR*.pdf' -ErrorAction SilentlyContinue)
$admin = @(Get-ChildItem -LiteralPath $AdminFolder -Recurse -Filter '*-DR*.pdf' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\Records\\' })
$records = @(Get-ChildItem -LiteralPath (Join-Path $AdminFolder 'Records') -Recurse -Filter 'DR-*.json' -ErrorAction SilentlyContinue)
Log ('{0,-5} 14  local PDF: {1}' -f $(if ($local.Count) { 'PASS' } else { 'FAIL' }), ($local | Select-Object -Last 1).FullName)
Log ('{0,-5} 15  crew/download PDF: {1}' -f $(if ($crew.Count) { 'PASS' } else { 'FAIL' }), ($crew | Select-Object -Last 1).FullName)
Log ('{0,-5} 16  admin PDF: {1}' -f $(if ($admin.Count) { 'PASS' } else { 'FAIL' }), ($admin | Select-Object -Last 1).FullName)
Log ('{0,-5} 17  record: {1}' -f $(if ($records.Count) { 'PASS' } else { 'FAIL' }), ($records | Select-Object -Last 1).FullName)
if ($records.Count) {
    $r = Get-Content -Raw -LiteralPath ($records | Sort-Object LastWriteTime | Select-Object -Last 1).FullName | ConvertFrom-Json
    Log ('      record: ReportID ' + $r.ReportID + ', Date ' + $r.Date + ', Project ' + $r.ProjectNumber + ', ScheduleProjectID ' + $r.ScheduleProjectID + ', Source ' + $r.Source)
}

# admin folder unreachable
$outbox = Join-Path $env:LOCALAPPDATA 'FieldToFinish\CrewUpload\DailyReports\Outbox'
$waitingBefore = @(Get-ChildItem -LiteralPath $outbox -Filter '*.json' -ErrorAction SilentlyContinue).Count
$p = Launch $AppDir $cfgAdminDown @()
Write-Host '  Admin folder is set to an unreachable server for this run. Submit another TEST report.'
Ask '18a' 'Submit still succeeds; the summary says the admin copy failed and the record will be sent next time; My reports shows it "waiting to send".'
Wait-Closed $p
$waitingAfter = @(Get-ChildItem -LiteralPath $outbox -Filter '*.json' -ErrorAction SilentlyContinue).Count
Log ('{0,-5} 18  outbox held {1} record(s) before, {2} after' -f $(if ($waitingAfter -gt $waitingBefore) { 'PASS' } else { 'FAIL' }), $waitingBefore, $waitingAfter)
$p = Launch $AppDir $cfgOn @()
Ask '18b' 'With the admin folder reachable again, My reports -> Send waiting records (or a restart) sends it; it shows "recorded".'
Wait-Closed $p

$p = Launch $AppDir $cfgOn @('--setup')
Ask '19' 'Project setup -> Scheduled vs reported (today): your crew shows "Reported" with the new ReportID.'
Wait-Closed $p

# ---------------------------------------------------------------- the upload workflow
$wt = Read-Host 'Work type code to use for the test upload (one in the Work type list, e.g. TOPO or STK)'
$initials = Read-Host 'Your crew initials'
$dl = Join-Path $TestRoot ('FLD_Download\' + (Get-Date -Format 'yyyyMMdd') + '-' + $initials.ToUpper() + '-' + $parts[1] + '-' + $parts[2] + '-' + $wt.ToUpper())
New-Item -ItemType Directory -Force -Path (Join-Path $dl 'Photos') | Out-Null
'smoke test job file' | Set-Content (Join-Path $dl ((Split-Path -Leaf $dl) + '.job'))
'smoke test notes' | Set-Content (Join-Path $dl ((Split-Path -Leaf $dl) + '-FN.pdf'))
[IO.File]::WriteAllBytes((Join-Path $dl 'Photos\IMG_0001.JPG'), [byte[]](0xFF, 0xD8, 0xFF, 0xD9))
Write-Host ('  Drag ' + $dl + ' onto Crew Upload''s boxes and Upload to job.')
Start-Process explorer.exe (Split-Path -Parent $dl)
$p = Launch $AppDir $cfgOn @($ProjectNumber)
Ask '20' 'The usual upload works: files planned and named as before, upload completes and verifies.'
Wait-Closed $p
$uploaded = Join-Path $unprocessed (Split-Path -Leaf $dl)
Log ('{0,-5} 20  upload folder {1} ({2} files, manifest {3})' -f $(if (Test-Path (Join-Path $uploaded 'upload-manifest.csv')) { 'PASS' } else { 'FAIL' }),
    $uploaded, @(Get-ChildItem -LiteralPath $uploaded -Recurse -File -ErrorAction SilentlyContinue).Count, (Test-Path (Join-Path $uploaded 'upload-manifest.csv')))

# ---------------------------------------------------------------- standalone Schedule, and nothing written
Ask 'S1' 'Open the Survey Schedule as usual: it works as before and shows the same board.'
$after = Fingerprint
$changed = @(@($before.Keys) + @($after.Keys) | Sort-Object -Unique | Where-Object { $before[$_] -ne $after[$_] })
if ($changed.Count -eq 0) { Log ('PASS  RO  all ' + $after.Count + ' schedule files unchanged (SHA-256, size and time identical before and after)') }
else {
    Log ('CHECK RO  changed during the test: ' + ($changed -join ', ') + '. Crew Upload only reads them (and pso-progress.json was switched off),')
    Log '          so this is a PM saving the schedule meanwhile -- check the time and who saved.'
}
Save-Results
Write-Host ''
Write-Host ('Done. Results: ' + (Join-Path $TestRoot 'smoke-results.txt')) -ForegroundColor Green
