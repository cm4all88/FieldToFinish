<#
.SYNOPSIS
    Builds the TRACE commercial bundle and refuses to finish if any company-specific
    content is in it.  UNTESTED: written without a Windows/Civil 3D machine.

.DESCRIPTION
    1. dotnet build -c Release -p:TraceEdition=Default   (neutral configuration)
    2. Assembles dist\TRACE.bundle\ (manifest + Contents\2024\*, Autodesk DLLs excluded,
       factory rules from config\default).
    3. Scans every text file and every assembly's strings in the bundle for
       Parametrix / PMX / internal paths and fails if any are found.

    Output: dist\TRACE.bundle  (input to deploy\installer\TRACE.iss)
#>
[CmdletBinding()]
param([string]$C3D2024 = 'C:\Program Files\Autodesk\AutoCAD 2024')

$ErrorActionPreference = 'Stop'
$repo   = Split-Path -Parent $PSScriptRoot
$dist   = Join-Path $repo 'dist\TRACE.bundle'
$banned = 'Parametrix', 'PMX', 'fdb_xdef', 'f2f_xdef', 'U:\', 'C:\dev\'

dotnet build (Join-Path $repo 'src\FieldCodes.Cad\FieldCodes.Cad.csproj') -c Release `
    -p:TraceEdition=Default "-p:C3D2024=$C3D2024"
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
$contents = Join-Path $dist 'Contents\2024'
New-Item -ItemType Directory -Path $contents -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'TRACE.bundle\PackageContents.xml') $dist

$out = Join-Path $repo 'src\FieldCodes.Cad\bin\Release\net48'
Get-ChildItem $out -File | Where-Object { $_.Name -notmatch '^(Ac|Aecc|Ad)' -and $_.Extension -notin '.pdb', '.xml' } |
    Copy-Item -Destination $contents -Force
Copy-Item (Join-Path $repo 'config\default\rules.json') $contents -Force

# --- release guard: nothing company-specific may ship ---
$hits = @()
foreach ($f in Get-ChildItem $dist -Recurse -File) {
    # ASCII and UTF-16 views of the bytes cover text files and .NET string literals.
    $bytes = [IO.File]::ReadAllBytes($f.FullName)
    $views = @([Text.Encoding]::ASCII.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes))
    foreach ($b in $banned) {
        foreach ($v in $views) {
            if ($v.IndexOf($b, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $hits += "$($f.Name): '$b'"; break }
        }
    }
}
if ($hits.Count -gt 0) {
    Write-Error ("Release guard failed. Company-specific content found:`n  " + ($hits | Sort-Object -Unique) -join "`n  ")
}
Write-Host "Bundle ready: $dist (release guard passed)"
