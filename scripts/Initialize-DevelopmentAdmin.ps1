#requires -Version 5.1
<#
.SYNOPSIS
    Windows 本地 Development 一次性 admin 初始化脚本(仅首次创建时交付凭据)。

.DESCRIPTION
    复用 Identity 的 --initialize-admin 命令(底层 IdentityInitializationService),
    将首次创建的一次性凭据写入仅当前用户可访问的 JSON 文件,避免密码进入终端滚屏。

    前置校验(不打印任何配置值):
      - dotnet CLI 可用;
      - 本地私有配置存在且为合法 JSON;
      - RemoteDevelopment.Enabled = true;
      - DatabaseTopology 可解析(Shared 需 SharedDatabaseName;PerService 需 identity 映射)。
    校验通过后调用准确 Identity Api csproj(dotnet run --launch-profile http),
    透传其退出码。目标凭据文件已存在时直接拒绝,不调用 dotnet。

.PARAMETER CredentialOutput
    一次性凭据 JSON 输出路径(绝对路径)。可选;默认
    %LOCALAPPDATA%\IndustrialPlatform\bootstrap-admin-<UTC>.json。

.PARAMETER LocalConfigurationPath
    仅用于前置校验的本地私有配置路径。默认仓库 src\backend\appsettings.Development.local.json;
    实际加载路径由 Identity launch profile 注入,正常使用无需指定(测试场景可覆盖)。

.EXAMPLE
    .\scripts\Initialize-DevelopmentAdmin.ps1
    .\scripts\Initialize-DevelopmentAdmin.ps1 -CredentialOutput D:\secrets\bootstrap-admin.json
#>
[CmdletBinding()]
param(
    [string]$CredentialOutput,
    [string]$LocalConfigurationPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Locations
# ---------------------------------------------------------------------------
$ScriptDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir '..')).Path
$IdentityProject = Join-Path $RepoRoot 'src\backend\src\Services\Identity\IndustrialPlatform.Identity.Api\IndustrialPlatform.Identity.Api.csproj'
$CliHome = Join-Path $RepoRoot '.dotnet_cli_home'
if (-not $LocalConfigurationPath) {
    $LocalConfigurationPath = Join-Path $RepoRoot 'src\backend\appsettings.Development.local.json'
}

# ---------------------------------------------------------------------------
# Preconditions(绝不打印配置值/凭据)
# ---------------------------------------------------------------------------
function Assert-Preconditions {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '未找到 dotnet CLI。请安装 .NET 10 SDK 或从开发环境 shell 运行。'
    }

    if (-not (Test-Path -LiteralPath $LocalConfigurationPath -PathType Leaf)) {
        throw "未找到本地私有配置: $LocalConfigurationPath (RemoteDevelopment 联调需要)。"
    }

    $config = $null
    try {
        $config = Get-Content -LiteralPath $LocalConfigurationPath -Raw | ConvertFrom-Json
    } catch {
        throw "本地私有配置不是合法 JSON: $LocalConfigurationPath ($($_.Exception.Message))"
    }

    if ($null -eq $config.RemoteDevelopment -or -not $config.RemoteDevelopment.Enabled) {
        throw '本地私有配置未启用 RemoteDevelopment.Enabled,拒绝初始化(admin 初始化仅允许 Development + 可用基础设施)。'
    }

    $topology = $config.DatabaseTopology
    if ($null -eq $topology) {
        throw '本地私有配置缺少 DatabaseTopology 节,无法解析数据库拓扑。'
    }

    switch ($topology.Mode) {
        'Shared' {
            if ([string]::IsNullOrWhiteSpace($topology.SharedDatabaseName)) {
                throw '数据库拓扑 Mode=Shared 但 SharedDatabaseName 为空,无法解析共享物理库。'
            }
        }
        'PerService' {
            if ($null -eq $topology.ServiceDatabases -or [string]::IsNullOrWhiteSpace($topology.ServiceDatabases.identity)) {
                throw '数据库拓扑 Mode=PerService 但缺少 identity 服务的物理映射,无法解析物理库。'
            }
        }
        default {
            throw "数据库拓扑 Mode 非法: $($topology.Mode)"
        }
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
Assert-Preconditions

if (-not $CredentialOutput) {
    $defaultDir = Join-Path $env:LOCALAPPDATA 'IndustrialPlatform'
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    $CredentialOutput = Join-Path $defaultDir "bootstrap-admin-$stamp.json"
}

if (-not [System.IO.Path]::IsPathRooted($CredentialOutput)) {
    throw "凭据输出路径必须为绝对路径,当前值: $CredentialOutput"
}

if (Test-Path -LiteralPath $CredentialOutput) {
    throw "凭据输出文件已存在,拒绝覆盖: $CredentialOutput (凭据只交付一次,请妥善保管既有文件)。"
}

$outputDir = Split-Path -Parent $CredentialOutput
if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Write-Host "[initialize-admin] 调用 Identity 初始化 (首次创建将写入凭据文件: $CredentialOutput)"

$savedCliHome = $env:DOTNET_CLI_HOME
$env:DOTNET_CLI_HOME = $CliHome
try {
    & dotnet run --project $IdentityProject --launch-profile http -- --initialize-admin --credential-output $CredentialOutput
    $exitCode = $LASTEXITCODE
} finally {
    $env:DOTNET_CLI_HOME = $savedCliHome
}

exit $exitCode
