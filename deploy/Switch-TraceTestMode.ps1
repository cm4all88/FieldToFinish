<#
.SYNOPSIS
    Reversibly sets an existing FieldToFinish.bundle aside while you test TRACE in Civil 3D,
    then puts it back and PROVES it is byte-for-byte the same.  UNTESTED on Windows.

.DESCRIPTION
    Both editions define the same FTF commands, so they must not be active in the same
    Civil 3D session. Civil 3D only loads folders whose name ends in ".bundle", so renaming
    FieldToFinish.bundle to FieldToFinish.bundle.paused switches it off without changing
    a single file inside it.

      -Begin   Fingerprints every file (SHA-256), then renames <x>.bundle -> <x>.bundle.paused
      -End     Renames it back, fingerprints again, and reports IDENTICAL or lists differences
      -Status  Shows what is where

    This script only ever renames that one folder. It never copies, deletes, writes or
    edits anything inside it. It refuses to run while Civil 3D is open, and refuses to
    rename back over an existing folder.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\deploy\Switch-TraceTestMode.ps1 -Begin
    # ... test TRACE in Civil 3D ...
    powershell -ExecutionPolicy Bypass -File .\deploy\Switch-TraceTestMode.ps1 -End
#>
[CmdletBinding()]
param(
    [switch]$Begin,
    [switch]$End,
    [switch]$Status
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TraceEnv.ps1')
$repo = Get-TraceRepoRoot
$logs = Join-Path $repo 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null

$roots = @(
    (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'),
    (Join-Path $env:ProgramFiles 'Autodesk\ApplicationPlugins'),
    (Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins')
)
$name   = 'FieldToFinish.bundle'
$paused = 'FieldToFinish.bundle.paused'

function Get-Fingerprint {
    param([string]$Folder)
    $root = (Resolve-Path -LiteralPath $Folder).Path.TrimEnd('\')
    return @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($root.Length + 1) + '|' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}

function Get-StateFile { param([string]$Folder) return (Join-Path $logs ('fieldtofinish-fingerprint-' + ($Folder -replace '[:\\ ]', '_') + '.txt')) }

if (-not ($Begin -or $End -or $Status)) { Write-Error 'Choose one of -Begin, -End or -Status.' }
if (($Begin -and $End)) { Write-Error 'Choose only one of -Begin or -End.' }

if ($Status) {
    foreach ($r in $roots) {
        foreach ($n in @($name, $paused)) {
            $p = Join-Path $r $n
            if (Test-Path -LiteralPath $p) { Write-Host ('present: ' + $p) }
        }
    }
    $any = $roots | ForEach-Object { Join-Path $_ $name } | Where-Object { Test-Path -LiteralPath $_ }
    $off = $roots | ForEach-Object { Join-Path $_ $paused } | Where-Object { Test-Path -LiteralPath $_ }
    if (-not $any -and -not $off) { Write-Host 'No FieldToFinish bundle found in any standard location.' }
    elseif ($off) { Write-Host 'TRACE test mode is ON (FieldToFinish is set aside). Run -End when finished.' }
    else { Write-Host 'FieldToFinish is active (test mode is off).' }
    return
}

$running = Get-Process -Name acad -ErrorAction SilentlyContinue
if ($running) { Write-Error 'Civil 3D is running. Close it first.' }

if ($Begin) {
    $done = 0
    foreach ($r in $roots) {
        $from = Join-Path $r $name
        $to   = Join-Path $r $paused
        # Already set aside (for example renamed by hand): fingerprint it where it is, so -End can
        # still prove nothing changed from now on. Nothing is renamed or touched.
        if ((-not (Test-Path -LiteralPath $from)) -and (Test-Path -LiteralPath $to)) {
            $fp = Get-Fingerprint $to
            $fp | Out-File -FilePath (Get-StateFile $from) -Encoding utf8
            Write-Host ("Already set aside: " + $to + "  (fingerprinted " + $fp.Count + " files in place; nothing was changed)")
            $done++
            continue
        }
        if (-not (Test-Path -LiteralPath $from)) { continue }
        if (Test-Path -LiteralPath $to) { Write-Error ("Refusing: '$to' already exists. Run -End first or resolve it by hand.") }
        $fp = Get-Fingerprint $from
        $fp | Out-File -FilePath (Get-StateFile $from) -Encoding utf8
        Write-Host ("Fingerprinted " + $fp.Count + " files in " + $from)
        Rename-Item -LiteralPath $from -NewName $paused
        Write-Host ("Set aside: " + $from + "  ->  " + $paused)
        $done++
    }
    if ($done -eq 0) { Write-Host 'Nothing to set aside: no FieldToFinish.bundle in the standard locations.' }
    else { Write-Host ''; Write-Host 'Done. Start Civil 3D and test TRACE. When finished, close Civil 3D and run this script with -End.' }
    return
}

if ($End) {
    $done = 0
    $bad = 0
    foreach ($r in $roots) {
        $from = Join-Path $r $paused
        $to   = Join-Path $r $name
        if (-not (Test-Path -LiteralPath $from)) { continue }
        if (Test-Path -LiteralPath $to) { Write-Error ("Refusing: '$to' already exists, so '$from' was left exactly as it is.") }
        Rename-Item -LiteralPath $from -NewName $name
        Write-Host ("Restored: " + $to)
        $stateFile = Get-StateFile $to
        if (Test-Path -LiteralPath $stateFile) {
            $before = @(Get-Content -LiteralPath $stateFile)
            $after  = Get-Fingerprint $to
            if ((($before -join "`n")) -eq (($after -join "`n"))) {
                Write-Host ("IDENTICAL: all " + $after.Count + " files match the fingerprint taken before the test.") -ForegroundColor Green
            } else {
                $bad++
                Write-Host 'DIFFERENCES FOUND:' -ForegroundColor Red
                Compare-Object $before $after | ForEach-Object { Write-Host ('   ' + $_.SideIndicator + ' ' + $_.InputObject) }
            }
        } else {
            Write-Host 'No saved fingerprint for this folder, so it could not be compared.' -ForegroundColor Yellow
        }
        $done++
    }
    if ($done -eq 0) { Write-Host 'Nothing to restore: no FieldToFinish.bundle.paused found.' }
    if ($bad -gt 0) { exit 1 }
    return
}
