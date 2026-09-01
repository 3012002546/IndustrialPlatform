#requires -Version 5.1
<#
.SYNOPSIS
    Lightweight default-UnifiedHost test for deploy/scripts/smoke.ps1.
    Runs the real script with fake dotnet, dev.ps1 and HTTP responses.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tests\scripts\smoke.ps1.Tests.ps1
#>
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ScriptUnderTest = Join-Path $RepoRoot 'deploy\scripts\smoke.ps1'
$SmokeSource = Get-Content -LiteralPath $ScriptUnderTest -Raw
$PowerShellExe = (Get-Command powershell -ErrorAction SilentlyContinue).Source
if (-not $PowerShellExe) {
    $PowerShellExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
}

$script:failures = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if ($Condition) { Write-Host "PASS: $Message" }
    else { Write-Host "FAIL: $Message"; $script:failures++ }
}

Assert-True ($SmokeSource -match '\[switch\]\$IndependentServices') 'smoke exposes an explicit IndependentServices switch'
Assert-True ($SmokeSource -match 'if \(\$IndependentServices\)') 'smoke keeps a separate IndependentServices branch'
Assert-True ($SmokeSource -match 'http://localhost:5080/health') 'IndependentServices probes Gateway only when explicitly selected'
Assert-True ($SmokeSource -match 'http://localhost:62311/health') 'IndependentServices probes ReferenceData only when explicitly selected'

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("industrial-platform-smoke-tests-" + [guid]::NewGuid().ToString('N'))
$fakeRepo = Join-Path $tempRoot 'repo'
$fakeScripts = Join-Path $fakeRepo 'deploy\scripts'
$fakeBin = Join-Path $tempRoot 'bin'
$httpLog = Join-Path $tempRoot 'http.log'
$devLog = Join-Path $tempRoot 'dev.log'
$runner = Join-Path $tempRoot 'runner.ps1'

try {
    New-Item -ItemType Directory -Path $fakeScripts, $fakeBin, (Join-Path $fakeRepo 'src\backend'), (Join-Path $fakeRepo 'docker') | Out-Null
    Copy-Item -LiteralPath $ScriptUnderTest -Destination (Join-Path $fakeScripts 'smoke.ps1')

    Set-Content -LiteralPath (Join-Path $fakeScripts 'dev.ps1') -Encoding Utf8 -Value @'
param(
    [string]$Command,
    [switch]$SkipInfrastructure,
    [switch]$SkipBuild,
    [switch]$IndependentServices
)
$mode = if ($IndependentServices) { 'IndependentServices' } else { 'UnifiedHost' }
Add-Content -LiteralPath $env:SMOKE_DEV_LOG -Value "$Command $mode"
'@

    Set-Content -LiteralPath (Join-Path $fakeBin 'dotnet.cmd') -Encoding Ascii -Value @'
@echo off
if "%1"=="build" (
  echo Build succeeded.
  echo 0 Warning(s)
  echo 0 Error(s)
  exit /b 0
)
if "%1"=="test" (
  echo Passed: 1. Failed: 0.
  exit /b 0
)
exit /b 1
'@

    Set-Content -LiteralPath $runner -Encoding Utf8 -Value @'
function Invoke-WebRequest {
    param([string]$Uri, [switch]$UseBasicParsing, [int]$TimeoutSec)
    Add-Content -LiteralPath $env:SMOKE_HTTP_LOG -Value $Uri
    $path = ([uri]$Uri).AbsolutePath
    $statusCode = if ($path -match 'unknown|definitely-not-a-route') { 404 } else { 200 }
    $content = if ($path -eq '/identity/health') { '{"service":"Identity"}' } else { '{"service":"UnifiedHost"}' }
    return [pscustomobject]@{ StatusCode = $statusCode; Content = $content }
}

if ($env:SMOKE_INDEPENDENT -eq 'true') { & $env:SMOKE_SCRIPT -IndependentServices }
else { & $env:SMOKE_SCRIPT }
exit $LASTEXITCODE
'@

    $savedPath = $env:PATH
        $env:PATH = "$fakeBin;$env:WINDIR\System32;$env:WINDIR"
        $env:SMOKE_SCRIPT = Join-Path $fakeScripts 'smoke.ps1'
        $env:SMOKE_HTTP_LOG = $httpLog
        $env:SMOKE_DEV_LOG = $devLog
        Remove-Item Env:SMOKE_INDEPENDENT -ErrorAction SilentlyContinue
    try {
        & $PowerShellExe -NoProfile -ExecutionPolicy Bypass -File $runner | Out-Host
        $exitCode = $LASTEXITCODE
    } finally {
        $env:PATH = $savedPath
        Remove-Item Env:SMOKE_SCRIPT, Env:SMOKE_HTTP_LOG, Env:SMOKE_DEV_LOG -ErrorAction SilentlyContinue
    }

    $urls = @(Get-Content -LiteralPath $httpLog -ErrorAction SilentlyContinue)
    $devInvocations = @(Get-Content -LiteralPath $devLog -ErrorAction SilentlyContinue)

    Assert-True ($exitCode -eq 0) 'default smoke succeeds against controlled UnifiedHost responses'
    Assert-True ($urls.Count -gt 0) 'default smoke performs HTTP probes'
    Assert-True (@($urls | Where-Object { ([uri]$_).Port -ne 5041 }).Count -eq 0) 'default smoke probes only UnifiedHost port 5041'
    Assert-True (@($urls | Where-Object { ([uri]$_).AbsolutePath -eq '/health' }).Count -gt 0) 'default smoke probes UnifiedHost /health'
    Assert-True (@($urls | Where-Object { ([uri]$_).AbsolutePath -eq '/health/ready' }).Count -gt 0) 'default smoke probes UnifiedHost /health/ready'
    Assert-True ($devInvocations -contains 'start UnifiedHost') 'default smoke starts dev.ps1 in UnifiedHost mode'
    Assert-True ($devInvocations -contains 'stop UnifiedHost') 'default smoke stops dev.ps1 in UnifiedHost mode'

    Clear-Content -LiteralPath $httpLog
    Clear-Content -LiteralPath $devLog
    $env:PATH = "$fakeBin;$env:WINDIR\System32;$env:WINDIR"
    $env:SMOKE_SCRIPT = Join-Path $fakeScripts 'smoke.ps1'
    $env:SMOKE_HTTP_LOG = $httpLog
    $env:SMOKE_DEV_LOG = $devLog
    $env:SMOKE_INDEPENDENT = 'true'
    try {
        & $PowerShellExe -NoProfile -ExecutionPolicy Bypass -File $runner | Out-Host
        $independentExitCode = $LASTEXITCODE
    } finally {
        $env:PATH = $savedPath
        Remove-Item Env:SMOKE_INDEPENDENT -ErrorAction SilentlyContinue
    }

    $independentUrls = @(Get-Content -LiteralPath $httpLog -ErrorAction SilentlyContinue)
    $independentInvocations = @(Get-Content -LiteralPath $devLog -ErrorAction SilentlyContinue)
    $independentPorts = @($independentUrls | ForEach-Object { ([uri]$_).Port } | Sort-Object -Unique)
    Assert-True ($independentExitCode -eq 0) 'explicit IndependentServices smoke succeeds against controlled responses'
    Assert-True (@(Compare-Object $independentPorts @(5041, 5080, 62311)).Count -eq 0) 'IndependentServices probes only distributed service ports'
    Assert-True ($independentInvocations -contains 'start IndependentServices') 'IndependentServices passes the explicit mode to dev.ps1'
    Assert-True ($independentInvocations -contains 'stop IndependentServices') 'IndependentServices stops the same explicit mode'
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:failures -gt 0) {
    Write-Host "FAILED: $($script:failures) assertion(s) failed"
    exit 1
}

Write-Host 'ALL PASSED'
exit 0
