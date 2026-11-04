<#
.SYNOPSIS
    Publishes an approved Field to Finish release to the office's release folder, the one
    surveyors install and update from ("Install or Update FieldToFinish.bat").

.DESCRIPTION
    The approval step. Only a release that is

      * an approved build (channel "approved", not built from uncommitted changes),
      * complete and exactly what was built (every file's SHA-256 against build-info.json,
        nothing extra, every FTF assembly stamped with the same commit),

    is published. UPDATEFTF.ps1 -PublishTo calls this after the build passed its unit tests, the
    installer tests and the smoke test in Civil 3D, and never after -SkipSmoke. A development
    build cannot be published, by this script or any other path.

    The release folder gets the setup files and the Bundle, plus

      release.json        what was approved: commit, channel, built, approved by and when, from
                          which computer, the checks it passed, and the SHA-256 of every file
      RELEASE-NOTES.txt   what changed (-Notes, or what UPDATEFTF gathers from git)
      updates-from.txt    the folder itself, so every installed copy updates from here

    The new release is copied beside the folder first (<folder>.incoming), verified there, on the
    server, and only then swapped in: the folder people install from is never half written. The
    release it replaces is kept beside it as <folder>.previous-<time> (the three most recent).

    The location is whatever -ReleaseFolder says: nothing is hard-coded. Staff need only read
    access to it; whoever publishes needs write access.

.PARAMETER Package
    The setup folder make-release.ps1 wrote (FieldToFinish-Setup).

.PARAMETER ReleaseFolder
    The approved release folder on the network, e.g. \\server\share\CAD\FieldToFinish.

.PARAMETER ApprovedBy
    Who approved it. Defaults to the person running this.

.PARAMETER Notes
.PARAMETER NotesFile
    What changed, for RELEASE-NOTES.txt.

.PARAMETER Checks
    What the build passed, separated by "|" (UPDATEFTF passes its unit, installer and smoke test
    summaries).

.EXAMPLE
    .\publish-release.ps1 -Package .\release\FieldToFinish-Setup -ReleaseFolder \\server\share\CAD\FieldToFinish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Package,
    [Parameter(Mandatory = $true)][string]$ReleaseFolder,
    [string]$ApprovedBy,
    [string]$Notes,
    [string]$NotesFile,
    [string]$Checks
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Join-Path $PSScriptRoot 'setup') 'ftf-common.ps1')

function Stop-Publish([string]$why)
{
    Write-Host ''
    Write-Host "NOT PUBLISHED: $why" -ForegroundColor Red
    Write-Host "The approved release in $ReleaseFolder (if any) is unchanged."
    exit 1
}

if (-not $ApprovedBy) { $ApprovedBy = $env:USERNAME }
if (-not $ApprovedBy) { $ApprovedBy = 'unknown' }
$ReleaseFolder = $ReleaseFolder.TrimEnd('\', '/')

# --- the package ---------------------------------------------------------------------------

$bundle = Join-Path $Package 'Bundle'
foreach ($name in @('Install or Update FieldToFinish.bat', 'install.ps1', 'ftf-common.ps1', 'check.ps1', 'README.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Package $name))) { Stop-Publish "the package has no $name; make it with make-release.ps1." }
}
$info = Read-FtfBuildInfo $bundle
if ($null -eq $info) { Stop-Publish 'the package has no Bundle\build-info.json; make it with make-release.ps1.' }
$problems = @(Test-FtfBundle $bundle $info -Strict)
if ($problems.Count -gt 0) { Stop-Publish ("the package is not what was built:`n  " + ($problems -join "`n  ")) }
if (-not (Test-FtfApproved $info)) {
    Stop-Publish ("this is a development build (" + (Get-FtfShort $info) + ", channel " + $info.channel + $(if ($info.dirty) { ', uncommitted changes' } else { '' }) +
                  "). Only an approved build of the integration branch, validated by UPDATEFTF, is published.")
}
$short = Get-FtfShort $info

# --- what was there before ---------------------------------------------------------------------

$before = Read-FtfBuildInfo (Join-Path $ReleaseFolder 'Bundle')
if ($before) { Write-Host ("Replacing: " + (Get-FtfDescription $before)) } else { Write-Host 'No approved release there yet.' }
Write-Host ("Publishing: " + (Get-FtfDescription $info))

# --- assemble beside the folder, verify on the server, then swap ---------------------------------

$parent = Split-Path $ReleaseFolder -Parent
$leaf = Split-Path $ReleaseFolder -Leaf
if (-not $parent) { Stop-Publish "$ReleaseFolder is not a folder path." }
try { New-Item -ItemType Directory -Force -Path $parent | Out-Null } catch { Stop-Publish ("cannot reach $parent (" + $_.Exception.Message + ").") }
$incoming = Join-Path $parent ("$leaf.incoming")
try {
    if (Test-Path -LiteralPath $incoming) { Remove-Item -LiteralPath $incoming -Recurse -Force }
    Copy-Item -LiteralPath $Package -Destination $incoming -Recurse -Force
    Remove-Item -LiteralPath (Join-Path $incoming 'updates-from.txt') -Force -ErrorAction SilentlyContinue
}
catch { Stop-Publish ("the copy to the server did not finish (" + $_.Exception.Message + ").") }

$notesText = $Notes
if ($NotesFile -and (Test-Path -LiteralPath $NotesFile)) { $notesText = (Get-Content -LiteralPath $NotesFile -Raw) }
if ([string]::IsNullOrWhiteSpace($notesText)) { $notesText = '(no notes given)' }
$approvedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$crlf = "`r`n"
$notesOut = 'FIELD TO FINISH -- RELEASE NOTES' + $crlf + '================================' + $crlf + $crlf +
            ('Build:     ' + (Get-FtfDescription $info)) + $crlf +
            ('Commit:    ' + $info.commit) + $crlf +
            ('Approved:  ' + $approvedUtc + ' by ' + $ApprovedBy) + $crlf +
            $(if ($before) { 'Replaces:  ' + (Get-FtfDescription $before) + $crlf } else { '' }) + $crlf +
            ($notesText.Trim() -replace "`r?`n", $crlf) + $crlf + $crlf +
            'To install or update: close Civil 3D, then double-click "Install or Update FieldToFinish.bat" in this folder.' + $crlf
[IO.File]::WriteAllText((Join-Path $incoming 'RELEASE-NOTES.txt'), $notesOut, [Text.Encoding]::UTF8)
Set-Content -Path (Join-Path $incoming 'updates-from.txt') -Value $ReleaseFolder -Encoding utf8

$top = [ordered]@{}
foreach ($file in @(Get-ChildItem -LiteralPath $incoming -File | Where-Object { $_.Name -ne 'release.json' } | Sort-Object Name)) {
    $top[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
[ordered]@{
    product      = 'Field to Finish'
    civil3d      = '2024'
    commit       = $info.commit
    short        = $short
    branch       = $info.branch
    channel      = $info.channel
    builtUtc     = (Get-FtfUtcText $info.builtUtc)
    approvedBy   = $ApprovedBy
    approvedUtc  = $approvedUtc
    approvedOn   = $env:COMPUTERNAME
    replaces     = $(if ($before) { "$($before.commit)" } else { $null })
    checks       = @("$Checks" -split '\s*\|\s*' | Where-Object { $_ })
    bundleFiles  = @((Get-FtfManifest $info).Keys).Count
    setupFiles   = $top
} | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $incoming 'release.json') -Encoding utf8

$onServer = @(Test-FtfBundle (Join-Path $incoming 'Bundle') (Read-FtfBuildInfo (Join-Path $incoming 'Bundle')) -Strict)
foreach ($name in $top.Keys) {
    $file = Join-Path $incoming $name
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $top[$name]) { $onServer += "changed on the server: $name" }
}
if ($onServer.Count -gt 0) {
    Remove-Item -LiteralPath $incoming -Recurse -Force -ErrorAction SilentlyContinue
    Stop-Publish ("the copy on the server does not verify:`n  " + ($onServer -join "`n  "))
}

$kept = $null
try {
    if (Test-Path -LiteralPath $ReleaseFolder) {
        $kept = "$leaf.previous-" + (Get-Date -Format 'yyyyMMdd-HHmmss')
        Rename-Item -LiteralPath $ReleaseFolder -NewName $kept
    }
    Rename-Item -LiteralPath $incoming -NewName $leaf
}
catch {
    if ($kept -and -not (Test-Path -LiteralPath $ReleaseFolder) -and (Test-Path -LiteralPath (Join-Path $parent $kept))) {
        Rename-Item -LiteralPath (Join-Path $parent $kept) -NewName $leaf -ErrorAction SilentlyContinue
    }
    Stop-Publish ("the release folder could not be swapped (" + $_.Exception.Message + "); someone may have a file in it open.")
}
foreach ($old in @(Get-ChildItem -LiteralPath $parent -Directory -Filter "$leaf.previous-*" -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -Skip 3)) {
    Remove-Item -LiteralPath $old.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ("PUBLISHED approved release $short to $ReleaseFolder") -ForegroundColor Green
if ($kept) { Write-Host ("  the release it replaced is kept as " + (Join-Path $parent $kept)) }
Write-Host '  Surveyors: close Civil 3D, double-click "Install or Update FieldToFinish.bat" in that folder.'
Write-Host '  FTF already installed from there also offers it when Civil 3D starts, and installs it when Civil 3D closes.'
exit 0
