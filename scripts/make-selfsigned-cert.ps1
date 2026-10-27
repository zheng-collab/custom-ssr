# Creates a SELF-SIGNED code-signing certificate for signing SecureGateway in-house.
#
# Why: a public code-signing certificate (OV ~$200+/yr, EV ~$300+/yr) is out of budget for a
# small internal fleet. A self-signed certificate costs nothing and works fine *as long as the
# matching public certificate is trusted on the machines that run the app* — do that once per PC
# with scripts\trust-cert.ps1. Signed + trusted means Windows SmartScreen / Defender stop treating
# SecureGateway.exe and v2ray.exe as unknown, unsigned binaries (a big driver of false-positive
# quarantine). It does NOT make the app trusted on random outside machines — that is what a paid
# cert buys, and we don't need it for internal use.
#
# Run ONCE on the build machine:
#   .\scripts\make-selfsigned-cert.ps1
# Outputs (git-ignored — keep the .pfx secret, it is the signing private key):
#   certs\SecureGateway-CodeSign.pfx   -> used to sign:  .\scripts\build.ps1 -Installer -SignCert certs\SecureGateway-CodeSign.pfx
#   certs\SecureGateway-CodeSign.cer   -> deployed to each fleet PC: .\scripts\trust-cert.ps1 (run there as admin)
#
# The certificate is valid for 5 years. When it expires, re-run this and re-deploy the new .cer.

param(
    [string]$Subject   = "CN=Fourth Zodiac SecureGateway, O=Fourth Zodiac Inc.",
    [string]$OutDir    = (Join-Path (Split-Path -Parent $PSScriptRoot) "certs"),
    [int]   $ValidYears = 5
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
$pfxPath = Join-Path $OutDir "SecureGateway-CodeSign.pfx"
$cerPath = Join-Path $OutDir "SecureGateway-CodeSign.cer"

if (Test-Path $pfxPath) {
    Write-Host "  $pfxPath already exists. Delete it first if you really want a new certificate" -ForegroundColor Yellow
    Write-Host "  (a new certificate means re-deploying the .cer to every PC)." -ForegroundColor Yellow
    exit 1
}

Write-Host "Creating self-signed code-signing certificate..." -ForegroundColor Cyan
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -KeyAlgorithm RSA -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears($ValidYears) `
    -CertStoreLocation "Cert:\CurrentUser\My"

$securePw = Read-Host -Prompt "  Set a password to protect the .pfx (you will need it when signing)" -AsSecureString

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePw | Out-Null
Export-Certificate   -Cert $cert -FilePath $cerPath -Type CERT        | Out-Null

Write-Host ""
Write-Host "  Signing key (keep secret):   $pfxPath" -ForegroundColor Green
Write-Host "  Public cert (deploy to PCs): $cerPath" -ForegroundColor Green
Write-Host "  Thumbprint:                  $($cert.Thumbprint)" -ForegroundColor Green
Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  1. Build & sign:   .\scripts\build.ps1 -Installer -SignCert `"$pfxPath`""
Write-Host "  2. On each PC (admin, once): copy certs\SecureGateway-CodeSign.cer there and run"
Write-Host "       .\scripts\trust-cert.ps1 -CerPath SecureGateway-CodeSign.cer"
