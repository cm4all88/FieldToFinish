<#
.SYNOPSIS
    Checks FTF against real Civil 3D survey figures, headless (accoreconsole), on the synthetic
    survey figure fixture.

.DESCRIPTION
    Survey figures can only be made by a Civil 3D survey database import, so the fixture drawing is
    built once by hand from tests\LiveSmoke\fixtures\figures-synthetic.csv (fixtures\README.md says
    how) and kept OUTSIDE the repository (a saved drawing carries the saving user's name and paths).

    This script copies the fixture into a work folder (the original is never opened for writing; its
    hash is checked before and after), puts factory settings and the build's own rules.json beside
    the copy, compiles tests\LiveSmoke\FigureFixtureHarness.cs and runs:

      FIGSNAP            reads every SurveyFigure (Name, GetPoints(AllPoints), closure), writes the
                         figure manifest, and checks the plugin reads every figure point, finds the
                         shots at them, and parses the coding the way Civil 3D built the figures
      FTFLINES, FIGVERIFY LINES    each figure's inventory row: full name, source, vertices, shots
      FTFLINES, FIGVERIFY AGAIN    the same inventory again, byte for byte
      FIGEND

    and at every stage that no figure or point changed and nothing FTF-owned appeared.

    Exit codes: 0 every stage passed; 1 a check failed or the run did not finish; 2 the fixture
    drawing does not exist yet (nothing was verified).

.PARAMETER Fixture
    The fixture drawing. Default C:\dev\FTF-fixtures\figures-synthetic.dwg.

.PARAMETER PluginDll
    The FieldCodes.Cad.dll under test. Default: the installed bundle (accoreconsole loads the
    installed bundle at startup, so that is the build that is tested when one is installed).

.EXAMPLE
    .\tests\LiveSmoke\run-figures.ps1
#>
[CmdletBinding()]
param(
    [string]$Fixture = 'C:\dev\FTF-fixtures\figures-synthetic.dwg',
    [string]$PluginDll,
    [string]$C3D2024 = 'C:\Program Files\Autodesk\AutoCAD 2024',
    [string]$WorkFolder,
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$C3D2024 = $C3D2024.TrimEnd('\')
$accore = Join-Path $C3D2024 'accoreconsole.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $repo 'tests\LiveSmoke\FigureFixtureHarness.cs'
if (-not $PluginDll) { $PluginDll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024\FieldCodes.Cad.dll' }
if (-not $WorkFolder) {
    $WorkFolder = Join-Path (Join-Path $env:LOCALAPPDATA 'FieldToFinish\figures') (Get-Date -Format 'yyyyMMdd-HHmmss')
}

function Say([string]$text) { Write-Host "   $text" }

function Fail([string]$why, [string[]]$details) {
    Write-Host ''
    Write-Host "   FIGURE CHECK FAILED: $why" -ForegroundColor Red
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

# --- the fixture ----------------------------------------------------------------------

if (-not (Test-Path -LiteralPath $Fixture)) {
    Write-Host ''
    Write-Host "   FIGURE CHECK NOT RUN: there is no fixture drawing at $Fixture" -ForegroundColor Yellow
    Write-Host '   Build it once in Civil 3D as tests\LiveSmoke\fixtures\README.md says, then run this again.' -ForegroundColor Yellow
    Write-Host '   Until it runs and passes, FTF is NOT verified against Civil 3D survey figures.' -ForegroundColor Yellow
    exit 2
}

$Fixture = (Resolve-Path -LiteralPath $Fixture).Path
$PluginDll = [IO.Path]::GetFullPath($PluginDll)
foreach ($need in @($PluginDll, $accore, $csc, $source)) {
    if (-not (Test-Path -LiteralPath $need)) { Fail "missing: $need" @('Civil 3D 2024, an installed FTF build and .NET Framework 4.8 are required.') }
}
foreach ($path in @($PluginDll, $WorkFolder)) {
    if ($path -match '[^\x20-\x7E]') { Fail "path has non-ASCII characters (accoreconsole scripts are ASCII): $path" @() }
}

New-Item -ItemType Directory -Force -Path $WorkFolder | Out-Null
$before = (Get-FileHash -LiteralPath $Fixture -Algorithm SHA256).Hash
$drawing = Join-Path $WorkFolder 'figures.dwg'
Copy-Item -LiteralPath $Fixture -Destination $drawing -Force
[IO.File]::WriteAllText((Join-Path $WorkFolder 'ftf-settings.json'), '{ "version": "1" }', [Text.Encoding]::ASCII)
$pluginFolder = Split-Path -Parent $PluginDll
$factoryRules = @((Join-Path $pluginFolder 'rules.json'), (Join-Path $pluginFolder 'config\rules.json')) |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $factoryRules) { Fail "no rules.json shipped beside $PluginDll" @() }
Copy-Item -LiteralPath $factoryRules -Destination (Join-Path $WorkFolder 'rules.json') -Force

# --- the harness and the script ---------------------------------------------------------

$harness = Join-Path $WorkFolder 'FtfFigureHarness.dll'
$compile = & $csc /nologo /target:library "/out:$harness" $source `
    "/r:$C3D2024\accoremgd.dll" "/r:$C3D2024\acdbmgd.dll" "/r:$C3D2024\acmgd.dll" `
    "/r:$C3D2024\C3D\AeccDbMgd.dll" "/r:$C3D2024\AecBaseMgd.dll" /r:System.Core.dll 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $harness)) {
    Fail 'the figure harness did not compile' @($compile | Where-Object { "$_" -match 'error' } | Select-Object -First 15 | ForEach-Object { "$_" })
}
Say 'harness compiled'

$script = Join-Path $WorkFolder 'figures.scr'
$lines = @(
    "NETLOAD `"$PluginDll`"",
    "NETLOAD `"$harness`"",
    'FIGSNAP',
    'FTFLINES',
    'FIGVERIFY', 'LINES',
    'FTFLINES',
    'FIGVERIFY', 'AGAIN',
    'FIGEND'
)
[IO.File]::WriteAllLines($script, [string[]]$lines, [Text.Encoding]::ASCII)

# --- run, watching ----------------------------------------------------------------------

$result = Join-Path $WorkFolder 'result.txt'
$manifest = Join-Path $WorkFolder 'figures-manifest.txt'
$raw = Join-Path $WorkFolder 'accore.raw.txt'
Remove-Item $result, $manifest, $raw -Force -ErrorAction SilentlyContinue
$env:FTF_FIGURE_RESULT = $result
$env:FTF_FIGURE_MANIFEST = $manifest
$env:FTF_TRACE_FILE = $result   # the plugin's own TRACE lines beside the harness's STEP lines
$arguments = '/i "' + $drawing + '" /s "' + $script + '" /product C3D /language en-US'
Say "running accoreconsole (up to $TimeoutSeconds s)..."
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
$exitLine = 'accoreconsole: still running; stopped by this script'
if (-not $process.HasExited) {
    if ($env:OS -eq 'Windows_NT') { & taskkill.exe /PID $process.Id /T /F 2>&1 | Out-Null } else { try { $process.Kill() } catch { } }
    Start-Sleep -Milliseconds 500
} else {
    try { $process.WaitForExit(); $exitLine = 'accoreconsole: exit code {0} (0x{0:X8}; 3 = FATAL, 0xE0434352 = a .NET exception nothing caught)' -f [int]$process.ExitCode } catch { }
}
Remove-Item Env:\FTF_FIGURE_RESULT, Env:\FTF_FIGURE_MANIFEST, Env:\FTF_TRACE_FILE -ErrorAction SilentlyContinue

$text = Read-Console $raw
$log = Join-Path $WorkFolder 'figures.log'
$text | Set-Content -Path $log -Encoding utf8
$runLines = $text -split "`r?`n"
$outcome = if (Test-Path -LiteralPath $result) { @(Get-Content -LiteralPath $result) } else { @() }
$failed = @($outcome | Where-Object { $_ -like 'FAIL *' -or $_ -like 'FATAL *' })
$last = @($runLines | Where-Object { $_.Trim() } | Select-Object -Last 25)
# Where a run that stopped early got to: how accoreconsole ended, the exception, the last steps.
$last = @($exitLine) + @($outcome | Where-Object { $_ -like 'EXCEPTION *' } | Select-Object -First 30) +
        @('--- last steps ---') + @($outcome | Where-Object { $_ -like 'STEP *' -or $_ -like 'TRACE *' } | Select-Object -Last 15) +
        @('--- last lines of the console ---') + $last

# --- judge ------------------------------------------------------------------------------

$after = (Get-FileHash -LiteralPath $Fixture -Algorithm SHA256).Hash
if ($after -ne $before) { Fail "the original fixture changed during the run: $Fixture" @() }
if ($stopped) { Fail $stopped (@($failed) + @('--- last lines of the run ---') + $last + @("log: $log")) }
$fatal = @($outcome | Where-Object { $_ -like 'FATAL*' })
if ($fatal.Count -gt 0) { Fail ($fatal[0].Substring(6)) (@('--- last lines of the run ---') + $last + @("log: $log")) }
if ($outcome.Count -eq 0) { Fail 'the harness never ran (NETLOAD of the plugin or the harness failed?)' ($last + @("log: $log")) }
if (-not ($outcome -contains 'COMPLETE')) { Fail 'the run stopped before its end' (@($failed) + $last + @("log: $log")) }
$stages = @('SNAP', 'LINES', 'AGAIN')
$notPassed = @($stages | Where-Object { -not ($outcome -contains "STAGE $_ PASS") })
$renamed = @($outcome | Where-Object { $_ -like 'RENAMED *' }) | Select-Object -First 1
if ($notPassed.Count -gt 0 -or -not ($outcome | Where-Object { $_ -like 'RESULT PASS*' })) {
    Fail ('stages not passed: ' + ($notPassed -join ', ')) (@($failed | Select-Object -First 25) + @("manifest: $manifest", "log: $log"))
}

$count = @($outcome | Where-Object { $_ -like 'PASS *' }).Count
Say ("FIGURE CHECK PASS ({0} checks): FTF reads Civil 3D survey figures, their points and their coding; nothing changed" -f $count)
if ($renamed) { Say ("figures Civil 3D renamed: " + $renamed.Substring(8)) }
Say "manifest: $manifest"
Say "log: $log"
exit 0
