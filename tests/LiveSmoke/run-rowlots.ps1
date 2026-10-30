# Headless live-smoke run of FTFROW and FTFLOTS.
#
# Builds the plugin, compiles the harness, seeds a drawing from the stock NCS template
# into rowlots\ (where its own ftf-settings.json sits, so the right-of-way and
# platted-block pages point at layers the seed creates), then drives accoreconsole
# through every mode the README describes:
#
#   FTFROW Run           three monuments along one frontage, 60' right of way to the
#                        right, the last one an offset monument 5' at N45E
#   FTFROW Block         a 200x100 block, two frontages (60' and 40'), two plain sides,
#                        one 25' rounded corner
#   FTFROW Intersection  a 100x100 ring of crossings, one of them 99.5' against a 100'
#                        record width, so the tolerance report fires
#   FTFLOTS              the same block divided into four 50' lots with a 20' rear alley,
#                        setbacks, easements, drainage (lot 3 to the rear) and the checks
#
# Then FTFCLEAN (which must leave all of it alone) and FTFDRAFTCLEAN Annotation (which
# must take the text and leave the geometry).
#
# Output: rowlots\rowlots_out.log (cleaned) and rowlots\outrowlots.dwg.

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'rowlots'
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$template = "$env:LOCALAPPDATA\Autodesk\C3D 2024\enu\Template\_Autodesk Civil 3D (Imperial) NCS.dwt"

# 1. Current plugin build. The script NETLOADs the Debug output.
dotnet build (Join-Path $here '..\..\src\FieldCodes.Cad\FieldCodes.Cad.csproj') -c Debug | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'plugin build failed' }

# 2. Harness.
& $csc /nologo /target:library /out:"$here\RowLotsTestHarness.dll" "$here\RowLotsTestHarness.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" `
    /r:"$acad\C3D\AeccDbMgd.dll"
if ($LASTEXITCODE -ne 0) { throw 'harness compile failed' }

# 3. Fresh seed + no stale output (SAVEAS hangs on an overwrite prompt).
if (-not (Test-Path $template)) { throw "template not found: $template" }
Remove-Item "$out\outrowlots.dwg" -ErrorAction SilentlyContinue
Remove-Item "$out\seedrowlots.dwg" -ErrorAction SilentlyContinue
Copy-Item $template "$out\seedrowlots.dwg"

# 4. Run. Output is character-spaced UTF-16: strip nulls/spaces afterwards.
$log = Join-Path $out 'rowlots_out.log'
& (Join-Path $here 'run-accore.ps1') -Drawing "$out\seedrowlots.dwg" -Script "$here\runrowlots.scr" `
    -Log $log -TimeoutSeconds 420

Write-Host "log: $log"
$errors = Select-String -Path $log -Pattern 'FTFROW:|FTFLOTS:|WARNING|failed|error' -SimpleMatch:$false |
    Select-Object -ExpandProperty Line
if ($errors) { Write-Host "`n--- what the commands said ---"; $errors | ForEach-Object { Write-Host $_ } }
