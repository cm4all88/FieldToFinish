# Dumps model-space geometry in a window from a copy of a real drawing.
#   dump-geometry.ps1 -Drawing <path> -Window "minX,minY,maxX,maxY" -Name <report> [-Out <folder>]
param([string]$Drawing, [string]$Window, [string]$Name, [string]$Out = 'C:\dev\FTF-RealWorld\dumps')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:library /out:"$here\StandardsDump.dll" "$here\StandardsDump.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" /r:System.Core.dll
if ($LASTEXITCODE -ne 0) { throw 'dump harness compile failed' }
$work = Join-Path $Out ('work-' + [IO.Path]::GetFileName($Drawing))
Copy-Item -LiteralPath $Drawing -Destination $work -Force
$scr = Join-Path $Out ($Name + '.scr')
Set-Content -LiteralPath $scr -Encoding ascii -Value @('NETLOAD "' + "$here\StandardsDump.dll" + '"', 'GEODUMP', $Window, (Join-Path $Out ($Name + '.txt')), '')
& (Join-Path $here '..\LiveSmoke\run-accore.ps1') -Drawing $work -Script $scr -Log (Join-Path $Out ($Name + '.log')) -TimeoutSeconds 200 | Out-Null
Remove-Item -LiteralPath $work, $scr -ErrorAction SilentlyContinue
