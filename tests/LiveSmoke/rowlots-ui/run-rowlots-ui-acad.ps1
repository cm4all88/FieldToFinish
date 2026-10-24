# Launches full Civil 3D on the window-test drawing, runs ROWUITEST, and waits for the
# harness to report DONE. Only the FTF windows are ever captured, by the harness itself;
# the desktop is never screenshotted.
param([int]$TimeoutMinutes = 12)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$harness = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024\RowLotsUiTestHarness.dll'
[IO.File]::WriteAllLines((Join-Path $here 'run-rowlots-ui.scr'), @(
    'NETLOAD "' + $harness + '"',
    'ROWUISEED',
    'ZOOM E',
    'ROWUITEST'
))

$log = Join-Path $here 'rowlots-ui.log'
if (Test-Path $log) { Remove-Item -LiteralPath $log }

$acad = 'C:\Program Files\Autodesk\AutoCAD 2024\acad.exe'
$arguments = '"' + (Join-Path $here 'rowlots-ui-test.dwg') + '" /product C3D /language en-US /nologo /b "' +
             (Join-Path $here 'run-rowlots-ui.scr') + '"'
$p = Start-Process $acad -ArgumentList $arguments -PassThru

$start = Get-Date
while ((Get-Date) - $start -lt [TimeSpan]::FromMinutes($TimeoutMinutes)) {
    Start-Sleep -Seconds 15
    if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'UI TEST DONE' -Quiet)) { Write-Host 'DONE'; break }
    if ($p.HasExited) { Write-Host 'acad exited'; break }
}
if (-not ((Test-Path $log) -and (Select-String -Path $log -Pattern 'UI TEST DONE' -Quiet))) { Write-Host 'NOT DONE' }

# Close only the Civil 3D this script started; the test drawing is throwaway.
Start-Sleep -Seconds 5
if (-not $p.HasExited) { Stop-Process -Id $p.Id }
