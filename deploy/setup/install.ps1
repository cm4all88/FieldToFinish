<#
.SYNOPSIS
    Installs Field to Finish for the person running it. No admin rights, no NETLOAD.

.DESCRIPTION
    Copies the Bundle folder that sits beside this script into
    %APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle, which is where Civil 3D
    looks for plugins to load at startup.

    Nothing outside that bundle folder is written. Office configuration lives in
    %APPDATA%\FieldToFinish and is never touched, so installing a newer FTF cannot
    lose office standards.

    Where it was installed from is recorded in the bundle, so FTF can tell whether the
    office copy has moved on. When it has, FTF launches this script again with
    -WaitForCivil3D as Civil 3D closes: a loaded assembly cannot be replaced underneath
    a running session, so the update lands between sessions and the next start is current.

    Surveyors do not run this directly: they double-click "Install FTF.bat".

.PARAMETER Uninstall
    Removes the installed bundle. Office configuration stays.

.PARAMETER Source
    The setup folder to remember as the office copy. Defaults to this script's folder.
    "Install FTF.bat" passes the original folder, because it stages itself locally first.

.PARAMETER WaitForCivil3D
    Waits for Civil 3D to close instead of refusing, then installs. How an update applies
    itself; it writes to %TEMP%\FTF-update.log, because nobody is watching.

.PARAMETER TargetRoot
    An ApplicationPlugins folder to install into instead of the real one. For the
    installer's own test, which must not disturb a real install.

.PARAMETER WaitMinutes
.PARAMETER ProcessName
    How long -WaitForCivil3D waits, and what it waits for. Both are here so the
    installer's own test can exercise waiting without a real Civil 3D, and without
    caring whether someone happens to have Civil 3D open while the test runs.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [string]$Source,
    [switch]$WaitForCivil3D,
    [string]$TargetRoot,
    [int]$WaitMinutes = 60,
    [string]$ProcessName = 'acad'
)

$ErrorActionPreference = 'Stop'
$bundleName = 'FieldToFinish.bundle'
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$from = Join-Path $here 'Bundle'

$testInstall = -not [string]::IsNullOrWhiteSpace($TargetRoot)
$root = if ($testInstall) { $TargetRoot } else { Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins' }
$target = Join-Path $root $bundleName
$log = Join-Path $env:TEMP 'FTF-update.log'

function Say([string]$message)
{
    Write-Host $message
    if ($WaitForCivil3D) {
        Add-Content -Path $log -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '  ' + $message) -ErrorAction SilentlyContinue
    }
}

function Fail([string]$message)
{
    Say ''
    Say '  ---------------------------------------------------------------'
    Say "  $message"
    Say '  ---------------------------------------------------------------'
    Say ''
    exit 1
}

# --- Civil 3D must be closed ------------------------------------------------------
# It holds a loaded .NET assembly for the life of the process, so the copy would fail
# against a running session. A test install into a scratch folder cannot disturb one.

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
elseif (-not $testInstall) {
    if (Get-Process -Name $ProcessName -ErrorAction SilentlyContinue) {
        Fail 'Civil 3D is open. Close it, then run this again.'
    }
}

# --- uninstall --------------------------------------------------------------------

if ($Uninstall) {
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
        Say "Field to Finish removed."
        Say "Your office settings are still here: $env:APPDATA\FieldToFinish"
    } else {
        Say "Field to Finish is not installed for $env:USERNAME."
    }
    exit 0
}

# --- what we are installing -------------------------------------------------------

if (-not (Test-Path (Join-Path $from 'PackageContents.xml'))) {
    Fail "This setup folder is incomplete -- no Bundle beside the script. Copy the whole folder, then run Install FTF.bat."
}

$module = Get-ChildItem (Join-Path $from 'Contents') -Recurse -Filter 'FieldCodes.Cad.dll' -ErrorAction SilentlyContinue |
          Select-Object -First 1
if (-not $module) {
    Fail "This setup folder is incomplete -- the FTF program file is missing. Copy the whole folder, then run Install FTF.bat."
}

$versionFile = Join-Path $from 'version.txt'
$version = if (Test-Path $versionFile) { (Get-Content $versionFile -First 1) } else { 'unknown build' }

# --- where the office copy is -----------------------------------------------------
# Recorded in the bundle so FTF can compare itself against it later. The one folder never
# worth recording is where "Install FTF.bat" stages itself: it deletes it on the way out.

$office = $null
$candidate = if ([string]::IsNullOrWhiteSpace($Source)) { $here } else { $Source }
try { $candidate = (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).Path } catch { }
$candidate = $candidate.TrimEnd('\')
$staging = (Join-Path $env:TEMP 'FTF-Setup').TrimEnd('\')
if ((Test-Path (Join-Path $candidate 'install.ps1')) -and
    (Test-Path (Join-Path $candidate 'Bundle\PackageContents.xml')) -and
    ($candidate -ne $staging)) {
    $office = $candidate
}

# --- copy -------------------------------------------------------------------------
# The old bundle goes first: a leftover file from an older FTF would otherwise stay
# behind and load alongside the new one.

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $from '*') -Destination $target -Recurse -Force

$installed = (Get-ChildItem $target -Recurse -File).Count
if ($installed -lt 2) { Fail "The copy did not finish. Try again, or send this message to the drafting lead." }

if ($office) { Set-Content -Path (Join-Path $target 'source.txt') -Value $office -Encoding utf8 }

Say ''
Say "Field to Finish is installed for $env:USERNAME."
Say "  build:   $version"
Say "  folder:  $target"
Say "  files:   $installed"
if ($office) {
    Say "  updates: from $office, applied when you close Civil 3D"
} else {
    Say "  updates: none -- this copy was installed by hand, so it stays as it is"
}

$officeRules = Join-Path $env:APPDATA 'FieldToFinish\rules.json'
if (Test-Path $officeRules) {
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
    Say 'Start Civil 3D. Two ribbon tabs will be there: FTF and FTF Boundary.'
    Say 'Nothing to load, nothing to type.'
    Say ''
}
exit 0
