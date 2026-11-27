# 发布 AirGlance 0.7.0

[English](shipping.md) · **简体中文**

本文档是 AirGlance 0.7.0（2026-11-23）的发布流程。它涵盖打包脚本、NativeAOT 构建、签名方面的限制，以及用于 Microsoft Store 提交、WACK 和上架信息的 MSIX 包。

0.7.0 版本是一个便携、未签名的桌面版本。它没有安装程序、更新程序、托盘图标或单实例转发。Windows x64 版本也已上架 [Microsoft Store](https://apps.microsoft.com/detail/9p30h2pjg0f6)；仓库中不保存任何签名证书。

## 发布矩阵

| 目标平台 | 公开产物 | 签名 | 构建者 | 分发渠道 |
| --- | --- | --- | --- | --- |
| Windows x64 | `AirGlance-0.7.0-win-x64.zip` | 无 | `packaging/scripts/pack.ps1` | GitHub release |
| Windows arm64 | `AirGlance-0.7.0-win-arm64.zip` | 无 | `windows-latest` 上的 `packaging/scripts/pack.ps1` | GitHub release |
| macOS arm64 | `AirGlance-0.7.0-osx-arm64.zip`（包含 `AirGlance.app`） | 无 | `macos-15` 上的 `packaging/scripts/pack.sh` | GitHub release |
| macOS x64 | `AirGlance-0.7.0-osx-x64.zip`（包含 `AirGlance.app`） | 无 | `macos-15-intel` 上的 `packaging/scripts/pack.sh` | GitHub release |
| Linux x64 | `AirGlance-0.7.0-linux-x64.tar.gz` | 无 | `ubuntu-22.04` 上的 `packaging/scripts/pack.sh` | GitHub release |
| Linux arm64 | `AirGlance-0.7.0-linux-arm64.tar.gz` | 无 | `ubuntu-24.04-arm` 上的 `packaging/scripts/pack.sh` | GitHub release |
| Windows x64 MSIX | `AirGlance_1.0.2.0_x64.msix` | 仓库中无 | `packaging/msix/Build-Msix.ps1` / `store.yml` | Microsoft Store / Partner Center |
| Windows arm64 MSIX | `AirGlance_1.0.2.0_arm64.msix` | 仓库中无 | `packaging/msix/Build-Msix.ps1` / `store.yml` | Microsoft Store / Partner Center |

归档文件名使用三段式产品版本 `0.7.0`。MSIX 文件名使用四段式包版本 `1.0.2.0`。`win-x64`、`win-arm64`、`osx-x64`、`osx-arm64`、`linux-x64` 和 `linux-arm64` 是 .NET 的运行时标识符。

> **当前提交（0.7.2）：** Store 包为 `AirGlance_1.0.4.0_x64.msix`；下面描述的仍是 0.7.0 的发布流程，当前的上传说明见 [packaging/msix/STORE-UPDATE-0.7.2.md](../packaging/msix/STORE-UPDATE-0.7.2.md)。

发布工作流只发布六个便携归档：Windows 的 x64/arm64 zip、macOS 的 x64/arm64 zip 和 Linux 的 x64/arm64 tar.gz。它会拒绝 MSIX 文件，因此 Store 包绝不会出现在公开的 GitHub 发布中。MSIX 文件由 `store.yml` 构建，用于 Store 审核和 Partner Center。

## 运行时验证状态

发布矩阵关注的是产物，而非运行时覆盖情况。v0.7.0 的状态如下：

| 目标平台 | 产物已构建 | AOT 二进制已运行 |
| --- | --- | --- |
| Windows x64 | 是 | **是** — 已启动 0.7.0 本地 AOT 构建并渲染 JPEG 首帧 |
| Linux x64 | 是 | **是** — 由维护者在本仓库 CI 之外对照 0.1.1 验证 |
| Windows arm64 | 是 | 未验证 |
| macOS arm64 | 是 | 未验证 |
| macOS x64 | 是 | 未验证 |
| Linux arm64 | 是 | 未验证 |

另外还有两个缺口是记录在案的，而非宣称已通过：

- 剪贴板图像复制已在 Windows x64 上用未修改的 PNG 和 JPEG 进行检查。Windows 剪贴板中包含 `image/png` 或 `image/jpeg`，同时还有 `CF_DIB`、`CF_DIBV5`、`CF_BITMAP` 和一个位图载荷。复制路径操作以及将原始字节粘贴到第二个应用程序中这两项没有单独测试。
- 125%、150% 和 200% 的 Windows 显示缩放未测试。参照机以 100%（96 DPI）运行；更高缩放下的文本截断情况未验证。
- Microsoft Store 上架地址为 https://apps.microsoft.com/detail/9p30h2pjg0f6。本仓库未记录 WACK 报告或本地安装签名 MSIX 的结果；以 Store 认证为准。

macOS 和 arm64 归档仍然会发布，因为它们在测试中有用；但在有人于相应平台上运行它们之前，请将其视为未验证。

在 100% 缩放下，一张 100 MP 的 JPEG 会在系统临时目录下创建约 400 MB 的临时分块文件。图像关闭时该文件会被删除；因崩溃而残留的文件会在下次启动时、一天之后被移除。

## 构建前提条件

- .NET SDK 10.0.100。`global.json` 使用 `rollForward: latestFeature` 和 `allowPrerelease: false`。
- 每台构建主机上的 NativeAOT 本机工具链：
  - Windows：Visual Studio 2022 的 C++ 桌面工作负载和一个 Windows SDK。
  - macOS：Xcode 命令行工具。`pack.sh` 还会使用 `/usr/libexec/PlistBuddy` 和 `ditto`。
  - Linux：clang、zlib，以及目标发行版上 .NET NativeAOT 所需的本机依赖。除非将全球化设置为固定区域性（invariant），否则需要 ICU；AirGlance 尚未改为固定区域性全球化。
- AVIF 解码器：
  - Windows x64：CMake 和 Ninja（来自 Visual Studio）、Python 3 + Meson 1.7.2（`python -m pip install --user meson==1.7.2`）以及 NASM。`native/avif/build.ps1` 会下载并校验 libavif 1.4.2 / dav1d 1.5.5，构建 `airglance_avif.dll`；`pack.ps1` 自动调用。
  - macOS/Linux：CMake、Ninja、Meson、C 编译器，x64 还需要 NASM。`native/avif/build.sh` 构建 `libairglance_avif.dylib` 或 `libairglance_avif.so`；`pack.sh` 自动调用。
  - Windows arm64：LLVM（clang、lld-link、llvm-ar）和支持 ARM64 库的 Windows SDK。`native/avif/build-win-arm64.ps1` 在 x64 主机上交叉构建 `airglance_avif.dll`；不带 x86 SIMD，未记录硬件运行结果。
- `Directory.Packages.props` 中的包版本。SkiaSharp 4.150.1 和 Avalonia 11.3.22 共用一套本机资产依赖图；不要把其中任何一个替换为 3.x 的 SkiaSharp 包。
- 用于 Store 打包：带有 `MakeAppx.exe`、`signtool.exe` 和 Windows App Certification Kit 的 Windows 10/11 SDK。
- 用于手动冒烟测试的显示器。构建和打包步骤是无头的，但启动应用不是。Linux 冒烟测试需要 X11/Wayland 或 Xvfb。
- 代码签名身份不在仓库中。没有签入任何 `.pfx`、Developer ID 身份、公证凭据或 Store 证书。

NativeAOT 无法在操作系统族之间交叉编译。`pack.sh` 在非 Darwin 主机上拒绝打包 macOS 包，在非 Linux 主机上拒绝打包 Linux 包。发布工作流使用原生的 macOS 和 Linux 运行器。当前 Windows 作业在 `windows-latest` 上同时构建 `win-x64` 和 `win-arm64`；没有记录到 Windows arm64 硬件上的冒烟测试。

## 打包脚本

| 文件 | 用途 |
| --- | --- |
| `packaging/scripts/pack.ps1` | 发布 AOT Windows 构建，移除符号文件，创建扁平 zip，可选地构建未签名 MSIX |
| `packaging/scripts/pack.sh` | 发布 AOT macOS 或 Linux 构建并创建对应平台的归档 |
| `packaging/msix/Build-Msix.ps1` | 暂存 Windows 发布目录，并用 `MakeAppx.exe` 打包未签名 MSIX |
| `packaging/msix/AppxManifest.xml` | 包标识、完全信任应用、徽标和图像文件关联 |
| `packaging/macos/Info.plist` | macOS 应用包元数据 |
| `packaging/linux/airglance.desktop` | 包含在 Linux tar.gz 中的桌面入口文件 |
| `packaging/linux/icons` | 包含在 Linux tar.gz 中的 PNG 图标 |

Windows x64 或 arm64：

```powershell
./packaging/scripts/pack.ps1 -Version 0.7.0 -Runtime win-x64 -Aot
./packaging/scripts/pack.ps1 -Version 0.7.0 -Runtime win-x64 -Aot -Msix
```

macOS 或 Linux，在匹配的主机上：

```sh
./packaging/scripts/pack.sh 0.7.0 osx-arm64
./packaging/scripts/pack.sh 0.7.0 osx-x64
./packaging/scripts/pack.sh 0.7.0 linux-x64
./packaging/scripts/pack.sh 0.7.0 linux-arm64
```

这些脚本会调用 `dotnet publish -c Release -r <rid> -p:PublishAot=true -p:StripSymbols=true -p:Version=<version>`。`pack.ps1` 还会移除所有残留的 PDB，并强制执行 30 MB 的 zip 大小上限。`pack.sh` 会在打包前移除 PDB、DBG 和 MDB 文件。

### Windows zip 内容

`pack.ps1` 直接压缩扁平的发布产物，而不是外层包装文件夹：

```text
AirGlance.exe
airglance_avif.dll
av_libglesv2.dll
libHarfBuzzSharp.dll
libSkiaSharp.dll
LICENSE
THIRD-PARTY-NOTICES.md
```

`win-x64` 和 `win-arm64` 都包含 `airglance_avif.dll`；arm64 解码器交叉构建，不带 x86 SIMD。macOS 应用包在 `Contents/MacOS` 中包含 `libairglance_avif.dylib`；Linux tar 包在根目录包含 `libairglance_avif.so`。

归档根目录下是 `AirGlance.exe`。它不包含 `README.md`，但 MIT 许可证和第三方声明随二进制一同提供。
macOS 应用包将它们放在 `Contents/Resources` 中；
Linux tar.gz 将它们放在归档根目录。

### macOS 应用包

`pack.sh` 基于发布输出、`packaging/macos/Info.plist` 和 `airglance.icns` 构建 `AirGlance.app`：

```text
AirGlance.app/Contents/
  Info.plist
  MacOS/                 (publish output, including AirGlance, libairglance_avif.dylib and native libraries)
  Resources/airglance.icns
```

当前 `Info.plist` 声明：

- `CFBundleExecutable` = `AirGlance`；
- `CFBundleIdentifier` = `dev.airworks.AirGlance`；
- `CFBundleName` 和 `CFBundleDisplayName` = `AirGlance`；
- `CFBundleShortVersionString` = `0.7.0`，由 `pack.sh` 根据版本参数写入；
- `CFBundleVersion` = `1`，不按每次构建写入；
- `CFBundleIconFile` = `airglance.icns`；
- `LSMinimumSystemVersion` = `12.0`；
- JPEG、PNG、WebP、BMP 和 GIF 的文档类型，`LSHandlerRank` = `Alternate`。

归档使用 `ditto -c -k --keepParent` 创建，因此 `AirGlance.app` 位于 zip 根目录，可执行权限位得以保留。脚本不调用 `codesign`。发布的 macOS 应用未签名、未做 ad-hoc 签名、未启用强化运行时，也未公证。

### Linux tar.gz 内容

`pack.sh` 创建包含发布产物、`airglance.desktop` 和图标集的扁平 tar.gz：

```text
AirGlance
libairglance_avif.so
airglance.desktop
icons/airglance-16.png
...
icons/airglance-512.png
```

归档没有外层包装目录。桌面入口使用 `Exec=AirGlance %f`，因此二进制必须位于 `PATH` 中，或者必须在解压后编辑 `Exec` 行。

### MSIX 构建

`Build-Msix.ps1` 暂存 Windows 发布目录，复制清单和 `Assets/`，移除符号文件，重写 `Identity/@Version` 和 `Identity/@ProcessorArchitecture`，然后调用 `MakeAppx.exe pack`。它不会创建或安装证书。

```powershell
./packaging/msix/Build-Msix.ps1 `
  -Version 1.0.2 `
  -Architecture x64 `
  -PublishDir publish/win-x64 `
  -OutputDir artifacts/msix
```

输出为 `AirGlance_1.0.2.0_x64.msix`，若指定 `-Architecture arm64` 则为 `AirGlance_1.0.2.0_arm64.msix`。arm64 包可在本地构建，但不在本次 Store 提交范围内，因此本次上传仅 x64。`store.yml` 只按 x64 运行此脚本。仓库中的包未签名；Store 会对交付的包签名。

## 冒烟测试

打包并不等于启动测试。发布前请从每个已解压的产物运行基准测试：

```powershell
./AirGlance.exe --bench=startup tests/Assets/orientation-1.jpg
```

```sh
./AirGlance --bench=startup tests/Assets/orientation-1.jpg
```

成功运行会打印 `first_window_ms`、`first_frame_ms`、`decode_ms`、`image_mp` 和 `peak_ws_mb`，然后以退出码 0 结束。失败时会打印 `error=...` 并以退出码 1 结束。`--version` 会打开一个窗口而不是向控制台输出，因此它不是无头冒烟测试。

发布前还应在每个操作系统上打开文件夹、用方向键导航；按 `D` 检查双画布（同文件夹下一张、`Tab`、`X`、分隔条、固定 50/50 胶片条、右键画布菜单的“打开文件夹”、信息、复制和 `Esc` 顺序）；切换全屏、切换语言并保存副本。这些检查是手动进行的。CI 工作流会运行测试套件并打包二进制，但不会启动打包后的应用，也不会运行 `--bench`；仓库中没有记录任何打包产物的冒烟测试结果。

## Windows 0.7.0：未签名便携版本

Windows zip 未做 Authenticode 签名。对于新的未签名二进制，Windows 会显示“Windows 已保护你的电脑”（Windows protected your PC）/“未知发布者”（Unknown publisher）。

在核验下载哈希之后，要继续运行：

1. 对照发布资产 `SHA256SUMS.txt` 校验文件。
2. 解压 zip。`AirGlance.exe` 位于归档根目录。
3. 如果出现 SmartScreen，请选择 **更多信息**（More info），然后选择 **仍要运行**（Run anyway）。
4. 如果解压后的文件仍带有 Mark-of-the-Web，可用 `Unblock-File .\AirGlance.exe` 清除它。

不要为了运行 AirGlance 而禁用 SmartScreen 或 Windows Defender。企业策略可能会阻止未签名二进制；这不在应用的控制范围内。

没有 Windows 安装程序。zip 不会注册文件关联、添加卸载条目或创建开始菜单快捷方式。MSIX 是 Store 包，不是为未签名公开发布提供的变通方案。

真正的 Authenticode 证书和时间戳可以消除未知发布者警告。0.7.0 未使用任何证书。

## macOS 0.7.0：未签名版本

macOS 发布归档包含未签名的 `AirGlance.app`。它未做 ad-hoc 签名、未启用强化运行时、也未公证。Gatekeeper 可能显示“无法打开 AirGlance，因为无法验证开发者”（AirGlance cannot be opened because the developer cannot be verified）。

打开方法：

1. 对照 `SHA256SUMS.txt` 校验归档。
2. 解压归档。
3. 右键点击 `AirGlance.app`，选择 **打开**（Open），然后确认 **打开**。
4. 如果 macOS 仍然阻止打开，请打开 **系统设置 → 隐私与安全性**（System Settings → Privacy & Security），并对 AirGlance 使用 **仍要打开**（Open Anyway）。

不要全局禁用 Gatekeeper。在 Apple 芯片上，x64 构建只有在安装 Rosetta 后才能运行。arm64 构建是原生的。

ad-hoc 签名在本地测试中是可选操作，但它不属于 `pack.sh` 或发布工作流的一部分：

```sh
codesign --force --deep --sign - AirGlance.app
codesign --verify --deep --strict --verbose=2 AirGlance.app
```

ad-hoc 签名无法满足 Gatekeeper 或公证要求。0.7.0 中没有 Developer ID 证书、强化运行时、公证或装订票据（stapled ticket）。macOS x64 和 arm64 是分开的构建；不生成通用二进制。

## Linux 0.7.0：tar.gz 安装

解压归档并运行二进制：

```sh
tar -xzf AirGlance-0.7.0-linux-x64.tar.gz
./AirGlance
```

按用户安装，与 `packaging/linux/README.md` 中的步骤一致：

```sh
mkdir -p "$HOME/.local/bin" "$HOME/.local/share/applications"
install -m 0755 AirGlance "$HOME/.local/bin/AirGlance"
install -m 0644 airglance.desktop "$HOME/.local/share/applications/airglance.desktop"

for icon in icons/airglance-*.png; do
    size="${icon##*/airglance-}"
    size="${size%.png}"
    install -Dm0644 "$icon" \
        "$HOME/.local/share/icons/hicolor/${size}x${size}/apps/airglance.png"
done
```

确保 `$HOME/.local/bin` 位于 `PATH` 中；桌面入口使用 `Exec=AirGlance %f`。如有需要，刷新桌面和图标缓存：

```sh
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
gtk-update-icon-cache -f -t "$HOME/.local/share/icons/hicolor" 2>/dev/null || true
```

若要全系统安装，请使用 `/usr/local/bin/AirGlance`、`/usr/share/applications/airglance.desktop` 和 `/usr/share/icons/hicolor`。桌面入口只是声明支持的 MIME 类型，并不会让 AirGlance 成为默认查看器。0.7.0 没有 `.deb`、`.rpm`、Flatpak 或 AppImage。Linux 产物在 Ubuntu 22.04/24.04 运行器上针对相应架构构建；musl/Alpine 未测试。

## 用于 Microsoft Store 的 MSIX

MSIX 包仅用于 Microsoft Store 提交：

- `AirGlance_1.0.2.0_x64.msix`

Microsoft Store 包版本与 GitHub tag 独立：上一个 Store 包为 `1.0.1.0`（应用 0.6.0），0.7.0 提交使用 `1.0.2.0`。本次 Store 提交仅提供 x64。现在 AVIF 解码器已支持 Windows ARM64，arm64 MSIX 可以在本地构建，但不在本次 Store 提交范围内。它们不是公开发布的资产。`store.yml` 按需构建 x64 包，并将其作为工作流产物上传。

### 包标识和发布者

`packaging/msix/AppxManifest.xml` 已签入。其标识为：

```xml
<Identity
  Name="RyzenWang.AirGlance"
  Publisher="CN=D87D6846-5A2B-4D82-BDB7-8E739EB3F35B"
  Version="1.0.2.0"
  ProcessorArchitecture="x64" />
```

`Publisher` 值为 `CN=D87D6846-5A2B-4D82-BDB7-8E739EB3F35B`，与 Partner Center 中准备的发布者身份一致。包发布者和 Store 账户必须一致。

`Name` 为 `RyzenWang.AirGlance`；`DisplayName` 为 `AirGlance`，`PublisherDisplayName` 为 `RyzenWang`。

清单还声明了：

- `TargetDeviceFamily` 为 `Windows.Desktop`，`MinVersion="10.0.19041.0"`，`MaxVersionTested="10.0.26100.0"`；
- `Application`，其 `Executable="AirGlance.exe"`，`EntryPoint="Windows.FullTrustApplication"`；
- `runFullTrust` 受限功能；
- 对 `.jpg`、`.jpeg`、`.jfif`、`.png`、`.webp`、`.avif`、`.bmp`、`.gif`、`.ico`、`.tif`、`.tiff` 和 `.svg` 的文件关联；
- 来自 `packaging/msix/Assets` 的包、磁贴和 Store 徽标。

清单不请求网络功能。它也没有遥测组件。

清单中将最低 Windows 版本设为 Windows 10 版本 2004（内部版本 19041）。首个提交的目标是 Windows 10/11 x64。

### 签名

上传到 Microsoft Store 的包不需要开发者签名。Store 认证通过后会用 Microsoft 的证书重新签名。本地开发证书只用于在测试机上安装和验证。

Microsoft 的 [Store readiness checklist](https://learn.microsoft.com/en-us/windows/apps/publish/store-readiness-checklist) 明确说明 Store 分发不需要自己的代码签名证书。本地 WACK/安装测试可使用 subject 精确为 `CN=D87D6846-5A2B-4D82-BDB7-8E739EB3F35B` 的自签名证书，或在 Windows 11 使用 `Add-AppxPackage -AllowUnsigned`。自签名包不要对外分发。

### WACK

`packaging/msix/README.md` 和 `.github/workflows/store.yml` 使用 WACK 的命令行形式。请在干净的 Windows 10/11 虚拟机上以管理员身份运行：

1. 在测试虚拟机上用与 manifest Publisher 完全一致的自签名证书安装 MSIX，或使用 Windows 11 的 `Add-AppxPackage -AllowUnsigned`。
2. 启动 AirGlance 一次，然后关闭它。
3. 重置并运行 WACK：

```powershell
$appcert = Get-ChildItem `
  'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe' `
  -ErrorAction Stop

& $appcert.FullName reset

& $appcert.FullName test `
  -appxpackagepath (Resolve-Path '.\AirGlance_1.0.2.0_x64.msix').Path `
  -reportoutputpath (Join-Path (Get-Location) 'wack-x64.xml')
```

4. 查看 `wack-x64.xml` 和 WACK 日志。修复所有会阻止 Store 认证的失败或警告。


`store.yml` 会对未签名包运行 WACK 作为信息性预检，并上传报告。对于未签名包，与签名相关的失败是预期之中的。有意义的 WACK 通过需要受信任的已签名包，而 0.7.0 没有记录到任何已签名包或通过的 WACK 报告。

## Store 上架信息

上架地址：https://apps.microsoft.com/detail/9p30h2pjg0f6。后续更新通过 Partner Center 以 MSIX 桌面包形式提交。

| 上架字段 | 值 |
| --- | --- |
| 产品名称 | AirGlance |
| 发布者 | RyzenWang |
| 类别 | 照片与视频 (Photos & video) |
| 价格 | 免费 |
| 试用 / 应用内购买 / 广告 | 无 |
| 语言 | 英语、简体中文（`zh-CN`） |
| 网络使用 | 无 |
| 遥测 | 无 |

上架信息需要：

- 一个公开的隐私政策 URL。即使应用不收集任何内容，Store 也要求提供政策。请说明设置和缩略图保留在设备上，且不收集或传输任何个人数据；
- `packaging/msix/screenshots/1.0.2.0/` 中的当前 Store 截图：7 张 1440 × 900（16:10）PNG，基于 README 场景和 `docs/design/assets/sample-photo.jpg`；见 `packaging/msix/STORE-UPDATE-0.7.0.md`；
- 已位于 `packaging/msix/Assets` 下的包、磁贴和 Store 徽标；
- 系统要求：Windows 10/11 x64。内存需求取决于图像；在 100% 缩放下查看 100 MP 图像需要大尺寸的全分辨率解码；
- 年龄分级问卷。AirGlance 没有用户生成内容、账户、广告或网络功能；
- 认证说明：本地图像查看器、完全信任桌面包、无网络调用、无遥测、支持的格式和已知限制。

类别在 Partner Center 中选择。中文上架信息请使用 **照片与视频**；英文 Store 界面显示对应的 Photos & video 类别。

Store 已上架；截图和隐私政策可在 Partner Center 继续修改。商店版本可能落后于 GitHub Release。

## 无网络、无遥测

AirGlance 不包含任何 HTTP 客户端、套接字、分析 SDK、崩溃上传或更新检查。设置和缩略图数据都是本地文件。唯一的外部跳转是显式点击 GitHub 项目链接或第三方声明链接，这会通过 shell 打开默认浏览器。那是用户操作，不是遥测。

这一声明已对照源码核查：`src/` 中没有 `HttpClient`、`WebRequest`、`Socket` 或遥测调用。添加功能时请保持这一点。即使不收集任何数据，仍然需要有一份 Store 隐私政策。

## SHA256SUMS.txt

发布工作流会写入名为 `SHA256SUMS.txt` 的校验和文件。它以标准 `sha256sum` 格式包含全部六个公开归档，并按文件名排序。它有意排除 MSIX 包。

```text
<64 lowercase hex characters>  AirGlance-0.7.0-linux-arm64.tar.gz
<64 lowercase hex characters>  AirGlance-0.7.0-linux-x64.tar.gz
<64 lowercase hex characters>  AirGlance-0.7.0-osx-arm64.zip
<64 lowercase hex characters>  AirGlance-0.7.0-osx-x64.zip
<64 lowercase hex characters>  AirGlance-0.7.0-win-arm64.zip
<64 lowercase hex characters>  AirGlance-0.7.0-win-x64.zip
```

已签入的工作流使用 `.txt` 后缀；它就是该发布所用的 SHA256SUMS 文件。当前工作流不会生成不带后缀的约定名称 `SHA256SUMS`。

`SHA256SUMS.txt` 由 `.github/workflows/release.yml` 在下载六个归档后生成。打包脚本会打印各个哈希，但本身不写入校验和文件。

验证下载内容：

```powershell
Get-FileHash .\AirGlance-0.7.0-win-x64.zip -Algorithm SHA256
```

```sh
shasum -a 256 AirGlance-0.7.0-osx-arm64.zip
sha256sum -c SHA256SUMS.txt
```

如果 MSIX 在 Partner Center 之外被分享，请单独计算其哈希，并说明由哪个证书签名。公开发布不包含 MSIX 文件。

## 发布工作流概览

已签入三个工作流：

| 工作流 | 触发方式 | 作用 |
| --- | --- | --- |
| `.github/workflows/build.yml` | 推送或拉取请求 | 在 Windows、Linux 和 macOS 上还原、构建和测试 |
| `.github/workflows/release.yml` | 打 `v*` 标签 | 测试、打包六个归档、创建 `SHA256SUMS.txt`、发布 GitHub release |
| `.github/workflows/store.yml` | 手动触发 | 构建未签名的 x64 和 arm64 MSIX 包、运行信息性 WACK、上传产物 |

`release.yml`：

1. 在 Ubuntu 上运行测试套件。
2. 在 `windows-latest` 上打包 `win-x64` 和 `win-arm64`，在 `macos-15` 上打包 `osx-arm64`，在 `macos-15-intel` 上打包 `osx-x64`，在 `ubuntu-22.04` 上打包 `linux-x64`，在 `ubuntu-24.04-arm` 上打包 `linux-arm64`。
3. 下载恰好六个归档，并拒绝发布集合中的任何 MSIX。
4. 对归档名称排序并写入 `SHA256SUMS.txt`。
5. 根据 `CHANGELOG.md` 为标签版本生成发布说明，若不可用则回退到标签消息。
6. 使用六个归档和 `SHA256SUMS.txt` 创建或更新 GitHub release `v0.7.0`。

`store.yml` 不向 Partner Center 提交任何内容。它构建 MSIX 文件，在存在该工具包时运行 WACK，并上传包和报告以供人工审核。在发布者被替换且包完成签名后，向 Partner Center 提交仍然是手动操作。

## 尚未完成的事项

- Windows 便携归档、macOS 应用包和 MSIX 包均未签名。没有 Authenticode 证书、Developer ID 身份、公证、强化运行时、MSIX 发布者证书或时间戳。
- macOS 发布归档也没有 ad-hoc 签名。`pack.sh` 不调用 `codesign`。可选的 ad-hoc 签名命令仅在文档中用于本地测试。
- MSIX 发布者已是 Partner Center 身份 `CN=D87D6846-5A2B-4D82-BDB7-8E739EB3F35B`，包标识为 `RyzenWang.AirGlance`。上架地址为 https://apps.microsoft.com/detail/9p30h2pjg0f6。
- Microsoft Store 已上架。本仓库未记录 WACK 报告或本地安装签名 MSIX 的结果；以 Store 认证为准。
- Windows arm64 在 x64 的 `windows-latest` 运行器上构建。没有记录到 Windows arm64 硬件上的启动测试。Linux arm64 和 macOS x64 在原生运行器上构建，但 CI 中没有记录任何打包产物的冒烟测试。
- 工作流中不包含打包产物的启动测试或 `--bench` 运行。发布工作流测试的是源码树，而不是解压后的 zip、tar.gz 或应用包。
- Windows zip、macOS 应用和 Linux tar.gz 不包含 `README.md`。`LICENSE` 和 `THIRD-PARTY-NOTICES.md` 会随二进制一同提供：只要仓库根目录存在这两个文件，`pack.ps1` 和 `pack.sh` 就会复制它们。
- macOS 的 `CFBundleVersion` 固定为 `1`；`pack.sh` 只写入 `CFBundleShortVersionString`。没有单调递增的构建号。
- 没有 Windows 安装程序，没有 tar.gz 之外的 Linux 包格式，也没有自动更新、崩溃报告或遥测。遥测的缺失是有意为之。
- 没有托盘图标。单实例转发会把第二次启动的路径通过本机命名管道交给已运行的窗口；没有窗口激活，所以已有窗口需要用户自己切过去。
- 发布校验和文件名为 `SHA256SUMS.txt`，而不是 `SHA256SUMS`。其内容是标准 `sha256sum` 输出。
- Store 已上架；截图和隐私政策可在 Partner Center 继续修改。清单中的 `MinVersion`（Windows 10 内部版本 19041）和 macOS 的 `LSMinimumSystemVersion`（12.0）都是声明，而非经过测量的兼容性矩阵。
- 没有通用 macOS 二进制。macOS x64 和 arm64 是分开的下载项。
- 没有任何工作流包含签名步骤，仓库中也没有存储任何签名证书。

## 相关文档

- [架构](architecture.zh-CN.md)
- [格式支持矩阵](formats.zh-CN.md)
- [变更日志](../CHANGELOG.md)
- [第三方声明](../THIRD-PARTY-NOTICES.md)
- [Linux 打包说明](../packaging/linux/README.md)
- [MSIX 打包说明](../packaging/msix/README.md)
- [README](../README.zh-CN.md)
