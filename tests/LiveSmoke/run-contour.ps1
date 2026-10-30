# Headless FTFCONTOUR smoke test in real Civil 3D (accoreconsole), for a developer build.
#
# Thin wrapper: deploy\smoke-test.ps1 does the work (UPDATEFTF.ps1 runs the same test on every
# build before it keeps it). It proves the DLL under test is the one Civil 3D loaded, that
# FTFVERSION and FTFCONTOUR are registered and actually ran, and then checks what FTFCONTOUR drew
# against a seeded TIN surface. An "Unknown command" fails the run at once.
#
#   .\run-contour.ps1                 builds Debug, tests src\FieldCodes.Cad\bin\Debug\net48\FieldCodes.Cad.dll
#   .\run-contour.ps1 -Installed      tests the installed bundle instead
#
# Civil 3D must be closed. When an FTF bundle is installed, accoreconsole loads THAT at startup, so
# a development DLL cannot be tested beside it: the test then fails and says so (use -Installed, or
# run UPDATEFTF.ps1, which installs and then tests).
param([switch]$Installed, [string]$C3D2024 = 'C:\Program Files\Autodesk\AutoCAD 2024')

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here '..\..')

if (Get-Process -Name acad -ErrorAction SilentlyContinue) { throw 'Close Civil 3D first.' }

if ($Installed) {
    $dll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024\FieldCodes.Cad.dll'
    $info = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle\build-info.json'
    if (-not (Test-Path $info)) { throw "No installed build with build-info.json at $info. Run UPDATEFTF.ps1." }
    $commit = (Get-Content $info -Raw | ConvertFrom-Json).commit
} else {
    dotnet build (Join-Path $repo 'src\FieldCodes.Cad\FieldCodes.Cad.csproj') -c Debug --nologo -v q "-p:C3D2024=$C3D2024"
    if ($LASTEXITCODE -ne 0) { throw 'plugin build failed' }
    $dll = Join-Path $repo 'src\FieldCodes.Cad\bin\Debug\net48\FieldCodes.Cad.dll'
    $commit = (git -C $repo rev-parse HEAD)
}

& (Join-Path $repo 'deploy\smoke-test.ps1') -PluginDll $dll -Commit $commit -C3D2024 $C3D2024
exit $LASTEXITCODE
