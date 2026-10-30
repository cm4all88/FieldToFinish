# Headless live-smoke run of FTFCOMPARE in real Civil 3D via accoreconsole.
# Thin wrapper: the build-install-test script owns the smoke test (it generates the
# command script with the real paths, seeds the drawing, runs and judges the log).
#   .\run-compare.ps1              builds, runs the unit tests, runs the smoke test (no install)
#   .\run-compare.ps1 -SkipTests   skips the unit tests
param([switch]$SkipTests, [string]$C3D2024 = 'C:\Program Files\Autodesk\AutoCAD 2024')
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $here '..\..\deploy\build-install-test.ps1') -SkipInstall -SkipTests:$SkipTests -C3D2024 $C3D2024
