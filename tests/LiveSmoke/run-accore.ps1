# Runs one accoreconsole script with a timeout, streaming output to a file so a
# hung prompt still leaves a readable log.
#   run-accore.ps1 <drawing> <script> <log> [timeoutSeconds]
param([string]$Drawing, [string]$Script, [string]$Log, [int]$TimeoutSeconds = 300, [string]$Profile)

$here0 = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here0 '..\..\deploy\TraceEnv.ps1')
$acad = Find-Civil3D2024
if (-not $acad) { throw 'Civil 3D 2024 not found. Set the C3D2024 environment variable to its install folder.' }
$rawPath = "$Log.raw"
$argsLine = '/i "' + $Drawing + '" /s "' + $Script + '" /product C3D /language en-US'
if ($Profile) { $argsLine += ' /p "' + $Profile + '"' }
$p = Start-Process "$acad\accoreconsole.exe" -ArgumentList $argsLine -PassThru -WindowStyle Hidden -RedirectStandardOutput $rawPath
if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { $p | Stop-Process -Force; Write-Host 'TIMEOUT' } else { Write-Host 'finished' }

$raw = [IO.File]::ReadAllText($rawPath)
$clean = ($raw -replace "`0", '') -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object {
    if ($_ -match '^(.) (. )+$' -or ($_ -replace '[^ ]', '').Length -gt ($_.Length * 0.4)) { ($_ -replace '(.) ', '$1') } else { $_ }
}
$clean | Set-Content $Log -Encoding utf8
