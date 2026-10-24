# Rebuilds the FTFROW / FTFLOTS window test harness, installs the plugin, stages the
# harness in the trusted bundle folder, runs the Civil 3D window test, then removes the
# harness again. Same shape as ui\run-ui-full.ps1, which does this for the Dip Builder.
#
# This drives a real Civil 3D GUI for a few minutes. Civil 3D must be closed first.
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$smoke = Split-Path -Parent $here
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
$bin = Join-Path $smoke '..\..\src\FieldCodes.Cad\bin\Debug\net48'
$bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024'

if (Get-Process acad -ErrorAction SilentlyContinue) { Write-Host 'Civil 3D is running -- close it first.'; exit 1 }

& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:library `
    /out:"$smoke\RowLotsUiTestHarness.dll" "$smoke\RowLotsUiTestHarness.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" /r:"$acad\C3D\AeccDbMgd.dll" `
    /r:"$bin\FieldCodes.dll" /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
if ($LASTEXITCODE -ne 0) { Write-Host 'harness compile failed'; exit 1 }

& (Join-Path $smoke '..\..\deploy\install-dev.ps1') | Select-Object -Last 1
Copy-Item -LiteralPath "$smoke\RowLotsUiTestHarness.dll" -Destination $bundle -Force
Get-ChildItem $here -Filter '*.png' | ForEach-Object { Remove-Item -LiteralPath $_.FullName }

# A fresh drawing beside this folder's ftf-settings.json, so the Right of Way and
# Platted Blocks pages point at layers the seed creates.
$template = "$env:LOCALAPPDATA\Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt"
if (-not (Test-Path $template)) { Write-Host "template not found: $template"; exit 1 }
Copy-Item -LiteralPath $template -Destination "$here\rowlots-ui-test.dwg" -Force

& (Join-Path $here 'run-rowlots-ui-acad.ps1') -TimeoutMinutes 12

for ($i = 0; $i -lt 12 -and (Get-Process acad -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 5 }
Remove-Item -LiteralPath "$bundle\RowLotsUiTestHarness.dll" -ErrorAction SilentlyContinue

$log = Join-Path $here 'rowlots-ui.log'
if (Test-Path $log) {
    Write-Host "`n--- failures and summary ---"
    Get-Content $log | Select-String 'FAIL|STUCK|DONE|threw|clipped:'
} else { Write-Host 'no log written' }
