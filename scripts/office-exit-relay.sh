#!/usr/bin/env bash
# SecureGateway "office exit" — the VPS half.
#
# Adds a second server to the app whose traffic leaves from an office computer (for
# example a NucBox mini PC) instead of from the VPS. Sites that refuse data-centre / VPN
# addresses then see the office's ordinary internet address.
#
#   staff app --(Shadowsocks, port 8389)--> VPS --(tunnel the office box opened)--> office box --> website
#
# The office box dials OUT to the VPS (scripts/office-exit-node.sh or .ps1), so the office
# router needs no port forwarding and the office IP never has to be public. Uses v2ray-core's
# built-in reverse proxy. The existing Shadowsocks server on 8388 is not touched.
#
# Run on the VPS as root:
#
#   bash office-exit-relay.sh
#   STAFF_PORT=8389 LINK_PORT=8390 NAME="US Office" bash office-exit-relay.sh
#   SG_EMAIL=admin@company.com SG_PASSWORD='...' bash office-exit-relay.sh   # also publish to the app
#
# Re-running is safe: the staff password and the office-box key are kept.
# If the office box is offline, staff connections on this server fail instead of silently
# going out from the VPS.

set -euo pipefail

STAFF_PORT="${STAFF_PORT:-8389}"
LINK_PORT="${LINK_PORT:-8390}"
METHOD="${METHOD:-aes-256-gcm}"
V2RAY_VERSION="${V2RAY_VERSION:-v5.22.0}"
NAME="${NAME:-US Office (via NucBox)}"
BIN_DIR="${BIN_DIR:-/usr/local/lib/sg-relay}"
CONF_DIR="${CONF_DIR:-/etc/sg-relay}"
CONF="$CONF_DIR/config.json"
SECRETS="$CONF_DIR/secrets.env"
UNIT=/etc/systemd/system/sg-relay.service
TUNNEL_DOMAIN="office.reverse.securegateway"
# SG_DRY_RUN=1: only write the config into CONF_DIR and print the result (used by tests).
SG_DRY_RUN="${SG_DRY_RUN:-}"

SUPABASE_URL="${SUPABASE_URL:-https://yahzzatmmmdmwalindai.supabase.co}"
SUPABASE_ANON_KEY="${SUPABASE_ANON_KEY:-eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InlhaHp6YXRtbW1kbXdhbGluZGFpIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzE3ODA1NzMsImV4cCI6MjA4NzM1NjU3M30._OKdLLUDN80GR7nMHYsI3S0WmzPmGw-7QmpZYEIREj4}"
SG_EMAIL="${SG_EMAIL:-}"
SG_PASSWORD="${SG_PASSWORD:-}"

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }

case "$METHOD" in
    aes-128-gcm|aes-256-gcm|chacha20-ietf-poly1305) ;;
    *) die "METHOD must be aes-128-gcm, aes-256-gcm or chacha20-ietf-poly1305" ;;
esac
[ "$STAFF_PORT" != "$LINK_PORT" ] || die "STAFF_PORT and LINK_PORT must differ"

if [ -z "$SG_DRY_RUN" ]; then
    [ "$(id -u)" -eq 0 ] || die "run as root (use: sudo bash $0)"
    command -v systemctl >/dev/null || die "systemd is required"
    command -v curl >/dev/null || die "curl is required (apt-get install curl)"
fi

# ---- install v2ray-core (static binary, checksum-verified) ----------------------------
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
[ -n "$SG_DRY_RUN" ] || install_v2ray

# ---- secrets (kept across re-runs) ------------------------------------------------------
mkdir -p "$CONF_DIR"
STAFF_PASSWORD=""; LINK_ID=""
# shellcheck disable=SC1090
[ -f "$SECRETS" ] && . "$SECRETS"
if [ -z "$STAFF_PASSWORD" ] || [ -z "$LINK_ID" ]; then
    STAFF_PASSWORD=$(openssl rand -base64 24 2>/dev/null || head -c 24 /dev/urandom | base64)
    LINK_ID=$(cat /proc/sys/kernel/random/uuid)
    log "Generated new staff password and office-box key"
else
    log "Keeping existing staff password and office-box key"
fi
umask 077
printf "STAFF_PASSWORD='%s'\nLINK_ID='%s'\n" "$STAFF_PASSWORD" "$LINK_ID" > "$SECRETS"

# ---- config -----------------------------------------------------------------------------
# Every connection on the staff port goes to the reverse "portal", i.e. down the tunnel the
# office box keeps open. The only other outbound is a blackhole: nothing exits from the VPS.
log "Writing $CONF"
cat > "$CONF" <<JSON
{
  "log": { "loglevel": "warning" },
  "reverse": { "portals": [ { "tag": "office", "domain": "$TUNNEL_DOMAIN" } ] },
  "inbounds": [
    {
      "tag": "staff",
      "port": $STAFF_PORT,
      "protocol": "shadowsocks",
      "settings": { "method": "$METHOD", "password": "$STAFF_PASSWORD", "network": "tcp,udp" }
    },
    {
      "tag": "link",
      "port": $LINK_PORT,
      "protocol": "vmess",
      "settings": { "clients": [ { "id": "$LINK_ID", "alterId": 0 } ] }
    }
  ],
  "outbounds": [ { "tag": "nowhere", "protocol": "blackhole" } ],
  "routing": {
    "rules": [
      { "type": "field", "inboundTag": ["staff"], "outboundTag": "office" },
      { "type": "field", "inboundTag": ["link"],  "outboundTag": "office" }
    ]
  }
}
JSON
# The service runs as an unprivileged dynamic user, which must be able to read the config.
chmod 644 "$CONF"

# ---- service ----------------------------------------------------------------------------
if [ -z "$SG_DRY_RUN" ]; then
    "$BIN_DIR/v2ray" test -c "$CONF" >/dev/null || { "$BIN_DIR/v2ray" test -c "$CONF"; die "config rejected"; }

    log "Writing $UNIT"
    cat > "$UNIT" <<EOF
[Unit]
Description=SecureGateway office-exit relay (v2ray reverse portal)
After=network-online.target
Wants=network-online.target

[Service]
Environment=V2RAY_LOCATION_ASSET=$BIN_DIR
ExecStart=$BIN_DIR/v2ray run -c $CONF
Restart=always
RestartSec=3
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE
DynamicUser=yes
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF

    if command -v ufw >/dev/null && ufw status | grep -q '^Status: active'; then
        log "Opening ports $STAFF_PORT and $LINK_PORT in ufw"
        ufw allow "$STAFF_PORT"/tcp >/dev/null; ufw allow "$STAFF_PORT"/udp >/dev/null
        ufw allow "$LINK_PORT"/tcp >/dev/null
    fi

    log "Starting sg-relay"
    systemctl daemon-reload
    systemctl enable sg-relay >/dev/null 2>&1
    systemctl restart sg-relay
    sleep 1
    systemctl is-active --quiet sg-relay \
        || { journalctl -u sg-relay -n 20 --no-pager; die "sg-relay failed to start"; }
fi

# ---- output -----------------------------------------------------------------------------
PUBLIC_IP="${PUBLIC_IP:-$(curl -fsS -4 --max-time 10 https://api.ipify.org 2>/dev/null || curl -fsS -4 --max-time 10 https://ifconfig.me 2>/dev/null || hostname -I | awk '{print $1}')}"
USERINFO=$(printf '%s:%s' "$METHOD" "$STAFF_PASSWORD" | base64 | tr -d '\n')
SS_LINK="ss://${USERINFO}@${PUBLIC_IP}:${STAFF_PORT}#$(printf '%s' "$NAME" | sed 's/ /%20/g;s/(/%28/g;s/)/%29/g')"
RAW=https://raw.githubusercontent.com/zheng-collab/custom-ssr/HEAD/scripts

cat <<EOF

========================================================================
  Office-exit relay is running on this VPS
========================================================================

  STEP 1 — on the office computer (NucBox), run ONE of these:

  Linux (Ubuntu / Debian), in a terminal:

    curl -fsSL $RAW/office-exit-node.sh | sudo RELAY=$PUBLIC_IP LINK_PORT=$LINK_PORT LINK_ID=$LINK_ID CHECK_LINK='$SS_LINK' bash

  Windows 10/11, in PowerShell opened with "Run as administrator":

    \$env:RELAY='$PUBLIC_IP'; \$env:LINK_PORT='$LINK_PORT'; \$env:LINK_ID='$LINK_ID'; \$env:CHECK_LINK='$SS_LINK'; irm $RAW/office-exit-node.ps1 | iex

  STEP 2 — staff add this server in the app (Servers -> Import from
  Clipboard) or scan it on phones. It only works while the office box is on.

$SS_LINK

  Keep both the LINK_ID above and this link private. The office-computer
  command ends by sending a real request through this server and prints
  WORKING (with the office address) or NOT WORKING.
EOF

# ---- publish to Supabase shared_servers (same as shadowsocks-setup.sh) -----------------
publish_to_supabase() {
    command -v python3 >/dev/null || { echo "  python3 missing — skipping Supabase publish"; return; }
    SG_EMAIL="$SG_EMAIL" SG_PASSWORD="$SG_PASSWORD" SUPABASE_URL="$SUPABASE_URL" \
    SUPABASE_ANON_KEY="$SUPABASE_ANON_KEY" NAME="$NAME" ADDRESS="$PUBLIC_IP" PORT="$STAFF_PORT" \
    SS_PASSWORD="$STAFF_PASSWORD" METHOD="$METHOD" \
    python3 - <<'PY'
import json, os, sys, urllib.request, urllib.error, urllib.parse

url, key = os.environ["SUPABASE_URL"], os.environ["SUPABASE_ANON_KEY"]
enum = {"aes-128-gcm": "Aes128Gcm", "aes-256-gcm": "Aes256Gcm", "chacha20-ietf-poly1305": "ChaCha20IetfPoly1305"}

def call(method, path, body=None, token=None, prefer=None):
    req = urllib.request.Request(url + path, method=method,
                                 data=json.dumps(body).encode() if body is not None else None)
    req.add_header("apikey", key)
    req.add_header("Content-Type", "application/json")
    if token:  req.add_header("Authorization", "Bearer " + token)
    if prefer: req.add_header("Prefer", prefer)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try: return e.code, json.loads(raw)
        except Exception: return e.code, raw

status, auth = call("POST", "/auth/v1/token?grant_type=password",
                    {"email": os.environ["SG_EMAIL"], "password": os.environ["SG_PASSWORD"]})
if status != 200:
    msg = auth.get("error_description") or auth.get("msg") if isinstance(auth, dict) else auth
    print(f"  Supabase sign-in failed ({status}): {msg}"); sys.exit(0)
token, uid = auth["access_token"], auth["user"]["id"]

row = {
    "name": os.environ["NAME"], "address": os.environ["ADDRESS"], "port": int(os.environ["PORT"]),
    "protocol": "Shadowsocks",
    "ss_password": os.environ["SS_PASSWORD"], "ss_encryption": enum[os.environ["METHOD"]],
    "ss_plugin": "", "ss_plugin_options": "",
    "remarks": "exits from the office connection; works while the office box is on",
    "enabled": True, "created_by": uid,
}
q = "/rest/v1/shared_servers?address=eq.%s&port=eq.%s&select=id" % (urllib.parse.quote(row["address"]), row["port"])
status, existing = call("GET", q, token=token)
if status == 200 and existing:
    status, resp = call("PATCH", "/rest/v1/shared_servers?id=eq." + existing[0]["id"], row, token, "return=minimal")
    action = "updated"
else:
    status, resp = call("POST", "/rest/v1/shared_servers", row, token, "return=minimal")
    action = "published"
if status in (200, 201, 204):
    print(f"  Supabase: server {action} as '{row['name']}' — it appears in every user's app on next sync/login.")
else:
    hint = " (does this account have gateway.admin?)" if status in (401, 403) else ""
    print(f"  Supabase publish failed ({status}): {resp}{hint}")
PY
}

if [ -z "$SG_DRY_RUN" ] && [ -n "$SG_EMAIL" ] && [ -n "$SG_PASSWORD" ]; then
    echo; publish_to_supabase
fi

cat <<EOF

  Relay log: journalctl -u sg-relay -f
========================================================================
EOF
