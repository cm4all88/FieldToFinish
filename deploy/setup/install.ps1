<#
.SYNOPSIS
    Installs or updates Field to Finish for the person running it, from an approved release
    folder. No admin rights, no NETLOAD.

.DESCRIPTION
    What "Install or Update FieldToFinish.bat" runs, from a local copy of the release folder it has
    already staged in %TEMP%:

      1. Civil 3D must be closed (it holds FTF open while it runs).
      2. Reads the installed build and the release's build (build-info.json in each).
      3. Decides: install, update, repair a damaged install, or nothing to do.
      4. Verifies the release is complete and is what was built: every file's SHA-256 against its
         build-info.json, nothing extra, every FTF assembly stamped with the same commit, and an
         approved release (a development build is refused unless -AllowDevelopmentBuild).
      5. Backs up the installed FTF to %LOCALAPPDATA%\FieldToFinish\backups (the last three kept).
      6. Copies the release beside the installed bundle, verifies the copy, carries over the files
         the person or the office saved inside the old bundle (the office settings level), and only
         then swaps the two folders.
      7. Verifies the installed files again, and tells Civil 3D to load it at startup. If either
         fails, the previous FTF is put straight back: never a partly installed bundle.
      8. Finds anything else that loads a copy of FTF (an old startup entry pointing at the network,
         a second bundle, the Startup Suite, an acad*.lsp NETLOAD) and switches off the per-user ones.
      9. Says what is installed, where the approved release is, and what happened.

    Only %APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle, Civil 3D's per-user startup
    record for it, and %LOCALAPPDATA%\FieldToFinish (logs, backups, local tools) are written. Office
    standards, personal settings, profiles and project files (%APPDATA%\FieldToFinish, and files
    beside drawings) are never touched.

    Every run writes a log: %LOCALAPPDATA%\FieldToFinish\logs\install-<time>.log.

    Exit codes: 0 installed, updated or already up to date; 1 not installed (the FTF already on the
    computer is unchanged, or was put back).

.PARAMETER Uninstall
    Removes the installed bundle (backed up first). Office configuration stays.

.PARAMETER RestorePrevious
    Puts back the most recent backup (what "Restore previous FieldToFinish.bat" runs).

.PARAMETER CheckOnly
    Compares and verifies, and says what installing would do. Changes nothing.

.PARAMETER Force
    Reinstalls even when the installed build is already the release's.

.PARAMETER AllowDevelopmentBuild
    Installs a release that is not an approved build. UPDATEFTF -Dev passes it; staff never need it.

.PARAMETER Source
    The release folder to remember as the approved release location. Defaults to this script's
    folder. "Install or Update FieldToFinish.bat" passes the original folder, because it stages
    itself locally first.

.PARAMETER WaitForCivil3D
    Waits for Civil 3D to close instead of refusing, then installs. How the in-Civil 3D update
    (FTFUPDATE) applies itself; it also writes to %TEMP%\FTF-update.log.

.PARAMETER NoOfficeCopy
    A developer install: records no approved release location, so this install never replaces
    itself from one, accepts a development build, and switches no other copy of FTF off (as
    -AllowDevelopmentBuild -LeaveOtherLoaders). UPDATEFTF.ps1 passes it: a developer's machine
    takes builds from the integration branch. The batch file and the automatic update never do.

.PARAMETER NoBackup
    No backup of its own (UPDATEFTF makes its own, with its own restore).

.PARAMETER LeaveOtherLoaders
    Reports other copies of FTF that load into Civil 3D but switches none off (UPDATEFTF reports
    them itself, and -FixStartup there decides).

.PARAMETER TargetRoot
.PARAMETER StateRoot
.PARAMETER RegistryRoot
.PARAMETER ProductRoot
.PARAMETER WaitMinutes
.PARAMETER ProcessName
.PARAMETER TestFault
    For the installer's own tests, which must not disturb a real install or a real Civil 3D: an
    ApplicationPlugins folder, a state folder and registry keys to use instead of the real ones,
    what -WaitForCivil3D waits for and how long, and a failure to inject after the swap.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -CheckOnly
    .\install.ps1 -RestorePrevious
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$RestorePrevious,
    [switch]$CheckOnly,
    [switch]$Force,
    [switch]$AllowDevelopmentBuild,
    [string]$Source,
    [switch]$WaitForCivil3D,
    [switch]$NoOfficeCopy,
    [switch]$NoBackup,
    [switch]$LeaveOtherLoaders,
    [string]$TargetRoot,
    [string]$StateRoot,
    [int]$WaitMinutes = 60,
    [string]$ProcessName = 'acad',
    [string]$RegistryRoot = 'HKCU:\SOFTWARE\Autodesk\AutoCAD',
    [string]$ProductRoot = 'HKLM:\SOFTWARE\Autodesk\AutoCAD',
    [ValidateSet('', 'AfterSwap', 'Verify')][string]$TestFault = ''
)

$ErrorActionPreference = 'Stop'
$bundleName = 'FieldToFinish.bundle'
# -NoOfficeCopy is a developer install (UPDATEFTF, from a package on this computer); the batch file
# and the automatic update never pass it. Earlier UPDATEFTFs pass only -NoOfficeCopy, so it also
# accepts a development build and leaves other copies of FTF to UPDATEFTF -FixStartup.
if ($NoOfficeCopy) { $AllowDevelopmentBuild = $true; $LeaveOtherLoaders = $true }
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$from = Join-Path $here 'Bundle'
$tempRoot = if ($env:TEMP) { $env:TEMP } else { [IO.Path]::GetTempPath() }

$testInstall = -not [string]::IsNullOrWhiteSpace($TargetRoot)
$root = if ($testInstall) { $TargetRoot } else { Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins' }
$target = Join-Path $root $bundleName
# A test install keeps its logs and backups beside its scratch plugin folder, never in the real profile.
$stateGiven = $PSBoundParameters.ContainsKey('StateRoot')
if (-not $StateRoot) {
    if ($testInstall) { $StateRoot = Join-Path (Split-Path $TargetRoot -Parent) 'ftf-state' }
    elseif ($env:LOCALAPPDATA) { $StateRoot = Join-Path $env:LOCALAPPDATA 'FieldToFinish' }
    else { $StateRoot = Join-Path $tempRoot 'FieldToFinish' }
}
$watchCivil3D = (-not $testInstall) -or $PSBoundParameters.ContainsKey('ProcessName')
$legacyLog = Join-Path $tempRoot 'FTF-update.log'
$logDir = Join-Path $StateRoot 'logs'
$runStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logFile = Join-Path $logDir ("install-$runStamp.log")
try { New-Item -ItemType Directory -Force -Path $logDir | Out-Null } catch { }

function Log([string]$message)
{
    $line = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '  ' + $message
    try { Add-Content -Path $logFile -Value $line -Encoding utf8 -ErrorAction Stop } catch { }
    if ($WaitForCivil3D) { Add-Content -Path $legacyLog -Value $line -ErrorAction SilentlyContinue }
}

function Say([string]$message)
{
    Write-Host $message
    Log $message
}

Log ("install.ps1 started by " + $env:USERNAME + " on " + $env:COMPUTERNAME + " from " + $here)

# Set the moment the installed bundle is actually touched, so a failure can say truthfully
# whether the FTF already on this machine is still there.
$script:changed = $false

function Fail([string]$message)
{
    Say ''
    Say '  ---------------------------------------------------------------'
    foreach ($line in ($message -split "`n")) { Say ("  " + $line.TrimEnd()) }
    if ((-not $script:changed) -and (Test-Path $target)) {
        Say '  The FTF you already had is still installed and was not changed.'
    }
    Say "  Log: $logFile"
    Say '  ---------------------------------------------------------------'
    Say ''
    exit 1
}

# --- the parts of this release folder -----------------------------------------------

$common = Join-Path $here 'ftf-common.ps1'
if (-not (Test-Path -LiteralPath $common)) {
    Fail "This setup folder is incomplete -- ftf-common.ps1 is missing. Copy the whole release folder, then run it again."
}
. $common

# --- Civil 3D's startup record ------------------------------------------------------
# Civil 3D keeps a record of each plugin it loads at startup. It writes that record itself
# after reading a bundle -- but only on a machine that scans the plugin folder, and some do
# not. Writing the same record directly makes FTF load either way. Per user, no admin, and
# removed again by the uninstaller. Only where Civil 3D actually is: the other AutoCAD product
# keys would try to load a Civil-only assembly and fail noisily at startup.

function Civil3DProducts()
{
    $found = @()
    foreach ($release in Get-ChildItem $RegistryRoot -ErrorAction SilentlyContinue) {
        foreach ($product in Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue) {
            $where = Join-Path $ProductRoot ($release.PSChildName + '\' + $product.PSChildName)
            $name = (Get-ItemProperty -Path $where -ErrorAction SilentlyContinue).ProductName
            if ($name -notmatch 'Civil') { continue }
            $found += [pscustomobject]@{ Key = $product.PSPath; Name = $name; Where = $release.PSChildName + '\' + $product.PSChildName }
        }
    }
    return $found
}

function Register([string]$loader)
{
    $done = @()
    foreach ($product in (Civil3DProducts)) {
        $key = Join-Path $product.Key 'Applications\FieldToFinish'
        try {
            if (-not (Test-Path $key)) { New-Item -Path $key -Force -ErrorAction Stop | Out-Null }
            New-ItemProperty -Path $key -Name DESCRIPTION -Value 'Field to Finish' -PropertyType String -Force -ErrorAction Stop | Out-Null
            New-ItemProperty -Path $key -Name LOADCTRLS -Value 2 -PropertyType DWord -Force -ErrorAction Stop | Out-Null
            New-ItemProperty -Path $key -Name MANAGED -Value 1 -PropertyType DWord -Force -ErrorAction Stop | Out-Null
            New-ItemProperty -Path $key -Name LOADER -Value $loader -PropertyType String -Force -ErrorAction Stop | Out-Null
            $done += $product.Name
        }
        catch { Say ("  (could not tell " + $product.Name + " to load it: " + $_.Exception.Message + ")") }
    }
    return $done
}

function Unregister()
{
    $done = @()
    foreach ($release in Get-ChildItem $RegistryRoot -ErrorAction SilentlyContinue) {
        foreach ($product in Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue) {
            $key = Join-Path $product.PSPath 'Applications\FieldToFinish'
            if (-not (Test-Path $key)) { continue }
            try { Remove-Item -Path $key -Recurse -Force -ErrorAction Stop; $done += $product.PSChildName }
            catch { }
        }
    }
    return $done
}

# Civil 3D's records for FTF as they are now, so a rollback can put them back as they were.
function Save-Registration()
{
    $saved = @()
    foreach ($release in Get-ChildItem $RegistryRoot -ErrorAction SilentlyContinue) {
        foreach ($product in Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue) {
            $key = Join-Path $product.PSPath 'Applications\FieldToFinish'
            $p = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
            if ($null -eq $p) { continue }
            $saved += [pscustomobject]@{ Key = $key; Description = "$($p.DESCRIPTION)"; LoadCtrls = $p.LOADCTRLS; Managed = $p.MANAGED; Loader = "$($p.LOADER)" }
        }
    }
    return $saved
}

function Restore-Registration($saved)
{
    [void](Unregister)
    foreach ($s in @($saved)) {
        try {
            New-Item -Path $s.Key -Force -ErrorAction Stop | Out-Null
            New-ItemProperty -Path $s.Key -Name DESCRIPTION -Value $s.Description -PropertyType String -Force | Out-Null
            if ($null -ne $s.LoadCtrls) { New-ItemProperty -Path $s.Key -Name LOADCTRLS -Value $s.LoadCtrls -PropertyType DWord -Force | Out-Null }
            if ($null -ne $s.Managed) { New-ItemProperty -Path $s.Key -Name MANAGED -Value $s.Managed -PropertyType DWord -Force | Out-Null }
            New-ItemProperty -Path $s.Key -Name LOADER -Value $s.Loader -PropertyType String -Force | Out-Null
        }
        catch { Say ("  (could not restore Civil 3D's startup record " + $s.Key + ": " + $_.Exception.Message + ")") }
    }
}

function TryRename([string]$from, [string]$toLeaf)
{
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try { Rename-Item -LiteralPath $from -NewName $toLeaf -ErrorAction Stop; return $true }
        catch { Start-Sleep -Milliseconds 500 }
    }
    return $false
}

function Get-InstalledDll([string]$bundleDir)
{
    return (Get-ChildItem (Join-Path $bundleDir 'Contents') -Recurse -Filter 'FieldCodes.Cad.dll' -ErrorAction SilentlyContinue | Select-Object -First 1)
}

# --- backups ------------------------------------------------------------------------

$backupRoot = Join-Path $StateRoot 'backups'

function Save-Backup([string]$why)
{
    if (-not (Test-Path $target)) { return $null }
    $info = Read-FtfBuildInfo $target
    $dir = Join-Path $backupRoot ("install-$runStamp-" + (Get-FtfShort $info))
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item -LiteralPath $target -Destination $dir -Recurse -Force
    [ordered]@{ why = $why; savedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'); build = (Get-FtfDescription $info)
                registration = @(Save-Registration) } | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $dir 'backup.json') -Encoding utf8
    # Kept: the newest backup of each of the three most recent builds, so reinstalling one build
    # never pushes the one before it out. UPDATEFTF's own backups are left alone.
    $seen = @{}
    foreach ($o in @(Get-ChildItem -LiteralPath $backupRoot -Directory -Filter 'install-*' -ErrorAction SilentlyContinue | Sort-Object Name -Descending)) {
        $c = "$((Read-FtfBuildInfo (Join-Path $o.FullName $bundleName)).commit)"
        if ($seen.ContainsKey($c) -or $seen.Count -ge 3) { Remove-Item -LiteralPath $o.FullName -Recurse -Force -ErrorAction SilentlyContinue; continue }
        $seen[$c] = $true
    }
    return $dir
}

function Find-Backups()
{
    $all = @()
    foreach ($dir in @(Get-ChildItem -LiteralPath $backupRoot -Directory -ErrorAction SilentlyContinue)) {
        $bundle = Join-Path $dir.FullName $bundleName
        if (Test-Path -LiteralPath (Join-Path $bundle 'PackageContents.xml')) {
            $all += [pscustomobject]@{ Dir = $dir.FullName; Bundle = $bundle; Time = $dir.LastWriteTimeUtc; Info = (Read-FtfBuildInfo $bundle) }
        }
    }
    return @($all | Sort-Object Time -Descending)
}

# --- other copies of FTF that Civil 3D would load -------------------------------------

function Check-OtherLoaders([string]$installedDll)
{
    $roots = @($root)
    if (-not $testInstall) {
        if ($env:ProgramData) { $roots += (Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins') }
        if ($env:ProgramFiles) { $roots += (Join-Path $env:ProgramFiles 'Autodesk\ApplicationPlugins') }
    }
    $appData = if ($testInstall) { $null } else { $env:APPDATA }
    $others = @(Find-FtfOtherLoaders $installedDll $target $roots $RegistryRoot $appData)
    if ($others.Count -eq 0) { Say '  others:  nothing else loads a copy of FTF into Civil 3D'; return }
    $fixDir = Join-Path $backupRoot ("startup-$runStamp")
    foreach ($o in $others) {
        if ($LeaveOtherLoaders -or -not $o.Fixable) {
            Say ("  WARNING: " + $o.Text)
            continue
        }
        try {
            New-Item -ItemType Directory -Force -Path $fixDir | Out-Null
            if ($o.Kind -eq 'registry') {
                $p = Get-ItemProperty -Path $o.Path -ErrorAction Stop
                [ordered]@{ key = $o.Path; LOADCTRLS = $p.LOADCTRLS; LOADER = "$($p.LOADER)" } | ConvertTo-Json |
                    Set-Content -Path (Join-Path $fixDir ('registry-' + ($o.Path -replace '[^A-Za-z0-9]', '_') + '.json')) -Encoding utf8
                Set-ItemProperty -Path $o.Path -Name LOADCTRLS -Value 0 -Type DWord -ErrorAction Stop
                Say ("  fixed:   " + $o.Text + " -- switched off (LOADCTRLS=0; saved in $fixDir)")
            } elseif ($o.Kind -eq 'bundle') {
                $leaf = (Split-Path $o.Path -Leaf) + '.disabled-by-FTF-' + $runStamp
                Rename-Item -LiteralPath $o.Path -NewName $leaf -ErrorAction Stop
                Say ("  fixed:   " + $o.Text + " -- renamed to $leaf, so Civil 3D no longer loads it")
            }
        }
        catch { Say ("  WARNING: " + $o.Text + " (could not switch it off: " + $_.Exception.Message + ")") }
    }
    if (@($others | Where-Object { $LeaveOtherLoaders -or -not $_.Fixable }).Count -gt 0) {
        Say '  Each of those loads a second copy of FTF beside this one; they fight over every command.'
        Say '  Startup Suite: in Civil 3D, APPLOAD > Startup Suite > Contents > remove FieldCodes.Cad.dll.'
        Say '  acad*.lsp: delete the NETLOAD line. Machine-wide entries: ask IT. Run "Check FieldToFinish.bat" to list them again.'
    }
}

# --- local tools ----------------------------------------------------------------------
# Kept in %LOCALAPPDATA%\FieldToFinish, so a check or a rollback works when the network does not,
# and updating does not mean finding the release folder again.

function Write-LocalTools([string]$office)
{
    if ($testInstall -and -not $stateGiven) { return }
    try {
        $tools = Join-Path $StateRoot 'installer'
        New-Item -ItemType Directory -Force -Path $tools | Out-Null
        foreach ($name in @('install.ps1', 'check.ps1', 'ftf-common.ps1')) {
            $file = Join-Path $here $name
            if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination $tools -Force }
        }
        $crlf = "`r`n"
        $update = '@echo off' + $crlf +
                  'rem Field to Finish: install or update from the approved release folder recorded at install.' + $crlf +
                  'setlocal' + $crlf
        if ($office) {
            $update += ('set "RELEASE=' + $office + '"') + $crlf +
                       'if not exist "%RELEASE%\Install or Update FieldToFinish.bat" goto unreachable' + $crlf +
                       'call "%RELEASE%\Install or Update FieldToFinish.bat"' + $crlf +
                       'exit /b %ERRORLEVEL%' + $crlf +
                       ':unreachable' + $crlf +
                       'echo.' + $crlf +
                       'echo  The approved FTF release folder cannot be reached:' + $crlf +
                       'echo    %RELEASE%' + $crlf +
                       'echo  Check the network connection or VPN, then try again. The FTF already installed' + $crlf +
                       'echo  on this computer is unchanged and keeps working.' + $crlf
        } else {
            $update += 'echo.' + $crlf + 'echo  This FTF was not installed from an approved release folder; there is nothing to update from.' + $crlf
        }
        $update += 'echo.' + $crlf + 'pause' + $crlf + 'exit /b 1' + $crlf
        [IO.File]::WriteAllText((Join-Path $StateRoot 'Update FieldToFinish.bat'), $update, [Text.Encoding]::ASCII)
        $restore = '@echo off' + $crlf + 'rem Puts back the FTF installed before the last install or update.' + $crlf +
                   'powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\install.ps1" -RestorePrevious' + $crlf +
                   'echo.' + $crlf + 'pause' + $crlf
        [IO.File]::WriteAllText((Join-Path $StateRoot 'Restore previous FieldToFinish.bat'), $restore, [Text.Encoding]::ASCII)
        $check = '@echo off' + $crlf + 'rem Writes a report on why FTF does or does not load, for the drafting lead.' + $crlf +
                 'powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\check.ps1"' + $crlf +
                 'echo.' + $crlf + 'pause' + $crlf
        [IO.File]::WriteAllText((Join-Path $StateRoot 'Check FieldToFinish.bat'), $check, [Text.Encoding]::ASCII)
    }
    catch { Log ("local tools not written: " + $_.Exception.Message) }
}

# --- Civil 3D must be closed ----------------------------------------------------------
# It holds a loaded .NET assembly for the life of the process, so the copy would fail against a
# running session. A check changes nothing, so it does not need Civil 3D closed.

if ($WaitForCivil3D) {
    Say "Update waiting; watching for Civil 3D to close."
    $deadline = (Get-Date).AddMinutes($WaitMinutes)
    while (Get-Process -Name $ProcessName -ErrorAction SilentlyContinue) {
        if ((Get-Date) -ge $deadline) {
            Say 'Civil 3D is still open; leaving this update for next time.'
            exit 0
        }
        Start-Sleep -Seconds 5
    }
    Say 'Civil 3D closed; installing.'
}
elseif (-not $CheckOnly -and $watchCivil3D) {
    $open = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($open.Count -gt 0) {
        Fail ("Civil 3D is open (process " + (($open | ForEach-Object { $_.Id }) -join ', ') + ").`n" +
              "Save your drawings, close every Civil 3D window, then run this again.`n" +
              "Civil 3D holds FTF open while it runs, so it cannot be replaced underneath it.")
    }
}

# --- uninstall ------------------------------------------------------------------------

if ($Uninstall) {
    $saved = if ($NoBackup) { $null } else { Save-Backup 'before uninstalling' }
    $forgot = Unregister
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
        Say "Field to Finish removed."
        if ($saved) { Say "  a copy is kept in $saved (Restore previous FieldToFinish.bat puts it back)" }
    } else {
        Say "Field to Finish is not installed for $env:USERNAME."
    }
    if ($forgot.Count -gt 0) { Say "Civil 3D will no longer load it at startup." }
    Say "Your office settings are still here: $(Join-Path $env:APPDATA 'FieldToFinish')"
    exit 0
}

# --- the swap ---------------------------------------------------------------------------
# A new bundle is built beside the installed one and checked; only then are the two folders
# swapped by rename, and the old one is kept until the new one has been verified in place and
# registered. Anything failing after the swap puts the old one straight back.

$staging = $target + '.new'
$previous = $target + '.old'

function Install-Bundle([string]$sourceBundle, $info, [string]$office, [string]$whatFor, [switch]$FromBackup)
{
    Remove-Item $staging, $previous -Recurse -Force -ErrorAction SilentlyContinue
    try {
        New-Item -ItemType Directory -Force -Path $staging | Out-Null
        Copy-Item (Join-Path $sourceBundle '*') -Destination $staging -Recurse -Force -ErrorAction Stop
    }
    catch {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
        Fail ("The copy did not finish (" + $_.Exception.Message + ").`nTry again when the drive is reachable.")
    }

    # Anything that arrived by email, download or a copy from another machine carries Windows'
    # "this came from another computer" mark, and Civil 3D will not load a marked plugin.
    if ($env:OS -eq 'Windows_NT') {
        try { Get-ChildItem $staging -Recurse -File | Unblock-File -ErrorAction SilentlyContinue } catch { Log ("unblock: " + $_.Exception.Message) }
    }

    # The copy is checked before anything installed is touched. A release must be exactly what was
    # built; a backup also holds what FTF and the person wrote into it (source.txt, settings).
    $copied = @(Test-FtfBundle $staging $info -Strict:(-not $FromBackup))
    if ($copied.Count -gt 0) {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
        Fail ("The copy of the release on this computer is not complete:`n  " + ($copied -join "`n  ") + "`nNothing was installed. Try again; if it repeats, tell the drafting lead.")
    }

    if ($office) { Set-Content -Path (Join-Path $staging 'source.txt') -Value $office -Encoding utf8 }

    # What the office or the person saved inside the old bundle -- the office settings level
    # (ftf-settings.json beside FieldCodes.Cad.dll) and anything else FTF wrote there -- belongs to
    # them, not to a release: it is carried into the new copy unless the release brings a file of
    # the same name. A program file is never carried, so nothing from an older FTF can load.
    if (Test-Path $target) {
        $oldInfo = Read-FtfBuildInfo $target
        $shipped = @{}
        foreach ($k in (Get-FtfManifest $oldInfo).Keys) { $shipped[$k.ToLowerInvariant()] = $true }
        foreach ($file in @(Get-ChildItem -LiteralPath $target -Recurse -File -ErrorAction SilentlyContinue)) {
            $relative = Get-FtfRelative $target $file.FullName
            $lower = $relative.ToLowerInvariant()
            if ($lower -in @('build-info.json', 'source.txt', 'packagecontents.xml', 'version.txt')) { continue }
            if ($file.Extension -match '^\.(dll|exe|pdb)$') { continue }
            if ($null -ne $oldInfo -and $shipped.ContainsKey($lower)) { continue }
            # An install from before build-info.json: only the office settings are known to be theirs.
            if ($null -eq $oldInfo -and $file.Name -notmatch '^ftf-settings\.json(\.bak)?$') { continue }
            $into = Get-FtfLocalPath $staging $relative
            # A release never overwrites them; putting back a backup keeps the person's settings as
            # they are now, not as they were when the backup was made.
            if ((Test-Path -LiteralPath $into) -and -not $FromBackup) { continue }
            try {
                New-Item -ItemType Directory -Force -Path (Split-Path $into -Parent) | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $into -Force -ErrorAction Stop
                Log ("kept: " + $relative)
            }
            catch {
                Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
                Fail ("Your settings file $relative could not be carried over (" + $_.Exception.Message + ").`nNothing was changed.")
            }
        }
    }

    $savedRegistration = @(Save-Registration)
    $script:changed = $true
    $hadOld = Test-Path $target
    if ($hadOld) {
        if (-not (TryRename $target (Split-Path $previous -Leaf))) {
            $script:changed = $false
            Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
            Fail "The FTF already installed could not be moved aside (a file in it is open).`nClose Civil 3D and try again."
        }
    }
    if (-not (TryRename $staging (Split-Path $target -Leaf))) {
        if ((-not (Test-Path $target)) -and (Test-Path $previous)) { [void](TryRename $previous (Split-Path $target -Leaf)) }
        $script:changed = $false
        Fail "The new FTF could not be put in place.`nClose Civil 3D and try again."
    }

    # Verified where Civil 3D will load it, then registered. A failure here puts the old one back.
    $problem = $null
    $placed = @(Test-FtfBundle $target $info)
    if ($placed.Count -gt 0) { $problem = 'the installed files are not the release: ' + ($placed -join '; ') }
    if ($TestFault -eq 'Verify') { $problem = 'installed files did not verify (test fault)' }
    $told = @()
    $loader = Get-InstalledDll $target
    if (-not $problem) {
        if (-not $loader) { $problem = 'FieldCodes.Cad.dll is not in the installed bundle' }
        else { $told = @(Register $loader.FullName) }
    }
    if (-not $problem -and $TestFault -eq 'AfterSwap') { $problem = 'a failure injected after the swap (test fault)' }
    if ($problem) {
        Say ''
        Say "  Something went wrong after the new files were put in place: $problem"
        $failedDir = Join-Path (Join-Path $StateRoot 'failed') ("$runStamp-" + (Get-FtfShort $info))
        try {
            New-Item -ItemType Directory -Force -Path $failedDir | Out-Null
            Copy-Item -LiteralPath $target -Destination $failedDir -Recurse -Force -ErrorAction SilentlyContinue
        } catch { }
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
        if ($hadOld -and (Test-Path $previous)) {
            [void](TryRename $previous (Split-Path $target -Leaf))
            Restore-Registration $savedRegistration
            $back = Read-FtfBuildInfo $target
            $script:changed = $false
            Fail ("FTF was not $whatFor. The previous FTF was put back: " + (Get-FtfDescription $back) + ".`nThe failed copy is kept for the drafting lead in $failedDir.")
        }
        Restore-Registration $savedRegistration
        $script:changed = $false
        Fail ("FTF was not $whatFor, and there was no FTF here before, so nothing is installed.`nThe failed copy is kept for the drafting lead in $failedDir.")
    }
    Remove-Item $previous -Recurse -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{ Told = $told; Loader = $loader }
}

# --- restore the previous FTF -----------------------------------------------------------

if ($RestorePrevious) {
    $current = Read-FtfBuildInfo $target
    # The newest intact backup of another build than the one installed now (a damaged backup is
    # never used); failing that, the newest intact backup of this build (a repair).
    $intact = @(Find-Backups | Where-Object { $_.Info -and @(Test-FtfBundle $_.Bundle $_.Info).Count -eq 0 })
    $pick = $intact | Where-Object { -not ($current -and "$($_.Info.commit)" -eq "$($current.commit)") } | Select-Object -First 1
    if (-not $pick) { $pick = $intact | Select-Object -First 1 }
    if (-not $pick) { Fail "There is no earlier FTF to put back (no intact backup in $backupRoot)." }
    Say ("Putting back the FTF from " + $pick.Dir)
    Say ("  that is: " + (Get-FtfDescription $pick.Info))
    if ($current) { Say ("  replacing: " + (Get-FtfDescription $current)) }
    if (-not $NoBackup) { [void](Save-Backup 'before putting back an earlier FTF') }
    $officeBack = $null
    $sourceFile = Join-Path $pick.Bundle 'source.txt'
    if (Test-Path -LiteralPath $sourceFile) { $officeBack = (Get-Content -LiteralPath $sourceFile -First 1) }
    $result = Install-Bundle $pick.Bundle $pick.Info $officeBack 'put back' -FromBackup
    Say ''
    Say ("Field to Finish was put back: " + (Get-FtfDescription (Read-FtfBuildInfo $target)))
    Say '  Start Civil 3D and type FTFVERSION to confirm.'
    Say "  Log: $logFile"
    exit 0
}

# --- what is being installed ------------------------------------------------------------

if (-not (Test-Path (Join-Path $from 'PackageContents.xml'))) {
    Fail "This setup folder is incomplete -- no Bundle beside the script.`nCopy the whole release folder, then run it again."
}
if (-not (Get-ChildItem (Join-Path $from 'Contents') -Recurse -Filter 'FieldCodes.Cad.dll' -ErrorAction SilentlyContinue | Select-Object -First 1)) {
    Fail "This setup folder is incomplete -- the FTF program file is missing.`nCopy the whole release folder, then run it again."
}
$release = Read-FtfBuildInfo $from
if ($null -eq $release) {
    Fail "This setup folder is incomplete -- Bundle\build-info.json, which says what the release should contain, is missing.`nIt was not made by make-release.ps1; nothing was installed."
}

Say ''
Say 'Field to Finish -- install or update'
Say ("  release:   " + (Get-FtfDescription $release))
$releaseProblems = @(Test-FtfBundle $from $release -Strict)
if ($releaseProblems.Count -gt 0) {
    Fail ("This release folder is not complete or not what was built, so nothing was installed:`n  " + ($releaseProblems -join "`n  ") +
          "`nIf a new release is being published right now, wait a minute and run this again. Otherwise tell the drafting lead: the release folder needs publishing again.")
}
Say '  verified:  every file of the release matches what was built (SHA-256), and every FTF program file is this build'
if (-not (Test-FtfApproved $release) -and -not $AllowDevelopmentBuild) {
    Fail ("This folder holds a development build (" + (Get-FtfShort $release) + ", " + $release.channel + "), not an approved release.`n" +
          "Development builds are never installed from a release folder. Nothing was installed.`nTell the drafting lead.")
}

# --- where the approved release is ------------------------------------------------------
# Recorded in the bundle so FTF can compare itself against it later. The one folder never worth
# recording is where the batch file stages itself: it deletes it on the way out. A release folder
# can name its own home in updates-from.txt: a copy taken to a laptop, a zip, or an install run from
# a local folder still takes its updates from the office.

$office = $null
if (-not $NoOfficeCopy) {
    $declared = Join-Path $here 'updates-from.txt'
    if (Test-Path $declared) {
        $declaredHome = (Get-Content $declared -First 1)
        if (-not [string]::IsNullOrWhiteSpace($declaredHome)) { $office = $declaredHome.Trim().TrimEnd('\') }
    }
    if (-not $office) {
        $candidate = if ([string]::IsNullOrWhiteSpace($Source)) { $here } else { $Source }
        try { $candidate = (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).Path } catch { }
        $candidate = $candidate.TrimEnd('\', '/')
        $stagedHere = (Join-Path $tempRoot 'FTF-Setup').TrimEnd('\', '/')
        if ((Test-Path (Join-Path $candidate 'install.ps1')) -and (Test-Path (Join-Path (Join-Path $candidate 'Bundle') 'PackageContents.xml')) -and
            -not (Test-FtfSamePath $candidate $stagedHere)) {
            $office = $candidate
        }
    }
}
# Recorded as its \\server\share path where the drive letter is a mapped network drive, so this
# copy finds its updates on a computer with the drive mapped differently, or not at all.
if ($office) {
    $network = Resolve-FtfUncPath $office
    if ($network -and $network -ne $office) { Log "release folder $office is $network"; $office = $network }
}

# --- installed, and what to do ------------------------------------------------------------

$installed = Read-FtfBuildInfo $target
$hasInstall = Test-Path -LiteralPath (Join-Path $target 'PackageContents.xml')
if ($hasInstall) { Say ("  installed: " + (Get-FtfDescription $installed)) } else { Say '  installed: nothing yet' }

$same = $false
if ($hasInstall -and $installed -and "$($installed.commit)" -eq "$($release.commit)") {
    $a = Get-FtfManifest $installed
    $b = Get-FtfManifest $release
    $same = ($a.Count -eq $b.Count) -and (@($b.Keys | Where-Object { $a[$_] -ne $b[$_] }).Count -eq 0)
}
$damage = @()
if ($same) { $damage = @(Test-FtfBundle $target $installed) }

if (-not $hasInstall) { $action = 'install'; $whatFor = 'installed' }
elseif ($same -and $damage.Count -eq 0 -and -not $Force) { $action = 'current'; $whatFor = 'changed' }
elseif ($same) { $action = 'repair'; $whatFor = 'repaired' }
else { $action = 'update'; $whatFor = 'updated' }

switch ($action) {
    'install' { Say '  action:    install (FTF is not on this computer yet)' }
    'current' { Say '  action:    none -- the installed FTF is already this approved release, and every file matches' }
    'repair'  {
        if ($damage.Count -gt 0) { Say ('  action:    repair -- the installed copy is damaged (' + ($damage -join '; ') + ')') }
        else { Say '  action:    reinstall the same release (-Force)' }
    }
    'update'  {
        $note = ''
        if ($installed -and $installed.builtUtc -and $release.builtUtc) {
            try {
                $old = [DateTime]::Parse((Get-FtfUtcText $installed.builtUtc), [Globalization.CultureInfo]::InvariantCulture)
                $new = [DateTime]::Parse((Get-FtfUtcText $release.builtUtc), [Globalization.CultureInfo]::InvariantCulture)
                if ($new -lt $old) { $note = ' -- the approved release is OLDER than what is installed; installing it (the office has gone back to it)' }
            } catch { }
        }
        if (-not (Test-FtfApproved $installed) -and $installed) { $note += ' -- replacing a development build' }
        Say ("  action:    update " + (Get-FtfShort $installed) + " -> " + (Get-FtfShort $release) + $note)
    }
}

if ($CheckOnly) {
    Say ''
    Say '  (check only: nothing was changed)'
    Say "  Log: $logFile"
    exit 0
}

if ($action -eq 'current') {
    # Nothing to install. Where updates come from is kept in step with this run, as a reinstall
    # would (a developer install records none), and Civil 3D's startup record is made sure of: it
    # is cheap and it is what makes FTF load at all.
    $recordedFile = Join-Path $target 'source.txt'
    $recorded = if (Test-Path -LiteralPath $recordedFile) { (Get-Content -LiteralPath $recordedFile -First 1) } else { $null }
    if ($office -and $recorded -ne $office) { Set-Content -Path $recordedFile -Value $office -Encoding utf8; Log ("recorded release folder: " + $office) }
    elseif (-not $office -and $recorded) { Remove-Item -LiteralPath $recordedFile -Force; Log 'no release folder recorded (not installed from one)' }
    $loader = Get-InstalledDll $target
    $told = @()
    if ($loader) { $told = @(Register $loader.FullName) }
    Say ''
    Say "Field to Finish is up to date for $env:USERNAME: $(Get-FtfDescription $installed)"
    if ($told.Count -gt 0) { foreach ($name in $told) { Say ("  startup: " + $name + " loads it when it starts") } }
    if ($loader) { Check-OtherLoaders $loader.FullName }
    Write-LocalTools $office
    Say "  Log: $logFile"
    exit 0
}

# --- back up, install, verify ---------------------------------------------------------------

if ($hasInstall -and -not $NoBackup) {
    try {
        $backup = Save-Backup ("before " + $action + " to " + (Get-FtfShort $release))
        Say "  backup:    $backup"
    }
    catch { Fail ("The installed FTF could not be backed up (" + $_.Exception.Message + "), so it was not replaced.") }
}

$result = Install-Bundle $from $release $office $whatFor
$count = (Get-ChildItem $target -Recurse -File).Count

Say ''
switch ($action) {
    'install' { Say "Field to Finish is installed for $env:USERNAME." }
    'update'  { Say "Field to Finish is updated for $env:USERNAME." }
    default   { Say "Field to Finish is repaired for $env:USERNAME." }
}
Say ("  build:   " + (Get-FtfDescription $release))
Say ("  was:     " + $(if ($hasInstall) { Get-FtfDescription $installed } else { 'not installed' }))
Say "  folder:  $target"
Say "  files:   $count, every one checked against the release"
if ($office) {
    Say "  updates: from $office (double-click Install or Update FieldToFinish.bat there, or FTF installs a newer approved release when you close Civil 3D)"
} else {
    Say "  updates: none -- this copy was not installed from an approved release folder"
}
if ($result.Told.Count -gt 0) {
    foreach ($name in $result.Told) { Say ("  startup: " + $name + " will load it when it starts") }
} else {
    Say '  startup: no Civil 3D found to tell -- FTF is installed, and Civil 3D finds it in ApplicationPlugins'
}
if ($result.Loader) { Check-OtherLoaders $result.Loader.FullName }
Write-LocalTools $office

$officeRules = if ($env:APPDATA) { Join-Path (Join-Path $env:APPDATA 'FieldToFinish') 'rules.json' } else { $null }
if ($officeRules -and (Test-Path $officeRules)) {
    Say "  settings: your office settings were left alone ($officeRules)"
}

if (-not $testInstall -and -not $WaitForCivil3D -and -not (Test-Path 'C:\Program Files\Autodesk\AutoCAD 2024')) {
    Say ''
    Say 'Note: Civil 3D 2024 was not found in the usual place. FTF is installed and will'
    Say 'load if Civil 3D 2024 is somewhere else; if you run a different year, tell the'
    Say 'drafting lead -- this build is for 2024.'
}

if (-not $WaitForCivil3D) {
    Say ''
    Say 'Start Civil 3D. FTF loads by itself: the FTF ribbon tabs are there, nothing to load, nothing to type.'
    Say ('Type FTFVERSION in Civil 3D to see the build it loaded (' + (Get-FtfShort $release) + ').')
    Say ''
}
Say "  Log: $logFile"
exit 0
