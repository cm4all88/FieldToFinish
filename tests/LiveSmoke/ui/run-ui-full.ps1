# Rebuilds the UI harness, installs the plugin, stages the harness in the trusted
# bundle folder, runs the Civil 3D window test, then removes the harness again.
param([ValidateSet('Dips', 'Easements', 'All')][string]$Suite = 'All')
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$smoke = Split-Path -Parent $here
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
$bin = Join-Path $smoke '..\..\src\FieldCodes.Cad\bin\Debug\net48'
$bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024'

if (Get-Process acad -ErrorAction SilentlyContinue) { Write-Host 'Civil 3D is running -- close it first.'; exit 1 }

& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:library `
    /out:"$smoke\DipUiTestHarness.dll" "$smoke\DipUiTestHarness.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" /r:"$acad\C3D\AeccDbMgd.dll" `
    /r:"$bin\FieldCodes.dll" /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
if ($LASTEXITCODE -ne 0) { Write-Host 'harness compile failed'; exit 1 }

& (Join-Path $smoke '..\..\deploy\install-dev.ps1') | Select-Object -Last 1
Copy-Item -LiteralPath "$smoke\DipUiTestHarness.dll" -Destination $bundle -Force
Get-ChildItem $here -Filter '*.png' | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
Copy-Item -LiteralPath "$env:LOCALAPPDATA\Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt" -Destination "$here\dip-ui-test.dwg" -Force

# Both suites in one session need roughly twice as long as one.
$minutes = 9
if ($Suite -eq 'All') { $minutes = 18 }
& (Join-Path $here 'run-ui.ps1') -TimeoutMinutes $minutes -Suite $Suite

for ($i = 0; $i -lt 12 -and (Get-Process acad -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 5 }
Remove-Item -LiteralPath "$bundle\DipUiTestHarness.dll" -ErrorAction SilentlyContinue
Get-Content "$here\ui-test.log" | Select-String 'FAIL|STUCK|DONE|threw|clipped:'
