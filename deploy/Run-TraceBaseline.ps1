<#
.SYNOPSIS
    One-run Windows baseline for TRACE (neutral commercial edition) on Civil 3D 2024.

.DESCRIPTION
    1. Checks prerequisites (Windows, .NET SDK, Civil 3D 2024, Civil 3D not running).
    2. Builds the TRACE edition.
    3. Runs the non-CAD unit tests once.
    4. Installs the development bundle for the current user.
    5. Saves every log, a summary and a zip of them in ONE folder under <repo>\logs.

    It does NOT start Civil 3D and does NOT open or change any drawing. After it
    finishes, follow docs\trace\MANUAL-VERIFICATION.md by hand.

    Works from wherever the repository is (any drive): everything is found relative
    to this script.

    UNTESTED on Windows at the time of writing. If a step fails, the log folder says
    which one and why.

.PARAMETER Civil3DPath
    Civil 3D 2024 install folder, only needed if it is not in the usual place.

.PARAMETER SkipTests
    Skip the unit tests (build and install only).

.PARAMETER RunSmoke
    Also run the existing headless drafting smoke test (accoreconsole: no Civil 3D
    window, no drawings of yours; it works on a drawing made from the stock template).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\deploy\Run-TraceBaseline.ps1
#>
[CmdletBinding()]
param(
    [string]$Civil3DPath,
    [switch]$SkipTests,
    [switch]$RunSmoke
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TraceEnv.ps1')

$repo   = Get-TraceRepoRoot
$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir = Join-Path $repo ("logs\trace-baseline-" + $stamp)
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

$results = New-Object System.Collections.ArrayList
$notes   = New-Object System.Collections.ArrayList

function Add-Result {
    param([string]$Step, [bool]$Ok, [string]$Detail)
    $status = if ($Ok) { 'PASS' } else { 'FAIL' }
    [void]$results.Add([pscustomobject]@{ Step = $Step; Status = $status; Detail = $Detail })
    $colour = if ($Ok) { 'Green' } else { 'Red' }
    Write-Host ("[{0}] {1}  {2}" -f $status, $Step, $Detail) -ForegroundColor $colour
}

function Add-Note { param([string]$Text) [void]$notes.Add($Text); Write-Host ("  note: " + $Text) -ForegroundColor Yellow }

function Invoke-Logged {
    # Runs a program, copies all its output to a log file, returns its exit code.
    param([string]$Exe, [string[]]$Arguments, [string]$LogFile)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Arguments 2>&1 | ForEach-Object { $_.ToString() } | Out-File -FilePath $LogFile -Encoding utf8
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Write-Summary {
    param([bool]$Finished)
    $failed = @($results | Where-Object { $_.Status -eq 'FAIL' })
    $lines = @()
    $lines += 'TRACE baseline summary'
    $lines += ('Date:        ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    $lines += ('Repository:  ' + $repo)
    $lines += ('Edition:     Default (TRACE, neutral configuration)')
    $lines += ('Finished:    ' + $Finished)
    $lines += ''
    foreach ($r in $results) { $lines += ('[{0}] {1}  {2}' -f $r.Status, $r.Step, $r.Detail) }
    if ($notes.Count -gt 0) { $lines += ''; $lines += 'Notes:'; foreach ($n in $notes) { $lines += ('  - ' + $n) } }
    $lines += ''
    $lines += $(if ($failed.Count -eq 0 -and $Finished) { 'RESULT: all automated steps passed. Now do the manual Civil 3D checks.' } else { 'RESULT: not clean. Send the log folder.' })
    $lines | Out-File -FilePath (Join-Path $logDir 'summary.txt') -Encoding utf8
    try {
        $zip = $logDir + '.zip'
        if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
        Compress-Archive -Path (Join-Path $logDir '*') -DestinationPath $zip -Force
        Write-Host ''
        Write-Host ('Logs:     ' + $logDir)
        Write-Host ('Send me:  ' + $zip) -ForegroundColor Cyan
    } catch {
        Write-Host ('Logs: ' + $logDir + ' (could not zip: ' + $_.Exception.Message + ')')
    }
}

function Stop-Run {
    param([string]$Why)
    Write-Host ''
    Write-Host ('Stopped: ' + $Why) -ForegroundColor Red
    Write-Summary -Finished $false
    exit 1
}

Write-Host ''
Write-Host 'TRACE baseline (Civil 3D 2024)' -ForegroundColor Cyan
Write-Host ('Repository: ' + $repo)
Write-Host ('Logs:       ' + $logDir)
Write-Host ''

# ----------------------------------------------------------------- 1. prerequisites
$onWindows = ($env:OS -eq 'Windows_NT')
Add-Result 'Windows' $onWindows $(if ($onWindows) { [Environment]::OSVersion.VersionString } else { 'This script is for Windows.' })
if (-not $onWindows) { Stop-Run 'Not Windows.' }

$psOk = ($PSVersionTable.PSVersion.Major -ge 5)
Add-Result 'PowerShell 5.1 or newer' $psOk ('version ' + $PSVersionTable.PSVersion)

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $dotnetVersion = (& dotnet --version 2>&1 | Select-Object -First 1).ToString().Trim()
    $major = 0
    [void][int]::TryParse(($dotnetVersion -split '\.')[0], [ref]$major)
    Add-Result '.NET SDK 6 or newer' ($major -ge 6) ('dotnet ' + $dotnetVersion)
} else {
    Add-Result '.NET SDK 6 or newer' $false 'dotnet not found. Install the .NET 8 SDK from https://dotnet.microsoft.com/download'
}

$c3d = Find-Civil3D2024 -Override $Civil3DPath
if ($c3d) {
    $needed = @('AcDbMgd.dll', 'AcCoreMgd.dll', 'AcMgd.dll', 'AdWindows.dll', 'AecBaseMgd.dll', 'C3D\AeccDbMgd.dll')
    $missing = @($needed | Where-Object { -not (Test-Path -LiteralPath (Join-Path $c3d $_)) })
    $acadExe = Join-Path $c3d 'acad.exe'
    $ver = if (Test-Path -LiteralPath $acadExe) { (Get-Item -LiteralPath $acadExe).VersionInfo.ProductVersion } else { 'acad.exe not found' }
    Add-Result 'Civil 3D 2024 files' ($missing.Count -eq 0) ($c3d + '  (acad.exe ' + $ver + ')' + $(if ($missing.Count -gt 0) { '  missing: ' + ($missing -join ', ') } else { '' }))
} else {
    Add-Result 'Civil 3D 2024 files' $false 'Not found. Re-run with -Civil3DPath "<install folder>" (the folder that contains acad.exe).'
}

$running = @(Get-Process -Name acad -ErrorAction SilentlyContinue)
Add-Result 'Civil 3D is closed' ($running.Count -eq 0) $(if ($running.Count -eq 0) { 'ok' } else { 'Close Civil 3D (it locks the plugin file), then run this again.' })

$repoFiles = @('FieldToFinish.sln', 'src\FieldCodes.Cad\FieldCodes.Cad.csproj', 'config\default\rules.json', 'deploy\install-dev.ps1')
$repoMissing = @($repoFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $repo $_)) })
Add-Result 'Repository files' ($repoMissing.Count -eq 0) $(if ($repoMissing.Count -eq 0) { 'ok' } else { 'missing: ' + ($repoMissing -join ', ') })

if ($repo.Contains(' ')) { Add-Note 'The repository path contains a space. The main steps cope; the optional -RunSmoke test may not.' }

$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) {
    $head = (& git -C $repo rev-parse --short HEAD 2>&1 | Select-Object -First 1).ToString()
    $branch = (& git -C $repo rev-parse --abbrev-ref HEAD 2>&1 | Select-Object -First 1).ToString()
    $dirty = (& git -C $repo status --porcelain 2>&1 | Measure-Object).Count
    Add-Note ("git: branch $branch, commit $head, $dirty changed file(s)")
}

if (@($results | Where-Object { $_.Status -eq 'FAIL' }).Count -gt 0) { Stop-Run 'A prerequisite is missing (see above).' }

# ---------------------------------------------------------------------- 2. build
Write-Host ''
Write-Host 'Building TRACE (this can take a minute the first time)...'
$bin = Join-Path $repo 'src\FieldCodes.Cad\bin\Debug\net48'
$buildLog = Join-Path $logDir 'build.log'
$code = Invoke-Logged 'dotnet' @('build', (Join-Path $repo 'src\FieldCodes.Cad\FieldCodes.Cad.csproj'), '-c', 'Debug', '-p:TraceEdition=Default', ('-p:C3D2024=' + $c3d), '-nologo') $buildLog
$dll = Join-Path $bin 'FieldCodes.Cad.dll'
$built = ($code -eq 0) -and (Test-Path -LiteralPath $dll)
$buildWarn = @(Select-String -Path $buildLog -Pattern ' warning ' -ErrorAction SilentlyContinue).Count
Add-Result 'Build TRACE edition' $built $(if ($built) { "ok ($buildWarn warning line(s))" } else { "failed, exit code $code. See build.log" })
if (-not $built) {
    Get-Content $buildLog -Tail 15 | ForEach-Object { Write-Host ('   ' + $_) }
    Stop-Run 'Build failed.'
}

$builtRules = Join-Path $bin 'rules.json'
$wanted = (Get-FileHash -LiteralPath (Join-Path $repo 'config\default\rules.json')).Hash
$gotRules = if (Test-Path -LiteralPath $builtRules) { (Get-FileHash -LiteralPath $builtRules).Hash } else { '' }
Add-Result 'Build shipped the neutral TRACE rules' ($wanted -eq $gotRules) $(if ($wanted -eq $gotRules) { 'rules.json matches config\default' } else { 'rules.json in the build is NOT the neutral default' })

# ----------------------------------------------------------------------- 3. tests
if ($SkipTests) {
    Add-Note 'Unit tests skipped (-SkipTests).'
} else {
    Write-Host ''
    Write-Host 'Running the non-CAD unit tests once...'
    $testLog = Join-Path $logDir 'unit-tests.log'
    $code = Invoke-Logged 'dotnet' @('test', (Join-Path $repo 'tests\FieldCodes.Tests\FieldCodes.Tests.csproj'), '--nologo', '--results-directory', (Join-Path $logDir 'test-results'), '--logger', 'trx;LogFileName=unit-tests.trx') $testLog
    $summaryLine = (Select-String -Path $testLog -Pattern '^(Passed|Failed)!' -ErrorAction SilentlyContinue | Select-Object -Last 1)
    $text = if ($summaryLine) { $summaryLine.Line.Trim() } else { 'no summary line, see unit-tests.log' }
    Add-Result 'Unit tests (non-CAD)' ($code -eq 0) $text
}

# --------------------------------------------------------------------- 4. install
Write-Host ''
Write-Host 'Installing the development bundle for the current user...'
$installLog = Join-Path $logDir 'install.log'
$code = Invoke-Logged 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $repo 'deploy\install-dev.ps1'), '-Edition', 'Default') $installLog
$bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle'
$installedDll = Join-Path $bundle 'Contents\2024\FieldCodes.Cad.dll'
$installedRules = Join-Path $bundle 'Contents\2024\rules.json'
$manifest = Join-Path $bundle 'PackageContents.xml'
$installOk = ($code -eq 0) -and (Test-Path -LiteralPath $installedDll) -and (Test-Path -LiteralPath $manifest)
Add-Result 'Install bundle' $installOk $(if ($installOk) { $bundle } else { "failed, exit code $code. See install.log" })
if ($installOk) {
    $rulesOk = (Test-Path -LiteralPath $installedRules) -and ((Get-FileHash -LiteralPath $installedRules).Hash -eq $wanted)
    Add-Result 'Installed rules are the neutral TRACE rules' $rulesOk $(if ($rulesOk) { 'ok' } else { 'installed rules.json differs from config\default' })
    $autodeskCopies = @(Get-ChildItem -LiteralPath (Join-Path $bundle 'Contents\2024') -File | Where-Object { $_.Name -match '^(Ac|Aecc)' })
    Add-Result 'No Autodesk DLLs copied into the bundle' ($autodeskCopies.Count -eq 0) $(if ($autodeskCopies.Count -eq 0) { 'ok' } else { ($autodeskCopies | ForEach-Object { $_.Name }) -join ', ' })
}

# ------------------------------------------------------------ 5. optional smoke
if ($RunSmoke) {
    Write-Host ''
    Write-Host 'Running the headless drafting smoke test (no Civil 3D window)...'
    $smokeLog = Join-Path $logDir 'smoke-run.log'
    $code = Invoke-Logged 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $repo 'tests\LiveSmoke\run-livesmoke.ps1')) $smokeLog
    $draftLog = Join-Path $repo 'tests\LiveSmoke\draft_out.log'
    if (Test-Path -LiteralPath $draftLog) {
        Copy-Item -LiteralPath $draftLog -Destination (Join-Path $logDir 'draft_out.log') -Force
        $pass = @(Select-String -Path $draftLog -Pattern '\[PASS\]').Count
        $fail = @(Select-String -Path $draftLog -Pattern '\[FAIL\]').Count
        Add-Result 'Drafting smoke test' (($fail -eq 0) -and ($pass -gt 0)) ("PASS lines: $pass, FAIL lines: $fail")
    } else {
        Add-Result 'Drafting smoke test' $false 'no draft_out.log produced. See smoke-run.log'
    }
} else {
    Add-Note 'Headless smoke test not run (add -RunSmoke to include it).'
}

Write-Host ''
Write-Summary -Finished $true
Write-Host ''
Write-Host 'Next: start Civil 3D yourself and follow docs\trace\MANUAL-VERIFICATION.md' -ForegroundColor Cyan
if (@($results | Where-Object { $_.Status -eq 'FAIL' }).Count -gt 0) { exit 1 }
