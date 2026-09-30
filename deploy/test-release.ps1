<#
.SYNOPSIS
    Tests the setup folder the surveyors run: what is in it, what installing does, and
    what it must never touch.

.DESCRIPTION
    Packages a setup folder into a scratch directory, installs it into a scratch
    ApplicationPlugins folder with a scratch APPDATA, and checks the result. No real
    install is touched and Civil 3D is not needed.

.EXAMPLE
    .\test-release.ps1
#>
[CmdletBinding()]
param([ValidateSet('Release', 'Debug')][string]$Configuration = 'Debug')

$ErrorActionPreference = 'Continue'
$pass = 0
$fail = 0

function Check([bool]$ok, [string]$what)
{
    if ($ok) { $script:pass++; Write-Host "  [PASS] $what" }
    else { $script:fail++; Write-Host "  [FAIL] $what" }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ftf-release-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$out = Join-Path $scratch 'out'
$plugins = Join-Path $scratch 'ApplicationPlugins'
$appdata = Join-Path $scratch 'AppData'
New-Item -ItemType Directory -Force -Path $out, $plugins, $appdata | Out-Null

try
{
    # --- package ------------------------------------------------------------------
    Write-Host "-- packaging a $Configuration setup folder"
    & (Join-Path $PSScriptRoot 'make-release.ps1') -Configuration $Configuration -OutputRoot $out | Out-Null
    $setup = Join-Path $out 'FieldToFinish-Setup'
    Check (Test-Path $setup) 'make-release.ps1 writes a setup folder'
    if (-not (Test-Path $setup)) { throw 'nothing to test' }

    foreach ($name in @('Install FTF.bat', 'Uninstall FTF.bat', 'install.ps1', 'README.txt', 'version.txt'))
    {
        Check (Test-Path (Join-Path $setup $name)) "the surveyor gets $name"
    }

    $contents = Join-Path $setup 'Bundle\Contents\2024'
    Check (Test-Path (Join-Path $setup 'Bundle\PackageContents.xml')) 'the bundle manifest is there, so Civil 3D loads it on startup'
    Check (Test-Path (Join-Path $contents 'FieldCodes.Cad.dll')) 'the plugin itself is there'
    Check (Test-Path (Join-Path $contents 'FieldCodes.dll')) 'and the core library beside it'
    Check (Test-Path (Join-Path $contents 'rules.json')) 'the factory rules ship with it'

    $files = Get-ChildItem $contents -File
    $autodesk = @($files | Where-Object { $_.Name -match '^(Ac|Aecc)' })
    Check ($autodesk.Count -eq 0) ("no Autodesk assemblies are copied -- the host has its own (" + (($autodesk | ForEach-Object { $_.Name }) -join ', ') + ")")
    $symbols = @($files | Where-Object { $_.Extension -eq '.pdb' })
    Check ($symbols.Count -eq 0) 'no debug symbols in what goes out'

    $version = Get-Content (Join-Path $setup 'version.txt') -First 1
    Check ($version -match '^FTF \d{4}-\d{2}-\d{2}') "version.txt says which build this is ($version)"

    $batch = Get-Content (Join-Path $setup 'Install FTF.bat') -Raw
    Check ($batch -match 'ExecutionPolicy Bypass') 'the batch file runs even where PowerShell scripts are restricted'
    Check ($batch -match 'install\.ps1') 'and it calls the installer'
    Check ($batch -match 'pause') 'the window stays open long enough to read'

    # --- install ------------------------------------------------------------------
    # A scratch APPDATA with office settings already in it: installing must not touch them.
    Write-Host '-- installing into a scratch profile'
    $realAppData = $env:APPDATA
    $env:APPDATA = $appdata
    $officeRules = Join-Path $appdata 'FieldToFinish\rules.json'
    New-Item -ItemType Directory -Force -Path (Split-Path $officeRules) | Out-Null
    Set-Content -Path $officeRules -Value '{ "office": "do not touch" }' -Encoding utf8

    try
    {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') -TargetRoot $plugins | Out-Null
        $code = $LASTEXITCODE
        $installed = Join-Path $plugins 'FieldToFinish.bundle'
        Check ($code -eq 0) "installing reports success ($code)"
        Check (Test-Path (Join-Path $installed 'Contents\2024\FieldCodes.Cad.dll')) 'the plugin lands where Civil 3D looks for it'
        Check (Test-Path (Join-Path $installed 'PackageContents.xml')) 'with its manifest'
        Check (Test-Path (Join-Path $installed 'version.txt')) 'and the build it came from'
        Check ((Get-Content $officeRules -Raw) -match 'do not touch') 'office settings are untouched by installing'

        # A file left from an older FTF must not survive the next install.
        $stale = Join-Path $installed 'Contents\2024\OldThing.dll'
        Set-Content -Path $stale -Value 'x' -Encoding utf8
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') -TargetRoot $plugins | Out-Null
        Check ($LASTEXITCODE -eq 0) 'installing over an existing install works'
        Check (-not (Test-Path $stale)) 'and clears out files from the older build'

        # --- an incomplete copy, which is what a half-copied network folder looks like
        $broken = Join-Path $scratch 'broken'
        New-Item -ItemType Directory -Force -Path $broken | Out-Null
        Copy-Item (Join-Path $setup 'install.ps1') -Destination $broken -Force
        $told = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $broken 'install.ps1') -TargetRoot (Join-Path $scratch 'nowhere') 2>&1
        Check ($LASTEXITCODE -eq 1) 'an incomplete setup folder stops with an error'
        Check (($told -join ' ') -match 'incomplete') "and says what is wrong ($(($told | Where-Object { $_ -match 'incomplete' } | Select-Object -First 1)))"

        # --- uninstall -----------------------------------------------------------
        Write-Host '-- uninstalling'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') -Uninstall -TargetRoot $plugins | Out-Null
        Check ($LASTEXITCODE -eq 0) 'uninstalling reports success'
        Check (-not (Test-Path (Join-Path $plugins 'FieldToFinish.bundle'))) 'the bundle is gone'
        Check ((Get-Content $officeRules -Raw) -match 'do not touch') 'office settings survive uninstalling'
    }
    finally { $env:APPDATA = $realAppData }
}
catch
{
    $fail++
    Write-Host "  [FAIL] the test itself stopped: $($_.Exception.Message)"
}
finally
{
    Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ("release setup: PASS {0}, FAIL {1}" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
