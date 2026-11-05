# Shared helpers for the TRACE / FieldToFinish Windows scripts. Dot-source it:
#     . (Join-Path $PSScriptRoot '..\..\deploy\TraceEnv.ps1')
# Nothing here depends on where the repository lives: everything is found relative to
# this file, so the project can sit on any drive.

$script:TraceDeployDir = $PSScriptRoot

function Get-TraceRepoRoot {
    # deploy\ sits directly under the repository root.
    return (Split-Path -Parent $script:TraceDeployDir)
}

function Find-Civil3D2024 {
    <# Returns the Civil 3D 2024 install folder (the one holding AcDbMgd.dll and C3D\AeccDbMgd.dll),
       or $null. Order: -Override, the C3D2024 environment variable, Program Files, then
       "Program Files" on every fixed drive. #>
    param([string]$Override)

    $candidates = New-Object System.Collections.ArrayList
    if ($Override)    { [void]$candidates.Add($Override) }
    if ($env:C3D2024) { [void]$candidates.Add($env:C3D2024) }
    [void]$candidates.Add((Join-Path $env:ProgramFiles 'Autodesk\AutoCAD 2024'))
    foreach ($drive in [System.IO.DriveInfo]::GetDrives()) {
        if ($drive.DriveType -eq 'Fixed') {
            [void]$candidates.Add((Join-Path $drive.RootDirectory.FullName 'Program Files\Autodesk\AutoCAD 2024'))
        }
    }
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath (Join-Path $c 'AcDbMgd.dll'))) { return $c }
    }
    return $null
}

function Get-TraceCsc {
    # The .NET Framework C# compiler the live-smoke harnesses use. Windows folder, any drive.
    return (Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe')
}

function Expand-TraceScript {
    <# The checked-in AutoCAD .scr files were written with the author's location baked in
       (C:\dev\FieldToFinish). Writes a copy with that replaced by this repository's real
       location, and returns the copy's path. The checked-in file is never modified. #>
    param([string]$Template, [string]$Output, [string]$PluginDll)

    $root = (Get-TraceRepoRoot).TrimEnd('\')
    $text = [System.IO.File]::ReadAllText($Template)
    $text = $text.Replace('C:\dev\FieldToFinish', $root)
    # The commercial edition's plugin is TRACE.Cad.dll; the checked-in scripts name the internal one.
    if ($PluginDll) { $text = $text.Replace('FieldCodes.Cad.dll', $PluginDll) }
    [System.IO.File]::WriteAllText($Output, $text, (New-Object System.Text.UTF8Encoding($false)))
    return $Output
}
