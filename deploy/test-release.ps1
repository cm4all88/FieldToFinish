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
$regRoot = 'HKCU:\Software\FTF-Test\Installed'       # stands in for Civil 3D's own keys
$regProducts = 'HKCU:\Software\FTF-Test\Products'    # stands in for the machine's product list
$out = Join-Path $scratch 'out'
$plugins = Join-Path $scratch 'ApplicationPlugins'
$appdata = Join-Path $scratch 'AppData'
New-Item -ItemType Directory -Force -Path $out, $plugins, $appdata | Out-Null

# One Civil 3D and one plain AutoCAD: the registration must go to the first and not the second.
Remove-Item 'HKCU:\Software\FTF-Test' -Recurse -Force -ErrorAction SilentlyContinue
foreach ($pair in @(@('R24.3\ACAD-7100:409', 'Autodesk Civil 3D 2024 - English'), @('R24.3\ACAD-7101:409', ''))) {
    New-Item -Path (Join-Path $regRoot $pair[0]) -Force | Out-Null
    New-Item -Path (Join-Path $regProducts $pair[0]) -Force | Out-Null
    if ($pair[1]) { New-ItemProperty -Path (Join-Path $regProducts $pair[0]) -Name ProductName -Value $pair[1] -PropertyType String -Force | Out-Null }
}
$civilKey = Join-Path $regRoot 'R24.3\ACAD-7100:409\Applications\FieldToFinish'
$otherKey = Join-Path $regRoot 'R24.3\ACAD-7101:409\Applications\FieldToFinish'

try
{
    # --- package ------------------------------------------------------------------
    Write-Host "-- packaging a $Configuration setup folder"
    & (Join-Path $PSScriptRoot 'make-release.ps1') -Configuration $Configuration -OutputRoot $out | Out-Null
    $setup = Join-Path $out 'FieldToFinish-Setup'
    Check (Test-Path $setup) 'make-release.ps1 writes a setup folder'
    if (-not (Test-Path $setup)) { throw 'nothing to test' }

    foreach ($name in @('Install FTF.bat', 'Uninstall FTF.bat', 'Check FTF.bat', 'Repair FTF.bat',
                        'install.ps1', 'check.ps1', 'README.txt', 'version.txt'))
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
    $batchRaw = [IO.File]::ReadAllText((Join-Path $setup 'Install FTF.bat'))
    Check ($batchRaw -match "`r`n") 'the batch file keeps Windows line endings -- with LF a goto can miss its label'

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
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts | Out-Null
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
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts | Out-Null
        Check ($LASTEXITCODE -eq 0) 'installing over an existing install works'
        Check (-not (Test-Path $stale)) 'and clears out files from the older build'

        # --- an incomplete copy, which is what a half-copied network folder looks like
        $broken = Join-Path $scratch 'broken'
        New-Item -ItemType Directory -Force -Path $broken | Out-Null
        Copy-Item (Join-Path $setup 'install.ps1') -Destination $broken -Force
        $told = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $broken 'install.ps1') -TargetRoot (Join-Path $scratch 'nowhere') 2>&1
        Check ($LASTEXITCODE -eq 1) 'an incomplete setup folder stops with an error'
        Check (($told -join ' ') -match 'incomplete') "and says what is wrong ($(($told | Where-Object { $_ -match 'incomplete' } | Select-Object -First 1)))"

        # --- arriving from somewhere else ---------------------------------------
        # A setup folder that came by email or download is marked by Windows, and Civil 3D
        # will not load a marked plugin. Mark one the same way and check it installs clean.
        Write-Host '-- a setup folder that came from another computer'
        $marked = Join-Path $setup 'Bundle\Contents\2024\FieldCodes.Cad.dll'
        Set-Content -Path ($marked + ':Zone.Identifier') -Value "[ZoneTransfer]`r`nZoneId=3" -ErrorAction SilentlyContinue
        $wasMarked = [bool](Get-Item $marked -Stream Zone.Identifier -ErrorAction SilentlyContinue)
        Check $wasMarked 'the test could mark the file the way a download does'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts -Source $setup | Out-Null
        $stillMarked = [bool](Get-Item (Join-Path $installed 'Contents\2024\FieldCodes.Cad.dll') -Stream Zone.Identifier -ErrorAction SilentlyContinue)
        Check (-not $stillMarked) 'installing clears the mark, so Civil 3D will load it'

        # --- where updates come from --------------------------------------------
        # Installing records the setup folder it came from, so FTF can compare itself
        # against it later. That recorded path is the whole update mechanism.
        Write-Host '-- the office copy it came from'
        $recorded = Join-Path $installed 'source.txt'
        Check (Test-Path $recorded) 'installing records where it was installed from'
        if (Test-Path $recorded)
        {
            Check ((Get-Content $recorded -First 1).TrimEnd('\') -eq $setup.TrimEnd('\')) `
                  ("the recorded folder is the setup folder (" + (Get-Content $recorded -First 1) + ")")
        }

        # A staging folder under TEMP is where the batch file copied itself, not somewhere
        # to come back to for updates.
        $staged = Join-Path $env:TEMP 'FTF-Setup'
        if (Test-Path $staged) { Remove-Item $staged -Recurse -Force }
        Copy-Item $setup -Destination $staged -Recurse -Force
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $staged 'install.ps1') -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts | Out-Null
        Check (-not (Test-Path $recorded)) 'a copy run from TEMP records no office copy, so nothing points at a staging folder'
        Remove-Item $staged -Recurse -Force -ErrorAction SilentlyContinue

        # --- telling Civil 3D to load it ------------------------------------------
        # Civil 3D writes this record itself after reading a bundle, but only on a machine
        # that scans the plugin folder. Some do not, and then FTF is installed and never
        # loads. Installing writes the same record directly.
        Write-Host '-- telling Civil 3D to load it at startup'
        Check (Test-Path $civilKey) 'installing tells Civil 3D to load FTF at startup'
        if (Test-Path $civilKey) {
            $reg = Get-ItemProperty $civilKey
            Check ($reg.LOADCTRLS -eq 2 -and $reg.MANAGED -eq 1) ("the record says load it, and that it is .NET (LOADCTRLS=" + $reg.LOADCTRLS + ", MANAGED=" + $reg.MANAGED + ")")
            Check ($reg.LOADER -eq (Join-Path $installed 'Contents\2024\FieldCodes.Cad.dll')) ("and points at the installed program (" + $reg.LOADER + ")")
            Check (Test-Path $reg.LOADER) 'which is really there'
        }
        Check (-not (Test-Path $otherKey)) 'plain AutoCAD is left alone -- it cannot load a Civil 3D plugin'

        # --- a copy that still updates from the office drive ---------------------
        # Installing from a local copy, or a zip somebody was emailed, must not cut the
        # machine off from updates. The package carries its home address.
        Write-Host '-- a local copy that updates from the office drive'
        $localCopy = Join-Path $scratch 'local-copy'
        Remove-Item $localCopy -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item $setup -Destination $localCopy -Recurse -Force
        $officeHome = 'U:\Somewhere\That\Is\Not\Mounted\FieldToFinish-Setup'
        Set-Content -Path (Join-Path $localCopy 'updates-from.txt') -Value $officeHome -Encoding utf8
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $localCopy 'install.ps1') -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts -Source $localCopy | Out-Null
        Check ($LASTEXITCODE -eq 0) 'installing from a local copy works'
        Check ((Get-Content $recorded -First 1) -eq $officeHome) `
              ("and it takes its updates from the office drive, not the local folder (" + (Get-Content $recorded -First 1) + ")")

        # Back to the real setup folder for the update tests below.
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts -Source $setup | Out-Null

        # --- an update applying itself -------------------------------------------
        # What FTF launches as Civil 3D closes: the same script, with -WaitForCivil3D.
        Write-Host '-- an update applying itself'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts -Source $setup | Out-Null
        $before = Get-Content (Join-Path $installed 'version.txt') -First 1

        $newer = 'FTF 2099-01-01 09:00  office deadbee  (Release, Civil 3D 2024)'
        Set-Content -Path (Join-Path $setup 'Bundle\version.txt') -Value $newer -Encoding utf8
        # -ProcessName is the test seam: nothing named this is running, so the wait ends at
        # once and the real Civil 3D -- which someone may well have open -- is left out of it.
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') `
            -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts -Source $setup -WaitForCivil3D -ProcessName 'ftf-nothing-runs-by-this-name' | Out-Null
        $after = Get-Content (Join-Path $installed 'version.txt') -First 1
        Check ($LASTEXITCODE -eq 0) 'the update installer finishes cleanly'
        Check ($before -ne $after -and $after -eq $newer) "the newer office build replaced the installed one ($after)"
        Check ((Get-Content $recorded -First 1).TrimEnd('\') -eq $setup.TrimEnd('\')) 'and the office copy is still recorded afterwards'
        Check ((Get-Content $officeRules -Raw) -match 'do not touch') 'an update leaves office settings alone too'
        # And the other half of that behaviour: while Civil 3D is open, nothing is replaced.
        # 'powershell' is certainly running -- this test is it -- so the wait gives up.
        Set-Content -Path (Join-Path $setup 'Bundle\version.txt') -Value 'FTF 2099-06-06 06:06  office cafe123  (Release, Civil 3D 2024)' -Encoding utf8
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') `
            -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts -Source $setup -WaitForCivil3D -WaitMinutes 0 -ProcessName 'powershell' | Out-Null
        Check ($LASTEXITCODE -eq 0) 'an update that cannot apply yet is not an error'
        Check ((Get-Content (Join-Path $installed 'version.txt') -First 1) -eq $newer) `
              'a session still open is never replaced underneath -- the update waits for the next close'

        $log = Join-Path $env:TEMP 'FTF-update.log'
        Check (Test-Path $log) 'the unattended update writes a log, since nobody is watching'
        if (Test-Path $log)
        {
            Check ((Get-Content $log -Raw) -match 'installed for') 'and the log says what it did'
        }

        # --- a source that goes wrong halfway ------------------------------------
        # The setup folder is normally on a network drive. If it becomes unreadable while
        # installing, the FTF already on the machine has to survive: losing it overnight
        # and finding the server copy intact is exactly the failure to design out.
        Write-Host '-- an install from a source that breaks'
        $good = Get-Content (Join-Path $installed 'version.txt') -First 1
        $broken = Join-Path $scratch 'broken-source'
        Remove-Item $broken -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item $setup -Destination $broken -Recurse -Force
        Remove-Item (Join-Path $broken 'Bundle\Contents\2024\FieldCodes.Cad.dll') -Force
        $said = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $broken 'install.ps1') -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts -Source $broken 2>&1
        Check ($LASTEXITCODE -eq 1) 'an install from a broken source fails rather than half-installing'
        Check (Test-Path (Join-Path $installed 'Contents\2024\FieldCodes.Cad.dll')) `
              'and the FTF already on the machine is still there'
        Check ((Get-Content (Join-Path $installed 'version.txt') -First 1) -eq $good) 'still the same build'
        Check ((($said -join ' ') -match 'still installed')) 'and it says so, instead of leaving someone guessing'
        Check (-not (Test-Path ($installed + '.new'))) 'no half-copied folder is left behind'
        Check (-not (Test-Path ($installed + '.old'))) 'and no old folder either'

        # --- uninstall -----------------------------------------------------------
        Write-Host '-- uninstalling'
        Check (Test-Path $civilKey) 'the startup record is there before uninstalling'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'install.ps1') -Uninstall -TargetRoot $plugins -RegistryRoot $regRoot -ProductRoot $regProducts | Out-Null
        Check ($LASTEXITCODE -eq 0) 'uninstalling reports success'
        Check (-not (Test-Path (Join-Path $plugins 'FieldToFinish.bundle'))) 'the bundle is gone'
        Check ((Get-Content $officeRules -Raw) -match 'do not touch') 'office settings survive uninstalling'
        Check (-not (Test-Path $civilKey)) 'and Civil 3D is told to stop loading it, so nothing is left pointing at files that are gone'
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
    Remove-Item 'HKCU:\Software\FTF-Test' -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ("release setup: PASS {0}, FAIL {1}" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
