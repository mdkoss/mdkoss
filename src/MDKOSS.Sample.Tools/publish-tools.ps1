# Publish MDKOSS.Sample.Tools as a forwardable sim zip (framework-dependent win-x64).
# Usage (from repo root or this folder):
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/MDKOSS.Sample.Tools/publish-tools.ps1

$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $here '..\..')).Path
$csproj = Join-Path $here 'MDKOSS.Sample.Tools.csproj'
$publishDir = Join-Path $here 'bin\Release\publish-tools'
$artifactsDir = Join-Path $repoRoot 'artifacts'
$zipPath = Join-Path $artifactsDir 'MDKOSS.Tools-sim-win-x64.zip'
$startSrc = Join-Path $here 'packaging'
# File name 开始.txt as UTF-16 code units (avoids .ps1 encoding issues on Windows PowerShell 5)
$startFileName = [string]::new(@([char]0x5F00, [char]0x59CB)) + '.txt'
$startSrcFile = Join-Path $startSrc $startFileName

Write-Host "Repo:     $repoRoot"
Write-Host "Publish:  $publishDir"
Write-Host "Zip:      $zipPath"

if (-not (Test-Path -LiteralPath $startSrcFile)) {
    throw "Missing packaging start file: $startSrcFile"
}

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
New-Item -ItemType Directory -Path $publishDir | Out-Null
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

foreach ($required in @('plugins', 'configs', 'views')) {
    $path = Join-Path $publishDir $required
    if (-not (Test-Path $path)) {
        throw "Publish output missing required folder: $required"
    }
}

Copy-Item -LiteralPath $startSrcFile -Destination (Join-Path $publishDir $startFileName) -Force

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force

Write-Host "OK: $zipPath"
Get-Item $zipPath | Select-Object FullName, Length, LastWriteTime
