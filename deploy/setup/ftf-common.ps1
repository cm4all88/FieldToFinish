<#
.SYNOPSIS
    What install.ps1 and check.ps1 both need: which build a bundle is, whether its files are the
    ones it was built with, and what else on the computer loads a copy of FTF.

.DESCRIPTION
    Dot-sourced (". .\ftf-common.ps1"). Reads only: nothing here installs, removes or changes
    anything. Windows PowerShell 5.1 and PowerShell 7 both run it; the registry parts find nothing
    where there is no registry.
#>

# The program files of FTF itself. Each carries its build's commit and channel in its product
# version ("1.0.0+5c3c021.approved"), written by build\FtfBuildInfo.targets.
$script:FtfAssemblies = @('FieldCodes.Cad.dll', 'FieldCodes.dll')

function Read-FtfBuildInfo([string]$bundleDir)
{
    if ([string]::IsNullOrWhiteSpace($bundleDir)) { return $null }
    $file = Join-Path $bundleDir 'build-info.json'
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    try { return (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json) } catch { return $null }
}

function Get-FtfManifest($info)
{
    $map = [ordered]@{}
    if ($null -eq $info -or $null -eq $info.files) { return $map }
    foreach ($prop in $info.files.PSObject.Properties) { $map[$prop.Name] = "$($prop.Value)".ToUpperInvariant() }
    return $map
}

# Windows PowerShell 5.1 leaves ISO dates from JSON as text; PowerShell 7 turns them into dates.
function Get-FtfUtcText($value)
{
    if ($value -is [DateTime]) { return $value.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }
    return "$value"
}

function Get-FtfShort($info)
{
    if ($null -eq $info -or -not $info.commit) { return 'unknown' }
    $c = "$($info.commit)"
    if ($c.Length -ge 7) { return $c.Substring(0, 7) }
    return $c
}

function Test-FtfApproved($info)
{
    return ($null -ne $info) -and ("$($info.channel)" -eq 'approved') -and (-not $info.dirty)
}

function Get-FtfDescription($info)
{
    if ($null -eq $info) { return 'a build without version information' }
    $kind = if (Test-FtfApproved $info) { 'approved release' } else { 'DEVELOPMENT build' }
    return ("{0} {1} ({2}), built {3}" -f $kind, (Get-FtfShort $info), $info.branch, (Get-FtfUtcText $info.builtUtc))
}

function Get-FtfLocalPath([string]$root, [string]$relative)
{
    return (Join-Path $root ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
}

function Get-FtfRelative([string]$root, [string]$full)
{
    return $full.Substring($root.TrimEnd('\', '/').Length + 1).Replace('\', '/')
}

# Whether a bundle's files are the ones its build-info.json lists, byte for byte (SHA-256).
# -Strict (a package about to be installed): every file present must be listed, so nothing
# foreign can ride along. Otherwise (an installed bundle): files FTF or the person wrote there
# later are allowed, but never an unlisted program file, which Civil 3D could load beside FTF.
# Returns the problems; none means the bundle is whole.
function Test-FtfBundle([string]$bundleDir, $info, [switch]$Strict)
{
    $problems = New-Object System.Collections.Generic.List[string]
    if ($null -eq $info) { $problems.Add('it has no build-info.json, so what it should contain is unknown'); return $problems }
    $manifest = Get-FtfManifest $info
    if ($manifest.Count -eq 0) { $problems.Add('its build-info.json lists no files'); return $problems }
    if (-not (Test-Path -LiteralPath (Join-Path $bundleDir 'PackageContents.xml'))) { $problems.Add('PackageContents.xml is missing, so Civil 3D would not load it') }
    foreach ($relative in $manifest.Keys) {
        $file = Get-FtfLocalPath $bundleDir $relative
        if (-not (Test-Path -LiteralPath $file)) { $problems.Add("missing: $relative"); continue }
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($hash -ne $manifest[$relative]) { $problems.Add("damaged or replaced (its SHA-256 differs): $relative") }
    }
    $listed = @{}
    foreach ($relative in $manifest.Keys) { $listed[$relative.ToLowerInvariant()] = $true }
    foreach ($file in @(Get-ChildItem -LiteralPath $bundleDir -Recurse -File -ErrorAction SilentlyContinue)) {
        $relative = Get-FtfRelative $bundleDir $file.FullName
        if ($listed.ContainsKey($relative.ToLowerInvariant())) { continue }
        if ($relative -ieq 'build-info.json') { continue }
        if ($Strict) { $problems.Add("not part of the build: $relative"); continue }
        if ($file.Extension -match '^\.(dll|exe)$') { $problems.Add("an extra program file that is not part of the build: $relative") }
    }
    # The FTF assemblies must all be this build: one stale DLL is the classic load failure.
    $short = Get-FtfShort $info
    foreach ($name in $script:FtfAssemblies) {
        $entry = @($manifest.Keys | Where-Object { ($_ -split '/')[-1] -ieq $name }) | Select-Object -First 1
        if (-not $entry) { $problems.Add("$name is not in the build"); continue }
        $file = Get-FtfLocalPath $bundleDir $entry
        if (-not (Test-Path -LiteralPath $file)) { continue }
        $product = $null
        try { $product = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($file).ProductVersion } catch { }
        if ("$product" -notmatch ('\+' + [regex]::Escape($short) + '\.' + [regex]::Escape("$($info.channel)") + '(\.dirty)?$')) {
            $problems.Add("$name is not build $short $($info.channel) (its version says '$product')")
        }
    }
    return $problems
}

function Test-FtfSamePath([string]$a, [string]$b)
{
    if ([string]::IsNullOrWhiteSpace($a) -or [string]::IsNullOrWhiteSpace($b)) { return $false }
    try { return [IO.Path]::GetFullPath($a).TrimEnd('\', '/') -ieq [IO.Path]::GetFullPath($b).TrimEnd('\', '/') } catch { return $false }
}

# Civil 3D's startup records that load FTF, per product: our own (Applications\FieldToFinish) and
# any other entry whose loader is an FTF DLL (an old NETLOAD-style registration, often to a
# network folder).
function Get-FtfStartupEntries([string]$registryRoot)
{
    $found = @()
    foreach ($release in @(Get-ChildItem $registryRoot -ErrorAction SilentlyContinue)) {
        foreach ($product in @(Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue)) {
            $apps = Join-Path $product.PSPath 'Applications'
            foreach ($app in @(Get-ChildItem $apps -ErrorAction SilentlyContinue)) {
                $p = Get-ItemProperty -Path $app.PSPath -ErrorAction SilentlyContinue
                if ($null -eq $p) { continue }
                $loader = if ($p.PSObject.Properties['LOADER']) { "$($p.LOADER)" } else { '' }
                if ($app.PSChildName -ne 'FieldToFinish' -and $loader -notmatch 'FieldCodes(\.Cad)?\.dll') { continue }
                $found += [pscustomobject]@{
                    Key = $app.PSPath; Name = $app.PSChildName
                    Product = ($release.PSChildName + '\' + $product.PSChildName); Loader = $loader
                    LoadCtrls = $(if ($p.PSObject.Properties['LOADCTRLS']) { $p.LOADCTRLS } else { $null })
                }
            }
        }
    }
    return $found
}

# Everything else that would load a copy of FTF into Civil 3D. A second FieldCodes.Cad.dll fights
# the installed one over every command, and a copy loaded from a network folder breaks whenever
# that folder changes (how NETLOAD from the server went wrong).
function Find-FtfOtherLoaders([string]$installedDll, [string]$installedBundle, [string[]]$pluginRoots, [string]$registryRoot, [string]$appData)
{
    $found = @()
    foreach ($entry in @(Get-FtfStartupEntries $registryRoot)) {
        if ($entry.Name -eq 'FieldToFinish' -and (Test-FtfSamePath $entry.Loader $installedDll)) { continue }
        if ($entry.Name -eq 'FieldToFinish') { continue }     # install.ps1 points this one at the installed DLL itself
        if ($entry.LoadCtrls -eq 0) { continue }
        $found += [pscustomobject]@{ Kind = 'registry'; Path = $entry.Key; Fixable = ($entry.Key -like '*HKEY_CURRENT_USER*')
                                     Text = ("a Civil 3D startup entry '{0}' ({1}) loads {2}" -f $entry.Name, $entry.Product, $entry.Loader) }
    }
    foreach ($release in @(Get-ChildItem $registryRoot -ErrorAction SilentlyContinue)) {
        foreach ($product in @(Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue)) {
            foreach ($userProfile in @(Get-ChildItem (Join-Path $product.PSPath 'Profiles') -ErrorAction SilentlyContinue)) {
                $values = Get-ItemProperty -Path (Join-Path $userProfile.PSPath 'Dialogs\Appload\Startup') -ErrorAction SilentlyContinue
                if ($null -eq $values) { continue }
                foreach ($prop in $values.PSObject.Properties) {
                    if ("$($prop.Value)" -match 'FieldCodes(\.Cad)?\.dll' -and -not (Test-FtfSamePath "$($prop.Value)" $installedDll)) {
                        $found += [pscustomobject]@{ Kind = 'suite'; Path = $null; Fixable = $false
                                                     Text = ("the Startup Suite (APPLOAD > Startup Suite > Contents) in profile '{0}' loads {1}" -f $userProfile.PSChildName, $prop.Value) }
                    }
                }
            }
        }
    }
    foreach ($root in @($pluginRoots | Where-Object { $_ })) {
        foreach ($bundle in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
            if (Test-FtfSamePath $bundle.FullName $installedBundle) { continue }
            if ($bundle.Name -notmatch '\.bundle$') { continue }
            $hasFtf = @(Get-ChildItem -LiteralPath $bundle.FullName -Recurse -File -Filter 'FieldCodes.Cad.dll' -ErrorAction SilentlyContinue).Count -gt 0
            if (-not $hasFtf) {
                $manifest = Join-Path $bundle.FullName 'PackageContents.xml'
                $hasFtf = (Test-Path -LiteralPath $manifest) -and ((Get-Content -LiteralPath $manifest -Raw -ErrorAction SilentlyContinue) -match 'FieldCodes\.Cad\.dll')
            }
            if ($hasFtf) {
                $found += [pscustomobject]@{ Kind = 'bundle'; Path = $bundle.FullName; Fixable = (Test-FtfSamePath $root $pluginRoots[0])
                                             Text = ("another copy of FTF that Civil 3D also loads: " + $bundle.FullName) }
            }
        }
    }
    if ($appData) {
        foreach ($support in @(Get-ChildItem -LiteralPath (Join-Path $appData 'Autodesk') -Directory -Filter 'C3D 20*' -ErrorAction SilentlyContinue)) {
            foreach ($lsp in @(Get-ChildItem -LiteralPath $support.FullName -Recurse -File -Include 'acad*.lsp' -ErrorAction SilentlyContinue)) {
                if ((Get-Content -LiteralPath $lsp.FullName -Raw -ErrorAction SilentlyContinue) -match 'FieldCodes') {
                    $found += [pscustomobject]@{ Kind = 'lisp'; Path = $lsp.FullName; Fixable = $false
                                                 Text = ($lsp.FullName + ' NETLOADs FTF when Civil 3D starts') }
                }
            }
        }
    }
    return $found
}
