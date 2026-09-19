# Headless live-smoke run of FTFRECORD in real Civil 3D via accoreconsole.
# Reads the synthetic King County short plat fixture through the Sidecar engine (no OCR
# needed), approves every call at or above the review threshold, starts Lot 1 at 500,500 and
# lets Lot 2 sit on the shared line, builds, checks, labels, rebuilds and checks again.
# Output: record_out.log, outrecord.dwg, outrecord.<project>.ftfrecord.json and the QC report.
# The settings beside the seed (ftf-settings.json) create the missing layers so the stock NCS
# template can take the geometry, and place plain-text labels (no Civil 3D label style is named).
# Civil 3D must be closed.

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$template = "$env:LOCALAPPDATA\Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt"

dotnet build (Join-Path $here '..\..\FieldToFinish.sln') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'plugin build failed' }

foreach ($stale in @("$here\outrecord.dwg", "$here\seedrecord.dwg")) {
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale }
}
Get-ChildItem $here -Filter 'seedrecord.*.ftfrecord.json' | Remove-Item
Copy-Item -LiteralPath $template -Destination "$here\seedrecord.dwg"

& (Join-Path $here 'run-accore.ps1') -Drawing "$here\seedrecord.dwg" -Script "$here\runrecord.scr" -Log "$here\record_out.log" -TimeoutSeconds 240

$c = Get-Content "$here\record_out.log"
Write-Host ("record_out.log: built {0}, MATCH {1}, REVIEW {2}, MISSING {3}, blocked {4}, command errors {5}" -f
    ($c | Select-String 'built for REC-').Count,
    ($c | Select-String ': MATCH').Count, ($c | Select-String 'REVIEW').Count, ($c | Select-String 'MISSING').Count,
    ($c | Select-String 'BLOCKED:').Count,
    ($c | Select-String ' failed: |AutoCAD error|rules file problem').Count)
# Expected on the fixture: 2 builds (FTFRECORD, FTFRECORDREBUILD), every course MATCH, no MISSING, nothing blocked.
