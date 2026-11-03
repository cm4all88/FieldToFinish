<#
.SYNOPSIS
    Proves an FTF build works inside Civil 3D 2024 (headless, accoreconsole) before it is kept.

.DESCRIPTION
    UPDATEFTF.ps1 runs this against the build it has just put in place; if it fails, UPDATEFTF puts
    the previous FTF back. It can also be run by hand against any FieldCodes.Cad.dll.

      1. compiles the smoke harness (tests\LiveSmoke\ContourTestHarness.cs) with the .NET Framework csc
      2. seeds a fresh drawing from the stock Civil 3D template, with a factory-default
         ftf-settings.json beside it so nobody's own settings change what is checked
      3. runs accoreconsole on a generated script that
           - loads the build under test and proves it is the one loaded: exactly one FieldCodes.Cad,
             from -PluginDll, stamped with -Commit, declaring the expected commands
           - runs FTFVERSION and proves it ran
           - builds a test TIN surface and runs FTFCONTOUR four ways (multi-elevation, undo, re-run,
             boundary + labels), proving each run happened before checking what it drew
      4. watches the output while it runs: an "Unknown command" stops the run at once and fails it,
         so a missing command can never leave later checks passing on nothing
      5. runs the identity check on a second fresh drawing (tests\LiveSmoke\IdentityTestHarness.cs):
         source points read as Civil 3D stores them, FTFLINES naming lines from their shots and
         FTFREVIEWCHECK reading linework coding, with nothing in the drawing changed. Required.
      6. runs the accountability check on a third (tests\LiveSmoke\AccountabilityTestHarness.cs):
         point accountability and linework continuity after FTFRUN, approved corrections, decisions
         that reopen when the source changes, and the Drafting QC Complete gate. Required.
      7. runs the annotation style check (tests\LiveSmoke\StyleTestHarness.cs). Reported, not required.

    Exit code 0 only when every required stage passed and each run reached its end.

.PARAMETER PluginDll
    The FieldCodes.Cad.dll under test. When a bundle is installed, Civil 3D (and accoreconsole) loads
    the installed one at startup, so that is the only DLL that can be tested headless: anything else
    fails with a clear message instead of testing the wrong build.

.PARAMETER Commit
    The full commit the DLL must be stamped with.

.EXAMPLE
    .\smoke-test.ps1 -PluginDll "$env:APPDATA\Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024\FieldCodes.Cad.dll" -Commit 5c3c0218830d94574671bd690a5df4ce559afdd6
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [Parameter(Mandatory = $true)][string]$Commit,
    [string]$C3D2024 = 'C:\Program Files\Autodesk\AutoCAD 2024',
    [string]$Template,
    [string]$WorkFolder,
    [int]$TimeoutSeconds = 420
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$C3D2024 = $C3D2024.TrimEnd('\')
$accore = Join-Path $C3D2024 'accoreconsole.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harnessSource = Join-Path $repo 'tests\LiveSmoke\ContourTestHarness.cs'
if (-not $Template) { $Template = Join-Path $env:LOCALAPPDATA 'Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt' }
if (-not $WorkFolder) {
    $WorkFolder = Join-Path (Join-Path $env:LOCALAPPDATA 'FieldToFinish\smoke') (Get-Date -Format 'yyyyMMdd-HHmmss')
}

function Say([string]$text) { Write-Host "   $text" }

function Fail([string]$why, [string[]]$details) {
    Write-Host ''
    Write-Host "   SMOKE TEST FAILED: $why" -ForegroundColor Red
    foreach ($line in $details) { if ($line) { Write-Host "     $line" -ForegroundColor Red } }
    Write-Host "     work folder: $WorkFolder" -ForegroundColor Red
    exit 1
}

# accoreconsole writes UTF-16 to a redirected stdout; read it while it is still being written.
function Read-Console([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    try {
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $bytes = New-Object byte[] $stream.Length
            [void]$stream.Read($bytes, 0, $bytes.Length)
        } finally { $stream.Dispose() }
    } catch { return '' }
    $kept = New-Object System.Collections.Generic.List[byte]
    foreach ($b in $bytes) { if ($b -ne 0) { $kept.Add($b) } }
    return [Text.Encoding]::ASCII.GetString($kept.ToArray())
}

# How an accoreconsole run ended: exit code (and what it means), killed or not, how long it ran. A run
# that ends without its COMPLETE line is otherwise silent: an exception nothing caught ends
# accoreconsole without printing it.
function Get-ExitReport($process, [bool]$killed, [string]$why, [datetime]$started) {
    $seconds = [int]((Get-Date) - $started).TotalSeconds
    if ($killed) { return @("accoreconsole: stopped by the smoke test after $seconds s ($why)") }
    $code = $null
    try { $process.WaitForExit(); $code = $process.ExitCode } catch { }
    if ($null -eq $code) { return @("accoreconsole: ended after $seconds s; exit code not available") }
    $hex = '0x{0:X8}' -f [int]$code
    $meaning = switch ([int64]$code) {
        0 { 'exited normally' }
        3 { 'the harness ended the run (FATAL)' }
        -532462766 { 'a .NET exception nothing caught' }
        -1073741819 { 'access violation in native code' }
        -1073740791 { 'fail-fast (stack buffer overrun or Environment.FailFast)' }
        -1073741571 { 'stack overflow' }
        default { 'unexpected exit code' }
    }
    return @("accoreconsole: exit code $code ($hex, $meaning) after $seconds s")
}

# The harness's STEP lines, the plugin's TRACE lines and any EXCEPTION, from a result file: where a
# run that stopped early got to.
function Get-RunDiagnostics([string[]]$resultLines) {
    $out = New-Object System.Collections.Generic.List[string]
    $exception = @($resultLines | Where-Object { $_ -like 'EXCEPTION *' } | Select-Object -First 40)
    if ($exception.Count -gt 0) { $out.Add('--- exception ---'); foreach ($l in $exception) { $out.Add($l.Substring(10)) } }
    $steps = @($resultLines | Where-Object { $_ -like 'STEP *' -or $_ -like 'TRACE *' } | Select-Object -Last 20)
    if ($steps.Count -gt 0) { $out.Add('--- last steps recorded (harness STEP, plugin TRACE) ---'); foreach ($l in $steps) { $out.Add($l) } }
    return @($out)
}

# --- prerequisites ------------------------------------------------------------------

$PluginDll = [IO.Path]::GetFullPath($PluginDll)
$Commit = $Commit.Trim().ToLowerInvariant()
if ($Commit -notmatch '^[0-9a-f]{40}$') { Fail "not a full commit id: $Commit" @() }
foreach ($need in @($PluginDll, $accore, $csc, $harnessSource, $Template)) {
    if (-not (Test-Path -LiteralPath $need)) { Fail "missing: $need" @('Civil 3D 2024, its template (start Civil 3D once) and .NET Framework 4.8 are required.') }
}
foreach ($path in @($PluginDll, $WorkFolder)) {
    if ($path -match '[^\x20-\x7E]') { Fail "path has non-ASCII characters (accoreconsole scripts are ASCII): $path" @() }
}
if (Get-Process -Name accoreconsole -ErrorAction SilentlyContinue) {
    Say 'note: another accoreconsole is running; this run is separate from it.'
}

New-Item -ItemType Directory -Force -Path $WorkFolder | Out-Null
$result = Join-Path $WorkFolder 'result.txt'
$raw = Join-Path $WorkFolder 'accore.raw.txt'
$log = Join-Path $WorkFolder 'smoke.log'
Remove-Item $result, $raw, $log -Force -ErrorAction SilentlyContinue

# --- 1. the harness -----------------------------------------------------------------

$harness = Join-Path $WorkFolder 'FtfSmokeHarness.dll'
$compile = & $csc /nologo /target:library "/out:$harness" $harnessSource `
    "/r:$C3D2024\accoremgd.dll" "/r:$C3D2024\acdbmgd.dll" "/r:$C3D2024\acmgd.dll" `
    "/r:$C3D2024\C3D\AeccDbMgd.dll" "/r:$C3D2024\AecBaseMgd.dll" /r:System.Core.dll 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $harness)) {
    Fail 'the smoke harness did not compile' @($compile | Where-Object { "$_" -match 'error' } | Select-Object -First 15 | ForEach-Object { "$_" })
}
Say 'harness compiled'

# --- 2. a fresh drawing -------------------------------------------------------------

$drawing = Join-Path $WorkFolder 'smoke.dwg'
Copy-Item -LiteralPath $Template -Destination $drawing -Force
# Factory defaults beside the drawing: they override the user's and the office's settings, so the
# checks (layer V-TOPO-CONT-E, labels off by default, masks on) mean the same on every machine.
[IO.File]::WriteAllText((Join-Path $WorkFolder 'ftf-settings.json'), '{ "version": "1" }', [Text.Encoding]::ASCII)

# --- 3. the script ------------------------------------------------------------------

$script = Join-Path $WorkFolder 'smoke.scr'
$lines = @(
    "NETLOAD `"$PluginDll`"",
    "NETLOAD `"$harness`"",
    'FTFSMOKEBEGIN',
    'FTFVERSION',
    'FTFSMOKEEXPECT', 'FTFVERSION',
    'CONTOURSEED',
    'ZOOM', 'E',
    # one U after a multi-elevation run removes all of it
    'FTFCONTOUR', 'N', 'FTF-SMOKE', '12.6, 13.0, 13.5',
    'U',
    'CONTOURVERIFY', 'UNDONE',
    # several elevations at once, two of them off the surface
    'FTFCONTOUR', 'N', 'FTF-SMOKE', '12.6, 13.0, 13.5, 99, 5',
    'CONTOURVERIFY', 'FIRST',
    # the same elevation again (typed differently): replaced, not duplicated
    'FTFCONTOUR', '', '12.60',
    'CONTOURVERIFY', 'AGAIN',
    # inside a boundary, with labels
    'FTFCONTOUR', '', 'B', '50,10', 'L', '13.0',
    'CONTOURVERIFY', 'BOUNDARY',
    'FTFSMOKEEND',
    'SAVEAS', '2018', ('"' + (Join-Path $WorkFolder 'smoke-out.dwg') + '"')
)
[IO.File]::WriteAllLines($script, [string[]]$lines, [Text.Encoding]::ASCII)

# --- 4. run, watching ---------------------------------------------------------------

$env:FTF_SMOKE_EXPECT_DLL = $PluginDll
$env:FTF_SMOKE_EXPECT_COMMIT = $Commit
$env:FTF_SMOKE_RESULT = $result
$env:FTF_TRACE_FILE = $result
$arguments = '/i "' + $drawing + '" /s "' + $script + '" /product C3D /language en-US'
Say "running accoreconsole (up to $TimeoutSeconds s)..."
$runStarted = Get-Date
$process = Start-Process -FilePath $accore -ArgumentList $arguments -PassThru -WindowStyle Hidden -RedirectStandardOutput $raw
$null = $process.Handle   # kept, so ExitCode is readable once the process has ended
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$stopped = $null
while (-not $process.HasExited) {
    Start-Sleep -Milliseconds 500
    $text = Read-Console $raw
    $unknown = [regex]::Match($text, 'Unknown command\s*"?([A-Za-z0-9_\-]+)')
    if ($unknown.Success) { $stopped = 'Unknown command ' + $unknown.Groups[1].Value + ' -- the command is not registered in the loaded build.'; break }
    if ($text -match '\[FATAL\]') { Start-Sleep -Milliseconds 500; break }
    if ((Get-Date) -gt $deadline) { $stopped = "no result after $TimeoutSeconds s: a prompt is waiting for input the script did not give."; break }
}
$killed = $false
if (-not $process.HasExited) {
    # The whole tree, at once: nothing from a failed run may linger holding the plugin open.
    $killed = $true
    if ($env:OS -eq 'Windows_NT') { & taskkill.exe /PID $process.Id /T /F 2>&1 | Out-Null }
    else { try { $process.Kill() } catch { } }
    Start-Sleep -Milliseconds 500
}
$exitReport = Get-ExitReport $process $killed $(if ($stopped) { $stopped } else { 'FATAL in the run' }) $runStarted
Remove-Item Env:\FTF_SMOKE_EXPECT_DLL, Env:\FTF_SMOKE_EXPECT_COMMIT, Env:\FTF_SMOKE_RESULT, Env:\FTF_TRACE_FILE -ErrorAction SilentlyContinue

$text = Read-Console $raw
$text | Set-Content -Path $log -Encoding utf8
$logLines = $text -split "`r?`n"
$outcome = if (Test-Path -LiteralPath $result) { @(Get-Content -LiteralPath $result) } else { @() }
$failLines = @($logLines | Where-Object { $_ -match '\[(FAIL|FATAL)\]' } | ForEach-Object { $_.Trim() })
$loadProblems = @($logLines | Where-Object { $_ -match 'Cannot load assembly|FileLoadException|FileNotFoundException|TypeLoadException|MissingMethodException|BadImageFormatException|TypeInitializationException' } | ForEach-Object { $_.Trim() } | Select-Object -Unique)

# --- 5. judge -----------------------------------------------------------------------

$tail = @($logLines | Where-Object { $_.Trim() } | Select-Object -Last 25)
$diagnostics = @($exitReport) + @(Get-RunDiagnostics $outcome)
if ($stopped) { Fail $stopped (@($failLines) + $diagnostics + @('--- last lines of the run ---') + $tail) }

$fatal = @($outcome | Where-Object { $_ -like 'FATAL*' })
if ($fatal.Count -gt 0) { Fail ($fatal[0].Substring(6)) (@($failLines) + $diagnostics + @('--- last lines of the run ---') + $tail) }
if ($outcome.Count -eq 0) { Fail 'the harness never ran (NETLOAD of the plugin or the harness failed?)' (@($loadProblems) + $diagnostics + $tail) }
if ($loadProblems.Count -gt 0) { Fail 'something failed to load in Civil 3D' $loadProblems }

$required = @('UNDONE', 'FIRST', 'AGAIN', 'BOUNDARY')
$missing = @($required | Where-Object { -not ($outcome -contains "STAGE $_ PASS") })
if (-not ($outcome -contains 'COMPLETE')) { Fail 'the run stopped before its end' (@($failLines) + $diagnostics + $tail) }
if ($missing.Count -gt 0 -or -not ($outcome | Where-Object { $_ -like 'RESULT PASS*' })) {
    Fail ('stages not passed: ' + ($missing -join ', ')) $failLines
}

$loaded = @($outcome | Where-Object { $_ -like 'LOADED *' }) | Select-Object -First 1
$commands = @($outcome | Where-Object { $_ -like 'COMMANDS *' }) | Select-Object -First 1
$passes = @($logLines | Where-Object { $_ -match '\[PASS\]' }).Count
Say ("PASS: {0} checks; build {1}; {2}; stages {3}" -f $passes, $Commit.Substring(0, 7), $commands, ($required -join ', '))
Say "log: $log"

# --- 6. required checks on fresh drawings --------------------------------------------
# Each one: a harness compiled from tests\LiveSmoke, a fresh drawing from the stock template with
# factory settings and the build's own rules.json beside it (an office rules.json in %APPDATA% must
# not change what is expected), a generated script, a run watched for "Unknown command", FATAL and
# a stalled prompt, and every stage proven by the command trace. A failure calls Fail, so UPDATEFTF
# puts the previous build back.
function Invoke-RequiredCheck([string]$label, [string]$harnessName, [string]$envName,
                              [string[]]$body, [string[]]$stages, [string]$passText) {
    $prefix = "$label check: "
    $folder = Join-Path $WorkFolder $harnessName
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $source = Join-Path $repo ("tests\LiveSmoke\" + $harnessName + ".cs")
    if (-not (Test-Path -LiteralPath $source)) { Fail "missing: $source" @() }
    $harnessDll = Join-Path $folder ($harnessName + '.dll')
    $out = & $csc /nologo /target:library "/out:$harnessDll" $source `
        "/r:$C3D2024\accoremgd.dll" "/r:$C3D2024\acdbmgd.dll" "/r:$C3D2024\acmgd.dll" `
        "/r:$C3D2024\C3D\AeccDbMgd.dll" "/r:$C3D2024\AecBaseMgd.dll" /r:System.Core.dll 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $harnessDll)) {
        Fail ($prefix + 'the harness did not compile') @($out | Where-Object { "$_" -match 'error' } | Select-Object -First 15 | ForEach-Object { "$_" })
    }
    $drawingPath = Join-Path $folder ($harnessName + '.dwg')
    Copy-Item -LiteralPath $Template -Destination $drawingPath -Force
    [IO.File]::WriteAllText((Join-Path $folder 'ftf-settings.json'), '{ "version": "1" }', [Text.Encoding]::ASCII)
    $pluginFolder = Split-Path -Parent $PluginDll
    $factoryRules = @((Join-Path $pluginFolder 'rules.json'), (Join-Path $pluginFolder 'config\rules.json')) |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $factoryRules) { Fail ($prefix + "no rules.json shipped beside $PluginDll") @() }
    Copy-Item -LiteralPath $factoryRules -Destination (Join-Path $folder 'rules.json') -Force

    $scriptPath = Join-Path $folder ($harnessName + '.scr')
    $scriptLines = @("NETLOAD `"$PluginDll`"", "NETLOAD `"$harnessDll`"") + $body +
                   @('SAVEAS', '2018', ('"' + (Join-Path $folder ($harnessName + '-out.dwg')) + '"'))
    [IO.File]::WriteAllLines($scriptPath, [string[]]$scriptLines, [Text.Encoding]::ASCII)

    $resultPath = Join-Path $folder 'result.txt'
    $rawPath = Join-Path $folder 'accore.raw.txt'
    Remove-Item $resultPath, $rawPath -Force -ErrorAction SilentlyContinue
    [Environment]::SetEnvironmentVariable($envName, $resultPath, 'Process')
    # The plugin's own trace (FtfCommandTrace.Step) into the same file, beside the harness's STEP lines.
    [Environment]::SetEnvironmentVariable('FTF_TRACE_FILE', $resultPath, 'Process')
    $arguments = '/i "' + $drawingPath + '" /s "' + $scriptPath + '" /product C3D /language en-US'
    Say ($prefix + 'running accoreconsole (up to 300 s)...')
    $started = Get-Date
    $p = Start-Process -FilePath $accore -ArgumentList $arguments -PassThru -WindowStyle Hidden -RedirectStandardOutput $rawPath
    $null = $p.Handle   # kept, so ExitCode is readable once the process has ended
    $until = (Get-Date).AddSeconds(300)
    $why = $null
    while (-not $p.HasExited) {
        Start-Sleep -Milliseconds 500
        $t = Read-Console $rawPath
        $u = [regex]::Match($t, 'Unknown command\s*"?([A-Za-z0-9_\-]+)')
        if ($u.Success) { $why = 'Unknown command ' + $u.Groups[1].Value + ' -- the command is not registered in the loaded build.'; break }
        if ($t -match '\[FATAL\]') { Start-Sleep -Milliseconds 500; break }
        if ((Get-Date) -gt $until) { $why = 'no result after 300 s: a prompt is waiting for input the script did not give.'; break }
    }
    $killed = $false
    if (-not $p.HasExited) {
        $killed = $true
        if ($env:OS -eq 'Windows_NT') { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } else { try { $p.Kill() } catch { } }
        Start-Sleep -Milliseconds 500
    }
    $exitReport = Get-ExitReport $p $killed $(if ($why) { $why } else { 'FATAL in the run' }) $started
    [Environment]::SetEnvironmentVariable($envName, $null, 'Process')
    [Environment]::SetEnvironmentVariable('FTF_TRACE_FILE', $null, 'Process')

    $text = Read-Console $rawPath
    $checkLog = Join-Path $folder ($harnessName + '.log')
    $text | Set-Content -Path $checkLog -Encoding utf8
    $runLines = $text -split "`r?`n"
    $r = if (Test-Path -LiteralPath $resultPath) { @(Get-Content -LiteralPath $resultPath) } else { @() }
    $failed = @($r | Where-Object { $_ -like 'FAIL *' -or $_ -like 'FATAL *' } | ForEach-Object { $_ })
    $last = @($runLines | Where-Object { $_.Trim() } | Select-Object -Last 25)
    $diagnostics = @($exitReport) + @(Get-RunDiagnostics $r) + @("result and trace: $resultPath")
    if ($why) { Fail ($prefix + $why) (@($failed) + $diagnostics + @('--- last lines of the run ---') + $last + @("log: $checkLog")) }
    $fatal = @($r | Where-Object { $_ -like 'FATAL*' })
    if ($fatal.Count -gt 0) { Fail ($prefix + $fatal[0].Substring(6)) ($diagnostics + @('--- last lines of the run ---') + $last + @("log: $checkLog")) }
    if ($r.Count -eq 0) { Fail ($prefix + 'the harness never ran (NETLOAD of the plugin or the harness failed?)') ($diagnostics + $last + @("log: $checkLog")) }
    if (-not ($r -contains 'COMPLETE')) { Fail ($prefix + 'the run stopped before its end') (@($failed) + $diagnostics + $last + @("log: $checkLog")) }
    $notPassed = @($stages | Where-Object { -not ($r -contains "STAGE $_ PASS") })
    if ($notPassed.Count -gt 0 -or -not ($r | Where-Object { $_ -like 'RESULT PASS*' })) {
        Fail ($prefix + 'stages not passed: ' + ($notPassed -join ', ')) (@($failed | Select-Object -First 20) + @("log: $checkLog"))
    }
    $count = @($r | Where-Object { $_ -like 'PASS *' }).Count
    Say ("{0}PASS ({1} checks: {2})" -f $prefix, $count, $passText)
    foreach ($n in @($r | Where-Object { $_ -like 'NOTE *' })) { Say ("  " + $n) }
    Say ($prefix + "log: $checkLog")
}

# Source-point identity and linework coding (tests\LiveSmoke\IdentityTestHarness.cs): synthetic COGO
# points and the plain polylines drawn through them, as a TBC export arrives. The plugin's own reader
# must return every point's number, handle, raw description and XYZ as CogoPoint reports them;
# FTFLINES must name each line from its shots (a stacked shot, a vertex across a rounding boundary,
# a near miss, a 3D polyline, a numbered figure) and write the same inventory twice; FTFREVIEWCHECK
# must flag an unknown word after a linework code. No point or line may change.
function Invoke-IdentityCheck {
    Invoke-RequiredCheck 'identity' 'IdentityTestHarness' 'FTF_IDENTITY_RESULT' @(
        'IDSEED',
        'ZOOM', 'E',
        'IDVERIFY', 'READER',
        'FTFLINES',
        'IDVERIFY', 'LINES',
        'FTFLINES',
        'IDVERIFY', 'AGAIN',
        'FTFREVIEWCHECK',
        'IDVERIFY', 'REVIEW',
        'IDEND'
    ) @('READER', 'LINES', 'AGAIN', 'REVIEW') `
      'points read as Civil 3D stores them; FTFLINES names lines from their shots, twice the same; FTFREVIEWCHECK reads linework coding; nothing changed'
}

# Point accountability and linework continuity (tests\LiveSmoke\AccountabilityTestHarness.cs): 34
# synthetic points, plain polylines and a TIN covering the audit's scenarios. FTFRUN must run the
# checks after itself and find the interrupted fence (probable gap), never join parallel strings or a
# gate opening, report duplicates, unknown and bare codes, undrawn and skipped shots, an ignore entry
# without a reason and a ground shot no surface uses; a second FTFRUN must duplicate nothing; an
# approved correction must be one separate line on the fence layer that a recheck keeps; a decision
# must reopen when its point moves; FTFQC must refuse, then mark once every item has a reason; an
# erased point and an erased correction must come back on the list. No seeded line may change.
function Invoke-AccountabilityCheck {
    Invoke-RequiredCheck 'accountability' 'AccountabilityTestHarness' 'FTF_ACCOUNT_RESULT' @(
        'ACCSEED',
        'ZOOM', 'E',
        'FTFRUN', 'Standard',
        'ACCVERIFY', 'RUN',
        'FTFRUN', 'Standard',
        'ACCVERIFY', 'AGAIN',
        '-FTFREVIEW', 'Approve', 'line.gap:FNC:103-105', 'approved in the smoke test',
        'ACCVERIFY', 'APPROVED',
        'FTFREVIEWCHECK',
        'ACCVERIFY', 'KEPT',
        '-FTFREVIEW', 'NotApplicable', 'point.nosymbol:702', 'cleanout symbol comes from the utility base map',
        'ACCMOVE',
        'FTFREVIEWCHECK',
        'ACCVERIFY', 'CHANGED',
        'FTFQC', 'Complete',
        'ACCVERIFY', 'REFUSED',
        '-FTFREVIEW', 'NotApplicable', '*', 'closed for the smoke test',
        'FTFQC', 'Complete',
        'ACCVERIFY', 'QC',
        'ACCERASE',
        'FTFREVIEWCHECK',
        'ACCVERIFY', 'DELETED',
        'ACCEND'
    ) @('RUN', 'AGAIN', 'APPROVED', 'KEPT', 'CHANGED', 'REFUSED', 'QC', 'DELETED') `
      'every point accounted for or on the review list; gaps, skipped shots and undrawn strings found, gates and parallel strings left alone; corrections separate, kept and traceable; decisions reopen when the source changes; QC Complete waits for every required item'
}

try { Invoke-IdentityCheck }
catch { Fail ('the identity check could not run: ' + $_.Exception.Message) @() }
try { Invoke-AccountabilityCheck }
catch { Fail ('the accountability check could not run: ' + $_.Exception.Message) @() }

# --- 7. annotation styles: reported, not required -------------------------------------
# A second, separate run on a fresh drawing: Settings > Text Styles / Leader Styles / Style
# Assignments must control what FTFLABELS draws (text style, height, colour, prefix, mask, layer,
# spline leader, arrowhead, landing), a feature turned off must stay unlabelled, and FTFRESTYLE
# must update the labels in place (preview first, a hand-moved label kept where it was put).
# New with the style system, so it reports loudly but never undoes the install on its own.
function Invoke-StyleCheck {
    $styleFolder = Join-Path $WorkFolder 'styles'
    New-Item -ItemType Directory -Force -Path $styleFolder | Out-Null
    $styleSource = Join-Path $repo 'tests\LiveSmoke\StyleTestHarness.cs'
    if (-not (Test-Path -LiteralPath $styleSource)) { Say 'style check: harness not in this build; skipped'; return }
    $styleHarness = Join-Path $styleFolder 'FtfStyleHarness.dll'
    $out = & $csc /nologo /target:library "/out:$styleHarness" $styleSource `
        "/r:$C3D2024\accoremgd.dll" "/r:$C3D2024\acdbmgd.dll" "/r:$C3D2024\acmgd.dll" `
        "/r:$C3D2024\C3D\AeccDbMgd.dll" "/r:$C3D2024\AecBaseMgd.dll" /r:System.Core.dll 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $styleHarness)) {
        Write-Host '   STYLE CHECK could not compile its harness (not blocking):' -ForegroundColor Yellow
        $out | Where-Object { "$_" -match 'error' } | Select-Object -First 8 | ForEach-Object { Write-Host "     $_" -ForegroundColor Yellow }
        return
    }
    $styleDrawing = Join-Path $styleFolder 'styles.dwg'
    Copy-Item -LiteralPath $Template -Destination $styleDrawing -Force
    $settings = @(
        '{',
        '  "version": "1",',
        '  "styles": {',
        '    "text": [',
        '      { "name": "Smoke text", "textStyle": "FTFSMOKE", "heightPlotted": 0.12, "color": "3", "prefix": "S-", "mask": "Background" }',
        '    ],',
        '    "leaders": [',
        '      { "name": "Smoke leader", "lineType": "Spline", "arrowBlock": "_Dot", "arrowSizePlotted": 0.05, "color": "1", "landing": false }',
        '    ],',
        '    "assignments": [',
        '      { "kind": "Label", "category": "Utilities", "textStyle": "Smoke text", "layer": "FTF-SMOKE-UTIL" },',
        '      { "kind": "Label", "code": "FH", "leaderStyle": "Smoke leader", "leader": "Always" },',
        '      { "kind": "Label", "code": "MB", "enabled": false }',
        '    ]',
        '  }',
        '}')
    [IO.File]::WriteAllLines((Join-Path $styleFolder 'ftf-settings.json'), [string[]]$settings, [Text.Encoding]::ASCII)
    $styleScript = Join-Path $styleFolder 'styles.scr'
    $styleLines = @(
        "NETLOAD `"$PluginDll`"",
        "NETLOAD `"$styleHarness`"",
        'STYLESEED',
        'ZOOM', 'E',
        'FTFLABELS',
        'STYLEVERIFY', 'STYLED',
        'STYLEMOVE',
        'STYLEEDIT',
        'FTFRESTYLE', 'Preview',
        'STYLEVERIFY', 'PREVIEW',
        'FTFRESTYLE', 'Apply',
        'STYLEVERIFY', 'RESTYLED',
        'STYLEEND',
        'SAVEAS', '2018', ('"' + (Join-Path $styleFolder 'styles-out.dwg') + '"')
    )
    [IO.File]::WriteAllLines($styleScript, [string[]]$styleLines, [Text.Encoding]::ASCII)

    $styleResult = Join-Path $styleFolder 'result.txt'
    $styleRaw = Join-Path $styleFolder 'accore.raw.txt'
    Remove-Item $styleResult, $styleRaw -Force -ErrorAction SilentlyContinue
    $env:FTF_STYLE_RESULT = $styleResult
    $env:FTF_TRACE_FILE = $styleResult
    $styleArgs = '/i "' + $styleDrawing + '" /s "' + $styleScript + '" /product C3D /language en-US'
    Say 'style check: running accoreconsole (up to 240 s)...'
    $styleStarted = Get-Date
    $p = Start-Process -FilePath $accore -ArgumentList $styleArgs -PassThru -WindowStyle Hidden -RedirectStandardOutput $styleRaw
    $null = $p.Handle
    $until = (Get-Date).AddSeconds(240)
    $why = $null
    while (-not $p.HasExited) {
        Start-Sleep -Milliseconds 500
        $t = Read-Console $styleRaw
        $u = [regex]::Match($t, 'Unknown command\s*"?([A-Za-z0-9_\-]+)')
        if ($u.Success) { $why = 'Unknown command ' + $u.Groups[1].Value; break }
        if ((Get-Date) -gt $until) { $why = 'no result after 240 s (a prompt waited for input)'; break }
    }
    $styleKilled = $false
    if (-not $p.HasExited) {
        $styleKilled = $true
        if ($env:OS -eq 'Windows_NT') { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } else { try { $p.Kill() } catch { } }
        Start-Sleep -Milliseconds 500
    }
    $styleExit = Get-ExitReport $p $styleKilled $(if ($why) { $why } else { 'stopped' }) $styleStarted
    Remove-Item Env:\FTF_STYLE_RESULT, Env:\FTF_TRACE_FILE -ErrorAction SilentlyContinue
    $styleLog = Join-Path $styleFolder 'styles.log'
    (Read-Console $styleRaw) | Set-Content -Path $styleLog -Encoding utf8
    $r = if (Test-Path -LiteralPath $styleResult) { @(Get-Content -LiteralPath $styleResult) } else { @() }
    $fails = @($r | Where-Object { $_ -like 'FAIL *' } | ForEach-Object { $_.Substring(5) })
    $pass = @($r | Where-Object { $_ -like 'PASS *' }).Count
    if (-not $why -and ($r -contains 'COMPLETE') -and $fails.Count -eq 0) {
        Say ("style check: PASS ({0} checks: styles control the labels FTF draws; FTFRESTYLE updates them in place)" -f $pass)
    } else {
        Write-Host ("   STYLE CHECK FAILED (not blocking; the install stands): {0} passed, {1} failed{2}" -f $pass, $fails.Count, $(if ($why) { "; $why" } else { '' })) -ForegroundColor Yellow
        $fails | Select-Object -First 12 | ForEach-Object { Write-Host "     $_" -ForegroundColor Yellow }
        if (-not ($r -contains 'COMPLETE')) { Write-Host '     the style run stopped before its end' -ForegroundColor Yellow }
        foreach ($l in @($styleExit) + @(Get-RunDiagnostics $r)) { Write-Host "     $l" -ForegroundColor Yellow }
    }
    Say "style log: $styleLog"
}
try { Invoke-StyleCheck }
catch { Write-Host ('   style check could not run (not blocking): ' + $_.Exception.Message) -ForegroundColor Yellow }
exit 0
