# SecureGateway "office exit" - the office-computer half (Windows 10/11).
#
# Keeps an outbound tunnel open to the VPS relay (scripts/office-exit-relay.sh). Staff
# traffic on the relay's office server comes down that tunnel and leaves from this
# computer's internet connection. No port forwarding needed.
#
# Run the exact command office-exit-relay.sh printed, in PowerShell opened with
# "Run as administrator":
#
#   $env:RELAY='149.248.11.216'; $env:LINK_PORT='8390'; $env:LINK_ID='<uuid>'; irm <url>/office-exit-node.ps1 | iex
#
# Options (set before running):  $env:KEEP_SLEEP='1'  leave power settings alone
#                                $env:SG_UNINSTALL='1' remove the service and files
#
# Safety: requests to private addresses (office LAN, router, localhost) are refused, so
# staff cannot reach office devices through this computer.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is 10x slower with the progress bar

$Relay        = $env:RELAY
$LinkPort     = if ($env:LINK_PORT) { $env:LINK_PORT } else { '8390' }
$LinkId       = $env:LINK_ID
$V2rayVersion = if ($env:V2RAY_VERSION) { $env:V2RAY_VERSION } else { 'v5.22.0' }
$DryRun       = [bool]$env:SG_DRY_RUN
$InstallDir   = if ($env:SG_INSTALL_DIR) { $env:SG_INSTALL_DIR } else { Join-Path $env:ProgramFiles 'SecureGateway Exit' }
$Config       = Join-Path $InstallDir 'config.json'
$Launcher     = Join-Path $InstallDir 'run-exit.ps1'
$TaskName     = 'SecureGateway Office Exit'
$TunnelDomain = 'office.reverse.securegateway'

function Say($m)  { Write-Host "==> $m" -ForegroundColor Cyan }
function Fail($m) { Write-Host "ERROR: $m" -ForegroundColor Red; throw $m }

function Stop-Exit {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*run-exit.ps1*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Get-Process v2ray -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

if (-not $DryRun) {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { Fail 'Open PowerShell with "Run as administrator" and run the command again.' }
}

if ($env:SG_UNINSTALL) {
    Say 'Removing the office exit'
    Stop-Exit
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue
    Say 'Removed.'
    return
}

if (-not $Relay) { Fail 'RELAY is not set - run the command printed by office-exit-relay.sh on the VPS.' }
if ($LinkId -notmatch '^[0-9a-fA-F-]{36}$') { Fail 'LINK_ID is missing or malformed - copy it from the relay script output.' }
if ($LinkPort -notmatch '^\d+$') { Fail 'LINK_PORT must be a number.' }

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# ---- install v2ray-core (checksum-verified) ---------------------------------------------
if (-not $DryRun) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $exe = Join-Path $InstallDir 'v2ray.exe'
    $have = if (Test-Path $exe) { (& $exe version 2>$null | Select-Object -First 1) } else { '' }
    if ($have -like "V2Ray $($V2rayVersion.TrimStart('v')) *") {
        Say "v2ray-core $V2rayVersion already installed"
    } else {
        Stop-Exit
        $asset = 'v2ray-windows-64.zip'
        if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { $asset = 'v2ray-windows-arm64-v8a.zip' }
        $base = "https://github.com/v2fly/v2ray-core/releases/download/$V2rayVersion"
        $zip = Join-Path $env:TEMP $asset
        Say "Downloading $asset ($V2rayVersion)"
        Invoke-WebRequest -UseBasicParsing -Uri "$base/$asset" -OutFile $zip
        $dgst = (Invoke-WebRequest -UseBasicParsing -Uri "$base/$asset.dgst").Content
        if ($dgst -is [byte[]]) { $dgst = [Text.Encoding]::ASCII.GetString($dgst) }
        $want = ([regex]::Match($dgst, 'SHA2-256=\s*([0-9a-fA-F]{64})')).Groups[1].Value
        $got = (Get-FileHash -Algorithm SHA256 $zip).Hash
        if (-not $want -or $want -ne $got) { Remove-Item $zip -Force; Fail "checksum mismatch for $asset - run the command again" }
        Expand-Archive -Path $zip -DestinationPath $InstallDir -Force
        Remove-Item $zip -Force
        Say "Installed $(& $exe version | Select-Object -First 1)"
    }
}

# ---- config -----------------------------------------------------------------------------
# Same routing as office-exit-node.sh: the tunnel to the relay, everything else out
# directly, private/LAN destinations refused (IPOnDemand applies that to hostnames too).
Say "Writing $Config"
$json = @"
{
  "log": { "loglevel": "warning" },
  "reverse": { "bridges": [ { "tag": "bridge", "domain": "$TunnelDomain" } ] },
  "outbounds": [
    {
      "tag": "link",
      "protocol": "vmess",
      "settings": { "vnext": [ { "address": "$Relay", "port": $LinkPort,
                                 "users": [ { "id": "$LinkId", "alterId": 0, "security": "aes-128-gcm" } ] } ] }
    },
    { "tag": "direct", "protocol": "freedom" },
    { "tag": "refused", "protocol": "blackhole" }
  ],
  "routing": {
    "domainStrategy": "IPOnDemand",
    "rules": [
      { "type": "field", "inboundTag": ["bridge"], "domain": ["full:$TunnelDomain"], "outboundTag": "link" },
      { "type": "field", "inboundTag": ["bridge"], "domain": ["full:localhost"], "outboundTag": "refused" },
      { "type": "field", "inboundTag": ["bridge"], "outboundTag": "refused",
        "ip": ["0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
               "192.168.0.0/16", "224.0.0.0/4", "::1/128", "fc00::/7", "fe80::/10"] },
      { "type": "field", "inboundTag": ["bridge"], "outboundTag": "direct" }
    ]
  }
}
"@
# UTF-8 without BOM: v2ray rejects a BOM, and Windows PowerShell 5.1's -Encoding UTF8 adds one.
[IO.File]::WriteAllText($Config, $json, (New-Object Text.UTF8Encoding($false)))

if ($DryRun) { Say "dry run: config written to $Config"; return }

# Only SYSTEM and Administrators may read the config (it holds the tunnel key).
& icacls.exe $InstallDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null

$exe = Join-Path $InstallDir 'v2ray.exe'
$test = & $exe test -c $Config 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { Write-Host $test; Fail 'config rejected' }

# ---- run at startup, restart if it ever exits --------------------------------------------
# A loop in a launcher is more dependable than Task Scheduler's own restart-on-failure,
# which does not fire when the program exits on its own.
@"
`$exe = '$exe'
`$cfg = '$Config'
while (`$true) {
    & `$exe run -c `$cfg
    Start-Sleep -Seconds 5
}
"@ | Set-Content -Path $Launcher -Encoding ASCII

Stop-Exit
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$Launcher`""
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -StartWhenAvailable
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Description 'Tunnel to the SecureGateway VPS relay (office exit).' -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

if (-not $env:KEEP_SLEEP) {
    Say 'Disabling sleep on mains power (the office server stops while this computer sleeps; set KEEP_SLEEP=1 to skip)'
    & powercfg.exe /change standby-timeout-ac 0 | Out-Null
    & powercfg.exe /change hibernate-timeout-ac 0 | Out-Null
}

# ---- check the tunnel came up -----------------------------------------------------------
Say 'Waiting for the tunnel to the relay'
$up = $false
for ($i = 0; $i -lt 15 -and -not $up; $i++) {
    Start-Sleep -Seconds 1
    $up = [bool](Get-NetTCPConnection -State Established -RemotePort ([int]$LinkPort) -ErrorAction SilentlyContinue)
}
$myIp = try { (Invoke-WebRequest -UseBasicParsing -TimeoutSec 10 https://api.ipify.org).Content } catch { 'unknown' }

Write-Host ''
if ($up) {
    Write-Host "  Tunnel to ${Relay}:$LinkPort is UP." -ForegroundColor Green
} elseif (-not (Test-Path $exe)) {
    Write-Host '  v2ray.exe has disappeared - antivirus probably quarantined it.' -ForegroundColor Yellow
    Write-Host "  Restore it in Windows Security > Protection history, allow it, then run this command again."
} else {
    Write-Host "  The task is running but no tunnel to ${Relay}:$LinkPort yet." -ForegroundColor Yellow
    Write-Host "  Check that the relay is running on the VPS and that port $LinkPort is reachable:"
    Write-Host "    Test-NetConnection $Relay -Port $LinkPort"
}
Write-Host "  Staff on the office server will appear as: $myIp"
Write-Host "  It starts automatically with Windows, before anyone signs in."
Write-Host "  Remove:  `$env:SG_UNINSTALL='1'; irm <same url> | iex"
