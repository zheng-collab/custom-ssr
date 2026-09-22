# SecureGateway Build Script
# Run from the repository root: .\scripts\build.ps1
#
# Code signing (needed for Windows 11 Smart App Control / clean SmartScreen):
#   .\scripts\build.ps1 -Publish -SignCert C:\certs\company.pfx
#     -> prompts for the PFX password, signs SecureGateway.exe and v2ray-core\v2ray.exe
#   .\scripts\build.ps1 -Publish -SignThumbprint 1A2B...   (cert already in the user store / HSM token)
# Requires signtool.exe from the Windows SDK (installed with Visual Studio, or
# "Windows 10/11 SDK" from the Visual Studio Installer).

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Publish,
    [switch]$Clean,
    [string]$SignCert = "",
    [string]$SignThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
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
        Write-Host "  ERROR: v2ray-core not found at $v2raySrc" -ForegroundColor Red
        Write-Host "  It is normally checked into the repository. Run 'git status' to see if it was deleted," -ForegroundColor Yellow
        Write-Host "  or restore it with scripts\setup-v2ray.ps1, then rebuild." -ForegroundColor Yellow
        exit 1
    }

    # Code signing — both executables must be signed or Smart App Control blocks the app.
    if ($SignCert -or $SignThumbprint) {
        Write-Host "[4/4] Signing executables..." -ForegroundColor Yellow

        $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if (-not $signtool) {
            $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
                        Sort-Object FullName -Descending | Select-Object -First 1
        }
        if (-not $signtool) {
            Write-Host "  signtool.exe not found. Install the Windows 10/11 SDK (Visual Studio Installer > Individual components)." -ForegroundColor Red
            exit 1
        }
        # Get-Command returns .Source, Get-ChildItem returns .FullName (no ?? in Windows PowerShell 5.1)
        $signtool = if ($signtool.PSObject.Properties['FullName']) { $signtool.FullName } else { $signtool.Source }

        $certArgs = @()
        if ($SignCert) {
            if (-not (Test-Path $SignCert)) { Write-Host "  Certificate not found: $SignCert" -ForegroundColor Red; exit 1 }
            $secure = Read-Host -Prompt "  PFX password" -AsSecureString
            $pw = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
            $certArgs = @("/f", $SignCert, "/p", $pw)
        } else {
            $certArgs = @("/sha1", $SignThumbprint)
        }

        $targets = @(
            (Join-Path $PublishDir "SecureGateway.exe"),
            (Join-Path $v2rayDst "v2ray.exe")
        )
        foreach ($t in $targets) {
            & $signtool sign /fd SHA256 /td SHA256 /tr $TimestampUrl @certArgs $t
            if ($LASTEXITCODE -ne 0) { Write-Host "  Signing FAILED for $t" -ForegroundColor Red; exit 1 }
            & $signtool verify /pa /q $t
            if ($LASTEXITCODE -ne 0) { Write-Host "  Signature verification FAILED for $t" -ForegroundColor Red; exit 1 }
            Write-Host "  Signed: $t" -ForegroundColor Green
        }
    } else {
        Write-Host "  Note: output is unsigned. Windows 11 Smart App Control will block it;" -ForegroundColor DarkYellow
        Write-Host "        pass -SignCert or -SignThumbprint to sign (see header of this script)." -ForegroundColor DarkYellow
    }

    Write-Host "  Published to: $PublishDir" -ForegroundColor Green
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
