# Reruns every production-drawing case (on copies) and prints each case's QA verdict and command timings.
# Civil 3D must be closed. Originals in <Root>\refs are copied, never opened for write.
#
# The cases name real projects, parcels and clients, so they are kept OUTSIDE the repository (it is
# public), beside the drawings they run on:
#   <Root>\cases\cases.psd1   @{ '<case>' = 'refs\<folder>\<drawing>.dwg'; ... }
#   <Root>\cases\<case>.txt   the command lines for that case (format: cases\example-synthetic.txt here)
# export-real-cases.ps1 restores the original set from git history. Root defaults to C:\dev\FTF-RealWorld
# (or $env:FTF_REALWORLD).
param([string[]]$Only, [string]$Root = $(if ($env:FTF_REALWORLD) { $env:FTF_REALWORLD } else { 'C:\dev\FTF-RealWorld' }))
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifest = Join-Path $Root 'cases\cases.psd1'
if (-not (Test-Path -LiteralPath $manifest)) {
    throw "No case list at $manifest. Run export-real-cases.ps1 to restore the original cases, or write one (see cases\README.md here)."
}
$table = Import-PowerShellDataFile -LiteralPath $manifest
$cases = [ordered]@{}
foreach ($key in ($table.Keys | Sort-Object)) { $cases[$key] = Join-Path $Root $table[$key] }

& (Join-Path $here 'make-titleblock-test-profile.ps1') | Out-Null
foreach ($name in $cases.Keys) {
    if ($Only -and $Only -notcontains $name) { continue }
    & (Join-Path $here 'run-real.ps1') -Drawing $cases[$name] -Name $name -Commands (Join-Path $Root "cases\$name.txt") -Office | Out-Null
}
foreach ($name in $cases.Keys) {
    if ($Only -and $Only -notcontains $name) { continue }
    $out = Join-Path (Join-Path $Root 'runs') $name
    $clock = @(Select-String -LiteralPath (Join-Path $out 'run.log') -Pattern '^RWCLOCK (\S+) (\S+)' | ForEach-Object { $_.Matches[0].Groups[1].Value + '=' + $_.Matches[0].Groups[2].Value })
    $qa = @(Select-String -LiteralPath (Join-Path $out 'report.txt') -Pattern '^EXHIBIT (.+)$' | ForEach-Object { $_.Matches[0].Groups[1].Value })
    $pdf = @(Get-ChildItem -LiteralPath $out -Filter 'plot-*.pdf' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name + ' ' + $_.Length })
    "== $name"
    $qa | ForEach-Object { "  $_" }
    "  clock: " + ($clock -join ', ')
    "  pdf: " + ($pdf -join ', ')
}
