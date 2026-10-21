<#
.SYNOPSIS
    Builds Field to Finish against Civil 3D 2024, installs it for the current user, and
    runs the FTFCOMPARE smoke test headlessly in Civil 3D (accoreconsole).

.DESCRIPTION
    One script, four steps, each reported as it runs:
      1. Prerequisites  - Civil 3D 2024 (AutoCAD R24.3) assemblies, the .NET Framework 4.8
                          runtime, the .NET 8 SDK (compiler only: the plugin targets
                          .NET Framework 4.8), the .NET Framework C# compiler (for the smoke
                          harness), the stock Civil 3D template.
      2. Build          - dotnet build FieldToFinish.sln against the installed Autodesk DLLs,
                          then verifies the output (net48 plugin, netstandard2.0 engine, no
                          Autodesk DLL copied), then the unit tests.
      3. Install        - copies the bundle to %APPDATA%\Autodesk\ApplicationPlugins so the
                          commands load on startup (Civil 3D must be closed).
      4. Smoke test     - seeds a consultant xref and a survey in a fresh drawing, drives
                          FTFCOMPARERUN / APPLY / QC / RESTORE / SAVE in accoreconsole, and
                          checks the plotting state after each step.

    The script stops at the first failed step and says what to do. It never writes
    anywhere but the build output folders, the per-user bundle folder, and
    tests\LiveSmoke\compare\.

.PARAMETER C3D2024
    Install root of AutoCAD / Civil 3D 2024. Default: C:\Program Files\Autodesk\AutoCAD 2024.

.PARAMETER SkipTests, SkipInstall, SkipSmoke
    Leave a step out.

.EXAMPLE
    .\deploy\build-install-test.ps1
    .\deploy\build-install-test.ps1 -SkipInstall -SkipSmoke      # build and unit tests only
    .\deploy\build-install-test.ps1 -C3D2024 "D:\Autodesk\AutoCAD 2024"
#>
[CmdletBinding()]
param(
    [string]$C3D2024 = 'C:\Program Files\Autodesk\AutoCAD 2024',
    [switch]$SkipTests,
    [switch]$SkipInstall,
    [switch]$SkipSmoke,
    [int]$SmokeTimeoutSeconds = 420
)

$ErrorActionPreference = 'Stop'
$C3D2024 = $C3D2024.TrimEnd('\')
$repo = Split-Path -Parent $PSScriptRoot
$configuration = 'Debug'
$buildOut = Join-Path $repo "src\FieldCodes.Cad\bin\$configuration\net48"
$bundleDll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024\FieldCodes.Cad.dll'

function Step([string]$title) { Write-Host ''; Write-Host "== $title" -ForegroundColor Cyan }
function Ok([string]$text)    { Write-Host "   OK   $text" -ForegroundColor Green }
function Warn([string]$text)  { Write-Host "   WARN $text" -ForegroundColor Yellow }
function Fail([string]$text, [string[]]$details) {
    Write-Host ''
    Write-Host "FAILED: $text" -ForegroundColor Red
    foreach ($d in $details) { Write-Host "   $d" -ForegroundColor Red }
    exit 1
}
# Runs a native program and returns its output lines and exit code. Local 'Continue'
# on purpose: under Windows PowerShell 5.1 a line on stderr would otherwise turn
# into a terminating error while $ErrorActionPreference is 'Stop'.
function Invoke-Native([string]$exe, [string[]]$arguments) {
    $ErrorActionPreference = 'Continue'
    $lines = @(& $exe @arguments 2>&1 | ForEach-Object { "$_" })
    return [pscustomobject]@{ Lines = $lines; ExitCode = $LASTEXITCODE }
}

# The TargetFrameworkAttribute string sits in the assembly as plain text; reading the
# bytes neither loads the assembly nor locks the file, so a rebuild still works after.
function Get-TargetFrameworkOf([string]$path) {
    $text = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($path))
    $m = [regex]::Match($text, '\.NET(Framework|CoreApp|Standard),Version=v[0-9.]+')
    if ($m.Success) { return $m.Value } else { return '(no target framework attribute)' }
}

# ------------------------------------------------------------------ 1. prerequisites
Step '1. Prerequisites'

if ($repo -match '[^\x20-\x7E]') { Fail "The repository path contains non-ASCII characters: $repo" @('accoreconsole scripts are plain ASCII; clone to a path such as C:\dev\FieldToFinish.') }

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Fail '.NET SDK not found.' @('Install the .NET 8 SDK from https://dotnet.microsoft.com/download and reopen PowerShell.') }
$sdkVersion = (Invoke-Native 'dotnet' @('--version')).Lines | Select-Object -First 1
if (-not $sdkVersion -or [int]($sdkVersion.Split('.')[0]) -lt 8) { Fail ".NET SDK 8 or newer is needed (found '$sdkVersion')." @('Install the .NET 8 SDK from https://dotnet.microsoft.com/download.') }
Ok ".NET SDK $sdkVersion"

$required = @("$C3D2024\AcDbMgd.dll", "$C3D2024\AcMgd.dll", "$C3D2024\AcCoreMgd.dll", "$C3D2024\AdWindows.dll", "$C3D2024\AecBaseMgd.dll", "$C3D2024\C3D\AeccDbMgd.dll")
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missing.Count -gt 0) {
    Fail "Civil 3D 2024 assemblies not found under '$C3D2024'." (@('Missing:') + $missing + @('', 'If Civil 3D 2024 is installed elsewhere, run again with -C3D2024 "<install root>" (the folder that holds acad.exe).'))
}
Ok "Civil 3D 2024 assemblies at $C3D2024"

# Civil 3D 2024 is AutoCAD R24.3 on .NET Framework 4.8; 2025 and later are R25+ on
# .NET 8 and cannot load this plugin. Checked here, before anything is built or copied.
$acdb = [Diagnostics.FileVersionInfo]::GetVersionInfo("$C3D2024\AcDbMgd.dll")
$hostVersion = "$($acdb.FileMajorPart).$($acdb.FileMinorPart)"
if ($acdb.FileMajorPart -ne 24) {
    Fail "The Autodesk assemblies under '$C3D2024' are release R$hostVersion, not Civil 3D 2024 (R24.3)." @(
        'This plugin is built for Civil 3D 2024 on .NET Framework 4.8.',
        'Civil 3D 2025 and later run on .NET 8 and need a different build; 2023 and earlier have a different API surface.',
        'If 2024 is installed in another folder, run again with -C3D2024 "<that folder>".')
}
if ($acdb.FileMinorPart -ne 3) { Warn "AcDbMgd.dll is R$hostVersion; Civil 3D 2024 is R24.3 (2021-2023 are R24.0-R24.2). The build may still work, but it is not the tested host." }
else { Ok "Host is AutoCAD R$hostVersion (Civil 3D 2024, .NET Framework 4.8)" }

$ndp = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue
if (-not $ndp -or $ndp.Release -lt 528040) { Fail '.NET Framework 4.8 runtime not found.' @('Civil 3D 2024 itself needs it; install it from https://dotnet.microsoft.com/download/dotnet-framework/net48.') }
Ok '.NET Framework 4.8 runtime present (the plugin runs on it; the .NET 8 SDK is only the compiler)'

$accore = Join-Path $C3D2024 'accoreconsole.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$template = Join-Path $env:LOCALAPPDATA 'Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt'
if (-not $SkipSmoke) {
    if (-not (Test-Path -LiteralPath $accore)) { Fail "accoreconsole.exe not found at $accore (needed for the smoke test)." @('Run with -SkipSmoke to build and install only.') }
    if (-not (Test-Path -LiteralPath $csc)) { Fail "The .NET Framework C# compiler was not found at $csc (needed to build the smoke harness)." @('It ships with .NET Framework 4.8; run with -SkipSmoke to build and install only.') }
    if (-not (Test-Path -LiteralPath $template)) {
        $alt = foreach ($root in @((Join-Path $env:LOCALAPPDATA 'Autodesk'), (Join-Path $env:ProgramData 'Autodesk'))) {
            if (Test-Path -LiteralPath $root) { Get-ChildItem -Path $root -Filter '_*Civil 3D (Imperial) NCS.dwt' -Recurse -ErrorAction SilentlyContinue }
        }
        $alt = @($alt | Where-Object { $_.FullName -like '*2024*' } | Select-Object -First 1)
        if ($alt.Count -gt 0) { $template = $alt[0].FullName } else { Fail "The stock Civil 3D template was not found: $template" @('Start Civil 3D 2024 once so its templates are installed, or run with -SkipSmoke.') }
    }
    Ok "accoreconsole, csc and the NCS template are available"
    Ok "Template: $template"
}

$running = Get-Process -Name acad -ErrorAction SilentlyContinue
if ($running) { Fail 'Civil 3D is running.' @('It holds the plugin assembly open; close it and run this script again.') }

# ------------------------------------------------------------------------ 2. build
Step '2. Build (dotnet build FieldToFinish.sln against the Civil 3D 2024 assemblies)'
$build = Invoke-Native 'dotnet' @('build', (Join-Path $repo 'FieldToFinish.sln'), '-c', $configuration, "-p:C3D2024=$C3D2024", '--nologo', '-v', 'minimal')
if ($build.ExitCode -ne 0) {
    $errors = @($build.Lines | Where-Object { $_ -match 'error ' } | Select-Object -Unique -First 25)
    Fail 'The build failed.' ($errors + @('', 'A compile error inside src\FieldCodes.Cad\Compare means an Autodesk API member differs from what the code expects;',
        'the file and line are above. README "FTFCOMPARE > Verified and not verified" lists the members to check first.'))
}
$dll = Join-Path $buildOut 'FieldCodes.Cad.dll'
if (-not (Test-Path -LiteralPath $dll)) { Fail "Build reported success but $dll is missing." }
$warnings = @($build.Lines | Where-Object { $_ -match 'warning ' } | Select-Object -Unique)
Ok "Built $dll ($($warnings.Count) warning(s))"

Step '2a. Verify what was built'
$cadTfm = Get-TargetFrameworkOf $dll
if ($cadTfm -ne '.NETFramework,Version=v4.8') { Fail "FieldCodes.Cad.dll targets '$cadTfm'; Civil 3D 2024 needs .NETFramework,Version=v4.8." @('src\FieldCodes.Cad\FieldCodes.Cad.csproj must say <TargetFramework>net48</TargetFramework>.') }
Ok "FieldCodes.Cad.dll targets $cadTfm (loads in Civil 3D 2024)"
$coreDll = Join-Path $buildOut 'FieldCodes.dll'
if (-not (Test-Path -LiteralPath $coreDll)) { Fail "FieldCodes.dll (the comparison engine) is missing from $buildOut." }
$coreTfm = Get-TargetFrameworkOf $coreDll
if ($coreTfm -notmatch '^\.NETStandard,Version=v2\.0$' -and $coreTfm -ne '.NETFramework,Version=v4.8') { Fail "FieldCodes.dll targets '$coreTfm', which .NET Framework 4.8 cannot load." @('src\FieldCodes\FieldCodes.csproj must target netstandard2.0 (or net48).') }
Ok "FieldCodes.dll targets $coreTfm (usable from .NET Framework 4.8)"
$copiedAutodesk = @(Get-ChildItem $buildOut -File | Where-Object { $_.Name -match '^(Ac|Aecc|Aec)[A-Za-z]*Mgd\.dll$|^AdWindows\.dll$' })
if ($copiedAutodesk.Count -gt 0) { Fail 'Autodesk assemblies were copied into the build output; they must only be referenced from the Civil 3D folder.' (@($copiedAutodesk | ForEach-Object { $_.Name }) + @('Every Autodesk <Reference> in FieldCodes.Cad.csproj needs <Private>false</Private>.')) }
Ok "Autodesk assemblies referenced from $C3D2024 only, none copied"
$json = Join-Path $buildOut 'Newtonsoft.Json.dll'
if (-not (Test-Path -LiteralPath $json)) { Fail "Newtonsoft.Json.dll is missing from $buildOut (NuGet restore did not run?)." }
$ours = [Diagnostics.FileVersionInfo]::GetVersionInfo($json)
$hostJson = Join-Path $C3D2024 'Newtonsoft.Json.dll'
if (Test-Path -LiteralPath $hostJson) {
    $theirs = [Diagnostics.FileVersionInfo]::GetVersionInfo($hostJson)
    if ($theirs.FileMajorPart -ne $ours.FileMajorPart) { Warn "Civil 3D ships Newtonsoft.Json $($theirs.FileVersion); the plugin carries $($ours.FileVersion). Both can load side by side, but if NETLOAD reports a FileLoadException for Newtonsoft.Json, say so." }
    else { Ok "Newtonsoft.Json $($ours.FileVersion) (Civil 3D ships $($theirs.FileVersion), same major version)" }
} else { Ok "Newtonsoft.Json $($ours.FileVersion) ships with the plugin" }

if (-not $SkipTests) {
    Step '2b. Unit tests (dotnet test)'
    $test = Invoke-Native 'dotnet' @('test', (Join-Path $repo 'tests\FieldCodes.Tests\FieldCodes.Tests.csproj'), '-c', $configuration, '--nologo', '-v', 'minimal')
    $summary = $test.Lines | Where-Object { $_ -match '^(Passed|Failed)!' } | Select-Object -Last 1
    if ($test.ExitCode -ne 0 -or -not $summary -or $summary -notmatch '^Passed!') {
        # Each failure block: the "Failed <test>" line, then its message up to the stack trace.
        $failed = @()
        for ($i = 0; $i -lt $test.Lines.Count; $i++) {
            if ($test.Lines[$i] -notmatch '^\s*Failed \S') { continue }
            $failed += $test.Lines[$i].Trim()
            for ($j = $i + 1; $j -lt [Math]::Min($i + 15, $test.Lines.Count); $j++) {
                if ($test.Lines[$j] -match '^\s*Stack Trace:') { break }
                if ($test.Lines[$j].Trim()) { $failed += '   ' + $test.Lines[$j].Trim() }
            }
        }
        if ($failed.Count -eq 0) { $failed = @($test.Lines | Where-Object { $_ -match '\[FAIL\]|error ' } | Select-Object -Unique -First 20) }
        Fail 'Unit tests failed.' (@("$summary") + $failed)
    }
    Ok $summary.Trim()
}

# ---------------------------------------------------------------------- 3. install
if (-not $SkipInstall) {
    Step '3. Install (per user, %APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle)'
    $bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle'
    $contents = Join-Path $bundle 'Contents\2024'
    New-Item -ItemType Directory -Force -Path $contents | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'FieldToFinish.bundle\PackageContents.xml') -Destination $bundle -Force
    # Never the Autodesk assemblies: the host already has them loaded.
    Get-ChildItem $buildOut -File | Where-Object { $_.Name -notmatch '^(Ac|Aecc|Aec)' } | Copy-Item -Destination $contents -Force
    Copy-Item (Join-Path $repo 'config\rules.json') -Destination $contents -Force
    $profilesOut = Join-Path $contents 'compare-profiles'
    New-Item -ItemType Directory -Force -Path $profilesOut | Out-Null
    Copy-Item (Join-Path $repo 'config\compare-profiles\*.json') -Destination $profilesOut -Force
    Ok "Installed $((Get-ChildItem $contents -File).Count) file(s) to $contents"
    Ok 'Civil 3D loads the bundle on startup: FTFCOMPARE and the FTF ribbon will be available without NETLOAD.'
}

# ------------------------------------------------------------------- 4. smoke test
if (-not $SkipSmoke) {
    Step '4. Smoke test (FTFCOMPARE in accoreconsole, Civil 3D 2024)'
    $here = Join-Path $repo 'tests\LiveSmoke'
    $work = Join-Path $here 'compare'
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $work | Out-Null

    # accoreconsole loads an installed bundle on startup. Load that same file, never a
    # second copy of the assembly; and refuse to test a stale installed copy.
    $plugin = $dll
    if (Test-Path -LiteralPath $bundleDll) {
        $same = (Get-FileHash -LiteralPath $bundleDll).Hash -eq (Get-FileHash -LiteralPath $dll).Hash
        if (-not $same) { Fail 'An older FieldToFinish bundle is installed and would be loaded instead of this build.' @("Installed: $bundleDll", 'Run again without -SkipInstall (refreshes it), or remove it with deploy\install-dev.ps1 -Uninstall.') }
        $plugin = $bundleDll
    }

    # The harness: standalone commands COMPARESEED / COMPAREVERIFY that read the drawing by hand.
    $harness = Join-Path $work 'CompareTestHarness.dll'
    $compile = Invoke-Native $csc @('/nologo', '/target:library', "/out:$harness", (Join-Path $here 'CompareTestHarness.cs'),
        "/r:$C3D2024\accoremgd.dll", "/r:$C3D2024\acdbmgd.dll", "/r:$C3D2024\acmgd.dll", "/r:$C3D2024\C3D\AeccDbMgd.dll", '/r:System.Core.dll')
    if ($compile.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $harness)) { Fail 'The smoke-test harness did not compile.' (@($compile.Lines | Where-Object { $_ -match 'error' } | Select-Object -First 15)) }
    Ok 'Harness compiled'

    # Deterministic settings for this job: beside-the-drawing settings win over the user's.
    $seed = Join-Path $work 'seedcompare.dwg'
    $consultant = Join-Path $work 'compare-consultant.dwg'
    $consultantSeed = Join-Path $work 'consultant-seed.dwg'
    $outFolder = Join-Path $work 'compare-out'
    $progress = Join-Path $work 'compare-progress.txt'
    Copy-Item -LiteralPath $template -Destination $seed
    Copy-Item -LiteralPath $template -Destination $consultantSeed
    $settings = @{ version = '1'; compare = @{ deliveryFolder = $outFolder; deliverySuffix = '-ACC'; writeCsvReport = $true; applyAutomaticResults = $true; decisionsBesideDrawing = $true; activeProfile = 'Default'; consultantSource = 'Xrefs' } } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $work 'ftf-settings.json'), $settings)
    # Compare the whole drawing. Without an area, the limits are the consultant data's
    # extent, which here is the xref clip at x = 1150; survey lines crossing that edge go
    # to review by design, and that would mask the partial-line split this test checks.
    $profileText = [IO.File]::ReadAllText((Join-Path $repo 'config\compare-profiles\Default.json'))
    if ($profileText -notmatch '"useConsultantExtentWhenNoBoundary": true') { Fail 'config\compare-profiles\Default.json has no "useConsultantExtentWhenNoBoundary": true to turn off for the smoke test.' }
    $profileText = $profileText -replace '"useConsultantExtentWhenNoBoundary": true', '"useConsultantExtentWhenNoBoundary": false'
    [IO.File]::WriteAllText((Join-Path $work 'ftf-compare-profile.json'), $profileText)

    function Show-FirstRun([string]$logPath) {
        # The first comparison and apply, as the plugin reported them, and the per-object CSV.
        $all = @(Get-Content -LiteralPath $logPath)
        $from = -1; $to = -1
        for ($i = 0; $i -lt $all.Count; $i++) {
            if ($from -lt 0) { if ($all[$i] -match 'COMPARENOTE: run-1') { $from = $i }; continue }
            if ($all[$i] -match 'COMPARENOTE: verify-1') { $to = $i; break }
        }
        if ($from -ge 0) {
            if ($to -lt 0) { $to = [Math]::Min($from + 80, $all.Count - 1) }
            Write-Host '   --- first comparison and apply, from the log ---' -ForegroundColor Yellow
            $all[$from..$to] | Where-Object { $_.Trim() } | ForEach-Object { Write-Host "   | $_" }
        }
        $csv = Join-Path $work 'seedcompare.ftf-compare.csv'
        if (Test-Path -LiteralPath $csv) {
            Write-Host '   --- seedcompare.ftf-compare.csv (one row per survey object) ---' -ForegroundColor Yellow
            Get-Content -LiteralPath $csv | ForEach-Object { Write-Host "   | $_" }
        }
    }
    function Show-RunEvidence([string]$logPath) {
        if (Test-Path -LiteralPath $progress) {
            Write-Host '   --- compare-progress.txt (the last line is the last step that ran) ---' -ForegroundColor Yellow
            Get-Content -LiteralPath $progress | ForEach-Object { Write-Host "   | $_" }
        } else { Write-Host '   (no compare-progress.txt: the harness never ran a command)' -ForegroundColor Yellow }
        Write-Host "   --- last 60 lines of $logPath (send these) ---" -ForegroundColor Yellow
        Get-Content -LiteralPath $logPath | Where-Object { $_.Trim() } | Select-Object -Last 60 | ForEach-Object { Write-Host "   | $_" }
    }

    # Run 1: Civil 3D draws and saves the consultant drawing itself.
    $scr1 = Join-Path $work 'runconsultant.scr'
    [IO.File]::WriteAllLines($scr1, [string[]]@("NETLOAD `"$harness`"", 'COMPARESEEDCONSULTANT', 'SAVEAS', '2018', "`"$consultant`""), [Text.Encoding]::ASCII)
    $log1 = Join-Path $work 'consultant_out.log'
    & (Join-Path $here 'run-accore.ps1') -Drawing $consultantSeed -Script $scr1 -Log $log1 -TimeoutSeconds 180 | Out-Null
    if (-not (Test-Path -LiteralPath $log1)) { Fail 'accoreconsole produced no log for the consultant drawing.' @("Expected $log1") }
    $drawn = @(Get-Content -LiteralPath $log1 | Select-String -SimpleMatch 'COMPARESEEDCONSULTANT: drawn').Count -gt 0
    if (-not $drawn -or -not (Test-Path -LiteralPath $consultant)) {
        Show-RunEvidence $log1
        Fail 'Run 1 (the consultant drawing) did not complete in accoreconsole.' @("drawn: $drawn; saved: $(Test-Path -LiteralPath $consultant)", "Log: $log1")
    }
    Ok "Consultant drawing written by Civil 3D: $consultant"

    # Run 2: the seed drawing, the plugin, and the checks. A COMPARENOTE before every
    # plugin command marks progress in a file that survives a crash.
    function Note([string]$label) { return @('COMPARENOTE', $label) }
    $scr = Join-Path $work 'runcompare.scr'
    $lines = @(
        "NETLOAD `"$plugin`""
        "NETLOAD `"$harness`""
        'COMPARESEED', 'QSAVE'
    ) + (Note 'run-1') + @('FTFCOMPARERUN', 'All') `
      + (Note 'apply-1') + @('FTFCOMPAREAPPLY') `
      + (Note 'verify-1') + @('COMPAREVERIFY', 'Applied') `
      + (Note 'qc-1') + @('FTFCOMPAREQC') `
      + (Note 'run-2') + @('FTFCOMPARERUN', 'All') `
      + (Note 'apply-2') + @('FTFCOMPAREAPPLY') `
      + (Note 'verify-2') + @('COMPAREVERIFY', 'Applied') `
      + (Note 'restore') + @('FTFCOMPARERESTORE') `
      + (Note 'verify-restored') + @('COMPAREVERIFY', 'Restored') `
      + (Note 'audit') + @('FTFCOMPAREAUDIT') `
      + (Note 'apply-3') + @('FTFCOMPAREAPPLY') `
      + (Note 'verify-3') + @('COMPAREVERIFY', 'Applied') `
      + (Note 'decide-unresolved') + @('FTFCOMPAREDECIDE', 'Unresolved') `
      + (Note 'apply-4') + @('FTFCOMPAREAPPLY') `
      + (Note 'save') + @('FTFCOMPARESAVE') `
      + (Note 'saveas') + @('SAVEAS', '2018', "`"$(Join-Path $work 'outcompare.dwg')`"")
    [IO.File]::WriteAllLines($scr, [string[]]$lines, [Text.Encoding]::ASCII)

    $log = Join-Path $work 'compare_out.log'
    & (Join-Path $here 'run-accore.ps1') -Drawing $seed -Script $scr -Log $log -TimeoutSeconds $SmokeTimeoutSeconds | Out-Null
    if (-not (Test-Path -LiteralPath $log)) { Fail 'accoreconsole produced no log.' @("Expected $log") }
    $logLines = @(Get-Content $log)

    $pass = @($logLines | Select-String -SimpleMatch '[VERIFY PASS]').Count
    $failLines = @($logLines | Select-String -SimpleMatch '[VERIFY FAIL]' | ForEach-Object { $_.Line.Trim() })
    $verifyRuns = @($logLines | Select-String -SimpleMatch 'COMPAREVERIFY: ').Count
    $verifyClean = @($logLines | Select-String -SimpleMatch 'COMPAREVERIFY: 0 failure').Count
    $loadProblems = @($logLines | Select-String 'Cannot load assembly|Unknown command|could not be loaded|Could not load|FileLoadException|FileNotFoundException|BadImageFormatException|TypeLoadException|MissingMethodException|TypeInitializationException' | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique)
    $seeded = @($logLines | Select-String -SimpleMatch 'COMPARESEED: consultant xref').Count -gt 0
    # Case-sensitive on purpose: the first FTFCOMPAREQC in this sequence is expected to
    # report "Final QC: FAILED" (an undecided discrepancy); that is the tool working.
    $commandErrors = @($logLines | Select-String -CaseSensitive ' failed: |AutoCAD error|NOT changed' | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique)
    $finalQc = @($logLines | Select-String -SimpleMatch 'Final QC: ' | ForEach-Object { $_.Line.Trim() }) | Select-Object -Last 1
    $deliverable = Join-Path $outFolder 'seedcompare-ACC.dwg'
    $saved = Test-Path -LiteralPath $deliverable
    $audit = @($logLines | Select-String -SimpleMatch 'CLEAN: nothing from the comparison').Count
    $exitLine = @($logLines | Select-String '^accoreconsole( exit code:|: TIMEOUT)' | ForEach-Object { $_.Line.Trim() }) | Select-Object -Last 1

    Write-Host ''
    Write-Host ("   harness seeded the drawing: {0}" -f $(if ($seeded) { 'yes' } else { 'NO (NETLOAD or COMPARESEED did not run)' }))
    Write-Host ("   checks passed             : {0}" -f $pass)
    Write-Host ("   checks failed             : {0}" -f $failLines.Count)
    Write-Host ("   verify runs clean         : {0} of {1} (expected 3 of 3)" -f $verifyClean, $verifyRuns)
    Write-Host ("   audit clean after restore : {0}" -f $(if ($audit -gt 0) { 'yes' } else { 'no' }))
    Write-Host ("   last final QC             : {0}" -f $(if ($finalQc) { $finalQc } else { 'not run' }))
    Write-Host ("   deliverable written       : {0}" -f $(if ($saved) { $deliverable } else { 'NO' }))
    Write-Host ("   accoreconsole             : {0}" -f $(if ($exitLine) { $exitLine } else { 'exit code unknown' }))
    Write-Host ("   log                       : {0}" -f $log)

    $problems = @()
    if ($loadProblems.Count -gt 0) { $problems += 'The plugin or harness did not load in accoreconsole:'; $problems += $loadProblems }
    if ($verifyRuns -lt 3) { $problems += "Only $verifyRuns verify run(s) completed; a command stopped the script (look for the last command in the log)." }
    if ($failLines.Count -gt 0) { $problems += 'Failed checks:'; $problems += $failLines }
    if ($commandErrors.Count -gt 0) { $problems += 'Errors reported by commands:'; $problems += ($commandErrors | Select-Object -First 15) }
    if (-not $saved) { $problems += 'FTFCOMPARESAVE did not write the deliverable (the last final QC failed, or the save command did not run).' }
    if ($audit -eq 0) { $problems += 'FTFCOMPAREAUDIT did not report a clean drawing after FTFCOMPARERESTORE.' }
    $crashed = $exitLine -and ($exitLine -match 'TIMEOUT' -or ($exitLine -match 'exit code: (-?\d+)$' -and $Matches[1] -ne '0'))
    if ($crashed) { $problems += "accoreconsole did not exit normally ($exitLine): a command crashed it; compare-progress.txt names the last step that ran." }
    if ($problems.Count -gt 0) {
        Write-Host ''
        Show-FirstRun $log
        Show-RunEvidence $log
        Fail 'The smoke test did not pass. Nothing in FTFCOMPARE should be used on a project until it does.' $problems
    }

    Ok "Smoke test passed: $pass checks, 3 clean verify runs, drawing clean after restore, deliverable written."
}

Write-Host ''
Write-Host 'All requested steps completed.' -ForegroundColor Green
Write-Host 'Next: open a COPY of a project survey drawing in Civil 3D and follow FTFCOMPARE-SETUP.md, step 5.'
