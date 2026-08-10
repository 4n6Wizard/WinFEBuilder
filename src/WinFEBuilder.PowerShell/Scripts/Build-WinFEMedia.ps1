<#
.SYNOPSIS
    (Milestone 2) Runs an official WinFE media build batch file from a workspace and captures output.
.DESCRIPTION
    Does NOT reimplement the WinFE build. Invokes the supplied official batch file with a hidden
    window, explicit working directory, and captured stdout/stderr/exit code.
.PARAMETER ScriptPath   Full path to the official build .bat inside the workspace.
.PARAMETER WorkingDirectory  Working directory (normally the workspace framework folder).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ScriptPath,
    [Parameter(Mandatory = $true)] [string] $WorkingDirectory,
    [switch] $AsJson
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) { throw "Build script not found: $ScriptPath" }
if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) { throw "Working directory not found: $WorkingDirectory" }

$start = Get-Date
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $env:ComSpec       # cmd.exe, to execute the .bat
$psi.Arguments = "/c `"$ScriptPath`""
$psi.WorkingDirectory = $WorkingDirectory
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true

$p = [System.Diagnostics.Process]::Start($psi)
$stdout = $p.StandardOutput.ReadToEnd()
$stderr = $p.StandardError.ReadToEnd()
$p.WaitForExit()
$finish = Get-Date

$result = [ordered]@{
    ScriptPath = $ScriptPath
    ExitCode   = $p.ExitCode
    Success    = ($p.ExitCode -eq 0)
    StartTime  = $start.ToString('o')
    FinishTime = $finish.ToString('o')
    DurationMs = ($finish - $start).TotalMilliseconds
    StdOut     = $stdout
    StdErr     = $stderr
}
if ($AsJson) { $result | ConvertTo-Json -Depth 4 } else { [pscustomobject]$result }
