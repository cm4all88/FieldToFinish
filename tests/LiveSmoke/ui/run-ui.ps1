# Launches full Civil 3D on the UI test drawing, runs the Dip Builder window test,
# and waits for the harness to report DONE. Only the Dip Builder window is ever
# captured (by the harness); the desktop is never screenshotted.
param([int]$TimeoutMinutes = 20, [ValidateSet('Dips', 'Easements', 'All')][string]$Suite = 'All')

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$command = @{ Dips = 'DIPUITEST'; Easements = 'ESMTUITEST'; All = 'ALLUITEST' }[$Suite]
$harness = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024\DipUiTestHarness.dll'
$lines = @('NETLOAD "' + $harness + '"')
if ($Suite -ne 'Easements') { $lines += 'UISEED' }
$lines += 'ZOOM E', $command
[IO.File]::WriteAllLines((Join-Path $here 'run-ui.scr'), $lines)
$log = Join-Path $here 'ui-test.log'
if (Test-Path $log) { Remove-Item -LiteralPath $log }

$acad = 'C:\Program Files\Autodesk\AutoCAD 2024\acad.exe'
$arguments = '"' + (Join-Path $here 'dip-ui-test.dwg') + '" /product C3D /language en-US /nologo /b "' + (Join-Path $here 'run-ui.scr') + '"'
$p = Start-Process $acad -ArgumentList $arguments -PassThru

$start = Get-Date
while ((Get-Date) - $start -lt [TimeSpan]::FromMinutes($TimeoutMinutes)) {
    Start-Sleep -Seconds 15
    if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'UI TEST DONE' -Quiet)) { Write-Host 'DONE'; break }
    if ($p.HasExited) { Write-Host 'acad exited'; break }
}
if (-not ((Test-Path $log) -and (Select-String -Path $log -Pattern 'UI TEST DONE' -Quiet))) { Write-Host 'NOT DONE' }

# Close only the Civil 3D instance this script started; the test drawing is throwaway
# and was saved by the test before it reopened it.
Start-Sleep -Seconds 5
if (-not $p.HasExited) { Stop-Process -Id $p.Id }
