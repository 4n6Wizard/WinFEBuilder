<#
.SYNOPSIS
    (Milestone 2) Runs the official WinFE ISO build batch file and reports the produced ISO.
.DESCRIPTION
    Invokes the supplied official ISO .bat, captures output, then attempts to locate the newest
    .iso under the search directory. Does NOT reimplement oscdimg logic.
.PARAMETER ScriptPath        Full path to the official ISO build .bat.
.PARAMETER WorkingDirectory  Working directory for the batch.
.PARAMETER IsoSearchDir      Directory to search for the produced .iso (defaults to WorkingDirectory).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ScriptPath,
    [Parameter(Mandatory = $true)] [string] $WorkingDirectory,
    [string] $IsoSearchDir,
    [switch] $AsJson
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) { throw "ISO script not found: $ScriptPath" }
if (-not $IsoSearchDir) { $IsoSearchDir = $WorkingDirectory }

$build = & (Join-Path $PSScriptRoot 'Build-WinFEMedia.ps1') -ScriptPath $ScriptPath -WorkingDirectory $WorkingDirectory

$iso = Get-ChildItem -LiteralPath $IsoSearchDir -Filter *.iso -Recurse -ErrorAction SilentlyContinue |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1

$result = [ordered]@{
    ExitCode   = $build.ExitCode
    Success    = ($build.ExitCode -eq 0 -and $iso -ne $null -and $iso.Length -gt 0)
    IsoPath    = if ($iso) { $iso.FullName } else { $null }
    IsoSize    = if ($iso) { $iso.Length } else { 0 }
    Sha256     = if ($iso) { (Get-FileHash -LiteralPath $iso.FullName -Algorithm SHA256).Hash.ToLower() } else { $null }
    StdOut     = $build.StdOut
    StdErr     = $build.StdErr
}
if (-not $iso) { $result['Error'] = 'No ISO was found after the build.' }
if ($AsJson) { $result | ConvertTo-Json -Depth 4 } else { [pscustomobject]$result }
