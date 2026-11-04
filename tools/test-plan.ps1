<#
.SYNOPSIS
    Chooses the tests a change needs from tests\areas.json, runs them, and reports what has been
    verified where and what needs revalidation.

.DESCRIPTION
    A development iteration runs the tests for what it changed, not the whole suite every time
    (docs\testing.md). This reads the map of functional areas in tests\areas.json and works out,
    for the files changed since -Since:

      - the areas they belong to;
      - files in no area: treated as shared (the full unit suite), except documentation and old
        checkpoints (noTests in the map), which need no tests;
      - the dotnet test filter for those areas (plus the map's always-run checks), or "full" when
        a shared file changed;
      - the Civil 3D smoke-test sections, and the Civil 3D runners run by hand, a milestone run of
        this change should look at;
      - whether the installer tests (deploy\test-release.ps1, deploy\test-installer.ps1) and the
        Civil 3D harness compile check (tests\SmokeHarnessCompileCheck) are needed.

    The changed files are what differs between -Since and the working tree: commits, staged and
    unstaged edits and new files that git does not ignore; with -Until, between the two commits.
    Renames count under both names.

    -Run runs those unit tests (dotnet test tests\FieldCodes.Tests) and exits with dotnet's exit code.
    -Status prints, for every area, what verified it at which commit and whether files in it (or in
    a shared area) changed since then, so that it needs revalidation.

    Runs in Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER Since
    The commit (or branch) to compare with. Default: the current branch's upstream if it has one,
    else HEAD~1.

.PARAMETER Until
    Compare with this commit instead of the working tree (UPDATEFTF passes the commit it builds).

.PARAMETER Run
    Run the chosen unit tests (or the whole suite) and exit with dotnet test's exit code.

.PARAMETER Json
    Print the plan (or with -Status, the status) as JSON instead of text.

.PARAMETER OutFile
    Also write the plan (or status) as JSON to this file.

.PARAMETER AndFilter
    With -Run: a dotnet test filter every test must also match, e.g. an exclusion for this system.

.PARAMETER Configuration
    With -Run: the build configuration for dotnet test (-c).

.PARAMETER NoBuild
    With -Run: do not build first (dotnet test --no-build).

.PARAMETER Repo
    The repository (default: the one this script is in).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\test-plan.ps1
.EXAMPLE
    pwsh tools/test-plan.ps1 -Since origin/integration -Run
.EXAMPLE
    pwsh tools/test-plan.ps1 -Status
#>
[CmdletBinding()]
param(
    [string]$Since,
    [string]$Until,
    [switch]$Run,
    [switch]$Json,
    [switch]$Status,
    [string]$OutFile,
    [string]$AndFilter,
    [string]$Configuration,
    [switch]$NoBuild,
    [string]$Repo
)

$ErrorActionPreference = 'Stop'
if (-not $Repo) { $Repo = Split-Path -Parent $PSScriptRoot }
$Repo = [IO.Path]::GetFullPath($Repo)
$mapFile = Join-Path (Join-Path $Repo 'tests') 'areas.json'
$mapName = 'tests/areas.json'

function Stop-Plan([string]$why) {
    [Console]::Error.WriteLine("test-plan: $why")
    exit 2
}

# git in the repository, its output as lines; stderr is dropped (an unknown ref is a result here).
function Invoke-Git([string[]]$arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & git -C $Repo -c core.quotepath=off @arguments 2>$null
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $saved }
    $lines = @($out | ForEach-Object { "$_" } | Where-Object { $_ -ne '' })
    return [pscustomobject]@{ Code = $code; Lines = $lines }
}

function Resolve-Commit([string]$ref) {
    $r = Invoke-Git @('rev-parse', '--verify', '--quiet', "$ref^{commit}")
    if ($r.Code -ne 0 -or $r.Lines.Count -eq 0) { return $null }
    return $r.Lines[0]
}

function Short([string]$commit) {
    if ($commit -and $commit.Length -ge 7) { return $commit.Substring(0, 7) }
    return "$commit"
}

# The glob of tests\areas.json (and AreaMapTests): * within a folder, ** across folders.
function ConvertTo-GlobRegex([string]$glob) {
    $re = New-Object System.Text.StringBuilder
    [void]$re.Append('^')
    $i = 0
    while ($i -lt $glob.Length) {
        $c = $glob[$i]
        if ($c -eq '*' -and $i + 1 -lt $glob.Length -and $glob[$i + 1] -eq '*') {
            if ($i + 2 -lt $glob.Length -and $glob[$i + 2] -eq '/') { [void]$re.Append('(?:.*/)?'); $i += 3 }
            else { [void]$re.Append('.*'); $i += 2 }
            continue
        }
        if ($c -eq '*') { [void]$re.Append('[^/]*') }
        elseif ($c -eq '?') { [void]$re.Append('[^/]') }
        else { [void]$re.Append([regex]::Escape([string]$c)) }
        $i++
    }
    [void]$re.Append('$')
    return New-Object System.Text.RegularExpressions.Regex ($re.ToString())
}

function Test-Globs($globs, [string]$path) {
    foreach ($g in $globs) { if ($g.IsMatch($path)) { return $true } }
    return $false
}

function Get-List($object, [string]$name) {
    if ($null -eq $object) { return @() }
    $p = $object.PSObject.Properties[$name]
    if ($null -eq $p -or $null -eq $p.Value) { return @() }
    return @($p.Value)
}

function Get-Flag($object, [string]$name) {
    $p = $object.PSObject.Properties[$name]
    return ($null -ne $p -and $p.Value -eq $true)
}

function Write-JsonOut($value) {
    $text = ConvertTo-Json -InputObject $value -Depth 8
    if ($OutFile) { Set-Content -LiteralPath $OutFile -Value $text -Encoding utf8 }
    if ($Json) { Write-Output $text }
}

# ===================================================================== the map

if (-not (Test-Path -LiteralPath $mapFile)) { Stop-Plan "no $mapName in $Repo" }
try { $map = Get-Content -LiteralPath $mapFile -Raw | ConvertFrom-Json } catch { Stop-Plan "$mapName is not valid JSON: $($_.Exception.Message)" }
$areas = @()
foreach ($a in @($map.areas)) {
    $areas += [pscustomobject]@{
        Id = "$($a.id)"; Title = "$($a.title)"; Shared = ($a.shared -eq $true)
        Globs = @(Get-List $a 'paths' | ForEach-Object { ConvertTo-GlobRegex "$_" })
        Tests = @(Get-List $a 'unitTests' | ForEach-Object { "$_" })
        Civil3d = @(Get-List $a 'civil3d' | ForEach-Object { "$_" })
        Manual = @(Get-List $a 'civil3dManual' | ForEach-Object { "$_" })
        Installer = (Get-Flag $a 'installerTests'); Harness = (Get-Flag $a 'harnessCompile')
        Verified = $a.verified
    }
}
$noTests = @(Get-List $map.noTests 'paths' | ForEach-Object { ConvertTo-GlobRegex "$_" })
$alwaysRun = @(Get-List $map 'alwaysRun' | ForEach-Object { "$_" })
$allSections = @($map.civil3dSections.PSObject.Properties | ForEach-Object { $_.Name })
$installerScripts = @($map.installerTests.PSObject.Properties | ForEach-Object { $_.Name })
if ((Invoke-Git @('rev-parse', '--git-dir')).Code -ne 0) { Stop-Plan "$Repo is not a git repository" }
$head = Resolve-Commit 'HEAD'

# ===================================================================== -Status

if ($Status) {
    $diffs = @{}
    # Files changed between a recorded commit and HEAD (Known is false when the commit is not in
    # this repository). An object, not a bare array: PowerShell returns an empty array as nothing.
    function Get-ChangedSince([string]$commit) {
        if ($diffs.ContainsKey($commit)) { return $diffs[$commit] }
        $value = [pscustomobject]@{ Known = $false; Files = @() }
        if (Resolve-Commit $commit) {
            # The record itself is not code: writing a result into it needs no revalidation.
            $value = [pscustomobject]@{ Known = $true
                Files = @((Invoke-Git @('diff', '--name-only', '--no-renames', $commit, 'HEAD')).Lines | Where-Object { $_ -ne $mapName }) }
        }
        $diffs[$commit] = $value
        return $value
    }
    # A change to shared code (a shared area, or a file in no area that is not documentation) runs
    # the full suite, so it leaves every area to revalidate, not only its own.
    function Test-SharedFile([string]$path) {
        $hits = @($areas | Where-Object { Test-Globs $_.Globs $path })
        if ($hits.Count -eq 0) { return (-not (Test-Globs $noTests $path)) }
        return (@($hits | Where-Object { $_.Shared }).Count -gt 0)
    }
    function Get-Staleness($area, $record) {
        $since = Get-ChangedSince "$($record.commit)"
        if (-not $since.Known) { return [pscustomobject]@{ Known = $false; Own = @(); Shared = @() } }
        $own = @($since.Files | Where-Object { Test-Globs $area.Globs $_ })
        $sharedChanged = @($since.Files | Where-Object { (-not (Test-Globs $area.Globs $_)) -and (Test-SharedFile $_) })
        return [pscustomobject]@{ Known = $true; Own = $own; Shared = $sharedChanged }
    }
    function Format-Files($files) {
        $list = @($files)
        $text = ($list | Select-Object -First 4) -join ', '
        if ($list.Count -gt 4) { $text += (', and {0} more' -f ($list.Count - 4)) }
        return $text
    }
    function Format-Staleness($stale, $record) {
        if (-not $stale.Known) { return (' -- commit {0} is not in this repository (fetch it to compare)' -f (Short "$($record.commit)")) }
        $parts = @()
        if ($stale.Own.Count -gt 0) { $parts += ('{0} file(s) of this area changed since: {1}' -f $stale.Own.Count, (Format-Files $stale.Own)) }
        if ($stale.Shared.Count -gt 0) { $parts += ('shared code changed since: {0}' -f (Format-Files $stale.Shared)) }
        if ($parts.Count -eq 0) { return '' }
        return (' -- NEEDS REVALIDATION: ' + ($parts -join '; '))
    }

    $report = @()
    foreach ($a in $areas) {
        $v = $a.Verified
        $entry = [ordered]@{ id = $a.Id; title = $a.Title; shared = $a.Shared }
        $lines = @()

        $unit = $null
        if ($v) { $unit = $v.unit }
        if ($null -eq $unit) {
            $entry.unit = [ordered]@{ state = 'not verified' }
            $lines += '   unit tests: NOT VERIFIED'
        } else {
            $stale = Get-Staleness $a $unit
            $state = 'verified'
            if (-not $stale.Known) { $state = 'unknown commit' } elseif ($stale.Own.Count -gt 0 -or $stale.Shared.Count -gt 0) { $state = 'needs revalidation' }
            $entry.unit = [ordered]@{ state = $state; commit = "$($unit.commit)"; date = "$($unit.date)"; where = "$($unit.where)"; changed = @($stale.Own); sharedChanged = @($stale.Shared) }
            $lines += ('   unit tests: passed at {0} on {1} ({2}){3}' -f (Short "$($unit.commit)"), $unit.date, $unit.where, (Format-Staleness $stale $unit))
        }

        $civil = $null
        if ($v) { $civil = $v.civil3d }
        if ($a.Civil3d.Count -eq 0) {
            $entry.civil3d = [ordered]@{ state = 'no smoke-test section'; manual = @($a.Manual) }
            $text = '   Civil 3D:   no smoke-test section checks this area'
            if ($a.Manual.Count -gt 0) { $text += ('; by hand at a milestone: {0} (no result recorded)' -f ($a.Manual -join ', ')) }
            $lines += $text
        } elseif ($null -eq $civil) {
            $entry.civil3d = [ordered]@{ state = 'not verified'; sections = @($a.Civil3d) }
            $lines += ('   Civil 3D:   NOT VERIFIED (smoke-test sections {0})' -f ($a.Civil3d -join ', '))
        } else {
            $passed = @(Get-List $civil 'passed' | ForEach-Object { "$_" })
            $missing = @($a.Civil3d | Where-Object { $passed -notcontains $_ })
            $stale = Get-Staleness $a $civil
            $state = 'verified'
            if ($missing.Count -gt 0) { $state = 'partly verified' }
            if (-not $stale.Known) { $state = 'unknown commit' } elseif ($stale.Own.Count -gt 0 -or $stale.Shared.Count -gt 0) { $state = 'needs revalidation' }
            $entry.civil3d = [ordered]@{ state = $state; commit = "$($civil.commit)"; date = "$($civil.date)"; where = "$($civil.where)"; passed = $passed; notVerified = $missing; changed = @($stale.Own); sharedChanged = @($stale.Shared) }
            $text = ('   Civil 3D:   passed {0} at {1} on {2} ({3})' -f ($passed -join ', '), (Short "$($civil.commit)"), $civil.date, $civil.results)
            if ($missing.Count -gt 0) { $text += (' -- NOT VERIFIED: {0}' -f ($missing -join ', ')) }
            $lines += ($text + (Format-Staleness $stale $civil))
        }

        if ($a.Installer) {
            $runs = @()
            if ($v) { $runs = @(Get-List $v 'installer') }
            $entry.installer = @()
            foreach ($script in $installerScripts) {
                $rec = $runs | Where-Object { "$($_.script)" -eq $script } | Select-Object -First 1
                if ($null -eq $rec) {
                    $entry.installer += [ordered]@{ script = $script; state = 'not verified' }
                    $lines += ('   installer:  {0} NOT VERIFIED' -f $script)
                } else {
                    $stale = Get-Staleness $a $rec
                    $state = 'verified'
                    if (-not $stale.Known) { $state = 'unknown commit' } elseif ($stale.Own.Count -gt 0 -or $stale.Shared.Count -gt 0) { $state = 'needs revalidation' }
                    $entry.installer += [ordered]@{ script = $script; state = $state; commit = "$($rec.commit)"; date = "$($rec.date)"; where = "$($rec.where)"; result = "$($rec.result)"; changed = @($stale.Own) }
                    $lines += ('   installer:  {0} {1} at {2} on {3} ({4}){5}' -f $script, $rec.result, (Short "$($rec.commit)"), $rec.date, $rec.where, (Format-Staleness $stale $rec))
                }
            }
        }
        $notes = ''
        if ($v -and $v.notes) { $notes = "$($v.notes)" }
        $entry.notes = $notes
        if ($notes) { $lines += ('   notes:      ' + $notes) }
        $entry.lines = $lines
        $report += [pscustomobject]$entry
    }

    if ($Json -or $OutFile) {
        $out = @($report | ForEach-Object { $o = $_; $o.PSObject.Properties.Remove('lines'); $o })
        Write-JsonOut ([pscustomobject]@{ head = $head; areas = $out })
    }
    if (-not $Json) {
        Write-Host ('Verification record ({0}) against HEAD {1}; uncommitted changes are not counted.' -f $mapName, (Short $head))
        foreach ($r in $report) {
            Write-Host ''
            $kind = ''
            if ($r.shared) { $kind = ' [shared]' }
            Write-Host ('{0}{1} -- {2}' -f $r.id, $kind, $r.title)
            foreach ($l in $r.lines) { Write-Host $l }
        }
        $stale = @($report | Where-Object { $_.unit.state -eq 'needs revalidation' -or ($_.civil3d.state -eq 'needs revalidation') -or @($_.installer | Where-Object { $_.state -eq 'needs revalidation' }).Count -gt 0 })
        $never = @($report | Where-Object { $_.unit.state -eq 'not verified' -or $_.civil3d.state -eq 'not verified' -or @($_.installer | Where-Object { $_.state -eq 'not verified' }).Count -gt 0 })
        $partly = @($report | Where-Object { $_.civil3d.state -eq 'partly verified' })
        Write-Host ''
        Write-Host ('{0} area(s); needs revalidation: {1}; partly verified in Civil 3D: {2}; something never verified: {3}' -f $report.Count,
            ((@($stale | ForEach-Object { $_.id }) -join ', ') + $(if ($stale.Count -eq 0) { 'none' } else { '' })),
            ((@($partly | ForEach-Object { $_.id }) -join ', ') + $(if ($partly.Count -eq 0) { 'none' } else { '' })),
            ((@($never | ForEach-Object { $_.id }) -join ', ') + $(if ($never.Count -eq 0) { 'none' } else { '' })))
    }
    exit 0
}

# ===================================================================== the plan

$sinceWhy = 'given'
if (-not $Since) {
    $up = Invoke-Git @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{u}')
    if ($up.Code -eq 0 -and $up.Lines.Count -gt 0) { $Since = $up.Lines[0]; $sinceWhy = 'the upstream of this branch' }
    else { $Since = 'HEAD~1'; $sinceWhy = 'no upstream: only the last commit (give -Since to cover more)' }
}
$sinceCommit = Resolve-Commit $Since
if (-not $sinceCommit) { Stop-Plan "$Since is not a commit in $Repo" }
$untilCommit = $null
if ($Until) {
    $untilCommit = Resolve-Commit $Until
    if (-not $untilCommit) { Stop-Plan "$Until is not a commit in $Repo" }
    $listed = @((Invoke-Git @('diff', '--name-only', '--no-renames', $sinceCommit, $untilCommit)).Lines)
} else {
    $listed = @((Invoke-Git @('diff', '--name-only', '--no-renames', $sinceCommit)).Lines) +
              @((Invoke-Git @('ls-files', '--others', '--exclude-standard')).Lines)
}
$set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
foreach ($f in $listed) { [void]$set.Add($f.Replace('\', '/')) }
$changed = @($set)

$files = @()
$touched = @{}
$unmatched = @()
$docs = @()
foreach ($f in $changed) {
    $hits = @($areas | Where-Object { Test-Globs $_.Globs $f })
    $kind = 'area'
    if ($hits.Count -eq 0) {
        if (Test-Globs $noTests $f) { $kind = 'no tests'; $docs += $f } else { $kind = 'in no area'; $unmatched += $f }
    }
    foreach ($h in $hits) { $touched[$h.Id] = $true }
    $files += [pscustomobject]@{ path = $f; kind = $kind; areas = @($hits | ForEach-Object { $_.Id }) }
}
$touchedAreas = @($areas | Where-Object { $touched.ContainsKey($_.Id) })    # in the map's order
$fullBecause = @()
foreach ($x in $files) {
    $sharedHits = @($touchedAreas | Where-Object { $_.Shared -and ($x.areas -contains $_.Id) } | ForEach-Object { $_.Id })
    if ($sharedHits.Count -gt 0) { $fullBecause += ('{0} (shared: {1})' -f $x.path, ($sharedHits -join ', ')) }
    elseif ($x.kind -eq 'in no area') { $fullBecause += ('{0} (in no area: treated as shared)' -f $x.path) }
}
$full = ($fullBecause.Count -gt 0)
$codeChanged = (($changed.Count - $docs.Count) -gt 0)

$clauses = @()
if (-not $full -and $codeChanged) {
    $seen = @{}
    foreach ($c in (@($touchedAreas | ForEach-Object { $_.Tests }) + $alwaysRun)) {
        if ($c -and -not $seen.ContainsKey($c)) { $seen[$c] = $true; $clauses += $c }
    }
    $clauses = @($clauses | Sort-Object)
}
$filter = ''
if ($full) { $filter = 'full' } elseif ($clauses.Count -gt 0) { $filter = ($clauses -join '|') }
$classes = @($clauses | ForEach-Object { $_ -replace '^FullyQualifiedName~FieldCodes\.Tests\.', '' -replace '\.$', '' })

# A milestone run of a change that touches shared code looks at every section and every runner.
if ($full) {
    $sections = @($allSections)
    $manual = @($areas | ForEach-Object { $_.Manual } | Select-Object -Unique)
} else {
    $sections = @($allSections | Where-Object { $s = $_; @($touchedAreas | Where-Object { $_.Civil3d -contains $s }).Count -gt 0 })
    $manual = @($touchedAreas | ForEach-Object { $_.Manual } | Select-Object -Unique)
}
$installer = ($unmatched.Count -gt 0) -or (@($touchedAreas | Where-Object { $_.Installer }).Count -gt 0)
$harness = ($unmatched.Count -gt 0) -or (@($touchedAreas | Where-Object { $_.Harness }).Count -gt 0)

$plan = [pscustomobject]@{
    since = $sinceCommit; sinceRef = $Since; sinceWhy = $sinceWhy; until = $untilCommit; workingTree = (-not $Until); head = $head
    changed = $files
    areas = @($touchedAreas | ForEach-Object { $_.Id })
    sharedAreas = @($touchedAreas | Where-Object { $_.Shared } | ForEach-Object { $_.Id })
    unmatched = $unmatched; noTests = $docs
    full = $full; fullBecause = $fullBecause
    unitTests = $(if ($full) { 'full' } elseif ($clauses.Count -gt 0) { 'targeted' } else { 'none' })
    filter = $filter; testClasses = $classes
    civil3d = $sections; civil3dManual = $manual
    installerTests = $installer; harnessCompile = $harness
}

if ($Json -or $OutFile) { Write-JsonOut $plan }
if (-not $Json) {
    $what = 'the working tree'
    if ($Until) { $what = (Short $untilCommit) }
    Write-Host ('Test plan: {0} file(s) differ between {1} ({2}, {3}) and {4}' -f $changed.Count, (Short $sinceCommit), $Since, $sinceWhy, $what)
    foreach ($x in $files) {
        $where = ($x.areas -join ', ')
        if ($x.kind -ne 'area') { $where = $x.kind }
        Write-Host ('     {0}  [{1}]' -f $x.path, $where)
    }
    $areaText = (@($touchedAreas | ForEach-Object { if ($_.Shared) { $_.Id + ' (shared)' } else { $_.Id } }) -join ', ')
    if (-not $areaText) { $areaText = 'none' }
    Write-Host ('  areas:          ' + $areaText)
    if ($full) {
        Write-Host '  unit tests:     FULL SUITE, because of:'
        foreach ($b in $fullBecause) { Write-Host ('                  ' + $b) }
    } elseif ($clauses.Count -gt 0) {
        Write-Host ('  unit tests:     {0} test class(es): {1}' -f $classes.Count, ($classes -join ', '))
    } elseif ($changed.Count -gt 0) {
        Write-Host '  unit tests:     none (documentation only)'
    } else {
        Write-Host '  unit tests:     none (nothing changed)'
    }
    $sectionText = ($sections -join ', ')
    if (-not $sectionText) { $sectionText = 'none of its sections checks these areas' }
    Write-Host ('  Civil 3D (at a milestone): smoke-test sections: ' + $sectionText)
    if ($manual.Count -gt 0) { Write-Host ('                  by hand: ' + ($manual -join ', ')) }
    $installerText = 'not needed'
    if ($installer) { $installerText = 'NEEDED: ' + ($installerScripts -join ', ') }
    Write-Host ('  installer tests: ' + $installerText)
    $harnessText = 'not needed'
    if ($harness) { $harnessText = 'NEEDED: dotnet build tests/SmokeHarnessCompileCheck' }
    Write-Host ('  harness compile check: ' + $harnessText)
}

if ($Run) {
    if (-not $full -and $clauses.Count -eq 0) {
        Write-Host 'No unit tests to run for this change.'
        exit 0
    }
    $runFilter = $AndFilter
    if (-not $full) {
        $runFilter = '(' + $filter + ')'
        if ($AndFilter) { $runFilter += ('&' + $AndFilter) }
    }
    $dotnetArgs = @('test', (Join-Path (Join-Path $Repo 'tests') 'FieldCodes.Tests'), '--nologo', '-v', 'q')
    if ($Configuration) { $dotnetArgs += @('-c', $Configuration) }
    if ($NoBuild) { $dotnetArgs += '--no-build' }
    if ($runFilter) { $dotnetArgs += @('--filter', $runFilter) }
    if ($full) { Write-Host 'Running the full unit suite.' } else { Write-Host ('Running {0} test class(es).' -f $classes.Count) }
    & dotnet @dotnetArgs
    exit $LASTEXITCODE
}
exit 0
