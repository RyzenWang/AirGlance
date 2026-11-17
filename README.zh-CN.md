# AirGlance

[English](README.md) · **简体中文**

**一个文件夹，一眼看完。**

AirGlance 是一个适用于 Windows、macOS 和 Linux 的桌面看图工具。它一次打开一个文件夹、显示一张图片，不用标签页、图库或工作区就能浏览其余图片。当前版本是 0.1.2，0.1.0 是首个公开版本。它刻意保持小巧。

- [下载](#安装) · [键盘](#键盘) · [性能](#性能) · [已知限制](#已知限制) · [从源码构建](#从源码构建)

## 功能

- 可以从命令行、拖放或打开对话框启动。窗口先出现，图片解码完成后立即显示。
- 按自然顺序浏览一个文件夹（`img2` 排在 `img10` 前），带 160 px 胶片条，上一张 / 下一张在两端循环。
- 适应窗口、适应宽度和 100% 三种视图；滚轮以指针为中心缩放，Shift + 滚轮横向平移，拖动平移，双击在 100% 和适应窗口之间切换。
- 在显示第一个像素前应用 EXIF 方向，并把内嵌 ICC 配置文件（Display P3、Adobe RGB）转换为 sRGB 显示。
- 旋转和翻转只影响显示。可以另存为 JPEG、PNG 或 WebP，也可以复制图片；原文件永远不会被写入。
- 全屏、3 / 5 / 10 秒幻灯片，以及包含文件、图片、ICC 和 EXIF 字段的信息面板。
- 中英文运行时切换。浅色、深色和跟随系统主题；尊重系统高对比度设置。
- 设置和缩略图只保存在本机。没有账号、没有遥测，也没有网络代码。

## 安装

0.1.2 尚未上产品站。请从 [v0.1.2 GitHub Release](https://github.com/RyzenWang/AirGlance/releases/tag/v0.1.2) 下载。

### Windows

1. 下载 `AirGlance-0.1.2-win-x64.zip`（或 `win-arm64`）。
2. 解压到任意位置，运行 `AirGlance.exe`。

0.1 便携版没有代码签名，首次运行会出现 SmartScreen 警告。选择 **更多信息 → 仍要运行**。可执行文件是 NativeAOT 构建，不需要安装 .NET 运行时。

### macOS

1. 下载 `AirGlance-0.1.2-osx-arm64.zip` 或 `-osx-x64.zip`。
2. 解压 `AirGlance.app`，第一次启动时右键点击应用并选择 **打开**。在应用完成公证之前，Gatekeeper 会一直阻止普通双击；0.1 尚未公证。

### Linux

```bash
tar -xzf AirGlance-0.1.2-linux-x64.tar.gz
./AirGlance
```

压缩包还包含 `airglance.desktop` 和 PNG 图标。把它们复制到 `~/.local/share/applications/` 和 `~/.local/share/icons/hicolor/` 可以获得菜单项。便携版不会注册默认打开方式。

### 平台状态

发布包含六个压缩包，但实际运行验证的范围更小：

| 平台 | 压缩包 | 实际运行过 NativeAOT 二进制 |
| --- | --- | --- |
| Windows x64 | `win-x64.zip` | **已验证**（参考机器，0.1.2） |
| Linux x64 | `linux-x64.tar.gz` | **已验证**（维护者） |
| Windows arm64 | `win-arm64.zip` | 未验证 |
| macOS arm64 | `osx-arm64.zip` | 未验证 |
| macOS x64 | `osx-x64.zip` | 未验证 |
| Linux arm64 | `linux-arm64.tar.gz` | 未验证 |

“已验证”是指实际启动过 AOT 二进制并显示过图片。Linux x64 由维护者在 0.1.1 上验证。其他压缩包由 CI 构建，但尚未在对应平台上运行。剪贴板图片复制已在 Windows x64 上用 PNG 和 JPEG 验证；复制路径和 100% 以上 Windows 显示缩放仍未验证。详见 [shipping](docs/shipping.md)。

## 键盘

| 按键 | 操作 |
| --- | --- |
| `Ctrl+O` / `Ctrl+Shift+O` | 打开图片 / 文件夹 |
| `←` `→` | 上一张 / 下一张；图片比窗口宽时平移 |
| `PgUp` `PgDn` | 上一张 / 下一张（始终） |
| `Space` / `Backspace` | 下一张 / 上一张 |
| `+` `-` | 以指针为中心放大 / 缩小 |
| `0` `1` `2` | 适应窗口 / 100% / 适应宽度 |
| `R` / `Shift+R` | 顺时针 / 逆时针旋转 |
| `H` / `V` | 水平 / 垂直翻转 |
| `F11` / `Esc` | 全屏；退出全屏或关闭窗口 |
| `S` | 开始或停止幻灯片；任意键停止 |
| `I` / `C` | 信息面板 / 胶片条 |
| `Ctrl+C` / `Ctrl+Shift+C` | 复制图片 / 复制路径 |
| `Ctrl+Shift+S` | 另存副本 |
| `Ctrl+Shift+L` | 切换英文 / 中文 |
| `F1` | 完整快捷键列表 |

右键点击图片可以打开、复制、在文件夹中显示、用其他应用打开以及进入设置。

## 性能

在 Windows 10 Pro 22H2 (19045)、Intel Xeon E5-2697 v4、32 GB 内存、.NET 10.0.14、NativeAOT `win-x64` 上测量。每行十个全新进程，取 `tools/bench.ps1` 的中位数；完整方法和原始数据见 [docs/performance.md](docs/performance.md)。

| 指标 | 目标 | 0.1.2 |
| --- | --- | --- |
| 冷启动到首窗 | ≤ 500 ms | 188.8 ms |
| 24 MP JPEG 首帧 | ≤ 500 ms | 302.4 ms |
| 100 MP JPEG 首帧（适应窗口） | ≤ 2.0 s | 293.1 ms |
| 24 MP 适应窗口内存峰值 | ≤ 250 MB | 76.5 MB |
| Windows x64 便携 zip | ≤ 30 MB | 15.52 MB |

0.1.2 在参考机器上达到启动和首帧目标：窗口约 189 ms 出现，24 MP 图片约 302 ms 上屏。窗口外壳用 C# 构建而不是 XAML，移除了最大的一项首帧开销；解码与建窗重叠。首次冷启动时操作系统扫描可执行文件可能更慢，因此应以中位数对比预算。这些是实测数字，不是估算。原始运行数据见 [docs/performance.md](docs/performance.md)。

## 已知限制

这些是限制，不是承诺清单。

- 不支持 HEIC、AVIF、RAW 或 TIFF。SkiaSharp 无法解码这些格式，AirGlance 也不附带平台编解码器。
- GIF 只显示第一帧。没有动画，也没有视频。
- 没有图库、数据库、标签、评分、搜索、批量转换或重命名、滤镜、图层、文字、拼接、云同步或账号。
- 没有标签页。一个窗口对应一个文件夹。没有托盘图标，也没有单实例转发。
- 没有裁剪，也没有无损 JPEG 旋转。旋转和翻转只改变屏幕显示。
- 16 位 PNG 会显示为 16 位，但显示和另存时按 8 位处理。
- 100 MP 图片在 100% 时使用基于文件的分块缓存：10000 × 10000 JPEG 的实测工作集峰值约 **452 MB**，分块写完后稳定在约 **83 MB**。图片打开期间临时分块文件约 400 MB。48 MP PNG 仍可能峰值接近 300 MB，因为 PNG 预览解码无法由编解码器缩放；内存较小的机器查看超大 PNG 时建议使用适应窗口。
- 0.1 Windows 便携版未签名，macOS 版未公证。MSIX 包尚未发布。

## 从源码构建

需要 .NET 10 SDK。解决方案目标为 `net10.0`，并通过 `rollForward: latestFeature` 将 SDK 固定到 10.0.100。

```bash
git clone https://github.com/RyzenWang/AirGlance.git
cd AirGlance
dotnet restore AirGlance.sln
dotnet build AirGlance.sln -c Release
dotnet test AirGlance.sln -c Release
```

运行：

```bash
dotnet run --project src/AirGlance.App -- path/to/photo.jpg
```

为当前机器发布 NativeAOT：

```bash
dotnet publish src/AirGlance.App/AirGlance.App.csproj \
  -c Release -r win-x64 -p:PublishAot=true -p:StripSymbols=true
```

`packaging/scripts/pack.ps1` 和 `pack.sh` 封装了各平台打包。AOT 无法交叉编译，因此每个平台都在对应机器上构建。

测试覆盖 EXIF 方向 1–8、ICC 转换、缩放解码、自然排序、文件夹枚举、设置恢复、缩略图缓存、导出和文件命名安全，以及视图模型和真实窗口键盘流程。见 `tests/`。

## 命令行

```text
AirGlance [选项] [文件或文件夹]

  -h, --help            显示帮助
      --version         显示版本
      --bench=startup   测量首窗、首帧和内存后退出
      --bench=decode    测量适应窗口解码后退出
      --bench=scan      测量文件夹枚举和首批缩略图后退出
  --                    后续所有参数都按路径处理
```

文件夹会打开其中第一张受支持的图片。第一个可显示的路径生效，其他参数被忽略。`--bench=startup` 会向标准输出打印 `first_window_ms`、`first_frame_ms`、`decode_ms`、`image_mp` 和 `peak_ws_mb`。

## 隐私

AirGlance 不包含 `HttpClient`、socket、遥测、崩溃上传或更新检查。唯一的外发动作是你主动点击的链接：状态栏中的 GitHub 会打开浏览器。设置保存在用户设置目录，缩略图保存在用户缓存目录并在 30 天后清理。

## 许可证

MIT。见 [LICENSE](LICENSE) 和 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

更多细节：[架构](docs/architecture.md) · [格式矩阵](docs/formats.md) · [性能方法](docs/performance.md) · [发布流程](docs/shipping.md) · [变更日志](CHANGELOG.md)。
