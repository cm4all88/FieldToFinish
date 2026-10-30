<#
.SYNOPSIS
    Builds, validates and installs the latest approved Field to Finish into Civil 3D 2024.

.DESCRIPTION
    One command for "give me the current FTF":

      1. Fetches the approved branch (origin/integration) and records its head, so FTFVERSION
         inside Civil 3D can say whether it is running the latest approved build.
      2. Builds that exact commit in a separate, FTF-managed checkout beside the repository
         (C:\dev\FieldToFinish-build by default). Your own working copy -- its branch and any
         uncommitted changes -- is never touched.
      3. Runs the unit tests, packages the bundle (refusing any DLL whose embedded commit is not
         the commit being built), and runs the installer's own test in a scratch profile.
         Nothing installed has changed yet; any failure stops here.
      4. Waits for Civil 3D to be closed (or tells you to close it), backs up the installed FTF
         and its startup registration, installs the new bundle into
         %APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle, registers it to load when
         Civil 3D starts, and checks every installed file against the package.
      5. Proves the installed build works inside Civil 3D, headless (deploy\smoke-test.ps1): the
         loaded DLL is this build, FTFVERSION and FTFCONTOUR are registered and run, and FTFCONTOUR
         draws the right contours without touching the surface. If that fails, the previous FTF
         is put back automatically.
      6. Reports what is installed and what to do next (start or restart Civil 3D).

    Only a clean build of origin/integration that passed every check is stamped "approved".
    -Dev builds another ref for testing and is stamped "dev" -- never an approved release.

    Exit codes: 0 installed (or already current), 1 failed (the installed FTF is unchanged or was
    restored), 2 validated but Civil 3D is open -- close it and run UPDATEFTF again.

.PARAMETER Status
    Only report: installed build, latest approved build, startup registration, anything else that
    loads FTF, and whether Civil 3D needs a restart. Builds and changes nothing.

.PARAMETER Rollback
    Puts back the most recent backup made by UPDATEFTF (Civil 3D must be closed).

.PARAMETER WaitForCivil3D
    When Civil 3D is open after validation, wait for it to close instead of stopping.

.PARAMETER Force
    Rebuild and reinstall even when the installed build is already the latest approved one.

.PARAMETER SkipSmoke
    Skip the in-Civil 3D smoke test. Not recommended: the install is then unproven in Civil 3D.

.PARAMETER Dev
.PARAMETER Ref
    Build and install a development ref (a branch such as origin/claude/xyz, or a commit) for
    testing. Stamped "dev"; FTFVERSION says so.

.PARAMETER PublishTo
    After a successful approved install, also publish the validated setup folder to this office
    folder (e.g. U:\Survey\FTF\FieldToFinish-Setup). Surveyors installed from there update
    themselves the next time they close Civil 3D.

.PARAMETER FixStartup
    Also disable (LOADCTRLS=0, after a .reg backup) other registry entries that make Civil 3D load
    some other FieldCodes.Cad.dll at startup, such as a development build in bin\Debug.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File C:\dev\FieldToFinish-build\UPDATEFTF.ps1
.EXAMPLE
    C:\dev\UPDATEFTF.cmd -Status
#>
[CmdletBinding()]
param(
    [switch]$Status,
    [switch]$Rollback,
    [switch]$WaitForCivil3D,
    [switch]$Force,
    [switch]$SkipSmoke,
    [switch]$Dev,
    [string]$Ref,
    [string]$PublishTo,
    [switch]$FixStartup,
    [string]$Branch = 'integration',
    [string]$Remote = 'origin',
    [string]$BuildRoot,
    [string]$C3D2024 = 'C:\Program Files\Autodesk\AutoCAD 2024',
    [switch]$Relaunched
)

$ErrorActionPreference = 'Continue'

$started = Get-Date
$state = Join-Path $env:LOCALAPPDATA 'FieldToFinish'
$pluginsRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$installedDir = Join-Path $pluginsRoot 'FieldToFinish.bundle'
$installedDll = Join-Path $installedDir 'Contents\2024\FieldCodes.Cad.dll'
# The PowerShell running this script, to run the other scripts and a newer UPDATEFTF with.
$hostExe = if ($PSVersionTable.PSEdition -eq 'Desktop') { Join-Path $PSHOME 'powershell.exe' }
           else { Join-Path $PSHOME $(if ($env:OS -eq 'Windows_NT') { 'pwsh.exe' } else { 'pwsh' }) }
if (-not (Test-Path -LiteralPath $hostExe)) {
    $found = Get-Command pwsh, powershell -ErrorAction SilentlyContinue | Select-Object -First 1
    $hostExe = if ($found) { $found.Source } else { 'powershell.exe' }
}
$script:touched = $false        # the installed FTF has been changed in this run
$script:backup = $null
$selfHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash

foreach ($dir in @($state, (Join-Path $state 'logs'), (Join-Path $state 'updates'), (Join-Path $state 'backups'))) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}
$logFile = Join-Path $state ('logs\UPDATEFTF-' + $started.ToString('yyyyMMdd-HHmmss') + '.log')

# ===================================================================== output

function Log([string]$text) { Add-Content -Path $logFile -Value $text -Encoding utf8 -ErrorAction SilentlyContinue }
function Step([string]$title) { Write-Host ''; Write-Host "== $title" -ForegroundColor Cyan; Log "== $title" }
function Ok([string]$text) { Write-Host "   OK    $text" -ForegroundColor Green; Log "   OK    $text" }
function Info([string]$text) { Write-Host "         $text"; Log "         $text" }
function Warn([string]$text) { Write-Host "   WARN  $text" -ForegroundColor Yellow; Log "   WARN  $text" }

function Fail([string]$text, [string[]]$details) {
    Write-Host ''
    Write-Host "FAILED: $text" -ForegroundColor Red
    Log "FAILED: $text"
    foreach ($d in @($details)) { if ($d) { Write-Host "   $d" -ForegroundColor Red; Log "   $d" } }
    Write-Host ''
    if ($script:touched) {
        Write-Host '   The installed FTF was changed in this run; see the lines above for what was restored.' -ForegroundColor Red
    } elseif (Test-Path $installedDir) {
        Write-Host '   Your installed FTF was NOT changed. Civil 3D will keep loading it as before.' -ForegroundColor Yellow
    } else {
        Write-Host '   Nothing was installed.' -ForegroundColor Yellow
    }
    Write-Host "   Full log: $logFile"
    exit 1
}

# Runs a program, capturing everything it writes (stderr included) without letting Windows
# PowerShell turn stderr text into a terminating error.
function Invoke-Native([string]$exe, [string[]]$arguments, [switch]$Echo, [string]$Capture) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $lines = New-Object System.Collections.Generic.List[string]
    try {
        & $exe @arguments 2>&1 | ForEach-Object {
            $line = "$_"
            $lines.Add($line)
            if ($Echo) { Write-Host "         $line" }
        }
        $code = $LASTEXITCODE
    } catch {
        $lines.Add($_.Exception.Message)
        $code = 9009
    } finally { $ErrorActionPreference = $saved }
    if ($Capture) { $lines | Set-Content -Path $Capture -Encoding utf8 }
    $lines | ForEach-Object { Log "           | $_" }
    return [pscustomobject]@{ Code = $code; Lines = $lines.ToArray() }
}

function Run-Git([string[]]$arguments) { return Invoke-Native 'git' $arguments }

function Invoke-Script([string]$path, [string[]]$arguments, [switch]$Echo) {
    $all = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $path) + $arguments
    return Invoke-Native $hostExe $all -Echo:$Echo
}

# ===================================================================== reading state

function Read-BuildInfo([string]$bundle) {
    $file = Join-Path $bundle 'build-info.json'
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    try { return (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json) } catch { return $null }
}

# Windows PowerShell 5.1 leaves ISO dates from JSON as text; PowerShell 7 turns them into dates.
function Utc-Text($value) {
    if ($value -is [DateTime]) { return $value.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }
    return "$value"
}

function Describe-Build($info) {
    if ($null -eq $info) { return 'a build without version information (installed before UPDATEFTF)' }
    $short = if ($info.commit) { "$($info.commit)".Substring(0, 7) } else { 'unknown' }
    $kind = if ($info.channel -eq 'approved' -and -not $info.dirty) { 'approved build' } else { 'DEV build' }
    return "$kind $short ($($info.branch)), built $(Utc-Text $info.builtUtc)"
}

# Civil 3D's own record of what to load at startup, per product, as install.ps1 writes it.
function Get-StartupEntries {
    $found = @()
    foreach ($hive in @('HKCU:\SOFTWARE\Autodesk\AutoCAD', 'HKLM:\SOFTWARE\Autodesk\AutoCAD')) {
        foreach ($release in @(Get-ChildItem $hive -ErrorAction SilentlyContinue)) {
            foreach ($product in @(Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue)) {
                $apps = Join-Path $product.PSPath 'Applications'
                foreach ($app in @(Get-ChildItem $apps -ErrorAction SilentlyContinue)) {
                    $p = Get-ItemProperty -Path $app.PSPath -ErrorAction SilentlyContinue
                    if ($null -eq $p) { continue }
                    $loader = if ($p.PSObject.Properties['LOADER']) { "$($p.LOADER)" } else { '' }
                    if ($app.PSChildName -ne 'FieldToFinish' -and $loader -notmatch 'FieldCodes\.Cad\.dll') { continue }
                    $found += [pscustomobject]@{
                        Key = $app.PSPath; RegName = $app.Name; Name = $app.PSChildName
                        Product = ($release.PSChildName + '\' + $product.PSChildName); Loader = $loader
                        LoadCtrls = $(if ($p.PSObject.Properties['LOADCTRLS']) { $p.LOADCTRLS } else { $null })
                        Machine = ($hive -like 'HKLM*')
                    }
                }
            }
        }
    }
    return $found
}

function Same-Path([string]$a, [string]$b) {
    try { return [IO.Path]::GetFullPath($a).TrimEnd('\') -ieq [IO.Path]::GetFullPath($b).TrimEnd('\') } catch { return $false }
}

# Anything else that would load a FieldCodes.Cad.dll into Civil 3D: a second copy fights the
# installed one over every command, and the older one often wins.
function Find-OtherLoaders {
    $problems = @()
    foreach ($entry in @(Get-StartupEntries)) {
        if ($entry.Name -eq 'FieldToFinish' -and (Same-Path $entry.Loader $installedDll)) { continue }
        if ($entry.LoadCtrls -eq 0) { continue }
        $problems += [pscustomobject]@{
            Kind = 'registry'; Entry = $entry
            Text = ("startup entry {0} ({1}) loads {2}" -f $entry.Name, $entry.Product, $entry.Loader)
        }
    }
    foreach ($release in @(Get-ChildItem 'HKCU:\SOFTWARE\Autodesk\AutoCAD' -ErrorAction SilentlyContinue)) {
        foreach ($product in @(Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue)) {
            foreach ($userProfile in @(Get-ChildItem (Join-Path $product.PSPath 'Profiles') -ErrorAction SilentlyContinue)) {
                $suite = Join-Path $userProfile.PSPath 'Dialogs\Appload\Startup'
                $values = Get-ItemProperty -Path $suite -ErrorAction SilentlyContinue
                if ($null -eq $values) { continue }
                foreach ($prop in $values.PSObject.Properties) {
                    if ("$($prop.Value)" -match 'FieldCodes\.Cad\.dll' -and -not (Same-Path "$($prop.Value)" $installedDll)) {
                        $problems += [pscustomobject]@{ Kind = 'suite'; Entry = $null
                            Text = ("Startup Suite (APPLOAD > Contents) in profile '{0}' loads {1}" -f $userProfile.PSChildName, $prop.Value) }
                    }
                }
            }
        }
    }
    foreach ($root in @((Join-Path $env:ProgramFiles 'Autodesk\ApplicationPlugins'), (Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins'), $pluginsRoot)) {
        foreach ($bundle in @(Get-ChildItem $root -Directory -ErrorAction SilentlyContinue)) {
            if (Same-Path $bundle.FullName $installedDir) { continue }
            if ($bundle.Name -match '\.(old|new)$') { continue }
            $manifest = Join-Path $bundle.FullName 'PackageContents.xml'
            if ((Test-Path $manifest) -and ((Get-Content $manifest -Raw -ErrorAction SilentlyContinue) -match 'FieldCodes\.Cad\.dll')) {
                $problems += [pscustomobject]@{ Kind = 'bundle'; Entry = $null; Text = ("another FTF bundle: " + $bundle.FullName) }
            }
        }
    }
    foreach ($support in @((Join-Path $env:APPDATA 'Autodesk\C3D 2024\enu\Support'))) {
        foreach ($name in @('acad.lsp', 'acaddoc.lsp', 'acad2024.lsp', 'acad2024doc.lsp')) {
            $file = Join-Path $support $name
            if ((Test-Path $file) -and ((Get-Content $file -Raw -ErrorAction SilentlyContinue) -match 'FieldCodes\.Cad')) {
                $problems += [pscustomobject]@{ Kind = 'lisp'; Entry = $null; Text = ("$file NETLOADs FieldCodes.Cad at startup") }
            }
        }
    }
    return $problems
}

function Report-OtherLoaders([switch]$Fix) {
    $others = @(Find-OtherLoaders)
    if ($others.Count -eq 0) { Ok 'nothing else loads an FTF DLL into Civil 3D'; return }
    foreach ($o in $others) { Warn $o.Text }
    if ($Fix) {
        $fixDir = Join-Path $state ('backups\startup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Force -Path $fixDir | Out-Null
        $n = 0
        foreach ($o in $others) {
            if ($o.Kind -ne 'registry' -or $o.Entry.Machine) { continue }
            $n++
            $reg = Join-Path $fixDir ("startup-$n.reg")
            [void](Invoke-Native 'reg.exe' @('export', $o.Entry.RegName, $reg, '/y'))
            Set-ItemProperty -Path $o.Entry.Key -Name LOADCTRLS -Value 0 -Type DWord
            Ok ("disabled {0} (LOADCTRLS=0); undo with: reg import `"{1}`"" -f $o.Entry.Name, $reg)
        }
    }
    Info 'Each of these loads a second FieldCodes.Cad.dll beside the installed one. Remove them:'
    Info '  registry entries: run UPDATEFTF -FixStartup (backs them up, then disables them);'
    Info '  Startup Suite: APPLOAD > Startup Suite > Contents > remove FieldCodes.Cad.dll;'
    Info '  other bundles / acad*.lsp: delete the bundle folder or the NETLOAD line.'
    Info 'And stop using NETLOAD on src\FieldCodes.Cad\bin\Debug -- the installed build already loads.'
}

function Civil3D-Processes { return @(Get-Process -Name acad -ErrorAction SilentlyContinue) }

function Verify-Installed([string]$expectedCommit) {
    $info = Read-BuildInfo $installedDir
    if ($null -eq $info) { return 'the installed bundle has no build-info.json' }
    if ("$($info.commit)" -ne $expectedCommit) { return "the installed bundle is $($info.commit), not $expectedCommit" }
    foreach ($prop in $info.files.PSObject.Properties) {
        $file = Join-Path $installedDir ($prop.Name.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $file)) { return "missing installed file: $($prop.Name)" }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $prop.Value) { return "installed file differs from the package: $($prop.Name)" }
    }
    return $null
}

function Show-Startup {
    $entries = @(Get-StartupEntries | Where-Object { $_.Name -eq 'FieldToFinish' -and (Same-Path $_.Loader $installedDll) })
    if ($entries.Count -gt 0) {
        foreach ($e in $entries) {
            if ($e.LoadCtrls -eq 2) { Ok ("Civil 3D ({0}) loads the installed FTF at startup" -f $e.Product) }
            else { Warn ("startup entry for {0} has LOADCTRLS={1}, not 2 (load at startup)" -f $e.Product, $e.LoadCtrls) }
        }
    } else {
        Warn 'no Civil 3D startup entry points at the installed FTF; Civil 3D relies on scanning the plugin folder (run UPDATEFTF -Force to re-register)'
    }
}

function Restart-Advice([string]$commitShort) {
    $running = Civil3D-Processes
    if ($running.Count -eq 0) {
        Info "NEXT: start Civil 3D. Type FTFVERSION -- it should say $commitShort."
        return
    }
    $installedAt = (Get-Item -LiteralPath $installedDll -ErrorAction SilentlyContinue)
    foreach ($p in $running) {
        $startedAt = $null
        try { $startedAt = $p.StartTime } catch { }
        if ($installedAt -and $startedAt -and $startedAt -lt $installedAt.LastWriteTime) {
            Warn ("RESTART Civil 3D: it was started (PID {0}, {1}) before this build was installed, so it is still running the previous one." -f $p.Id, $startedAt)
        } else {
            Info ("Civil 3D is running (PID {0}). Type FTFVERSION in it to see which build it loaded." -f $p.Id)
        }
    }
}

# ===================================================================== where things are

Log ("UPDATEFTF started " + $started.ToString('o') + " by " + $env:USERNAME + " on " + $env:COMPUTERNAME)
Write-Host 'Field to Finish -- update' -ForegroundColor White
Write-Host "   log: $logFile"

if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) { Fail 'git is not on PATH.' @('Install Git for Windows, then run this again.') }

$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$commonDir = (Run-Git @('-C', $here, 'rev-parse', '--git-common-dir')).Lines | Select-Object -First 1
if (-not $commonDir) { Fail "UPDATEFTF.ps1 must run from inside the FieldToFinish repository (or its build checkout): $here" @() }
if (-not [IO.Path]::IsPathRooted($commonDir)) { $commonDir = Join-Path $here $commonDir }
$commonDir = [IO.Path]::GetFullPath($commonDir)
if ((Split-Path $commonDir -Leaf) -ne '.git') { Fail "unexpected git layout ($commonDir)" @() }
$repoRoot = Split-Path $commonDir -Parent
if (-not $BuildRoot) { $BuildRoot = Join-Path (Split-Path $repoRoot -Parent) ((Split-Path $repoRoot -Leaf) + '-build') }
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$marker = Join-Path $BuildRoot '.ftf-build-checkout'
Info "repository:     $repoRoot (your working copy; never changed by this script)"
Info "build checkout: $BuildRoot"

# ===================================================================== -Rollback

if ($Rollback) {
    Step 'Rollback to the previous FTF'
    if ((Civil3D-Processes).Count -gt 0) { Fail 'Civil 3D is open. Close it, then run UPDATEFTF -Rollback again.' @() }
    $backups = @(Get-ChildItem (Join-Path $state 'backups') -Directory -ErrorAction SilentlyContinue |
                 Where-Object { Test-Path (Join-Path $_.FullName 'FieldToFinish.bundle') } | Sort-Object Name -Descending)
    if ($backups.Count -eq 0) { Fail 'there is no backup to roll back to.' @("Backups are kept in $state\backups.") }
    $from = $backups[0].FullName
    $current = Read-BuildInfo $installedDir
    Info ("restoring " + (Describe-Build (Read-BuildInfo (Join-Path $from 'FieldToFinish.bundle'))) + " from $from")
    if (Test-Path $installedDir) {
        $aside = Join-Path $state ('replaced\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Force -Path $aside | Out-Null
        Copy-Item -LiteralPath $installedDir -Destination $aside -Recurse -Force
        Remove-Item -LiteralPath $installedDir -Recurse -Force
        Info ("the build being replaced (" + (Describe-Build $current) + ") is kept in $aside")
    }
    Copy-Item -LiteralPath (Join-Path $from 'FieldToFinish.bundle') -Destination $pluginsRoot -Recurse -Force
    foreach ($reg in @(Get-ChildItem $from -Filter 'startup-*.reg' -ErrorAction SilentlyContinue)) {
        [void](Invoke-Native 'reg.exe' @('import', $reg.FullName))
    }
    Ok ("rolled back to " + (Describe-Build (Read-BuildInfo $installedDir)))
    Info 'NEXT: start Civil 3D (restart it if it was open).'
    exit 0
}

# ===================================================================== fetch

Step "Latest approved build ($Remote/$Branch)"
$fetched = $false
foreach ($wait in @(0, 2, 4, 8, 16)) {
    if ($wait -gt 0) { Info "fetch failed; retrying in $wait s"; Start-Sleep -Seconds $wait }
    $fetch = Run-Git @('-C', $repoRoot, 'fetch', '--quiet', $Remote, $Branch)
    if ($fetch.Code -eq 0) { $fetched = $true; break }
}
if (-not $fetched) { Fail "could not fetch $Remote/$Branch." $fetch.Lines }

$approvedHead = ((Run-Git @('-C', $repoRoot, 'rev-parse', "$Remote/$Branch^{commit}")).Lines | Select-Object -First 1)
if ($approvedHead -notmatch '^[0-9a-f]{40}$') { Fail "$Remote/$Branch does not resolve to a commit." @() }
$subject = ((Run-Git @('-C', $repoRoot, 'log', '-1', '--format=%s', $approvedHead)).Lines | Select-Object -First 1)
$committed = ((Run-Git @('-C', $repoRoot, 'log', '-1', '--format=%cI', $approvedHead)).Lines | Select-Object -First 1)
$headRecord = [ordered]@{
    commit       = $approvedHead
    branch       = $Branch
    subject      = $subject
    committedUtc = $(try { ([DateTimeOffset]::Parse($committed)).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ') } catch { $null })
    checkedUtc   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
}
$headRecord | ConvertTo-Json | Set-Content -Path (Join-Path $state 'latest-approved.json') -Encoding utf8
Ok ("latest approved: {0}  {1}" -f $approvedHead.Substring(0, 7), $subject)

if ($Dev) {
    if (-not $Ref) { Fail '-Dev needs -Ref: the branch or commit to build (e.g. -Ref origin/claude/my-feature).' @() }
    [void](Run-Git @('-C', $repoRoot, 'fetch', '--quiet', $Remote))
    $target = ((Run-Git @('-C', $repoRoot, 'rev-parse', "$Ref^{commit}")).Lines | Select-Object -First 1)
    if ($target -notmatch '^[0-9a-f]{40}$') { Fail "$Ref does not resolve to a commit." @() }
    $channel = 'dev'
    $branchName = ($Ref -replace '^(refs/remotes/|refs/heads/)', '') -replace ("^" + [regex]::Escape($Remote) + "/"), ''
    Warn ("DEV build of {0} ({1}): it will be stamped and reported as a development build, NOT an approved release." -f $Ref, $target.Substring(0, 7))
} else {
    if ($Ref) { Fail '-Ref builds a development ref and needs -Dev. Approved builds come only from the approved branch.' @() }
    $target = $approvedHead
    $channel = 'approved'
    $branchName = $Branch
}
$short = $target.Substring(0, 7)

# ===================================================================== -Status

$installed = Read-BuildInfo $installedDir
if ($Status) {
    Step 'Status'
    if (Test-Path $installedDir) { Info ("installed: " + (Describe-Build $installed)) } else { Info 'installed: nothing (run UPDATEFTF to install)' }
    if ($installed -and "$($installed.commit)" -eq $approvedHead -and $installed.channel -eq 'approved') {
        $problem = Verify-Installed $approvedHead
        if ($problem) { Warn "installed files do not match their package: $problem (run UPDATEFTF -Force)" } else { Ok 'the installed build is the latest approved build, and every file matches its package' }
    } elseif (Test-Path $installedDir) {
        Warn ("a newer approved build is available: {0}. Run UPDATEFTF." -f $approvedHead.Substring(0, 7))
    }
    if (Test-Path $installedDir) { Show-Startup }
    Report-OtherLoaders -Fix:$FixStartup
    if (Test-Path $installedDll) { Restart-Advice $(if ($installed) { "$($installed.commit)".Substring(0, 7) } else { 'its commit' }) }
    exit 0
}

# ===================================================================== already current?

if (-not $Force -and $installed -and "$($installed.commit)" -eq $target -and $installed.channel -eq $channel -and -not $installed.dirty) {
    $problem = Verify-Installed $target
    if (-not $problem) {
        Step 'Already up to date'
        Ok ("installed: " + (Describe-Build $installed))
        Ok 'every installed file matches its package'
        Show-Startup
        Report-OtherLoaders -Fix:$FixStartup
        Restart-Advice $short
        exit 0
    }
    Warn "the installed build is $short but $problem -- reinstalling it."
}

# ===================================================================== the build checkout

Step "Build checkout at $short"
[void](Run-Git @('-C', $repoRoot, 'worktree', 'prune'))
if (-not (Test-Path $BuildRoot)) {
    $add = Run-Git @('-C', $repoRoot, 'worktree', 'add', '--detach', $BuildRoot, $target)
    if ($add.Code -ne 0) { Fail "could not create the build checkout at $BuildRoot." $add.Lines }
    Set-Content -Path $marker -Value 'This folder is the FTF build checkout managed by UPDATEFTF.ps1. Do not edit files here; work in your own clone.' -Encoding utf8
    Ok "created $BuildRoot"
} else {
    $theirs = (Run-Git @('-C', $BuildRoot, 'rev-parse', '--git-common-dir')).Lines | Select-Object -First 1
    if ($theirs -and -not [IO.Path]::IsPathRooted($theirs)) { $theirs = Join-Path $BuildRoot $theirs }
    if (-not $theirs -or -not (Same-Path $theirs $commonDir)) {
        Fail "$BuildRoot exists but is not a checkout of this repository; refusing to touch it." @('Pass -BuildRoot with another folder, or move that folder away.')
    }
    $dirty = (Run-Git @('-C', $BuildRoot, 'status', '--porcelain', '--untracked-files=no')).Lines | Where-Object { $_ }
    if (-not (Test-Path $marker)) {
        # A checkout made by hand (git worktree add --detach ...): adopt it only when it is clean and
        # on no branch, i.e. when there is nothing in it anybody could lose.
        $onBranch = ((Run-Git @('-C', $BuildRoot, 'symbolic-ref', '-q', 'HEAD')).Code -eq 0)
        if ($onBranch -or $dirty) {
            Fail "$BuildRoot is a checkout of this repository that is on a branch or has changes; it looks like somebody's work, so UPDATEFTF will not use it." @('Pass -BuildRoot with another folder (UPDATEFTF creates it).')
        }
        Set-Content -Path $marker -Value 'This folder is the FTF build checkout managed by UPDATEFTF.ps1. Do not edit files here; work in your own clone.' -Encoding utf8
        Ok "adopted $BuildRoot as the build checkout"
    }
    if ($dirty) {
        Fail "files in the build checkout were edited by hand; UPDATEFTF will not discard them." (@($dirty) + @("Copy what you need from $BuildRoot, then run: git -C `"$BuildRoot`" checkout -- ."))
    }
    $checkout = Run-Git @('-C', $BuildRoot, 'checkout', '--quiet', '--detach', $target)
    if ($checkout.Code -ne 0) { Fail "could not check out $short in the build checkout." $checkout.Lines }
    Ok "checked out $short"
}

# A newer UPDATEFTF arrived with this commit: let it do the rest, so fixes to the update process
# itself take effect on the same run.
$next = Join-Path $BuildRoot 'UPDATEFTF.ps1'
if (-not $Relaunched -and -not $Dev -and (Test-Path $next) -and
    ((Get-FileHash -LiteralPath $next -Algorithm SHA256).Hash -ne $selfHash)) {
    Info 'this commit carries a newer UPDATEFTF.ps1; continuing with it'
    $forward = @()
    foreach ($key in $PSBoundParameters.Keys) {
        $value = $PSBoundParameters[$key]
        if ($value -is [System.Management.Automation.SwitchParameter]) { if ($value.IsPresent) { $forward += "-$key" } }
        else { $forward += "-$key"; $forward += "$value" }
    }
    $forward += '-Relaunched'
    & $hostExe -NoProfile -ExecutionPolicy Bypass -File $next @forward
    exit $LASTEXITCODE
}

# ===================================================================== build and validate (nothing installed changes)

$package = Join-Path $state ("updates\$short-$channel")
$validated = Join-Path $package 'validated.json'
$setup = Join-Path $package 'FieldToFinish-Setup'
$reuse = $false
if ((Test-Path $validated) -and -not $Force) {
    try {
        $v = Get-Content $validated -Raw | ConvertFrom-Json
        $reuse = ($v.commit -eq $target) -and (Test-Path (Join-Path $setup 'Bundle\build-info.json'))
    } catch { $reuse = $false }
}

if ($reuse) {
    Step 'Build and tests'
    Ok ("already built and validated for $short on " + (Utc-Text $v.validatedUtc) + " (" + $v.unitTests + "); reusing it")
} else {
    Step 'Prerequisites'
    foreach ($need in @("$C3D2024\AcDbMgd.dll", "$C3D2024\AcMgd.dll", "$C3D2024\AcCoreMgd.dll", "$C3D2024\C3D\AeccDbMgd.dll")) {
        if (-not (Test-Path $need)) { Fail "Civil 3D 2024 not found: $need" @('Pass -C3D2024 with the install root if it is not in C:\Program Files\Autodesk\AutoCAD 2024.') }
    }
    Ok "Civil 3D 2024 at $C3D2024"
    if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail 'the .NET SDK (dotnet) is not on PATH.' @('Install the .NET 8 SDK.') }
    Ok ('dotnet ' + ((Invoke-Native 'dotnet' @('--version')).Lines | Select-Object -First 1))

    Step "Build $short ($channel, Release)"
    $buildDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    foreach ($stale in @('src\FieldCodes.Cad\bin\Release', 'src\FieldCodes\bin\Release')) {
        $path = Join-Path $BuildRoot $stale
        if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }    # build output of the managed checkout only
    }
    $props = @("-p:FtfCommit=$target", "-p:FtfBranch=$branchName", "-p:FtfChannel=$channel",
               "-p:FtfBuildDate=$buildDate", "-p:C3D2024=$C3D2024")
    New-Item -ItemType Directory -Force -Path $package | Out-Null
    foreach ($project in @('src\FieldCodes.Cad\FieldCodes.Cad.csproj', 'tests\FieldCodes.Tests\FieldCodes.Tests.csproj')) {
        $buildLog = Join-Path $package ('build-' + [IO.Path]::GetFileNameWithoutExtension($project) + '.log')
        $built = Invoke-Native 'dotnet' (@('build', (Join-Path $BuildRoot $project), '-c', 'Release', '--nologo', '-v', 'minimal') + $props) -Capture $buildLog
        if ($built.Code -ne 0) {
            Fail "the build of $project failed." (@($built.Lines | Where-Object { $_ -match ' error ' } | Select-Object -Unique -First 20) + @("build log: $buildLog"))
        }
    }
    $dll = Join-Path $BuildRoot 'src\FieldCodes.Cad\bin\Release\net48\FieldCodes.Cad.dll'
    $product = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
    if ("$product" -notmatch ('\+' + $short + '\.' + $channel + '$')) { Fail "the built DLL is not a clean $channel build of ${short}: its version says '$product'." @() }
    Ok "built FieldCodes.Cad.dll $product"

    Step 'Unit tests (regression suite for every FTF feature)'
    $testLog = Join-Path $package 'tests.log'
    $tests = Invoke-Native 'dotnet' @('test', (Join-Path $BuildRoot 'tests\FieldCodes.Tests\FieldCodes.Tests.csproj'), '-c', 'Release', '--no-build', '--nologo') -Capture $testLog
    $summary = ($tests.Lines | Where-Object { $_ -match '(Passed|Failed)!\s+-' } | Select-Object -Last 1)
    if ($tests.Code -ne 0) {
        Fail 'unit tests failed.' (@($tests.Lines | Where-Object { $_ -match '^\s+Failed ' } | Select-Object -First 20) + @("$summary", "test log: $testLog"))
    }
    Ok "$summary".Trim()

    Step 'Package'
    if (Test-Path $setup) { Remove-Item -LiteralPath $setup -Recurse -Force }
    $made = Invoke-Script (Join-Path $BuildRoot 'deploy\make-release.ps1') @('-Configuration', 'Release', '-NoBuild', '-OutputRoot', $package,
        '-Commit', $target, '-Branch', $branchName, '-Channel', $channel, '-BuildDate', $buildDate)
    if ($made.Code -ne 0 -or -not (Test-Path (Join-Path $setup 'Bundle\build-info.json'))) { Fail 'packaging failed.' $made.Lines }
    $packaged = Read-BuildInfo (Join-Path $setup 'Bundle')
    if ("$($packaged.commit)" -ne $target) { Fail "the package says $($packaged.commit), not $target." @() }
    Ok ("packaged: " + (Describe-Build $packaged) + ", " + @($packaged.files.PSObject.Properties).Count + " files")

    Step 'Installer test (scratch profile; your install is not touched)'
    $installerTest = Invoke-Script (Join-Path $BuildRoot 'deploy\test-release.ps1') @('-Configuration', 'Release', '-NoBuild')
    $installerSummary = ($installerTest.Lines | Where-Object { $_ -match 'release setup: PASS' } | Select-Object -Last 1)
    if ($installerTest.Code -ne 0) { Fail 'the installer test failed.' (@($installerTest.Lines | Where-Object { $_ -match '\[FAIL\]' }) + @("$installerSummary")) }
    Ok "$installerSummary".Trim()

    [ordered]@{ commit = $target; channel = $channel; builtUtc = $buildDate; unitTests = "$summary".Trim()
                installerTest = "$installerSummary".Trim(); validatedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') } |
        ConvertTo-Json | Set-Content -Path $validated -Encoding utf8
}

$packaged = Read-BuildInfo (Join-Path $setup 'Bundle')

# A one-word launcher beside the repository, made once: C:\dev\UPDATEFTF.cmd runs the managed copy.
$launcher = Join-Path (Split-Path $repoRoot -Parent) 'UPDATEFTF.cmd'
if (-not (Test-Path $launcher)) {
    $text = "@echo off`r`nrem Runs the FTF update from the build checkout UPDATEFTF.ps1 manages.`r`n" +
            "powershell -NoProfile -ExecutionPolicy Bypass -File `"$BuildRoot\UPDATEFTF.ps1`" %*`r`n"
    try { [IO.File]::WriteAllText($launcher, $text, [Text.Encoding]::ASCII); Info "made $launcher (run it to update FTF from now on)" } catch { }
}

# ===================================================================== Civil 3D must be closed

$open = Civil3D-Processes
if ($open.Count -gt 0) {
    if (-not $WaitForCivil3D) {
        Write-Host ''
        Write-Host ("VALIDATED, NOT YET INSTALLED: Civil 3D is open (PID {0})." -f (($open | ForEach-Object { $_.Id }) -join ', ')) -ForegroundColor Yellow
        Write-Host "   Build $short passed its build, unit and installer tests. Civil 3D holds FTF open while it runs,"
        Write-Host '   so it cannot be replaced now. Close Civil 3D, then run UPDATEFTF again: it installs this'
        Write-Host '   validated build and smoke-tests it in about a minute, without rebuilding.'
        Write-Host '   (Or run UPDATEFTF -WaitForCivil3D to wait for Civil 3D to close.)'
        Write-Host '   Your installed FTF was NOT changed.'
        exit 2
    }
    Write-Host ''
    Write-Host '   Waiting for Civil 3D to close (save your work and close it; Ctrl+C to stop waiting)...' -ForegroundColor Yellow
    while ((Civil3D-Processes).Count -gt 0) { Start-Sleep -Seconds 3 }
    Ok 'Civil 3D closed'
}
$consoles = @(Get-Process -Name accoreconsole -ErrorAction SilentlyContinue)
if ($consoles.Count -gt 0) { Fail ("accoreconsole is running (PID {0}) and may hold FTF open. Let it finish, then run UPDATEFTF again." -f (($consoles | ForEach-Object { $_.Id }) -join ', ')) @() }

# ===================================================================== back up, install, verify

Step 'Back up the installed FTF'
if (Test-Path $installedDir) {
    $label = if ($installed -and $installed.commit) { "$($installed.commit)".Substring(0, 7) } else { 'unversioned' }
    $script:backup = Join-Path $state ('backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-$label")
    New-Item -ItemType Directory -Force -Path $script:backup | Out-Null
    Copy-Item -LiteralPath $installedDir -Destination $script:backup -Recurse -Force
    $n = 0
    foreach ($entry in @(Get-StartupEntries | Where-Object { -not $_.Machine -and $_.Name -eq 'FieldToFinish' })) {
        $n++
        [void](Invoke-Native 'reg.exe' @('export', $entry.RegName, (Join-Path $script:backup "startup-$n.reg"), '/y'))
    }
    Ok ("backed up " + (Describe-Build $installed) + " to $script:backup")
} else {
    Info 'nothing installed yet; nothing to back up'
}

function Restore-Backup([string]$why) {
    Write-Host ''
    Write-Host "   Putting the previous FTF back ($why)..." -ForegroundColor Yellow
    $failedDir = Join-Path $state ("failed\" + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-$short")
    if (Test-Path $installedDir) {
        New-Item -ItemType Directory -Force -Path $failedDir | Out-Null
        Copy-Item -LiteralPath $installedDir -Destination $failedDir -Recurse -Force
        Remove-Item -LiteralPath $installedDir -Recurse -Force
    }
    if ($script:backup) {
        Copy-Item -LiteralPath (Join-Path $script:backup 'FieldToFinish.bundle') -Destination $pluginsRoot -Recurse -Force
        foreach ($reg in @(Get-ChildItem $script:backup -Filter 'startup-*.reg' -ErrorAction SilentlyContinue)) {
            [void](Invoke-Native 'reg.exe' @('import', $reg.FullName))
        }
        Write-Host ("   Restored: " + (Describe-Build (Read-BuildInfo $installedDir))) -ForegroundColor Yellow
    } else {
        foreach ($entry in @(Get-StartupEntries | Where-Object { -not $_.Machine -and $_.Name -eq 'FieldToFinish' })) {
            Remove-Item -Path $entry.Key -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host '   There was no FTF installed before; the failed build was removed again.' -ForegroundColor Yellow
    }
    Write-Host "   The failed build is kept for diagnosis in $failedDir" -ForegroundColor Yellow
}

Step "Install $short"
$script:touched = $true
$install = Invoke-Script (Join-Path $setup 'install.ps1') @('-Source', $setup, '-NoOfficeCopy') -Echo
if ($install.Code -ne 0) {
    # install.ps1 swaps folders only once the new copy is whole, so a failed install has normally
    # left the previous one in place; make sure of it.
    if ($script:backup -and -not (Read-BuildInfo $installedDir) -and -not (Test-Path $installedDll)) { Restore-Backup 'the install did not finish' }
    $script:touched = $false
    Fail 'the installer failed.' $install.Lines
}
$problem = Verify-Installed $target
if ($problem) { Restore-Backup $problem; Fail "the installed files are not the package: $problem" @() }
Ok 'every installed file matches the package (SHA-256)'
Show-Startup

# ===================================================================== prove it in Civil 3D

if ($SkipSmoke) {
    Step 'Smoke test in Civil 3D'
    Warn 'SKIPPED (-SkipSmoke): this install has not been proven inside Civil 3D.'
} else {
    Step 'Smoke test in Civil 3D (headless): the loaded build, FTFVERSION, FTFCONTOUR'
    $smokeDir = Join-Path $state ('smoke\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-$short")
    $smoke = Invoke-Script (Join-Path $BuildRoot 'deploy\smoke-test.ps1') @('-PluginDll', $installedDll, '-Commit', $target,
        '-C3D2024', $C3D2024, '-WorkFolder', $smokeDir) -Echo
    if ($smoke.Code -ne 0) {
        Restore-Backup 'the smoke test failed'
        Fail "build $short did not pass its smoke test in Civil 3D, so it was not kept." @("smoke test files: $smokeDir")
    }
    Ok 'smoke test passed'
}

# ===================================================================== publish (optional)

if ($PublishTo) {
    Step "Publish to the office copy: $PublishTo"
    if ($channel -ne 'approved') {
        Warn 'not published: only approved builds go to the office copy.'
    } else {
        $published = Invoke-Script (Join-Path $BuildRoot 'deploy\make-release.ps1') @('-Configuration', 'Release', '-NoBuild',
            '-OutputRoot', (Join-Path $package 'publish'), '-Commit', $target, '-Branch', $branchName, '-Channel', 'approved',
            '-BuildDate', (Utc-Text $packaged.builtUtc), '-OfficeFolder', $PublishTo)
        $source = Join-Path $package 'publish\FieldToFinish-Setup'
        if ($published.Code -ne 0 -or -not (Test-Path $source)) {
            Warn 'publishing failed; the local install is unaffected.'
        } else {
            $parent = Split-Path $PublishTo -Parent
            $leaf = Split-Path $PublishTo -Leaf
            $incoming = Join-Path $parent ("$leaf.incoming")
            if (Test-Path $incoming) { Remove-Item -LiteralPath $incoming -Recurse -Force }
            Copy-Item -LiteralPath $source -Destination $incoming -Recurse -Force
            if (Test-Path $PublishTo) { Rename-Item -LiteralPath $PublishTo -NewName ("$leaf.previous-" + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
            Rename-Item -LiteralPath $incoming -NewName $leaf
            Ok "published $short; surveyors installed from there update when they next close Civil 3D"
        }
    }
}

# ===================================================================== report

Step 'Done'
$final = Read-BuildInfo $installedDir
Ok ("installed: " + (Describe-Build $final))
Info "folder:    $installedDir"
if ($script:backup) { Info ("previous:  " + (Describe-Build $installed) + " (UPDATEFTF -Rollback puts it back)") }
Report-OtherLoaders -Fix:$FixStartup
Write-Host ''
Write-Host ('   Civil 3D will load this build automatically when it starts. NEXT: start Civil 3D and type ' +
            "FTFVERSION -- it should say $short.") -ForegroundColor Green
Write-Host "   log: $logFile"
exit 0
