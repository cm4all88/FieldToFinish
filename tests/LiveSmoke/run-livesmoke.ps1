# Headless live-smoke run of the FTFDRAWLINE drafting world.
# Builds the plugin, compiles the harness, seeds a drawing from the stock NCS
# template, then drives accoreconsole through every straight-line milestone step.
# Output: draft_out.log (cleaned), outdraft.dwg (the resulting drawing).

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$template = "$env:LOCALAPPDATA\Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt"

# 1. Current plugin build.
dotnet build (Join-Path $here '..\..\FieldToFinish.sln') | Out-Null
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

# 4. Run. Output is character-spaced UTF-16: strip nulls/spaces afterwards.
$raw = & "$acad\accoreconsole.exe" /i "$here\seeddraft.dwg" /s "$here\rundraft.scr" `
    /product C3D /language en-US 2>&1 | Out-String

$clean = ($raw -replace "`0", '') -split "`r?`n" | ForEach-Object {
    if ($_ -match '^(.) (. )+$' -or ($_ -replace '[^ ]', '').Length -gt ($_.Length * 0.4)) {
        ($_ -replace '(.) ', '$1')
    } else { $_ }
}
$clean | Set-Content "$here\draft_out.log" -Encoding utf8
Write-Host "log: $here\draft_out.log"
