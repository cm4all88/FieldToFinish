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

    Exit code 0 only when every stage passed and the run reached its end.

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
$arguments = '/i "' + $drawing + '" /s "' + $script + '" /product C3D /language en-US'
Say "running accoreconsole (up to $TimeoutSeconds s)..."
$process = Start-Process -FilePath $accore -ArgumentList $arguments -PassThru -WindowStyle Hidden -RedirectStandardOutput $raw
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
if (-not $process.HasExited) {
    # The whole tree, at once: nothing from a failed run may linger holding the plugin open.
    if ($env:OS -eq 'Windows_NT') { & taskkill.exe /PID $process.Id /T /F 2>&1 | Out-Null }
    else { try { $process.Kill() } catch { } }
    Start-Sleep -Milliseconds 500
}
Remove-Item Env:\FTF_SMOKE_EXPECT_DLL, Env:\FTF_SMOKE_EXPECT_COMMIT, Env:\FTF_SMOKE_RESULT -ErrorAction SilentlyContinue

$text = Read-Console $raw
$text | Set-Content -Path $log -Encoding utf8
$logLines = $text -split "`r?`n"
$outcome = if (Test-Path -LiteralPath $result) { @(Get-Content -LiteralPath $result) } else { @() }
$failLines = @($logLines | Where-Object { $_ -match '\[(FAIL|FATAL)\]' } | ForEach-Object { $_.Trim() })
$loadProblems = @($logLines | Where-Object { $_ -match 'Cannot load assembly|FileLoadException|FileNotFoundException|TypeLoadException|MissingMethodException|BadImageFormatException|TypeInitializationException' } | ForEach-Object { $_.Trim() } | Select-Object -Unique)

# --- 5. judge -----------------------------------------------------------------------

$tail = @($logLines | Where-Object { $_.Trim() } | Select-Object -Last 25)
if ($stopped) { Fail $stopped (@($failLines) + @('--- last lines of the run ---') + $tail) }

$fatal = @($outcome | Where-Object { $_ -like 'FATAL*' })
if ($fatal.Count -gt 0) { Fail ($fatal[0].Substring(6)) (@($failLines) + @('--- last lines of the run ---') + $tail) }
if ($outcome.Count -eq 0) { Fail 'the harness never ran (NETLOAD of the plugin or the harness failed?)' (@($loadProblems) + $tail) }
if ($loadProblems.Count -gt 0) { Fail 'something failed to load in Civil 3D' $loadProblems }

$required = @('UNDONE', 'FIRST', 'AGAIN', 'BOUNDARY')
$missing = @($required | Where-Object { -not ($outcome -contains "STAGE $_ PASS") })
if (-not ($outcome -contains 'COMPLETE')) { Fail 'the run stopped before its end' (@($failLines) + $tail) }
if ($missing.Count -gt 0 -or -not ($outcome | Where-Object { $_ -like 'RESULT PASS*' })) {
    Fail ('stages not passed: ' + ($missing -join ', ')) $failLines
}

$loaded = @($outcome | Where-Object { $_ -like 'LOADED *' }) | Select-Object -First 1
$commands = @($outcome | Where-Object { $_ -like 'COMMANDS *' }) | Select-Object -First 1
$passes = @($logLines | Where-Object { $_ -match '\[PASS\]' }).Count
Say ("PASS: {0} checks; build {1}; {2}; stages {3}" -f $passes, $Commit.Substring(0, 7), $commands, ($required -join ', '))
Say "log: $log"
exit 0
