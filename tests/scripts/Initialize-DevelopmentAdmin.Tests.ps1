#requires -Version 5.1
<#
.SYNOPSIS
    scripts/Initialize-DevelopmentAdmin.ps1 的独立测试(无 Pester 依赖)。
    通过子 powershell 调用被测脚本,使用假 dotnet(记录调用参数、可配置退出码)
    与临时配置,验证前置条件、参数校验、退出码透传与不泄密。退出码 0 = 全部通过。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tests\scripts\Initialize-DevelopmentAdmin.Tests.ps1
#>
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ScriptUnderTest = Join-Path $RepoRoot 'scripts\Initialize-DevelopmentAdmin.ps1'
$IdentityProject = Join-Path $RepoRoot 'src\backend\src\Services\Identity\IndustrialPlatform.Identity.Api\IndustrialPlatform.Identity.Api.csproj'
$PowershellExe = (Get-Command powershell -ErrorAction SilentlyContinue).Source
if (-not $PowershellExe) {
    $PowershellExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
}

$script:failures = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if ($Condition) { Write-Host "PASS: $Message" }
    else { Write-Host "FAIL: $Message"; $script:failures++ }
}

$script:tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("industrial-platform-script-tests-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:tempRoot | Out-Null

# ---------------------------------------------------------------------------
# 子进程调用被测脚本:脚本内 throw/exit 不会中断测试进程
# ---------------------------------------------------------------------------
function Invoke-ScriptUnderTest {
    param([string[]]$Arguments)
    # 子进程 stderr 会作为错误记录进入管道;Stop 会中断测试,这里临时降级为 Continue
    $savedEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = (& $PowershellExe -NoProfile -ExecutionPolicy Bypass -File $ScriptUnderTest @Arguments 2>&1 | Out-String)
    } finally {
        $ErrorActionPreference = $savedEap
    }
    return $output
}

# ---------------------------------------------------------------------------
# 假 dotnet:记录参数到日志文件,退出码由环境变量控制
# ---------------------------------------------------------------------------
function New-FakeDotnet {
    $bin = Join-Path $script:tempRoot 'fakedotnet'
    New-Item -ItemType Directory -Path $bin | Out-Null
    $log = Join-Path $script:tempRoot 'dotnet-args.log'
    Set-Content -LiteralPath (Join-Path $bin 'dotnet.cmd') -Value @'
@echo off
echo %*>> "%FAKE_DOTNET_LOG%"
if "%FAKE_DOTNET_EXIT%"=="" (exit /b 0) else (exit /b %FAKE_DOTNET_EXIT%)
'@ -Encoding Ascii
    return [pscustomobject]@{ Bin = $bin; Log = $log }
}

function Reset-FakeDotnetLog([string]$Log) {
    if (Test-Path -LiteralPath $Log) { Remove-Item -LiteralPath $Log -Force }
}

function Get-FakeDotnetInvocations([string]$Log) {
    if (-not (Test-Path -LiteralPath $Log)) { return ,@() }
    $lines = @(Get-Content -LiteralPath $Log)
    return ,$lines
}

function New-FakeConfig {
    param(
        [bool]$RemoteEnabled = $true,
        [string]$TopologyJson = '{"Mode":"Shared","SharedDatabaseName":"industrial_platform_dev","ServiceDatabases":{}}'
    )
    $path = Join-Path $script:tempRoot ("appsettings.local-" + [guid]::NewGuid().ToString('N') + '.json')
    $json = @{
        RemoteDevelopment = @{ Enabled = $RemoteEnabled }
        DatabaseTopology  = ($TopologyJson | ConvertFrom-Json)
        # 泄密检测探针:任何输出中绝不允许出现该值
        SecretProbe       = 'SUPER-SECRET-VALUE-XYZ'
    } | ConvertTo-Json -Depth 5
    Set-Content -LiteralPath $path -Value $json -Encoding Utf8
    return $path
}

# ---------------------------------------------------------------------------
# 1. 前置条件
# ---------------------------------------------------------------------------
Write-Host '== 前置条件 =='
$fake = New-FakeDotnet

# 1a. 无 dotnet:受限 PATH(仅 System32,排除真实与假 dotnet)
$savedPath = $env:PATH
$env:PATH = "$env:WINDIR\System32"
try {
    $out1 = Invoke-ScriptUnderTest @('-LocalConfigurationPath', (New-FakeConfig))
    $code1 = $LASTEXITCODE
} finally {
    $env:PATH = $savedPath
}
Assert-True ($code1 -ne 0) '无 dotnet 时脚本失败'
Assert-True ($out1 -match 'dotnet') '无 dotnet 错误信息提到 dotnet'

# 1b. 本地配置缺失
$out2 = Invoke-ScriptUnderTest @('-LocalConfigurationPath', (Join-Path $script:tempRoot 'missing-config.json'))
$code2 = $LASTEXITCODE
Assert-True ($code2 -ne 0) '配置缺失时脚本失败'
Assert-True ($out2 -match '未找到本地私有配置') '配置缺失错误信息正确'

# 1c. 非法 JSON
$badConfig = Join-Path $script:tempRoot 'bad-config.json'
Set-Content -LiteralPath $badConfig -Value '{not-json' -Encoding Ascii
$out3 = Invoke-ScriptUnderTest @('-LocalConfigurationPath', $badConfig)
Assert-True ($LASTEXITCODE -ne 0) '非法 JSON 时脚本失败'
Assert-True ($out3 -match '不是合法 JSON') '非法 JSON 错误信息正确'

# 1d. RemoteDevelopment 未启用
$out4 = Invoke-ScriptUnderTest @('-LocalConfigurationPath', (New-FakeConfig -RemoteEnabled $false))
Assert-True ($LASTEXITCODE -ne 0) 'RemoteDevelopment 关闭时脚本失败'
Assert-True ($out4 -match 'RemoteDevelopment.Enabled') 'RemoteDevelopment 错误信息正确'

# 1e. 拓扑不可解析:Shared 缺 SharedDatabaseName
$out5 = Invoke-ScriptUnderTest @('-LocalConfigurationPath', (New-FakeConfig -TopologyJson '{"Mode":"Shared","SharedDatabaseName":"","ServiceDatabases":{}}'))
Assert-True ($LASTEXITCODE -ne 0) 'Shared 缺 SharedDatabaseName 时脚本失败'

# 1f. 拓扑不可解析:PerService 缺 identity 映射
$out6 = Invoke-ScriptUnderTest @('-LocalConfigurationPath', (New-FakeConfig -TopologyJson '{"Mode":"PerService","ServiceDatabases":{}}'))
Assert-True ($LASTEXITCODE -ne 0) 'PerService 缺 identity 映射时脚本失败'

# 1g. 拓扑 Mode 非法
$out7 = Invoke-ScriptUnderTest @('-LocalConfigurationPath', (New-FakeConfig -TopologyJson '{"Mode":"Bogus","ServiceDatabases":{}}'))
Assert-True ($LASTEXITCODE -ne 0) '非法 Mode 时脚本失败'

# 1h. 全部前置条件错误输出不得泄密
$allErrorOutput = ($out1 + $out2 + $out3 + $out4 + $out5 + $out6 + $out7) -join "`n"
Assert-True ($allErrorOutput -notmatch 'SUPER-SECRET-VALUE-XYZ') '错误输出不含配置中的秘密值'

# ---------------------------------------------------------------------------
# 2. 参数与退出码(前置条件通过,假 dotnet 记录调用)
# ---------------------------------------------------------------------------
Write-Host '== 参数与退出码 =='
$goodConfig = New-FakeConfig
$env:PATH = "$($fake.Bin);$env:PATH"
$env:FAKE_DOTNET_LOG = $fake.Log
$env:FAKE_DOTNET_EXIT = '0'
try {
    # 2a. 相对路径拒绝
    Reset-FakeDotnetLog $fake.Log
    $outRel = Invoke-ScriptUnderTest @('-LocalConfigurationPath', $goodConfig, '-CredentialOutput', 'relative\bootstrap-admin.json')
    Assert-True ($LASTEXITCODE -ne 0) '相对路径被拒绝'
    Assert-True ((Get-FakeDotnetInvocations $fake.Log).Count -eq 0) '相对路径拒绝后未调用 dotnet'

    # 2b. 目标文件已存在拒绝且不调用 dotnet
    Reset-FakeDotnetLog $fake.Log
    $existingTarget = Join-Path $script:tempRoot 'existing-bootstrap-admin.json'
    Set-Content -LiteralPath $existingTarget -Value '{}' -Encoding Ascii
    $outExist = Invoke-ScriptUnderTest @('-LocalConfigurationPath', $goodConfig, '-CredentialOutput', $existingTarget)
    Assert-True ($LASTEXITCODE -ne 0) '目标文件已存在被拒绝'
    Assert-True ((Get-FakeDotnetInvocations $fake.Log).Count -eq 0) '目标文件已存在拒绝后未调用 dotnet'

    # 2c. 快乐路径:调用准确 csproj + 参数,退出码透传
    Reset-FakeDotnetLog $fake.Log
    $env:FAKE_DOTNET_EXIT = '0'
    $target = Join-Path $script:tempRoot 'out\bootstrap-admin.json'
    $outHappy = Invoke-ScriptUnderTest @('-LocalConfigurationPath', $goodConfig, '-CredentialOutput', $target)
    Assert-True ($LASTEXITCODE -eq 0) '快乐路径退出码 0'
    $invocations = Get-FakeDotnetInvocations $fake.Log
    Assert-True ($invocations.Count -eq 1) '快乐路径恰好调用一次 dotnet'
    if ($invocations.Count -eq 1) {
        $argsLine = $invocations[0]
        Assert-True ($argsLine -match '^run --project ') 'dotnet 以 run --project 启动'
        Assert-True ($argsLine -match [regex]::Escape($IdentityProject)) 'dotnet 使用准确 Identity csproj'
        Assert-True ($argsLine -match '--launch-profile http') 'dotnet 使用 http launch profile'
        Assert-True ($argsLine -match '-- --initialize-admin --credential-output ') 'dotnet 透传 --initialize-admin --credential-output'
        Assert-True ($argsLine -match [regex]::Escape($target)) 'dotnet 收到绝对输出路径'
        Assert-True ($argsLine -notmatch 'SUPER-SECRET-VALUE-XYZ') '调用参数不含配置秘密值'
    }
    Assert-True ($outHappy -notmatch 'SUPER-SECRET-VALUE-XYZ') '快乐路径输出不含配置秘密值'

    # 2d. 非零退出码透传
    Reset-FakeDotnetLog $fake.Log
    $env:FAKE_DOTNET_EXIT = '7'
    $outFail = Invoke-ScriptUnderTest @('-LocalConfigurationPath', $goodConfig, '-CredentialOutput', (Join-Path $script:tempRoot 'out2\bootstrap-admin.json'))
    Assert-True ($LASTEXITCODE -eq 7) 'dotnet 非零退出码被透传 (7)'

    # 2e. 默认 CredentialOutput:%LOCALAPPDATA%\IndustrialPlatform\bootstrap-admin-<UTC>.json
    Reset-FakeDotnetLog $fake.Log
    $env:FAKE_DOTNET_EXIT = '0'
    $outDefault = Invoke-ScriptUnderTest @('-LocalConfigurationPath', $goodConfig)
    Assert-True ($LASTEXITCODE -eq 0) '默认输出路径场景退出码 0'
    $defaultArgs = (Get-FakeDotnetInvocations $fake.Log)[0]
    $expectedPrefix = [regex]::Escape((Join-Path $env:LOCALAPPDATA 'IndustrialPlatform\bootstrap-admin-'))
    Assert-True ($defaultArgs -match ($expectedPrefix + '[0-9TZ-]+\.json')) '默认输出路径符合 %LOCALAPPDATA%\IndustrialPlatform\bootstrap-admin-<UTC>.json'
} finally {
    $env:PATH = $savedPath
    Remove-Item Env:FAKE_DOTNET_LOG -ErrorAction SilentlyContinue
    Remove-Item Env:FAKE_DOTNET_EXIT -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
# 清理与结果
# ---------------------------------------------------------------------------
Remove-Item -LiteralPath $script:tempRoot -Recurse -Force -ErrorAction SilentlyContinue

if ($script:failures -gt 0) {
    Write-Host "FAILED: $($script:failures) 个断言失败"
    exit 1
}
Write-Host 'ALL PASSED'
exit 0
