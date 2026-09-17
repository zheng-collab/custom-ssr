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
# Re-running the script is safe: it keeps the existing UUID and reissues the config.

set -euo pipefail

DOMAIN="${1:-}"
PORT="${PORT:-443}"
WS_PATH="${WS_PATH:-/ws}"
CONF_DIR=/usr/local/etc/v2ray
CONF="$CONF_DIR/config.json"
CERT_DIR="$CONF_DIR/tls"
NAME="${NAME:-Vultr $(hostname -s 2>/dev/null || echo VPS)}"

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

  To share this server with every user of the app: import it, select it,
  click Share. It is then stored in Supabase and appears for all accounts.

  Server log: journalctl -u v2ray -f
========================================================================
EOF
