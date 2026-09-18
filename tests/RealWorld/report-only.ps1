# Writes RWREPORT for an existing FTF drawing (a copy is opened; nothing is changed or saved).
#   report-only.ps1 -Drawing <dwg> -Out <report.txt>
param([string]$Drawing, [string]$Out)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $here '..\..')
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
$bin = Join-Path $root 'src\FieldCodes.Cad\bin\Debug\net48'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:library /out:"$here\RealWorldHarness.dll" "$here\RealWorldHarness.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" /r:"$bin\FieldCodes.dll" /r:System.Core.dll `
    /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
if ($LASTEXITCODE -ne 0) { throw 'harness compile failed' }
$work = Join-Path $env:TEMP ('ftf-report-' + [guid]::NewGuid().ToString('N') + '.dwg')
Copy-Item -LiteralPath $Drawing -Destination $work
$scr = [IO.Path]::ChangeExtension($work, '.scr')
Set-Content -LiteralPath $scr -Encoding utf8 -Value @('NETLOAD "' + "$bin\FieldCodes.Cad.dll" + '"', 'NETLOAD "' + "$here\RealWorldHarness.dll" + '"', 'RWREPORT', $Out)
& (Join-Path $root 'tests\LiveSmoke\run-accore.ps1') -Drawing $work -Script $scr -Log ([IO.Path]::ChangeExtension($work, '.log')) -TimeoutSeconds 300 | Out-Null
Remove-Item -LiteralPath $work, $scr -ErrorAction SilentlyContinue
