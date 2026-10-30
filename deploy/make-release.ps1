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

.PARAMETER OfficeFolder
    Where the office keeps this package -- the U: drive folder people install from. It is
    written into the setup folder as updates-from.txt, so every copy of the package, local
    or zipped or emailed, still takes its updates from there.

.PARAMETER NoBuild
    Packages whatever is already built instead of building first. The build is still checked:
    a DLL whose embedded commit is not the commit being packaged is refused, so a stale DLL
    can never be shipped under a new version line.

.PARAMETER Commit
.PARAMETER Branch
.PARAMETER Channel
.PARAMETER BuildDate
    What this build is. UPDATEFTF.ps1 passes all four (it builds a detached checkout, where git
    cannot name the branch); by hand they default to the repository's HEAD, its branch, "dev",
    and now. Channel "approved" is UPDATEFTF's to give: only a clean build of origin/integration
    that passed its tests is an approved release.

.EXAMPLE
    .\make-release.ps1
    .\make-release.ps1 -Zip
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$OutputRoot,
    [string]$OfficeFolder,
    [switch]$Zip,
    [switch]$NoBuild,
    [string]$Commit,
    [string]$Branch,
    [ValidateSet('', 'dev', 'approved')][string]$Channel = '',
    [string]$BuildDate
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$setupSource = Join-Path $PSScriptRoot 'setup'
if (-not $OutputRoot) { $OutputRoot = Join-Path $PSScriptRoot 'release' }
$setup = Join-Path $OutputRoot 'FieldToFinish-Setup'
$bundle = Join-Path $setup 'Bundle'
$series = '2024'

# --- what is being built ----------------------------------------------------------

if (-not $Commit) { $Commit = (& git -C $repo rev-parse HEAD 2>$null) }
if (-not $Branch) { $Branch = (& git -C $repo rev-parse --abbrev-ref HEAD 2>$null) }
if (-not $BuildDate) { $BuildDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }
$Commit = "$Commit".Trim().ToLowerInvariant()
if ($Commit -notmatch '^[0-9a-f]{40}$') { Write-Error "Not a commit id: '$Commit'. Package from a git checkout, or pass -Commit." }
$short = $Commit.Substring(0, 7)

# --- build ------------------------------------------------------------------------

if (-not $NoBuild) {
    if (-not $Channel) { $Channel = 'dev' }
    Write-Host "Building $Configuration ($Channel $short)..."
    & dotnet build (Join-Path $repo 'FieldToFinish.sln') -c $Configuration -v q --nologo `
        "-p:FtfCommit=$Commit" "-p:FtfBranch=$Branch" "-p:FtfChannel=$Channel" "-p:FtfBuildDate=$BuildDate"
    if ($LASTEXITCODE -ne 0) { Write-Error 'The build failed; nothing was packaged.' }
}

$binaries = Join-Path $repo ("src\FieldCodes.Cad\bin\$Configuration\net48")
if (-not (Test-Path (Join-Path $binaries 'FieldCodes.Cad.dll'))) {
    Write-Error "No $Configuration build to package. Looked in $binaries."
}

# The DLL must be the build being packaged: its product version is "1.0.0+<commit>.<channel>",
# written by build\FtfBuildInfo.targets. Anything else is a stale or foreign build.
$product = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $binaries 'FieldCodes.Cad.dll')).ProductVersion
$stampMatch = [regex]::Match("$product", ('\+' + $short + '\.(dev|approved)(\.dirty)?$'))
if (-not $stampMatch.Success) {
    Write-Error ("$binaries\FieldCodes.Cad.dll is not a build of $short (its version says '$product'). " +
                 'Rebuild, or package without -NoBuild.')
}
$builtChannel = $stampMatch.Groups[1].Value
$builtDirty = $stampMatch.Groups[2].Success
if (-not $Channel) { $Channel = $builtChannel }
if ($Channel -ne $builtChannel) {
    Write-Error "The DLL was built as '$builtChannel' but '$Channel' was asked for. Rebuild with -p:FtfChannel=$Channel."
}
if ($Channel -eq 'approved' -and $builtDirty) {
    Write-Error 'A build from a tree with uncommitted changes cannot be packaged as approved.'
}

# --- assemble ---------------------------------------------------------------------

if (Test-Path $setup) { Remove-Item $setup -Recurse -Force }
$contents = Join-Path $bundle ("Contents\" + $series)
New-Item -ItemType Directory -Force -Path $contents | Out-Null

Copy-Item (Join-Path $setupSource '*') -Destination $setup -Force
Copy-Item (Join-Path $PSScriptRoot 'FieldToFinish.bundle\PackageContents.xml') -Destination $bundle -Force

# Never an Autodesk assembly (AcDbMgd, AcMgd, AcCoreMgd, AdWindows' Ac*, AecBaseMgd, AeccDbMgd).
Get-ChildItem $binaries -File |
    Where-Object { $_.Name -notmatch '^(Ac|Aec)' -and $_.Extension -ne '.pdb' } |
    Copy-Item -Destination $contents -Force

# The factory rules ship from config\ directly, not from the build output copy, which
# only refreshes when the CAD project itself rebuilds (a config-only change once
# deployed stale that way).
Copy-Item (Join-Path $repo 'config\rules.json') -Destination $contents -Force

# FTFCOMPARE's factory comparison profiles, from config\ for the same reason. A user's own copies in
# %APPDATA%\FieldToFinish\compare-profiles win over these.
$profilesOut = Join-Path $contents 'compare-profiles'
New-Item -ItemType Directory -Force -Path $profilesOut | Out-Null
Copy-Item (Join-Path $repo 'config\compare-profiles\*.json') -Destination $profilesOut -Force

# --- what this build is -----------------------------------------------------------
# version.txt is the one-line form the office updater compares (FieldCodes.Deploy.OfficeBuild);
# build-info.json carries the full stamp and a hash of every program file, which FTFVERSION
# and UPDATEFTF.ps1 read.

$stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm')
$line = "FTF $stamp  $Branch $short  ($Configuration, Civil 3D $series)"
Set-Content -Path (Join-Path $bundle 'version.txt') -Value $line -Encoding utf8
Copy-Item (Join-Path $bundle 'version.txt') -Destination $setup -Force

$hashes = [ordered]@{}
foreach ($file in Get-ChildItem $bundle -Recurse -File | Sort-Object FullName) {
    $relative = $file.FullName.Substring($bundle.Length + 1).Replace('\', '/')
    $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
$info = [ordered]@{
    version       = '1.0.0'
    commit        = $Commit
    branch        = $Branch
    channel       = $Channel
    builtUtc      = $BuildDate
    dirty         = $builtDirty
    configuration = $Configuration
    files         = $hashes
}
$info | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $bundle 'build-info.json') -Encoding utf8

if ($OfficeFolder) {
    Set-Content -Path (Join-Path $setup 'updates-from.txt') -Value $OfficeFolder.TrimEnd('\') -Encoding utf8
}

# --- report -----------------------------------------------------------------------

$files = (Get-ChildItem $setup -Recurse -File).Count
Write-Host ''
Write-Host "Setup folder ready: $setup"
Write-Host "  $line"
Write-Host "  $Channel build of $Commit"
Write-Host "  $files file(s); program files in Bundle\Contents\$series"
if ($OfficeFolder) { Write-Host "  updates come from: $OfficeFolder" }
else { Write-Host '  updates come from wherever it is installed from (pass -OfficeFolder to pin that)' }
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
