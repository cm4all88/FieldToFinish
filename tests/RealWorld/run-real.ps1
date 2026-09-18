# Runs one production-drawing test: copies a real drawing, runs a script of FTF commands on the COPY
# in accoreconsole (with the office Civil 3D profile, so office plot styles and fonts resolve), then
# plots every FTF exhibit layout to PDF for a visual comparison.
#   run-real.ps1 -Drawing <original> -Name <case> -Commands <file with script lines> [-Office]
# Output in C:\dev\FTF-RealWorld\runs\<case>: the working copy, log, report, PDFs.
param([string]$Drawing, [string]$Name, [string]$Commands, [switch]$Office, [int]$TimeoutSeconds = 600)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $here '..\..')
$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'
$bin = Join-Path $root 'src\FieldCodes.Cad\bin\Debug\net48'
$runner = Join-Path $root 'tests\LiveSmoke\run-accore.ps1'
$out = Join-Path 'C:\dev\FTF-RealWorld\runs' $Name
New-Item -ItemType Directory -Force $out | Out-Null

& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:library /out:"$here\RealWorldHarness.dll" "$here\RealWorldHarness.cs" `
    /r:"$acad\accoremgd.dll" /r:"$acad\acdbmgd.dll" /r:"$acad\acmgd.dll" /r:"$bin\FieldCodes.dll" /r:System.Core.dll `
    /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
if ($LASTEXITCODE -ne 0) { throw 'harness compile failed' }

# The office exhibit profile, where FTFPROFILE and FTFEXHIBIT look for it.
$profiles = Join-Path $env:APPDATA 'FieldToFinish\profiles'
New-Item -ItemType Directory -Force $profiles | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'config\profiles') -Filter '*.json' | Copy-Item -Destination $profiles -Force

$work = Join-Path $out ([IO.Path]::GetFileNameWithoutExtension($Drawing) + '-FTF.dwg')
Copy-Item -LiteralPath $Drawing -Destination $work -Force
$report = Join-Path $out 'report.txt'
$lines = @(
    'NETLOAD "' + "$bin\FieldCodes.Cad.dll" + '"',
    'NETLOAD "' + "$here\RealWorldHarness.dll" + '"',
    'RWCLOCK', 'start'
) + (Get-Content -LiteralPath $Commands) + @(
    'RWCLOCK', 'end',
    'RWREPORT', $report,
    '_.QSAVE'
)
$scr = Join-Path $out 'run.scr'
Set-Content -LiteralPath $scr -Encoding utf8 -Value $lines
$args = @{ Drawing = $work; Script = $scr; Log = (Join-Path $out 'run.log'); TimeoutSeconds = $TimeoutSeconds }
if ($Office) { $args.Profile = 'PMX Survey Civil3D 2024' }
& $runner @args

# Preview each exhibit layout the report names with FTFEXHIBITPREVIEW (the profile's plotter, paper and plot styles).
if (Test-Path -LiteralPath $report) {
    $layouts = @(Select-String -LiteralPath $report -Pattern '^EXHIBIT (.+?): 1"' | ForEach-Object { $_.Matches[0].Groups[1].Value })
    if ($layouts) {
        Get-ChildItem -LiteralPath $out -Filter 'plot-*.pdf' | Remove-Item
        $plot = @()
        foreach ($l in $layouts) { $plot += 'FTFEXHIBITPREVIEW'; $plot += $l }
        # A PNG of each sheet too, to look at the plotted result directly.
        Get-ChildItem -LiteralPath $out -Filter 'view-*.png' | Remove-Item
        foreach ($l in $layouts) { $plot += 'RWPNG'; $plot += $l; $plot += (Join-Path $out ('view-' + ($l -replace '[^A-Za-z0-9]+', '-') + '.png')) }
        $pscr = Join-Path $out 'plot.scr'
        Set-Content -LiteralPath $pscr -Encoding utf8 -Value (@('NETLOAD "' + "$bin\FieldCodes.Cad.dll" + '"', 'NETLOAD "' + "$here\RealWorldHarness.dll" + '"') + $plot)
        $copy = Join-Path $out 'plotcopy.dwg'
        Copy-Item -LiteralPath $work -Destination $copy -Force
        $pargs = @{ Drawing = $copy; Script = $pscr; Log = (Join-Path $out 'plot.log'); TimeoutSeconds = 300 }
        if ($Office) { $pargs.Profile = 'PMX Survey Civil3D 2024' }
        & $runner @pargs
        Remove-Item -LiteralPath $copy -ErrorAction SilentlyContinue
        $n = 0
        foreach ($m in (Select-String -LiteralPath (Join-Path $out 'plot.log') -Pattern '^FTFEXHIBITPREVIEW: (.+\.pdf)')) {
            $src = $m.Matches[0].Groups[1].Value.Trim()
            if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $out ('plot-' + ($layouts[$n] -replace '[^A-Za-z0-9]+', '-') + '.pdf')) -Force }
            $n++
        }
    }
}
Get-ChildItem -LiteralPath $out -Filter *.pdf | Select-Object Name, Length

