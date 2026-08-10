$ErrorActionPreference = "Stop"

# Portable: the repo root is wherever this script lives (no hard-coded machine path).
$ProjectRoot = $PSScriptRoot
$Solution = Join-Path $ProjectRoot "WinFEBuilder.sln"
$AuditRoot = Join-Path $ProjectRoot "audit"
$Timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$AuditDir = Join-Path $AuditRoot $Timestamp
$ReportPath = Join-Path $AuditDir "WinFEBuilder_Audit.txt"

New-Item -ItemType Directory -Path $AuditDir -Force | Out-Null

function Write-Audit {
    param(
        [AllowEmptyString()]
        [string]$Text = ""
    )

    Add-Content -Path $ReportPath -Value $Text -Encoding UTF8
    Write-Host $Text
}

function Write-StepOutput {
    param(
        [object[]]$Output
    )

    foreach ($Item in $Output) {
        if ($null -eq $Item) {
            Write-Audit ""
        }
        else {
            Write-Audit ([string]$Item)
        }
    }
}

function Run-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Audit ""
    Write-Audit "============================================================"
    Write-Audit $Name
    Write-Audit "============================================================"

    try {
        $global:LASTEXITCODE = 0

        $StepOutput = @(& $Action 2>&1)
        $NativeExitCode = $LASTEXITCODE

        Write-StepOutput -Output $StepOutput

        if ($null -ne $NativeExitCode -and $NativeExitCode -ne 0) {
            throw "$Name failed with exit code $NativeExitCode."
        }

        Write-Audit "[PASS] $Name"
    }
    catch {
        Write-Audit "[FAIL] $Name"
        Write-Audit $_.Exception.Message
        throw
    }
}

function Get-SourceFiles {
    Get-ChildItem $ProjectRoot -Recurse -File -Force |
        Where-Object {
            $_.FullName -notmatch '\\(bin|obj|logs|workspace|output|reports|audit|\.vs)\\'
        }
}

if (-not (Test-Path $ProjectRoot -PathType Container)) {
    throw "Project folder not found: $ProjectRoot"
}

if (-not (Test-Path $Solution -PathType Leaf)) {
    throw "Solution file not found: $Solution"
}

Write-Host ""
Write-Host "Starting WinFE Builder audit..." -ForegroundColor Cyan
Write-Host "Project: $ProjectRoot"
Write-Host "Report:  $ReportPath"
Write-Host ""

Write-Audit "WinFE Builder - Pre-Release Audit"
Write-Audit "Date:         $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Audit "Project root: $ProjectRoot"
Write-Audit "Computer:     $env:COMPUTERNAME"
Write-Audit "User:         $env:USERNAME"

Run-Step "1. .NET SDK information" {
    dotnet --info
}

Run-Step "2. Clean solution" {
    dotnet clean $Solution `
        --configuration Release `
        --verbosity minimal
}

Run-Step "3. Restore NuGet packages" {
    dotnet restore $Solution `
        --force-evaluate `
        --verbosity minimal
}

Run-Step "4. Build Release x64" {
    dotnet build $Solution `
        --configuration Release `
        --no-restore `
        -p:Platform=x64 `
        -p:TreatWarningsAsErrors=true `
        --verbosity minimal
}

Run-Step "5. Run all tests" {
    dotnet test $Solution `
        --configuration Release `
        --no-build `
        -p:Platform=x64 `
        --logger "console;verbosity=normal" `
        --logger "trx;LogFileName=WinFEBuilderTests.trx" `
        --results-directory $AuditDir
}

Run-Step "6. Check generated folders" {
    $GeneratedFolders = Get-ChildItem $ProjectRoot -Directory -Recurse -Force |
        Where-Object {
            $_.Name -in @(
                "bin",
                "obj",
                "logs",
                "workspace",
                "output",
                "reports",
                ".vs"
            )
        } |
        Sort-Object FullName

    if ($GeneratedFolders) {
        "Generated folders found:"
        $GeneratedFolders.FullName | ForEach-Object {
            "  $_"
        }
    }
    else {
        "No generated folders found."
    }
}

Run-Step "7. Search for unfinished markers" {
    $Files = Get-SourceFiles |
        Where-Object {
            $_.Extension -in @(
                ".cs",
                ".csproj",
                ".json",
                ".md",
                ".ps1"
            )
        }

    $Markers = $Files |
        Select-String `
            -Pattern 'TODO|FIXME|HACK|XXX|NotImplementedException' `
            -CaseSensitive:$false

    if ($Markers) {
        $Markers | ForEach-Object {
            "$($_.Path):$($_.LineNumber): $($_.Line.Trim())"
        }
    }
    else {
        "No unfinished-code markers found."
    }
}

Run-Step "8. Search for hard-coded local paths" {
    $Files = Get-SourceFiles |
        Where-Object {
            $_.Extension -in @(
                ".cs",
                ".json",
                ".md",
                ".ps1",
                ".config"
            )
        }

    $Matches = $Files |
        Select-String `
            -Pattern 'C:\\Users\\|C:\\WinFEBuilder|Desktop\\WinFE' `
            -CaseSensitive:$false

    if ($Matches) {
        $Matches | ForEach-Object {
            "$($_.Path):$($_.LineNumber): $($_.Line.Trim())"
        }
    }
    else {
        "No hard-coded development paths found."
    }
}

Run-Step "9. Search for possible secrets" {
    $Files = Get-SourceFiles |
        Where-Object {
            $_.Extension -in @(
                ".cs",
                ".json",
                ".config",
                ".xml",
                ".ps1"
            )
        }

    $SecretMatches = $Files |
        Select-String `
            -Pattern 'password\s*=|api[_-]?key\s*=|secret\s*=|connectionstring\s*=' `
            -CaseSensitive:$false

    if ($SecretMatches) {
        $SecretMatches | ForEach-Object {
            "$($_.Path):$($_.LineNumber): $($_.Line.Trim())"
        }
    }
    else {
        "No obvious embedded secrets found."
    }
}

Run-Step "10. Locate application project" {
    $AppProject = Get-ChildItem $ProjectRoot -Recurse -File `
        -Filter "WinFEBuilder.App.csproj" |
        Select-Object -First 1

    if (-not $AppProject) {
        throw "WinFEBuilder.App.csproj was not found."
    }

    "Application project: $($AppProject.FullName)"
}

Run-Step "11. Publish Release win-x64" {
    $AppProject = Get-ChildItem $ProjectRoot -Recurse -File `
        -Filter "WinFEBuilder.App.csproj" |
        Select-Object -First 1

    if (-not $AppProject) {
        throw "WinFEBuilder.App.csproj was not found."
    }

    $PublishDir = Join-Path $AuditDir "publish"

    dotnet publish $AppProject.FullName `
        --configuration Release `
        --runtime win-x64 `
        --self-contained false `
        -p:Platform=x64 `
        --output $PublishDir
}

Run-Step "12. Verify published executable" {
    $PublishDir = Join-Path $AuditDir "publish"

    if (-not (Test-Path $PublishDir -PathType Container)) {
        throw "Publish folder was not created: $PublishDir"
    }

    $Executable = Get-ChildItem $PublishDir -File |
        Where-Object {
            $_.Name -in @(
                "WinFEBuilder.exe",
                "WinFEBuilder.App.exe"
            )
        } |
        Select-Object -First 1

    if (-not $Executable) {
        $Executable = Get-ChildItem $PublishDir -File -Filter "*.exe" |
            Select-Object -First 1
    }

    if (-not $Executable) {
        throw "No published executable was found in: $PublishDir"
    }

    "Published executable: $($Executable.FullName)"
    "Executable size: $($Executable.Length) bytes"
}

Run-Step "13. Generate SHA-256 hashes" {
    $PublishDir = Join-Path $AuditDir "publish"
    $HashFile = Join-Path $AuditDir "SHA256SUMS.txt"

    if (-not (Test-Path $PublishDir -PathType Container)) {
        throw "Publish folder was not found: $PublishDir"
    }

    $PublishedFiles = Get-ChildItem $PublishDir -File -Recurse |
        Sort-Object FullName

    if (-not $PublishedFiles) {
        throw "No files were found in the publish folder."
    }

    $HashLines = foreach ($File in $PublishedFiles) {
        $Hash = Get-FileHash $File.FullName -Algorithm SHA256
        $RelativePath = $File.FullName.Substring($PublishDir.Length).TrimStart("\")

        "$($Hash.Hash)  $RelativePath"
    }

    $HashLines | Set-Content $HashFile -Encoding UTF8

    "Hashes written to: $HashFile"
    "Files hashed: $($PublishedFiles.Count)"
}

Write-Audit ""
Write-Audit "============================================================"
Write-Audit "AUDIT COMPLETE"
Write-Audit "============================================================"
Write-Audit "Report:    $ReportPath"
Write-Audit "Artifacts: $AuditDir"

Write-Host ""
Write-Host "AUDIT PASSED" -ForegroundColor Green
Write-Host "Report: $ReportPath"
Write-Host "Artifacts: $AuditDir"
Write-Host ""