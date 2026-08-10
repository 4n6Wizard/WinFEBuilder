<#
.SYNOPSIS
    Detects the Windows ADK and WinPE add-on. Read-only. Emits a JSON object.
.NOTES
    Mirrors the native detection in WinFEBuilder.Core.Services.AdkDetectionService so the
    detection can also be run/audited from the command line. PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [switch] $AsJson
)

$ErrorActionPreference = 'Stop'

function Get-KitsRoot {
    $keys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots'
    )
    foreach ($k in $keys) {
        try {
            $v = (Get-ItemProperty -Path $k -Name 'KitsRoot10' -ErrorAction Stop).KitsRoot10
            if ($v -and (Test-Path $v)) { return $v.TrimEnd('\') }
        } catch { }
    }
    foreach ($pf in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ($pf) {
            $c = Join-Path $pf 'Windows Kits\10'
            if (Test-Path $c) { return $c }
        }
    }
    return $null
}

$result = [ordered]@{
    Found                        = $false
    Version                      = $null
    AdkRoot                      = $null
    WinPeRoot                    = $null
    DismPath                     = $null
    OscdimgPath                  = $null
    DandISetEnvPath              = $null
    WinPeOptionalComponentsPath  = $null
    WinPeMediaPath               = $null
    SupportedArchitectures       = @()
    Warnings                     = @()
}

$kits = Get-KitsRoot
if (-not $kits) {
    $result.Warnings += 'No Windows Kits root located via registry or Program Files.'
} else {
    $adk   = Join-Path $kits 'Assessment and Deployment Kit'
    $dt    = Join-Path $adk 'Deployment Tools'
    $winpe = Join-Path $adk 'Windows Preinstallation Environment'

    $result.AdkRoot = if (Test-Path $adk) { $adk } else { $kits }

    $dandi = Join-Path $dt 'DandISetEnv.bat'
    if (Test-Path $dandi) { $result.DandISetEnvPath = $dandi } else { $result.Warnings += 'DandISetEnv.bat not found.' }

    foreach ($arch in 'amd64','x86') {
        $dism = Join-Path $dt "$arch\DISM\dism.exe"
        if (-not $result.DismPath -and (Test-Path $dism)) { $result.DismPath = $dism }
        $osc = Join-Path $dt "$arch\Oscdimg\oscdimg.exe"
        if (-not $result.OscdimgPath -and (Test-Path $osc)) { $result.OscdimgPath = $osc }
    }
    if (-not $result.DismPath)    { $result.Warnings += 'ADK DISM not found.' }
    if (-not $result.OscdimgPath) { $result.Warnings += 'Oscdimg not found.' }

    if (Test-Path $winpe) {
        $result.WinPeRoot = $winpe
        $ocs = Join-Path $winpe 'amd64\WinPE_OCs'
        if (Test-Path $ocs) { $result.WinPeOptionalComponentsPath = $ocs } else { $result.Warnings += 'WinPE_OCs (amd64) not found.' }
        $media = Join-Path $winpe 'amd64\Media'
        if (Test-Path $media) { $result.WinPeMediaPath = $media } else { $result.Warnings += 'WinPE media (amd64) not found.' }
        foreach ($arch in 'amd64','x86','arm64') {
            if (Test-Path (Join-Path $winpe $arch)) { $result.SupportedArchitectures += $arch }
        }
    } else {
        $result.Warnings += 'WinPE add-on directory not found.'
    }

    $bin = Join-Path $kits 'bin'
    if (Test-Path $bin) {
        $ver = Get-ChildItem $bin -Directory -ErrorAction SilentlyContinue |
               Where-Object { $_.Name -match '^\d+\.\d+\.\d+' } |
               Sort-Object { [version]($_.Name) } -Descending |
               Select-Object -First 1
        if ($ver) { $result.Version = $ver.Name }
    }

    $result.Found = [bool]($result.DismPath -and $result.OscdimgPath -and $result.WinPeRoot)
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 5
} else {
    [pscustomobject]$result
}
