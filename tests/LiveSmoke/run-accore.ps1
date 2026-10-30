# Runs one accoreconsole script with a timeout, streaming output to a file so a
# hung prompt still leaves a readable log.
#   run-accore.ps1 <drawing> <script> <log> [timeoutSeconds]
param([string]$Drawing, [string]$Script, [string]$Log, [int]$TimeoutSeconds = 300, [string]$Profile)

$acad = 'C:\Program Files\Autodesk\AutoCAD 2024'

# accoreconsole loads the installed bundle, and a NETLOAD of the same assembly from the build
# folder is then ignored. A run against a stale bundle looks like a pass but tests yesterday's
# code, so say so loudly rather than let it pass quietly.
$built = Join-Path (Split-Path -Parent $PSScriptRoot) '..\src\FieldCodes.Cad\bin\Debug\net48\FieldCodes.Cad.dll'
$installed = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024\FieldCodes.Cad.dll'
if ((Test-Path $built) -and (Test-Path $installed)) {
    $b = (Get-Item $built).LastWriteTimeUtc
    $i = (Get-Item $installed).LastWriteTimeUtc
    if ($b -gt $i.AddSeconds(2)) {
        Write-Host ('STALE PLUGIN: the installed bundle is from ' + $i.ToLocalTime().ToString('yyyy-MM-dd HH:mm') +
                    ' but the build is from ' + $b.ToLocalTime().ToString('yyyy-MM-dd HH:mm') +
                    '. Run deploy\install-dev.ps1 (Civil 3D must be closed); this run is testing the older code.')
    }
}

$rawPath = "$Log.raw"
$argsLine = '/i "' + $Drawing + '" /s "' + $Script + '" /product C3D /language en-US'
if ($Profile) { $argsLine += ' /p "' + $Profile + '"' }
$errPath = "$Log.err"
$p = Start-Process "$acad\accoreconsole.exe" -ArgumentList $argsLine -PassThru -WindowStyle Hidden -RedirectStandardOutput $rawPath -RedirectStandardError $errPath
$null = $p.Handle   # cache the handle now, or ExitCode reads as empty after the process ends
$timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
if ($timedOut) { $p | Stop-Process -Force; Write-Host 'TIMEOUT' } else { Write-Host 'finished' }

$raw = [IO.File]::ReadAllText($rawPath)
$clean = ($raw -replace "`0", '') -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object {
    if ($_ -match '^(.) (. )+$' -or ($_ -replace '[^ ]', '').Length -gt ($_.Length * 0.4)) { ($_ -replace '(.) ', '$1') } else { $_ }
}
$clean = @($clean)
# The exit code and anything on stderr go at the end: a run that dies inside a command
# leaves no console output of its own, and these are then the only evidence.
$exitNote = if ($timedOut) { 'accoreconsole: TIMEOUT (killed)' } else { "accoreconsole exit code: $($p.ExitCode)" }
$clean += $exitNote
if ((Test-Path -LiteralPath $errPath) -and (Get-Item -LiteralPath $errPath).Length -gt 0) {
    $clean += '--- stderr ---'
    $clean += @(Get-Content -LiteralPath $errPath | Where-Object { $_.Trim().Length -gt 0 })
}
$clean | Set-Content $Log -Encoding utf8
