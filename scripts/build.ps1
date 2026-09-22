# SecureGateway Build Script
# Run from the repository root: .\scripts\build.ps1
#
# Code signing (needed for Windows 11 Smart App Control / clean SmartScreen):
#   .\scripts\build.ps1 -Publish -SignCert C:\certs\company.pfx
#     -> prompts for the PFX password, signs SecureGateway.exe and v2ray-core\v2ray.exe
#   .\scripts\build.ps1 -Publish -SignThumbprint 1A2B...   (cert already in the user store / HSM token)
# Requires signtool.exe from the Windows SDK (installed with Visual Studio, or
# "Windows 10/11 SDK" from the Visual Studio Installer).
#
# Distribution packages (both imply -Publish):
#   .\scripts\build.ps1 -Zip          -> build\SecureGateway-<version>-win-x64.zip
#   .\scripts\build.ps1 -Installer    -> build\SecureGateway-Setup-<version>.exe  (needs Inno Setup 6)
#   .\scripts\build.ps1 -Zip -Installer -SignCert C:\certs\company.pfx   (signed installer too)

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Publish,
    [switch]$Clean,
    [switch]$Zip,
    [switch]$Installer,
    [string]$SignCert = "",
    [string]$SignThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$SolutionDir = Split-Path -Parent $PSScriptRoot
$ProjectDir = Join-Path $SolutionDir "src\SecureGateway"
$OutputDir = Join-Path $SolutionDir "build"
if ($Zip -or $Installer) { $Publish = $true }

$Version = ([xml](Get-Content "$ProjectDir\SecureGateway.csproj")).Project.PropertyGroup.Version |
           Where-Object { $_ } | Select-Object -First 1
if (-not $Version) { $Version = "1.0.0" }

# ---- code-signing helpers (used for the executables and, if built, the installer) ----
$script:SignTool = $null
$script:CertArgs = @()

function Initialize-Signing {
    if (-not ($SignCert -or $SignThumbprint)) { return }

    $st = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($st) { $script:SignTool = $st.Source }
    else {
        $found = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
                 Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { $script:SignTool = $found.FullName }
    }
    if (-not $script:SignTool) {
        Write-Host "  signtool.exe not found. Install the Windows 10/11 SDK (Visual Studio Installer > Individual components)." -ForegroundColor Red
        exit 1
    }

    if ($SignCert) {
        if (-not (Test-Path $SignCert)) { Write-Host "  Certificate not found: $SignCert" -ForegroundColor Red; exit 1 }
        $secure = Read-Host -Prompt "  PFX password" -AsSecureString
        $pw = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
        $script:CertArgs = @("/f", $SignCert, "/p", $pw)
    } else {
        $script:CertArgs = @("/sha1", $SignThumbprint)
    }
}

function Invoke-Sign([string]$Path) {
    if (-not $script:SignTool) { return }
    & $script:SignTool sign /fd SHA256 /td SHA256 /tr $TimestampUrl @script:CertArgs $Path
    if ($LASTEXITCODE -ne 0) { Write-Host "  Signing FAILED for $Path" -ForegroundColor Red; exit 1 }
    & $script:SignTool verify /pa /q $Path
    if ($LASTEXITCODE -ne 0) { Write-Host "  Signature verification FAILED for $Path" -ForegroundColor Red; exit 1 }
    Write-Host "  Signed: $Path" -ForegroundColor Green
}

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

    # A SecureGateway (or its v2ray engine) still running from the publish folder locks
    # the exe and the bundler fails with "Access to the path ... is denied". Stop only
    # instances that were started from this exact output folder.
    $running = Get-Process -Name "SecureGateway", "v2ray" -ErrorAction SilentlyContinue |
               Where-Object { $_.Path -and $_.Path.StartsWith($PublishDir, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) {
        Write-Host "  Stopping running instance(s) from $PublishDir ..." -ForegroundColor DarkYellow
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
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

    # Debug symbols are not needed by end users (and embed local source paths).
    Get-ChildItem $PublishDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

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
    Initialize-Signing
    if ($script:SignTool) {
        Write-Host "[4/4] Signing executables..." -ForegroundColor Yellow
        Invoke-Sign (Join-Path $PublishDir "SecureGateway.exe")
        Invoke-Sign (Join-Path $v2rayDst "v2ray.exe")
    } else {
        Write-Host "  Note: output is unsigned. Windows 11 Smart App Control will block it;" -ForegroundColor DarkYellow
        Write-Host "        pass -SignCert or -SignThumbprint to sign (see header of this script)." -ForegroundColor DarkYellow
    }

    Write-Host "  Published to: $PublishDir" -ForegroundColor Green

    # ---- distribution packages ----------------------------------------------------------
    if ($Zip) {
        $zipPath = Join-Path $OutputDir "SecureGateway-$Version-win-x64.zip"
        Write-Host "Creating $zipPath ..." -ForegroundColor Yellow
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
        Write-Host "  Zip: $zipPath  ($([math]::Round((Get-Item $zipPath).Length / 1MB)) MB)" -ForegroundColor Green
    }

    if ($Installer) {
        Write-Host "Building installer ..." -ForegroundColor Yellow
        $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        $isccPath = if ($iscc) { $iscc.Source } else {
            @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
              "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
        }
        if (-not $isccPath) {
            Write-Host "  Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php (or: winget install JRSoftware.InnoSetup)" -ForegroundColor Red
            exit 1
        }

        $iss = Join-Path $SolutionDir "installer\SecureGateway.iss"
        & $isccPath /Q "/DMyAppVersion=$Version" "/DSourceDir=$PublishDir" "/DOutputDir=$OutputDir" $iss
        if ($LASTEXITCODE -ne 0) { Write-Host "  Installer build FAILED!" -ForegroundColor Red; exit 1 }

        $setupExe = Join-Path $OutputDir "SecureGateway-Setup-$Version.exe"
        Invoke-Sign $setupExe
        Write-Host "  Installer: $setupExe  ($([math]::Round((Get-Item $setupExe).Length / 1MB)) MB)" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
