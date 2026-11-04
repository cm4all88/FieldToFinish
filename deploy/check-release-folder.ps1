<#
.SYNOPSIS
    Checks the approved release folder before anything is published there: where it really is (the
    \\server\share path behind a mapped drive letter), whether it can be reached, what it holds now,
    whether the person running this can publish there, and who can read and change it.

.DESCRIPTION
    Changes nothing on the share. The only thing written is the write test (unless -NoWriteTest):
    a folder "_publishing-check-<time>" created in the release folder (or in its parent, when the
    release folder does not exist yet), a file written, both renamed, and both deleted again. That
    is exactly what publishing does, so it is the one reliable test of the publisher's rights.

    UPDATEFTF -CheckReleaseFolder runs this on the office's folder (deploy\release-folder.txt), and
    UPDATEFTF -Publish runs it again before building. Run it on the publishing workstation, as the
    person who publishes, without "Run as administrator" (an elevated PowerShell does not see
    mapped drives).

    What it cannot see from a workstation: the share permissions on the server (as opposed to the
    folder permissions), and what another person can do. Those are reported as things to confirm.

.PARAMETER ReleaseFolder
    The approved release folder, as a mapped drive path (U:\...) or a \\server\share path.

.PARAMETER NoWriteTest
    Skip the write test (only read).

.PARAMETER ResultFile
    Also write the result as JSON here (UPDATEFTF reads it).

.PARAMETER DriveMap
    For tests: drive letter -> network path, instead of asking Windows.

.EXAMPLE
    .\check-release-folder.ps1 -ReleaseFolder "U:\Survey\FieldToFinish-Setup"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ReleaseFolder,
    [switch]$NoWriteTest,
    [string]$ResultFile,
    [hashtable]$DriveMap
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Join-Path $PSScriptRoot 'setup') 'ftf-common.ps1')

$problems = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
function Say([string]$text) { Write-Host "  $text" }
function Ok([string]$text) { Write-Host "  OK      $text" -ForegroundColor Green }
function Warn([string]$text) { $warnings.Add($text); Write-Host "  WARNING $text" -ForegroundColor Yellow }
function Problem([string]$text) { $problems.Add($text); Write-Host "  PROBLEM $text" -ForegroundColor Red }

Write-Host ''
Write-Host 'Field to Finish -- approved release folder check' -ForegroundColor White
Say ("as:      " + $env:USERNAME + " on " + $env:COMPUTERNAME)

# --- where it is -------------------------------------------------------------------------------

$given = $ReleaseFolder.Trim().TrimEnd('\', '/')
$folder = $given
$isUnc = $false
Say "given:   $given"
if ($given.StartsWith('\\')) {
    $isUnc = $true
    Ok 'a network (UNC) path: every computer reaches it the same way'
} elseif ($given -match '^([A-Za-z]):') {
    $drive = $Matches[1].ToUpperInvariant() + ':'
    $unc = Resolve-FtfUncPath $given $DriveMap
    $driveHere = ($null -ne $DriveMap) -or (Test-Path -LiteralPath ($drive + '\'))
    if ($unc) {
        $folder = $unc
        $isUnc = $true
        Ok ("$drive is a mapped network drive; the folder is $unc")
        Say '         installed copies record this path, so they find updates whether or not they have'
        Say "         $drive mapped."
    } elseif (-not $driveHere) {
        Problem ("$drive is not mapped in this PowerShell, so the \\server\share path behind it is unknown, and installed " +
                 "copies would record $given (which works only where $drive is mapped the same way). " +
                 'Run this without "Run as administrator", or give the \\server\share path instead.')
    } else {
        Warn ("$drive is a local drive here, not a network drive: only this computer can install from $given.")
    }
} else {
    Say '         (not a Windows drive or network path: a local test folder)'
}

$parent = Split-Path $folder -Parent
$leaf = Split-Path $folder -Leaf
$exists = Test-Path -LiteralPath $folder -PathType Container
$parentExists = $false
if (-not $exists -and $parent) { $parentExists = Test-Path -LiteralPath $parent -PathType Container }
if ($exists) {
    Ok "reachable: $folder"
} elseif ($parentExists) {
    Say "the folder does not exist yet; publishing creates it in $parent"
} else {
    Problem "cannot reach $folder (network, VPN, or the path is wrong)."
}
if ($isUnc -and $folder -ne $given -and -not $exists -and (Test-Path -LiteralPath $given -PathType Container)) {
    Problem "$given can be reached but $folder cannot: the drive letter and the network path disagree. Give the \\server\share path that works."
}

# --- what it holds -------------------------------------------------------------------------------

$current = $null
if ($exists) {
    $items = @(Get-ChildItem -LiteralPath $folder -Force -ErrorAction SilentlyContinue)
    $releaseJson = Join-Path $folder 'release.json'
    $info = Read-FtfBuildInfo (Join-Path $folder 'Bundle')
    if (Test-Path -LiteralPath $releaseJson) {
        try {
            $rel = Get-Content -LiteralPath $releaseJson -Raw | ConvertFrom-Json
            $current = ("approved release {0}, approved {1} by {2}" -f $rel.short, (Get-FtfUtcText $rel.approvedUtc), $rel.approvedBy)
        } catch { $current = 'a release whose release.json cannot be read' }
    } elseif ($info) {
        $current = (Get-FtfDescription $info) + ' (published before release.json)'
    } elseif (Test-Path -LiteralPath (Join-Path (Join-Path $folder 'Bundle') 'PackageContents.xml')) {
        $current = 'an older FTF setup folder without build information'
    } elseif ($items.Count -eq 0) {
        $current = 'nothing (empty)'
    } else {
        $current = 'no FTF release'
    }
    Say "holds:   $current"
    if ($info) {
        $damage = @(Test-FtfBundle (Join-Path $folder 'Bundle') $info)
        if ($damage.Count -gt 0) { Warn ("the release there does not verify (" + ($damage -join '; ') + "); publishing replaces it.") }
    }
    $known = @($script:FtfReleaseItems) + @($script:FtfPreviousReleases, $script:FtfPublishing)
    $others = @($items | Where-Object { $known -notcontains $_.Name -and $_.Name -notlike '_publishing-check-*' })
    foreach ($o in $others) { Say ("         also there, not part of a release (publishing leaves it where it is): " + $o.Name) }
    $previous = Join-Path $folder $script:FtfPreviousReleases
    if (Test-Path -LiteralPath $previous) {
        Say ("         previous releases kept: " + @(Get-ChildItem -LiteralPath $previous -Directory -ErrorAction SilentlyContinue).Count)
    }
    $staging = Join-Path $folder $script:FtfPublishing
    if (Test-Path -LiteralPath $staging) {
        Warn ("a publish is in progress or was interrupted (" + $script:FtfPublishing + ", " +
              (Get-Item -LiteralPath $staging -Force).LastWriteTime.ToString('yyyy-MM-dd HH:mm') + "); the next publish clears it.")
    }
}

# --- can the publisher write there ---------------------------------------------------------------

$writeTested = $false
$testIn = if ($exists) { $folder } elseif ($parentExists) { $parent } else { $null }
if ($NoWriteTest) {
    Say 'write test: skipped (-NoWriteTest)'
} elseif ($testIn) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $probe = Join-Path $testIn ("_publishing-check-$stamp")
    $moved = $probe + '-renamed'
    $step = 'create a folder'
    try {
        New-Item -ItemType Directory -Path $probe -ErrorAction Stop | Out-Null
        $step = 'write a file'
        [IO.File]::WriteAllText((Join-Path $probe 'probe.txt'), "FTF publish check by $env:USERNAME")
        $step = 'rename a file'
        Rename-Item -LiteralPath (Join-Path $probe 'probe.txt') -NewName 'probe-renamed.txt' -ErrorAction Stop
        $step = 'rename a folder'
        Rename-Item -LiteralPath $probe -NewName (Split-Path $moved -Leaf) -ErrorAction Stop
        $step = 'delete'
        Remove-Item -LiteralPath $moved -Recurse -Force -ErrorAction Stop
        $writeTested = $true
        Ok ("you can publish here: creating, writing, renaming and deleting in $testIn all work for " + $env:USERNAME)
    } catch {
        Problem ("you cannot publish here: could not $step in $testIn (" + $_.Exception.Message + "). " +
                 "The person who publishes needs Modify on that folder; ask IT.")
    } finally {
        foreach ($left in @($probe, $moved)) {
            if (Test-Path -LiteralPath $left) { Remove-Item -LiteralPath $left -Recurse -Force -ErrorAction SilentlyContinue }
            if (Test-Path -LiteralPath $left) { Warn "could not remove the write test's folder $left; delete it by hand." }
        }
    }
}

# --- who can read and change it ------------------------------------------------------------------

$aclOf = if ($exists) { $folder } elseif ($parentExists) { $parent } else { $null }
$readers = @()
$writers = @()
if ($aclOf) {
    $acl = $null
    if (-not (Get-Command Get-Acl -ErrorAction SilentlyContinue)) {
        Say 'permissions: not read (Get-Acl is Windows only)'
    } else {
        try { $acl = Get-Acl -LiteralPath $aclOf -ErrorAction Stop } catch {
            Say ("permissions: could not be read (" + (("$($_.Exception.Message)" -split "`r?`n")[0]) + ")")
        }
    }
    if ($acl) {
        Say ("permissions on $aclOf" + $(if (-not $exists) { ' (the new folder inherits them)' } else { '' }) + ", owner " + $acl.Owner + ':')
        $broad = '(?i)^(Everyone|.*\\Authenticated Users|BUILTIN\\Users|.*\\Domain Users)$'
        foreach ($rule in @($acl.Access)) {
            $id = "$($rule.IdentityReference)"
            if ($id -match '(?i)CREATOR OWNER') { continue }
            $r = [int]$rule.FileSystemRights
            # Write data, append, delete children, delete, change permissions, take ownership, and
            # the generic write / all bits; read data, generic read (the sign bit) or generic all.
            $changes = ($r -band 0x500D0046) -ne 0
            $reads = (($r -band 0x10000001) -ne 0) -or ($r -lt 0)
            $how = "$($rule.FileSystemRights)" + $(if ($rule.IsInherited) { ', inherited' } else { '' })
            if ("$($rule.AccessControlType)" -ne 'Allow') { Say ("         denied: $id ($how)"); continue }
            if ($changes) {
                $writers += $id
                Say ("         can change:  $id ($how)")
                if ($id -match $broad) {
                    Warn ("everyone in '$id' can change or delete the release here. Survey staff need Read & execute " +
                          'only; ask IT to give Modify only to the people who publish.')
                }
            } elseif ($reads) {
                $readers += $id
                Say ("         can read:    $id ($how)")
            }
        }
        $broadReaders = @(@($readers) + @($writers) | Where-Object { $_ -match $broad })
        if ($broadReaders.Count -gt 0) {
            Ok ("survey staff can read it (through " + ($broadReaders -join ', ') + ")")
        } elseif ($readers.Count + $writers.Count -gt 0) {
            Warn ('no general group can read it: make sure every surveyor is in one of: ' + ((@($readers) + @($writers)) -join ', ') + '.')
        }
        Say '         (the share permissions on the server also apply; they are not visible from here)'
    }
}

# --- path length ---------------------------------------------------------------------------------

$longest = $folder.Length + 70
if ($exists) {
    foreach ($f in @(Get-ChildItem -LiteralPath $folder -Recurse -File -Force -ErrorAction SilentlyContinue)) {
        if ($f.FullName.Length -gt $longest) { $longest = $f.FullName.Length }
    }
}
if ($longest -gt 240) { Warn "the longest path in the release is about $longest characters; Windows tools can fail past 260. Use a shorter folder." }

# --- result --------------------------------------------------------------------------------------

$canPublish = ($problems.Count -eq 0) -and ($writeTested -or $NoWriteTest)
Write-Host ''
if ($canPublish) {
    Write-Host ("READY: approved releases can be published to $folder" + $(if ($warnings.Count -gt 0) { " (" + $warnings.Count + " warning(s) above)" } else { '' })) -ForegroundColor Green
} else {
    Write-Host "CANNOT PUBLISH to ${folder}:" -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "  $p" -ForegroundColor Red }
}
Write-Host '  Nothing was changed on the share.'

if ($ResultFile) {
    [ordered]@{
        givenAs     = $given
        folder      = $folder
        unc         = $isUnc
        exists      = $exists
        current     = $current
        writeTested = $writeTested
        canPublish  = $canPublish
        readers     = @($readers)
        writers     = @($writers)
        warnings    = @($warnings)
        problems    = @($problems)
        checkedUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        checkedBy   = $env:USERNAME
        checkedOn   = $env:COMPUTERNAME
    } | ConvertTo-Json -Depth 3 | Set-Content -Path $ResultFile -Encoding utf8
}
if ($canPublish) { exit 0 }
exit 1
