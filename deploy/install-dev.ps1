<#
.SYNOPSIS
    Installs Field to Finish into the per-user ApplicationPlugins folder so Civil 3D
    loads it on startup and nobody has to run NETLOAD.

.DESCRIPTION
    Copies the bundle manifest and the built assemblies to
    %APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle.

    Per-user by design: no admin rights, and it cannot disturb anyone else's Civil 3D
    while this is still being developed. Use -AllUsers for the department-wide install
    once it is ready, which does need an elevated prompt.

    Civil 3D must be closed. It holds a loaded .NET assembly for the life of the
    process, so the copy would fail against a running session.

.EXAMPLE
    .\install-dev.ps1
    .\install-dev.ps1 -AllUsers
    .\install-dev.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$AllUsers,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$repo       = Split-Path -Parent $PSScriptRoot
$bundleName = 'FieldToFinish.bundle'
$source     = Join-Path $PSScriptRoot $bundleName

$root = if ($AllUsers) {
    Join-Path $env:ProgramFiles 'Autodesk\ApplicationPlugins'
} else {
    Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
}
$target = Join-Path $root $bundleName

# --- Civil 3D must not be running -------------------------------------------------

$running = Get-Process -Name acad -ErrorAction SilentlyContinue
if ($running) {
    Write-Error ("Civil 3D is running (PID $($running.Id -join ', ')). " +
                 'It holds the plugin assembly open. Close it and run this again.')
}

# --- uninstall --------------------------------------------------------------------

if ($Uninstall) {
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
        Write-Host "Removed $target"
    } else {
        Write-Host "Nothing installed at $target"
    }
    return
}

# --- locate the build output ------------------------------------------------------

# Civil 3D 2024 only while this build is being stabilised. A second release adds an
# entry here and a matching Components block in PackageContents.xml.
$builds = @(
    @{ Series = '2024'; Path = Join-Path $repo 'src\FieldCodes.Cad\bin\Debug\net48' }
)

$found = @($builds | Where-Object { Test-Path (Join-Path $_.Path 'FieldCodes.Cad.dll') })
if ($found.Count -eq 0) {
    Write-Error ("No build output found. Run 'dotnet build FieldToFinish.sln' first. " +
                 "Looked in:`n  " + (($builds | ForEach-Object { $_.Path }) -join "`n  "))
}

# --- copy -------------------------------------------------------------------------

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $source 'PackageContents.xml') -Destination $target -Force

foreach ($build in $found) {
    $contents = Join-Path $target ("Contents\" + $build.Series)
    New-Item -ItemType Directory -Force -Path $contents | Out-Null

    # Everything except the Autodesk assemblies, which must never be copied: the host
    # already has them loaded and a duplicate is the classic "works on my machine"
    # load failure.
    Get-ChildItem $build.Path -File |
        Where-Object { $_.Name -notmatch '^(Ac|Aecc)' } |
        Copy-Item -Destination $contents -Force

    # The factory rules ship from config\ directly, NOT from the build output copy:
    # the output copy only refreshes on a rebuild of the CAD project, so a
    # config-only change could otherwise deploy stale (it did, once - FOG's label).
    Copy-Item (Join-Path $repo 'config\rules.json') -Destination $contents -Force

    $count = (Get-ChildItem $contents -File).Count
    Write-Host ("Installed Civil 3D {0}: {1} file(s) -> {2}" -f $build.Series, $count, $contents)
}

# --- configuration ownership ------------------------------------------------------
# This script only ever writes inside the bundle. The rules.json it deploys is the
# FACTORY DEFAULT; user configuration lives in %APPDATA%\FieldToFinish and is never
# touched here, so redeploying cannot destroy office standards.

$officeRules = Join-Path $env:APPDATA 'FieldToFinish\rules.json'
if (Test-Path $officeRules) {
    Write-Host "Office configuration preserved: $officeRules (deploys never touch it)."
} else {
    Write-Host "No office configuration yet: FTF uses the shipped factory defaults" `
               "until one is saved from the rule editor."
}

Write-Host ""
Write-Host "Done. Start Civil 3D - the FTF commands will be available without NETLOAD."
Write-Host "Rebuilding still needs Civil 3D closed first; a loaded assembly is never released."
Write-Host ""
Write-Host "Uninstall with:  .\install-dev.ps1 -Uninstall"
