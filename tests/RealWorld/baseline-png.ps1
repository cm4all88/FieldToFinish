# Plots PNGs of saved exhibit drawings (copies) for a visual before/after comparison: view-<layout>.png beside each drawing.
#   baseline-png.ps1 -Root <folder with one sub-folder per case, each holding *-FTF.dwg and report.txt>
param([string]$Root = 'C:\dev\FTF-RealWorld\baseline-2026-09-16')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = Join-Path (Resolve-Path (Join-Path $here '..\..')) 'src\FieldCodes.Cad\bin\Debug\net48'
$runner = Join-Path $here '..\LiveSmoke\run-accore.ps1'
foreach ($case in Get-ChildItem -LiteralPath $Root -Directory) {
    $dwg = Get-ChildItem -LiteralPath $case.FullName -Filter '*-FTF.dwg' | Select-Object -First 1
    $report = Join-Path $case.FullName 'report.txt'
    if (-not $dwg -or -not (Test-Path -LiteralPath $report)) { continue }
    $layouts = @(Select-String -LiteralPath $report -Pattern '^EXHIBIT (.+?): 1"' | ForEach-Object { $_.Matches[0].Groups[1].Value })
    $lines = @('NETLOAD "' + "$bin\FieldCodes.Cad.dll" + '"', 'NETLOAD "' + "$here\RealWorldHarness.dll" + '"')
    foreach ($l in $layouts) { $lines += 'RWPNG'; $lines += $l; $lines += (Join-Path $case.FullName ('view-' + ($l -replace '[^A-Za-z0-9]+', '-') + '.png')) }
    $copy = Join-Path $env:TEMP ('ftf-png-' + [guid]::NewGuid().ToString('N') + '.dwg')
    Copy-Item -LiteralPath $dwg.FullName -Destination $copy
    $scr = [IO.Path]::ChangeExtension($copy, '.scr')
    Set-Content -LiteralPath $scr -Encoding utf8 -Value $lines
    & $runner -Drawing $copy -Script $scr -Log ([IO.Path]::ChangeExtension($copy, '.log')) -TimeoutSeconds 300 -Profile 'PMX Survey Civil3D 2024' | Out-Null
    Remove-Item -LiteralPath $copy, $scr -ErrorAction SilentlyContinue
    Write-Host ($case.Name + ': ' + (@(Get-ChildItem -LiteralPath $case.FullName -Filter 'view-*.png')).Count + ' png')
}
