<#
.SYNOPSIS
    Tests and publishes Synapse Mouse Adjustments.

.EXAMPLE
    .\build.ps1                       # self-contained single-file exe for win-x64 in .\publish\win-x64
    .\build.ps1 -Runtime win-arm64    # ARM64 build
    .\build.ps1 -FrameworkDependent   # small exe that needs the .NET 10 Desktop Runtime installed
#>
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$FrameworkDependent,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not $SkipTests) {
    dotnet test tests/SynapseMouse.Core.Tests/SynapseMouse.Core.Tests.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

$output = Join-Path "publish" $Runtime
$selfContained = if ($FrameworkDependent) { "false" } else { "true" }

dotnet publish src/SynapseMouse.App/SynapseMouse.App.csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=$selfContained `
    -p:DebugType=none `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host ""
Write-Host "Built: $(Resolve-Path (Join-Path $output 'SynapseMouseAdjustments.exe'))" -ForegroundColor Green
