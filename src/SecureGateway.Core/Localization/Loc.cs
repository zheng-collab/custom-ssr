using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SecureGateway.Localization
{
    public enum UiLanguage { Auto, English, Chinese }

    /// <summary>
    /// UI translation, keyed by the English text itself so call sites stay readable and an
    /// untranslated string simply shows in English. Entries with {0}, {1}… are patterns for
    /// messages built at run time ("Delete server 'X'?"); the captured parts are translated too.
    /// The Log tab is deliberately left in English for support.
    /// Language is chosen once at startup (Settings asks for a restart to change it).
    /// </summary>
    public static class Loc
    {
        public static bool IsChinese { get; private set; }

        public static void Init(UiLanguage language)
        {
            IsChinese = language == UiLanguage.Chinese ||
                        (language == UiLanguage.Auto &&
                         CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Translated text, or the input unchanged when English is active or no entry exists.</summary>
        public static string T(string english) => IsChinese ? Translate(english, 0) : english;

        /// <summary>string.Format over the translated pattern.</summary>
        public static string F(string englishFormat, params object[] args) => string.Format(T(englishFormat), args);

        public static IReadOnlyDictionary<string, string> Chinese => Zh;

        private static string Translate(string s, int depth)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (Zh.TryGetValue(s, out var exact)) return exact;
            if (depth > 2) return s;
            foreach (var (regex, zh) in Patterns.Value)
            {
                var m = regex.Match(s);
                if (!m.Success) continue;
                return Regex.Replace(zh, @"\{(\d+)\}", p =>
                {
                    var g = m.Groups["p" + p.Groups[1].Value];
                    return g.Success ? Translate(g.Value, depth + 1) : p.Value;
                });
            }
            return s;
        }

        // Longest literal text first, so "Connected to {0}:{1} via {2}" wins over "Connected to {0}:{1}".
        private static readonly Lazy<List<(Regex, string)>> Patterns = new(() =>
            Zh.Where(kv => Regex.IsMatch(kv.Key, @"\{\d+\}"))
              .OrderByDescending(kv => Regex.Replace(kv.Key, @"\{\d+\}", "").Length)
              .Select(kv => (new Regex("^" + Regex.Replace(Regex.Escape(kv.Key), @"\\\{(\d+)}", "(?<p$1>.*?)") + "$",
                                       RegexOptions.Singleline | RegexOptions.CultureInvariant), kv.Value))
              .ToList());

        private static readonly Dictionary<string, string> Zh = new()
        {
            // ---- navigation, header, Home -------------------------------------------------
            ["Home"] = "主页",
            ["Servers"] = "服务器",
            ["Settings"] = "设置",
            ["Log"] = "日志",
            ["Sign out"] = "退出登录",
            ["Sign Out"] = "退出登录",
            ["Sign out and return to the login screen?"] = "退出登录并返回登录界面？",
            ["Server"] = "服务器",
            ["Test"] = "测试",
            ["Measure the latency to this server"] = "测试到此服务器的延迟",
            ["Connect"] = "连接",
            ["Disconnect"] = "断开",
            ["Connecting…"] = "正在连接…",
            ["Connecting..."] = "正在连接…",
            ["Connected"] = "已连接",
            ["Not connected"] = "未连接",
            ["Disconnected"] = "已断开",
            ["Not connected to any server"] = "未连接到任何服务器",
            ["No server selected"] = "未选择服务器",
            ["Latency"] = "延迟",
            ["Download"] = "下载",
            ["Upload"] = "上传",
            ["Time"] = "时长",
            ["Timeout"] = "超时",
            ["Routing"] = "路由",
            ["Rule-Based"] = "规则模式",
            ["Global Proxy"] = "全局代理",
            ["Direct (No Proxy)"] = "直连（不代理）",
            ["Everything goes through the VPN, except your bypass list."] = "除“绕过 VPN 的网站”外，所有流量都通过 VPN。",
            ["Mainland-China sites and your bypass list connect directly. Everything else goes through the VPN."] =
                "中国大陆网站和“绕过 VPN 的网站”直接连接，其余流量都通过 VPN。",
            ["The VPN stays connected, but apps only use it if you set them to."] = "VPN 保持连接，但只有手动设置了代理的软件才会使用它。",
            ["No servers yet. Open the Servers tab and click Sync Shared to load your company’s servers."] =
                "还没有服务器。请打开“服务器”页，点击“同步共享服务器”加载公司的服务器。",
            ["Open the Servers tab and click Sync Shared to load your company’s servers."] =
                "请打开“服务器”页，点击“同步共享服务器”加载公司的服务器。",

            // ---- Servers tab ----------------------------------------------------------------
            ["Sync Shared"] = "同步共享服务器",
            ["Load the servers your company has shared"] = "加载公司共享的服务器",
            ["Import from Clipboard"] = "从剪贴板导入",
            ["Paste a vmess:// or ss:// link"] = "粘贴 vmess:// 或 ss:// 链接",
            ["Add Server"] = "添加服务器",
            ["Test All"] = "全部测试",
            ["No servers yet"] = "还没有服务器",
            ["Click Sync Shared to load your company’s servers, or copy a server link and click Import from Clipboard."] =
                "点击“同步共享服务器”加载公司的服务器，或复制服务器链接后点击“从剪贴板导入”。",
            ["Selected server:"] = "选中的服务器：",
            ["Edit"] = "编辑",
            ["Duplicate"] = "复制一份",
            ["Share QR"] = "分享二维码",
            ["Show a QR code / link for phone VPN apps"] = "显示供手机 VPN 应用使用的二维码 / 链接",
            ["Share"] = "共享",
            ["Admins: publish this server to every user"] = "管理员：把此服务器发布给所有用户",
            ["Unshare"] = "取消共享",
            ["Admins: stop sharing this server"] = "管理员：停止共享此服务器",
            ["Delete"] = "删除",
            ["Shared"] = "共享",
            ["Shared Server"] = "共享服务器",
            ["Shared servers cannot be edited. They are managed centrally for all users."] = "共享服务器不能编辑，它们由管理员为所有用户统一管理。",
            ["Shared servers cannot be deleted locally. Use 'Unshare' to remove from the shared pool."] =
                "共享服务器不能在本机删除。请用“取消共享”将其从共享列表中移除。",
            ["Confirm Delete"] = "确认删除",
            ["Delete server '{0}'?"] = "删除服务器“{0}”？",
            ["Import"] = "导入",
            ["No text found in clipboard."] = "剪贴板中没有文本。",
            ["Unsupported share link format. Supported: vmess://, ss://"] = "不支持的链接格式。支持：vmess://、ss://",
            ["The share link could not be parsed."] = "无法解析该分享链接。",
            ["Server '{0}' imported successfully."] = "服务器“{0}”已导入。",
            ["Import Error"] = "导入失败",
            ["Failed to import: {0}"] = "导入失败：{0}",
            ["Shared server list unavailable (offline or not authorised); using {0} cached server(s)."] =
                "无法获取共享服务器列表（离线或无权限），正在使用 {0} 个缓存的服务器。",
            ["Synced {0} shared server(s)."] = "已同步 {0} 个共享服务器。",
            ["Share Server"] = "共享服务器",
            ["Share server '{0}' with all users?\n\nAll authorized users will see this server and can connect to it simultaneously."] =
                "将服务器“{0}”共享给所有用户？\n\n所有授权用户都会看到此服务器，并可同时连接。",
            ["Server Shared"] = "已共享",
            ["Server '{0}' is now shared with all users."] = "服务器“{0}”已共享给所有用户。",
            ["Share Failed"] = "共享失败",
            ["Failed to share server. You may not have permission to publish shared servers."] = "共享失败。您可能没有发布共享服务器的权限。",
            ["Unshare Server"] = "取消共享",
            ["Remove shared server '{0}' from the pool?\n\nOther users will no longer see this server after their next sync."] =
                "将共享服务器“{0}”从共享列表中移除？\n\n其他用户下次同步后将看不到此服务器。",
            ["Unshare Failed"] = "取消共享失败",
            ["Failed to remove shared server. You may not have permission."] = "移除共享服务器失败。您可能没有权限。",
            ["Sign-in expired"] = "登录已过期",
            ["Your saved sign-in is no longer valid. Use Sign Out and sign in again."] = "保存的登录信息已失效。请退出登录后重新登录。",

            // ---- Settings -----------------------------------------------------------------------
            ["Application Settings"] = "应用设置",
            ["Start with Windows"] = "开机自动启动",
            ["Start at login"] = "登录时启动",
            ["Auto-connect on startup"] = "启动后自动连接",
            ["Minimize to system tray on close"] = "关闭窗口时最小化到系统托盘",
            ["Keep running in the menu bar when the window is closed"] = "关闭窗口后继续在菜单栏运行",
            ["Sites that bypass the VPN"] = "绕过 VPN 的网站",
            ["One per line. These connect directly with your real IP, in every proxy mode. Use it for sites that reject the VPN’s address (banks, streaming, company portals). A domain covers all its subdomains; IPs and CIDR ranges work too. Applied when you click elsewhere; the tunnel restarts for a second."] =
                "每行一个。这些网站在任何代理模式下都用您的真实 IP 直接访问，适用于拒绝 VPN 地址的网站（网上银行、视频网站、公司门户）。填域名会自动包含其所有子域名，也支持 IP 和 CIDR 网段。点击输入框以外的地方即生效，已连接时隧道会重启约一秒。",
            ["Automatic (system language)"] = "自动（跟随系统）",
            ["Restart SecureGateway to use the new language."] = "重新启动 SecureGateway 后使用新语言。",
            ["Language"] = "语言",
            ["About"] = "关于",
            [". Supports V2Ray (VMess) and Shadowsocks via the bundled v2ray-core engine."] = "。通过内置的 v2ray-core 引擎支持 V2Ray（VMess）和 Shadowsocks。",
            [" for macOS. Supports V2Ray (VMess) and Shadowsocks via the bundled v2ray-core engine."] = "（macOS 版）。通过内置的 v2ray-core 引擎支持 V2Ray（VMess）和 Shadowsocks。",
            ["Clear Log"] = "清空日志",

            // ---- server editor ----------------------------------------------------------------
            ["Server Configuration"] = "服务器配置",
            ["General"] = "基本信息",
            ["Server Name"] = "服务器名称",
            ["Address (IP or domain)"] = "地址（IP 或域名）",
            ["Port"] = "端口",
            ["Protocol"] = "协议",
            ["V2Ray Settings"] = "V2Ray 设置",
            ["User ID (UUID)"] = "用户 ID（UUID）",
            ["Alter ID"] = "额外 ID（Alter ID）",
            ["Security"] = "加密（Security）",
            ["Transport"] = "传输方式",
            ["Path"] = "路径",
            ["Host"] = "主机（Host）",
            ["Enable TLS"] = "启用 TLS",
            ["SNI (Server Name Indication)"] = "SNI（服务器名称指示）",
            ["Shadowsocks Settings"] = "Shadowsocks 设置",
            ["Password"] = "密码",
            ["Encryption"] = "加密方式",
            ["Plugin (optional)"] = "插件（可选）",
            ["Plugin (optional, not supported by the bundled engine)"] = "插件（可选，内置引擎不支持）",
            ["Plugin Options"] = "插件参数",
            ["Local Ports"] = "本地端口",
            ["SOCKS5 Port"] = "SOCKS5 端口",
            ["HTTP Port"] = "HTTP 端口",
            ["Remarks"] = "备注",
            ["Cancel"] = "取消",
            ["Save"] = "保存",
            ["Validation"] = "请检查输入",
            ["Server address is required."] = "请填写服务器地址。",
            ["Port must be between 1 and 65535."] = "端口必须在 1 到 65535 之间。",
            ["V2Ray User ID is required."] = "请填写 V2Ray 用户 ID。",
            ["Shadowsocks password is required."] = "请填写 Shadowsocks 密码。",

            // ---- share window, dialogs, tray --------------------------------------------------
            ["Share Link / QR Code"] = "分享链接 / 二维码",
            ["Scan with your phone’s VPN app (Android: v2rayNG or Shadowsocks; iPhone: Shadowrocket) or copy the link. Anyone with this link can use the server — share it only with staff."] =
                "用手机 VPN 应用扫码（Android：v2rayNG 或 Shadowsocks；iPhone：Shadowrocket），或复制链接。任何拿到此链接的人都能使用该服务器，请只分享给公司同事。",
            ["Copy link"] = "复制链接",
            ["Copied"] = "已复制",
            ["Copy failed"] = "复制失败",
            ["Close"] = "关闭",
            ["OK"] = "确定",
            ["Yes"] = "是",
            ["No"] = "否",
            ["Show SecureGateway"] = "显示 SecureGateway",
            ["Exit"] = "退出",
            ["Quit SecureGateway"] = "退出 SecureGateway",
            ["SecureGateway is already running."] = "SecureGateway 已在运行。",
            ["Failed to initialize authentication:\n{0}"] = "无法初始化登录服务：\n{0}",
            ["An unexpected error occurred:\n{0}"] = "发生意外错误：\n{0}",

            // ---- sign-in -----------------------------------------------------------------------
            ["SecureGateway - Sign In"] = "SecureGateway - 登录",
            ["Enterprise VPN Client"] = "企业 VPN 客户端",
            ["Sign In"] = "登录",
            ["Sign Up"] = "注册",
            ["Create Account"] = "创建账号",
            ["Email"] = "邮箱",
            ["Confirm Password"] = "确认密码",
            ["Show / hide password"] = "显示 / 隐藏密码",
            ["Forgot password?"] = "忘记密码？",
            ["Remember me"] = "记住我",
            ["Authenticating..."] = "正在验证…",
            ["Can’t reach the login server?"] = "无法连接登录服务器？",
            ["On restricted networks the login server is blocked. Connect through your VPN server first; the sign-in then goes through the tunnel."] =
                "在受限网络中，登录服务器会被屏蔽。请先通过 VPN 服务器连接，之后的登录会经由隧道完成。",
            ["Server link from your administrator (ss:// or vmess://). Leave empty to use a server saved on this PC."] =
                "管理员提供的服务器链接（ss:// 或 vmess://）。留空则使用本机已保存的服务器。",
            ["Server link from your administrator (ss:// or vmess://). Leave empty to use a server saved on this Mac."] =
                "管理员提供的服务器链接（ss:// 或 vmess://）。留空则使用本机已保存的服务器。",
            ["Connect through server, then sign in"] = "先连接服务器，再登录",
            ["Back to sign in"] = "返回登录",
            ["We’ll e-mail a reset code to the address above."] = "我们会向上面的邮箱发送重置码。",
            ["Send reset code"] = "发送重置码",
            ["Reset code, or paste the link from the e-mail"] = "重置码，或粘贴邮件中的链接",
            ["New password"] = "新密码",
            ["Set new password"] = "设置新密码",
            ["Connecting through server..."] = "正在通过服务器连接…",
            ["Connecting through {0}..."] = "正在通过 {0} 连接…",
            ["Tunnel up via {0}. Signing in through it..."] = "已通过 {0} 建立隧道，正在经由隧道登录…",
            ["Connected through the server. Enter your e-mail and password to sign in."] = "已通过服务器连接。请输入邮箱和密码登录。",
            ["Lockout expired. You may try again."] = "锁定已解除，可以重新尝试。",
            ["Opened {0} in your browser.\nReset your password there, then sign in here."] = "已在浏览器中打开 {0}。\n请在网页上重置密码，然后回到这里登录。",
            ["Could not open the browser. Visit {0} to reset your password."] = "无法打开浏览器。请访问 {0} 重置密码。",
            ["Please enter your email address."] = "请输入邮箱地址。",
            ["Please enter your password."] = "请输入密码。",
            ["Password must be at least 6 characters."] = "密码至少需要 6 个字符。",
            ["Passwords do not match."] = "两次输入的密码不一致。",
            ["{0}\n{1} attempt{2} remaining before lockout."] = "{0}\n还剩 {1} 次尝试机会，之后将被锁定。",
            ["{0} attempt{1} remaining"] = "还剩 {0} 次尝试机会",
            ["Too many failed attempts. Try again in {0}:{1}."] = "失败次数过多，请在 {0}:{1} 后重试。",
            ["Too many failed attempts. Account locked.\nTry again in {0}:{1}."] = "失败次数过多，账号已锁定。\n请在 {0}:{1} 后重试。",
            ["Enter your account e-mail address above first."] = "请先在上方输入账号邮箱。",
            ["Enter your account e-mail address above."] = "请在上方输入账号邮箱。",
            ["Sending reset code..."] = "正在发送重置码…",
            ["{0}\nEnter the code (or paste the link) below along with your new password."] = "{0}\n请在下方输入重置码（或粘贴链接）和新密码。",
            ["{0}\nEnter the code below along with your new password."] = "{0}\n请在下方输入重置码和新密码。",
            ["Enter the reset code, or paste the reset link from the e-mail."] = "请输入重置码，或粘贴邮件中的重置链接。",
            ["New password must be at least 6 characters."] = "新密码至少需要 6 个字符。",
            ["Updating password..."] = "正在更新密码…",
            ["Connection error: {0}"] = "连接错误：{0}",
            ["Cannot reach the login server ({0}). If you are on a restricted network, connect through your VPN server first using the option below."] =
                "无法连接登录服务器（{0}）。如果您在受限网络中，请先用下方的选项通过 VPN 服务器连接。",
            ["Sign in failed. The server did not return a session."] = "登录失败：服务器没有返回会话。",
            ["Sign in failed. The session token was not understood."] = "登录失败：无法识别会话令牌。",
            ["Access denied. Your account does not have gateway access."] = "拒绝访问：您的账号没有 VPN 使用权限。",
            ["Could not verify account permissions. Please try again."] = "无法验证账号权限，请重试。",
            ["Account created. Check your e-mail to confirm the account, then sign in."] = "账号已创建。请查收邮件确认账号后再登录。",
            ["Account created. Ask your administrator to grant gateway access, then sign in."] = "账号已创建。请联系管理员开通 VPN 权限后再登录。",
            ["If an account exists for that address, a reset code has been e-mailed to it."] = "如果该邮箱已注册，重置码已发送到该邮箱。",
            ["That reset code or link is invalid, expired, or already used. Request a new one."] = "该重置码或链接无效、已过期或已被使用，请重新申请。",
            ["Could not verify the reset code. Request a new one."] = "无法验证重置码，请重新申请。",
            ["That reset link has expired (they last one hour). Request a new one."] = "重置链接已过期（有效期一小时），请重新申请。",
            ["Could not update the password. Please try again."] = "无法更新密码，请重试。",
            ["Password updated. Sign in with your new password."] = "密码已更新，请用新密码登录。",
            ["Authentication failed. Please check your credentials."] = "验证失败，请检查邮箱和密码。",
            // Common messages returned by the login server itself.
            ["Invalid login credentials"] = "邮箱或密码错误",
            ["Email not confirmed"] = "邮箱尚未确认，请先点击确认邮件中的链接",
            ["User already registered"] = "该邮箱已注册",
            ["That is not a supported server link (expected ss:// or vmess://)."] = "这不是支持的服务器链接（应为 ss:// 或 vmess://）。",
            ["The server link could not be parsed. Copy the whole line from your administrator."] = "无法解析服务器链接。请完整复制管理员提供的整行链接。",
            ["No server is saved on this device yet. Paste the server link from your administrator."] = "本机还没有保存的服务器。请粘贴管理员提供的服务器链接。",
            ["Could not connect through any saved server."] = "无法通过任何已保存的服务器连接。",
            ["{0}: engine did not start"] = "{0}：引擎未能启动",
            ["{0}: tunnel is up but the login server still did not answer through it"] = "{0}：隧道已建立，但登录服务器仍无响应",

            // ---- engine / connection status ---------------------------------------------------
            ["Generating V2Ray configuration..."] = "正在生成 V2Ray 配置…",
            ["VPN engine missing: the download was probably incomplete or antivirus removed it. Re-download SecureGateway-Setup.exe and reinstall, or restore v2ray from antivirus quarantine."] =
                "缺少 VPN 引擎：可能是下载不完整，或被杀毒软件删除。请重新下载 SecureGateway-Setup.exe 并安装，或从杀毒软件隔离区还原 v2ray。",
            ["V2Ray process exited unexpectedly."] = "V2Ray 进程意外退出。",
            ["V2Ray exited with code {0}. See the Log tab for details."] = "V2Ray 已退出（代码 {0}）。详情请查看“日志”页。",
            ["V2Ray did not open local port {0} in time."] = "V2Ray 未能及时打开本地端口 {0}。",
            ["Connected to {0}:{1} via {2}"] = "已通过 {2} 连接到 {0}:{1}",
            ["Connected to {0}:{1}"] = "已连接到 {0}:{1}",
            ["Failed to start V2Ray: {0}"] = "无法启动 V2Ray：{0}",
            ["Stopping V2Ray..."] = "正在停止 V2Ray…",
            ["V2Ray stopped."] = "V2Ray 已停止。",
            ["Connection failed: {0}"] = "连接失败：{0}",
            ["Connection lost while applying the routing change. Please reconnect."] = "应用路由更改时连接中断，请重新连接。",
        };
    }
}
