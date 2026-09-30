<#
.SYNOPSIS
    Builds the setup folder the surveyors run: the plugin, the batch files and a README.

.DESCRIPTION
    Produces  deploy\release\FieldToFinish-Setup\  containing

        Install FTF.bat      what a surveyor double-clicks
        Uninstall FTF.bat
        install.ps1          the work, staged locally by the batch file
        README.txt
        version.txt          build date, branch and commit -- printed when installing
        Bundle\              PackageContents.xml + Contents\2024\ (the program itself)

    Copy that folder anywhere the office can reach it (the U: drive) and point people
    at Install FTF.bat. Nothing in it needs admin rights or the repository.

    The Autodesk assemblies are deliberately left out: the host already has them
    loaded and a second copy is the classic "works on my machine" load failure.

.PARAMETER Configuration
    Release by default. Debug for trying the packaging out against a debug build.

.PARAMETER OutputRoot
    Where to write the setup folder. Defaults to deploy\release.

.PARAMETER Zip
    Also writes FieldToFinish-Setup.zip beside the folder, for emailing.

.PARAMETER NoBuild
    Packages whatever is already built instead of building first.

.EXAMPLE
    .\make-release.ps1
    .\make-release.ps1 -Zip
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$OutputRoot,
    [switch]$Zip,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$setupSource = Join-Path $PSScriptRoot 'setup'
if (-not $OutputRoot) { $OutputRoot = Join-Path $PSScriptRoot 'release' }
$setup = Join-Path $OutputRoot 'FieldToFinish-Setup'
$bundle = Join-Path $setup 'Bundle'
$series = '2024'

# --- build ------------------------------------------------------------------------

if (-not $NoBuild) {
    Write-Host "Building $Configuration..."
    & dotnet build (Join-Path $repo 'FieldToFinish.sln') -c $Configuration -v q --nologo
    if ($LASTEXITCODE -ne 0) { Write-Error 'The build failed; nothing was packaged.' }
}

$binaries = Join-Path $repo ("src\FieldCodes.Cad\bin\$Configuration\net48")
if (-not (Test-Path (Join-Path $binaries 'FieldCodes.Cad.dll'))) {
    Write-Error "No $Configuration build to package. Looked in $binaries."
}

# --- assemble ---------------------------------------------------------------------

if (Test-Path $setup) { Remove-Item $setup -Recurse -Force }
$contents = Join-Path $bundle ("Contents\" + $series)
New-Item -ItemType Directory -Force -Path $contents | Out-Null

Copy-Item (Join-Path $setupSource '*') -Destination $setup -Force
Copy-Item (Join-Path $PSScriptRoot 'FieldToFinish.bundle\PackageContents.xml') -Destination $bundle -Force

Get-ChildItem $binaries -File |
    Where-Object { $_.Name -notmatch '^(Ac|Aecc)' -and $_.Extension -ne '.pdb' } |
    Copy-Item -Destination $contents -Force

# The factory rules ship from config\ directly, not from the build output copy, which
# only refreshes when the CAD project itself rebuilds (a config-only change once
# deployed stale that way).
Copy-Item (Join-Path $repo 'config\rules.json') -Destination $contents -Force

# --- what this build is -----------------------------------------------------------

$branch = (& git -C $repo rev-parse --abbrev-ref HEAD 2>$null)
$commit = (& git -C $repo rev-parse --short HEAD 2>$null)
$stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm')
$line = "FTF $stamp"
if ($commit) { $line += "  $branch $commit" }
$line += "  ($Configuration, Civil 3D $series)"
Set-Content -Path (Join-Path $bundle 'version.txt') -Value $line -Encoding utf8
Copy-Item (Join-Path $bundle 'version.txt') -Destination $setup -Force

# --- report -----------------------------------------------------------------------

$files = (Get-ChildItem $setup -Recurse -File).Count
Write-Host ''
Write-Host "Setup folder ready: $setup"
Write-Host "  $line"
Write-Host "  $files file(s); program files in Bundle\Contents\$series"
Write-Host ''
Write-Host 'Copy that folder where the office can reach it and tell people to close'
Write-Host 'Civil 3D and double-click "Install FTF.bat".'

if ($Zip) {
    $archive = Join-Path $OutputRoot 'FieldToFinish-Setup.zip'
    if (Test-Path $archive) { Remove-Item $archive -Force }
    Compress-Archive -Path $setup -DestinationPath $archive
    Write-Host ''
    Write-Host "Zipped for email: $archive"
}
