<#
.SYNOPSIS
    Read-only validation of a selected WinFE framework folder. Emits JSON.
.PARAMETER FrameworkPath
    Path to the extracted WinFE framework directory.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $FrameworkPath,
    [switch] $AsJson
)

$ErrorActionPreference = 'Stop'

$knownScripts = @('MakeWinFEx64-x86.bat','Makex64-x86-CD.bat','MakeWinFEx64.bat','MakeWinFEx86.bat','MakePE.bat')
$expectedDirs = @('Drivers','Programs','Wallpaper')

$result = [ordered]@{
    SourcePath           = $FrameworkPath
    DirectoryExists      = $false
    IsValid              = $false
    Status               = 'FAIL'
    Summary              = ''
    BuildScripts         = @()
    Components           = @()
    ExpectedItemsFound   = @()
    ExpectedItemsMissing = @()
    PossibleDoubleNesting= $false
    SupportsX64          = $false
    Warnings             = @()
}

if (-not (Test-Path -LiteralPath $FrameworkPath -PathType Container)) {
    $result.Summary = 'Directory does not exist.'
    if ($AsJson) { $result | ConvertTo-Json -Depth 5 } else { [pscustomobject]$result }
    return
}
$result.DirectoryExists = $true

$topFiles = Get-ChildItem -LiteralPath $FrameworkPath -File -ErrorAction SilentlyContinue
$topDirs  = Get-ChildItem -LiteralPath $FrameworkPath -Directory -ErrorAction SilentlyContinue

function Test-IsBuildScript($name) {
    if ($knownScripts -contains $name) { return $true }
    if ($name -notlike '*.bat') { return $false }
    $l = $name.ToLower()
    return ($l -like '*winfe*') -or ($l -like 'make*' -and ($l -like '*pe*' -or $l -like '*cd*' -or $l -like '*x64*' -or $l -like '*x86*'))
}

$allFiles = Get-ChildItem -LiteralPath $FrameworkPath -File -Recurse -ErrorAction SilentlyContinue
foreach ($f in $allFiles) {
    if (Test-IsBuildScript $f.Name) {
        $sha = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLower()
        $result.BuildScripts += [ordered]@{ Name = $f.Name; Size = $f.Length; Sha256 = $sha; ZeroBytes = ($f.Length -eq 0) }
    } elseif ($f.Extension -in '.exe','.dll','.wim') {
        $result.Components += [ordered]@{ Name = $f.Name; Size = $f.Length }
    }
}

# Double-nesting heuristic
$topHasScripts = @($topFiles | Where-Object { Test-IsBuildScript $_.Name }).Count -gt 0
$childWithScripts = 0
foreach ($d in $topDirs) {
    $has = Get-ChildItem -LiteralPath $d.FullName -Filter *.bat -ErrorAction SilentlyContinue |
           Where-Object { Test-IsBuildScript $_.Name }
    if ($has) { $childWithScripts++ }
}
$result.PossibleDoubleNesting = ((-not $topHasScripts) -and ($childWithScripts -ge 1))

foreach ($k in $knownScripts) { if ($result.BuildScripts.Name -contains $k) { $result.ExpectedItemsFound += $k } }
foreach ($d in $expectedDirs) {
    if ($topDirs.Name -contains $d) { $result.ExpectedItemsFound += "$d\" } else { $result.ExpectedItemsMissing += "$d\" }
}

$allNames = @($result.BuildScripts.Name) + @($result.Components.Name)
$result.SupportsX64 = [bool](@($allNames | Where-Object { $_ -match '(?i)x64|amd64' }).Count -gt 0)

$zero = @($result.BuildScripts | Where-Object { $_.ZeroBytes })
foreach ($z in $zero) { $result.Warnings += "Zero-byte build script: $($z.Name)" }

if ($result.BuildScripts.Count -eq 0) {
    $result.Status = 'FAIL'
    $result.Summary = if ($result.PossibleDoubleNesting) { 'No scripts here; select the inner folder.' } else { 'No WinFE build scripts found.' }
} elseif ($zero.Count -gt 0) {
    $result.Status = 'FAIL'; $result.Summary = 'Zero-byte build scripts; framework may be corrupt.'
} elseif (-not $result.SupportsX64) {
    $result.IsValid = $true; $result.Status = 'WARNING'; $result.Summary = 'Scripts found; x64 support not confirmed.'
} elseif ($result.ExpectedItemsMissing.Count -gt 0) {
    $result.IsValid = $true; $result.Status = 'WARNING'; $result.Summary = "Valid; missing: $($result.ExpectedItemsMissing -join ', ')"
} else {
    $result.IsValid = $true; $result.Status = 'PASS'; $result.Summary = "Validated: $($result.BuildScripts.Count) script(s), x64 supported."
}

if ($AsJson) { $result | ConvertTo-Json -Depth 6 } else { [pscustomobject]$result }
