#!/usr/bin/env bash
# SecureGateway VPS setup — Shadowsocks (AEAD) server for any x86_64/aarch64 Linux
# with systemd (Debian 11/12, Ubuntu 20.04+, ...).
#
# Installs shadowsocks-rust as a static binary from GitHub — no distro packages, so it
# works on end-of-life releases (e.g. Debian 11 after Aug 2026) whose apt repos have
# gone away — and configures it so the SecureGateway client can import it in one click.
#
# Note: this is Shadowsocks (AEAD), which the client supports — not ShadowsocksR (SSR),
# an unmaintained fork the client does not speak.
#
# Run on a fresh VPS as root:
#
#   bash shadowsocks-setup.sh                 # aes-256-gcm on port 8388
#   PORT=443 bash shadowsocks-setup.sh        # different port
#   METHOD=chacha20-ietf-poly1305 bash shadowsocks-setup.sh
#   SS_VERSION=v1.22.0 bash shadowsocks-setup.sh   # pin a release instead of latest
#
# Auto-publish to Supabase (server appears in every user's app, no copy/paste):
#
#   SG_EMAIL=admin@company.com SG_PASSWORD='secret' bash shadowsocks-setup.sh
#
# Re-running is safe: the existing password is kept and the config reissued;
# re-publishing updates the Supabase row instead of adding a duplicate.

set -euo pipefail

PORT="${PORT:-8388}"
METHOD="${METHOD:-aes-256-gcm}"
SS_VERSION="${SS_VERSION:-}"
CONF_DIR=/etc/shadowsocks
CONF="$CONF_DIR/config.json"
BIN=/usr/local/bin/ssserver
UNIT=/etc/systemd/system/shadowsocks.service
NAME="${NAME:-Vultr $(hostname -s 2>/dev/null || echo VPS) (SS)}"

SUPABASE_URL="${SUPABASE_URL:-https://yahzzatmmmdmwalindai.supabase.co}"
SUPABASE_ANON_KEY="${SUPABASE_ANON_KEY:-eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InlhaHp6YXRtbW1kbXdhbGluZGFpIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzE3ODA1NzMsImV4cCI6MjA4NzM1NjU3M30._OKdLLUDN80GR7nMHYsI3S0WmzPmGw-7QmpZYEIREj4}"
SG_EMAIL="${SG_EMAIL:-}"
SG_PASSWORD="${SG_PASSWORD:-}"

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "run as root (use: sudo bash $0)"
command -v systemctl >/dev/null || die "systemd is required"
command -v curl >/dev/null || die "curl is required (apt-get install curl)"

case "$METHOD" in
    aes-128-gcm|aes-256-gcm|chacha20-ietf-poly1305|xchacha20-ietf-poly1305) ;;
    *) die "METHOD must be one of: aes-128-gcm, aes-256-gcm, chacha20-ietf-poly1305, xchacha20-ietf-poly1305" ;;
esac

case "$(uname -m)" in
    x86_64|amd64)   ARCH=x86_64 ;;
    aarch64|arm64)  ARCH=aarch64 ;;
    *) die "unsupported CPU architecture: $(uname -m)" ;;
esac

# ---- install shadowsocks-rust (static binary, distro-independent) ---------------------
resolve_latest_version() {
    # 1) follow the /releases/latest redirect; 2) GitHub API; either yields "vX.Y.Z"
    local v
    v=$(curl -fsSI --max-time 30 https://github.com/shadowsocks/shadowsocks-rust/releases/latest 2>/dev/null \
        | tr -d '\r' | awk 'tolower($1)=="location:" {print $2}' | sed -E 's#.*/tag/##')
    if [ -z "$v" ]; then
        v=$(curl -fsS --max-time 30 https://api.github.com/repos/shadowsocks/shadowsocks-rust/releases/latest 2>/dev/null \
            | grep -o '"tag_name": *"[^"]*"' | head -1 | sed -E 's/.*"(v[^"]+)"$/\1/')
    fi
    printf '%s' "$v"
}

if [ -z "$SS_VERSION" ]; then
    log "Looking up latest shadowsocks-rust release"
    SS_VERSION=$(resolve_latest_version)
    [ -n "$SS_VERSION" ] || die "could not determine the latest release — set SS_VERSION=vX.Y.Z and re-run"
fi
case "$SS_VERSION" in v*) ;; *) SS_VERSION="v$SS_VERSION" ;; esac

if [ -x "$BIN" ] && "$BIN" --version 2>/dev/null | grep -q "${SS_VERSION#v}"; then
    log "shadowsocks-rust $SS_VERSION already installed"
else
    ASSET="shadowsocks-${SS_VERSION}.${ARCH}-unknown-linux-musl.tar.xz"
    URL="https://github.com/shadowsocks/shadowsocks-rust/releases/download/${SS_VERSION}/${ASSET}"
    TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT

    log "Downloading $ASSET"
    curl -fsSL --max-time 300 -o "$TMP/$ASSET" "$URL" || die "download failed: $URL"

    if command -v xz >/dev/null; then
        tar -xJf "$TMP/$ASSET" -C "$TMP" || die "archive extraction failed"
    elif command -v python3 >/dev/null; then
        python3 -c "import tarfile,sys; tarfile.open(sys.argv[1]).extractall(sys.argv[2])" "$TMP/$ASSET" "$TMP" \
            || die "archive extraction failed"
    else
        die "need either xz-utils or python3 to extract the archive"
    fi

    [ -f "$TMP/ssserver" ] || die "ssserver not found in archive"
    install -m 0755 "$TMP/ssserver" "$BIN"
    log "Installed $("$BIN" --version 2>/dev/null || echo "ssserver $SS_VERSION") to $BIN"
fi

# ---- password (reuse if already configured) ------------------------------------------
if [ -f "$CONF" ] && grep -q '"password"' "$CONF"; then
    PASSWORD=$(grep -o '"password": *"[^"]*"' "$CONF" | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
    log "Keeping existing password"
elif [ -f /etc/shadowsocks-libev/config.json ] && grep -q '"password"' /etc/shadowsocks-libev/config.json; then
    PASSWORD=$(grep -o '"password": *"[^"]*"' /etc/shadowsocks-libev/config.json | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
    log "Reusing password from an earlier shadowsocks-libev install"
    systemctl disable --now shadowsocks-libev >/dev/null 2>&1 || true
else
    PASSWORD=$(openssl rand -base64 24 2>/dev/null || head -c 24 /dev/urandom | base64)
    log "Generated new password"
fi

# ---- config -----------------------------------------------------------------------------
log "Writing $CONF"
mkdir -p "$CONF_DIR"
cat > "$CONF" <<JSON
{
    "server": "::",
    "server_port": $PORT,
    "password": "$PASSWORD",
    "method": "$METHOD",
    "mode": "tcp_and_udp",
    "timeout": 300,
    "ipv6_only": false
}
JSON
chmod 600 "$CONF"

# ---- service ----------------------------------------------------------------------------
log "Writing $UNIT"
cat > "$UNIT" <<EOF
[Unit]
Description=Shadowsocks server (shadowsocks-rust) for SecureGateway
After=network-online.target
Wants=network-online.target

[Service]
ExecStart=$BIN -c $CONF
Restart=always
RestartSec=3
# Binding a port <1024 (e.g. 443) needs this even as an unprivileged service.
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
# DynamicUser can't read a root-only file; the service needs the config but nothing else.
chmod 644 "$CONF"

# BBR noticeably helps throughput on long-distance links; harmless if unavailable.
if ! sysctl -n net.ipv4.tcp_congestion_control 2>/dev/null | grep -q bbr; then
    printf 'net.core.default_qdisc=fq\nnet.ipv4.tcp_congestion_control=bbr\n' > /etc/sysctl.d/90-bbr.conf
    sysctl -p /etc/sysctl.d/90-bbr.conf >/dev/null 2>&1 || true
fi

# ---- firewall ---------------------------------------------------------------------------
if command -v ufw >/dev/null && ufw status | grep -q '^Status: active'; then
    log "Opening port $PORT in ufw"
    ufw allow "$PORT"/tcp >/dev/null
    ufw allow "$PORT"/udp >/dev/null
fi

# ---- start ------------------------------------------------------------------------------
log "Starting shadowsocks"
systemctl daemon-reload
systemctl enable shadowsocks >/dev/null 2>&1
systemctl restart shadowsocks
sleep 1
systemctl is-active --quiet shadowsocks \
    || { journalctl -u shadowsocks -n 20 --no-pager; die "shadowsocks failed to start"; }

# ---- client details ---------------------------------------------------------------------
PUBLIC_IP=$(curl -fsS -4 https://api.ipify.org 2>/dev/null || curl -fsS -4 https://ifconfig.me 2>/dev/null || hostname -I | awk '{print $1}')

# SIP002 share link: ss://base64(method:password)@host:port#name  (what the app imports)
USERINFO=$(printf '%s:%s' "$METHOD" "$PASSWORD" | base64 -w0)
SS_LINK="ss://${USERINFO}@${PUBLIC_IP}:${PORT}#$(printf '%s' "$NAME" | sed 's/ /%20/g')"

cat <<EOF

========================================================================
  SecureGateway Shadowsocks server is running
========================================================================

  Address    : $PUBLIC_IP
  Port       : $PORT
  Protocol   : Shadowsocks
  Encryption : $METHOD
  Password   : $PASSWORD

  One-click import link (copy the whole line, then in the app click
  Servers -> Import from Clipboard):

$SS_LINK

EOF

# ---- publish to Supabase shared_servers -------------------------------------------------
publish_to_supabase() {
    command -v python3 >/dev/null || { echo "  python3 missing — skipping Supabase publish"; return; }

    SG_EMAIL="$SG_EMAIL" SG_PASSWORD="$SG_PASSWORD" SUPABASE_URL="$SUPABASE_URL" \
    SUPABASE_ANON_KEY="$SUPABASE_ANON_KEY" NAME="$NAME" ADDRESS="$PUBLIC_IP" PORT="$PORT" \
    SS_PASSWORD="$PASSWORD" METHOD="$METHOD" \
    python3 - <<'PY'
import json, os, sys, urllib.request, urllib.error, urllib.parse

url, key = os.environ["SUPABASE_URL"], os.environ["SUPABASE_ANON_KEY"]
enum = {"aes-128-gcm": "Aes128Gcm", "aes-256-gcm": "Aes256Gcm",
        "chacha20-ietf-poly1305": "ChaCha20IetfPoly1305",
        "xchacha20-ietf-poly1305": "XChaCha20IetfPoly1305"}

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
    "remarks": "registered by shadowsocks-setup.sh", "enabled": True, "created_by": uid,
}

q = "/rest/v1/shared_servers?address=eq.%s&port=eq.%s&select=id" % (
    urllib.parse.quote(row["address"]), row["port"])
status, existing = call("GET", q, token=token)
if status == 200 and existing:
    status, resp = call("PATCH", "/rest/v1/shared_servers?id=eq." + existing[0]["id"], row, token, "return=minimal")
    action = "updated"
else:
    status, resp = call("POST", "/rest/v1/shared_servers", row, token, "return=minimal")
    action = "published"

if status in (200, 201, 204):
    print(f"  Supabase: server {action} as '{row['name']}' — it will appear in every user's app on next sync/login.")
else:
    hint = ""
    if status in (401, 403) or (isinstance(resp, dict) and "policy" in json.dumps(resp).lower()):
        hint = " (does this account have gateway.admin? see scripts/supabase-shared-servers.sql)"
    if status == 404:
        hint = " (shared_servers table missing — run scripts/supabase-shared-servers.sql in the Supabase SQL editor)"
    print(f"  Supabase publish failed ({status}): {resp}{hint}")
PY
}

if [ -n "$SG_EMAIL" ] && [ -n "$SG_PASSWORD" ]; then
    publish_to_supabase
else
    cat <<EOF
  To share this server with every user of the app, either import it in the app,
  select it and click Share — or re-run this script with your admin login so it
  registers itself:

    SG_EMAIL=you@company.com SG_PASSWORD='...' bash shadowsocks-setup.sh
EOF
fi

cat <<EOF

  Server log: journalctl -u shadowsocks -f
========================================================================
EOF
