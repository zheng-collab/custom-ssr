# SecureGateway 发布教程：不用 Mac 也能生成 macOS 版本

**面向对象：管理员（您）· 更新日期：2026-09-22**

本教程说明如何让 GitHub 自动在它的 Mac 服务器上编译 SecureGateway 的 macOS 版本（同时也生成 Windows 安装包），并把所有安装文件放到一个可以直接分享给员工的下载页面上。**全程不需要拥有 Mac 电脑。**

---

## 目录

1. [原理](#1-原理)
2. [常用链接](#2-常用链接)
3. [前提条件](#3-前提条件)
4. [方法一（推荐）：在 GitHub 网页上发布一个版本](#4-方法一推荐在-github-网页上发布一个版本)
5. [方法二：用命令行打标签发布](#5-方法二用命令行打标签发布)
6. [方法三：只编译、不发布（测试用）](#6-方法三只编译不发布测试用)
7. [把下载链接分享给员工](#7-把下载链接分享给员工)
8. [发布新版本时如何更新版本号](#8-发布新版本时如何更新版本号)
9. [查看进度与排查失败](#9-查看进度与排查失败)
10. [常见问题](#10-常见问题)

---

## 1. 原理

仓库里有一个文件 `.github/workflows/build.yml`，它是给 **GitHub Actions**（GitHub 自带的自动化服务）看的"任务清单"。每次代码有变动，GitHub 会自动：

| 任务 | 运行在 | 产出 |
|------|--------|------|
| `windows` | GitHub 的 Windows 服务器 | `SecureGateway-Setup.exe`、`SecureGateway-win-x64.zip` |
| `macos arm64` | GitHub 的 **Mac** 服务器 | `SecureGateway-macos-arm64.dmg`（Apple 芯片 M1/M2/M3/M4） |
| `macos x64` | GitHub 的 **Mac** 服务器 | `SecureGateway-macos-x64.dmg`（Intel 芯片） |
| `test-ui` | GitHub 的 Linux 服务器 | 自动渲染 macOS 版每个界面并截图，用于检查 |

当您给代码打上一个**版本标签**（例如 `v1.0.0`）时，第四个任务 `release` 会把上面所有文件汇总到仓库的 **Releases（发布）** 页面，形成一个固定的下载链接。

编译 macOS 应用所需的苹果工具（签名、制作 DMG、图标）都在 GitHub 的 Mac 服务器上执行，这就是不需要自己有 Mac 的原因。

---

## 2. 常用链接

| 用途 | 链接 |
|------|------|
| 仓库首页 | https://github.com/zheng-collab/custom-ssr |
| **Actions（查看编译进度）** | https://github.com/zheng-collab/custom-ssr/actions |
| **Releases（下载页面，分享给员工的就是这里）** | https://github.com/zheng-collab/custom-ssr/releases |
| 新建发布（方法一使用） | https://github.com/zheng-collab/custom-ssr/releases/new |
| 手动触发编译（方法三使用） | https://github.com/zheng-collab/custom-ssr/actions/workflows/build.yml |
| 任务清单文件本身 | https://github.com/zheng-collab/custom-ssr/blob/claude/windows-vpn-app-mPjrB/.github/workflows/build.yml |
| GitHub Actions 官方文档（中文） | https://docs.github.com/zh/actions |
| GitHub Releases 官方文档（中文） | https://docs.github.com/zh/repositories/releasing-projects-on-github/managing-releases-in-a-repository |

> 目前所有代码在分支 `claude/windows-vpn-app-mPjrB` 上。如果以后合并到 `main`，把上面链接里的分支名换成 `main` 即可，操作步骤不变。

---

## 3. 前提条件

- 您能用自己的 GitHub 账号登录，并且对仓库 `zheng-collab/custom-ssr` 有写入权限（能推送代码即可）。
- 仓库目前是**公开**的。公开仓库使用 GitHub Actions **免费、不限时长**。如果以后改为私有仓库，免费额度是每月 2000 分钟，且 Mac 服务器按 **10 倍**计费（一次完整发布大约消耗 15–20 分钟的 Mac 时间，即 150–200 分钟额度），请留意。
- 不需要安装任何软件。方法一完全在浏览器里完成。

---

## 4. 方法一（推荐）：在 GitHub 网页上发布一个版本

这是最简单的方式，只用浏览器。

1. 打开 https://github.com/zheng-collab/custom-ssr/releases/new
2. 点击 **Choose a tag（选择标签）** 下拉框，在输入框里输入版本号，格式必须是 `v` 开头，例如 **`v1.0.0`**。然后点击下方出现的 **Create new tag: v1.0.0 on publish（发布时创建新标签）**。
3. 点击 **Target（目标）** 下拉框，选择分支 **`claude/windows-vpn-app-mPjrB`**（以后合并后选 `main`）。
4. **Release title（标题）** 填 `SecureGateway v1.0.0`。描述可以留空或写更新说明。
5. **不要**在这里上传任何文件，安装包会由 GitHub 自动生成并附加。
6. 点击底部绿色的 **Publish release（发布）**。

之后会发生什么：

- 发布页面先出现，但暂时**没有**安装文件。
- GitHub 同时开始编译（大约 8–15 分钟）。进度在 https://github.com/zheng-collab/custom-ssr/actions 查看，最上面一条名为 **build** 的记录就是。
- 编译完成后，刷新发布页面，**Assets（资产）** 区域会自动出现 4 个安装文件：两个 Windows、两个 macOS。

---

## 5. 方法二：用命令行打标签发布

适合已经在电脑上 `git clone` 了仓库的情况。在仓库目录下打开 PowerShell：

```powershell
git checkout claude/windows-vpn-app-mPjrB
git pull
git tag v1.0.0
git push origin v1.0.0
```

效果与方法一相同：推送标签后 GitHub 自动编译并创建发布页面，附上 4 个安装文件。

如果标签打错了想重来（例如版本号写错）：

```powershell
git tag -d v1.0.0
git push origin :refs/tags/v1.0.0
```

然后在 Releases 页面删除对应的发布记录（进入该发布 → **Delete**），再重新打标签。

---

## 6. 方法三：只编译、不发布（测试用）

想在正式发布前先看看能不能编译成功，或者想拿一个测试版：

1. 打开 https://github.com/zheng-collab/custom-ssr/actions/workflows/build.yml
2. 点击右侧的 **Run workflow（运行工作流）**，选择分支 `claude/windows-vpn-app-mPjrB`，再点绿色的 **Run workflow**。
3. 等待完成后点进这次运行记录，页面底部 **Artifacts（产物）** 区域可以下载 `windows`、`macos-arm64`、`macos-x64`、`ui-screenshots` 四个压缩包。

这种方式不会创建发布页面，产物保留 90 天后自动删除，适合内部测试。

另外，**每次向仓库推送代码时 GitHub 也会自动编译一遍**（同样不发布），所以在 Actions 页面看到绿色勾，就说明当前代码是能通过编译的。

---

## 7. 把下载链接分享给员工

发布完成后，把这个链接发给员工即可：

**https://github.com/zheng-collab/custom-ssr/releases/latest**

（`latest` 永远指向最新版本，以后更新不需要重新发链接。）

安装文件名**不包含版本号**，因此每个文件也有固定的直接下载地址，可以放进公司内网页面或邮件模板，永远有效：

| 文件 | 直接下载地址 |
|------|--------------|
| Windows 安装程序 | https://github.com/zheng-collab/custom-ssr/releases/latest/download/SecureGateway-Setup.exe |
| Windows 免安装包 | https://github.com/zheng-collab/custom-ssr/releases/latest/download/SecureGateway-win-x64.zip |
| Mac（Apple 芯片） | https://github.com/zheng-collab/custom-ssr/releases/latest/download/SecureGateway-macos-arm64.dmg |
| Mac（Intel 芯片） | https://github.com/zheng-collab/custom-ssr/releases/latest/download/SecureGateway-macos-x64.dmg |

告诉员工按电脑类型选择文件：

| 电脑 | 下载文件 | 安装说明所在文档 |
|------|----------|------------------|
| Windows 10 / 11 | `SecureGateway-Setup.exe` | 《SecureGateway 用户手册》（USER_MANUAL_ZH.pdf）第 3、4 节 |
| Mac（Apple 芯片：M1/M2/M3/M4） | `SecureGateway-macos-arm64.dmg` | USER_GUIDE.md 中的"方式 A3：macOS" |
| Mac（Intel 芯片，2020 年及以前） | `SecureGateway-macos-x64.dmg` | 同上 |

Mac 用户不确定芯片类型时：点击屏幕左上角  → **关于本机**，"芯片"一行写 Apple 即选 arm64，写 Intel 即选 x64。

**Mac 首次打开的提示：** 由于应用尚未经过苹果公证，首次打开会提示"无法验证开发者"。让用户在"应用程序"中**右键点击 SecureGateway → 打开 → 打开**，只需一次。如需彻底消除该提示，需要购买 Apple Developer ID（99 美元/年）并公证，届时我可以把签名步骤接入这套自动流程。

---

## 8. 发布新版本时如何更新版本号

版本号只需要改**一个文件**：仓库根目录的 `Directory.Build.props`。

1. 在 GitHub 网页打开 https://github.com/zheng-collab/custom-ssr/blob/claude/windows-vpn-app-mPjrB/Directory.Build.props
2. 点击右上角铅笔图标 **Edit（编辑）**。
3. 把 `<Version>1.0.0</Version>` 改成新版本号，例如 `<Version>1.0.1</Version>`。
4. 点击 **Commit changes（提交更改）**，直接提交到当前分支。
5. 按方法一发布，标签填 **`v1.0.1`**（标签要和版本号一致，前面加 `v`）。

Windows 安装程序里显示的版本、macOS 的 `Info.plist`、Release 标题都会自动使用这个号码。安装文件名本身**不带版本号**（例如始终叫 `SecureGateway-Setup.exe`），这样固定下载地址不会变；要区分版本，看 Release 页面的标签或程序"设置"页中的版本号。员工在旧版本上直接运行新安装包即可升级，设置会保留。

---

## 9. 查看进度与排查失败

1. 打开 https://github.com/zheng-collab/custom-ssr/actions
2. 每一行是一次运行。图标含义：🟡 正在运行，✅ 成功，❌ 失败。
3. 点进一次运行，左侧列出 5 个任务：`Headless UI test (Linux)`、`Windows installer + zip`、`macOS arm64 DMG`、`macOS x64 DMG`、`GitHub Release`。
4. 某个任务失败时，点击它，展开红色的步骤，日志最下面通常是错误原因。**把最后 30 行左右复制发给我**，我来修。
5. 修好后不需要重新打标签：在这次运行页面右上角点击 **Re-run failed jobs（重新运行失败的任务）** 即可。

常见的失败原因：

| 现象 | 原因 | 处理 |
|------|------|------|
| `release` 任务失败，提示 `already exists` | 同一个标签已经发布过 | 现在的流程已能自动把文件补到已有发布上；若仍失败，把日志发给我 |
| `macos` 任务在 "Downloading v2ray-core" 失败 | GitHub 下载临时故障 | 点 Re-run failed jobs 重试 |
| 所有任务都没启动 | 仓库设置里关闭了 Actions | 仓库 **Settings → Actions → General**，选择 **Allow all actions and reusable workflows** |
| `release` 任务提示权限不足 | 仓库禁止了工作流写入 | **Settings → Actions → General → Workflow permissions**，选择 **Read and write permissions** 并保存 |

---

## 10. 常见问题

**Q：每次发布都要重新等 10 多分钟吗？**
是的，每个版本都在干净的服务器上从头编译，这也是保证结果可靠的原因。发布不是频繁操作，一般可以接受。

**Q：可以只发布 Windows 版或只发布 Mac 版吗？**
默认四个文件一起生成。如果需要，可以在 `build.yml` 里注释掉不需要的任务，告诉我即可。

**Q：Mac 版是"真正的 Mac 应用"吗？**
是。它在 GitHub 的真实 macOS 服务器上用苹果自己的工具编译、签名并打包成 DMG，和在本地 Mac 上操作的结果一致。

**Q：员工打开 Mac 版时提示"已损坏，无法打开"而不是"无法验证开发者"？**
这是 Gatekeeper 对未公证应用的另一种表现形式。让用户在"终端"里执行：
`xattr -d com.apple.quarantine /Applications/SecureGateway.app`
然后正常打开。根治方法同样是 Apple Developer ID 签名 + 公证。

**Q：我把仓库改成私有会怎样？**
一切照常工作，只是开始消耗每月免费额度（见第 3 节）。员工下载 Release 文件时也需要先登录并被授予仓库访问权限；如果希望员工无需登录即可下载，仓库需要保持公开，或者把安装包另外放到公司网盘。

---

*本教程随代码一起保存在仓库根目录 `RELEASE_TUTORIAL_ZH.md`，并附 PDF 版本。*
