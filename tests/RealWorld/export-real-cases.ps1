# Restores the real-drawing test cases into the local real-world folder from git history.
#
# They were removed from the repository on 2026-11-03 because the repository is public and they name
# real projects, parcels and clients (Step 0 of the point accountability work). Nothing was lost: this
# copies them back out of the last commit that held them, into <Root>\cases (case list cases.psd1 and
# one <case>.txt each) and <Root>\KNOWN-SOURCE-DISCREPANCIES.md. Existing files are never overwritten
# unless -Force is given.
#   export-real-cases.ps1 [-Commit e76f6c2] [-Root C:\dev\FTF-RealWorld] [-Force]
param([string]$Commit = 'e76f6c2',
      [string]$Root = $(if ($env:FTF_REALWORLD) { $env:FTF_REALWORLD } else { 'C:\dev\FTF-RealWorld' }),
      [switch]$Force)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8   # legal text holds degree signs; keep them intact
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$casesOut = Join-Path $Root 'cases'
New-Item -ItemType Directory -Force $casesOut | Out-Null

function Write-FromHistory([string]$path, [string]$target) {
    if ((Test-Path -LiteralPath $target) -and -not $Force) { Write-Host "kept     $target (exists; -Force to replace)"; return }
    $text = & git -C $repo show "${Commit}:$path"
    if ($LASTEXITCODE -ne 0) { throw "git show ${Commit}:$path failed" }
    [IO.File]::WriteAllLines($target, [string[]]$text)
    Write-Host "restored $target"
}

foreach ($path in (& git -C $repo ls-tree --name-only "$Commit" 'tests/RealWorld/cases/')) {
    Write-FromHistory $path (Join-Path $casesOut (Split-Path $path -Leaf))
}
Write-FromHistory 'tests/RealWorld/KNOWN-SOURCE-DISCREPANCIES.md' (Join-Path $Root 'KNOWN-SOURCE-DISCREPANCIES.md')

# The case list was a table in run-all-cases.ps1: '<case>' = "$refs\<folder>\<drawing>.dwg".
$manifest = Join-Path $casesOut 'cases.psd1'
if ((Test-Path -LiteralPath $manifest) -and -not $Force) { Write-Host "kept     $manifest (exists; -Force to replace)"; return }
$entries = @()
foreach ($line in (& git -C $repo show "${Commit}:tests/RealWorld/run-all-cases.ps1")) {
    if ($line -match "^\s*'([^']+)'\s*=\s*`"\`$refs\\(.+)`"\s*$") { $entries += "    '" + $Matches[1] + "' = 'refs\" + $Matches[2] + "'" }
}
if ($entries.Count -eq 0) { throw "No cases found in ${Commit}:tests/RealWorld/run-all-cases.ps1" }
[IO.File]::WriteAllLines($manifest, [string[]](@('@{') + $entries + @('}')))
Write-Host "restored $manifest ($($entries.Count) cases)"
