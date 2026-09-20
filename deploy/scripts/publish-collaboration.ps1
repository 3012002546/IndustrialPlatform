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
        throw "不能将磁盘根目录作为发布目录: $outputPath"
    }
}
if ($backendOutputPath -eq $frontendOutputPath -or
    $backendOutputPath.StartsWith($frontendOutputPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $frontendOutputPath.StartsWith($backendOutputPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw '前后端发布目录不能相同或互相包含。'
}

$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ('pf06-publish-' + [guid]::NewGuid().ToString('N'))
$backendStage = Join-Path $stagingRoot 'backend'
$frontendStage = Join-Path $stagingRoot 'frontend'
$frontendSource = Join-Path $repoRoot 'src\frontend'
$frontendBundle = Join-Path $frontendSource 'dist-collaboration'
$frontendWebConfig = Join-Path $repoRoot 'deploy\iis\collaboration-frontend.web.config'
$backendProject = Join-Path $repoRoot 'src\backend\src\Hosts\IndustrialPlatform.Collaboration.EmbeddedHost\IndustrialPlatform.Collaboration.EmbeddedHost.csproj'

function Assert-CommandSucceeded([string]$name) {
    if ($LASTEXITCODE -ne 0) { throw "$name 失败，退出码 $LASTEXITCODE。发布目录未更新。" }
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
        if (-not (Test-Path -LiteralPath $required)) { throw "发布产物缺失: $required" }
    }
    if (Get-ChildItem -LiteralPath $backendStage -Recurse -File -Filter 'appsettings*.json' | Select-Object -First 1) {
        throw '后端产物包含 appsettings 配置，已停止发布。'
    }

    if ($BuildOnly) {
        Write-Host 'BuildOnly: 前后端构建及产物检查通过，未更新发布目录。'
        return
    }

    New-Item -ItemType Directory -Path $backendOutputPath, $frontendOutputPath -Force | Out-Null
    $offlineFile = Join-Path $backendOutputPath 'app_offline.htm'
    if (Test-Path -LiteralPath $offlineFile) {
        throw "后端已有 app_offline.htm，未覆盖现有维护状态: $offlineFile"
    }
    try {
        Set-Content -LiteralPath $offlineFile -Value 'Updating standalone collaboration host.' -Encoding Ascii
        Start-Sleep -Seconds 2
        Copy-PublishItems $backendStage $backendOutputPath
        # 先更新静态资源，最后替换入口 HTML，避免入口引用尚未复制的资源。
        Get-ChildItem -LiteralPath $frontendStage -Force |
            Where-Object { $_.Name -ne 'index.html' } |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $frontendOutputPath -Recurse -Force }
        Copy-Item -LiteralPath (Join-Path $frontendStage 'index.html') -Destination $frontendOutputPath -Force
    }
    finally { Remove-Item -LiteralPath $offlineFile -Force -ErrorAction SilentlyContinue }

    Write-Host "Backend:  $backendOutputPath"
    Write-Host "Frontend: $frontendOutputPath"
    Write-Host '发布完成。现有 appsettings 配置和数据库未修改；请按 README 检查 IIS 与 /_backend/health/ready。'
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
    $resolvedStage = [IO.Path]::GetFullPath($stagingRoot)
    if ($resolvedStage.StartsWith($tempRoot + '\pf06-publish-', [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
