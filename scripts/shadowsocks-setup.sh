#!/usr/bin/env bash
# SecureGateway VPS setup — Shadowsocks (AEAD) server for Debian 11/12 and Ubuntu.
# Installs shadowsocks-libev from the distro repository and configures it so the
# SecureGateway Windows client can import it with one click.
#
# Note: this is Shadowsocks (AEAD), which the client supports — not ShadowsocksR (SSR),
# an unmaintained fork the client does not speak.
#
# Run on a fresh VPS as root:
#
#   bash shadowsocks-setup.sh                 # aes-256-gcm on port 8388
#   PORT=443 bash shadowsocks-setup.sh        # different port
#   METHOD=chacha20-ietf-poly1305 bash shadowsocks-setup.sh
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
CONF_DIR=/etc/shadowsocks-libev
CONF="$CONF_DIR/config.json"
NAME="${NAME:-Vultr $(hostname -s 2>/dev/null || echo VPS) (SS)}"

SUPABASE_URL="${SUPABASE_URL:-https://yahzzatmmmdmwalindai.supabase.co}"
SUPABASE_ANON_KEY="${SUPABASE_ANON_KEY:-eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InlhaHp6YXRtbW1kbXdhbGluZGFpIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzE3ODA1NzMsImV4cCI6MjA4NzM1NjU3M30._OKdLLUDN80GR7nMHYsI3S0WmzPmGw-7QmpZYEIREj4}"
SG_EMAIL="${SG_EMAIL:-}"
SG_PASSWORD="${SG_PASSWORD:-}"

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "run as root (use: sudo bash $0)"
command -v apt-get >/dev/null || die "this script supports Debian/Ubuntu only"

case "$METHOD" in
    aes-128-gcm|aes-256-gcm|chacha20-ietf-poly1305|xchacha20-ietf-poly1305) ;;
    *) die "METHOD must be one of: aes-128-gcm, aes-256-gcm, chacha20-ietf-poly1305, xchacha20-ietf-poly1305" ;;
esac

export DEBIAN_FRONTEND=noninteractive
log "Installing shadowsocks-libev"
apt-get update -qq
apt-get install -y -qq shadowsocks-libev curl ca-certificates >/dev/null
command -v ss-server >/dev/null || die "shadowsocks-libev did not install"

# ---- password (reuse if already configured) ------------------------------------------
if [ -f "$CONF" ] && grep -q '"password"' "$CONF"; then
    PASSWORD=$(grep -o '"password": *"[^"]*"' "$CONF" | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
    log "Keeping existing password"
else
    PASSWORD=$(openssl rand -base64 24 2>/dev/null || head -c 24 /dev/urandom | base64)
    log "Generated new password"
fi

# ---- config -----------------------------------------------------------------------------
log "Writing $CONF"
mkdir -p "$CONF_DIR"
cat > "$CONF" <<JSON
{
    "server": ["0.0.0.0", "::"],
    "server_port": $PORT,
    "password": "$PASSWORD",
    "method": "$METHOD",
    "timeout": 300,
    "fast_open": false,
    "mode": "tcp_and_udp",
    "nameserver": "1.1.1.1"
}
JSON
chmod 600 "$CONF"

# Debian's unit runs ss-server as user "nobody" via the config above.
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
log "Starting shadowsocks-libev"
systemctl enable shadowsocks-libev >/dev/null 2>&1
systemctl restart shadowsocks-libev
sleep 1
systemctl is-active --quiet shadowsocks-libev \
    || { journalctl -u shadowsocks-libev -n 20 --no-pager; die "shadowsocks-libev failed to start"; }

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

  Server log: journalctl -u shadowsocks-libev -f
========================================================================
EOF
