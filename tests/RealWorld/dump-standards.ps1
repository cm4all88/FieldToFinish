# Dumps the drafting standard of real office drawings (copies only) with STDDUMP.
#   dump-standards.ps1 -Drawings <paths> [-Out <folder>]
# Each drawing is copied into a work folder first; the copy is opened, never the original.
param([string[]]$Drawings, [string]$Out = 'C:\dev\FTF-RealWorld\dumps')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
$runner = Join-Path $here '..\LiveSmoke\run-accore.ps1'
New-Item -ItemType Directory -Force $Out | Out-Null

& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:library /out:"$here\StandardsDump.dll" "$here\StandardsDump.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" /r:System.Core.dll
if ($LASTEXITCODE -ne 0) { throw 'dump harness compile failed' }

foreach ($d in $Drawings) {
    $name = [IO.Path]::GetFileNameWithoutExtension($d)
    $work = Join-Path $Out ('work-' + $name + [IO.Path]::GetExtension($d))
    Copy-Item -LiteralPath $d -Destination $work -Force
    $report = Join-Path $Out ($name + '.txt')
    $scr = Join-Path $Out ($name + '.scr')
    Set-Content -LiteralPath $scr -Encoding ascii -Value @(
        'NETLOAD "' + "$here\StandardsDump.dll" + '"',
        'STDDUMP', $report, '')
    & $runner -Drawing $work -Script $scr -Log (Join-Path $Out ($name + '.log')) -TimeoutSeconds 300 | Out-Null
    Remove-Item -LiteralPath $work, $scr -ErrorAction SilentlyContinue
    '{0}: {1}' -f $name, $(if (Test-Path -LiteralPath $report) { (Get-Item -LiteralPath $report).Length } else { 'NO REPORT' })
}
