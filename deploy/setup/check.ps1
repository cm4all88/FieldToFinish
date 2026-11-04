<#
.SYNOPSIS
    Reports why Field to Finish is not showing up in Civil 3D.

.DESCRIPTION
    Writes ftf-check.txt next to this script (or to the desktop when the folder is
    read-only) with everything that decides whether Civil 3D loads FTF:

      which Autodesk releases are on the machine, and whether 2024 is one of them
      whether the plugin is installed, where, which build, and from which office copy
      whether every installed file is the one its build shipped (SHA-256)
      the approved release folder it updates from: reachable or not, which build is there
      anything else that loads a copy of FTF (a second bundle, an old startup entry, NETLOAD)
      whether Windows has marked the files as "from another computer"
      whether Civil 3D is set to load plugins at startup at all
      the last install or update, from its log

    Reads only. Changes nothing, installs nothing. A copy of the report is also kept in
    %LOCALAPPDATA%\FieldToFinish\logs.

.EXAMPLE
    .\check.ps1
#>
[CmdletBinding()]
param(
    [switch]$Repair,
    [string]$OutFile,
    [string]$RegistryRoot = 'HKCU:\SOFTWARE\Autodesk\AutoCAD',
    # For the installer's own test: a scratch plugin folder and state folder instead of the real ones.
    [string]$PluginsRoot,
    [string]$StateRoot
)

$ErrorActionPreference = 'Continue'
$lines = New-Object System.Collections.Generic.List[string]
function Say([string]$text) { $lines.Add($text); Write-Host $text }
function Section([string]$title) { Say ''; Say ('== ' + $title) }

if (-not $PluginsRoot) { $PluginsRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins' }
$bundle = Join-Path $PluginsRoot 'FieldToFinish.bundle'
$module = Join-Path (Join-Path (Join-Path $bundle 'Contents') '2024') 'FieldCodes.Cad.dll'
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$common = Join-Path $here 'ftf-common.ps1'
$haveCommon = Test-Path -LiteralPath $common
if ($haveCommon) { . $common }
$state = if ($StateRoot) { $StateRoot } elseif ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'FieldToFinish' } else { $null }

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
    Say '  -> run "Install or Update FieldToFinish.bat" in the approved release folder and read what it says.'
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

# --- which build, whole or not, and the approved release -----------------------------
if ($haveCommon -and (Test-Path $bundle)) {
    Section 'Installed build'
    $info = Read-FtfBuildInfo $bundle
    Say ("  " + (Get-FtfDescription $info))
    if ($info) {
        Say ("  commit:  " + $info.commit)
        $damage = @(Test-FtfBundle $bundle $info)
        if ($damage.Count -eq 0) { Say '  every installed file is the one this build shipped (SHA-256), and the FTF DLLs are all this build' }
        else {
            Say '  PROBLEM: the installed files are not what was installed:'
            foreach ($d in $damage) { Say ("    " + $d) }
            Say '  -> run "Install or Update FieldToFinish.bat" from the approved release folder; it repairs this.'
        }
    }
    Section 'Approved release folder'
    $sourceFile = Join-Path $bundle 'source.txt'
    $office = if (Test-Path $sourceFile) { (Get-Content $sourceFile -First 1) } else { $null }
    if (-not $office) {
        Say '  none recorded -- this FTF was not installed from an approved release folder, so it never updates itself.'
    } elseif (-not (Test-Path -LiteralPath $office)) {
        Say ("  " + $office)
        Say '  PROBLEM: it cannot be reached from this computer right now (network, VPN, drive letter).'
        Say '  -> FTF keeps working; updates wait until the folder can be reached.'
    } else {
        $approved = Read-FtfBuildInfo (Join-Path $office 'Bundle')
        Say ("  " + $office)
        Say ("  release there: " + (Get-FtfDescription $approved))
        if ($approved -and $info -and "$($approved.commit)" -eq "$($info.commit)") { Say '  -> this computer has the approved release.' }
        elseif ($approved) { Say '  -> a different approved release is there: close Civil 3D and run "Install or Update FieldToFinish.bat" in that folder.' }
    }
    Section 'Other copies of FTF'
    $roots = @($PluginsRoot)
    if (-not $StateRoot) {
        if ($env:ProgramData) { $roots += (Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins') }
        if ($env:ProgramFiles) { $roots += (Join-Path $env:ProgramFiles 'Autodesk\ApplicationPlugins') }
    }
    $others = @(Find-FtfOtherLoaders $module $bundle $roots $RegistryRoot $(if ($StateRoot) { $null } else { $env:APPDATA }))
    if ($others.Count -eq 0) { Say '  none -- Civil 3D loads one FTF, from the installed bundle.' }
    else {
        foreach ($o in $others) { Say ("  PROBLEM: " + $o.Text) }
        Say '  -> each loads a second FTF beside the installed one. "Install or Update FieldToFinish.bat" switches off'
        Say '     the per-user ones; the Startup Suite (APPLOAD) and acad*.lsp NETLOAD lines are removed by hand.'
    }
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

# --- what Civil 3D has recorded about FTF ------------------------------------------
# Civil 3D keeps its own note of each plugin it has seen. A note left pointing at files
# that went away, or one it disabled after a failed load, makes it ignore a perfectly good
# bundle however many times it is reinstalled. Deleting the note is safe: Civil 3D writes
# a fresh one from the bundle at the next start.
Section "Civil 3D's own record of FTF"
$records = @()
foreach ($release in Get-ChildItem $RegistryRoot -ErrorAction SilentlyContinue) {
    foreach ($product in Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue) {
        $apps = Join-Path $product.PSPath 'Applications'
        foreach ($app in Get-ChildItem $apps -ErrorAction SilentlyContinue) {
            if ($app.PSChildName -notmatch 'FieldToFinish') { continue }
            $values = Get-ItemProperty $app.PSPath -ErrorAction SilentlyContinue
            $records += [pscustomobject]@{
                Where = $release.PSChildName + '\' + $product.PSChildName
                Path = $app.PSPath
                LoadCtrls = $values.LOADCTRLS
                Loader = $values.LOADER
            }
        }
    }
}

if ($records.Count -eq 0) {
    Say '  Civil 3D has no record of FTF.'
    Say '  -> It has never managed to load the bundle. Start Civil 3D once after installing;'
    Say '     if this still says nothing afterwards, the bundle is being ignored outright.'
} else {
    foreach ($r in $records) {
        Say ("  " + $r.Where + ": LOADCTRLS=" + $r.LoadCtrls)
        Say ("    loader: " + $r.Loader)
        if (-not $r.Loader) {
            Say '    PROBLEM: the record has no program file at all.'
        } elseif (-not (Test-Path $r.Loader)) {
            Say '    PROBLEM: that file does not exist -- the record is stale, left from an older install.'
        } elseif ($r.LoadCtrls -eq 0) {
            Say '    PROBLEM: Civil 3D has this plugin switched off (LOADCTRLS=0).'
        } else {
            Say '    looks right (a working machine reads LOADCTRLS=2).'
        }
    }
    $bad = @($records | Where-Object { (-not $_.Loader) -or (-not (Test-Path $_.Loader)) -or ($_.LoadCtrls -eq 0) })
    if ($bad.Count -gt 0 -and -not $Repair) {
        Say ''
        Say '  -> Run "Repair FTF.bat" in this folder. It deletes the bad record so Civil 3D'
        Say '     writes a fresh one next time it starts. Nothing else is touched.'
    }
    if ($Repair) {
        Say ''
        foreach ($r in $records) {
            try {
                Remove-Item -LiteralPath $r.Path -Recurse -Force -ErrorAction Stop
                Say ("  removed Civil 3D's record at " + $r.Where)
            }
            catch { Say ("  could not remove " + $r.Where + ": " + $_.Exception.Message) }
        }
        Say '  -> Start Civil 3D. It will read the bundle again and write a new record.'
    }
}

# --- does this profile keep its settings on a server --------------------------------
Section 'Windows profile'
$roaming = $null
try {
    $sid = ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
    $profile = Get-CimInstance Win32_UserProfile -Filter ("SID='" + $sid + "'") -ErrorAction SilentlyContinue
    if ($profile) { $roaming = $profile.RoamingConfigured }
}
catch { }
Say ("  APPDATA: " + $env:APPDATA)
Say ("  roaming profile: " + $(if ($null -eq $roaming) { 'unknown' } elseif ($roaming) { 'YES -- a profile reset can take FTF with it' } else { 'no' }))

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
        Say '  -> an update was interrupted. Run "Install or Update FieldToFinish.bat" again; it clears these.'
    }
}

# --- the last install or update ----------------------------------------------------
if ($state) {
    Section 'Last install or update'
    $last = Get-ChildItem (Join-Path $state 'logs') -Filter 'install-*.log' -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if ($last) {
        Say ("  log: " + $last.FullName)
        foreach ($line in (Get-Content $last.FullName -Tail 15)) { Say ("    " + $line) }
    } else { Say '  no install log on this computer (installed before install logs, or never installed here).' }
    $backups = @(Get-ChildItem (Join-Path $state 'backups') -Directory -ErrorAction SilentlyContinue)
    Say ("  backups: " + $backups.Count + $(if ($backups.Count -gt 0) { ' ("Restore previous FieldToFinish.bat" puts the newest one back)' } else { '' }))
}

# --- what to do next ---------------------------------------------------------------
Section 'If the tabs are still missing'
Say '  1. Close Civil 3D, run "Repair FTF.bat", then start Civil 3D again.'
Say '  2. Still missing: send this report to the drafting lead.'
Say '  Never NETLOAD FTF from the network folder: a copy loaded from there breaks whenever the'
Say '  folder changes. For the drafting lead only, to tell a loading problem from a program one:'
Say ("    NETLOAD the installed file " + $module + ", then type FTF.")

# --- save ---------------------------------------------------------------------------
# On the desktop, where the person running it can find it -- and not inside the setup
# folder, which is usually a shared drive everyone else installs from.
if (-not $OutFile) {
    $OutFile = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ftf-check.txt'
}
if ($state) {
    try {
        $kept = Join-Path (Join-Path $state 'logs') ('ftf-check-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')
        New-Item -ItemType Directory -Force -Path (Split-Path $kept -Parent) | Out-Null
        Set-Content -Path $kept -Value $lines -Encoding utf8
    } catch { }
}
try {
    Set-Content -Path $OutFile -Value $lines -Encoding utf8
    Write-Host ''
    Write-Host ("Saved to " + $OutFile + " -- send that file to the drafting lead.")
}
catch { Write-Host ''; Write-Host "Could not save the report; copy the text above instead." }
