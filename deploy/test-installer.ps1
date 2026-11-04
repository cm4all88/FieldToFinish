<#
.SYNOPSIS
    Tests the staff installer (setup\install.ps1) and release publishing (publish-release.ps1)
    against synthetic releases, in scratch folders: no real install, profile, registry or Civil 3D
    is touched.

.DESCRIPTION
    Builds small stand-in FTF assemblies (FieldCodes.Cad.dll, FieldCodes.dll) stamped the way the
    real build stamps them ("1.0.0+<commit>.<channel>"), packages them as make-release.ps1 does
    (Bundle, PackageContents.xml, build-info.json with every file's SHA-256), and checks:

      clean install; no update needed (nothing rewritten); update; repair of a damaged install;
      settings kept (inside the bundle and in APPDATA); a release older than the installed build;
      duplicate copies of FTF found and switched off; integrity (a damaged, missing or extra file;
      no build-info.json; DLLs of another build); development builds refused; Civil 3D open;
      rollback when anything fails after the swap; restore of the previous FTF; check only;
      uninstall; logs and local tools; publishing an approved release, refusing anything else.

    Windows-only parts (Civil 3D's startup record in the registry) are checked on Windows only and
    reported as not run elsewhere. Needs the .NET SDK (dotnet) to build the stand-in assemblies.

.EXAMPLE
    pwsh -File deploy/test-installer.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$pass = 0
$fail = 0
$skipped = New-Object System.Collections.Generic.List[string]

function Check([bool]$ok, [string]$what)
{
    if ($ok) { $script:pass++; Write-Host "  [PASS] $what" }
    else { $script:fail++; Write-Host "  [FAIL] $what" }
}

$onWindows = ($PSVersionTable.PSEdition -eq 'Desktop') -or ($IsWindows -eq $true)
$sep = [IO.Path]::DirectorySeparatorChar
$repo = Split-Path -Parent $PSScriptRoot
$setupSource = Join-Path $PSScriptRoot 'setup'
# The PowerShell running this test, to run the installer in its own process (it ends with exit).
$shell = if ($PSVersionTable.PSEdition -eq 'Desktop') { Join-Path $PSHOME 'powershell.exe' }
         else { Join-Path $PSHOME $(if ($onWindows) { 'pwsh.exe' } else { 'pwsh' }) }
if (-not (Test-Path -LiteralPath $shell)) { $shell = (Get-Process -Id $PID).Path }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ftf-installer-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$plugins = Join-Path $scratch 'ApplicationPlugins'
$state = Join-Path $scratch 'LocalAppData-FieldToFinish'
$appdata = Join-Path $scratch 'AppData'
$fixtures = Join-Path $scratch 'fixtures'
New-Item -ItemType Directory -Force -Path $plugins, $state, $appdata, $fixtures | Out-Null
$installed = Join-Path $plugins 'FieldToFinish.bundle'
$regRoot = 'HKCU:\Software\FTF-InstallerTest\Installed'
$regProducts = 'HKCU:\Software\FTF-InstallerTest\Products'

. (Join-Path $setupSource 'ftf-common.ps1')

# --- stand-in FTF assemblies -------------------------------------------------------------

$dllCache = @{}
function New-Assembly([string]$name, [string]$short, [string]$channel)
{
    $key = "$name|$short|$channel"
    if ($dllCache.ContainsKey($key)) { return $dllCache[$key] }
    $proj = Join-Path $fixtures ("src-" + $name + "-" + $short + "-" + $channel)
    New-Item -ItemType Directory -Force -Path $proj | Out-Null
    $csproj = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework>' +
              '<AssemblyName>' + [IO.Path]::GetFileNameWithoutExtension($name) + '</AssemblyName>' +
              '<InformationalVersion>1.0.0+' + $short + '.' + $channel + '</InformationalVersion>' +
              '<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>' +
              '<Version>1.0.0</Version></PropertyGroup></Project>'
    Set-Content -Path (Join-Path $proj 'Stand.csproj') -Value $csproj -Encoding utf8
    Set-Content -Path (Join-Path $proj 'Stand.cs') -Value ('namespace FtfStandIn { public static class Build { public const string Commit = "' + $short + '"; } }') -Encoding utf8
    $out = Join-Path $proj 'out'
    & dotnet build (Join-Path $proj 'Stand.csproj') -c Release -o $out --nologo -v q 2>&1 | Out-Null
    $dll = Join-Path $out $name
    if (-not (Test-Path $dll)) { throw "could not build the stand-in $name for $short $channel (is the .NET SDK installed?)" }
    $dllCache[$key] = $dll
    return $dll
}

# A release folder laid out as make-release.ps1 writes it.
function New-Release([string]$dir, [string]$commit, [string]$channel, [string]$builtUtc, [string]$cadCommit, [switch]$Dirty)
{
    $short = $commit.Substring(0, 7)
    if (-not $cadCommit) { $cadCommit = $commit }
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    $bundle = Join-Path $dir 'Bundle'
    $contents = Join-Path (Join-Path $bundle 'Contents') '2024'
    New-Item -ItemType Directory -Force -Path $contents | Out-Null
    Copy-Item (Join-Path $setupSource '*') -Destination $dir -Force
    Copy-Item (Join-Path (Join-Path $PSScriptRoot 'FieldToFinish.bundle') 'PackageContents.xml') -Destination $bundle -Force
    $stamp = if ($Dirty) { "$channel.dirty" } else { $channel }
    Copy-Item (New-Assembly 'FieldCodes.Cad.dll' $cadCommit.Substring(0, 7) $stamp) -Destination $contents -Force
    Copy-Item (New-Assembly 'FieldCodes.dll' $short $stamp) -Destination $contents -Force
    [IO.File]::WriteAllText((Join-Path $contents 'Newtonsoft.Json.dll'), 'stand-in third-party assembly')
    Copy-Item (Join-Path (Join-Path $repo 'config') 'rules.json') -Destination $contents -Force
    $line = "FTF " + $builtUtc.Substring(0, 10) + " " + $builtUtc.Substring(11, 5) + "  integration $short  (Release, Civil 3D 2024)"
    Set-Content -Path (Join-Path $bundle 'version.txt') -Value $line -Encoding utf8
    Copy-Item (Join-Path $bundle 'version.txt') -Destination $dir -Force
    Write-Manifest $bundle $commit $channel $builtUtc $Dirty
}

function Write-Manifest([string]$bundle, [string]$commit, [string]$channel, [string]$builtUtc, [bool]$dirty)
{
    $hashes = [ordered]@{}
    foreach ($file in @(Get-ChildItem $bundle -Recurse -File | Where-Object { $_.Name -ne 'build-info.json' } | Sort-Object FullName)) {
        $hashes[(Get-FtfRelative $bundle $file.FullName)] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    [ordered]@{ version = '1.0.0'; commit = $commit; branch = 'integration'; channel = $channel; builtUtc = $builtUtc; dirty = $dirty
                configuration = 'Release'; files = $hashes } | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $bundle 'build-info.json') -Encoding utf8
}

function Install([string]$release, [string[]]$extra)
{
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $release 'install.ps1'), '-TargetRoot', $plugins, '-StateRoot', $state,
                   '-RegistryRoot', $regRoot, '-ProductRoot', $regProducts) + @($extra)
    $said = & $shell @arguments 2>&1 | ForEach-Object { "$_" }
    return [pscustomobject]@{ Code = $LASTEXITCODE; Text = ($said -join "`n") }
}

function Installed() { return (Read-FtfBuildInfo $installed) }
function Contents2024([string]$bundle) { return (Join-Path (Join-Path $bundle 'Contents') '2024') }

$commitA = 'aaaaaaa1111111111111111111111111111111aa'
$commitB = 'bbbbbbb2222222222222222222222222222222bb'
$commitC = 'ccccccc3333333333333333333333333333333cc'
$releases = Join-Path $scratch 'releases'
$relA = Join-Path $releases 'A'
$relB = Join-Path $releases 'B'
$relDev = Join-Path $releases 'Dev'

$realAppData = $env:APPDATA
try
{
    $env:APPDATA = $appdata
    if ($onWindows) {
        Remove-Item 'HKCU:\Software\FTF-InstallerTest' -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -Path (Join-Path $regRoot 'R24.3\ACAD-7100:409') -Force | Out-Null
        New-Item -Path (Join-Path $regProducts 'R24.3\ACAD-7100:409') -Force | Out-Null
        New-ItemProperty -Path (Join-Path $regProducts 'R24.3\ACAD-7100:409') -Name ProductName -Value 'Autodesk Civil 3D 2024 - English' -PropertyType String -Force | Out-Null
    }
    Write-Host '-- building stand-in releases'
    New-Release $relA $commitA 'approved' '2026-11-01T10:00:00Z'
    New-Release $relB $commitB 'approved' '2026-11-03T10:00:00Z'
    New-Release $relDev $commitC 'dev' '2026-11-04T10:00:00Z'
    Check ((Read-FtfBuildInfo (Join-Path $relB 'Bundle')).commit -eq $commitB) 'stand-in releases built'
    Check (@(Test-FtfBundle (Join-Path $relB 'Bundle') (Read-FtfBuildInfo (Join-Path $relB 'Bundle')) -Strict).Count -eq 0) 'a release as make-release.ps1 lays it out verifies (SHA-256 and DLL stamps)'

    $officeRules = Join-Path (Join-Path $appdata 'FieldToFinish') 'rules.json'
    $officeSettings = Join-Path (Join-Path $appdata 'FieldToFinish') 'ftf-settings.json'
    $profileFile = Join-Path (Join-Path (Join-Path $appdata 'FieldToFinish') 'profiles') 'Survey.json'
    foreach ($f in @($officeRules, $officeSettings, $profileFile)) {
        New-Item -ItemType Directory -Force -Path (Split-Path $f -Parent) | Out-Null
        Set-Content -Path $f -Value '{ "office": "do not touch" }' -Encoding utf8
    }

    # --- clean install ---------------------------------------------------------------
    Write-Host '-- a clean install'
    $r = Install $relA @()
    Check ($r.Code -eq 0) "installs into an empty profile ($($r.Code))"
    if ($r.Code -ne 0) { Write-Host $r.Text }
    Check ((Installed).commit -eq $commitA) 'the installed bundle is release A'
    Check (@(Test-FtfBundle $installed (Installed)).Count -eq 0) 'every installed file matches the release'
    Check ($r.Text -match 'action:\s+install') 'it says it is installing'
    Check (@(Get-ChildItem (Join-Path $state 'logs') -Filter 'install-*.log' -ErrorAction SilentlyContinue).Count -ge 1) 'a log is written in the state folder'
    Check ((Test-Path (Join-Path $state 'Update FieldToFinish.bat')) -and (Test-Path (Join-Path (Join-Path $state 'installer') 'install.ps1'))) 'local tools are left for updating, checking and restoring without the network'
    $updateBat = [IO.File]::ReadAllText((Join-Path $state 'Update FieldToFinish.bat'))
    Check ($updateBat -match [regex]::Escape($relA) -and $updateBat -match "`r`n") 'the local update launcher points at the approved release folder (CRLF)'
    Check ($updateBat -match 'cannot be reached') 'and says so plainly when the network is not there'
    Check ((Get-Content (Join-Path $installed 'source.txt') -First 1) -eq $relA) 'the approved release folder is recorded'
    if ($onWindows) {
        $key = Join-Path $regRoot 'R24.3\ACAD-7100:409\Applications\FieldToFinish'
        Check ((Test-Path $key) -and ((Get-ItemProperty $key).LOADER -eq (Join-Path (Contents2024 $installed) 'FieldCodes.Cad.dll'))) 'Civil 3D is told to load the installed DLL at startup'
    } else { $skipped.Add('Civil 3D startup record (registry): Windows only') }

    # --- no update needed --------------------------------------------------------------
    Write-Host '-- no update needed'
    $dll = Join-Path (Contents2024 $installed) 'FieldCodes.Cad.dll'
    $before = (Get-Item $dll).LastWriteTimeUtc
    $backupsBefore = @(Get-ChildItem (Join-Path $state 'backups') -Directory -Filter 'install-*' -ErrorAction SilentlyContinue).Count
    Start-Sleep -Milliseconds 1100
    $r = Install $relA @()
    Check ($r.Code -eq 0 -and $r.Text -match 'up to date') 'the same release again is "up to date"'
    Check ((Get-Item $dll).LastWriteTimeUtc -eq $before) 'and nothing is copied again'
    Check (@(Get-ChildItem (Join-Path $state 'backups') -Directory -Filter 'install-*' -ErrorAction SilentlyContinue).Count -eq $backupsBefore) 'and no backup is made'

    # --- the diagnostic report -------------------------------------------------------------
    $report = Join-Path $scratch 'ftf-check.txt'
    & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setupSource 'check.ps1') -PluginsRoot $plugins -StateRoot $state -RegistryRoot $regRoot -OutFile $report 2>&1 | Out-Null
    $text = if (Test-Path $report) { Get-Content $report -Raw } else { '' }
    Check ($text -match 'approved release aaaaaaa' -and $text -match 'every installed file is the one this build shipped') 'the diagnostic report names the installed build and checks every file'
    Check ($text -match [regex]::Escape($relA) -and $text -match 'this computer has the approved release') 'and compares it with the approved release folder'
    Check ($text -match 'Last install or update' -and $text -match 'install-') 'and shows the last install log'
    Check (@(Get-ChildItem (Join-Path $state 'logs') -Filter 'ftf-check-*.txt' -ErrorAction SilentlyContinue).Count -ge 1) 'a copy of the report is kept with the logs'

    # --- check only ----------------------------------------------------------------------
    $r = Install $relB @('-CheckOnly')
    Check ($r.Code -eq 0 -and $r.Text -match 'update aaaaaaa -> bbbbbbb' -and $r.Text -match 'nothing was changed') 'a check says what an update would do'
    Check ((Installed).commit -eq $commitA) 'and changes nothing'

    # --- settings ---------------------------------------------------------------------------
    $officeLevel = Join-Path (Contents2024 $installed) 'ftf-settings.json'
    Set-Content -Path $officeLevel -Value '{ "office level": "saved from Settings" }' -Encoding utf8
    Set-Content -Path ($officeLevel + '.bak') -Value '{ "office level": "previous" }' -Encoding utf8
    $userFile = Join-Path (Contents2024 $installed) 'my-commands.json'
    Set-Content -Path $userFile -Value '{ "mine": true }' -Encoding utf8

    # --- update -----------------------------------------------------------------------------
    Write-Host '-- an update'
    $r = Install $relB @()
    Check ($r.Code -eq 0) "updates A to B ($($r.Code))"
    Check ((Installed).commit -eq $commitB) 'the installed bundle is release B'
    Check ($r.Text -match 'update aaaaaaa -> bbbbbbb') 'it says which build it replaced with which'
    Check (@(Test-FtfBundle $installed (Installed)).Count -eq 0) 'every installed file matches release B'
    $backups = @(Get-ChildItem (Join-Path $state 'backups') -Directory -Filter 'install-*' -ErrorAction SilentlyContinue)
    Check ($backups.Count -eq 1 -and (Read-FtfBuildInfo (Join-Path $backups[0].FullName 'FieldToFinish.bundle')).commit -eq $commitA) 'release A is backed up first'
    Check ((Get-Content (Join-Path (Contents2024 $installed) 'ftf-settings.json') -Raw) -match 'saved from Settings') 'the office settings level inside the bundle is carried over'
    Check ((Test-Path (Join-Path (Contents2024 $installed) 'ftf-settings.json.bak')) -and (Test-Path (Join-Path (Contents2024 $installed) 'my-commands.json'))) 'and so is anything else the person saved there'
    $untouched = @($officeRules, $officeSettings, $profileFile | Where-Object { (Get-Content $_ -Raw) -match 'do not touch' }).Count
    Check ($untouched -eq 3) 'office rules, personal settings and profiles in APPDATA are untouched'
    Check (-not (Test-Path ($installed + '.new')) -and -not (Test-Path ($installed + '.old'))) 'no half-finished folder is left'

    # --- an older approved release (the office went back) --------------------------------
    $r = Install $relA @('-CheckOnly')
    Check ($r.Text -match 'OLDER') 'a release older than the installed build is called out'

    # --- duplicates -------------------------------------------------------------------------
    Write-Host '-- another copy of FTF'
    $dup = Join-Path $plugins 'FieldToFinish (copy from server).bundle'
    New-Item -ItemType Directory -Force -Path (Contents2024 $dup) | Out-Null
    Copy-Item (Join-Path (Contents2024 (Join-Path $relA 'Bundle')) 'FieldCodes.Cad.dll') -Destination (Contents2024 $dup)
    $other = Join-Path $plugins 'SomethingElse.bundle'
    New-Item -ItemType Directory -Force -Path $other | Out-Null
    Set-Content -Path (Join-Path $other 'PackageContents.xml') -Value '<ApplicationPackage/>' -Encoding utf8
    $r = Install $relB @('-Force')
    Check ($r.Code -eq 0 -and $r.Text -match 'another copy of FTF') 'a second FTF bundle is found'
    Check (-not (Test-Path $dup) -and @(Get-ChildItem $plugins -Directory -Filter '*.disabled-by-FTF-*').Count -eq 1) 'and switched off (renamed), so Civil 3D loads one FTF only'
    Check (Test-Path $other) 'a bundle that is not FTF is left alone'
    $r = Install $relB @('-Force', '-LeaveOtherLoaders')
    Check ($r.Code -eq 0) 'reinstalling the same release with -Force works'

    # --- integrity --------------------------------------------------------------------------
    Write-Host '-- a release that is not what was built'
    $bad = Join-Path $releases 'damaged'
    Remove-Item $bad -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item $relA -Destination $bad -Recurse
    Add-Content -Path (Join-Path (Contents2024 (Join-Path $bad 'Bundle')) 'rules.json') -Value ' '
    $r = Install $bad @()
    Check ($r.Code -eq 1 -and $r.Text -match 'damaged or replaced' -and $r.Text -match 'rules.json') 'a damaged file is refused, by name'
    Check ((Installed).commit -eq $commitB -and $r.Text -match 'still installed') 'and the installed FTF is untouched'

    Remove-Item $bad -Recurse -Force; Copy-Item $relA -Destination $bad -Recurse
    Remove-Item (Join-Path (Contents2024 (Join-Path $bad 'Bundle')) 'Newtonsoft.Json.dll')
    $r = Install $bad @()
    Check ($r.Code -eq 1 -and $r.Text -match 'missing: Contents/2024/Newtonsoft.Json.dll') 'a missing file is refused'

    Remove-Item $bad -Recurse -Force; Copy-Item $relA -Destination $bad -Recurse
    [IO.File]::WriteAllText((Join-Path (Contents2024 (Join-Path $bad 'Bundle')) 'Stray.dll'), 'x')
    $r = Install $bad @()
    Check ($r.Code -eq 1 -and $r.Text -match 'not part of the build: Contents/2024/Stray.dll') 'a file that is not part of the build is refused'

    Remove-Item $bad -Recurse -Force; Copy-Item $relA -Destination $bad -Recurse
    Remove-Item (Join-Path (Join-Path $bad 'Bundle') 'build-info.json')
    $r = Install $bad @()
    Check ($r.Code -eq 1 -and $r.Text -match 'incomplete') 'a release without build-info.json is refused as incomplete'

    $mixed = Join-Path $releases 'mixed'
    New-Release $mixed $commitB 'approved' '2026-11-03T10:00:00Z' -cadCommit $commitA
    $r = Install $mixed @()
    Check ($r.Code -eq 1 -and $r.Text -match 'FieldCodes.Cad.dll is not build bbbbbbb') 'DLLs from different builds are refused, even with matching hashes'

    $partial = Join-Path $releases 'partial'
    New-Item -ItemType Directory -Force -Path $partial | Out-Null
    Copy-Item (Join-Path $setupSource 'install.ps1') -Destination $partial
    $r = Install $partial @()
    Check ($r.Code -eq 1 -and $r.Text -match 'incomplete') 'a half-copied release folder stops with "incomplete"'
    Check ((Installed).commit -eq $commitB) 'and nothing changes'

    # --- development builds ------------------------------------------------------------------
    Write-Host '-- a development build'
    $r = Install $relDev @()
    Check ($r.Code -eq 1 -and $r.Text -match 'development build') 'a development build is refused from a release folder'
    Check ((Installed).commit -eq $commitB) 'and nothing changes'
    $r = Install $relDev @('-AllowDevelopmentBuild')
    Check ($r.Code -eq 0 -and (Installed).commit -eq $commitC) 'UPDATEFTF -Dev can still install one, deliberately'
    $r = Install $relB @()
    Check ($r.Code -eq 0 -and $r.Text -match 'replacing a development build' -and (Installed).commit -eq $commitB) 'the approved release replaces a development build'

    # --- Civil 3D open --------------------------------------------------------------------------
    Write-Host '-- Civil 3D open'
    $running = (Get-Process -Id $PID).ProcessName
    $r = Install $relA @('-ProcessName', $running)
    Check ($r.Code -eq 1 -and $r.Text -match 'Civil 3D is open') 'it stops while Civil 3D is running, and says to close it'
    Check ((Installed).commit -eq $commitB) 'and nothing changes'

    # --- rollback -----------------------------------------------------------------------------
    Write-Host '-- a failure after the swap'
    foreach ($fault in @('AfterSwap', 'Verify')) {
        $r = Install $relA @('-TestFault', $fault)
        Check ($r.Code -eq 1 -and $r.Text -match 'previous FTF was put back') "a failure after the swap ($fault) puts the previous FTF back"
        Check ((Installed).commit -eq $commitB -and @(Test-FtfBundle $installed (Installed)).Count -eq 0) 'release B is installed again, every file intact'
        Check (-not (Test-Path ($installed + '.new')) -and -not (Test-Path ($installed + '.old'))) 'never a partly installed bundle'
    }
    Check (@(Get-ChildItem (Join-Path $state 'failed') -Directory -ErrorAction SilentlyContinue).Count -ge 1) 'the failed copy is kept for diagnosis'
    Check ((Get-Content (Join-Path (Contents2024 $installed) 'ftf-settings.json') -Raw) -match 'saved from Settings') 'settings survive a rollback'
    if ($onWindows) {
        $key = Join-Path $regRoot 'R24.3\ACAD-7100:409\Applications\FieldToFinish'
        Check ((Get-ItemProperty $key).LOADER -eq (Join-Path (Contents2024 $installed) 'FieldCodes.Cad.dll')) 'and the startup record is as it was'
    }

    # --- repair -----------------------------------------------------------------------------------
    Write-Host '-- a damaged install'
    Remove-Item (Join-Path (Contents2024 $installed) 'FieldCodes.dll')
    $r = Install $relB @()
    Check ($r.Code -eq 0 -and $r.Text -match 'repair') 'a damaged install of the same release is repaired'
    Check (@(Test-FtfBundle $installed (Installed)).Count -eq 0) 'every file is back'

    # --- restore previous -------------------------------------------------------------------------
    Write-Host '-- putting back the previous FTF'
    Set-Content -Path (Join-Path (Contents2024 $installed) 'ftf-settings.json') -Value '{ "office level": "changed after the update" }' -Encoding utf8
    $r = Install $relB @('-RestorePrevious')
    Check ($r.Code -eq 0 -and (Installed).commit -ne $commitB) ("Restore previous puts back an earlier build (" + (Get-FtfShort (Installed)) + ")")
    Check ((Get-Content (Join-Path (Contents2024 $installed) 'ftf-settings.json') -Raw) -match 'changed after the update') 'and keeps the settings as they are now'
    $r = Install $relB @()
    Check ($r.Code -eq 0 -and (Installed).commit -eq $commitB) 'and the approved release installs again afterwards'

    # --- uninstall ---------------------------------------------------------------------------------
    Write-Host '-- uninstalling'
    $r = Install $relB @('-Uninstall')
    Check ($r.Code -eq 0 -and -not (Test-Path $installed)) 'uninstall removes the bundle'
    Check ($r.Text -match 'Restore previous') 'after backing it up'
    Check (@($officeRules, $officeSettings, $profileFile | Where-Object { (Get-Content $_ -Raw) -match 'do not touch' }).Count -eq 3) 'office and personal settings survive uninstalling'

    # --- publishing ---------------------------------------------------------------------------------
    Write-Host '-- publishing an approved release'
    $server = Join-Path (Join-Path $scratch 'server') 'FieldToFinish'
    $publish = Join-Path $PSScriptRoot 'publish-release.ps1'
    $p = & $shell -NoProfile -ExecutionPolicy Bypass -File $publish -Package $relB -ReleaseFolder $server -ApprovedBy 'test' -Notes 'First approved release.' 2>&1 | ForEach-Object { "$_" }
    Check ($LASTEXITCODE -eq 0) ("publishes an approved release (" + ($p | Select-Object -Last 1) + ")")
    foreach ($name in @('Install or Update FieldToFinish.bat', 'install.ps1', 'ftf-common.ps1', 'check.ps1', 'README.txt', 'RELEASE-NOTES.txt', 'release.json', 'version.txt')) {
        Check (Test-Path (Join-Path $server $name)) "the release folder has $name"
    }
    $rel = Get-Content (Join-Path $server 'release.json') -Raw | ConvertFrom-Json
    Check ($rel.commit -eq $commitB -and $rel.approvedBy -eq 'test' -and $rel.channel -eq 'approved') 'release.json says what was approved, by whom'
    Check ((Get-Content (Join-Path $server 'updates-from.txt') -First 1) -eq $server) 'the release names its own location for updates'
    Check (@(Test-FtfBundle (Join-Path $server 'Bundle') (Read-FtfBuildInfo (Join-Path $server 'Bundle')) -Strict).Count -eq 0) 'the published copy verifies on the server'
    $r = Install $server @()
    Check ($r.Code -eq 0 -and (Installed).commit -eq $commitB -and (Get-Content (Join-Path $installed 'source.txt') -First 1) -eq $server) 'installing from the published folder records it for updates'

    $p = & $shell -NoProfile -ExecutionPolicy Bypass -File $publish -Package $relDev -ReleaseFolder $server -ApprovedBy 'test' 2>&1 | ForEach-Object { "$_" }
    Check ($LASTEXITCODE -ne 0 -and ($p -join ' ') -match 'development build') 'a development build is never published'
    Check ((Read-FtfBuildInfo (Join-Path $server 'Bundle')).commit -eq $commitB) 'and the approved release stays'
    $p = & $shell -NoProfile -ExecutionPolicy Bypass -File $publish -Package $mixed -ReleaseFolder $server -ApprovedBy 'test' 2>&1 | ForEach-Object { "$_" }
    Check ($LASTEXITCODE -ne 0 -and (Read-FtfBuildInfo (Join-Path $server 'Bundle')).commit -eq $commitB) 'an inconsistent package is never published'
    $relC = Join-Path $releases 'C'
    New-Release $relC $commitC 'approved' '2026-11-05T10:00:00Z'
    $p = & $shell -NoProfile -ExecutionPolicy Bypass -File $publish -Package $relC -ReleaseFolder $server -ApprovedBy 'test' 2>&1 | ForEach-Object { "$_" }
    Check ($LASTEXITCODE -eq 0 -and (Read-FtfBuildInfo (Join-Path $server 'Bundle')).commit -eq $commitC) 'a newer approved release replaces it'
    Check (@(Get-ChildItem (Split-Path $server -Parent) -Directory -Filter 'FieldToFinish.previous-*').Count -eq 1) 'and the previous release is kept beside it'
    $r = Install $server @()
    Check ($r.Code -eq 0 -and $r.Text -match 'update bbbbbbb -> ccccccc') 'surveyors update from the same folder, same double-click'
}
catch
{
    $fail++
    Write-Host "  [FAIL] the test itself stopped: $($_.Exception.Message) at $($_.InvocationInfo.ScriptLineNumber)"
}
finally
{
    $env:APPDATA = $realAppData
    Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
    if ($onWindows) { Remove-Item 'HKCU:\Software\FTF-InstallerTest' -Recurse -Force -ErrorAction SilentlyContinue }
}

foreach ($s in $skipped) { Write-Host "  [NOT RUN] $s" }
Write-Host ''
Write-Host ("installer and publishing: PASS {0}, FAIL {1}" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
exit 0
