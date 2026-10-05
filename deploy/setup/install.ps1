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
    [string]$ProcessName = 'acad',
    [string]$RegistryRoot = 'HKCU:\SOFTWARE\Autodesk\AutoCAD',
    [string]$ProductRoot = 'HKLM:\SOFTWARE\Autodesk\AutoCAD'
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

# Set the moment the installed bundle is actually touched, so a failure can say truthfully
# whether the FTF already on this machine is still there.
$script:changed = $false

# Civil 3D keeps a record of each plugin it loads at startup. It writes that record itself
# after reading a bundle -- but only on a machine that scans the plugin folder, and some do
# not. Writing the same record directly makes FTF load either way. Per user, no admin, and
# removed again by the uninstaller.
#
# Only where Civil 3D actually is: the other AutoCAD product keys would try to load a
# Civil-only assembly and fail noisily at startup.
function Civil3DProducts()
{
    $found = @()
    foreach ($release in Get-ChildItem $RegistryRoot -ErrorAction SilentlyContinue) {
        foreach ($product in Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue) {
            $where = Join-Path $ProductRoot ($release.PSChildName + '\' + $product.PSChildName)
            $name = (Get-ItemProperty -Path $where -ErrorAction SilentlyContinue).ProductName
            if ($name -notmatch 'Civil') { continue }
            $found += [pscustomobject]@{
                Key = $product.PSPath
                Name = $name
                Where = $release.PSChildName + '\' + $product.PSChildName
            }
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

function TryRename([string]$from, [string]$toLeaf)
{
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try { Rename-Item -LiteralPath $from -NewName $toLeaf -ErrorAction Stop; return $true }
        catch { Start-Sleep -Milliseconds 500 }
    }
    return $false
}

function Fail([string]$message)
{
    Say ''
    Say '  ---------------------------------------------------------------'
    Say "  $message"
    if ((-not $script:changed) -and (Test-Path $target)) {
        Say '  The FTF you already had is still installed and was not changed.'
    }
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
    $forgot = Unregister
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
        Say "Field to Finish removed."
    } else {
        Say "Field to Finish is not installed for $env:USERNAME."
    }
    if ($forgot.Count -gt 0) { Say "Civil 3D will no longer load it at startup." }
    Say "Your office settings are still here: $env:APPDATA\FieldToFinish"
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

# A setup folder can name its own home in updates-from.txt. That is how a copy taken to a
# laptop, a zip sent by email, or an install run from a local folder still takes its
# updates from the office drive. It is believed even when that drive is not reachable
# right now: FTF checks reachability later, every time Civil 3D starts.
$declared = Join-Path $here 'updates-from.txt'
if (Test-Path $declared) {
    $declaredHome = (Get-Content $declared -First 1)
    if (-not [string]::IsNullOrWhiteSpace($declaredHome)) { $office = $declaredHome.Trim().TrimEnd('\') }
}

if (-not $office) {
    $candidate = if ([string]::IsNullOrWhiteSpace($Source)) { $here } else { $Source }
    try { $candidate = (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).Path } catch { }
    $candidate = $candidate.TrimEnd('\')
    $staging = (Join-Path $env:TEMP 'FTF-Setup').TrimEnd('\')
    if ((Test-Path (Join-Path $candidate 'install.ps1')) -and
        (Test-Path (Join-Path $candidate 'Bundle\PackageContents.xml')) -and
        ($candidate -ne $staging)) {
        $office = $candidate
    }
}

# --- copy -------------------------------------------------------------------------
# Copied beside the installed one first, and only swapped in once it is whole. The source
# is usually a network folder: a share that drops halfway through must never be able to
# leave somebody with no FTF at all. The old bundle is then removed rather than merged,
# so a file from an older FTF cannot linger and load beside the new one.

$staging = $target + '.new'
$previous = $target + '.old'
Remove-Item $staging, $previous -Recurse -Force -ErrorAction SilentlyContinue

try {
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Copy-Item (Join-Path $from '*') -Destination $staging -Recurse -Force -ErrorAction Stop
}
catch {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    Fail ("The copy did not finish (" + $_.Exception.Message + "). Try again when the drive is reachable.")
}

$staged = @(Get-ChildItem $staging -Recurse -File)
$whole = (Test-Path (Join-Path $staging 'PackageContents.xml')) -and
         ($staged | Where-Object { $_.Name -eq 'FieldCodes.Cad.dll' })
if (-not $whole) {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    Fail 'The copy did not finish. Try again, or send this message to the drafting lead.'
}

# Anything that arrived by email, download or a copy from another machine carries Windows'
# "this came from another computer" mark, and Civil 3D will not load a marked plugin. The
# files just copied are the ones being installed, so the mark goes.
Get-ChildItem $staging -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

if ($office) { Set-Content -Path (Join-Path $staging 'source.txt') -Value $office -Encoding utf8 }

# The swap itself: two local renames, with the old one kept until the new one is in place.
$script:changed = $true
if (Test-Path $target) {
    if (-not (TryRename $target (Split-Path $previous -Leaf))) {
        $script:changed = $false
        Fail 'The FTF already installed could not be moved aside. Close Civil 3D and try again.'
    }
}
if (-not (TryRename $staging (Split-Path $target -Leaf))) {
    if ((-not (Test-Path $target)) -and (Test-Path $previous)) {
        [void](TryRename $previous (Split-Path $target -Leaf))
    }
    Fail 'The new FTF could not be put in place. Close Civil 3D and try again.'
}
Remove-Item $previous -Recurse -Force -ErrorAction SilentlyContinue

$installed = (Get-ChildItem $target -Recurse -File).Count

# Tell Civil 3D to load it at startup, rather than relying on it noticing the folder.
$loader = (Get-ChildItem (Join-Path $target 'Contents') -Recurse -Filter 'FieldCodes.Cad.dll' -ErrorAction SilentlyContinue |
           Select-Object -First 1)
$told = @()
if ($loader) { $told = Register $loader.FullName }

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
if ($told.Count -gt 0) {
    foreach ($name in $told) { Say ("  startup: " + $name + " will load it when it starts") }
} else {
    Say '  startup: no Civil 3D found to tell -- FTF is installed, but nothing will load it'
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
