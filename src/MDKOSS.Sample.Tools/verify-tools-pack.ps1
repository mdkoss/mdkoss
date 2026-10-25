# Smoke-test the forwardable Tools zip: unzip, --console, GET homepage, POST sim DO.
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/MDKOSS.Sample.Tools/verify-tools-pack.ps1
#   powershell ... -File verify-tools-pack.ps1 -ZipPath D:\path\MDKOSS.Tools-sim-win-x64.zip

[CmdletBinding()]
param(
    [string]$ZipPath = ''
)

$ErrorActionPreference = 'Stop'
$exitCode = 1
$proc = $null
$extractRoot = $null

$here = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $here '..\..')).Path
if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    $ZipPath = Join-Path $repoRoot 'artifacts\MDKOSS.Tools-sim-win-x64.zip'
}

$baseUrl = 'http://127.0.0.1:5088'
$exeName = 'MDKOSS.Sample.Tools.exe'

function Wait-HttpOk([string]$Url, [int]$TimeoutSec = 90) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
    $lastErr = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 300) {
                return $resp
            }
            $lastErr = "HTTP $($resp.StatusCode)"
        }
        catch {
            $lastErr = $_.Exception.Message
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for $Url ($lastErr)"
}

try {
    if (-not (Test-Path -LiteralPath $ZipPath)) {
        throw "Zip not found: $ZipPath (run publish-tools.ps1 first)"
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $extractRoot = Join-Path $env:TEMP ("mdkoss-tools-verify-$stamp")
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
    Write-Host "Zip:  $ZipPath"
    Write-Host "Temp: $extractRoot"

    Expand-Archive -LiteralPath $ZipPath -DestinationPath $extractRoot -Force

    $exePath = Join-Path $extractRoot $exeName
    if (-not (Test-Path -LiteralPath $exePath)) {
        $nested = Get-ChildItem -LiteralPath $extractRoot -Recurse -Filter $exeName -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -eq $nested) {
            throw "Unpacked zip missing $exeName under $extractRoot"
        }
        $exePath = $nested.FullName
    }

    $workDir = Split-Path -Parent $exePath
    Write-Host "Start: $exePath --console"
    $proc = Start-Process -FilePath $exePath `
        -ArgumentList '--console' `
        -WorkingDirectory $workDir `
        -PassThru `
        -WindowStyle Hidden

    $homeResp = Wait-HttpOk "$baseUrl/index_tools.html"
    Write-Host "GET index_tools.html -> $($homeResp.StatusCode)"

    $body = @{
        deviceId = 'gpio1'
        alias    = 'tower.green'
        value    = $true
    } | ConvertTo-Json -Compress

    $writeResp = Invoke-WebRequest `
        -Uri "$baseUrl/api/io/write" `
        -Method POST `
        -ContentType 'application/json; charset=utf-8' `
        -Body $body `
        -UseBasicParsing `
        -TimeoutSec 15

    Write-Host "POST /api/io/write gpio1/tower.green -> $($writeResp.StatusCode) $($writeResp.Content)"
    if ($writeResp.StatusCode -lt 200 -or $writeResp.StatusCode -ge 300) {
        throw "io/write HTTP $($writeResp.StatusCode)"
    }
    if ($writeResp.Content -notmatch '"success"\s*:\s*true') {
        throw "io/write did not report success: $($writeResp.Content)"
    }

    Write-Host 'OK: verify-tools-pack passed'
    $exitCode = 0
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
finally {
    if ($null -ne $proc -and -not $proc.HasExited) {
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $proc.Id -TimeoutSec 10 -ErrorAction SilentlyContinue
        }
        catch { }
    }
    Get-Process -Name 'MDKOSS.Sample.Tools' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    if ($null -ne $extractRoot -and (Test-Path -LiteralPath $extractRoot)) {
        Start-Sleep -Milliseconds 400
        try {
            Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Could not remove temp dir: $extractRoot ($($_.Exception.Message))"
        }
    }
}

exit $exitCode
