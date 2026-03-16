[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [Parameter()]
    [string]$Framework = "net10.0",

    [Parameter()]
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$samplesRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $samplesRoot
$cecilProject = Join-Path $repoRoot "Cecil\Cecil.csproj"
$publisherProject = Join-Path $samplesRoot "Publisher\Publisher.csproj"
$consumerProject = Join-Path $samplesRoot "Consumer\Consumer.csproj"
$cecilExe = Join-Path $repoRoot "Cecil\bin\$Configuration\$Framework\Cecil.exe"

function Invoke-DotNetBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    Write-Host "Building $ProjectPath ($Configuration|$Framework)..."
    & dotnet build $ProjectPath -c $Configuration

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $ProjectPath"
    }
}

function Patch-MassTransitAssembly {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectName,

        [Parameter(Mandatory = $true)]
        [string]$AssemblyPath
    )

    if (-not (Test-Path $AssemblyPath)) {
        throw "MassTransit.dll not found for $ProjectName at '$AssemblyPath'. Build the project first or omit -SkipBuild."
    }

    Write-Host "Patching $ProjectName assembly: $AssemblyPath"
    & $cecilExe $AssemblyPath

    if ($LASTEXITCODE -ne 0) {
        throw "Cecil.exe failed for $ProjectName"
    }

    Write-Host "Cecil finished for $ProjectName."
    Write-Host ""
}

if (-not (Test-Path $cecilExe)) {
    throw "Cecil.exe was not found at '$cecilExe'. Build Cecil first or omit -SkipBuild."
}

$publisherMassTransit = Join-Path $samplesRoot "Publisher\bin\$Configuration\$Framework\MassTransit.dll"
$consumerMassTransit = Join-Path $samplesRoot "Consumer\bin\$Configuration\$Framework\MassTransit.dll"

Patch-MassTransitAssembly -ProjectName "Publisher" -AssemblyPath $publisherMassTransit
Patch-MassTransitAssembly -ProjectName "Consumer" -AssemblyPath $consumerMassTransit

Write-Host "Finished patching MassTransit.dll for Publisher and Consumer."
