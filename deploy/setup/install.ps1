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

    Surveyors do not run this directly: they double-click "Install FTF.bat", which
    stages the files locally and calls this.

.PARAMETER Uninstall
    Removes the installed bundle. Office configuration stays.

.PARAMETER TargetRoot
    An ApplicationPlugins folder to install into instead of the real one. For the
    installer's own test, which must not disturb a real install.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [string]$TargetRoot
)

$ErrorActionPreference = 'Stop'
$bundleName = 'FieldToFinish.bundle'
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$source = Join-Path $here 'Bundle'

$testInstall = -not [string]::IsNullOrWhiteSpace($TargetRoot)
$root = if ($testInstall) { $TargetRoot } else { Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins' }
$target = Join-Path $root $bundleName

function Fail([string]$message)
{
    Write-Host ''
    Write-Host '  ---------------------------------------------------------------'
    Write-Host "  $message"
    Write-Host '  ---------------------------------------------------------------'
    Write-Host ''
    exit 1
}

# --- Civil 3D must be closed ------------------------------------------------------
# It holds a loaded .NET assembly for the life of the process, so the copy would fail
# against a running session. A test install into a scratch folder cannot disturb one.

if (-not $testInstall) {
    if (Get-Process -Name acad -ErrorAction SilentlyContinue) {
        Fail 'Civil 3D is open. Close it, then run this again.'
    }
}

# --- uninstall --------------------------------------------------------------------

if ($Uninstall) {
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
        Write-Host "Field to Finish removed."
        Write-Host "Your office settings are still here: $env:APPDATA\FieldToFinish"
    } else {
        Write-Host "Field to Finish is not installed for $env:USERNAME."
    }
    exit 0
}

# --- what we are installing -------------------------------------------------------

if (-not (Test-Path (Join-Path $source 'PackageContents.xml'))) {
    Fail "This setup folder is incomplete -- no Bundle beside the script. Copy the whole folder, then run Install FTF.bat."
}

$module = Get-ChildItem (Join-Path $source 'Contents') -Recurse -Filter 'FieldCodes.Cad.dll' -ErrorAction SilentlyContinue |
          Select-Object -First 1
if (-not $module) {
    Fail "This setup folder is incomplete -- the FTF program file is missing. Copy the whole folder, then run Install FTF.bat."
}

$versionFile = Join-Path $source 'version.txt'
$version = if (Test-Path $versionFile) { (Get-Content $versionFile -First 1) } else { 'unknown build' }

# --- copy -------------------------------------------------------------------------
# The old bundle goes first: a leftover file from an older FTF would otherwise stay
# behind and load alongside the new one.

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $source '*') -Destination $target -Recurse -Force

$installed = (Get-ChildItem $target -Recurse -File).Count
if ($installed -lt 2) { Fail "The copy did not finish. Try again, or send this message to the drafting lead." }

Write-Host ''
Write-Host "Field to Finish is installed for $env:USERNAME."
Write-Host "  build:   $version"
Write-Host "  folder:  $target"
Write-Host "  files:   $installed"

$officeRules = Join-Path $env:APPDATA 'FieldToFinish\rules.json'
if (Test-Path $officeRules) {
    Write-Host "  settings: your office settings were left alone ($officeRules)"
}

if (-not $testInstall -and -not (Test-Path 'C:\Program Files\Autodesk\AutoCAD 2024')) {
    Write-Host ''
    Write-Host 'Note: Civil 3D 2024 was not found in the usual place. FTF is installed and will'
    Write-Host 'load if Civil 3D 2024 is somewhere else; if you run a different year, tell the'
    Write-Host 'drafting lead -- this build is for 2024.'
}

Write-Host ''
Write-Host 'Start Civil 3D. Two ribbon tabs will be there: FTF and FTF Boundary.'
Write-Host 'Nothing to load, nothing to type.'
Write-Host ''
exit 0
