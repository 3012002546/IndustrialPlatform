#requires -Version 5.1
<#
.SYNOPSIS
    deploy/scripts/dev.ps1 服务清单轻量测试(无 Pester 依赖,不构建不启动进程)。
    验证:
      1. 默认(无开关)status 目标为 UnifiedHost(不含独立服务名);
      2. -IndependentServices status 目标为 Gateway + Identity + ReferenceData;
      3. 旧 -UnifiedHost 参数已移除(参数绑定被拒绝,不产生含糊行为)。
    退出码 0 = 全部通过。
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tests\scripts\dev.ps1.Tests.ps1
#>
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ScriptUnderTest = Join-Path $RepoRoot 'deploy\scripts\dev.ps1'
$PowershellExe = (Get-Command powershell -ErrorAction SilentlyContinue).Source
if (-not $PowershellExe) {
    $PowershellExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
}

$script:failures = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if ($Condition) { Write-Host "PASS: $Message" }
    else { Write-Host "FAIL: $Message"; $script:failures++ }
}

function Invoke-DevPs1 {
    param([string[]]$Arguments)
    # 子进程 stderr 会作为错误记录进入管道;Stop 会中断测试,这里临时降级为 Continue
    $savedEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = (& $PowershellExe -NoProfile -ExecutionPolicy Bypass -File $ScriptUnderTest @Arguments 2>&1 | Out-String)
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedEap
    }
    return [pscustomobject]@{ Output = $output; ExitCode = $code }
}

# ---------------------------------------------------------------------------
# 1. 默认模式:status 目标为 UnifiedHost(不依赖 .run 状态,只看服务清单输出)
# ---------------------------------------------------------------------------
Write-Host '== 默认模式(UnifiedHost)=='
$resultDefault = Invoke-DevPs1 @('status')
$outDefault = $resultDefault.Output
Assert-True ($outDefault -match 'UnifiedHost') '默认 status 目标为 UnifiedHost'
Assert-True ($outDefault -notmatch 'Gateway') '默认 status 不含 Gateway'
Assert-True ($outDefault -notmatch 'ReferenceData') '默认 status 不含 ReferenceData'

# ---------------------------------------------------------------------------
# 2. 独立模式:-IndependentServices
# ---------------------------------------------------------------------------
Write-Host '== 独立模式(-IndependentServices)=='
$resultIndependent = Invoke-DevPs1 @('status', '-IndependentServices')
$outIndependent = $resultIndependent.Output
Assert-True ($outIndependent -match 'Gateway') '独立模式 status 含 Gateway'
Assert-True ($outIndependent -match 'Identity') '独立模式 status 含 Identity'
Assert-True ($outIndependent -match 'ReferenceData') '独立模式 status 含 ReferenceData'
Assert-True ($outIndependent -notmatch 'UnifiedHost') '独立模式 status 不含 UnifiedHost'

# ---------------------------------------------------------------------------
# 3. 旧 -UnifiedHost 已移除:参数绑定被拒绝,脚本未执行(不产生含糊行为)
# ---------------------------------------------------------------------------
Write-Host '== 旧参数兼容 =='
$resultLegacy = Invoke-DevPs1 @('status', '-UnifiedHost')
$outLegacy = $resultLegacy.Output
Assert-True ($resultLegacy.ExitCode -ne 0) '旧 -UnifiedHost 调用失败(参数被拒绝)'
Assert-True ($outLegacy -notmatch 'STOPPED|RUNNING') '旧 -UnifiedHost 未执行服务清单逻辑(脚本被参数绑定拒绝)'

# ---------------------------------------------------------------------------
# 结果
# ---------------------------------------------------------------------------
if ($script:failures -gt 0) {
    Write-Host "FAILED: $($script:failures) 个断言失败"
    exit 1
}
Write-Host 'ALL PASSED'
exit 0
