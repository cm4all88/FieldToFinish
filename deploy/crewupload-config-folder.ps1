<#
.SYNOPSIS
    Creates the shared Crew Upload configuration folder and gives PMs/admins Modify and crews Read.

.DESCRIPTION
    project-registry.json lives here, with its backups (project-registry.backup-1..5.json), the
    temporary file a save writes first, and the writers' lock file. A PM save creates, renames and
    deletes files in this folder, so PMs need Modify on the folder itself, not just on the JSON.
    Crews only ever read it.

    These NTFS permissions are the security boundary. The app's --setup switch and its optional
    projectManagers list only keep the setup screen out of the way; they protect nothing.

    The script adds two grants and leaves everything else alone. It then prints the folder's ACL:
    check that the crew group does not also get write access through an inherited entry (for
    example "Everyone" or "Domain Users" with Modify on a parent folder). If it does, break
    inheritance on this folder and remove that entry -- an IT decision, so not done here.

.EXAMPLE
    .\crewupload-config-folder.ps1 -PmGroup 'PARAMETRIX\Survey-PMs' -CrewGroup 'PARAMETRIX\Survey-Crews'
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Folder = '\\parametrix.com\pmx\PSO\Shared\Divisions\00Survey\CrewUpload\Config',
    [Parameter(Mandatory)][string]$PmGroup,
    [Parameter(Mandatory)][string]$CrewGroup
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Folder)) {
    if ($PSCmdlet.ShouldProcess($Folder, 'Create folder')) { New-Item -ItemType Directory -Path $Folder | Out-Null }
}

if ($PSCmdlet.ShouldProcess($Folder, "Grant $PmGroup Modify, $CrewGroup Read")) {
    # (OI)(CI): applies to the folder, its files and anything created in it later.
    & icacls $Folder /grant "${PmGroup}:(OI)(CI)M" | Out-Host
    & icacls $Folder /grant "${CrewGroup}:(OI)(CI)RX" | Out-Host
}

Write-Host ''
Write-Host 'Current permissions -- check the crew group has no write or modify entry, inherited or not:'
& icacls $Folder
