<#
.SYNOPSIS
    Builds TRACE and installs it for the current user. One step, nothing else.

.DESCRIPTION
    TRACE has its own commands, its own stored-data names, its own settings folder
    (%APPDATA%\TRACE) and its own bundle (TRACE.bundle), so it runs beside FieldToFinish
    without setting anything aside. FieldToFinish.bundle is not touched.

    Close Civil 3D first. Then start it: the TRACE tab and palette are there.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\deploy\Install-Trace.ps1
    powershell -ExecutionPolicy Bypass -File .\deploy\Install-Trace.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$Civil3DPath,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TraceEnv.ps1')
$repo = Get-TraceRepoRoot

if (Get-Process -Name acad -ErrorAction SilentlyContinue) {
    Write-Error 'Civil 3D is running. Close it, then run this again.'
}

if ($Uninstall) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'deploy\install-dev.ps1') -Edition Default -Uninstall
    return
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'The .NET SDK is not installed. Get the .NET 8 SDK from https://dotnet.microsoft.com/download'
}
$c3d = Find-Civil3D2024 -Override $Civil3DPath
if (-not $c3d) {
    Write-Error 'Civil 3D 2024 was not found. Run again with -Civil3DPath "<folder that contains acad.exe>".'
}

Write-Host 'Building TRACE...'
& dotnet build (Join-Path $repo 'src\FieldCodes.Cad\FieldCodes.Cad.csproj') -c Debug -p:TraceEdition=Default "-p:C3D2024=$c3d" -nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Error 'The build failed. Send me the red error lines above.' }

Write-Host 'Installing TRACE...'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'deploy\install-dev.ps1') '-Edition', 'Default'
if ($LASTEXITCODE -ne 0) { Write-Error 'The install failed. Send me the red error lines above.' }

Write-Host ''
Write-Host 'Done. Start Civil 3D: you will see the TRACE tab and the TRACE palette.' -ForegroundColor Green
Write-Host 'FieldToFinish.bundle is not touched.'
