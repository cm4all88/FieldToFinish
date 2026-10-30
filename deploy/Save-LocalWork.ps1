<#
.SYNOPSIS
    Saves everything in a working copy that is not on the remote yet -- uncommitted edits, new
    files, local commits -- to a new branch on the remote, without changing the working copy.

.DESCRIPTION
    Use it before switching how FTF is built, or any time work exists only on one machine.

      * Nothing in the working copy changes: not the files, not the current branch, not what is
        staged. The snapshot is written through a temporary index, so `git status` afterwards
        looks exactly as it did before.
      * The snapshot is one commit on top of the current HEAD, holding every tracked change
        (edits and deletions) and every new file that .gitignore does not exclude. It is put on
        a new branch, wip/<user>-<date>, and pushed, so it can be reviewed and merged into the
        integration branch.
      * Files .gitignore excludes are NOT in the snapshot. They are listed (bin/obj aside) so
        nothing is silently left behind; name any you need with -Also.

.PARAMETER Path
    The working copy. Defaults to the current folder.

.PARAMETER Name
    The branch to create. Defaults to wip/<user>-<yyyyMMdd-HHmm>.

.PARAMETER Also
    Ignored files to include anyway (paths relative to the working copy), e.g. a deployment file
    that .gitignore excludes.

.PARAMETER NoPush
    Create the branch locally only.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File C:\dev\FieldToFinish-build\deploy\Save-LocalWork.ps1 -Path C:\dev\FieldToFinish
#>
[CmdletBinding()]
param(
    [string]$Path = (Get-Location).Path,
    [string]$Name,
    [string[]]$Also = @(),
    [string]$Remote = 'origin',
    [switch]$NoPush
)

$ErrorActionPreference = 'Continue'

function Fail([string]$text) {
    Write-Host "FAILED: $text" -ForegroundColor Red
    Write-Host '   Nothing in your working copy was changed.' -ForegroundColor Yellow
    exit 1
}

function Run-Git([string[]]$arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $out = & git @arguments 2>&1 | ForEach-Object { "$_" }; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $saved }
    return [pscustomobject]@{ Code = $code; Lines = @($out) }
}

if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) { Fail 'git is not on PATH.' }
$root = (Run-Git @('-C', $Path, 'rev-parse', '--show-toplevel')).Lines | Select-Object -First 1
if (-not $root -or -not (Test-Path $root)) { Fail "$Path is not inside a git working copy." }
$root = [IO.Path]::GetFullPath($root)

$head = (Run-Git @('-C', $root, 'rev-parse', 'HEAD')).Lines | Select-Object -First 1
$branch = (Run-Git @('-C', $root, 'symbolic-ref', '--short', '-q', 'HEAD')).Lines | Select-Object -First 1
if (-not $branch) { $branch = "detached at $($head.Substring(0, 7))" }
if (-not $Name) { $Name = 'wip/' + ($env:USERNAME -replace '[^A-Za-z0-9_.-]', '') + '-' + (Get-Date -Format 'yyyyMMdd-HHmm') }
if ((Run-Git @('-C', $root, 'check-ref-format', '--branch', $Name)).Code -ne 0) { Fail "not a valid branch name: $Name" }
if ((Run-Git @('-C', $root, 'rev-parse', '--verify', '-q', "refs/heads/$Name")).Code -eq 0) { Fail "branch $Name already exists; pass -Name." }

Write-Host "Working copy: $root"
Write-Host "On:           $branch ($($head.Substring(0, 7)))"

# --- the snapshot, through a temporary copy of the index -----------------------------------

$gitDir = (Run-Git @('-C', $root, 'rev-parse', '--absolute-git-dir')).Lines | Select-Object -First 1
$realIndex = Join-Path $gitDir 'index'
$tempIndex = Join-Path ([IO.Path]::GetTempPath()) ('ftf-save-' + [Guid]::NewGuid().ToString('N') + '.index')
if (Test-Path $realIndex) { Copy-Item -LiteralPath $realIndex -Destination $tempIndex }
$before = (Run-Git @('-C', $root, 'status', '--porcelain')).Lines

$env:GIT_INDEX_FILE = $tempIndex
try {
    $add = Run-Git @('-C', $root, 'add', '-A')
    if ($add.Code -ne 0) { Fail ('git add failed: ' + ($add.Lines -join ' ')) }
    foreach ($extra in $Also) {
        $forced = Run-Git @('-C', $root, 'add', '-f', '--', $extra)
        if ($forced.Code -ne 0) { Fail ("could not include ${extra}: " + ($forced.Lines -join ' ')) }
    }
    $tree = (Run-Git @('-C', $root, 'write-tree')).Lines | Select-Object -First 1
} finally {
    Remove-Item Env:\GIT_INDEX_FILE -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tempIndex -Force -ErrorAction SilentlyContinue
}
if ($tree -notmatch '^[0-9a-f]{40}$') { Fail 'could not write the snapshot tree.' }

$message = "Work in progress from $env:COMPUTERNAME ($branch), saved $(Get-Date -Format 'yyyy-MM-dd HH:mm')`n`n" +
           "Everything that was not committed in $root -- edits, deletions and new files -- as it\n" +
           "stood, on top of $head. Saved by deploy/Save-LocalWork.ps1; the working copy was not changed."
$commit = (Run-Git @('-C', $root, 'commit-tree', $tree, '-p', $head, '-m', $message.Replace('\n', "`n"))).Lines | Select-Object -First 1
if ($commit -notmatch '^[0-9a-f]{40}$') { Fail 'could not create the snapshot commit.' }
$made = Run-Git @('-C', $root, 'branch', $Name, $commit)
if ($made.Code -ne 0) { Fail ('could not create branch ' + $Name + ': ' + ($made.Lines -join ' ')) }

$after = (Run-Git @('-C', $root, 'status', '--porcelain')).Lines
if (($before -join "`n") -ne ($after -join "`n")) {
    Write-Host 'WARNING: git status reads differently than before (something else changed the working copy meanwhile).' -ForegroundColor Yellow
}

# --- what is in it, and what is not ------------------------------------------------------

$changes = @((Run-Git @('-C', $root, 'diff', '--name-status', $head, $commit)).Lines | Where-Object { $_ })
Write-Host ''
Write-Host ("Snapshot {0} on branch {1}: {2} file(s) differ from {3}" -f $commit.Substring(0, 7), $Name, $changes.Count, $head.Substring(0, 7))
foreach ($line in $changes) { Write-Host "   $line" }

$ignored = @((Run-Git @('-C', $root, 'status', '--porcelain', '--ignored')).Lines |
             Where-Object { $_ -like '!! *' } | ForEach-Object { $_.Substring(3) } |
             Where-Object { $_ -notmatch '(^|/)(bin|obj|\.vs|TestResults|packages)/' -and $_ -notmatch '\.(user|suo|log)$' } |
             Where-Object { $Also -notcontains $_ -and $Also -notcontains ($_ -replace '/', '\') })
if ($ignored.Count -gt 0) {
    Write-Host ''
    Write-Host "Not in the snapshot -- .gitignore excludes these (name any you need with -Also):" -ForegroundColor Yellow
    foreach ($file in ($ignored | Select-Object -First 40)) { Write-Host "   $file" -ForegroundColor Yellow }
    if ($ignored.Count -gt 40) { Write-Host ("   ... and {0} more" -f ($ignored.Count - 40)) -ForegroundColor Yellow }
}

# --- push ------------------------------------------------------------------------------------

if ($NoPush) {
    Write-Host ''
    Write-Host "Saved locally as branch $Name (not pushed). Push it with: git push $Remote $Name"
    exit 0
}
$pushed = $false
foreach ($wait in @(0, 2, 4, 8, 16)) {
    if ($wait -gt 0) { Start-Sleep -Seconds $wait }
    $push = Run-Git @('-C', $root, 'push', $Remote, "refs/heads/${Name}:refs/heads/$Name")
    if ($push.Code -eq 0) { $pushed = $true; break }
}
Write-Host ''
if ($pushed) {
    Write-Host "Pushed $Remote/$Name. Your working copy is exactly as it was." -ForegroundColor Green
    Write-Host 'Ask for it to be reviewed and merged into the integration branch.'
} else {
    Write-Host "Saved locally as branch $Name, but the push failed:" -ForegroundColor Yellow
    $push.Lines | ForEach-Object { Write-Host "   $_" -ForegroundColor Yellow }
    Write-Host "Retry with: git -C `"$root`" push $Remote $Name"
    exit 1
}
exit 0
