#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$FrameworkDependent,
    [string]$OutputRoot,
    [string]$ReleaseRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-PathInside {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue,
        [Parameter(Mandatory = $true)]
        [string]$RootValue,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $fullPath = [System.IO.Path]::GetFullPath($PathValue)
    $fullRoot = [System.IO.Path]::GetFullPath($RootValue)
    if (!$fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description must stay inside '$fullRoot': $fullPath"
    }

    return $fullPath
}

function Copy-ReleaseFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory
    )

    if (!(Test-Path -LiteralPath $SourcePath)) {
        throw "Release file is missing: $SourcePath"
    }

    Copy-Item -LiteralPath $SourcePath -Destination $DestinationDirectory
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\Mitmi.Host.Console\Mitmi.Host.Console.csproj"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\publish"
}

if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    $ReleaseRoot = Join-Path $repoRoot "artifacts\release"
}

$outputRootFull = Assert-PathInside $OutputRoot $repoRoot "Output root"
$releaseRootFull = Assert-PathInside $ReleaseRoot $repoRoot "Release root"

$packageName = "mitmi-v$Version-$RuntimeIdentifier"
$packageDirectory = Assert-PathInside (Join-Path $outputRootFull $packageName) $outputRootFull "Package directory"
$zipPath = Assert-PathInside (Join-Path $releaseRootFull "$packageName.zip") $releaseRootFull "Release archive"

if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRootFull -Force | Out-Null

$selfContainedValue = if ($FrameworkDependent.IsPresent) { "false" } else { "true" }

$publishArguments = @(
    "publish",
    $projectPath,
    "--configuration",
    "Release",
    "--runtime",
    $RuntimeIdentifier,
    "--self-contained",
    $selfContainedValue,
    "--output",
    $packageDirectory,
    "-p:Version=$Version"
)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$executableName = if ($RuntimeIdentifier -like "win-*") { "mitmi.exe" } else { "mitmi" }
$executablePath = Join-Path $packageDirectory $executableName
if (!(Test-Path -LiteralPath $executablePath)) {
    throw "Expected executable was not produced: $executablePath"
}

Copy-ReleaseFile (Join-Path $repoRoot "README.md") $packageDirectory
Copy-ReleaseFile (Join-Path $repoRoot "LICENSE") $packageDirectory
Copy-ReleaseFile (Join-Path $repoRoot "THIRD_PARTY.md") $packageDirectory

$examplesSource = Join-Path $repoRoot "examples"
$examplesDestination = Join-Path $packageDirectory "examples"
if (Test-Path -LiteralPath $examplesSource) {
    Copy-Item -LiteralPath $examplesSource -Destination $examplesDestination -Recurse
}

Compress-Archive -Path (Join-Path $packageDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Published MITMI to $packageDirectory"
Write-Host "Created release archive $zipPath"
