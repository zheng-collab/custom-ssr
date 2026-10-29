#!/usr/bin/env bash
# SecureGateway "office exit" — the office-computer half (Linux: Ubuntu, Debian, ...).
#
# Keeps an outbound tunnel open to the VPS relay (scripts/office-exit-relay.sh). Staff
# traffic on the relay's office server comes down that tunnel and leaves from this
# computer's internet connection. No port forwarding needed. Run the exact command the
# relay script printed, e.g.:
#
#   curl -fsSL .../office-exit-node.sh | sudo RELAY=149.248.11.216 LINK_PORT=8390 LINK_ID=<uuid> bash
#
# Safety: requests to private addresses (the office LAN, the router, localhost) are
# refused, so staff cannot reach office devices through this box.
# KEEP_SLEEP=1 leaves the computer's sleep settings alone (default: sleep is disabled,
# because the server stops working while this box sleeps).

set -euo pipefail

RELAY="${RELAY:-}"
LINK_PORT="${LINK_PORT:-8390}"
LINK_ID="${LINK_ID:-}"
V2RAY_VERSION="${V2RAY_VERSION:-v5.22.0}"
BIN_DIR="${BIN_DIR:-/usr/local/lib/sg-exit}"
CONF_DIR="${CONF_DIR:-/etc/sg-exit}"
CONF="$CONF_DIR/config.json"
UNIT=/etc/systemd/system/sg-exit.service
TUNNEL_DOMAIN="office.reverse.securegateway"
KEEP_SLEEP="${KEEP_SLEEP:-}"
SG_DRY_RUN="${SG_DRY_RUN:-}"

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }

[ -n "$RELAY" ] || die "RELAY is not set — run the command printed by office-exit-relay.sh on the VPS"
[[ "$LINK_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || die "LINK_ID is missing or malformed — copy it from the relay script output"
[[ "$LINK_PORT" =~ ^[0-9]+$ ]] || die "LINK_PORT must be a number"

if [ -z "$SG_DRY_RUN" ]; then
    [ "$(id -u)" -eq 0 ] || die "run as root (put sudo before bash)"
    command -v systemctl >/dev/null || die "systemd is required"
    command -v curl >/dev/null || die "curl is required (sudo apt-get install curl)"
fi

install_v2ray() {
    local asset
    case "$(uname -m)" in
        x86_64|amd64)  asset=v2ray-linux-64.zip ;;
        aarch64|arm64) asset=v2ray-linux-arm64-v8a.zip ;;
        *) die "unsupported CPU architecture: $(uname -m)" ;;
    esac
    if [ -x "$BIN_DIR/v2ray" ] && "$BIN_DIR/v2ray" version 2>/dev/null | grep -q "V2Ray ${V2RAY_VERSION#v} "; then
        log "v2ray-core $V2RAY_VERSION already installed"; return
    fi
    local base="https://github.com/v2fly/v2ray-core/releases/download/$V2RAY_VERSION"
    local tmp; tmp=$(mktemp -d)
    log "Downloading $asset ($V2RAY_VERSION)"
    curl -fsSL --max-time 300 -o "$tmp/$asset" "$base/$asset" || die "download failed: $base/$asset"
    local want got
    want=$(curl -fsSL --max-time 60 "$base/$asset.dgst" | awk '/^SHA2-256=/ {print $2}')
    got=$(sha256sum "$tmp/$asset" | awk '{print $1}')
    [ -n "$want" ] && [ "$want" = "$got" ] || die "checksum mismatch for $asset (expected $want, got $got) — re-run"
    mkdir -p "$BIN_DIR"
    if command -v unzip >/dev/null; then
        unzip -qo "$tmp/$asset" -d "$BIN_DIR"
    else
        python3 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" "$tmp/$asset" "$BIN_DIR" \
            || die "need unzip or python3 to extract the archive"
    fi
    chmod 0755 "$BIN_DIR/v2ray"
    rm -rf "$tmp"
    log "Installed $("$BIN_DIR/v2ray" version | head -1)"
}
if [ -z "$SG_DRY_RUN" ] && [ -z "${SG_CHECK_ONLY:-}" ]; then install_v2ray; fi

CHECK_LINK="${CHECK_LINK:-}"
if [ -n "${SG_CHECK_ONLY:-}" ]; then :; else
# ---- config -----------------------------------------------------------------------------
# The bridge opens the tunnel to the relay ("link"); everything that arrives down it goes
# out directly, except private/LAN destinations, which are dropped. IPOnDemand makes the
# LAN check apply to hostnames too (e.g. a name that resolves to 192.168.x.x).
log "Writing $CONF"
mkdir -p "$CONF_DIR"
umask 077
cat > "$CONF" <<JSON
{
  "log": { "loglevel": "warning" },
  "reverse": { "bridges": [ { "tag": "bridge", "domain": "$TUNNEL_DOMAIN" } ] },
  "outbounds": [
    {
      "tag": "link",
      "protocol": "vmess",
      "settings": { "vnext": [ { "address": "$RELAY", "port": $LINK_PORT,
                                 "users": [ { "id": "$LINK_ID", "alterId": 0, "security": "aes-128-gcm" } ] } ] }
    },
    { "tag": "direct", "protocol": "freedom" },
    { "tag": "refused", "protocol": "blackhole" }
  ],
  "routing": {
    "domainStrategy": "IPOnDemand",
    "rules": [
      { "type": "field", "inboundTag": ["bridge"], "domain": ["full:$TUNNEL_DOMAIN"], "outboundTag": "link" },
      { "type": "field", "inboundTag": ["bridge"], "domain": ["full:localhost"], "outboundTag": "refused" },
      { "type": "field", "inboundTag": ["bridge"], "outboundTag": "refused",
        "ip": ["0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
               "192.168.0.0/16", "224.0.0.0/4", "::1/128", "fc00::/7", "fe80::/10"] },
      { "type": "field", "inboundTag": ["bridge"], "outboundTag": "direct" }
    ]
  }
}
JSON
chmod 600 "$CONF"

[ -z "$SG_DRY_RUN" ] || { log "dry run: config written to $CONF"; exit 0; }

# Unprivileged account that alone can read the config (it holds the tunnel key).
id sg-exit >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin sg-exit
chown sg-exit: "$CONF"

"$BIN_DIR/v2ray" test -c "$CONF" >/dev/null || { "$BIN_DIR/v2ray" test -c "$CONF"; die "config rejected"; }

# ---- service ----------------------------------------------------------------------------
log "Writing $UNIT"
cat > "$UNIT" <<EOF
[Unit]
Description=SecureGateway office exit (tunnel to the VPS relay)
After=network-online.target
Wants=network-online.target

[Service]
Environment=V2RAY_LOCATION_ASSET=$BIN_DIR
ExecStart=$BIN_DIR/v2ray run -c $CONF
User=sg-exit
Restart=always
RestartSec=5
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable sg-exit >/dev/null 2>&1
systemctl restart sg-exit

if [ -z "$KEEP_SLEEP" ]; then
    log "Disabling sleep/suspend (the office server stops while this computer sleeps; KEEP_SLEEP=1 to skip)"
    systemctl mask sleep.target suspend.target hibernate.target hybrid-sleep.target >/dev/null 2>&1 || true
fi

fi  # end of install (skipped by SG_CHECK_ONLY)

# ---- end-to-end check ------------------------------------------------------------------
# A TCP connection to the relay proves nothing: the relay holds a connection with a wrong
# key open just like a good one. So send a real request through the staff server (the
# CHECK_LINK printed by the relay script) and see which address comes out.
check_exit() {
    local link="${CHECK_LINK:-}"
    MYIP=$(curl -fsS -4 --noproxy '*' --max-time 10 "${CHECK_URL:-https://api.ipify.org}" 2>/dev/null || echo "unknown")
    if [ -z "$link" ]; then
        echo "  Service is running. Not verified end to end (CHECK_LINK not given):"
        echo "  connect to the office server in the app and open https://ipinfo.io."
        echo "  Expected address: $MYIP"
        return 0
    fi
    local body=${link#ss://}; body=${body%%#*}
    local userinfo=${body%@*} hostport=${body##*@}
    local host=${hostport%:*} port=${hostport##*:}
    userinfo=$(printf '%s' "$userinfo" | tr '_-' '/+'); while [ $(( ${#userinfo} % 4 )) -ne 0 ]; do userinfo="$userinfo="; done
    local creds; creds=$(printf '%s' "$userinfo" | base64 -d 2>/dev/null) || creds=""
    local method=${creds%%:*} pw=${creds#*:}
    [ -n "$method" ] && [ -n "$pw" ] || { echo "  CHECK_LINK is not a valid ss:// link — skipping the check."; return 0; }

    local tmp lport; tmp=$(mktemp -d); lport=$(( 39000 + RANDOM % 900 ))
    cat > "$tmp/check.json" <<JSON
{ "log": { "loglevel": "none" },
  "inbounds": [ { "listen": "127.0.0.1", "port": $lport, "protocol": "http" } ],
  "outbounds": [ { "protocol": "shadowsocks",
                   "settings": { "servers": [ { "address": "$host", "port": $port, "method": "$method", "password": "$pw" } ] } } ] }
JSON
    "$BIN_DIR/v2ray" run -c "$tmp/check.json" >/dev/null 2>&1 & local cpid=$!
    local via="" i
    for i in $(seq 1 10); do
        sleep 3
        via=$(curl -fsS -4 --noproxy '' --max-time 8 -x "http://127.0.0.1:$lport" "${CHECK_URL:-https://api.ipify.org}" 2>/dev/null) && [ -n "$via" ] && break
        via=""
    done
    kill "$cpid" 2>/dev/null; rm -rf "$tmp"

    if [ -n "$via" ] && [ "$via" = "$MYIP" ]; then
        printf '  \033[1;32mWORKING.\033[0m A request through the office server came out as %s (this office).\n' "$via"
    elif [ -n "$via" ]; then
        echo "  Requests through the office server come out as $via; this computer's own address is $MYIP."
        echo "  If the office has several internet addresses that is fine; otherwise check the setup."
    else
        printf '  \033[1;31mNOT WORKING.\033[0m No request got through the office server within 30 s.\n'
        echo "  Most likely the tunnel key (LINK_ID) does not match the one on the VPS"
        echo "  (VPS log shows 'invalid user'), or the relay is not running."
        return 1
    fi
}

if [ -n "${SG_CHECK_ONLY:-}" ]; then check_exit; exit $?; fi

log "Checking end to end (up to 30 s)"
sleep 2
systemctl is-active --quiet sg-exit || { journalctl -u sg-exit -n 20 --no-pager; die "sg-exit failed to start"; }
echo
check_exit || true
cat <<EOF
  Service log: journalctl -u sg-exit -f      Stop: sudo systemctl disable --now sg-exit
EOF
