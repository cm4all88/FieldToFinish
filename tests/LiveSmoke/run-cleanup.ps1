# Headless live-smoke run of the production cleanup pass (curved ties, lot lines, exhibit hatch scale, viewport
# content, stamp, wording, grouped QA) on state plane coordinates. Output: clean_out.log.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$template = "$env:LOCALAPPDATA\Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt"
$bin = Join-Path $here '..\..\src\FieldCodes.Cad\bin\Debug\net48'
$profile = Join-Path $env:APPDATA 'FieldToFinish\profiles\LIVESMOKE CLEANUP.json'

& $csc /nologo /target:library /out:"$here\CleanupTestHarness.dll" "$here\CleanupTestHarness.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" /r:"$bin\FieldCodes.dll" /r:System.Core.dll `
    /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
if ($LASTEXITCODE -ne 0) { throw 'cleanup harness compile failed' }

Copy-Item -LiteralPath $template -Destination "$here\seedclean.dwg" -Force
& (Join-Path $here 'run-accore.ps1') -Drawing "$here\seedclean.dwg" -Script "$here\runcleanup.scr" -Log "$here\clean_out.log" -TimeoutSeconds 240
if (Test-Path -LiteralPath $profile) { Remove-Item -LiteralPath $profile }

# The QA summary is text on the command line: check its grouping here.
$c = Get-Content "$here\clean_out.log"
$text = $c -join "`n"
$extra = @()
foreach ($want in 'Survey content (geometry, source data, legal reproduction):', 'Drafting, sheet and manual review:', 'GEOMETRY --', 'SOURCE DATA --', 'LEGAL REPRODUCTION --', 'DRAFTING --', 'SHEET / PLOT --', 'MANUAL REVIEW --', 'commencement tie follows the drawn line', 'READY FOR SURVEYOR REVIEW') {
    $extra += if ($text.Contains($want)) { "[PASS] QA text has: $want" } else { "[FAIL] QA text lacks: $want" }
}
$extra += if ($text -match 'APPROVED') { "[FAIL] QA text says APPROVED" } else { "[PASS] QA text never says APPROVED" }
$extra += if ($text.Contains('enclose 2 separate areas')) { "[PASS] the ambiguous lot selection stopped with its reason" } else { "[FAIL] the ambiguous lot selection did not stop with a reason" }
$extra | ForEach-Object { Write-Host "  $_" }
Write-Host ("clean_out.log: PASS {0}, FAIL {1}, command errors {2}" -f
    (($c | Select-String '\[PASS\]').Count + ($extra | Where-Object { $_ -like '[[]PASS*' }).Count),
    (($c | Select-String '\[FAIL\]').Count + ($extra | Where-Object { $_ -like '[[]FAIL*' }).Count),
    ($c | Select-String ' failed: |AutoCAD error|rules file problem').Count)
