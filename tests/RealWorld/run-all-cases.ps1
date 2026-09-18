# Reruns every production-drawing case (on copies) and prints each case's QA verdict and command timings.
# Civil 3D must be closed. Originals in C:\dev\FTF-RealWorld\refs are copied, never opened for write.
param([string[]]$Only)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$refs = 'C:\dev\FTF-RealWorld\refs'
$cases = [ordered]@{
    'ref-104100'               = "$refs\silverlake\SV-2169171001-ESMT-28052700104100.dwg"
    'c2-104300-perm-temp'      = "$refs\silverlake\SV-2169171001-ESMT-28052700104300.dwg"
    'c3-kenmore-163-tce'       = "$refs\kenmore\dwg\SV-554-3744-009-EXH-0114100163.dwg"
    'c4-kenmore-710-curve-tce' = "$refs\kenmore\dwg\SV-554-3744-009-EXH-0114100710.dwg"
    'c5-104100-portion'        = "$refs\silverlake\SV-2169171001-ESMT-28052700104100.dwg"
    'c6-svba-heavy'            = "$refs\silverlake\2169171001-CreekSideLS-SVBA 20251118.dwg"
    'c7-kenmore-crowded'       = "$refs\kenmore\dwg\SV-554-3744-009-EXH-0114100163.dwg"
    'c8-titleblock-mapping'    = "$refs\silverlake\SV-2169171001-ESMT-28052700104100.dwg"
    'c9-104100-turned-view'    = "$refs\silverlake\SV-2169171001-ESMT-28052700104100.dwg"
}
& (Join-Path $here 'make-titleblock-test-profile.ps1') | Out-Null
foreach ($name in $cases.Keys) {
    if ($Only -and $Only -notcontains $name) { continue }
    & (Join-Path $here 'run-real.ps1') -Drawing $cases[$name] -Name $name -Commands (Join-Path $here "cases\$name.txt") -Office | Out-Null
}
foreach ($name in $cases.Keys) {
    if ($Only -and $Only -notcontains $name) { continue }
    $out = Join-Path 'C:\dev\FTF-RealWorld\runs' $name
    $clock = @(Select-String -LiteralPath (Join-Path $out 'run.log') -Pattern '^RWCLOCK (\S+) (\S+)' | ForEach-Object { $_.Matches[0].Groups[1].Value + '=' + $_.Matches[0].Groups[2].Value })
    $qa = @(Select-String -LiteralPath (Join-Path $out 'report.txt') -Pattern '^EXHIBIT (.+)$' | ForEach-Object { $_.Matches[0].Groups[1].Value })
    $pdf = @(Get-ChildItem -LiteralPath $out -Filter 'plot-*.pdf' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name + ' ' + $_.Length })
    "== $name"
    $qa | ForEach-Object { "  $_" }
    "  clock: " + ($clock -join ', ')
    "  pdf: " + ($pdf -join ', ')
}
