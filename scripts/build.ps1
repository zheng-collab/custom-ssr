# SecureGateway Build Script
# Run from the repository root: .\scripts\build.ps1

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Publish,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$SolutionDir = Split-Path -Parent $PSScriptRoot
$ProjectDir = Join-Path $SolutionDir "src\SecureGateway"
$OutputDir = Join-Path $SolutionDir "build"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SecureGateway Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean
if ($Clean) {
    Write-Host "[1/3] Cleaning..." -ForegroundColor Yellow
    dotnet clean "$ProjectDir\SecureGateway.csproj" -c $Configuration --nologo -q
    if (Test-Path $OutputDir) {
        Remove-Item -Recurse -Force $OutputDir
    }
    Write-Host "  Cleaned." -ForegroundColor Green
}

# Restore
Write-Host "[1/3] Restoring packages..." -ForegroundColor Yellow
dotnet restore "$ProjectDir\SecureGateway.csproj" --nologo -q
Write-Host "  Packages restored." -ForegroundColor Green

# Build
Write-Host "[2/3] Building ($Configuration)..." -ForegroundColor Yellow
dotnet build "$ProjectDir\SecureGateway.csproj" -c $Configuration --no-restore --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Build FAILED!" -ForegroundColor Red
    exit 1
}
Write-Host "  Build succeeded." -ForegroundColor Green

# Publish
if ($Publish) {
    Write-Host "[3/3] Publishing self-contained executable..." -ForegroundColor Yellow
    $PublishDir = Join-Path $OutputDir "publish"
    dotnet publish "$ProjectDir\SecureGateway.csproj" `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $PublishDir `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  Publish FAILED!" -ForegroundColor Red
        exit 1
    }

    # Create v2ray-core directory in publish output
    $v2rayDir = Join-Path $PublishDir "v2ray-core"
    if (-not (Test-Path $v2rayDir)) {
        New-Item -ItemType Directory -Path $v2rayDir | Out-Null
        Write-Host "  Created v2ray-core directory. Place v2ray.exe here." -ForegroundColor Yellow
    }

    Write-Host "  Published to: $PublishDir" -ForegroundColor Green
    Write-Host ""
    Write-Host "  IMPORTANT: Download v2ray-core and place v2ray.exe in:" -ForegroundColor Yellow
    Write-Host "  $v2rayDir" -ForegroundColor White
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
