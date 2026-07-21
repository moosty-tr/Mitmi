#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ProjectPath = "src/Mitmi.Host.Console/Mitmi.Host.Console.csproj",
    [string]$Configuration = "Release",
    [string]$Framework = "net10.0",
    [string]$WorkingRoot,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-PathFromRepo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $script:RepoRoot $PathValue))
}

function Assert-TextContains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,
        [Parameter(Mandatory = $true)]
        [string]$Expected,
        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if (!$Text.Contains($Expected)) {
        throw "$Context did not contain expected text '$Expected'."
    }
}

function Invoke-SmokeProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [int]$ExpectedExitCode = 0,
        [string[]]$ExpectedOutput = @()
    )

    Write-Host "==> $Name"
    Push-Location $WorkingDirectory
    try {
        $captured = & $FilePath @ArgumentList 2>&1
        $exitCode = if ($null -eq $global:LASTEXITCODE) { 0 } else { $global:LASTEXITCODE }
    }
    finally {
        Pop-Location
    }

    $outputText = ($captured | Out-String).Trim()
    if ($outputText.Length -gt 0) {
        Write-Host $outputText
    }

    if ($exitCode -ne $ExpectedExitCode) {
        throw "$Name exited with $exitCode; expected $ExpectedExitCode."
    }

    foreach ($expected in $ExpectedOutput) {
        Assert-TextContains -Text $outputText -Expected $expected -Context $Name
    }

    return $outputText
}

function Get-PublishedMitmiCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    $windowsExe = Join-Path $PublishDirectory "mitmi.exe"
    if (Test-Path -LiteralPath $windowsExe) {
        return [pscustomobject]@{
            FilePath = $windowsExe
            PrefixArguments = @()
        }
    }

    $unixExe = Join-Path $PublishDirectory "mitmi"
    if (Test-Path -LiteralPath $unixExe) {
        return [pscustomobject]@{
            FilePath = $unixExe
            PrefixArguments = @()
        }
    }

    $dll = Join-Path $PublishDirectory "mitmi.dll"
    if (Test-Path -LiteralPath $dll) {
        return [pscustomobject]@{
            FilePath = "dotnet"
            PrefixArguments = @($dll)
        }
    }

    $legacyWindowsExe = Join-Path $PublishDirectory "Mitmi.Host.Console.exe"
    if (Test-Path -LiteralPath $legacyWindowsExe) {
        return [pscustomobject]@{
            FilePath = $legacyWindowsExe
            PrefixArguments = @()
        }
    }

    $legacyUnixExe = Join-Path $PublishDirectory "Mitmi.Host.Console"
    if (Test-Path -LiteralPath $legacyUnixExe) {
        return [pscustomobject]@{
            FilePath = $legacyUnixExe
            PrefixArguments = @()
        }
    }

    $legacyDll = Join-Path $PublishDirectory "Mitmi.Host.Console.dll"
    if (Test-Path -LiteralPath $legacyDll) {
        return [pscustomobject]@{
            FilePath = "dotnet"
            PrefixArguments = @($legacyDll)
        }
    }

    throw "Published MITMI executable was not found in '$PublishDirectory'."
}

function Invoke-Mitmi {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [object]$Command,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [string[]]$ExpectedOutput = @()
    )

    $arguments = @($Command.PrefixArguments) + $ArgumentList
    Invoke-SmokeProcess `
        -Name $Name `
        -FilePath $Command.FilePath `
        -ArgumentList $arguments `
        -WorkingDirectory $WorkingDirectory `
        -ExpectedOutput $ExpectedOutput | Out-Null
}

function Assert-ZipContainsEntry {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    if (!$Archive.GetEntry($EntryName)) {
        throw "Diagnostics bundle did not contain '$EntryName'."
    }
}

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedProjectPath = Resolve-PathFromRepo $ProjectPath
$hasProvidedWorkingRoot = $PSBoundParameters.ContainsKey("WorkingRoot") -and ![string]::IsNullOrWhiteSpace($WorkingRoot)

if ($hasProvidedWorkingRoot) {
    $workingRootPath = [System.IO.Path]::GetFullPath($WorkingRoot)
}
else {
    $workingRootPath = Join-Path ([System.IO.Path]::GetTempPath()) ("mitmi-release-smoke-" + [Guid]::NewGuid().ToString("N"))
}

if (Test-Path -LiteralPath $workingRootPath) {
    $existingChild = Get-ChildItem -LiteralPath $workingRootPath -Force | Select-Object -First 1
    if ($existingChild) {
        throw "Working root '$workingRootPath' must be empty."
    }
}
else {
    New-Item -ItemType Directory -Path $workingRootPath | Out-Null
}

$publishDirectory = Join-Path $workingRootPath "publish"
$runDirectory = Join-Path $workingRootPath "run"
New-Item -ItemType Directory -Path $publishDirectory | Out-Null
New-Item -ItemType Directory -Path $runDirectory | Out-Null

$succeeded = $false
try {
    Invoke-SmokeProcess `
        -Name "dotnet publish" `
        -FilePath "dotnet" `
        -ArgumentList @("publish", $resolvedProjectPath, "--configuration", $Configuration, "--framework", $Framework, "--output", $publishDirectory) `
        -WorkingDirectory $script:RepoRoot | Out-Null

    $mitmiCommand = Get-PublishedMitmiCommand -PublishDirectory $publishDirectory
    $configPath = Join-Path $runDirectory "mitmi.config.json"
    $bundlePath = Join-Path $runDirectory "support/mitmi-diagnostics.zip"

    Invoke-Mitmi `
        -Name "published --help" `
        -Command $mitmiCommand `
        -ArgumentList @("--help") `
        -WorkingDirectory $runDirectory `
        -ExpectedOutput @("Usage:", "mitmi --bundle-diagnostics <zip-path>")

    Invoke-Mitmi `
        -Name "published --init-config" `
        -Command $mitmiCommand `
        -ArgumentList @("--init-config", "--config", $configPath) `
        -WorkingDirectory $runDirectory `
        -ExpectedOutput @("CONFIGURATION_FILE_CREATED", "Created configuration template")

    if (!(Test-Path -LiteralPath $configPath)) {
        throw "Expected configuration file was not created at '$configPath'."
    }

    Invoke-Mitmi `
        -Name "published --validate-config" `
        -Command $mitmiCommand `
        -ArgumentList @("--config", $configPath, "--validate-config") `
        -WorkingDirectory $runDirectory `
        -ExpectedOutput @("MITMI v0.1 diagnostic proxy configuration", "Configuration valid.")

    $logDirectory = Join-Path $runDirectory "logs"
    $captureDirectory = Join-Path $runDirectory "captures"
    $summaryDirectory = Join-Path $captureDirectory "summaries"
    $reportDirectory = Join-Path $captureDirectory "reports"
    New-Item -ItemType Directory -Path $logDirectory, $summaryDirectory, $reportDirectory | Out-Null
    Set-Content -Path (Join-Path $logDirectory "mitmi.log") -Value "release smoke log" -Encoding UTF8
    Set-Content -Path (Join-Path $captureDirectory "mitmi-capture-release-smoke.ndjson") -Value '{"captureFormatVersion":1}' -Encoding UTF8
    Set-Content -Path (Join-Path $summaryDirectory "mitmi-modbus-analyzer-summary-release-smoke.ndjson") -Value '{"summaryFormatVersion":1}' -Encoding UTF8
    Set-Content -Path (Join-Path $reportDirectory "mitmi-modbus-device-discovery-release-smoke.md") -Value "# MITMI Modbus Device Discovery Report" -Encoding UTF8

    Invoke-Mitmi `
        -Name "published --bundle-diagnostics" `
        -Command $mitmiCommand `
        -ArgumentList @("--config", $configPath, "--bundle-diagnostics", $bundlePath) `
        -WorkingDirectory $runDirectory `
        -ExpectedOutput @("Created diagnostics bundle")

    if (!(Test-Path -LiteralPath $bundlePath)) {
        throw "Expected diagnostics bundle was not created at '$bundlePath'."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($bundlePath)
    try {
        Assert-ZipContainsEntry -Archive $archive -EntryName "configuration/mitmi.config.json"
        Assert-ZipContainsEntry -Archive $archive -EntryName "logs/mitmi.log"
        Assert-ZipContainsEntry -Archive $archive -EntryName "captures/mitmi-capture-release-smoke.ndjson"
        Assert-ZipContainsEntry -Archive $archive -EntryName "captures/summaries/mitmi-modbus-analyzer-summary-release-smoke.ndjson"
        Assert-ZipContainsEntry -Archive $archive -EntryName "captures/reports/mitmi-modbus-device-discovery-release-smoke.md"
        Assert-ZipContainsEntry -Archive $archive -EntryName "manifest.json"
    }
    finally {
        $archive.Dispose()
    }

    $succeeded = $true
    Write-Host "Release smoke test passed."
    if ($KeepArtifacts -or $hasProvidedWorkingRoot) {
        Write-Host "Artifacts: $workingRootPath"
    }
}
finally {
    if ($succeeded -and !$KeepArtifacts -and !$hasProvidedWorkingRoot) {
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        $resolvedWorkingRoot = [System.IO.Path]::GetFullPath($workingRootPath)
        $leafName = Split-Path -Path $resolvedWorkingRoot -Leaf
        if ($resolvedWorkingRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and $leafName.StartsWith("mitmi-release-smoke-", [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedWorkingRoot -Recurse -Force
        }
    }

    if (!$succeeded) {
        Write-Warning "Release smoke artifacts were left at '$workingRootPath'."
    }
}
