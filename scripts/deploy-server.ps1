# Deploys the SecureGateway V2Ray server to a VPS from Windows in one step.
# Runs from the repository root:
#
#   .\scripts\deploy-server.ps1 -ServerIp 45.76.1.2
#   .\scripts\deploy-server.ps1 -ServerIp 45.76.1.2 -Domain vpn.example.com
#   .\scripts\deploy-server.ps1 -ServerIp 45.76.1.2 -AdminEmail admin@company.com
#   .\scripts\deploy-server.ps1 -ServerIp 45.76.1.2 -Shadowsocks          # Shadowsocks instead of V2Ray
#
# -AdminEmail makes the server register itself in Supabase (you are prompted for that
#  account's password). The account needs the gateway.admin permission.
# -Shadowsocks runs shadowsocks-setup.sh (AEAD, port 8388) instead of server-setup.sh;
#  -Domain is ignored in that mode.
#
# You will be asked for the VPS root password once (Vultr dashboard -> server -> Password).
# Requires the OpenSSH client that ships with Windows 10/11.

param(
    [Parameter(Mandatory = $true)] [string]$ServerIp,
    [string]$Domain = "",
    [string]$AdminEmail = "",
    [string]$User = "root",
    [switch]$Shadowsocks
)

$ErrorActionPreference = "Stop"
$scriptName = if ($Shadowsocks) { "shadowsocks-setup.sh" } else { "server-setup.sh" }
$script = Join-Path $PSScriptRoot $scriptName
if (-not (Test-Path $script)) { Write-Error "$scriptName not found next to this script."; exit 1 }
if ($Shadowsocks) { $Domain = "" }
if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    Write-Error "ssh not found. Install 'OpenSSH Client' via Settings > Apps > Optional Features."; exit 1
}

$envPrefix = ""
if ($AdminEmail) {
    $secure = Read-Host -Prompt "Supabase password for $AdminEmail" -AsSecureString
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    $escapedPw = $plain -replace "'", "'\''"
    $envPrefix = "SG_EMAIL='$AdminEmail' SG_PASSWORD='$escapedPw' "
}

Write-Host "Connecting to $User@$ServerIp (enter the VPS root password when prompted)..." -ForegroundColor Cyan

# The script travels over stdin, so it needs no scp step and no second password prompt.
# CRLF -> LF in case Git checked the file out with Windows line endings.
$content = (Get-Content $script -Raw) -replace "`r`n", "`n"
$content | ssh -o StrictHostKeyChecking=accept-new "$User@$ServerIp" "${envPrefix}bash -s -- '$Domain'"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Deployment failed (exit $LASTEXITCODE). Scroll up for the error." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Done. Open SecureGateway and click Sync (if you used -AdminEmail)," -ForegroundColor Green
Write-Host "or copy the vmess:// line above and use Servers > Import from Clipboard." -ForegroundColor Green
