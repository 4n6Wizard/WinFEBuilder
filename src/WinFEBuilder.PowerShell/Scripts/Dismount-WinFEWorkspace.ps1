<#
.SYNOPSIS
    (Milestone 4) Cleans up stale DISM WIM mounts to avoid leaving images mounted.
.DESCRIPTION
    Lists mounted images, optionally discards a specific mount directory, and runs
    DISM /Cleanup-Mountpoints. Uses DISM only (read/repair of mount state); does not modify disks.
.PARAMETER MountDir  Optional specific mount directory to unmount and discard.
.PARAMETER Commit    Commit instead of discard for the specific MountDir (default: discard).
#>
[CmdletBinding()]
param(
    [string] $MountDir,
    [switch] $Commit,
    [switch] $AsJson
)
$ErrorActionPreference = 'Stop'

$actions = @()

# Report current mounts.
$mounted = & dism.exe /Get-MountedImageInfo 2>&1 | Out-String
$actions += [ordered]@{ Step = 'Get-MountedImageInfo'; Output = $mounted }

if ($MountDir) {
    if (Test-Path -LiteralPath $MountDir) {
        $mode = if ($Commit) { '/Commit' } else { '/Discard' }
        $unmount = & dism.exe /Unmount-Image /MountDir:$MountDir $mode 2>&1 | Out-String
        $actions += [ordered]@{ Step = "Unmount-Image $mode"; MountDir = $MountDir; Output = $unmount; ExitCode = $LASTEXITCODE }
    } else {
        $actions += [ordered]@{ Step = 'Unmount-Image'; MountDir = $MountDir; Output = 'Mount dir not found.' }
    }
}

$cleanup = & dism.exe /Cleanup-Mountpoints 2>&1 | Out-String
$actions += [ordered]@{ Step = 'Cleanup-Mountpoints'; Output = $cleanup; ExitCode = $LASTEXITCODE }

$result = [ordered]@{ Success = ($LASTEXITCODE -eq 0); Actions = $actions }
if ($AsJson) { $result | ConvertTo-Json -Depth 5 } else { [pscustomobject]$result }
