# Headless live-smoke run of the production tools (Dip Builder command-line path
# and STRIPEASEMENT) in real AutoCAD/Civil 3D via accoreconsole.
#   Run 1: seed, notes, connections, drafting, undo, labels, two easements, a
#          survey revision with rebuilds, undo of each rebuild, profile, SAVEAS.
#   Run 2: reopen the saved drawing, verify everything persisted, exports, checks.
# Output: prod_out.log, prod_out2.log, outprod.dwg and the files written beside it.
# Civil 3D must be closed. The test profile it creates is removed afterwards.

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '..\..\deploy\TraceEnv.ps1')
$acad = Find-Civil3D2024
if (-not $acad) { throw 'Civil 3D 2024 not found. Set the C3D2024 environment variable to its install folder.' }
$csc = Get-TraceCsc
$template = "$env:LOCALAPPDATA\Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt"
$bin = Join-Path $here '..\..\src\FieldCodes.Cad\bin\Debug\net48'
$profile = Join-Path $env:APPDATA 'FieldToFinish\profiles\LIVESMOKE CLIENT.json'
$wide = Join-Path $env:APPDATA 'FieldToFinish\profiles\LIVESMOKE WIDE.json'

# Internal-edition test: builds FieldCodes.dll / FieldCodes.Cad.dll
dotnet build (Join-Path $here '..\..\FieldToFinish.sln') -p:TraceEdition=Parametrix | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'plugin build failed' }

& $csc /nologo /target:library /out:"$here\ProductionTestHarness.dll" "$here\ProductionTestHarness.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" `
    /r:"$acad\C3D\AeccDbMgd.dll" /r:"$bin\FieldCodes.dll" /r:System.Core.dll `
    /r:"$(Split-Path -Parent $csc)\netstandard.dll"
if ($LASTEXITCODE -ne 0) { throw 'harness compile failed' }

foreach ($stale in @("$here\outprod.dwg", "$here\seedprod.dwg", $profile, $wide)) {
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale }
}
Copy-Item -LiteralPath $template -Destination "$here\seedprod.dwg"

& (Join-Path $here 'run-accore.ps1') -Drawing "$here\seedprod.dwg" -Script (Expand-TraceScript -Template "$here\runprod.scr" -Output "$here\runprod.generated.scr") -Log "$here\prod_out.log" -TimeoutSeconds 240
if (Test-Path "$here\outprod.dwg") {
    & (Join-Path $here 'run-accore.ps1') -Drawing "$here\outprod.dwg" -Script (Expand-TraceScript -Template "$here\runprod2.scr" -Output "$here\runprod2.generated.scr") -Log "$here\prod_out2.log" -TimeoutSeconds 180
}
if (Test-Path -LiteralPath $profile) { Remove-Item -LiteralPath $profile }
if (Test-Path -LiteralPath $wide) { Remove-Item -LiteralPath $wide }

foreach ($log in 'prod_out.log', 'prod_out2.log') {
    $c = Get-Content "$here\$log"
    Write-Host ("{0}: PASS {1}, FAIL {2}, clean verify runs {3}, command errors {4}" -f $log,
        ($c | Select-String '\[PASS\]').Count, ($c | Select-String '\[FAIL\]').Count,
        ($c | Select-String 'PRODVERIFY: 0 failure').Count,
        ($c | Select-String ' failed: |AutoCAD error|rules file problem').Count)
}

# The production cleanup pass: curved ties, lot lines, exhibit hatch scale, viewport content, stamp, grouped QA.
& (Join-Path $here 'run-cleanup.ps1')
