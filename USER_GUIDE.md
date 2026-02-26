# SecureGateway - User Guide / 用户指南

---

# English Version

## Table of Contents

1. [System Requirements](#1-system-requirements)
2. [Installation](#2-installation)
3. [First Launch & Sign In](#3-first-launch--sign-in)
4. [Main Interface Overview](#4-main-interface-overview)
5. [Dashboard Tab](#5-dashboard-tab)
6. [Servers Tab](#6-servers-tab)
7. [Adding & Editing Servers](#7-adding--editing-servers)
8. [Importing Servers from Share Links](#8-importing-servers-from-share-links)
9. [Connecting to a Server](#9-connecting-to-a-server)
10. [Proxy Modes](#10-proxy-modes)
11. [Settings Tab](#11-settings-tab)
12. [Log Tab](#12-log-tab)
13. [System Tray](#13-system-tray)
14. [Setting Up Your VPS Server](#14-setting-up-your-vps-server)
15. [Troubleshooting](#15-troubleshooting)
16. [Security Notes](#16-security-notes)

---

## 1. System Requirements

- **Operating System:** Windows 10 or Windows 11 (64-bit)
- **Disk Space:** ~200 MB (including v2ray-core)
- **Network:** Active internet connection
- **Account:** A SecureGateway account with `gateway.access` permission

## 2. Installation

### Option A: Run the Pre-Built Executable

1. Download `SecureGateway.exe` from the release package.
2. Place it in any folder (e.g., `C:\Program Files\SecureGateway\`).
3. Download **v2ray-core** from [https://github.com/v2fly/v2ray-core/releases](https://github.com/v2fly/v2ray-core/releases).
4. Extract the v2ray-core files into a `v2ray-core` subdirectory next to the executable.
5. Double-click `SecureGateway.exe` to launch.

### Option B: Build from Source

```powershell
# Clone the repository
git clone <repo-url>
cd custom-ssr

# Download v2ray-core automatically
.\scripts\setup-v2ray.ps1

# Build and publish
.\scripts\build.ps1 -Publish

# Run the published executable
.\build\publish\SecureGateway.exe
```

### Directory Structure After Installation

```
SecureGateway/
├── SecureGateway.exe        ← Main application
└── v2ray-core/
    ├── v2ray.exe            ← V2Ray core engine (required)
    ├── geoip.dat
    └── geosite.dat
```

## 3. First Launch & Sign In

When you first launch SecureGateway, you will see the **Sign In** window.

### Sign In

1. Enter your **Email** and **Password**.
2. (Optional) Check **"Remember me"** to save your credentials for next time.
3. Click **"Sign In"**.
4. If your account has `gateway.access` permission, you will be taken to the main window.

### Sign Up

1. Click the **"Sign Up"** tab at the top of the login window.
2. Enter your **Email**, **Password**, and **Confirm Password**.
3. Click **"Sign Up"**.
4. Check your email for a confirmation link.
5. After confirming your email, return and sign in.

> **Note:** After signing up, an administrator must grant `gateway.access` permission to your account before you can use the app.

### Forgot Password

1. Click **"Forgot password?"** on the sign-in screen.
2. Follow the instructions sent to your email to reset your password.

### Session Persistence

- Your session remains active for up to **120 days**.
- If "Remember me" was checked, the app will auto-sign in on next launch.
- Sessions are automatically refreshed when possible.

## 4. Main Interface Overview

The main window has four sections:

```
┌─────────────────────────────────────────────────┐
│  Header Bar (status indicator, user, connect)   │
├─────────────────────────────────────────────────┤
│  Tab Navigation: Dashboard | Servers | Settings | Log  │
├─────────────────────────────────────────────────┤
│                                                 │
│              Tab Content Area                   │
│                                                 │
├─────────────────────────────────────────────────┤
│  Status Bar (connection status, version)        │
└─────────────────────────────────────────────────┘
```

### Header Bar

- **Status Indicator** — Green circle when connected, red when disconnected.
- **Status Text** — Shows current connection state (e.g., "Connected to Tokyo Server").
- **User Email** — Your logged-in email address.
- **Sign Out** — Logs you out and returns to the sign-in screen.
- **Connect / Disconnect** — The large button to toggle your VPN connection.

### Status Bar

- Shows connection status and app version at the bottom of the window.

## 5. Dashboard Tab

The Dashboard provides a real-time overview of your connection.

### Stats Cards

| Card | Description |
|------|-------------|
| **Upload** | Current upload speed (shown in green) |
| **Download** | Current download speed (shown in blue) |
| **Latency** | Round-trip time to the server in milliseconds (shown in yellow) |
| **Duration** | How long you've been connected (shown in purple) |

### Active Server Panel

- Shows the name and details of the currently selected server.
- **Test** button — Measures latency to the active server.
- **Reconnect** button — Disconnects and reconnects (only visible when connected).

### Proxy Mode Selector

Choose how traffic is routed:

- **Global Proxy** — All internet traffic goes through the VPN.
- **Rule-Based** — Only specific traffic goes through the VPN (using PAC rules).
- **Direct (No Proxy)** — VPN tunnel is active but system proxy is disabled.

## 6. Servers Tab

The Servers tab lets you manage your server profiles.

### Server List

Each server entry shows:
- **Server Name** — A friendly name you assign.
- **Protocol** — V2Ray or Shadowsocks.
- **Address:Port** — The server's IP/domain and port.
- **Local Ports** — SOCKS5 and HTTP proxy ports (default: 10808 / 10809).
- **Remarks** — Optional notes.

### Toolbar Buttons

| Button | Action |
|--------|--------|
| **+ Add Server** | Opens the server configuration dialog to add a new server |
| **Edit** | Edit the currently selected server |
| **Duplicate** | Create a copy of the selected server |
| **Delete** | Remove the selected server (confirmation required) |
| **Import from Clipboard** | Import a server from a `vmess://` or `ss://` share link |
| **Test All** | Test latency for all servers |

## 7. Adding & Editing Servers

Click **"+ Add Server"** or **"Edit"** to open the Server Configuration dialog.

### General Settings

| Field | Description |
|-------|-------------|
| **Server Name** | A friendly name (e.g., "Tokyo Server") |
| **Address** | Server IP address or domain name |
| **Port** | Server port number |
| **Protocol** | V2Ray or Shadowsocks |

### V2Ray (VMess) Settings

| Field | Description |
|-------|-------------|
| **User ID (UUID)** | The authentication UUID from your V2Ray server |
| **Alter ID** | Usually `0` for modern V2Ray (AEAD) |
| **Security** | Encryption method: `auto`, `aes-128-gcm`, `chacha20-poly1305`, `none`, `zero` |
| **Transport** | Connection transport: `TCP`, `WebSocket`, `HTTP2`, `GRPC` |
| **Path** | WebSocket/HTTP2 path (e.g., `/ws`) |
| **Host** | HTTP host header value |
| **Enable TLS** | Check to enable TLS encryption (recommended) |
| **SNI** | Server Name Indication for TLS (usually your domain) |

### Shadowsocks Settings

| Field | Description |
|-------|-------------|
| **Password** | The Shadowsocks server password |
| **Encryption** | Encryption algorithm: `AES-128-GCM`, `AES-256-GCM`, `ChaCha20-Poly1305`, `XChaCha20-Poly1305` |
| **Plugin** | Optional plugin name (e.g., `obfs-local`) |
| **Plugin Options** | Plugin configuration string |

### Local Ports

| Field | Default | Description |
|-------|---------|-------------|
| **SOCKS5 Port** | 10808 | Local SOCKS5 proxy port |
| **HTTP Port** | 10809 | Local HTTP proxy port |

> **Tip:** Only change local ports if you have a conflict with another application.

### Saving

Click **"Save"** to save the server profile, or **"Cancel"** to discard changes.

## 8. Importing Servers from Share Links

SecureGateway can import servers from standard share link formats:

### Supported Formats

- **V2Ray:** `vmess://` (Base64-encoded JSON)
- **Shadowsocks:** `ss://` (Base64-encoded method:password@host:port)

### How to Import

1. Copy a `vmess://` or `ss://` link to your clipboard.
2. Go to the **Servers** tab.
3. Click **"Import from Clipboard"**.
4. The server will be automatically added to your server list.

## 9. Connecting to a Server

### Quick Connect

1. Go to the **Servers** tab and select a server from the list.
2. Click the large **"Connect"** button in the header bar.
3. The status indicator will turn green when connected.

### What Happens When You Connect

1. The app starts the V2Ray or Shadowsocks engine locally.
2. A local SOCKS5 proxy (port 10808) and HTTP proxy (port 10809) are created.
3. Your Windows system proxy is configured based on the selected proxy mode.
4. All compatible traffic is routed through the VPN tunnel.

### Disconnecting

1. Click the **"Disconnect"** button in the header bar.
2. The local proxy engines are stopped.
3. Windows system proxy settings are restored to their previous state.

## 10. Proxy Modes

| Mode | Behavior | Best For |
|------|----------|----------|
| **Global** | All system traffic goes through the proxy | Maximum privacy, accessing region-locked content |
| **Rule-Based** | Uses PAC rules to selectively proxy traffic | Daily use — local sites go direct, blocked sites go through proxy |
| **Direct** | No system proxy configured (tunnel still active) | Manual proxy configuration in specific apps |

### Changing Proxy Mode

- Use the radio buttons in the **Dashboard** tab.
- The change takes effect immediately, even while connected.
- In **Direct** mode, you can still manually configure apps to use `127.0.0.1:10808` (SOCKS5) or `127.0.0.1:10809` (HTTP).

## 11. Settings Tab

### Application Settings

| Setting | Description |
|---------|-------------|
| **Start with Windows** | Automatically launch SecureGateway when Windows starts |
| **Auto-connect on startup** | Automatically connect to the last used server on launch |
| **Minimize to system tray on close** | Closing the window minimizes to tray instead of exiting |

### About

Shows the current app version and supported protocols.

## 12. Log Tab

The Log tab shows real-time connection activity.

- All log entries are displayed in a monospace font with timestamps.
- Click **"Clear Log"** to clear the displayed log.
- Logs are also saved to files at `%APPDATA%\SecureGateway\logs\`.
- Log files are rotated daily and kept for 7 days.

### Log Levels

| Level | Color | Description |
|-------|-------|-------------|
| **Info** | Default | Normal operational messages |
| **Warning** | Yellow | Potential issues that don't prevent operation |
| **Error** | Red | Errors that may affect connectivity |
| **Debug** | Gray | Detailed diagnostic information |

## 13. System Tray

When minimized, SecureGateway sits in the Windows system tray (notification area).

### Tray Icon Actions

- **Double-click** the tray icon to restore the main window.
- **Right-click** the tray icon for a context menu:
  - **Show** — Restore the main window
  - **Connect / Disconnect** — Toggle VPN connection
  - **Exit** — Fully close the application

### Minimize Behavior

- If **"Minimize to system tray on close"** is enabled, clicking the window's X button minimizes to tray.
- To fully exit, right-click the tray icon and select **Exit**.

## 14. Setting Up Your VPS Server

### V2Ray Server (Recommended)

1. **Provision a VPS** — Any Linux VPS works (Ubuntu 22.04 recommended). Providers: Vultr, DigitalOcean, Linode, etc.

2. **Install V2Ray on the server:**
   ```bash
   bash <(curl -L https://raw.githubusercontent.com/v2fly/fhs-install-v2ray/master/install-release.sh)
   ```

3. **Generate a UUID:**
   ```bash
   v2ray uuid
   ```

4. **Edit server config** (`/usr/local/etc/v2ray/config.json`):
   ```json
   {
     "inbounds": [{
       "port": 443,
       "protocol": "vmess",
       "settings": {
         "clients": [{ "id": "YOUR-UUID", "alterId": 0 }]
       },
       "streamSettings": {
         "network": "ws",
         "wsSettings": { "path": "/ws" },
         "security": "tls",
         "tlsSettings": {
           "certificates": [{
             "certificateFile": "/path/to/fullchain.pem",
             "keyFile": "/path/to/privkey.pem"
           }]
         }
       }
     }],
     "outbounds": [{ "protocol": "freedom" }]
   }
   ```

5. **Start the service:**
   ```bash
   systemctl enable v2ray && systemctl start v2ray
   ```

6. **In SecureGateway**, add a server with:
   - Address: Your VPS IP
   - Port: 443
   - Protocol: V2Ray
   - User ID: Your UUID
   - Transport: WebSocket
   - Path: `/ws`
   - TLS: Enabled

### Shadowsocks Server

1. **Install on your VPS:**
   ```bash
   apt install shadowsocks-libev
   ```

2. **Edit config** (`/etc/shadowsocks-libev/config.json`):
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

3. **Start:**
   ```bash
   systemctl enable shadowsocks-libev && systemctl start shadowsocks-libev
   ```

4. **In SecureGateway**, add a server with:
   - Address: Your VPS IP
   - Port: 8388
   - Protocol: Shadowsocks
   - Password: your-strong-password
   - Encryption: AES-256-GCM

## 15. Troubleshooting

### Cannot sign in

| Problem | Solution |
|---------|----------|
| "Access denied" | Your account needs `gateway.access` permission. Contact your administrator. |
| "Invalid credentials" | Check your email and password. Use "Forgot password?" if needed. |
| "Connection error" | Check your internet connection. The Supabase backend may be temporarily unavailable. |

### Cannot connect to server

| Problem | Solution |
|---------|----------|
| Engine fails to start | Ensure `v2ray-core/v2ray.exe` exists in the correct directory. |
| Connection timeout | Verify the server address, port, and that the server is running. |
| TLS handshake failure | Check that TLS settings (SNI, certificates) match your server configuration. |
| Port conflict | Change the local SOCKS5/HTTP ports if another app is using 10808/10809. |

### System proxy not working

| Problem | Solution |
|---------|----------|
| Websites still accessible after connecting | Switch proxy mode from "Direct" to "Global". |
| Some apps bypass the proxy | Some apps don't respect system proxy settings. Configure them manually to use `127.0.0.1:10808` (SOCKS5). |
| Proxy not restored after disconnect | Restart the app or manually reset proxy in Windows Settings > Network & Internet > Proxy. |

### General issues

| Problem | Solution |
|---------|----------|
| App won't start | Ensure only one instance is running (check Task Manager). |
| High latency | Try a different server or transport (WebSocket often performs better than TCP). |
| Logs show errors | Check the Log tab for detailed error messages. Log files are in `%APPDATA%\SecureGateway\logs\`. |

## 16. Security Notes

- **Use TLS** — Always enable TLS for V2Ray connections to prevent traffic inspection.
- **Strong Passwords** — Use passwords of 16+ characters for Shadowsocks.
- **Proxy Cleanup** — System proxy settings are automatically restored when disconnecting. If the app crashes, manually check Windows proxy settings.
- **Credential Storage** — "Remember me" credentials are encrypted with Windows DPAPI (user-scope). Session tokens are stored as JSON files.
- **Single Instance** — The app enforces single-instance mode to prevent port conflicts.
- **Local Only** — Proxy listeners bind to `127.0.0.1` only (not accessible from other devices on your network).

---
---

# 中文版

## 目录

1. [系统要求](#1-系统要求)
2. [安装](#2-安装)
3. [首次启动与登录](#3-首次启动与登录)
4. [主界面概览](#4-主界面概览)
5. [仪表盘页](#5-仪表盘页)
6. [服务器页](#6-服务器页)
7. [添加与编辑服务器](#7-添加与编辑服务器)
8. [通过分享链接导入服务器](#8-通过分享链接导入服务器)
9. [连接服务器](#9-连接服务器)
10. [代理模式](#10-代理模式)
11. [设置页](#11-设置页)
12. [日志页](#12-日志页)
13. [系统托盘](#13-系统托盘)
14. [配置 VPS 服务器](#14-配置-vps-服务器)
15. [常见问题排查](#15-常见问题排查)
16. [安全须知](#16-安全须知)

---

## 1. 系统要求

- **操作系统：** Windows 10 或 Windows 11（64位）
- **磁盘空间：** 约 200 MB（含 v2ray-core）
- **网络：** 可用的互联网连接
- **账户：** 拥有 `gateway.access` 权限的 SecureGateway 账户

## 2. 安装

### 方式 A：运行预编译程序

1. 从发布包下载 `SecureGateway.exe`。
2. 放置到任意文件夹（如 `C:\Program Files\SecureGateway\`）。
3. 从 [https://github.com/v2fly/v2ray-core/releases](https://github.com/v2fly/v2ray-core/releases) 下载 **v2ray-core**。
4. 将 v2ray-core 文件解压到可执行文件旁边的 `v2ray-core` 子目录中。
5. 双击 `SecureGateway.exe` 启动。

### 方式 B：从源码编译

```powershell
# 克隆仓库
git clone <仓库地址>
cd custom-ssr

# 自动下载 v2ray-core
.\scripts\setup-v2ray.ps1

# 编译并发布
.\scripts\build.ps1 -Publish

# 运行发布的可执行文件
.\build\publish\SecureGateway.exe
```

### 安装后的目录结构

```
SecureGateway/
├── SecureGateway.exe        ← 主程序
└── v2ray-core/
    ├── v2ray.exe            ← V2Ray 核心引擎（必需）
    ├── geoip.dat
    └── geosite.dat
```

## 3. 首次启动与登录

首次启动 SecureGateway 时，将显示**登录窗口**。

### 登录

1. 输入您的**邮箱**和**密码**。
2. （可选）勾选**"记住我"**，下次启动时自动填入凭据。
3. 点击**"Sign In"（登录）**。
4. 如果您的账户拥有 `gateway.access` 权限，将进入主窗口。

### 注册

1. 点击登录窗口顶部的**"Sign Up"（注册）**标签。
2. 输入**邮箱**、**密码**和**确认密码**。
3. 点击**"Sign Up"（注册）**。
4. 检查邮箱中的确认链接。
5. 确认邮箱后，返回登录。

> **注意：** 注册后，管理员需要为您的账户授予 `gateway.access` 权限，您才能使用该应用。

### 忘记密码

1. 在登录界面点击**"Forgot password?"（忘记密码？）**。
2. 按照发送到邮箱的说明重置密码。

### 会话保持

- 登录会话最长有效期为 **120 天**。
- 如果勾选了"记住我"，下次启动将自动登录。
- 会话在可能的情况下会自动刷新。

## 4. 主界面概览

主窗口分为四个区域：

```
┌──────────────────────────────────────────────────┐
│  顶部栏（状态指示灯、用户信息、连接按钮）            │
├──────────────────────────────────────────────────┤
│  标签导航：仪表盘 | 服务器 | 设置 | 日志             │
├──────────────────────────────────────────────────┤
│                                                  │
│                标签页内容区域                       │
│                                                  │
├──────────────────────────────────────────────────┤
│  底部状态栏（连接状态、版本号）                      │
└──────────────────────────────────────────────────┘
```

### 顶部栏

- **状态指示灯** — 连接时为绿色圆点，断开时为红色。
- **状态文本** — 显示当前连接状态（如"已连接到 Tokyo Server"）。
- **用户邮箱** — 当前登录的邮箱地址。
- **Sign Out（退出登录）** — 登出并返回登录界面。
- **Connect / Disconnect（连接/断开）** — 切换 VPN 连接的大按钮。

### 底部状态栏

- 显示连接状态和应用版本号。

## 5. 仪表盘页

仪表盘提供连接的实时概览。

### 统计卡片

| 卡片 | 说明 |
|------|------|
| **Upload（上传）** | 当前上传速度（绿色显示） |
| **Download（下载）** | 当前下载速度（蓝色显示） |
| **Latency（延迟）** | 到服务器的往返时间，单位毫秒（黄色显示） |
| **Duration（时长）** | 已连接的持续时间（紫色显示） |

### 当前服务器信息面板

- 显示当前选中服务器的名称和详细信息。
- **Test（测试）** 按钮 — 测量到当前服务器的延迟。
- **Reconnect（重新连接）** 按钮 — 断开并重新连接（仅在连接状态下可见）。

### 代理模式选择器

选择流量路由方式：

- **Global Proxy（全局代理）** — 所有网络流量都通过 VPN。
- **Rule-Based（规则模式）** — 仅特定流量通过 VPN（使用 PAC 规则）。
- **Direct（直连模式）** — VPN 隧道保持活跃，但不配置系统代理。

## 6. 服务器页

服务器页用于管理您的服务器配置。

### 服务器列表

每个服务器条目显示：
- **服务器名称** — 您指定的友好名称。
- **协议** — V2Ray 或 Shadowsocks。
- **地址:端口** — 服务器 IP/域名和端口号。
- **本地端口** — SOCKS5 和 HTTP 代理端口（默认：10808 / 10809）。
- **备注** — 可选的说明文字。

### 工具栏按钮

| 按钮 | 功能 |
|------|------|
| **+ Add Server（添加服务器）** | 打开服务器配置对话框，添加新服务器 |
| **Edit（编辑）** | 编辑当前选中的服务器 |
| **Duplicate（复制）** | 创建选中服务器的副本 |
| **Delete（删除）** | 删除选中的服务器（需确认） |
| **Import from Clipboard（从剪贴板导入）** | 从 `vmess://` 或 `ss://` 分享链接导入服务器 |
| **Test All（测试全部）** | 测试所有服务器的延迟 |

## 7. 添加与编辑服务器

点击 **"+ Add Server"** 或 **"Edit"** 打开服务器配置对话框。

### 通用设置

| 字段 | 说明 |
|------|------|
| **Server Name（服务器名称）** | 友好名称（如"东京服务器"） |
| **Address（地址）** | 服务器 IP 地址或域名 |
| **Port（端口）** | 服务器端口号 |
| **Protocol（协议）** | V2Ray 或 Shadowsocks |

### V2Ray（VMess）设置

| 字段 | 说明 |
|------|------|
| **User ID (UUID)** | V2Ray 服务器的认证 UUID |
| **Alter ID** | 现代 V2Ray (AEAD) 通常填 `0` |
| **Security（加密方式）** | `auto`、`aes-128-gcm`、`chacha20-poly1305`、`none`、`zero` |
| **Transport（传输方式）** | `TCP`、`WebSocket`、`HTTP2`、`GRPC` |
| **Path（路径）** | WebSocket/HTTP2 路径（如 `/ws`） |
| **Host（主机头）** | HTTP 主机头值 |
| **Enable TLS（启用 TLS）** | 勾选以启用 TLS 加密（推荐） |
| **SNI** | TLS 的服务器名称指示（通常为您的域名） |

### Shadowsocks 设置

| 字段 | 说明 |
|------|------|
| **Password（密码）** | Shadowsocks 服务器密码 |
| **Encryption（加密算法）** | `AES-128-GCM`、`AES-256-GCM`、`ChaCha20-Poly1305`、`XChaCha20-Poly1305` |
| **Plugin（插件）** | 可选的插件名称（如 `obfs-local`） |
| **Plugin Options（插件选项）** | 插件配置字符串 |

### 本地端口

| 字段 | 默认值 | 说明 |
|------|--------|------|
| **SOCKS5 Port（SOCKS5 端口）** | 10808 | 本地 SOCKS5 代理端口 |
| **HTTP Port（HTTP 端口）** | 10809 | 本地 HTTP 代理端口 |

> **提示：** 仅在与其他应用冲突时才需要修改本地端口。

### 保存

点击 **"Save"（保存）** 保存服务器配置，或点击 **"Cancel"（取消）** 放弃更改。

## 8. 通过分享链接导入服务器

SecureGateway 支持从标准分享链接格式导入服务器：

### 支持的格式

- **V2Ray：** `vmess://`（Base64 编码的 JSON）
- **Shadowsocks：** `ss://`（Base64 编码的 method:password@host:port）

### 导入步骤

1. 将 `vmess://` 或 `ss://` 链接复制到剪贴板。
2. 进入**服务器**页。
3. 点击 **"Import from Clipboard"（从剪贴板导入）**。
4. 服务器将自动添加到您的服务器列表中。

## 9. 连接服务器

### 快速连接

1. 进入**服务器**页，从列表中选择一个服务器。
2. 点击顶部栏的 **"Connect"（连接）** 大按钮。
3. 连接成功后状态指示灯变为绿色。

### 连接时发生了什么

1. 应用在本地启动 V2Ray 或 Shadowsocks 引擎。
2. 创建本地 SOCKS5 代理（端口 10808）和 HTTP 代理（端口 10809）。
3. 根据选定的代理模式配置 Windows 系统代理。
4. 所有兼容的流量通过 VPN 隧道路由。

### 断开连接

1. 点击顶部栏的 **"Disconnect"（断开）** 按钮。
2. 本地代理引擎停止运行。
3. Windows 系统代理设置恢复到之前的状态。

## 10. 代理模式

| 模式 | 行为 | 适用场景 |
|------|------|----------|
| **Global（全局）** | 所有系统流量通过代理 | 最大隐私保护、访问区域锁定内容 |
| **Rule-Based（规则）** | 使用 PAC 规则选择性代理流量 | 日常使用——国内网站直连，被屏蔽网站走代理 |
| **Direct（直连）** | 不配置系统代理（隧道仍然活跃） | 在特定应用中手动配置代理 |

### 切换代理模式

- 在**仪表盘**页使用单选按钮切换。
- 即使在连接状态下，更改也会立即生效。
- 在**直连**模式下，您仍然可以手动将应用配置为使用 `127.0.0.1:10808`（SOCKS5）或 `127.0.0.1:10809`（HTTP）。

## 11. 设置页

### 应用设置

| 设置 | 说明 |
|------|------|
| **Start with Windows（开机启动）** | Windows 启动时自动运行 SecureGateway |
| **Auto-connect on startup（启动时自动连接）** | 启动后自动连接上次使用的服务器 |
| **Minimize to system tray on close（关闭时最小化到托盘）** | 关闭窗口时最小化到系统托盘而非退出 |

### 关于

显示当前应用版本和支持的协议。

## 12. 日志页

日志页显示实时的连接活动记录。

- 所有日志条目以等宽字体显示，带有时间戳。
- 点击 **"Clear Log"（清除日志）** 清除显示的日志。
- 日志同时保存在 `%APPDATA%\SecureGateway\logs\` 目录下。
- 日志文件按天轮转，保留 7 天。

### 日志级别

| 级别 | 颜色 | 说明 |
|------|------|------|
| **Info（信息）** | 默认 | 正常运行消息 |
| **Warning（警告）** | 黄色 | 不影响运行的潜在问题 |
| **Error（错误）** | 红色 | 可能影响连接的错误 |
| **Debug（调试）** | 灰色 | 详细的诊断信息 |

## 13. 系统托盘

最小化时，SecureGateway 显示在 Windows 系统托盘（通知区域）。

### 托盘图标操作

- **双击**托盘图标可恢复主窗口。
- **右键**托盘图标显示上下文菜单：
  - **Show（显示）** — 恢复主窗口
  - **Connect / Disconnect（连接/断开）** — 切换 VPN 连接
  - **Exit（退出）** — 完全关闭应用

### 最小化行为

- 如果启用了**"关闭时最小化到托盘"**，点击窗口的 X 按钮会最小化到托盘。
- 要完全退出，右键托盘图标选择 **Exit（退出）**。

## 14. 配置 VPS 服务器

### V2Ray 服务器（推荐）

1. **购买 VPS** — 任何 Linux VPS 均可（推荐 Ubuntu 22.04）。供应商：Vultr、DigitalOcean、Linode 等。

2. **在服务器上安装 V2Ray：**
   ```bash
   bash <(curl -L https://raw.githubusercontent.com/v2fly/fhs-install-v2ray/master/install-release.sh)
   ```

3. **生成 UUID：**
   ```bash
   v2ray uuid
   ```

4. **编辑服务器配置**（`/usr/local/etc/v2ray/config.json`）：
   ```json
   {
     "inbounds": [{
       "port": 443,
       "protocol": "vmess",
       "settings": {
         "clients": [{ "id": "您的UUID", "alterId": 0 }]
       },
       "streamSettings": {
         "network": "ws",
         "wsSettings": { "path": "/ws" },
         "security": "tls",
         "tlsSettings": {
           "certificates": [{
             "certificateFile": "/证书路径/fullchain.pem",
             "keyFile": "/证书路径/privkey.pem"
           }]
         }
       }
     }],
     "outbounds": [{ "protocol": "freedom" }]
   }
   ```

5. **启动服务：**
   ```bash
   systemctl enable v2ray && systemctl start v2ray
   ```

6. **在 SecureGateway 中**添加服务器：
   - 地址：您的 VPS IP
   - 端口：443
   - 协议：V2Ray
   - User ID：您的 UUID
   - 传输方式：WebSocket
   - 路径：`/ws`
   - TLS：启用

### Shadowsocks 服务器

1. **在 VPS 上安装：**
   ```bash
   apt install shadowsocks-libev
   ```

2. **编辑配置**（`/etc/shadowsocks-libev/config.json`）：
   ```json
   {
     "server": "0.0.0.0",
     "server_port": 8388,
     "password": "您的强密码",
     "method": "aes-256-gcm",
     "timeout": 300,
     "fast_open": true,
     "mode": "tcp_and_udp"
   }
   ```

3. **启动服务：**
   ```bash
   systemctl enable shadowsocks-libev && systemctl start shadowsocks-libev
   ```

4. **在 SecureGateway 中**添加服务器：
   - 地址：您的 VPS IP
   - 端口：8388
   - 协议：Shadowsocks
   - 密码：您的强密码
   - 加密：AES-256-GCM

## 15. 常见问题排查

### 无法登录

| 问题 | 解决方案 |
|------|----------|
| "Access denied"（访问被拒绝） | 您的账户需要 `gateway.access` 权限，请联系管理员。 |
| "Invalid credentials"（凭据无效） | 检查邮箱和密码。如需要，使用"忘记密码"功能。 |
| "Connection error"（连接错误） | 检查网络连接。Supabase 后端可能暂时不可用。 |

### 无法连接服务器

| 问题 | 解决方案 |
|------|----------|
| 引擎启动失败 | 确保 `v2ray-core/v2ray.exe` 存在于正确目录中。 |
| 连接超时 | 验证服务器地址、端口，确认服务器正在运行。 |
| TLS 握手失败 | 检查 TLS 设置（SNI、证书）是否与服务器配置匹配。 |
| 端口冲突 | 如果其他应用占用了 10808/10809，请更改本地端口。 |

### 系统代理不工作

| 问题 | 解决方案 |
|------|----------|
| 连接后仍可正常访问网站 | 将代理模式从"直连"切换为"全局"。 |
| 某些应用绕过代理 | 部分应用不遵循系统代理设置，需手动配置使用 `127.0.0.1:10808`（SOCKS5）。 |
| 断开后代理未恢复 | 重启应用或手动在 Windows 设置 > 网络和 Internet > 代理 中重置。 |

### 一般问题

| 问题 | 解决方案 |
|------|----------|
| 应用无法启动 | 确保只有一个实例在运行（检查任务管理器）。 |
| 延迟过高 | 尝试不同的服务器或传输方式（WebSocket 通常比 TCP 性能更好）。 |
| 日志显示错误 | 查看日志页的详细错误信息。日志文件在 `%APPDATA%\SecureGateway\logs\` 目录。 |

## 16. 安全须知

- **使用 TLS** — V2Ray 连接请务必启用 TLS，防止流量被检测。
- **强密码** — Shadowsocks 密码建议 16 个字符以上。
- **代理清理** — 断开连接时系统代理设置会自动恢复。如果应用异常退出，请手动检查 Windows 代理设置。
- **凭据存储** — "记住我"功能使用 Windows DPAPI（用户级别）加密存储凭据。会话令牌以 JSON 文件形式保存。
- **单实例** — 应用强制单实例运行，防止端口冲突。
- **仅本地监听** — 代理仅监听 `127.0.0.1`，网络上的其他设备无法访问。

---

*SecureGateway v1.0.0 — Enterprise VPN Client*
