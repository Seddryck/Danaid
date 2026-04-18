[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot ".."),
    [string]$SourceRoot = (Join-Path $PSScriptRoot "instructions"),
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message"
}

function Write-WarnMsg {
    param([string]$Message)
    Write-Warning $Message
}

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Copy-IfDifferent {
    param(
        [string]$SourceFile,
        [string]$DestinationFile
    )

    $destinationDirectory = Split-Path -Path $DestinationFile -Parent
    Ensure-Directory -Path $destinationDirectory

    $shouldCopy = $true

    if (Test-Path -LiteralPath $DestinationFile) {
        $sourceHash = (Get-FileHash -LiteralPath $SourceFile -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $DestinationFile -Algorithm SHA256).Hash
        $shouldCopy = $sourceHash -ne $destinationHash
    }

    if ($shouldCopy) {
        Copy-Item -LiteralPath $SourceFile -Destination $DestinationFile -Force
        Write-Info "Copied: $SourceFile -> $DestinationFile"
    }
    else {
        Write-Info "Unchanged: $DestinationFile"
    }
}

function Remove-StaleFiles {
    param(
        [string[]]$ExpectedFiles,
        [string]$TargetRoot
    )

    if (-not (Test-Path -LiteralPath $TargetRoot)) {
        return
    }

    $expectedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $ExpectedFiles) {
        [void]$expectedSet.Add(([System.IO.Path]::GetFullPath($file)))
    }

    $existingFiles = Get-ChildItem -LiteralPath $TargetRoot -File -Recurse
    foreach ($file in $existingFiles) {
        $fullName = [System.IO.Path]::GetFullPath($file.FullName)
        if (-not $expectedSet.Contains($fullName)) {
            Remove-Item -LiteralPath $file.FullName -Force
            Write-Info "Removed stale file: $($file.FullName)"
        }
    }

    Get-ChildItem -LiteralPath $TargetRoot -Directory -Recurse |
        Sort-Object FullName -Descending |
        ForEach-Object {
            if (-not (Get-ChildItem -LiteralPath $_.FullName -Force | Select-Object -First 1)) {
                Remove-Item -LiteralPath $_.FullName -Force
                Write-Info "Removed empty directory: $($_.FullName)"
            }
        }
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)

$githubRoot = Join-Path $RepositoryRoot ".github"
$instructionsTargetRoot = Join-Path $githubRoot "instructions"
$globalInstructionTarget = Join-Path $githubRoot "copilot-instructions.md"

Write-Info "Repository root: $RepositoryRoot"
Write-Info "Source root: $SourceRoot"

if (-not (Test-Path -LiteralPath $SourceRoot)) {
    throw "Source folder not found: $SourceRoot"
}

Ensure-Directory -Path $githubRoot
Ensure-Directory -Path $instructionsTargetRoot

$expectedFiles = [System.Collections.Generic.List[string]]::new()

$globalInstructionSource = Join-Path $SourceRoot "copilot-instructions.md"
if (Test-Path -LiteralPath $globalInstructionSource) {
    Copy-IfDifferent -SourceFile $globalInstructionSource -DestinationFile $globalInstructionTarget
    [void]$expectedFiles.Add([System.IO.Path]::GetFullPath($globalInstructionTarget))
}
else {
    Write-WarnMsg "Global instruction file not found: $globalInstructionSource"
}

$instructionFiles = Get-ChildItem -LiteralPath $SourceRoot -File -Recurse |
    Where-Object { $_.Name -like "*.instructions.md" }

foreach ($file in $instructionFiles) {
    $relativePath = $file.FullName.Substring($SourceRoot.Length).TrimStart('\', '/')
    $destination = Join-Path $instructionsTargetRoot $relativePath

    Copy-IfDifferent -SourceFile $file.FullName -DestinationFile $destination
    [void]$expectedFiles.Add([System.IO.Path]::GetFullPath($destination))
}

if ($Clean) {
    Write-Info "Cleaning stale generated files..."
    Remove-StaleFiles -ExpectedFiles $expectedFiles.ToArray() -TargetRoot $instructionsTargetRoot

    if ((-not (Test-Path -LiteralPath $globalInstructionSource)) -and (Test-Path -LiteralPath $globalInstructionTarget)) {
        Remove-Item -LiteralPath $globalInstructionTarget -Force
        Write-Info "Removed stale global instruction: $globalInstructionTarget"
    }
}

Write-Info "Sync completed successfully."
