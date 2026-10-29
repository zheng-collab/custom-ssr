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

# End-to-end check. A TCP connection to the relay proves nothing (the relay holds a
# connection with a wrong key open just like a good one), so send a real request through
# the staff server (CHECK_LINK, printed by the relay script) and see which address comes out.
function Test-OfficeExit([string]$Exe) {
    $url = if ($env:CHECK_URL) { $env:CHECK_URL } else { 'https://api.ipify.org' }
    $myIp = try { (Invoke-WebRequest -UseBasicParsing -TimeoutSec 10 -Uri $url).Content.Trim() } catch { 'unknown' }
    $link = $env:CHECK_LINK
    if (-not $link) {
        Write-Host '  Service is running. Not verified end to end (CHECK_LINK not given):'
        Write-Host '  connect to the office server in the app and open https://ipinfo.io.'
        Write-Host "  Expected address: $myIp"
        return $true
    }
    $m = [regex]::Match($link, '^ss://([^@#]+)@([^:#]+):(\d+)')
    if (-not $m.Success) { Write-Host '  CHECK_LINK is not a valid ss:// link - skipping the check.'; return $true }
    $b64 = $m.Groups[1].Value.Replace('_', '/').Replace('-', '+')
    while ($b64.Length % 4) { $b64 += '=' }
    $creds = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
    $method, $pw = $creds.Split(':', 2)
    $port = Get-Random -Minimum 39000 -Maximum 39900
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "sg-check-$port.json"
    $cfg = @{
        log = @{ loglevel = 'none' }
        inbounds = @(@{ listen = '127.0.0.1'; port = $port; protocol = 'http' })
        outbounds = @(@{ protocol = 'shadowsocks'; settings = @{ servers = @(@{
            address = $m.Groups[2].Value; port = [int]$m.Groups[3].Value; method = $method; password = $pw }) } })
    } | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($tmp, $cfg, (New-Object Text.UTF8Encoding($false)))
    $sp = @{ FilePath = $Exe; ArgumentList = @('run', '-c', "`"$tmp`""); PassThru = $true }
    if ($env:OS -eq 'Windows_NT') { $sp.WindowStyle = 'Hidden' }
    else { $sp.RedirectStandardOutput = "$tmp.out"; $sp.RedirectStandardError = "$tmp.err" }
    $p = Start-Process @sp
    $via = $null
    try {
        for ($i = 0; $i -lt 10 -and -not $via; $i++) {
            Start-Sleep -Seconds 3
            try { $via = (Invoke-WebRequest -UseBasicParsing -TimeoutSec 8 -Uri $url -Proxy "http://127.0.0.1:$port").Content.Trim() } catch { $via = $null }
        }
    } finally {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Remove-Item $tmp, "$tmp.out", "$tmp.err" -Force -ErrorAction SilentlyContinue
    }
    if ($via -and $via -eq $myIp) {
        Write-Host "  WORKING. A request through the office server came out as $via (this office)." -ForegroundColor Green
        return $true
    } elseif ($via) {
        Write-Host "  Requests through the office server come out as $via; this computer's own address is $myIp." -ForegroundColor Yellow
        Write-Host '  If the office has several internet addresses that is fine; otherwise check the setup.'
        return $true
    }
    Write-Host '  NOT WORKING. No request got through the office server within 30 s.' -ForegroundColor Red
    Write-Host "  Most likely the tunnel key (LINK_ID) does not match the one on the VPS (VPS log shows 'invalid user'),"
    Write-Host '  or the relay is not running.'
    return $false
}

if ($env:SG_CHECK_ONLY) {
    $exe = if ($env:SG_V2RAY_EXE) { $env:SG_V2RAY_EXE } else { Join-Path $InstallDir 'v2ray.exe' }
    [void](Test-OfficeExit $exe)
    return
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

# ---- end-to-end check ------------------------------------------------------------------
Say 'Checking end to end (up to 30 s)'
Start-Sleep -Seconds 2
Write-Host ''
if (-not (Test-Path $exe)) {
    Write-Host '  v2ray.exe has disappeared - antivirus probably quarantined it.' -ForegroundColor Yellow
    Write-Host '  Restore it in Windows Security > Protection history, allow it, then run this command again.'
} else {
    [void](Test-OfficeExit $exe)
}
Write-Host '  It starts automatically with Windows, before anyone signs in.'
Write-Host "  Check again later:  `$env:SG_CHECK_ONLY='1'; `$env:CHECK_LINK='<link>'; irm <same url> | iex"
Write-Host "  Remove:             `$env:SG_UNINSTALL='1'; irm <same url> | iex"
