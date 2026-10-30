# Headless live-smoke run of FTFCONTOUR in real Civil 3D via accoreconsole.
# Builds a test TIN surface (a slope with two hills) plus a second surface, contours it at
# 12.6, 13.0 and 13.5 (and 99 and 5, which must report nothing), undoes once, runs again,
# re-runs 12.60 (must replace, not duplicate) and finally 13.0 inside a boundary with labels.
# After every step the harness checks that the surface's statistics and every triangle are
# exactly as seeded, that none of Civil 3D's own extracted polylines was left behind, and
# that every contour vertex sits on the surface at its contour elevation.
# Output: contour_out.log and outcontour.dwg. Civil 3D must be closed.

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$template = "$env:LOCALAPPDATA\Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt"

dotnet build (Join-Path $here '..\..\FieldToFinish.sln') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'plugin build failed' }

& $csc /nologo /target:library /out:"$here\ContourTestHarness.dll" "$here\ContourTestHarness.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" `
    /r:"$acad\C3D\AeccDbMgd.dll" /r:"$acad\AecBaseMgd.dll" /r:System.Core.dll
if ($LASTEXITCODE -ne 0) { throw 'contour harness compile failed' }

foreach ($stale in @("$here\outcontour.dwg", "$here\seedcontour.dwg")) {
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale }
}
Copy-Item -LiteralPath $template -Destination "$here\seedcontour.dwg"

& (Join-Path $here 'run-accore.ps1') -Drawing "$here\seedcontour.dwg" -Script "$here\runcontour.scr" -Log "$here\contour_out.log" -TimeoutSeconds 240

$c = Get-Content "$here\contour_out.log"
$text = $c -join "`n"
$extra = @()
$extra += if ($text.Contains('Surface untouched')) { '[PASS] FTFCONTOUR reported the surface untouched' } else { '[FAIL] no "Surface untouched" line in the FTFCONTOUR report' }
$extra += if ($text.Contains("none: above the surface's highest point")) { '[PASS] 99 reported above the surface' } else { '[FAIL] 99 not reported as above the surface' }
$extra += if ($text.Contains("none: below the surface's lowest point")) { '[PASS] 5 reported below the surface' } else { '[FAIL] 5 not reported as below the surface' }
$extra += if ($text.Contains('traced from the surface triangles')) { '[FAIL] ExtractContoursAt failed and the fallback tracer was used' } else { '[PASS] Civil 3D extraction used (no fallback)' }
$extra += if ($text -match 'WARNING') { '[FAIL] a WARNING was reported' } else { '[PASS] no warnings' }
$extra | ForEach-Object { Write-Host "  $_" }
Write-Host ("contour_out.log: PASS {0}, FAIL {1}, stages passed {2}/4, command errors {3}" -f
    (($c | Select-String '\[PASS\]').Count + ($extra | Where-Object { $_ -like '[[]PASS*' }).Count),
    (($c | Select-String '\[FAIL\]').Count + ($extra | Where-Object { $_ -like '[[]FAIL*' }).Count),
    ($c | Select-String 'CONTOURVERIFY \w+: PASS').Count,
    ($c | Select-String 'FTFCONTOUR: AutoCAD error|Nothing drawn\.|rolled back|rules problem').Count)
# Expected: every check PASS, 4/4 stages, 0 command errors.
