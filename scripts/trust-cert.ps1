# Trusts the SecureGateway self-signed code-signing certificate on THIS PC.
# Run once per fleet machine, as Administrator. After this, SecureGateway.exe and v2ray.exe
# signed with the matching .pfx validate as a trusted, signed app on this machine.
#
#   # Right-click PowerShell -> Run as administrator, then:
#   .\scripts\trust-cert.ps1 -CerPath .\SecureGateway-CodeSign.cer
#
# Deploy to many machines at once (Active Directory): import the .cer into a GPO under
#   Computer Configuration > Policies > Windows Settings > Security Settings > Public Key Policies
#   > Trusted Publishers  (and Trusted Root Certification Authorities).
#
# This only trusts OUR certificate. It does not lower any other security setting.

param(
    [Parameter(Mandatory = $true)][string]$CerPath
)
$ErrorActionPreference = "Stop"

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "  Run this in an elevated PowerShell (Run as administrator)." -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $CerPath)) { Write-Host "  Certificate not found: $CerPath" -ForegroundColor Red; exit 1 }

# Root: makes the certificate chain trusted. TrustedPublisher: lets the signed app run without the
# "unknown publisher" prompt. Both at machine scope so every user on the PC is covered.
foreach ($store in "Root", "TrustedPublisher") {
    Import-Certificate -FilePath $CerPath -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null
    Write-Host "  Imported into LocalMachine\$store" -ForegroundColor Green
}
Write-Host "Done. SecureGateway signed with the matching key is now trusted on this PC." -ForegroundColor Cyan
