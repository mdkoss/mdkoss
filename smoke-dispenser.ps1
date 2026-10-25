#Requires -Version 5.1
<#
.SYNOPSIS
  Cold-start MDKOSS.Sample.Dispenser (--console, win-x64), run grid-2x2 to Done, assert valve off.
.NOTES
  Exit 0 on success; non-zero on failure. Kills the host process on exit.
  Outside CI: powershell -NoProfile -ExecutionPolicy Bypass -File .\smoke-dispenser.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [int]$ReadyTimeoutSec = 90,
    [int]$CycleTimeoutSec = 120,
    [int]$PollMs = 250
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$RepoRoot = $PSScriptRoot
$BaseUrl = 'http://127.0.0.1:5082'
$Project = Join-Path $RepoRoot 'src\MDKOSS.Sample.Dispenser\MDKOSS.Sample.Dispenser.csproj'
$proc = $null
$exitCode = 1

function Write-Smoke([string]$Message) {
    Write-Host "[smoke-dispenser] $Message"
}

function Test-PortInUse([int]$Port) {
    try {
        $listeners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        return $null -ne $listeners
    } catch {
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $iar = $tcp.BeginConnect('127.0.0.1', $Port, $null, $null)
            $ok = $iar.AsyncWaitHandle.WaitOne(200)
            if ($ok) { try { $tcp.EndConnect($iar) } catch { } }
            $connected = $tcp.Connected
            $tcp.Close()
            return $connected
        } catch {
            return $false
        }
    }
}

function Wait-HttpReady([string]$Url, [int]$TimeoutSec) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500) {
                return
            }
        } catch {
            # not ready yet
        }
        Start-Sleep -Milliseconds 400
    }
    throw "Timed out after ${TimeoutSec}s waiting for $Url"
}

function Stop-ProcessTree([System.Diagnostics.Process]$Process) {
    if ($null -eq $Process) { return }
    if ($Process.HasExited) { return }
    $procId = $Process.Id
    Write-Smoke "Stopping process tree PID $procId"
    & taskkill.exe /PID $procId /T /F 2>$null | Out-Null
    try { $Process.WaitForExit(5000) | Out-Null } catch { }
}

function Get-ValveOn($valveRaw) {
    if ($valveRaw -is [bool]) { return [bool]$valveRaw }
    if ($null -eq $valveRaw) { return $false }
    $parsed = $false
    if ([bool]::TryParse([string]$valveRaw, [ref]$parsed)) { return $parsed }
    $n = 0
    if ([int]::TryParse([string]$valveRaw, [ref]$n)) { return $n -ne 0 }
    return $false
}

try {
    if (-not (Test-Path -LiteralPath $Project)) {
        throw "Project not found: $Project"
    }

    if (Test-PortInUse -Port 5082) {
        throw "Port 5082 is already in use. Change monitoringPrefix or stop the occupying process, then retry."
    }

    Write-Smoke "Restoring / building Sample.Dispenser ($Configuration, win-x64)"
    $sln = Join-Path $RepoRoot 'MDKOSS.sln'
    & dotnet build $Project -c $Configuration -r win-x64
    if ($LASTEXITCODE -ne 0) {
        Write-Smoke 'Project build failed; restoring solution then retry'
        & dotnet restore $sln
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit $LASTEXITCODE" }
        & dotnet build $Project -c $Configuration -r win-x64
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit $LASTEXITCODE" }
    }

    Write-Smoke 'Starting Sample.Dispenser --console'
    $argList = @(
        'run',
        '--project', $Project,
        '-c', $Configuration,
        '-r', 'win-x64',
        '--no-build',
        '--no-launch-profile',
        '--',
        '--console'
    )
    # Do not redirect stdout/stderr: filled pipes can stall the console host.
    $proc = Start-Process -FilePath 'dotnet' -ArgumentList $argList `
        -WorkingDirectory $RepoRoot -PassThru -WindowStyle Hidden
    if ($null -eq $proc) {
        throw 'Failed to start dotnet process'
    }
    Write-Smoke "Host PID $($proc.Id)"

    Write-Smoke "Waiting for $BaseUrl (ready timeout ${ReadyTimeoutSec}s)"
    Wait-HttpReady -Url $BaseUrl -TimeoutSec $ReadyTimeoutSec

    Write-Smoke 'POST /api/dispense/start'
    try {
        $start = Invoke-WebRequest -Uri "$BaseUrl/api/dispense/start" -Method POST -UseBasicParsing -TimeoutSec 10
    } catch {
        throw "start failed: $($_.Exception.Message)"
    }
    if ($start.StatusCode -lt 200 -or $start.StatusCode -ge 300) {
        throw "start failed: HTTP $($start.StatusCode) $($start.Content)"
    }

    Write-Smoke "Polling /api/dispense/dashboard until phase=Done (cycle timeout ${CycleTimeoutSec}s)"
    $deadline = [DateTime]::UtcNow.AddSeconds($CycleTimeoutSec)
    $phase = $null
    $valveOn = $true
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($proc.HasExited) {
            throw "Host exited early with code $($proc.ExitCode) before Done"
        }

        try {
            $dash = Invoke-WebRequest -Uri "$BaseUrl/api/dispense/dashboard" -UseBasicParsing -TimeoutSec 5
            $doc = $dash.Content | ConvertFrom-Json
            $phase = [string]$doc.phase
            $valveOn = Get-ValveOn $doc.valve
            if ($phase -eq 'Done' -or $phase -eq 'Fault') {
                break
            }
        } catch {
            # transient
        }

        Start-Sleep -Milliseconds $PollMs
    }

    if ($phase -ne 'Done') {
        throw "Expected phase=Done, got '$phase' (timeout ${CycleTimeoutSec}s)"
    }
    if ($valveOn) {
        throw 'Valve still on after Done (expected closed)'
    }

    Write-Smoke "OK phase=Done valve=off"
    $exitCode = 0
} catch {
    Write-Smoke "FAIL: $($_.Exception.Message)"
    $exitCode = 1
} finally {
    Stop-ProcessTree -Process $proc
}

exit $exitCode
