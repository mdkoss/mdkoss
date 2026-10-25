# Publish MDKOSS.Sample.Tools as a forwardable sim zip (framework-dependent win-x64).
# Usage (from repo root or this folder):
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/MDKOSS.Sample.Tools/publish-tools.ps1

$ErrorActionPreference = 'Stop'

function Get-FolderSizeBytes([string]$Path) {
    $sum = [long]0
    Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
        ForEach-Object { $sum += $_.Length }
    return $sum
}

function Format-SizeMb([long]$Bytes) {
    return ('{0:N2}' -f ($Bytes / 1MB))
}

$here = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $here '..\..')).Path
$csproj = Join-Path $here 'MDKOSS.Sample.Tools.csproj'
$publishDir = Join-Path $here 'bin\Release\publish-tools'
$artifactsDir = Join-Path $repoRoot 'artifacts'
$zipPath = Join-Path $artifactsDir 'MDKOSS.Tools-sim-win-x64.zip'
$startSrc = Join-Path $here 'packaging'
# File names as UTF-16 code units (avoids .ps1 encoding issues on Windows PowerShell 5)
$startFileName = [string]::new(@([char]0x5F00, [char]0x59CB)) + '.txt'
$handoffFileName = [string]::new(@(
        [char]0x6362, [char]0x673A, [char]0x68C0, [char]0x67E5
    )) + '.txt'
$startSrcFile = Join-Path $startSrc $startFileName
$handoffSrcFile = Join-Path $startSrc $handoffFileName

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

# Fresh worktrees need plugin project assets before the host's BuildPlugins target runs.
$sln = Join-Path $repoRoot 'MDKOSS.sln'
if (Test-Path -LiteralPath $sln) {
    Write-Host "Restoring solution (plugins)..."
    dotnet restore $sln -v q
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE"
    }
}

dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $startSrcFile -Destination (Join-Path $publishDir $startFileName) -Force
if (Test-Path -LiteralPath $handoffSrcFile) {
    Copy-Item -LiteralPath $handoffSrcFile -Destination (Join-Path $publishDir $handoffFileName) -Force
}

$requiredPaths = @(
    (Join-Path $publishDir 'MDKOSS.Sample.Tools.exe'),
    (Join-Path $publishDir 'plugins'),
    (Join-Path $publishDir 'configs\sample.setting.json'),
    (Join-Path $publishDir 'views\index_tools.html'),
    (Join-Path $publishDir $startFileName)
)
foreach ($path in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Publish output missing required artifact: $path"
    }
}

$dirBytes = Get-FolderSizeBytes $publishDir
Write-Host ("Publish dir size: {0} MB  ({1})" -f (Format-SizeMb $dirBytes), $publishDir)

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force

if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "Zip was not created: $zipPath"
}

$zipItem = Get-Item -LiteralPath $zipPath
Write-Host ("Zip size:         {0} MB  ({1})" -f (Format-SizeMb $zipItem.Length), $zipPath)
Write-Host "OK: $zipPath"
Write-Host ""
Write-Host "Suggested verify (after unzip on a clean folder):"
Write-Host "  powershell -NoProfile -ExecutionPolicy Bypass -File src/MDKOSS.Sample.Tools/verify-tools-pack.ps1"
Write-Host "  # or open http://127.0.0.1:5088/index_tools.html and write gpio1 / tower.green"
