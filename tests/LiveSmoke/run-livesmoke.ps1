# Headless live-smoke run of the FTFDRAWLINE drafting world.
# Builds the plugin, compiles the harness, seeds a drawing from the stock NCS
# template, then drives accoreconsole through every straight-line milestone step.
# Output: draft_out.log (cleaned), outdraft.dwg (the resulting drawing).

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '..\..\deploy\TraceEnv.ps1')
$acad = Find-Civil3D2024
if (-not $acad) { throw 'Civil 3D 2024 not found. Set the C3D2024 environment variable to its install folder.' }
$csc = Get-TraceCsc
$template = "$env:LOCALAPPDATA\Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt"

# 1. Current plugin build.
dotnet build (Join-Path $here '..\..\FieldToFinish.sln') -p:TraceEdition=Default | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'plugin build failed' }

# 2. Harness.
& $csc /nologo /target:library /out:"$here\DraftTestHarness.dll" "$here\DraftTestHarness.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" `
    /r:"$acad\C3D\AeccDbMgd.dll"
if ($LASTEXITCODE -ne 0) { throw 'harness compile failed' }

# 3. Fresh seed + no stale outputs (SAVEAS hangs on an overwrite prompt).
Remove-Item "$here\outdraft.dwg" -ErrorAction SilentlyContinue
Remove-Item "$here\seeddraft.dwg" -ErrorAction SilentlyContinue
Copy-Item $template "$here\seeddraft.dwg"

# 4. Run. The checked-in script names the author's folder; use a copy for this location.
$scr = Expand-TraceScript -Template "$here\rundraft.scr" -Output "$here\rundraft.generated.scr" -PluginDll 'TRACE.Cad.dll'
# Output is character-spaced UTF-16: strip nulls/spaces afterwards.
$raw = & "$acad\accoreconsole.exe" /i "$here\seeddraft.dwg" /s "$scr" `
    /product C3D /language en-US 2>&1 | Out-String

$clean = ($raw -replace "`0", '') -split "`r?`n" | ForEach-Object {
    if ($_ -match '^(.) (. )+$' -or ($_ -replace '[^ ]', '').Length -gt ($_.Length * 0.4)) {
        ($_ -replace '(.) ', '$1')
    } else { $_ }
}
$clean | Set-Content "$here\draft_out.log" -Encoding utf8
Write-Host "log: $here\draft_out.log"
