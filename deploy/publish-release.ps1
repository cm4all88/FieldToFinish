<#
.SYNOPSIS
    Publishes an approved Field to Finish release to the office's approved release folder, the one
    surveyors install and update from ("Install or Update FieldToFinish.bat"), or puts the release
    it replaced back (-Restore).

.DESCRIPTION
    The approval step. Only a release that is

      * an approved build (channel "approved", not built from uncommitted changes),
      * complete and exactly what was built (every file's SHA-256 against build-info.json,
        nothing extra, every FTF assembly stamped with the same commit),

    is published. UPDATEFTF.ps1 -Publish calls this after the build passed its unit tests, the
    installer tests and the smoke test in Civil 3D, and never after -SkipSmoke. A development
    build cannot be published, by this script or any other path.

    The release folder itself never moves, so the permissions IT sets on it stay (staff read,
    publishers modify). Inside it:

      the release                 the setup files and Bundle\, replaced as a set
      release.json                what was approved: commit, channel, built, approved by and when,
                                  from which computer, the checks it passed, every file's SHA-256
      RELEASE-NOTES.txt           what changed (-Notes, or what UPDATEFTF gathers from git)
      updates-from.txt            the folder's \\server\share path, so every installed copy updates
                                  from here whether or not it has the drive letter mapped
      Previous releases\<time>-<commit>\   the releases it replaced (the three most recent)
      _publishing\                only while publishing: the new release, copied and verified here

    The new release is copied into _publishing and verified there, on the server. Only then are the
    current release's files moved into Previous releases and the new ones moved up (renames on the
    same share: a moment). If any of that fails, everything is moved back as it was. The release is
    verified again in place; if that fails, the previous release is put back. Anything else
    somebody keeps in the folder is left where it is.

    A surveyor who double-clicks during that moment gets "not a complete release" and nothing
    changes on their computer: the installer verifies every file before it installs anything.

.PARAMETER Package
    The setup folder make-release.ps1 wrote (FieldToFinish-Setup).

.PARAMETER ReleaseFolder
    The approved release folder: a \\server\share path, or a mapped drive path, which is turned
    into its \\server\share path.

.PARAMETER Restore
    Put back the most recent release in Previous releases that verifies (or -RestoreFrom <name>).
    The current release goes into Previous releases. Staff get it by double-clicking the batch
    file (it says OLDER); the automatic update never goes backwards.

.PARAMETER ApprovedBy
    Who approved it. Defaults to the person running this.

.PARAMETER Notes
.PARAMETER NotesFile
    What changed, for RELEASE-NOTES.txt.

.PARAMETER Checks
    What the build passed, separated by "|" (UPDATEFTF passes its unit, installer and smoke test
    summaries).

.PARAMETER DriveMap
.PARAMETER TestFault
    For deploy\test-installer.ps1 only.

.EXAMPLE
    .\publish-release.ps1 -Package .\release\FieldToFinish-Setup -ReleaseFolder \\server\share\Survey\FieldToFinish-Setup
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ReleaseFolder,
    [string]$Package,
    [switch]$Restore,
    [string]$RestoreFrom,
    [string]$ApprovedBy,
    [string]$Notes,
    [string]$NotesFile,
    [string]$Checks,
    [hashtable]$DriveMap,
    [ValidateSet('', 'MidSwap', 'AfterSwap')][string]$TestFault = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Join-Path $PSScriptRoot 'setup') 'ftf-common.ps1')

$given = $ReleaseFolder.Trim().TrimEnd('\', '/')
function Stop-Publish([string]$why)
{
    Write-Host ''
    Write-Host "NOT PUBLISHED: $why" -ForegroundColor Red
    Write-Host "The approved release in $given (if any) is unchanged."
    exit 1
}

if (-not $ApprovedBy) { $ApprovedBy = $env:USERNAME }
if (-not $ApprovedBy) { $ApprovedBy = 'unknown' }
$onWindows = ($PSVersionTable.PSEdition -eq 'Desktop') -or ($IsWindows -eq $true)

# --- where ------------------------------------------------------------------------------------

$folder = Resolve-FtfUncPath $given $DriveMap
if (-not $folder) {
    $folder = $given
    Write-Host ("WARNING: " + $given.Substring(0, 2) + " is not a mapped network drive here, so the release records $given; " +
                'installed copies find updates only where that drive is mapped the same way.') -ForegroundColor Yellow
}
if ($folder -ne $given) { Write-Host "Release folder: $folder  (given as $given)" } else { Write-Host "Release folder: $folder" }
$parent = Split-Path $folder -Parent
if (-not $parent) { Stop-Publish "$folder is not a folder path." }
$previousRoot = Join-Path $folder $script:FtfPreviousReleases
$staging = Join-Path $folder $script:FtfPublishing

# --- what is there now --------------------------------------------------------------------------

$before = Read-FtfBuildInfo (Join-Path $folder 'Bundle')
$beforeRelease = $null
if (Test-Path -LiteralPath (Join-Path $folder 'release.json')) {
    try { $beforeRelease = Get-Content -LiteralPath (Join-Path $folder 'release.json') -Raw | ConvertFrom-Json } catch { }
}

# The release's own items at the top of the folder: the known names, the new release's names, and
# whatever the current release's release.json lists. Nothing else is moved.
function Get-ReleaseItemNames([string]$incoming)
{
    $names = @($script:FtfReleaseItems)
    if ($incoming -and (Test-Path -LiteralPath $incoming)) { $names += @(Get-ChildItem -LiteralPath $incoming -Force | ForEach-Object { $_.Name }) }
    if ($beforeRelease -and $beforeRelease.setupFiles) { $names += @($beforeRelease.setupFiles.PSObject.Properties | ForEach-Object { $_.Name }) }
    return @($names | Where-Object { $_ -and $_ -ne $script:FtfPreviousReleases -and $_ -ne $script:FtfPublishing } | Select-Object -Unique)
}

# Files at the top of a release, with their SHA-256 (release.json itself excepted).
function Get-TopHashes([string]$dir)
{
    $top = [ordered]@{}
    foreach ($file in @(Get-ChildItem -LiteralPath $dir -File -Force | Where-Object { $_.Name -ne 'release.json' } | Sort-Object Name)) {
        $top[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return $top
}

# What is wrong with the release in $dir: the Bundle against its build-info.json (strictly), and
# every top-level file against $top.
function Test-Release([string]$dir, $top)
{
    $bundle = Join-Path $dir 'Bundle'
    $info = Read-FtfBuildInfo $bundle
    if ($null -eq $info) { return @('no Bundle\build-info.json') }
    $found = @(Test-FtfBundle $bundle $info -Strict)
    if ($null -ne $top) {
        foreach ($name in @($top.Keys)) {
            $file = Join-Path $dir $name
            if (-not (Test-Path -LiteralPath $file)) { $found += "missing: $name" }
            elseif ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $top[$name]) { $found += "changed: $name" }
        }
    }
    return $found
}

# The release-folder lock: _publishing exists while a publish runs. One left from an interrupted
# publish more than 30 minutes ago is cleared.
function Open-Staging
{
    if (Test-Path -LiteralPath $staging) {
        $age = (Get-Date) - (Get-Item -LiteralPath $staging -Force).LastWriteTime
        if ($age.TotalMinutes -lt 30) {
            Stop-Publish ("another publish may be running (" + $script:FtfPublishing + " was written " + [int]$age.TotalMinutes +
                          " minute(s) ago). If it was interrupted, wait half an hour or delete $staging, then publish again.")
        }
        try { Remove-Item -LiteralPath $staging -Recurse -Force } catch { Stop-Publish ("an interrupted publish left $staging, and it cannot be removed (" + $_.Exception.Message + ").") }
    }
}

# A rename on the share, tried again for a few seconds when a file in it is held open for a moment
# (antivirus on the server, a surveyor's copy reading it). A file held open for good still fails.
function Move-Patiently([string]$from, [string]$to)
{
    for ($attempt = 1; ; $attempt++) {
        try { Move-Item -LiteralPath $from -Destination $to -ErrorAction Stop; return }
        catch { if ($attempt -ge 4) { throw }; Start-Sleep -Milliseconds (500 * $attempt) }
    }
}

# Moves the current release's items into $keepAs and the items in $from up into the folder. On any
# failure, moves everything back. Returns $null, or what went wrong.
$script:movedOut = @()
$script:movedIn = @()
function Switch-Release([string]$from, [string]$keepAs)
{
    $script:movedOut = @()
    $script:movedIn = @()
    $names = Get-ReleaseItemNames $from
    $outgoing = @(Get-ChildItem -LiteralPath $folder -Force | Where-Object { $names -contains $_.Name })
    try {
        if ($outgoing.Count -gt 0) { New-Item -ItemType Directory -Force -Path $keepAs | Out-Null }
        foreach ($o in $outgoing) {
            Move-Patiently $o.FullName (Join-Path $keepAs $o.Name)
            $script:movedOut += $o.Name
        }
        if ($TestFault -eq 'MidSwap') { throw 'a failure injected in the middle of the swap (test fault)' }
        foreach ($i in @(Get-ChildItem -LiteralPath $from -Force)) {
            Move-Patiently $i.FullName (Join-Path $folder $i.Name)
            $script:movedIn += $i.Name
        }
        return $null
    } catch {
        $why = $_.Exception.Message
        $left = Undo-Switch $from $keepAs
        if ($left) { return "$why; and putting it back failed: $left" }
        return $why
    }
}

function Undo-Switch([string]$from, [string]$keepAs)
{
    $trouble = @()
    foreach ($n in @($script:movedIn)) {
        try { Move-Patiently (Join-Path $folder $n) (Join-Path $from $n) } catch { $trouble += "$n (" + $_.Exception.Message + ")" }
    }
    foreach ($n in @($script:movedOut)) {
        try { Move-Patiently (Join-Path $keepAs $n) (Join-Path $folder $n) } catch { $trouble += "$n (" + $_.Exception.Message + ")" }
    }
    $script:movedIn = @()
    $script:movedOut = @()
    if ((Test-Path -LiteralPath $keepAs) -and @(Get-ChildItem -LiteralPath $keepAs -Force).Count -eq 0) { Remove-Item -LiteralPath $keepAs -Force -ErrorAction SilentlyContinue }
    if ($trouble.Count -gt 0) { return ("these are in " + $keepAs + " or " + $from + ", not in the folder: " + ($trouble -join ', ')) }
    return $null
}

function Limit-Previous
{
    if (-not (Test-Path -LiteralPath $previousRoot)) { return }
    foreach ($old in @(Get-ChildItem -LiteralPath $previousRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -Skip 3)) {
        Remove-Item -LiteralPath $old.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    $about = Join-Path $previousRoot 'README.txt'
    if (-not (Test-Path -LiteralPath $about)) {
        $text = 'The Field to Finish releases this folder had before, kept by publishing (the three most recent).' + "`r`n" +
                'Install from the folder above, not from here. To put one of these back as the approved release, the' + "`r`n" +
                'publisher runs UPDATEFTF -RestoreRelease.' + "`r`n"
        try { [IO.File]::WriteAllText($about, $text, [Text.Encoding]::ASCII) } catch { }
    }
}

function Previous-Name($info)
{
    $label = if ($info) { Get-FtfShort $info } else { 'earlier' }
    return ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $label)
}

# --- -Restore: put back a release this folder had ------------------------------------------------

if ($Restore) {
    if (-not (Test-Path -LiteralPath $previousRoot)) { Stop-Publish "there is no $($script:FtfPreviousReleases) folder in $folder to restore from." }
    $candidates = @(Get-ChildItem -LiteralPath $previousRoot -Directory | Sort-Object Name -Descending)
    if ($RestoreFrom) { $candidates = @($candidates | Where-Object { $_.Name -eq $RestoreFrom }) }
    $pick = $null
    foreach ($c in $candidates) {
        $info = Read-FtfBuildInfo (Join-Path $c.FullName 'Bundle')
        $top = $null
        $relFile = Join-Path $c.FullName 'release.json'
        if (Test-Path -LiteralPath $relFile) {
            try {
                $r = Get-Content -LiteralPath $relFile -Raw | ConvertFrom-Json
                $top = [ordered]@{}
                foreach ($p in @($r.setupFiles.PSObject.Properties)) { $top[$p.Name] = "$($p.Value)" }
            } catch { $top = $null }
        }
        $wrong = @(Test-Release $c.FullName $top)
        if ($wrong.Count -eq 0 -and (Test-FtfApproved $info)) { $pick = [pscustomobject]@{ Dir = $c.FullName; Info = $info; Top = $top }; break }
        Write-Host ("  skipped " + $c.Name + ": " + $(if ($wrong.Count -gt 0) { $wrong -join '; ' } else { 'not an approved release' }))
    }
    if (-not $pick) { Stop-Publish "no previous release there verifies as an approved release, so none was put back." }
    Write-Host ("Putting back: " + (Get-FtfDescription $pick.Info))
    if ($before) { Write-Host ("Replacing:   " + (Get-FtfDescription $before)) }
    Open-Staging
    $keepAs = Join-Path $previousRoot (Previous-Name $before)
    $failed = Switch-Release $pick.Dir $keepAs
    if ($failed) { Stop-Publish ("the folder could not be switched ($failed); someone may have a file in it open.") }
    $live = @(Test-Release $folder $pick.Top)
    if ($TestFault -eq 'AfterSwap') { $live += 'a failure injected after the swap (test fault)' }
    if ($live.Count -gt 0) {
        $left = Undo-Switch $pick.Dir $keepAs
        Stop-Publish ("the restored release did not verify in place (" + ($live -join '; ') + "); the release folder was put back as it was." + $(if ($left) { " BUT: $left" } else { '' }))
    }
    if (@(Get-ChildItem -LiteralPath $pick.Dir -Force).Count -eq 0) { Remove-Item -LiteralPath $pick.Dir -Force -ErrorAction SilentlyContinue }
    Limit-Previous
    Write-Host ''
    Write-Host ("RESTORED release " + (Get-FtfShort $pick.Info) + " as the approved release in $folder") -ForegroundColor Green
    if ($before) { Write-Host ("  the release it replaced is kept in " + $keepAs) }
    Write-Host '  Surveyors get it by double-clicking "Install or Update FieldToFinish.bat" (it says OLDER).'
    Write-Host '  The automatic update never goes backwards, so copies already on the newer release keep it until then.'
    exit 0
}

# --- the package ---------------------------------------------------------------------------------

if (-not $Package) { Stop-Publish 'no -Package given.' }
$bundle = Join-Path $Package 'Bundle'
foreach ($name in @('Install or Update FieldToFinish.bat', 'Restore previous FieldToFinish.bat', 'install.ps1', 'ftf-common.ps1', 'check.ps1', 'README.txt')) {
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

if ($before) { Write-Host ("Replacing:  " + (Get-FtfDescription $before)) } else { Write-Host 'No approved release there yet.' }
Write-Host ("Publishing: " + (Get-FtfDescription $info))

# --- copy into the folder, verify there, then switch --------------------------------------------

try { New-Item -ItemType Directory -Force -Path $folder | Out-Null } catch { Stop-Publish ("cannot reach or create $folder (" + $_.Exception.Message + ").") }
Open-Staging
try {
    Copy-Item -LiteralPath $Package -Destination $staging -Recurse -Force
    Remove-Item -LiteralPath (Join-Path $staging 'updates-from.txt') -Force -ErrorAction SilentlyContinue
    if ($onWindows) { try { $s = Get-Item -LiteralPath $staging -Force; $s.Attributes = $s.Attributes -bor [IO.FileAttributes]::Hidden } catch { } }
}
catch {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    Stop-Publish ("the copy to the server did not finish (" + $_.Exception.Message + ").")
}

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
[IO.File]::WriteAllText((Join-Path $staging 'RELEASE-NOTES.txt'), $notesOut, [Text.Encoding]::UTF8)
Set-Content -Path (Join-Path $staging 'updates-from.txt') -Value $folder -Encoding utf8

$top = Get-TopHashes $staging
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
    folder       = $folder
    publishedAs  = $given
    replaces     = $(if ($before) { "$($before.commit)" } else { $null })
    checks       = @("$Checks" -split '\s*\|\s*' | Where-Object { $_ })
    bundleFiles  = @((Get-FtfManifest $info).Keys).Count
    setupFiles   = $top
} | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $staging 'release.json') -Encoding utf8

$onServer = @(Test-Release $staging $top)
if ($onServer.Count -gt 0) {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    Stop-Publish ("the copy on the server does not verify:`n  " + ($onServer -join "`n  "))
}

$keepAs = Join-Path $previousRoot (Previous-Name $before)
$failed = Switch-Release $staging $keepAs
if ($failed) {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    Stop-Publish ("the release could not be switched in ($failed). Someone may have a file in the folder open, for example FTF loaded into Civil 3D from there with NETLOAD.")
}
$live = @(Test-Release $folder $top)
if ($TestFault -eq 'AfterSwap') { $live += 'a failure injected after the swap (test fault)' }
if ($live.Count -gt 0) {
    $left = Undo-Switch $staging $keepAs
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    Stop-Publish ("the published release did not verify in place (" + ($live -join '; ') + "); the previous release was put back." + $(if ($left) { " BUT: $left" } else { '' }))
}
Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
Limit-Previous

Write-Host ''
Write-Host ("PUBLISHED approved release $short to $folder") -ForegroundColor Green
if (Test-Path -LiteralPath $keepAs) { Write-Host ("  the release it replaced is kept in $keepAs") }
$others = @(Get-ChildItem -LiteralPath $folder -Force | Where-Object { (Get-ReleaseItemNames $null) -notcontains $_.Name -and $_.Name -ne $script:FtfPreviousReleases })
foreach ($o in $others) { Write-Host ("  left as it was (not part of a release): " + $o.Name) }
Write-Host '  Surveyors: close Civil 3D, double-click "Install or Update FieldToFinish.bat" in that folder.'
Write-Host '  FTF already installed from there also offers it when Civil 3D starts, and installs it when Civil 3D closes.'
exit 0
