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

    # Ensure v2ray-core files are in the publish output
    $v2raySrc = Join-Path $ProjectDir "v2ray-core"
    $v2rayDst = Join-Path $PublishDir "v2ray-core"

    if (Test-Path $v2raySrc) {
        if (-not (Test-Path $v2rayDst)) {
            New-Item -ItemType Directory -Path $v2rayDst | Out-Null
        }
        Copy-Item -Path "$v2raySrc\*" -Destination $v2rayDst -Recurse -Force
        Write-Host "  Copied v2ray-core files to publish output." -ForegroundColor Green
    } else {
        Write-Host "  WARNING: v2ray-core not found at $v2raySrc" -ForegroundColor Red
        Write-Host "  Run scripts\setup-v2ray.ps1 first, then rebuild." -ForegroundColor Yellow
    }

    Write-Host "  Published to: $PublishDir" -ForegroundColor Green
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
