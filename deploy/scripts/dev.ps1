#requires -Version 5.1
<#
.SYNOPSIS
    Industrial Platform local development one-click workflow.

.DESCRIPTION
    Starts (in dependency order), stops, or reports the status of the local
    development stack. Default mode starts the single UnifiedHost process
    (Identity + SystemData + ReferenceData composed); use -IndependentServices
    to start the independent backend services (Gateway + Identity + ReferenceData)
    for boundary validation and future split. The frontend (Vite) is NOT started
    automatically — run it separately.

    Process identity is tracked via PID files under <repo-root>/.run/ and each
    service logs to .run/<service>.*.log. Stopping the stack never deletes
    Docker volumes; only an explicit `docker compose down -v` does.

.PARAMETER Command
    start | stop | status

.PARAMETER SkipInfrastructure
    Skip the Docker Compose infrastructure step (use on machines without Docker).

.PARAMETER StopInfrastructure
    With `stop`, also run `docker compose stop` (volumes are preserved).

.PARAMETER SkipBuild
    Skip the solution build before starting services.

.PARAMETER IndependentServices
    Start the independent backend services (Gateway + Identity + ReferenceData)
    instead of the default single UnifiedHost process. Default (no switch) is
    UnifiedHost only — the daily debugging entry (UnifiedHost + Vite).
    `status` and `stop` target the same mode as `start`.

.EXAMPLE
    ./deploy/scripts/dev.ps1 start
    ./deploy/scripts/dev.ps1 start -SkipInfrastructure
    ./deploy/scripts/dev.ps1 start -IndependentServices
    ./deploy/scripts/dev.ps1 status
    ./deploy/scripts/dev.ps1 stop
    ./deploy/scripts/dev.ps1 stop -StopInfrastructure
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('start', 'stop', 'status')]
    [string]$Command,

    [switch]$SkipInfrastructure,
    [switch]$StopInfrastructure,
    [switch]$SkipBuild,
    [switch]$IndependentServices
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Locations
# ---------------------------------------------------------------------------
$ScriptDir  = $PSScriptRoot
$RepoRoot   = (Resolve-Path (Join-Path $ScriptDir '..\..')).Path
$DockerDir  = Join-Path $RepoRoot 'docker'
$SlnPath    = Join-Path $RepoRoot 'src\backend\IndustrialPlatform.slnx'
$RunDir     = Join-Path $RepoRoot '.run'
$CliHome    = Join-Path $RepoRoot '.dotnet_cli_home'
$TargetFw   = 'Debug'
$TargetTfm  = 'net10.0'

# ---------------------------------------------------------------------------
# Service registry
# Default (no switch): only the single UnifiedHost process
# (composed Identity + SystemData + ReferenceData) - the daily entry (UnifiedHost + Vite).
# status/stop use the same registry as start, so their targets match the start mode.
# -IndependentServices: start Gateway + Identity + ReferenceData for boundary
# validation and future split.
# ---------------------------------------------------------------------------
if ($IndependentServices) {
    $Services = @(
        [pscustomobject]@{ Name = 'Gateway';       Port = 5080;  Project = 'src\backend\src\Gateway\IndustrialPlatform.Gateway\IndustrialPlatform.Gateway.csproj';  HealthPath = '/health' }
        [pscustomobject]@{ Name = 'Identity';      Port = 5041;  Project = 'src\backend\src\Services\Identity\IndustrialPlatform.Identity.Api\IndustrialPlatform.Identity.Api.csproj';      HealthPath = '/health' }
        [pscustomobject]@{ Name = 'ReferenceData'; Port = 62311; Project = 'src\backend\src\Services\ReferenceData\IndustrialPlatform.ReferenceData.Api\IndustrialPlatform.ReferenceData.Api.csproj'; HealthPath = '/health' }
    )
} else {
    $Services = @(
        [pscustomobject]@{ Name = 'UnifiedHost'; Port = 5041; Project = 'src\backend\src\Hosts\IndustrialPlatform.UnifiedHost\IndustrialPlatform.UnifiedHost.csproj'; HealthPath = '/health' }
    )
}

function Get-PidFile([pscustomobject]$Service) {
    return Join-Path $RunDir ($Service.Name.ToLowerInvariant() + '.pid')
}

function Get-ServicePid([pscustomobject]$Service) {
    $file = Get-PidFile $Service
    if (-not (Test-Path $file)) { return $null }
    $content = (Get-Content $file -Raw).Trim()
    if ($content -notmatch '^\d+$') { return $null }
    return [int]$content
}

function Test-ProcessAlive([int]$ProcessId) {
    return $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Get-OccupierDescription([int]$Port) {
    $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $conn) {
        $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
        if ($null -ne $proc) { return "PID $($conn.OwningProcess) ($($proc.ProcessName))" }
        return "PID $($conn.OwningProcess)"
    }
    return 'unknown process'
}

function Invoke-HealthRequest([string]$Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
        return [int]$response.StatusCode
    } catch {
        $responseProperty = $_.Exception.PSObject.Properties['Response']
        if ($null -ne $responseProperty -and $null -ne $responseProperty.Value) {
            return [int]$responseProperty.Value.StatusCode
        }
        return $null
    }
}

# ---------------------------------------------------------------------------
# Infrastructure (Docker Compose) -- failures warn but do not abort
# ---------------------------------------------------------------------------
function Start-Infrastructure {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Host '  [infra] docker CLI not found; skipping infrastructure (services will report Unhealthy until dependencies are up).'
        return
    }
    if (-not (Test-Path (Join-Path $DockerDir '.env'))) {
        Write-Host '  [infra] docker/.env not found; copying from .env.example.'
        Copy-Item (Join-Path $DockerDir '.env.example') (Join-Path $DockerDir '.env')
    }
    Push-Location $DockerDir
    try {
        Write-Host '  [infra] validating docker compose configuration...'
        docker compose config --quiet
        if ($LASTEXITCODE -ne 0) {
            Write-Warning '  [infra] docker compose config failed; skipping infrastructure.'
            return
        }
        Write-Host '  [infra] starting PostgreSQL / Redis / RabbitMQ / Seq...'
        docker compose up -d
        if ($LASTEXITCODE -ne 0) {
            Write-Warning '  [infra] docker compose up -d failed; services will report Unhealthy until dependencies are up.'
            return
        }
        docker compose ps
    } finally {
        Pop-Location
    }
}

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
function Invoke-SolutionBuild {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet CLI not found on PATH. Install the .NET 10 SDK or run from a developer shell.'
    }
    # Windows requires a local CLI home for reliable NuGet operations; set it for the build child process.
    $savedCli = $env:DOTNET_CLI_HOME
    $env:DOTNET_CLI_HOME = $CliHome
    try {
        Write-Host '  [build] restoring and building solution...'
        & dotnet build $SlnPath --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Solution build failed (exit code $LASTEXITCODE)."
        }
    } finally {
        $env:DOTNET_CLI_HOME = $savedCli
    }
}

# ---------------------------------------------------------------------------
# Services
# ---------------------------------------------------------------------------
function Start-ServiceProcess([pscustomobject]$Service) {
    $assembly = [System.IO.Path]::GetFileNameWithoutExtension($Service.Project)
    $projectDir = Join-Path $RepoRoot (Split-Path -Parent $Service.Project)
    $dll = Join-Path $projectDir "bin\$TargetFw\$TargetTfm\$assembly.dll"

    if (-not (Test-Path $dll)) {
        throw "Build output not found: $dll (run without -SkipBuild first)."
    }

    $runPidPath = Get-PidFile $Service
    $stdoutLog = Join-Path $RunDir ($Service.Name.ToLowerInvariant() + '.stdout.log')
    $stderrLog = Join-Path $RunDir ($Service.Name.ToLowerInvariant() + '.stderr.log')

    # Preserve and set environment for the child process.
    $savedUrls = $env:ASPNETCORE_URLS
    $savedEnv  = $env:ASPNETCORE_ENVIRONMENT
    $savedCli  = $env:DOTNET_CLI_HOME
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = "http://localhost:$($Service.Port)"
    $env:DOTNET_CLI_HOME = $CliHome

    try {
        # Quote the DLL path: the repository path may contain spaces, and
        # Start-Process splits unquoted arguments on whitespace.
        $dllArgument = '"' + $dll + '"'
        $process = Start-Process -FilePath 'dotnet' `
            -ArgumentList @($dllArgument) `
            -WorkingDirectory $projectDir `
            -WindowStyle Hidden `
            -RedirectStandardOutput $stdoutLog `
            -RedirectStandardError $stderrLog `
            -PassThru
    } finally {
        $env:ASPNETCORE_URLS = $savedUrls
        $env:ASPNETCORE_ENVIRONMENT = $savedEnv
        $env:DOTNET_CLI_HOME = $savedCli
    }

    Start-Sleep -Milliseconds 1500
    if ($process.HasExited) {
        throw "$($Service.Name) exited immediately (code $($process.ExitCode)); see $stderrLog."
    }

    Set-Content -Path $runPidPath -Value $process.Id
    Write-Host "  [svc] $($Service.Name) started (PID $($process.Id)) -> http://localhost:$($Service.Port) (log: $stdoutLog)"
}

function Wait-ServiceHealth([pscustomobject]$Service) {
    $url = "http://localhost:$($Service.Port)$($Service.HealthPath)"
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $status = Invoke-HealthRequest $url
        if ($status -eq 200) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

# ---------------------------------------------------------------------------
# start
# ---------------------------------------------------------------------------
function Start-Stack {
    Write-Host "[start] working directory: $RepoRoot"
    if (-not (Test-Path $RunDir)) { New-Item -ItemType Directory -Path $RunDir | Out-Null }

    if (-not $SkipInfrastructure) { Start-Infrastructure } else { Write-Host '  [infra] skipped (-SkipInfrastructure).' }

    if (-not $SkipBuild) { Invoke-SolutionBuild } else { Write-Host '  [build] skipped (-SkipBuild).' }

    # Port conflict pre-check: abort before starting anything on a hard conflict.
    $conflicts = @()
    foreach ($service in $Services) {
        $occupied = $null -ne (Get-NetTCPConnection -LocalPort $service.Port -State Listen -ErrorAction SilentlyContinue)
        if (-not $occupied) { continue }
        $servicePid = Get-ServicePid $service
        if ($null -ne $servicePid -and (Test-ProcessAlive $servicePid)) {
            Write-Host "  [svc] $($service.Name) is already running (PID $servicePid) on port $($service.Port); skipping."
        } else {
            $conflicts += "$($service.Name) port $($service.Port) is occupied by $(Get-OccupierDescription $service.Port)"
        }
    }
    if ($conflicts.Count -gt 0) {
        Write-Error "Port conflict(s): $($conflicts -join '; '). Resolve them, then retry. Nothing was started."
        exit 1
    }

    # Start each service that is not already running.
    foreach ($service in $Services) {
        $servicePid = Get-ServicePid $service
        if ($null -ne $servicePid -and (Test-ProcessAlive $servicePid)) { continue }
        Start-ServiceProcess $service
    }

    # Wait for readiness and report.
    $started = @()
    foreach ($service in $Services) {
        $servicePid = Get-ServicePid $service
        if ($null -eq $servicePid -or -not (Test-ProcessAlive $servicePid)) { continue }
        $ready = Wait-ServiceHealth $service
        if ($ready) {
            Write-Host "  [svc] $($Service.Name) health OK on http://localhost:$($Service.Port)"
            $started += $true
        } else {
            Write-Warning "$($Service.Name) did not answer /health within 30s; check .run/$($Service.Name.ToLowerInvariant()).stderr.log"
        }
    }

    Write-Host ''
    Write-Host 'Dev endpoints:'
    if (-not $IndependentServices) {
        Write-Host '  UnifiedHost   http://localhost:5041  (Identity + SystemData + ReferenceData)'
        Write-Host '  Health        http://localhost:5041/health/ready'
        Write-Host '  Vite          前端: cd src/frontend && pnpm dev (VITE_API_BASE_URL=http://localhost:5041)'
    } else {
        Write-Host '  Gateway       http://localhost:5080'
        Write-Host '  Identity      http://localhost:5041'
        Write-Host '  ReferenceData http://localhost:62311'
        Write-Host '  Seq           http://localhost:5341'
        Write-Host '  RabbitMQ      http://localhost:15672'
        Write-Host '  Health        http://localhost:5080/health/ready'
    }
    Write-Host ''
    Write-Host 'Run ./deploy/scripts/dev.ps1 status to verify, ./deploy/scripts/dev.ps1 stop to stop.'
}

# ---------------------------------------------------------------------------
# stop
# ---------------------------------------------------------------------------
function Stop-Stack {
    Write-Host '[stop] stopping backend services...'
    foreach ($service in $Services) {
        $servicePid = Get-ServicePid $service
        $file = Get-PidFile $service
        if ($null -eq $servicePid) { continue }
        if (Test-ProcessAlive $servicePid) {
            Stop-Process -Id $servicePid -Force -ErrorAction SilentlyContinue
            Write-Host "  [svc] $($service.Name) stopped (PID $servicePid)."
        } else {
            Write-Host "  [svc] $($service.Name) PID $servicePid was not running; cleaning up."
        }
        Remove-Item $file -Force -ErrorAction SilentlyContinue
    }

    if ($StopInfrastructure -and (Get-Command docker -ErrorAction SilentlyContinue)) {
        Push-Location $DockerDir
        try {
            Write-Host '  [infra] stopping Docker Compose containers (volumes preserved)...'
            docker compose stop
        } finally {
            Pop-Location
        }
    } elseif ($StopInfrastructure) {
        Write-Warning '  [infra] docker CLI not found; skipping docker compose stop.'
    }

    Write-Host 'Docker data volumes were NOT removed. To remove containers/network (keeping data): docker compose down;'
    Write-Host 'to also wipe data: docker compose down -v (irreversible).'
}

# ---------------------------------------------------------------------------
# status
# ---------------------------------------------------------------------------
function Show-Status {
    Write-Host '[status] backend services:'
    $allOk = $true
    foreach ($service in $Services) {
        $servicePid = Get-ServicePid $service
        $runPidPath = Get-PidFile $service
        if ($null -ne $servicePid -and (Test-ProcessAlive $servicePid)) {
            $status = Invoke-HealthRequest "http://localhost:$($Service.Port)$($Service.HealthPath)"
            if ($status -eq 200) {
                Write-Host "  $($service.Name.PadRight(14)) RUNNING  PID $servicePid  health 200 OK"
            } else {
                Write-Host "  $($service.Name.PadRight(14)) RUNNING  PID $servicePid  health HTTP $status"
                $allOk = $false
            }
        } else {
            Write-Host "  $($service.Name.PadRight(14)) STOPPED  ($runPidPath)"
            $allOk = $false
        }
    }

    if (Get-Command docker -ErrorAction SilentlyContinue) {
        Push-Location $DockerDir
        try {
            Write-Host '[status] infrastructure:'
            docker compose ps
            $infraOk = $LASTEXITCODE -eq 0
        } finally {
            Pop-Location
        }
    } else {
        Write-Host '[status] infrastructure: docker CLI not found (skipped).'
    }

    if (-not $allOk) {
        Write-Host ''
        Write-Host 'Some services are not healthy. Start them with ./deploy/scripts/dev.ps1 start.'
        exit 1
    }
}

# ---------------------------------------------------------------------------
# Dispatch
# ---------------------------------------------------------------------------
switch ($Command) {
    'start'  { Start-Stack }
    'stop'   { Stop-Stack }
    'status' { Show-Status }
}
