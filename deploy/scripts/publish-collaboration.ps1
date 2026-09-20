#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$BackendOutput = 'D:\Code\Deploy\Collaboration.EmbeddedHost\backend',
    [string]$FrontendOutput = 'D:\Code\Deploy\Collaboration.EmbeddedHost\frontend',
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$backendOutputPath = [IO.Path]::GetFullPath($BackendOutput).TrimEnd('\', '/')
$frontendOutputPath = [IO.Path]::GetFullPath($FrontendOutput).TrimEnd('\', '/')
foreach ($outputPath in @($backendOutputPath, $frontendOutputPath)) {
    if ($outputPath -eq [IO.Path]::GetPathRoot($outputPath).TrimEnd('\', '/')) {
        throw "A drive root cannot be a publish directory: $outputPath"
    }
}
if ($backendOutputPath -eq $frontendOutputPath -or
    $backendOutputPath.StartsWith($frontendOutputPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $frontendOutputPath.StartsWith($backendOutputPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Backend and frontend publish directories must be distinct and non-nested.'
}

$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ('pf06-publish-' + [guid]::NewGuid().ToString('N'))
$backendStage = Join-Path $stagingRoot 'backend'
$frontendStage = Join-Path $stagingRoot 'frontend'
$frontendSource = Join-Path $repoRoot 'src\frontend'
$frontendBundle = Join-Path $frontendSource 'dist-collaboration'
$frontendWebConfig = Join-Path $repoRoot 'deploy\iis\collaboration-frontend.web.config'
$backendProject = Join-Path $repoRoot 'src\backend\src\Hosts\IndustrialPlatform.Collaboration.EmbeddedHost\IndustrialPlatform.Collaboration.EmbeddedHost.csproj'

function Assert-CommandSucceeded([string]$name) {
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE. Publish directories were not updated." }
}

function Copy-PublishItems([string]$source, [string]$destination) {
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
    }
}

try {
    New-Item -ItemType Directory -Path $backendStage, $frontendStage -Force | Out-Null

    Write-Host 'Publishing standalone backend...'
    & dotnet publish $backendProject --configuration Release --output $backendStage
    Assert-CommandSucceeded 'dotnet publish'

    Write-Host 'Building standalone frontend...'
    $previousEnvironment = [Environment]::GetEnvironmentVariable('VITE_DEPLOYMENT_ENVIRONMENT', 'Process')
    try {
        $env:VITE_DEPLOYMENT_ENVIRONMENT = 'PROD'
        Push-Location -LiteralPath $frontendSource
        try {
            & pnpm install --frozen-lockfile
            Assert-CommandSucceeded 'pnpm install'
            & pnpm build:collaboration
            Assert-CommandSucceeded 'pnpm build:collaboration'
        }
        finally { Pop-Location }
    }
    finally {
        if ($null -eq $previousEnvironment) { Remove-Item Env:VITE_DEPLOYMENT_ENVIRONMENT -ErrorAction SilentlyContinue }
        else { $env:VITE_DEPLOYMENT_ENVIRONMENT = $previousEnvironment }
    }

    Copy-PublishItems $frontendBundle $frontendStage
    Copy-Item -LiteralPath $frontendWebConfig -Destination (Join-Path $frontendStage 'web.config') -Force
    foreach ($required in @(
        (Join-Path $backendStage 'IndustrialPlatform.Collaboration.EmbeddedHost.dll'),
        (Join-Path $backendStage 'web.config'),
        (Join-Path $frontendStage 'index.html'),
        (Join-Path $frontendStage 'assets'),
        (Join-Path $frontendStage 'web.config')
    )) {
        if (-not (Test-Path -LiteralPath $required)) { throw "Missing publish artifact: $required" }
    }
    if (Get-ChildItem -LiteralPath $backendStage -Recurse -File -Filter 'appsettings*.json' | Select-Object -First 1) {
        throw 'Backend output contains appsettings files; deployment stopped.'
    }

    if ($BuildOnly) {
        Write-Host 'BuildOnly: backend and frontend artifacts verified; publish directories were not updated.'
        return
    }

    New-Item -ItemType Directory -Path $backendOutputPath, $frontendOutputPath -Force | Out-Null
    $offlineFile = Join-Path $backendOutputPath 'app_offline.htm'
    if (Test-Path -LiteralPath $offlineFile) {
        throw "Existing app_offline.htm was not overwritten: $offlineFile"
    }
    try {
        Set-Content -LiteralPath $offlineFile -Value 'Updating standalone collaboration host.' -Encoding Ascii
        Start-Sleep -Seconds 2
        Copy-PublishItems $backendStage $backendOutputPath
        # Copy assets before index.html so the new entry never points to missing assets.
        Get-ChildItem -LiteralPath $frontendStage -Force |
            Where-Object { $_.Name -ne 'index.html' } |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $frontendOutputPath -Recurse -Force }
        Copy-Item -LiteralPath (Join-Path $frontendStage 'index.html') -Destination $frontendOutputPath -Force
    }
    finally { Remove-Item -LiteralPath $offlineFile -Force -ErrorAction SilentlyContinue }

    Write-Host "Backend:  $backendOutputPath"
    Write-Host "Frontend: $frontendOutputPath"
    Write-Host 'Publish complete. Existing appsettings and database files were preserved. Check IIS and /_backend/health/ready as described in README.'
    if (-not (Test-Path -LiteralPath (Join-Path $backendOutputPath 'appsettings.json'))) {
        Write-Warning 'backend/appsettings.json is missing. Create the production host configuration before starting the IIS application.'
    }
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
    $resolvedStage = [IO.Path]::GetFullPath($stagingRoot)
    if ($resolvedStage.StartsWith($tempRoot + '\pf06-publish-', [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
