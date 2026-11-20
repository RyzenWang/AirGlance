# AVIF 重新支持可行性评估

> 状态：已实现 Windows x64 spike（2026-11-20）。其他 RID 尚未接入，`SupportedFormats`
> 会在缺少原生库时隐藏 `.avif`。
>
> 背景：0.5.0 移除 libvips 后 AVIF 不再支持，但 MSIX 仍注册 `.avif` 文件关联，
> 用户从“打开方式”选择 AirGlance 打开 AVIF 会得到“不支持的格式”。本次评估回答
> “要不要恢复、怎么恢复、代价多大”。

## 结论

**可行，建议重新支持，但不要回到 libvips。**

推荐方案：**自建 libavif + dav1d，静态链接成一个很小的原生 shim，按 RID 随包分发。**

- 版本：libavif **1.4.2** + dav1d **1.5.5**。两者都是 BSD-2-Clause；AV1 由 AOMedia
  推动，不属于 HEVC 那种专利池模式。POC 用的是 dav1d 1.5.3，正式接入应升级到 1.5.5。
- 体积：POC（关闭 x86 SIMD）`avif.dll` 206 KB + `dav1d.dll` 526 KB，压缩后约
  **381 KB**；生产构建静态链接并打开 SIMD，预计压缩后 **+0.8–1.5 MB**。当前 Windows
  zip 为 **20.76 MiB**，打包脚本的预算是 30 MB，余量充足；对比 libvips 的 **7.7 MB**
  小一个数量级。
- 工期：只做 Windows x64 约 **3–5 人日**；6 个发布 RID 全部支持约 **8–12 人日**
  （含三平台 CI 与验证）。
- 主要风险不在解码本身（POC 已解出 1440×900、alpha、10-bit），而在 **6 个 RID 的
  原生构建/CI** 和 **AVIF 方向、裁剪、ICC 元数据的正确接入**。

建议先做 1 天 spike（见“里程碑”），验证 5 个关键点后再全量投入。在此之前不要改
release 文档里的格式承诺。

## 当前为什么打不开 AVIF

1. `SupportedFormats.Extensions` 不含 `.avif`。文件夹扫描、打开对话框、命令行和
   拖放都会先把它挡掉。
2. 即使加入扩展名，`ImageDecoder` 对非 TIFF/SVG 统一走 `SKCodec`。0.5.0 使用的
   SkiaSharp 4.150.1 不含 AVIF 解码器，`SKCodec.Create` 返回 null，文件会被报成
   损坏。0.5.0 发布包里的 `libSkiaSharp.dll` 也找不到 `avif`/`dav1d` 字符串。
3. `tests/Assets/sample.avif` 还在，但没有 AVIF 测试。
4. `packaging/msix/AppxManifest.xml` 仍注册 `.avif` 文件关联，
   `packaging/msix/README.md` 也列出了它。实际打开会失败——**这是当前版本的用户
   可见缺陷，无论最终是否恢复 AVIF 都要处理。**
5. `.github/workflows/store.yml` 还留着 `x64-only until NetVips ships a Windows
   ARM64 native payload` 和 `complete AVIF/large-TIFF feature set` 的注释，且
   `APP_VERSION` 仍是 0.4.1，属于移除 libvips 时没清理干净的内容。

0.4.1 曾经通过 NetVips 支持 AVIF，发布包里带 `libvips-42.dll`（19 MB），说明
**NativeAOT + 原生 DLL 的加载路径在本项目已经验证过**。0.5.0 为了缩小 7.7 MB 包体
把它移除了；现在要恢复的只是“AVIF 解码器”，不是整套 libvips。

## 候选方案对比

| 方案 | 包体增量 | 平台 | 许可证 | 结论 |
| --- | --- | --- | --- | --- |
| **libavif + dav1d（自建 shim）** | POC 0.4 MB；生产预计 0.8–1.5 MB | 6 个 RID 均可 | BSD-2 | **推荐** |
| libvips（NetVips） | 约 7.7 MB | 6 个 RID 需要 native 包 | LGPL 系 | 不推荐，已移除 |
| Windows WIC + AV1 Video Extension | 0 | 仅 Windows，依赖可选系统编解码器 | 系统组件 | 只能做可选兜底，不能做基线 |
| 第三方 NuGet 封装（旧调研） | 4–30 MB 或只有绑定 | 取决于包 | 各异 | 需逐个复核；自建更可控 |
| 纯托管 AVIF 解码器 | — | — | — | 目前没有可用的成熟实现 |
| `avifdec.exe` 子进程 | 12 MB+ | 官方只发 exe | BSD-2 | 每次解码起进程/临时文件，不适合预览 |

补充两点：

- **WIC 不是可靠基线。** 本机（Windows 11 26200，装有 `Microsoft.AV1VideoExtension
  2.0.35.0`）用 WinRT `BitmapDecoder` 实测能解 `sample.avif`（1440×900、BGRA8），
  但干净系统、LTSC/Server、或未装扩展的机器不保证。把 AVIF 建立在可选 Store 组件上
  会变成“为什么别人能打开、我不能”的支持负担；而且需要 Windows 专用 TFM 或 COM
  互操作，与当前跨平台 + NativeAOT 结构冲突。
- **不要回到 libvips。** 它带来 7.7 MB 和 LGPL 系依赖；0.5.0 的移除本身是正确的。
  libavif 只解 AVIF，不会顺带支持 HEIC，边界更清楚。

## 推荐架构

```text
AirGlance.Core
  ImageDecoder
    ├─ TIFF  -> LibTiff（现有）
    ├─ SVG   -> Svg.Skia（现有）
    ├─ AVIF  -> LibAvifDecoder（新）
    │            └─ LibraryImport -> airglance_avif.dll / .so / .dylib
    │                                  └─ 静态链接 libavif + dav1d
    └─ 其他  -> SKCodec（现有）

native/avif/
  shim/airglance_avif.c
  build.ps1 / build.sh
  README.md（固定版本、SHA-256、构建命令、许可证）
```

### 原生层

- 每个 RID 产出 **一个** `airglance_avif` 库，静态链接 libavif + dav1d；不把
  libavif/dav1d 的内部 ABI 暴露给 C#。
- Windows 用 **`/MT`（静态 CRT）**，避免要求用户安装 VC++ Redistributable；portable
  zip 和 MSIX 都不应多一个运行时依赖。
- 只保留解码路径：`AVIF_CODEC_DAV1D=SYSTEM`（或 `LOCAL`）、
  `AVIF_BUILD_APPS=OFF`、`AVIF_BUILD_TESTS=OFF`、`AVIF_LIBYUV=OFF`（或按需
  LOCAL），不编 AVIF 编码器。
- C shim 只暴露少量函数（4–5 个），避免在 C# 里 marshal `avifDecoder`/`avifImage`
  这些大结构体：
  - `airglance_avif_probe()`：库是否可加载、libavif 版本；
  - `airglance_avif_info(data, size, out info)`：只 parse，不解像素；返回尺寸、位深、
    alpha、`irot`/`imir`/`clap`、ICC/EXIF 数据；
  - `airglance_avif_decode(data, size, maxWidth, maxHeight, dst, dstCapacity, out info)`：
    解第一帧，写入调用方提供的 BGRA premultiplied 缓冲（分配器所有权见下）；
  - `airglance_avif_free_info(info)` / `airglance_avif_free(ptr)`：释放 shim 分配的
    ICC/EXIF 副本或临时缓冲。
- 输入用内存（`avifDecoderSetIOMemory`）而不是路径：C# 先读文件，文件不存在/无权限/
  IO 错误沿用现有异常映射；同时可以在 C# 侧限制文件大小。
- ICC/EXIF 从 libavif 取出时按现有上限复制（EXIF 1 MiB、ICC 4 MiB），避免恶意
  元数据撑爆内存。
- `avifDecoder.imageSizeLimit` / `imageDimensionLimit` 设上限（默认 16384² / 32768）。
  建议预览上限设 64 MP、全图上限对齐现有 250 MP 大图阈值；超限报 `Unsupported`，
  不要先分配再失败，防止解压炸弹和 OOM。
- `avifRGBImage`：`format = AVIF_RGB_FORMAT_BGRA`、`depth = 8`、
  `alphaPremultiplied = AVIF_TRUE`、`maxThreads` 取 CPU 数的一个小上限。这样输出
  布局和现有 `DecodedBitmap`/`BitmapFactory` 完全一致，C# 不需要再转一次。
- **分配器所有权要提前定好**：`DecodedImageBuffer.Dispose` 现在用
  `NativeMemory.Free`；如果 shim 用 `/MT` 的 `malloc` 分配、C# 用另一套 CRT 释放，
  会崩。推荐 C# 先按 `info` 算好目标尺寸，用 `NativeMemory.Alloc` 分配目标 BGRA，
  shim 通过 `avifRGBImage.pixels` 直接写入；预览则写入 pinned `byte[]`。如果让 shim
  分配，就必须给 `DecodedImageBuffer` 加 free 回调，不能和 `NativeMemory.Free`
  混用。
- 预览：先 decode，再用 `avifImageScale` 缩到不超过请求尺寸的 2 倍，然后 YUV→RGB；
  避免为大图先分配全尺寸 BGRA。若 `avifImageScale` 不适用（尺寸超限），再退回
  Skia 缩放。
- 动画 AVIF：只解第一帧（`avifDecoderNextImage` 一次），与 0.4.1 行为一致；
  `imageCount` 可以读，但不要播放。

### C# 层

新增 `LibAvifDecoder`（internal）和 `AvifNative`（`LibraryImport`），在
`ImageDecoder` 三个入口分支：

- `Decode(path, maxWidth, maxHeight)`：预览，返回 `DecodedBitmap`（BGRA premul）。
- `DecodeFullRaw(path)`：返回 `DecodedImageBuffer`，用于 100% tile 和编辑导出。
- `ReadMetadata(path)`：只 parse；`FormatName = "AVIF"`、`BitDepth = image->depth`、
  `IccDescription = IccProfile.ReadDescription(image->icc)`、
  `Exif = ExifReader.Read(image->exif)`、`FrameCount = 1`（只显示首帧，避免被标成
  动画）、`HasAlpha` 由 alpha plane 是否存在决定。注意确认 libavif 返回的 Exif
  payload 起点（`Exif\0\0`、TIFF header 还是 4 字节偏移），必要时先剥前缀；用一个
  带 EXIF 的 fixture 验证，不要假设。
- `SupportedFormats`：加入 `.avif`，`Describe` 返回 `AVIF`。如果某个平台不带 native
  库，应在这里按 `AvifNative.IsAvailable` 隐藏 `.avif`，避免“列出来但打不开”。
  更简单的做法是所有发布 RID 都带库，不做动态 gate。
- 库放可执行文件同目录，依赖 .NET 默认探测；如果 macOS/Linux 或 AOT 下不命中，
  用 `NativeLibrary.SetDllImportResolver` 显式指向 `AppContext.BaseDirectory`。
  native 库的复制要覆盖测试项目输出（例如通过 `Directory.Build.targets`），否则
  测试进程找不到库。

### 方向、裁剪、色彩：必须处理好的三件事

1. **libavif 不会自动应用 `irot`/`imir`/`clap`。** 1.4.2 的 `avif.h` 写得很明确：
   这些 transform 只解析元数据，像素不会被预先裁剪或镜像，调用方必须在渲染/转换前
   应用。不要假设“libavif 已经转好了”。推荐让 shim 把 `transformFlags`、
   `irot`、`imir`、`clap` 返回给 C#，用现有 `ImageTransform` 的 8 种 EXIF 方向映射
   应用；`clap` 用 `avifImageSetViewRect` 或 C# 裁剪。`pasp` 只影响显示宽高比，
   不影响像素范围，可以先忽略。
2. **EXIF orientation 与 `irot`/`imir` 可能同时存在**，直接套用
   `ImageHeaderReader` 的 EXIF 方向会二次旋转。规则：有 `irot`/`imir` 时以它为准，
   否则用 EXIF orientation；加一个带方向标签的 fixture 验证。
3. **ICC/HDR**：AVIF 可以带 ICC 或 CICP（BT.709/P3/BT.2020）。0.4.1 的 NetVips
   路径没有做 ICC 转换；如果要保持“所有图片转 sRGB”的承诺，解码后用 Skia 把源
   色彩空间转到 sRGB（`SKColorSpace.CreateIcc` + 一次绘制/复制）。建议 v1 做 ICC
   （约 1 天），HDR/gain map 明确不做，只在文档里说明。未知 CICP 组合回退 sRGB。

## 集成清单

| 区域 | 改动 |
| --- | --- |
| `SupportedFormats` | 加 `.avif`、`IsAvif`、`Describe`；必要时按 native 可用性 gate |
| `ImageDecoder` | `Decode` / `DecodeFullRaw` / `ReadMetadata` 三个 AVIF 分支；错误映射 `ImageLoadException` |
| 新文件 | `LibAvifDecoder.cs`、`AvifNative.cs`、`native/avif/**` |
| `ImageHeaderReader` | 不改；AVIF 的 EXIF/ICC 从 libavif 拿，避免在 C# 里解析 ISOBMFF |
| App 层 | 无逻辑改动：缩略图、启动预载、filmstrip、信息面板、编辑、导出都经 `ImageDecoder` 自动生效；`CanEdit` 条件已满足（非 proxy、非 tiled、非动画） |
| 打包 | `.csproj` 按 RID 复制 native 库；`pack.ps1` / `pack.sh` 逻辑不变，但发布后要校验库存在；MSIX manifest 已有 `.avif` 关联，可保留 |
| macOS/Linux 关联 | 可选：Info.plist 增加 AVIF UTI（标识符需在目标 macOS 上确认），`.desktop` 增加 `image/avif`；不做也能打开文件，只是“打开方式”里不出现 |
| 测试 | 新增 AVIF fixtures：基础 8-bit、alpha、10-bit、4:4:4、`irot`/`imir`、EXIF 方向、截断/损坏、超大尺寸、动画首帧；三平台跑。fixtures 用 ffmpeg/avifenc 生成并记录命令（本机 Pillow 10.2 不支持 AVIF） |
| CI | `build.yml` 三个 OS 先构建 native 再 `dotnet test`；`release.yml` 每个 RID 构建对应 native；缓存按版本键 |
| 文档 | README / README.zh-CN、docs/formats*、docs/architecture*、docs/shipping*、CHANGELOG、THIRD-PARTY-NOTICES、packaging/msix/README、docs/design/avif-support.md 状态 |
| 清理 | `store.yml` 的 NetVips/AVIF 注释、`APP_VERSION 0.4.1`；MSIX README 的 0.4.1/扩展列表 |

## 平台与构建

发布矩阵（`.github/workflows/release.yml`）：`win-x64`、`win-arm64`、`osx-x64`、
`osx-arm64`、`linux-x64`、`linux-arm64`。

| RID | 构建要点 | 风险 |
| --- | --- | --- |
| win-x64 | MSVC + CMake + Meson + NASM；`/MT`；x86 SIMD | 中：NASM 与 MSVC 的 Meson 组合需验证，必要时 clang-cl |
| win-arm64 | dav1d 的 arm64 汇编是 GAS 语法，MSVC 不能直接汇编；用 clang-cl 或先关 asm | 高：性能/构建二选一 |
| osx-x64 | Xcode clang + Meson + NASM；dylib 放 `Contents/MacOS` | 低 |
| osx-arm64 | NEON，无需 NASM；将来签名要覆盖 dylib | 低 |
| linux-x64 | gcc/clang + Meson + NASM；静态链接，避免 rpath | 低 |
| linux-arm64 | NEON；在 ubuntu-24.04-arm 构建 | 低 |

注意：

- 构建脚本固定版本 + SHA-256 校验源码包；产物不提交到 git；CI 缓存按
  `libavif/dav1d` 版本键。
- Windows 不要动态链接 VC++ 运行时；`/MT` 统一，否则干净机器会缺 DLL。
- macOS 当前发布包本身未签名；等将来签名/公证时，`airglance_avif.dylib` 也要一起签。
- 本地开发：运行 `native/avif/build.ps1` / `build.sh`；没有 native 库时 AVIF 测试
  要明确跳过并提示，CI 不允许静默跳过（native 构建失败必须让 job 失败）。
- 不要在只完成 win-x64 时发版：release 的 6 个包要么都带库，要么在其它平台隐藏
  `.avif`，不能出现“同一个版本，Windows 能开、macOS 不能开”的文档承诺。

## 风险

| 风险 | 可能性 | 影响 | 缓解 |
| --- | --- | --- | --- |
| 6 个 RID 原生构建/CI 复杂度 | 中高 | 进度 | 先 win-x64；脚本化 + 缓存；arm64 允许 no-asm 兜底 |
| `irot`/`imir`/`clap` 或 EXIF 二次旋转 | 中 | 显示错误 | 专项 fixtures；只在 C# 应用一次；对照 ffmpeg/WIC 参考图 |
| ICC/HDR 色彩偏差 | 中 | 观感 | v1 用 Skia 做 ICC→sRGB；HDR 明确不支持 |
| 超大 AVIF 内存 | 中 | OOM | `imageSizeLimit`/`imageDimensionLimit`；预览先 scale；超限报 Unsupported |
| 原生解码器安全面 | 中 | 崩溃/漏洞 | 固定最新版、限制尺寸/像素、定期升级；不引入更多解码器；保留快速下线开关（隐藏 `.avif`） |
| 包体超预算 | 低 | 发布 | 静态链接 + LTO/`-Os`；实测；30 MB 预算仍有余量 |
| AOT P/Invoke | 低 | 阻塞 | 0.4.1 的 libvips 已验证；shim 只用 blittable 参数 |
| 许可证合规 | 低 | 法务 | BSD-2 全文进 THIRD-PARTY-NOTICES；随包分发 |
| Store/WACK | 低 | 上架 | DLL 放包根；full trust 无需新 capability；WACK 预检 |

## 里程碑

- **M0（1 天）Windows x64 spike**：静态 shim + `/MT` + SIMD；AOT publish 后从输出
  目录加载；解 `sample.avif`、alpha、10-bit、`irot`/`imir`、EXIF 方向；量包体和
  decode 时间。五项全绿再继续。
- **M1（2–3 天）Windows x64 接入**：`SupportedFormats` + `ImageDecoder` 三分支 +
  元数据 + 错误映射 + 测试 + 文档；产出可发布的 win-x64 zip。
- **M2（3–5 天）6 个 RID**：win-arm64/macOS/Linux 构建脚本、CI、三平台 fixtures、
  发布校验。
- **M3（1–2 天）加固**：ICC、超大/损坏/截断、动画首帧、包体、许可证、MSIX/Store
  清理。

合计 **8–12 人日**（含缓冲）；只做 Windows x64 约 **3–5 人日**。

## 验收标准

- `sample.avif` 1440×900 解码成功，颜色与 ffmpeg/WIC 参考图在容差内一致。
- alpha fixture：透明角 alpha = 0，半透明像素 premultiplied，中心颜色正确。
- 10-bit/12-bit AVIF 解到 8-bit，颜色无明显断层。
- `irot`/`imir` fixture 显示方向正确；带 EXIF orientation 的文件不二次旋转。
- 截断/损坏 AVIF → `ImageLoadFailure.Corrupt`，不崩溃、不弹未处理异常。
- 超过像素上限 → `Unsupported`（或明确提示），不 OOM。
- 动画 AVIF 只显示第一帧；信息面板显示 AVIF/尺寸/位深/ICC/EXIF。
- 预览遵守 `maxWidth`/`maxHeight`；100% 查看、旋转/裁剪、另存 JPEG/PNG 正常。
- 6 个发布 RID 都带 native 库；`dotnet test` 三平台通过；zip < 30 MB；
  THIRD-PARTY-NOTICES 含 BSD-2 全文。
- 无新增运行时网络调用/遥测。

## 建议

1. **做**，但按“先 Windows x64 spike、再全 RID”的顺序；不要直接开 6 平台。
2. **不要**回到 libvips；也**不要**把 WIC 作为唯一实现。
3. spike 通过前不改 release 文档承诺；通过后按 minor 版本（0.6.0）发布。
4. 无论 AVIF 是否恢复，先修 MSIX `.avif` 关联与 `store.yml` 的过期注释——现在它们
   和实际能力不一致。
5. HEIC/HEIF、RAW、AVIF 编码继续不支持；AVIF 动画只取第一帧。

## 实现结果（2026-11-20，Windows x64）

本评估按“先 Windows x64 spike”落地，已完成并本地验证：

- 原生层：libavif 1.4.2 + dav1d 1.5.5，x86 SIMD，静态 CRT，单个
  `airglance_avif.dll` **2.06 MiB**；压缩进 zip 后约 **0.84 MiB**。
- C#：`AvifNative`（`LibraryImport`）、`LibAvifDecoder`、`SupportedFormats` 门控、
  `ImageDecoder` 的预览/全图/元数据分支；`Directory.Build.targets` 把 DLL 复制到
  可执行文件和测试输出。
- 测试：10 个 AVIF 测试通过（基础、alpha、10-bit/irot、ICC/EXIF、动画首帧、1×1、
  损坏、可用性、支持列表、预览缩放）；Core 187 + App 35 全绿。
- AOT 打包实测（win-x64 zip 解压后运行 `--bench=decode`）：
  - 1440 × 900：**17.6 ms**，峰值 41.7 MB；
  - 4000 × 3000（12 MP）：**164.2 ms**，峰值 103.8 MB。
- 打包后的完整启动路径（`--bench=startup`，1440 × 900）：first window
  **122.1 ms**、first frame **191.1 ms**、decode **17.1 ms**、峰值 100.8 MB。
- 包体：带 AVIF 的 win-x64 zip **20.84 MB**（十进制显示），低于 30 MB 预算；
  DLL 本身 2.06 MiB。
- 限制：只做了 Windows x64；`win-arm64`/macOS/Linux 隐藏 `.avif`；ICC 只读描述、
  不做像素色彩管理；HDR/gain map 不支持；AVIF 编码不支持；动画只取第一帧。
- 构建：`native/avif/build.ps1` 下载/校验源码并构建；`pack.ps1` 为 `win-x64`
  自动调用；CI 的 Windows 测试、Release 和 Store 任务已加构建步骤。

## 已有基础

`spike/avif-libavif` 分支有一份 POC 记录：
[avif-libavif-dav1d.md](https://github.com/RyzenWang/AirGlance/blob/spike/avif-libavif/docs/design/avif-libavif-dav1d.md)。
这也是 [no-libvips.md](no-libvips.md) 里列出的方案 1（libavif + dav1d），不是新方向。

POC 在 Windows x64 上实际编译并解码成功：libavif 1.4.2 + dav1d 1.5.3、MSVC 19.44、
关闭 libyuv 和 dav1d x86 SIMD；1440×900 解码约 90 ms（含进程启动），alpha 完全保留，
10-bit 正常降到 8-bit。POC 没有接进应用，也没有做 AOT/打包/跨平台，因此本评估把它
当作“解码器可用”的证据，而不是“功能已完成”。

## 附：本机验证记录

- OS：Windows 11 Pro 10.0.26200 x64。
- 已安装 `Microsoft.AV1VideoExtension 2.0.35.0`、`Microsoft.HEIFImageExtension
  1.2.62.0`、`Microsoft.WebpImageExtension 1.2.31.0`。
- 用 WinRT `Windows.Graphics.Imaging.BitmapDecoder` 解码 `tests/Assets/sample.avif`：
  成功，1440×900，BGRA8，`FrameCount = 1`；`alpha.png` 对照也成功。
- 0.5.0 发布包的 `libSkiaSharp.dll` 中没有 `avif`/`dav1d` 字符串；
  `SkiaSharp.dll` 里只有 `Avif` 字样（枚举名），不代表原生解码器存在。
- 0.4.1 发布包内含 `libvips-42.dll`，AVIF 通过它解码，说明 NativeAOT + 原生 DLL
  的路径在本项目可用。
- 版本信息来自 release feed：libavif 1.4.2（2026-05-26）、dav1d 1.5.5（2026-11-16）。

参考：

- [libavif releases](https://github.com/AOMediaCodec/libavif/releases)
- [dav1d tags](https://code.videolan.org/videolan/dav1d/-/tags)
- [libavif 1.4.2 avif.h](https://github.com/AOMediaCodec/libavif/blob/v1.4.2/include/avif/avif.h)
