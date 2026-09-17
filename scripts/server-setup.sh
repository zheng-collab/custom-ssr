#!/usr/bin/env bash
# SecureGateway VPS setup — installs v2ray-core and configures a VMess-over-WebSocket
# inbound that the SecureGateway Windows client can import with one click.
#
# Run on a fresh Ubuntu/Debian VPS (Vultr, DigitalOcean, ...) as root:
#
#   bash server-setup.sh                      # VMess + WebSocket on port 443, no TLS
#   bash server-setup.sh vpn.example.com      # same, plus a Let's Encrypt TLS certificate
#
# With a domain: point an A record at this server's IP *before* running, and make sure
# port 80 is reachable (Let's Encrypt validates over it). TLS is strongly recommended —
# it hides the traffic pattern and the client refuses self-signed certificates.
#
# Auto-publish to Supabase (so the server appears in every user's app, no copy/paste):
#
#   SG_EMAIL=admin@company.com SG_PASSWORD='secret' bash server-setup.sh [domain]
#
# The account must have the "gateway.admin" permission (see supabase-shared-servers.sql).
# Works as a Vultr "Startup Script" too — the server registers itself on first boot.
#
# Re-running the script is safe: it keeps the existing UUID and reissues the config;
# re-publishing updates the existing Supabase row instead of adding a duplicate.

set -euo pipefail

DOMAIN="${1:-}"
PORT="${PORT:-443}"
WS_PATH="${WS_PATH:-/ws}"
CONF_DIR=/usr/local/etc/v2ray
CONF="$CONF_DIR/config.json"
CERT_DIR="$CONF_DIR/tls"
NAME="${NAME:-Vultr $(hostname -s 2>/dev/null || echo VPS)}"

# Same project + public anon key the Windows app is built with.
SUPABASE_URL="${SUPABASE_URL:-https://yahzzatmmmdmwalindai.supabase.co}"
SUPABASE_ANON_KEY="${SUPABASE_ANON_KEY:-eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InlhaHp6YXRtbW1kbXdhbGluZGFpIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzE3ODA1NzMsImV4cCI6MjA4NzM1NjU3M30._OKdLLUDN80GR7nMHYsI3S0WmzPmGw-7QmpZYEIREj4}"
SG_EMAIL="${SG_EMAIL:-}"
SG_PASSWORD="${SG_PASSWORD:-}"

log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "run as root (use: sudo bash $0 ${DOMAIN})"
command -v apt-get >/dev/null || die "this script supports Ubuntu/Debian only"

export DEBIAN_FRONTEND=noninteractive
log "Installing prerequisites"
apt-get update -qq
apt-get install -y -qq curl unzip ca-certificates >/dev/null

log "Installing v2ray-core"
bash <(curl -fsSL https://raw.githubusercontent.com/v2fly/fhs-install-v2ray/master/install-release.sh) >/dev/null
command -v v2ray >/dev/null || die "v2ray did not install"

# ---- UUID (reuse if already configured) ---------------------------------------------
if [ -f "$CONF" ] && grep -q '"id"' "$CONF"; then
    UUID=$(grep -o '"id": *"[^"]*"' "$CONF" | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
    log "Keeping existing UUID $UUID"
else
    UUID=$(v2ray uuid 2>/dev/null || cat /proc/sys/kernel/random/uuid)
    log "Generated UUID $UUID"
fi

# ---- TLS ------------------------------------------------------------------------------
TLS=false
if [ -n "$DOMAIN" ]; then
    log "Obtaining Let's Encrypt certificate for $DOMAIN"
    apt-get install -y -qq certbot >/dev/null
    systemctl stop v2ray 2>/dev/null || true
    certbot certonly --standalone --non-interactive --agree-tos \
        --register-unsafely-without-email -d "$DOMAIN" >/dev/null \
        || die "certificate request failed — is the A record for $DOMAIN pointing here and port 80 open?"

    # v2ray runs as 'nobody'; give it a readable copy and keep it fresh on renewal.
    mkdir -p "$CERT_DIR"
    cat > /etc/letsencrypt/renewal-hooks/deploy/secure-gateway.sh <<HOOK
#!/bin/sh
cp /etc/letsencrypt/live/$DOMAIN/fullchain.pem $CERT_DIR/fullchain.pem
cp /etc/letsencrypt/live/$DOMAIN/privkey.pem   $CERT_DIR/privkey.pem
chown -R nobody:nogroup $CERT_DIR
chmod 600 $CERT_DIR/privkey.pem
systemctl restart v2ray
HOOK
    chmod +x /etc/letsencrypt/renewal-hooks/deploy/secure-gateway.sh
    /etc/letsencrypt/renewal-hooks/deploy/secure-gateway.sh 2>/dev/null || true
    TLS=true
fi

# ---- v2ray config ---------------------------------------------------------------------
log "Writing $CONF"
mkdir -p "$CONF_DIR"
if [ "$TLS" = true ]; then
    STREAM=$(cat <<JSON
      "streamSettings": {
        "network": "ws",
        "wsSettings": { "path": "$WS_PATH" },
        "security": "tls",
        "tlsSettings": {
          "certificates": [{
            "certificateFile": "$CERT_DIR/fullchain.pem",
            "keyFile": "$CERT_DIR/privkey.pem"
          }]
        }
      }
JSON
)
else
    STREAM=$(cat <<JSON
      "streamSettings": {
        "network": "ws",
        "wsSettings": { "path": "$WS_PATH" }
      }
JSON
)
fi

cat > "$CONF" <<JSON
{
  "log": { "loglevel": "warning" },
  "inbounds": [
    {
      "port": $PORT,
      "listen": "0.0.0.0",
      "protocol": "vmess",
      "settings": {
        "clients": [ { "id": "$UUID", "alterId": 0 } ]
      },
$STREAM
    }
  ],
  "outbounds": [
    { "protocol": "freedom", "tag": "direct" },
    { "protocol": "blackhole", "tag": "block" }
  ],
  "routing": {
    "rules": [
      { "type": "field", "ip": [ "geoip:private" ], "outboundTag": "block" }
    ]
  }
}
JSON

v2ray test -config "$CONF" >/dev/null || die "generated config failed validation"

# ---- firewall -------------------------------------------------------------------------
if command -v ufw >/dev/null && ufw status | grep -q '^Status: active'; then
    log "Opening port $PORT in ufw"
    ufw allow "$PORT"/tcp >/dev/null
    [ "$TLS" = true ] && ufw allow 80/tcp >/dev/null
fi

# ---- start ----------------------------------------------------------------------------
log "Starting v2ray"
systemctl enable v2ray >/dev/null 2>&1
systemctl restart v2ray
sleep 1
systemctl is-active --quiet v2ray || { journalctl -u v2ray -n 20 --no-pager; die "v2ray failed to start"; }

# ---- client details -------------------------------------------------------------------
PUBLIC_IP=$(curl -fsS -4 https://api.ipify.org 2>/dev/null || curl -fsS -4 https://ifconfig.me 2>/dev/null || hostname -I | awk '{print $1}')
ADDRESS="${DOMAIN:-$PUBLIC_IP}"
TLS_FLAG=""; [ "$TLS" = true ] && TLS_FLAG="tls"

VMESS_JSON=$(printf '{"v":"2","ps":"%s","add":"%s","port":"%s","id":"%s","aid":"0","scy":"auto","net":"ws","type":"none","host":"%s","path":"%s","tls":"%s","sni":"%s"}' \
    "$NAME" "$ADDRESS" "$PORT" "$UUID" "$ADDRESS" "$WS_PATH" "$TLS_FLAG" "${DOMAIN:-}")
VMESS_LINK="vmess://$(printf '%s' "$VMESS_JSON" | base64 -w0)"

cat <<EOF

========================================================================
  SecureGateway server is running
========================================================================

  Address    : $ADDRESS
  Port       : $PORT
  Protocol   : V2Ray (VMess)
  User ID    : $UUID
  Transport  : WebSocket
  Path       : $WS_PATH
  TLS        : $( [ "$TLS" = true ] && echo "Enabled (Let's Encrypt, auto-renews)" || echo "Disabled  <-- re-run with a domain to enable" )

  One-click import link (copy the whole line, then in the app click
  Servers -> Import from Clipboard):

$VMESS_LINK

EOF

# ---- publish to Supabase shared_servers ---------------------------------------------
publish_to_supabase() {
    command -v python3 >/dev/null || { echo "  python3 missing — skipping Supabase publish"; return; }

    SG_EMAIL="$SG_EMAIL" SG_PASSWORD="$SG_PASSWORD" SUPABASE_URL="$SUPABASE_URL" \
    SUPABASE_ANON_KEY="$SUPABASE_ANON_KEY" NAME="$NAME" ADDRESS="$ADDRESS" PORT="$PORT" \
    UUID="$UUID" WS_PATH="$WS_PATH" TLS="$TLS" SNI="${DOMAIN:-}" \
    python3 - <<'PY'
import json, os, sys, urllib.request, urllib.error, urllib.parse

url, key = os.environ["SUPABASE_URL"], os.environ["SUPABASE_ANON_KEY"]

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
    "protocol": "V2Ray", "v2ray_user_id": os.environ["UUID"], "v2ray_alter_id": 0,
    "v2ray_security": "auto", "v2ray_transport": "WebSocket", "v2ray_path": os.environ["WS_PATH"],
    "v2ray_host": os.environ["ADDRESS"], "v2ray_tls": os.environ["TLS"] == "true",
    "v2ray_sni": os.environ["SNI"], "remarks": "registered by server-setup.sh",
    "enabled": True, "created_by": uid,
}

q = "/rest/v1/shared_servers?address=eq.%s&port=eq.%s&select=id" % (
    urllib.parse.quote(row["address"]), row["port"])
status, existing = call("GET", q, token=token)
if status == 200 and existing:
    sid = existing[0]["id"]
    status, resp = call("PATCH", "/rest/v1/shared_servers?id=eq." + sid, row, token, "return=minimal")
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

    SG_EMAIL=you@company.com SG_PASSWORD='...' bash server-setup.sh ${DOMAIN}
EOF
fi

cat <<EOF

  Server log: journalctl -u v2ray -f
========================================================================
EOF
