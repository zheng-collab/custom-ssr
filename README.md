# SecureGateway - Enterprise VPN Client

A Windows desktop application for securing your company's internet access using V2Ray (VMess) and Shadowsocks protocols. Connects to your VPS server (e.g., Vultr) to encrypt and tunnel all network traffic.

## Features

- **V2Ray (VMess) Protocol** - Full support with WebSocket, HTTP/2, gRPC, and TCP transports
- **Shadowsocks Protocol** - AEAD encryption (AES-256-GCM, ChaCha20-Poly1305, etc.)
- **Three Proxy Modes** - Global proxy, rule-based routing, or direct connection
- **System Proxy Integration** - Automatically configures Windows system proxy settings
- **System Tray** - Minimize to tray, quick connect/disconnect from tray icon
- **Server Management** - Add, edit, duplicate, delete server profiles
- **Import/Export** - Import servers from vmess:// and ss:// share links (clipboard)
- **Latency Testing** - Test server response times
- **Logging** - Real-time connection logs with file persistence
- **Auto-Start** - Optional Windows startup with auto-connect
- **Dark Theme UI** - Modern Catppuccin-themed WPF interface

## Prerequisites

- Windows 10/11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building)
- [v2ray-core](https://github.com/v2fly/v2ray-core/releases) (required for V2Ray/Shadowsocks connections)

## Quick Start

### 1. Clone and Build

```powershell
git clone <repo-url>
cd custom-ssr

# Build
.\scripts\build.ps1

# Or build + publish self-contained exe
.\scripts\build.ps1 -Publish
```

### 2. Install v2ray-core

```powershell
# Automatic download
.\scripts\setup-v2ray.ps1

# Or manually download from https://github.com/v2fly/v2ray-core/releases
# and place files in src/SecureGateway/v2ray-core/
```

### 3. Run

```powershell
dotnet run --project src/SecureGateway
```

Or run the published executable from `build/publish/SecureGateway.exe`.

## Setting Up Your Vultr VPS Server

### V2Ray Server Setup (Recommended)

1. **Create a Vultr VPS** - Ubuntu 22.04, any plan ($5/mo works fine)

2. **SSH into your server** and install v2ray:
   ```bash
   bash <(curl -L https://raw.githubusercontent.com/v2fly/fhs-install-v2ray/master/install-release.sh)
   ```

3. **Generate a UUID** for authentication:
   ```bash
   v2ray uuid
   ```

4. **Configure v2ray** - Edit `/usr/local/etc/v2ray/config.json`:
   ```json
   {
     "inbounds": [{
       "port": 443,
       "protocol": "vmess",
       "settings": {
         "clients": [{
           "id": "YOUR-UUID-HERE",
           "alterId": 0
         }]
       },
       "streamSettings": {
         "network": "ws",
         "wsSettings": {
           "path": "/ws"
         },
         "security": "tls",
         "tlsSettings": {
           "certificates": [{
             "certificateFile": "/path/to/fullchain.pem",
             "keyFile": "/path/to/privkey.pem"
           }]
         }
       }
     }],
     "outbounds": [{
       "protocol": "freedom"
     }]
   }
   ```

5. **Start v2ray**:
   ```bash
   systemctl enable v2ray
   systemctl start v2ray
   ```

6. **In SecureGateway**, add a server with:
   - Address: your Vultr VPS IP
   - Port: 443
   - Protocol: V2Ray
   - User ID: the UUID you generated
   - Transport: WebSocket
   - Path: /ws
   - TLS: enabled

### Shadowsocks Server Setup

1. **Install shadowsocks** on your Vultr VPS:
   ```bash
   apt install shadowsocks-libev
   ```

2. **Configure** `/etc/shadowsocks-libev/config.json`:
   ```json
   {
     "server": "0.0.0.0",
     "server_port": 8388,
     "password": "your-strong-password",
     "method": "aes-256-gcm",
     "timeout": 300,
     "fast_open": true,
     "mode": "tcp_and_udp"
   }
   ```

3. **Start the service**:
   ```bash
   systemctl enable shadowsocks-libev
   systemctl start shadowsocks-libev
   ```

4. **In SecureGateway**, add a server with:
   - Address: your Vultr VPS IP
   - Port: 8388
   - Protocol: Shadowsocks
   - Password: your-strong-password
   - Encryption: AES-256-GCM

## Proxy Modes

| Mode | Description |
|------|-------------|
| **Global** | All traffic goes through the VPN proxy |
| **Rule-Based** | Uses PAC rules to determine which traffic is proxied |
| **Direct** | No proxy - all traffic goes directly (disables system proxy) |

## Importing Servers

Copy a `vmess://` or `ss://` share link to your clipboard, then click **Import from Clipboard** in the Servers tab.

## Project Structure

```
custom-ssr/
├── SecureGateway.sln
├── scripts/
│   ├── build.ps1              # Build script
│   └── setup-v2ray.ps1        # V2Ray core installer
└── src/SecureGateway/
    ├── Core/
    │   ├── Config/             # Configuration management
    │   ├── Engines/            # V2Ray and Shadowsocks engine wrappers
    │   ├── Logging/            # Application logging
    │   └── Routing/            # PAC/routing rule management
    ├── Models/                 # Data models
    ├── Services/               # Connection service, system proxy
    ├── UI/
    │   ├── Converters/         # WPF value converters
    │   ├── ViewModels/         # MVVM view models
    │   └── Views/              # WPF windows and controls
    └── Utils/                  # Auto-start, clipboard helpers
```

## Configuration

Settings are stored in `%APPDATA%/SecureGateway/settings.json`. Logs are in `%APPDATA%/SecureGateway/logs/`.

## Security Notes

- Passwords and UUIDs are stored locally in the settings file
- System proxy settings are automatically restored when disconnecting
- TLS is recommended for V2Ray connections to prevent traffic inspection
- Use strong passwords (16+ characters) for Shadowsocks
- The application runs as a single instance to prevent port conflicts

## License

MIT
