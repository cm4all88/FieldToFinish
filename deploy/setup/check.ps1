<#
.SYNOPSIS
    Reports why Field to Finish is not showing up in Civil 3D.

.DESCRIPTION
    Writes ftf-check.txt next to this script (or to the desktop when the folder is
    read-only) with everything that decides whether Civil 3D loads FTF:

      which Autodesk releases are on the machine, and whether 2024 is one of them
      whether the plugin is installed, where, which build, and from which office copy
      whether Windows has marked the files as "from another computer"
      whether Civil 3D is set to load plugins at startup at all

    Reads only. Changes nothing, installs nothing.

.EXAMPLE
    .\check.ps1
#>
[CmdletBinding()]
param([string]$OutFile)

$ErrorActionPreference = 'Continue'
$lines = New-Object System.Collections.Generic.List[string]
function Say([string]$text) { $lines.Add($text); Write-Host $text }
function Section([string]$title) { Say ''; Say ('== ' + $title) }

$bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle'
$module = Join-Path $bundle 'Contents\2024\FieldCodes.Cad.dll'

Say 'FIELD TO FINISH -- INSTALLATION CHECK'
Say ((Get-Date).ToString('yyyy-MM-dd HH:mm'))
Say ("user " + $env:USERNAME + " on " + $env:COMPUTERNAME)

# --- what Autodesk releases are here ---------------------------------------------
Section 'Autodesk releases on this computer'
$found = @()
foreach ($root in @("$env:ProgramFiles\Autodesk", "${env:ProgramFiles(x86)}\Autodesk")) {
    if (-not (Test-Path $root)) { continue }
    foreach ($dir in Get-ChildItem $root -Directory -Filter 'AutoCAD 20*' -ErrorAction SilentlyContinue) {
        $civil = Test-Path (Join-Path $dir.FullName 'C3D')
        $found += [pscustomobject]@{ Name = $dir.Name; Civil = $civil }
        Say ("  " + $dir.Name + $(if ($civil) { '  (Civil 3D)' } else { '  (AutoCAD only -- no C3D folder)' }))
    }
}
if ($found.Count -eq 0) { Say '  none found in the usual place' }

$has2024 = $found | Where-Object { $_.Name -eq 'AutoCAD 2024' -and $_.Civil }
if ($has2024) {
    Say '  -> Civil 3D 2024 is here. FTF is built for this one.'
} else {
    Say '  -> PROBLEM: Civil 3D 2024 was not found. This FTF build only loads in Civil 3D 2024;'
    Say '     in any other release the ribbon tabs will simply not appear.'
}

# --- is it installed, and which build ---------------------------------------------
Section 'The plugin itself'
if (-not (Test-Path $bundle)) {
    Say ("  PROBLEM: nothing installed at " + $bundle)
    Say '  -> run Install FTF.bat again and read what it says.'
} else {
    Say ("  folder:  " + $bundle)
    foreach ($name in @('version.txt', 'source.txt')) {
        $file = Join-Path $bundle $name
        if (Test-Path $file) { Say ("  " + $name.PadRight(12) + (Get-Content $file -First 1)) }
        else { Say ("  " + $name.PadRight(12) + "(none)") }
    }
    if (Test-Path $module) {
        $item = Get-Item $module
        Say ("  program: " + $item.Name + ", " + [int]($item.Length / 1KB) + " KB, " + $item.LastWriteTime)
    } else {
        Say "  PROBLEM: the program file is missing -- the copy did not finish. Install again."
    }
    $count = (Get-ChildItem $bundle -Recurse -File -ErrorAction SilentlyContinue).Count
    Say ("  files:   " + $count)
}

# --- has Windows marked the files -------------------------------------------------
Section 'Marked as "came from another computer"'
$marked = @()
if (Test-Path $bundle) {
    foreach ($file in Get-ChildItem $bundle -Recurse -File -ErrorAction SilentlyContinue) {
        if (Get-Item $file.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue) { $marked += $file.Name }
    }
}
if ($marked.Count -gt 0) {
    Say ("  PROBLEM: " + $marked.Count + " file(s) are marked: " + ($marked -join ', '))
    Say '  -> Civil 3D will not load a marked plugin. Tell the drafting lead; a newer installer'
    Say '     clears this, or they can right-click each file, Properties, Unblock.'
} else {
    Say '  none -- nothing is blocked.'
}

# --- will Civil 3D load plugins at startup ----------------------------------------
Section 'Civil 3D plugin loading'
$profiles = @()
foreach ($key in Get-ChildItem 'HKCU:\SOFTWARE\Autodesk\AutoCAD' -ErrorAction SilentlyContinue) {
    foreach ($product in Get-ChildItem $key.PSPath -ErrorAction SilentlyContinue) {
        $general = Join-Path $product.PSPath 'Profiles'
        foreach ($profile in Get-ChildItem $general -ErrorAction SilentlyContinue) {
            $auto = $null
            $secure = $null
            foreach ($where in @('General', 'Variables', '')) {
                $path = if ($where) { Join-Path $profile.PSPath $where } else { $profile.PSPath }
                $vars = Get-ItemProperty $path -ErrorAction SilentlyContinue
                if ($null -eq $vars) { continue }
                if ($null -eq $auto) { $auto = $vars.APPAUTOLOAD }
                if ($null -eq $secure) { $secure = $vars.SECURELOAD }
            }
            $profiles += [pscustomobject]@{
                Release = $key.PSChildName; Profile = $profile.PSChildName
                AppAutoLoad = $auto; SecureLoad = $secure
            }
        }
    }
}
if ($profiles.Count -eq 0) {
    Say '  (no profile settings found -- normal on a machine where Civil 3D has not run yet)'
} else {
    foreach ($p in $profiles) {
        $note = ''
        if ($null -ne $p.AppAutoLoad -and ($p.AppAutoLoad -band 2) -eq 0) {
            $note = '   <- PROBLEM: startup loading of plugin bundles is switched off (APPAUTOLOAD)'
        }
        Say ("  " + $p.Release + " / " + $p.Profile + ": APPAUTOLOAD=" +
             $(if ($null -eq $p.AppAutoLoad) { 'default' } else { $p.AppAutoLoad }) +
             " SECURELOAD=" + $(if ($null -eq $p.SecureLoad) { 'default' } else { $p.SecureLoad }) + $note)
    }
}

# --- has an automatic update run here ----------------------------------------------
Section 'Automatic updates'
$updateLog = Join-Path $env:TEMP 'FTF-update.log'
if (Test-Path $updateLog) {
    Say ("  log: " + $updateLog)
    foreach ($line in (Get-Content $updateLog -Tail 12)) { Say ("    " + $line) }
} else {
    Say '  no update has ever run on this computer.'
}
foreach ($leftover in @(($bundle + '.new'), ($bundle + '.old'))) {
    if (Test-Path $leftover) {
        Say ("  PROBLEM: a half-finished update left " + $leftover)
        Say '  -> an update was interrupted. Run Install FTF.bat again; it clears these.'
    }
}

# --- what to do next ---------------------------------------------------------------
Section 'If the tabs are still missing'
Say '  In Civil 3D, type NETLOAD, choose this file, and then type FTF:'
Say ("    " + $module)
Say '  If the FTF window opens, the plugin is fine and only the automatic loading is not'
Say '  happening. If an error appears instead, send that exact wording on -- it names the cause.'

# --- save ---------------------------------------------------------------------------
if (-not $OutFile) {
    $here = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    $OutFile = Join-Path $here 'ftf-check.txt'
    try { Set-Content -Path $OutFile -Value 'test' -ErrorAction Stop; Remove-Item $OutFile -ErrorAction SilentlyContinue }
    catch { $OutFile = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ftf-check.txt' }
}
try {
    Set-Content -Path $OutFile -Value $lines -Encoding utf8
    Write-Host ''
    Write-Host ("Saved to " + $OutFile + " -- send that file to the drafting lead.")
}
catch { Write-Host ''; Write-Host "Could not save the report; copy the text above instead." }
