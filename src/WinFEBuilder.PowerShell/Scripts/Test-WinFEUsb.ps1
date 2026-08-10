<#
.SYNOPSIS
    (Milestone 3) Read-only post-copy structural validation of a WinFE volume.
.DESCRIPTION
    Confirms the expected boot structure exists on the given drive/root. This is STRUCTURAL
    validation only. It does NOT assert boot success or write-protection — those are manual
    tests recorded on the Validation page.
.PARAMETER Root  Drive root or folder to validate, e.g. "E:\".
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Root,
    [switch] $AsJson
)
$ErrorActionPreference = 'Stop'

$expected = @('Boot','EFI','Sources','Sources\boot.wim')
$found = @{}
foreach ($e in $expected) { $found[$e] = Test-Path -LiteralPath (Join-Path $Root $e) }

$bootWim = Join-Path $Root 'Sources\boot.wim'
$wimSha = $null; $wimSize = 0
if (Test-Path -LiteralPath $bootWim) {
    $wimSize = (Get-Item -LiteralPath $bootWim).Length
    $wimSha = (Get-FileHash -LiteralPath $bootWim -Algorithm SHA256).Hash.ToLower()
}

$structurePass = -not ($found.Values -contains $false)

$result = [ordered]@{
    Root                        = $Root
    Expected                    = $found
    BootWimSize                 = $wimSize
    BootWimSha256               = $wimSha
    UsbCreation                 = 'NOT TESTED'   # set by higher-level workflow
    BootStructure               = if ($structurePass) {'PASS'} else {'FAIL'}
    OfflineStructuralValidation = if ($structurePass) {'PASS'} else {'FAIL'}
    BootTest                    = 'NOT TESTED'    # manual only
    WriteProtectionTest         = 'NOT TESTED'    # manual only
}
if ($AsJson) { $result | ConvertTo-Json -Depth 4 } else { [pscustomobject]$result }
