#requires -Version 5.1
<#
.SYNOPSIS
    Resets the Development admin password and writes one-time credentials to a protected file.

.EXAMPLE
    .\scripts\Reset-DevelopmentAdmin.ps1
    .\scripts\Reset-DevelopmentAdmin.ps1 -CredentialOutput D:\secrets\reset-admin.json
#>
[CmdletBinding()]
param(
    [string]$CredentialOutput
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$identityProject = Join-Path $repoRoot 'src\backend\src\Services\Identity\IndustrialPlatform.Identity.Api\IndustrialPlatform.Identity.Api.csproj'
$localConfiguration = Join-Path $repoRoot 'src\backend\appsettings.Development.local.json'
$cliHome = Join-Path $repoRoot '.dotnet_cli_home'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet CLI was not found. Install the .NET 10 SDK.'
}
if (-not (Test-Path -LiteralPath $localConfiguration -PathType Leaf)) {
    throw "Local Development configuration was not found: $localConfiguration"
}

if (-not $CredentialOutput) {
    $defaultDir = Join-Path $env:LOCALAPPDATA 'IndustrialPlatform'
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    $CredentialOutput = Join-Path $defaultDir "reset-admin-$stamp.json"
}
if (-not [System.IO.Path]::IsPathRooted($CredentialOutput)) {
    throw "Credential output path must be absolute: $CredentialOutput"
}
if (Test-Path -LiteralPath $CredentialOutput) {
    throw "Credential output file already exists; refusing to overwrite: $CredentialOutput"
}

$outputDir = Split-Path -Parent $CredentialOutput
if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Write-Host "[reset-development-admin] Resetting admin. One-time credentials will be written to: $CredentialOutput"

$savedCliHome = $env:DOTNET_CLI_HOME
$env:DOTNET_CLI_HOME = $cliHome
try {
    & dotnet run --project $identityProject --launch-profile http -- --reset-development-admin --credential-output $CredentialOutput
    $exitCode = $LASTEXITCODE
} finally {
    $env:DOTNET_CLI_HOME = $savedCliHome
}

exit $exitCode
