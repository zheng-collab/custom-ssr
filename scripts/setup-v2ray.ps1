# Download and setup v2ray-core for SecureGateway
# Run from repository root: .\scripts\setup-v2ray.ps1

param(
    [string]$Version = "5.16.1"
)

$ErrorActionPreference = "Stop"
$SolutionDir = Split-Path -Parent $PSScriptRoot
$V2RayDir = Join-Path $SolutionDir "src\SecureGateway\v2ray-core"

Write-Host "Setting up v2ray-core v$Version..." -ForegroundColor Cyan

# Create directory
if (-not (Test-Path $V2RayDir)) {
    New-Item -ItemType Directory -Path $V2RayDir | Out-Null
}

$ZipFile = Join-Path $env:TEMP "v2ray-windows-64.zip"
$DownloadUrl = "https://github.com/v2fly/v2ray-core/releases/download/v$Version/v2ray-windows-64.zip"

Write-Host "Downloading from: $DownloadUrl"
Invoke-WebRequest -Uri $DownloadUrl -OutFile $ZipFile -UseBasicParsing

Write-Host "Extracting..."
Expand-Archive -Path $ZipFile -DestinationPath $V2RayDir -Force

# Clean up
Remove-Item $ZipFile -Force

Write-Host "v2ray-core installed to: $V2RayDir" -ForegroundColor Green
Write-Host ""
Write-Host "Files:" -ForegroundColor Yellow
Get-ChildItem $V2RayDir | ForEach-Object { Write-Host "  $_" }
