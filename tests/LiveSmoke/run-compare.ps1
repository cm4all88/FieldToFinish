# Headless live-smoke run of FTFCOMPARE in real Civil 3D via accoreconsole.
#   COMPARESEED builds a consultant xref + survey; FTFCOMPARERUN / APPLY / QC /
#   RESTORE / SAVE are driven in sequence and COMPAREVERIFY checks the plotting
#   state after each step. Output: compare_out.log, outcompare.dwg, compare-out\.
# Civil 3D must be closed.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$template = "$env:LOCALAPPDATA\Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt"

dotnet build (Join-Path $here '..\..\FieldToFinish.sln') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'plugin build failed' }

& $csc /nologo /target:library /out:"$here\CompareTestHarness.dll" "$here\CompareTestHarness.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" `
    /r:"$acad\C3D\AeccDbMgd.dll" /r:System.Core.dll
if ($LASTEXITCODE -ne 0) { throw 'harness compile failed' }

foreach ($stale in @("$here\outcompare.dwg", "$here\seedcompare.dwg", "$here\compare-consultant.dwg", "$here\seedcompare.ftf-compare.csv", "$here\seedcompare.ftf-compare-result.json", "$here\seedcompare.ftf-compare-decisions.json")) {
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale }
}
if (Test-Path "$here\compare-out") { Remove-Item "$here\compare-out" -Recurse -Force }
Copy-Item -LiteralPath $template -Destination "$here\seedcompare.dwg"

& (Join-Path $here 'run-accore.ps1') -Drawing "$here\seedcompare.dwg" -Script "$here\runcompare.scr" -Log "$here\compare_out.log" -TimeoutSeconds 300

$c = Get-Content "$here\compare_out.log"
Write-Host ("compare_out.log: PASS {0}, FAIL {1}, clean verify runs {2}, command errors {3}" -f
    ($c | Select-String '\[PASS\]').Count, ($c | Select-String '\[FAIL\]').Count,
    ($c | Select-String 'COMPAREVERIFY: 0 failure').Count,
    ($c | Select-String ' failed: |AutoCAD error').Count)
Write-Host "Deliverable expected under $here\compare-out (QC must have passed for it to exist)."
