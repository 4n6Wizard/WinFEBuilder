<#
.SYNOPSIS
    Read-only environment audit for WinFE Builder: admin, architecture, PowerShell, ADK.
.NOTES
    PowerShell 5.1 compatible. Emits a JSON object with per-check status.
#>
[CmdletBinding()]
param(
    [switch] $AsJson
)

$ErrorActionPreference = 'Stop'

function New-Check($name, $status, $summary) {
    [ordered]@{ Name = $name; Status = $status; Summary = $summary }
}

$checks = @()

# Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
$checks += New-Check 'Administrator privileges' ($(if ($isAdmin) {'PASS'} else {'FAIL'})) `
    ($(if ($isAdmin) {'Running elevated.'} else {'Not elevated; run as Administrator.'}))

# 64-bit
$is64 = [Environment]::Is64BitOperatingSystem
$checks += New-Check '64-bit Windows' ($(if ($is64) {'PASS'} else {'FAIL'})) `
    ("OS 64-bit: $is64")

# PowerShell
$psv = $PSVersionTable.PSVersion.ToString()
$checks += New-Check 'PowerShell' 'PASS' "PowerShell $psv"

# ADK (delegates to Get-ADKInstallation.ps1 if present)
$adkStatus = 'FAIL'; $adkSummary = 'ADK not detected.'
$adkScript = Join-Path $PSScriptRoot 'Get-ADKInstallation.ps1'
if (Test-Path $adkScript) {
    try {
        $adk = & $adkScript
        if ($adk.Found) { $adkStatus = 'PASS'; $adkSummary = "ADK $($adk.Version) at $($adk.AdkRoot)" }
        elseif ($adk.AdkRoot) { $adkStatus = 'WARNING'; $adkSummary = "ADK partially detected at $($adk.AdkRoot)" }
    } catch { $adkSummary = "ADK detection error: $($_.Exception.Message)" }
}
$checks += New-Check 'Windows ADK' $adkStatus $adkSummary

$overall = if ($checks.Status -contains 'FAIL') {'FAIL'} elseif ($checks.Status -contains 'WARNING') {'WARNING'} else {'PASS'}
$result = [ordered]@{ TimestampUtc = (Get-Date).ToUniversalTime().ToString('o'); Overall = $overall; Checks = $checks }

if ($AsJson) { $result | ConvertTo-Json -Depth 5 } else { [pscustomobject]$result }
