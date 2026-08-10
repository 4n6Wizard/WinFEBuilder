<#
.SYNOPSIS
    (Milestone 3) Read-only enumeration of removable/USB disks with full identity data.
.DESCRIPTION
    Uses Storage cmdlets (Get-Disk / Get-Partition / Get-Volume). Performs NO modifications.
    Flags system/boot disks so the caller can block them as targets.
#>
[CmdletBinding()]
param(
    [switch] $IncludeAllDisks,   # advanced: also list non-removable disks (still never a valid target)
    [switch] $AsJson
)
$ErrorActionPreference = 'Stop'

$sysDrive = $env:SystemDrive           # e.g. C:
$disks = Get-Disk | Sort-Object Number

$out = foreach ($d in $disks) {
    $isRemovable = ($d.BusType -eq 'USB') -or ($d.MediaType -eq 'Removable')
    if (-not $IncludeAllDisks -and -not $isRemovable) { continue }

    $partitions = Get-Partition -DiskNumber $d.Number -ErrorAction SilentlyContinue
    $letters = @()
    $isSystem = $false
    foreach ($p in $partitions) {
        if ($p.DriveLetter) {
            $letters += "$($p.DriveLetter):"
            if ("$($p.DriveLetter):" -eq $sysDrive) { $isSystem = $true }
        }
        if ($p.IsBoot -or $p.IsSystem) { $isSystem = $true }
    }

    [ordered]@{
        Number         = $d.Number
        FriendlyName   = $d.FriendlyName
        Manufacturer   = $d.Manufacturer
        Model          = $d.Model
        SerialNumber   = $d.SerialNumber
        UniqueId       = $d.UniqueId
        BusType        = "$($d.BusType)"
        SizeBytes      = $d.Size
        PartitionCount = $d.NumberOfPartitions
        DriveLetters   = $letters
        IsOffline      = $d.IsOffline
        IsReadOnly     = $d.IsReadOnly
        HealthStatus   = "$($d.HealthStatus)"
        IsRemovable    = $isRemovable
        IsSystemDisk   = ($d.IsSystem -or $isSystem)
        IsBootDisk     = $d.IsBoot
    }
}

if ($AsJson) { ,@($out) | ConvertTo-Json -Depth 5 } else { $out | ForEach-Object { [pscustomobject]$_ } }
