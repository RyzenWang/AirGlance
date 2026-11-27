# AirGlance Microsoft Store 更新包说明：0.7.2

## 包信息

| 项目 | 值 |
| --- | --- |
| 应用版本 | `0.7.2`（`ProductVersion 0.7.2+d0590e5`） |
| Store 包版本 | `1.0.4.0`（上次 Store 包为 `1.0.3.0`，应用 0.7.1） |
| 架构 | `x64` |
| 文件 | `artifacts/msix-store/AirGlance_1.0.4.0_x64.msix` |
| 大小 | 21.97 MiB（23,040,908 字节） |
| SHA-256 | `DF6B5D8268BF9B637701FB4787EF379557AC2AC0893C523AC5AD71CAE9C54EB2` |
| 签名 | **未签名**；Microsoft Store 认证通过后由 Store 重新签名 |

包内确认包含（14 个文件）：

- `AirGlance.exe` — 0.7.2 NativeAOT，33,704,448 字节
- `airglance_avif.dll` — libavif + dav1d 的 Windows x64 解码器，2,129,920 字节
- `libSkiaSharp.dll` — **4.157.0**，13,187,896 字节（0.7.1 的 4.150.1 是 12,254,048 字节）
- `libHarfBuzzSharp.dll`、`av_libglesv2.dll`
- `Assets/` 四个 logo、`LICENSE`、`THIRD-PARTY-NOTICES.md`
- `AppxManifest.xml` 版本 `1.0.4.0`、架构 `x64`、publisher identity 未变（`CN=D87D6846-5A2B-4D82-BDB7-8E739EB3F35B`），`.avif` 文件关联仍在

关于 `d0590e5`：打包时的 HEAD 比 tag `v0.7.2` 晚两个提交，但 `git diff --name-only v0.7.2..d0590e5` 只有
`CHANGELOG.md`、`README.md`、`README.zh-CN.md`、`docs/optimization-backlog.md` 四个文档文件——
**代码与 tag 完全一致**，差异只是 AOT 二进制里嵌入的提交号。

## 距离上次 Store 版本更新了什么

上次：`1.0.3.0` = 应用 0.7.1；本次：`1.0.4.0` = 应用 0.7.2。查看器、编辑、裁剪、导出副本、AVIF、
大 TIFF 与双画布全部保持不变，**没有界面改动**。

### 1. 大图 100% 缩放的内存降到约四分之一

瓦片写入器改为按"一个瓦片行"（512 行）流式解码、直接写入临时瓦片文件，不再先建 400 MB 的全分辨率光栅。
实测同一脚本、同一夹具：10000 × 10000 JPEG 的工作集峰值 **478 MB → 128 MB（−73%）**，稳定值同为 128 MB。

限制说清楚：只对 **JPEG** 生效。PNG、WebP、ICO 在 SkiaSharp 里 `StartScanlineDecode` 返回
`Unimplemented`，带 EXIF 旋转的文件和带编辑的文件仍走原光栅路径；48 MP PNG 仍可能接近 300 MB。
临时瓦片文件仍是 400 MB（瓦片仍以原始 BGRA 存储）。

### 2. 旋转过的 TIFF 尺寸显示正确

带方向标签的 TIFF 以前用存储尺寸填状态栏：80 × 40 的文件旋转显示成 40 × 80，状态栏却写 80 × 40，
缩放比例还是 0.8。现在两条 TIFF 路径都按"方向应用后的尺寸"报告，比例 1.0。

### 3. 引擎升级

SkiaSharp 4.150.1 → **4.157.0**（原生库 +0.89 MiB，便携包 +0.39 MiB），Avalonia 11.3.22 → 11.3.23，
Svg.Skia 5.2.3 → 5.2.4，Microsoft.NET.Test.Sdk 17.14.1 → 17.14.2。升级前对每个格式做过端到端冒烟，
逐行解码能力矩阵未变。

### 4. 内部变化（无用户可见影响）

两条 TIFF 解码路径约 1030 行重复合并进 `TiffCodec`；`MainWindow`（2886 → 407 行）、`DualCanvasView`
（2146 → 616）、`ImageDecoder`（1557 → 579）拆成多个文件；瓦片写入侧抽成 `TileFileWriter`；
新增 26 个测试（命令行解析、本地化目录、SkiaSharp 逐行解码能力）。

## 提交清单

- [ ] Partner Center → 提交更新 → 上传 `AirGlance_1.0.4.0_x64.msix`
- [ ] 确认包版本显示 `1.0.4.0`、架构 `x64`、包标识 `RyzenWang.AirGlance`
- [ ] 商店列表沿用 0.7.1 的截图与描述；本次没有界面改版
- [ ] 提交认证

## 验证记录

把 MSIX **完整解包**到临时目录，用包内 `AirGlance.exe` 逐个 `--bench=decode`，11 个夹具退出码全 0：

| 夹具 | decode | 夹具 | decode |
| --- | --- | --- | --- |
| `sample.avif` | 56.7 ms | `animated.gif` | 1.3 ms |
| `clap.avif` | 64.9 ms | `sample.bmp` | 1.2 ms |
| `animated.webp` | 3.6 ms | `sample.ico` | 9.1 ms |
| `orientation-6.png` | 2.0 ms | `bigtiff-lzw.tif` | 4.3 ms |
| `vector.svg` | 10.3 ms | `rgb-lzw.tif` | 3.9 ms |
| `exif-full.jpg` | 2.1 ms | | |

其它检查：

- 包内 `AirGlance.exe` 报告 `0.7.2+d0590e5`，`AppxManifest` 为 `1.0.4.0` / `x64`
- 包里**没有** `AppxSignature.p7x`，符合"未签名、由 Store 重签"的流程
- 只解出 exe 单独运行不是受支持的布局：原生 payload（`airglance_avif.dll`、`libSkiaSharp.dll` 等）是独立文件
- 本地没有 Windows App Certification Kit，WACK 结论以 `store.yml` 的 CI 报告为准
