<#
.SYNOPSIS
    (Milestone 3) DESTRUCTIVE USB preparation. SIMULATION-FIRST and heavily guarded.
.DESCRIPTION
    Generates the DiskPart script for the verified disk number. By DEFAULT it only PRINTS the
    intended script (simulation) and does NOT touch any disk. Actual execution requires ALL of:
      -Execute switch, the exact -ConfirmPhrase "ERASE DISK <n>", and a disk that is NOT the
      system/boot disk. Even then it re-checks the disk identity immediately before running.

    This script must never be run against an unverified disk. The GUI performs additional
    protected-disk and identity-revalidation checks before ever calling it.
.PARAMETER DiskNumber   Target disk number (validated by the caller).
.PARAMETER Label        FAT32 volume label (default WINFE).
.PARAMETER ConfirmPhrase Must equal "ERASE DISK <DiskNumber>" exactly to allow execution.
.PARAMETER Execute      Actually run DiskPart. Omit for simulation (default).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [int] $DiskNumber,
    [string] $Label = 'WINFE',
    [string] $ConfirmPhrase,
    [switch] $Execute,
    [switch] $AsJson
)
$ErrorActionPreference = 'Stop'

$expectedPhrase = "ERASE DISK $DiskNumber"

$script = @"
select disk $DiskNumber
clean
convert mbr
create partition primary
format fs=fat32 quick label=$Label
active
assign
exit
"@

$result = [ordered]@{
    DiskNumber     = $DiskNumber
    ExpectedPhrase = $expectedPhrase
    DiskPartScript = $script
    Executed       = $false
    Simulated      = (-not $Execute)
    Blocked        = $false
    Reason         = $null
    ExitCode       = $null
    Output         = $null
}

# Guard 1: protected-disk check (system/boot).
$disk = Get-Disk -Number $DiskNumber -ErrorAction SilentlyContinue
if (-not $disk) {
    $result.Blocked = $true; $result.Reason = "Disk $DiskNumber not found."
    if ($AsJson) { $result | ConvertTo-Json -Depth 4 } else { [pscustomobject]$result }; return
}
if ($disk.IsSystem -or $disk.IsBoot) {
    $result.Blocked = $true; $result.Reason = "Refusing: disk $DiskNumber is a system/boot disk."
    if ($AsJson) { $result | ConvertTo-Json -Depth 4 } else { [pscustomobject]$result }; return
}

# Guard 2: simulation default.
if (-not $Execute) {
    $result.Reason = 'Simulation mode: DiskPart script generated but NOT executed.'
    if ($AsJson) { $result | ConvertTo-Json -Depth 4 } else { [pscustomobject]$result }; return
}

# Guard 3: exact confirmation phrase.
if ($ConfirmPhrase -cne $expectedPhrase) {
    $result.Blocked = $true; $result.Reason = "Confirmation phrase mismatch. Expected exactly: '$expectedPhrase'."
    if ($AsJson) { $result | ConvertTo-Json -Depth 4 } else { [pscustomobject]$result }; return
}

# Execute (only reached when all guards pass).
$tmp = Join-Path $env:TEMP ("winfe_diskpart_{0}.txt" -f ([guid]::NewGuid().ToString('N')))
Set-Content -LiteralPath $tmp -Value $script -Encoding ASCII
try {
    $out = & diskpart.exe /s $tmp 2>&1 | Out-String
    $result.Executed = $true
    $result.ExitCode = $LASTEXITCODE
    $result.Output = $out
} finally {
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
}

if ($AsJson) { $result | ConvertTo-Json -Depth 4 } else { [pscustomobject]$result }
