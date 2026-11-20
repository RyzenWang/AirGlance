# 性能

[English](performance.md) · **简体中文**

本文件中的所有数字，都是在下面这台机器上使用所展示的命令测得的。如果某个数字没有出现在这里，说明它没有被测量；本文件中的任何内容都不是推算出来的。

> **版本说明（2026-11-20）：** 下面的计时数据是最近一次完整测量，基于 v0.3.0。v0.4.x 增加了双画布和缩放动画；v0.5.0 用托管 LibTiff 替换了 libvips TIFF 负载，并新增编辑和导出工具。启动性能此后尚未重新测量。v0.5.0 win-x64 包为 20.76 MiB。

## 机器与构建

| | |
| --- | --- |
| 操作系统 | Windows 10 Pro 22H2, 10.0.19045 |
| CPU | 2 × Intel Xeon E5-2697 v4，共 16 核 / 16 线程 |
| 内存 | 32 GB |
| .NET SDK | 10.0.403 |
| 运行时 | .NET 10.0.14 |
| 构建 | `dotnet publish src/AirGlance.App/AirGlance.App.csproj -c Release -r win-x64 -p:PublishAot=true -p:StripSymbols=true` |
| 渲染 | Avalonia Win32 软件合成器（0.1.1 中 Windows 上的默认选项） |
| 日期 | 2026-11-17 |

测试图像由 `python tools/make-test-images.py` 生成：

| 文件 | 大小 | 像素 |
| --- | --- | --- |
| `tools/testdata/photo-24mp.jpg` | 0.55 MB | 6000 × 4000 |
| `tools/testdata/photo-100mp.jpg` | 2.1 MB | 10000 × 10000 |

基准测试图像是渐变图，因此其 JPEG 文件比相同尺寸的照片更小。解码工作量主要由像素数量决定，而不是压缩后的大小。

## 方法

```powershell
powershell -ExecutionPolicy Bypass -File tools/bench.ps1 -Runs 10
powershell -ExecutionPolicy Bypass -File tools/bench.ps1 -Runs 5 `
  -Image tools/testdata/photo-100mp.jpg
```

每次运行都会启动一个全新进程：

```text
AirGlance.exe --bench=startup <image>
```

应用以操作系统报告的进程启动时间为起点，记录到以下时刻为止的经过时间：

- `first_window_ms`：窗口的 `Opened` 事件。
- `first_frame_ms`：首个包含图像像素的渲染通道。
- `decode_ms`：按屏幕尺寸进行的解码，包含重叠部分（第一张图像在工作线程上解码，此时窗口仍在构建中）。
- `peak_ws_mb`：进程退出时的峰值工作集。

`tools/bench.ps1` 解析这些行并报告中位数和 p95。应用没有控制台，因此脚本会把 stdout 重定向到临时文件。只有 NativeAOT 构建才具有可比性；普通的 Release 构建是 JIT 应用，速度慢得多。

## 结果

### 24 MP JPEG，适应窗口

对已发布的 `v0.3.0` Windows x64 产物运行 10 次，取中位数（括号内为 p95）。

| 指标 | 目标 | 实测 | 结果 |
| --- | --- | --- | --- |
| 首个窗口 | ≤ 500 ms | **222.0 ms**（497.1） | 通过 |
| 首帧 | ≤ 500 ms | **424.7 ms**（688.3） | 通过 |
| 解码 | — | **59.0 ms**（87.5） | — |
| 峰值工作集 | ≤ 250 MB | **84.5 MB**（86.8） | 通过 |

原始运行数据：

```text
Run first_window_ms first_frame_ms decode_ms image_mp peak_ws_mb wall_ms
  1 497.1           688.3           67.7       24.0     86.8       911.1
  2 223.2           369.2           58.1       24.0     83.8       443.9
  3 228.3           454.9           59.0       24.0     84.6       536.6
  4 220.8           454.4           87.5       24.0     84.5       526.5
  5 234.6           402.9           58.5       24.0     83.8       475.4
  6 231.4           469.2           58.9       24.0     84.5       541.2
  7 216.2           446.4           60.0       24.0     84.5       529.3
  8 213.6           394.4           78.7       24.0     84.7       467.8
  9 213.9           366.1           58.3       24.0     84.2       439.5
 10 216.0           389.7           58.7       24.0     84.5       464.2
```

### 100 MP JPEG，适应窗口

同一产物运行 5 次，取中位数。

| 指标 | 目标 | 实测 | 结果 |
| --- | --- | --- | --- |
| 首个窗口 | — | 214.9 ms | — |
| 首帧 | ≤ 2.0 s | **327.0 ms** | 通过 |
| 解码 | — | 143.3 ms | — |
| 峰值工作集 | — | 82.1 MB | — |

原始运行数据：327.0、308.8、333.8、310.3、329.0 ms。

### 100 MP JPEG 在 100% 缩放下

操作流程如下：

1. 启动 `v0.3.0` 的 `win-x64` AOT 产物，并让 `tools/testdata/photo-100mp.jpg` 单独放在一个文件夹中（否则胶片条对邻近大图的预览解码会主导样本）。
2. 等待适应窗口预览出现。
3. 按 `1`（实际大小），并每秒采样一次 `WorkingSet64` 和 `PrivateMemorySize64`。

| 时点 | 工作集 | 专用字节 |
| --- | --- | --- |
| 适应窗口预览 | 74.8 MB | 43.3 MB |
| 按下 `1` 后 1 秒 | 457.6 MB | 427.6 MB |
| 按下 `1` 后 2 秒 | 88.2 MB | 57.4 MB |
| 运行期间的峰值 | 457.6 MB | — |
| 100% 下的稳定状态 | 88.2 MB | 57.4 MB |

图像打开期间，临时瓦片文件约为 400 MB。400 MB 的完整解码是非托管内存，在 512 px 瓦片写入后立即释放；只有可见瓦片集（最多 32 块瓦片）留在内存中。窗口保持响应，显示 `10000 × 10000 · 100%`，且没有崩溃。

在 100% 缩放下旋转同一张 100 MP 图像，峰值达到 465.5 MB，最终稳定在 87.1 MB 工作集 / 58.6 MB 专用字节。旧的 0.1.1 路径峰值达到 842 MB 并维持在 836 MB，因为它同时保留了完整的 BGRA 数组和一个完整的 Avalonia 位图。

瓦片路径适用于 40 百万像素及以上的图像。48 MP 的 PNG 峰值仍接近 300 MB，因为 PNG 预览解码无法由编解码器缩放，所以其适应窗口预览会在软件缩放器运行之前短暂分配整幅图像。

### 包含 500 张图像的文件夹

`AirGlance.exe --bench=scan <folder>` 会枚举文件夹并解码前八张 160 px 缩略图（胶片条的第一屏）：

```text
scan_ms=2.4
thumbnail_ms=5.6
total_ms=7.9
files=500
peak_ws_mb=29.0
```

目标是枚举加上第一屏缩略图 ≤ 1 s。

### 便携包

| 产物 | 大小 | SHA256 |
| --- | --- | --- |
| `AirGlance-0.3.0-win-x64.zip` | **20.51 MB** | `b27ac3914ea69dc92e4dcbc3133c85f353ca67abeb90f77d32b80867f68b0d04` |
| `AirGlance-0.5.0-win-x64.zip` | **20.76 MiB** | `a659778644ca9a2927391c2b70b081606950f07d3c9ed180faefb8496907558c` |

0.5.0 的 zip 包含六个文件（四个运行时文件、LICENSE 和 THIRD-PARTY-NOTICES.md），解压后约为 50 MB。30 MB 的目标针对 zip 包，而不是解压后的目录。0.5.0 发布还包含 Linux 和 macOS 归档包以及 `SHA256SUMS.txt`；六个文件都小于 23 MB。

## 启动时间花在哪里

`AIRGLANCE_STARTUP_TRACE=1` 会把需要显式启用的标记打印到 stderr。下面是在软件合成器上的一次有代表性的 24 MP 运行：

```text
main=42.7
app-init=72.2
xaml-loaded=76.0
framework-init=113.2
services-ready=120.3
window-ctor-enter=138.3
window-ctor-ui=152.2
window-ctor-vm=152.9
window-ctor-chrome=154.6
window-ctor-exit=155.2
window-created=155.7
window-opened=192.3
image-initialized=200.7
```

现在窗口内容约耗时 14 ms（`window-ctor-ui`）。旧的 MainWindow XAML 构建约耗时 370 ms；0.1.1–0.3.0 用 C# 构建同样的窗口外壳，并将胶片条、信息面板和上下文菜单保留在 XAML 中，在首帧之后加载。24 MP 解码（约 57 ms）与窗口构建和图像初始化过程重叠。从 `image-initialized` 到首个绘制帧的剩余间隔约为 100 ms。

较早的构建使用 ANGLE/GPU 合成器。在这台机器上，这会把首个窗口推迟到约 565 ms、首帧推迟到约 790 ms，因此 0.1 在 Windows 上默认使用软件合成器。`AIRGLANCE_GPU_RENDER=1` 会切换回 Avalonia 默认的 GPU 路径。

## 目标状态

| 预算项 | 状态 |
| --- | --- |
| 冷启动首个窗口 ≤ 500 ms | 已达成 |
| 24 MP 首帧 ≤ 500 ms | 已达成 |
| 100 MP 首帧 ≤ 2.0 s | 已达成 |
| 24 MP 适应窗口内存 ≤ 250 MB | 已达成 |
| win-x64 zip ≤ 30 MB | 已达成 |
| 500 张图像列表 + 首批缩略图 ≤ 1 s | 已达成 |
| 预取时下一张图像 ≤ 60 ms | 未单独测量 |

0.3.0 的代码和测量脚本都在仓库中，因此这些数字可以被复现或改进。启动目标已经达成；大图方面剩余的工作是瓦片缓存策略，而不是首帧延迟。
