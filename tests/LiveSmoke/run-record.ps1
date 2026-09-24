# Headless live-smoke run of FTFRECORD in real Civil 3D via accoreconsole.
# Reads the synthetic King County short plat fixture through the Sidecar engine (no OCR
# needed), approves every call at or above the review threshold, starts Lot 1 at 500,500 (Lot 2
# is placed through the line the two share, so there is exactly one start prompt), builds,
# checks, labels, rebuilds and checks again.
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
$text = ($c -join "`n")

# The whole sequence on the fixture, step by step. A line saying a standard is MISSING from the
# drawing is not a failure: the stock template has no office monument or table layers, and those
# items are withheld on purpose, so only the QC summary counts missing courses.
$checks = @(
    @('read the plat',            ($text.Contains('SHORT PLAT NO. SP-2019-0042'))),
    @('calls approved headless',  ($text -match 'Headless review: 9 call\(s\) approved')),
    @('one start point asked',    (($text -match 'Start point of Lot 1') -and -not ($text -match 'Start point of Lot 2'))),
    @('built both lots',          ($text -match 'FTFRECORD: 7 line\(s\), 1 curve\(s\)')),
    @('Lot 1 closes exactly',     ($text -match 'Lot 1: 4 course\(s\), closure 0\.000')),
    @('Lot 2 closes 1:127,195',   ($text -match 'Lot 2: 5 course\(s\), closure 0\.005')),
    @('the curve matches',        ($text.Contains('Curve K8: Radius MATCH / Arc MATCH / Delta MATCH / Chord MATCH'))),
    @('shared boundary matches',  ($text.Contains('Shared boundary Lot 1 / Lot 2: MATCH'))),
    @('labels placed',            ($text -match 'FTFRECORDLABEL: 8 label\(s\)')),
    @('rebuilt the same',         ($text -match 'FTFRECORDREBUILD: 7 line\(s\), 1 curve\(s\)')),
    @('QC clean after build and rebuild', ([regex]::Matches($text, '12 match, 0 review, 0 missing, 0 error\(s\)')).Count -ge 2),
    @('nothing blocked',          (-not ($text -match 'BLOCKED:'))),
    @('no command errors',        (-not ($text -match ' failed: |AutoCAD error|rules file problem'))),
    @('project file written',     (Get-ChildItem $here -Filter 'seedrecord.*.ftfrecord.json').Count -ge 1),
    @('drawing saved',            (Test-Path -LiteralPath "$here\outrecord.dwg"))
)
foreach ($check in $checks) { Write-Host ("  [{0}] {1}" -f $(if ($check[1]) { 'PASS' } else { 'FAIL' }), $check[0]) }
Write-Host ("record_out.log: PASS {0}, FAIL {1}, withheld standards {2} (the stock template has no office monument/table layers)" -f
    ($checks | Where-Object { $_[1] }).Count,
    ($checks | Where-Object { -not $_[1] }).Count,
    ($c | Select-String 'WITHHELD:').Count)
