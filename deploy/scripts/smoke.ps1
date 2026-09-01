#requires -Version 5.1
<#
.SYNOPSIS
    Industrial Platform runnable-baseline smoke test (new-environment perspective).

.DESCRIPTION
    End-to-end acceptance for TASK-BASE-006. Runs the full baseline loop from a
    clean checkout point of view and prints a pass/fail report:

      1. Build the solution (expect 0 warnings / 0 errors).
      2. Run the full test suite (report total pass count).
      3. Start the default UnifiedHost via deploy/scripts/dev.ps1, or the
         explicitly requested independent services.
         (infrastructure skipped; Docker acceptance is deferred).
      4. Probe UnifiedHost health, readiness and the unified 404 envelope.
      5. Report infrastructure container status (N/A when Docker is absent).
      6. Stop services (unless -KeepRunning).

    Any failing step keeps running to gather full evidence, then the script
    exits non-zero with a summary report.

.PARAMETER KeepRunning
    Leave services running after the smoke test instead of stopping them.

.PARAMETER IndependentServices
    Explicitly validate the distributed Gateway + Identity + ReferenceData
    deployment. The default remains the single UnifiedHost on port 5041.

.EXAMPLE
    ./deploy/scripts/smoke.ps1
    ./deploy/scripts/smoke.ps1 -KeepRunning
    ./deploy/scripts/smoke.ps1 -IndependentServices
#>
[CmdletBinding()]
param(
    [switch]$KeepRunning,
    [switch]$IndependentServices
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Decode child-process output (dotnet emits UTF-8) as UTF-8; PowerShell 5.1
# otherwise uses the OEM code page and mangles CJK text in captured output.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ScriptDir = $PSScriptRoot
$RepoRoot  = (Resolve-Path (Join-Path $ScriptDir '..\..')).Path
$SlnPath   = Join-Path $RepoRoot 'src\backend\IndustrialPlatform.slnx'
$CliHome   = Join-Path $RepoRoot '.dotnet_cli_home'
$DevScript = Join-Path $ScriptDir 'dev.ps1'
$DockerDir = Join-Path $RepoRoot 'docker'

$Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$Results   = [System.Collections.Generic.List[object]]::new()
$OverallOk = $true

function Add-StepResult([string]$Name, [int]$ExitCode, [string]$Detail) {
    $script:Results.Add([pscustomobject]@{ Step = $Name; ExitCode = $ExitCode; Detail = $Detail })
    if ($ExitCode -ne 0) { $script:OverallOk = $false }
}

function Write-Step([string]$Message) {
    Write-Host "`n[step] $Message"
    Write-Host ('-' * $Message.Length)
}

function Test-Http([string]$Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
        return [int]$response.StatusCode
    } catch {
        if ($null -ne $_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return $null
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet CLI not found on PATH. Install the .NET 10 SDK and retry.'
}

# ---------------------------------------------------------------------------
# 1. Build
# ---------------------------------------------------------------------------
Write-Step 'Build solution (expect 0 warnings / 0 errors)'
$savedCli = $env:DOTNET_CLI_HOME
$env:DOTNET_CLI_HOME = $CliHome
try {
    $buildOutput = & dotnet build $SlnPath --nologo 2>&1 | Out-String
    $buildExit = $LASTEXITCODE
} finally {
    $env:DOTNET_CLI_HOME = $savedCli
}

$warningCount = 0
$errorCount = 0
# SDK summary is localized; match both Chinese and English forms.
if ($buildOutput -match '(\d+)\s+个警告') { $warningCount = [int]$Matches[1] }
elseif ($buildOutput -match '(\d+)\s+Warning') { $warningCount = [int]$Matches[1] }
if ($buildOutput -match '(\d+)\s+个错误') { $errorCount = [int]$Matches[1] }
elseif ($buildOutput -match '(\d+)\s+Error') { $errorCount = [int]$Matches[1] }

$buildDetail = "exit=$buildExit, warnings=$warningCount, errors=$errorCount"
if ($buildExit -eq 0 -and $warningCount -eq 0 -and $errorCount -eq 0) {
    Write-Host "  PASS  $buildDetail"
} else {
    Write-Host "  FAIL  $buildDetail"
    Write-Host $buildOutput
}
Add-StepResult 'Build' $buildExit $buildDetail

# ---------------------------------------------------------------------------
# 2. Test suite
# ---------------------------------------------------------------------------
Write-Step 'Run full test suite'
$env:DOTNET_CLI_HOME = $CliHome
try {
    $testOutput = & dotnet test $SlnPath --no-build --nologo 2>&1 | Out-String
    $testExit = $LASTEXITCODE
} finally {
    $env:DOTNET_CLI_HOME = $savedCli
}

$totalPassed = 0
$totalFailed = 0
$lines = $testOutput -split "`r?`n"
foreach ($line in $lines) {
    if ($line -match '通过:\s*(\d+)') { $totalPassed += [int]$Matches[1] }
    elseif ($line -match 'Passed:\s*(\d+)') { $totalPassed += [int]$Matches[1] }
    if ($line -match '失败:\s*(\d+)') { $totalFailed += [int]$Matches[1] }
    elseif ($line -match 'Failed:\s*(\d+)') { $totalFailed += [int]$Matches[1] }
}

$testDetail = "exit=$testExit, passed=$totalPassed, failed=$totalFailed"
if ($testExit -eq 0 -and $totalFailed -eq 0) {
    Write-Host "  PASS  $testDetail"
} else {
    Write-Host "  FAIL  $testDetail"
    Write-Host $testOutput
}
Add-StepResult 'Test' $testExit $testDetail

# ---------------------------------------------------------------------------
# 3. Start services (infrastructure skipped; Docker acceptance deferred)
# ---------------------------------------------------------------------------
if ($IndependentServices) {
    Write-Step 'Start independent services (infrastructure skipped)'
    & $DevScript start -SkipInfrastructure -SkipBuild -IndependentServices
} else {
    Write-Step 'Start UnifiedHost (infrastructure skipped)'
    & $DevScript start -SkipInfrastructure -SkipBuild
}
$startExit = $LASTEXITCODE
Add-StepResult 'Start' $startExit $(if ($IndependentServices) {
        'dev.ps1 start -SkipInfrastructure -SkipBuild -IndependentServices'
    } else {
        'dev.ps1 start -SkipInfrastructure -SkipBuild'
    })
if ($startExit -ne 0) {
    Write-Host 'Services failed to start; skipping endpoint probes and stopping.'
    $KeepRunning = $false
}

# ---------------------------------------------------------------------------
# 4. Endpoint probes
# ---------------------------------------------------------------------------
if ($startExit -eq 0) {
    Write-Step 'Endpoint probes'

    if ($IndependentServices) {
        $probes = @(
            [pscustomobject]@{ Name = 'Gateway /health';          Url = 'http://localhost:5080/health';          Expect = 200 }
            [pscustomobject]@{ Name = 'Identity /health';         Url = 'http://localhost:5041/health';          Expect = 200 }
            [pscustomobject]@{ Name = 'ReferenceData /health';    Url = 'http://localhost:62311/health';         Expect = 200 }
            [pscustomobject]@{ Name = 'Gateway /health/live';     Url = 'http://localhost:5080/health/live';     Expect = 200 }
            [pscustomobject]@{ Name = 'Gateway /identity/health'; Url = 'http://localhost:5080/identity/health'; Expect = 200 }
            [pscustomobject]@{ Name = 'Gateway unknown API (404)'; Url = 'http://localhost:5080/unknown';       Expect = 404 }
        )
    } else {
        $probes = @(
            [pscustomobject]@{ Name = 'UnifiedHost /health';           Url = 'http://localhost:5041/health';                       Expect = 200 }
            [pscustomobject]@{ Name = 'UnifiedHost /health/live';      Url = 'http://localhost:5041/health/live';                  Expect = 200 }
            [pscustomobject]@{ Name = 'UnifiedHost unknown API (404)'; Url = 'http://localhost:5041/api/v1/definitely-not-a-route'; Expect = 404 }
        )
    }

    foreach ($probe in $probes) {
        $status = Test-Http $probe.Url
        $ok = $status -eq $probe.Expect
        if (-not $ok) { $OverallOk = $false }
        Write-Host ("  {0}  {1}  actual={2}, expected={3}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $probe.Name, $status, $probe.Expect)
        Add-StepResult ("Probe $($probe.Name)") $(if ($ok) { 0 } else { 1 }) "actual=$status, expected=$($probe.Expect)"
    }

    # Readiness aggregates the composed modules' dependency state.
    # It can take ~10s (dependency checks time out in parallel), so probe with a longer timeout.
    $readyUrl = if ($IndependentServices) { 'http://localhost:5080/health/ready' } else { 'http://localhost:5041/health/ready' }
    $readyStatus = $null
    try {
        $readyResponse = Invoke-WebRequest -Uri $readyUrl -UseBasicParsing -TimeoutSec 20
        $readyStatus = [int]$readyResponse.StatusCode
    } catch {
        if ($null -ne $_.Exception.Response) { $readyStatus = [int]$_.Exception.Response.StatusCode }
    }
    Write-Host "  INFO  $(if ($IndependentServices) { 'Gateway' } else { 'UnifiedHost' }) /health/ready actual=$readyStatus (200 when dependencies up, 503 when down)"
    Add-StepResult 'Probe /health/ready' 0 "actual=$readyStatus (informational)"

    if ($IndependentServices) {
        $forwardBody = ''
        try {
            $forwardBody = (Invoke-WebRequest -Uri 'http://localhost:5080/identity/health' -UseBasicParsing -TimeoutSec 5).Content
        } catch { }
        $forwardOk = $forwardBody -match '"service"\s*:\s*"Identity"'
        Write-Host ("  {0}  Gateway forwarding body mentions service=Identity" -f $(if ($forwardOk) { 'PASS' } else { 'FAIL' }))
        Add-StepResult 'Probe gateway forwarding body' $(if ($forwardOk) { 0 } else { 1 }) $(if ($forwardOk) { 'body ok' } else { $forwardBody })
        if (-not $forwardOk) { $OverallOk = $false }
    } else {
        $healthBody = ''
        try {
            $healthBody = (Invoke-WebRequest -Uri 'http://localhost:5041/health' -UseBasicParsing -TimeoutSec 5).Content
        } catch { }
        $hostOk = $healthBody -match '"service"\s*:\s*"UnifiedHost"'
        Write-Host ("  {0}  UnifiedHost health body mentions service=UnifiedHost" -f $(if ($hostOk) { 'PASS' } else { 'FAIL' }))
        Add-StepResult 'Probe UnifiedHost identity body' $(if ($hostOk) { 0 } else { 1 }) $(if ($hostOk) { 'body ok' } else { $healthBody })
        if (-not $hostOk) { $OverallOk = $false }
    }
}

# ---------------------------------------------------------------------------
# 5. Infrastructure container status
# ---------------------------------------------------------------------------
Write-Step 'Infrastructure container status'
$infraDetail = ''
if (Get-Command docker -ErrorAction SilentlyContinue) {
    if (Test-Path (Join-Path $DockerDir 'docker-compose.yml')) {
        Push-Location $DockerDir
        try {
            docker compose ps
            $infraDetail = "docker compose ps exit=$LASTEXITCODE"
        } finally {
            Pop-Location
        }
    } else {
        $infraDetail = 'docker compose file missing'
    }
    Add-StepResult 'Infrastructure' 0 $infraDetail
} else {
    Write-Host '  INFO  Docker CLI not found. Infrastructure acceptance deferred (TASK-BASE-002).'
    $infraDetail = 'N/A - Docker not available; deferred to TASK-BASE-002 verification'
    Add-StepResult 'Infrastructure' 0 $infraDetail
}

# ---------------------------------------------------------------------------
# 6. Stop services
# ---------------------------------------------------------------------------
if (-not $KeepRunning) {
    Write-Step 'Stop services'
    if ($IndependentServices) { & $DevScript stop -IndependentServices }
    else { & $DevScript stop }
    $stopExit = $LASTEXITCODE
    Add-StepResult 'Stop' $stopExit $(if ($IndependentServices) { 'dev.ps1 stop -IndependentServices' } else { 'dev.ps1 stop' })
} else {
    Write-Host "`n[step] KeepRunning set; services left running."
}

# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------
$Stopwatch.Stop()
Write-Step 'Smoke test report'
Write-Host ('Overall: {0}' -f $(if ($OverallOk) { 'PASS' } else { 'FAIL' }))
Write-Host ('Total elapsed: {0:N1}s' -f $Stopwatch.Elapsed.TotalSeconds)
foreach ($row in $Results) {
    Write-Host ("  {0,-42} exit={1,-3} {2}" -f $row.Step, $row.ExitCode, $row.Detail)
}

if ($OverallOk) { exit 0 } else { exit 1 }
