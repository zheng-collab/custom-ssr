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

**To run the app:** Windows 10/11 (x64). Nothing else — the published build is self-contained and bundles v2ray-core, so end users do not need to install .NET or v2ray.

**To build from source:** [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). v2ray-core is already checked in under `src/SecureGateway/v2ray-core/` and is copied into the build automatically.

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

### 2. (Optional) Update the bundled v2ray-core

Only needed when you want to move to a newer v2ray release:

```powershell
.\scripts\setup-v2ray.ps1
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

## macOS

The macOS app shares all logic with the Windows app (`src/SecureGateway.Core`) and has its own UI in `src/SecureGateway.Mac` (Avalonia). It uses `networksetup` for the system proxy, the login Keychain for "Remember me", and a LaunchAgent for start-at-login. The engine is the same v2ray-core.

**Build on a Mac** (needs the .NET 8 SDK and Xcode command line tools):

```bash
git clone <repo-url> && cd custom-ssr
./scripts/build-mac.sh                # Apple Silicon on an M-series Mac, Intel on an Intel Mac
./scripts/build-mac.sh --arch x64     # force an Intel build
```

Output: `build/mac/SecureGateway.app` and `build/SecureGateway-<version>-macos-<arch>.dmg`. The script downloads v2ray-core for macOS (same release as the Windows bundle), assembles the bundle with icon and `Info.plist`, ad-hoc signs it and packs the DMG.

**Install:** open the DMG, drag SecureGateway to Applications. First launch on a Mac other than the build machine: Gatekeeper says the developer cannot be verified because the app is not notarized. Right-click the app → **Open** → **Open** (once), or run `xattr -d com.apple.quarantine /Applications/SecureGateway.app`. When connecting for the first time, macOS asks for the administrator password once so the app may change the system proxy.

**Distributing to staff without the Gatekeeper prompt** requires an Apple Developer ID certificate (US$99/year): `./scripts/build-mac.sh --sign "Developer ID Application: Your Company (TEAMID)"`, then notarize with the `notarytool` command the script prints.

**Headless UI test** (runs anywhere, no Mac needed; renders every window and saves screenshots):

```bash
dotnet run --project tests/SecureGateway.Mac.Smoke -c Release -- /tmp/sg-smoke
```

## Distributing to Users

**Easiest: let GitHub build every installer.** The workflow in `.github/workflows/build.yml` builds the Windows installer + zip on a Windows runner and the macOS DMGs (Apple Silicon and Intel) on a real macOS runner, so no Mac is needed. Pushing a version tag publishes all of them as a GitHub Release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

A few minutes later, the repo's **Releases** page has the download links to share. Every ordinary push also runs the builds plus the headless UI test, and *Actions → build → Run workflow* produces the files on demand as artifacts. (macOS builds from the runner are ad-hoc signed; add a Developer ID certificate as a secret and pass `--sign` to notarize.)

**Or build locally.** Windows packages (both imply `-Publish`):

```powershell
.\scripts\build.ps1 -Installer          # build\SecureGateway-Setup-<version>.exe   (needs Inno Setup 6)
.\scripts\build.ps1 -Zip                # build\SecureGateway-<version>-win-x64.zip (no installer needed)
.\scripts\build.ps1 -Installer -Zip -SignCert C:\certs\company.pfx   # signed, for Smart App Control
```

- **Installer** (recommended for staff): Start menu entry, optional desktop icon, in-place upgrades, clean uninstall. Install Inno Setup once with `winget install JRSoftware.InnoSetup`.
- **Zip**: extract anywhere and run `SecureGateway.exe`; keep the `v2ray-core` folder next to it.

Publish either file as a **GitHub Release** (repo → Releases → Draft a new release → attach the file) to get a permanent download link. Windows 11 PCs with Smart App Control enabled only run signed builds; see the *Prerequisites* note on code signing.

To ship a new version: bump `<Version>` in `src/SecureGateway/SecureGateway.csproj`, rebuild, upload. The installer upgrades an existing install in place; users keep their settings.

## Project Structure

```
custom-ssr/
├── SecureGateway.sln
├── scripts/
│   ├── build.ps1              # Build script
├── installer/
│   └── SecureGateway.iss      # Inno Setup definition (build.ps1 -Installer)
├── scripts/
│   ├── deploy-server.ps1      # Run server-setup.sh on a VPS from Windows (one SSH session)
│   ├── server-setup.sh        # One-command V2Ray server install for the VPS
│   ├── shadowsocks-setup.sh   # One-command Shadowsocks (AEAD) server install (Debian/Ubuntu)
│   └── setup-v2ray.ps1        # Updates the bundled v2ray-core (optional)
├── src/SecureGateway.Core/     # Shared logic (both platforms): models, config, v2ray engine,
│                               #   auth/Supabase, connection service, view-model base
├── src/SecureGateway.Mac/      # macOS app (Avalonia UI + networksetup/Keychain/LaunchAgent)
├── tests/SecureGateway.Mac.Smoke/  # Headless render test for the macOS UI
└── src/SecureGateway/          # Windows app (WPF UI + registry proxy, DPAPI, Run key)
    ├── Services/               # Windows system proxy (registry/WinINET), DPAPI credential store
    ├── UI/
    │   ├── Converters/         # WPF value converters
    │   ├── ViewModels/         # MVVM view models
    │   └── Views/              # WPF windows and controls
    └── Utils/                  # Windows auto-start (Run key)
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
