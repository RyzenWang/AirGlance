# 托管 TIFF 预览

> 分支：`spike/managed-tiff-preview`（基于 `spike/managed-tiff-region`）  
> 状态：验证完成，未合并。目标是把大 TIFF 的元数据和代理预览也从 libvips 换成托管 LibTiff.NET。

## 结论

可行。`16000 × 16000`（256 MP）的 tiled、stripped 和单 strip TIFF 都能用托管路径生成代理，峰值内存约 **80 MB**，而旧 libvips 路径约 **270–300 MB**。代价是 LZW 解码慢约 20–50%（256 MP 约 3.0 s，libvips 约 2.1–2.6 s）。

托管预览让大 TIFF 不再依赖 libvips 的 `Thumbnail`，是让大 TIFF 在 macOS/Linux 端到端可用的关键一步。

## 实现

新增 `src/AirGlance.Core/Imaging/TiffPreviewDecoder.cs`：

- **元数据**：用 LibTiff.NET 读宽度、高度、`BITSPERSAMPLE`、`ICCPROFILE`；`FormatName = "TIFF"`，方向按原始存储方向报告。
- **tiled 预览**：
  - 8-bit、contig、RGB/gray、无损压缩（None/LZW/Deflate/PackBits）走快速路径：`ReadEncodedTile` 取原始样本，只采样需要的像素；
  - 其他情况（16-bit、palette、CMYK、YCbCr、JPEG-in-TIFF、planar）走 `ReadRGBATile` 的 RGBA 转换路径。
- **stripped 预览**：
  - 8-bit RGB/gray 走 `ReadScanline` 逐行快速路径：内存只有一行，**单 strip 大图也能预览**；
  - 其他情况走 `ReadRGBAStrip`，单条带光栅上限 64 MiB。
- **采样**：每个目标像素在每个轴上约 2 个采样点（`step ≈ 0.5 / scale`），最近邻写入；目标缓冲零初始化，所有目标像素都有采样覆盖。不是 Lanczos/box 平均，代理在细密纹理上会比 libvips 略硬。
- **方向**：在内存目录中把 orientation 改成 `TOPLEFT`，返回原始存储方向；和区域 tile、`DecodeFullRaw` 一致。源文件不重写。
- **回退**：Windows 上，单条带 16-bit/CMYK 等超出托管限制的情况回退到 `NetVipsTiffDecoder`；其他平台返回明确错误。
- **路由**：`ImageDecoder.DecodeTiff` 的大图分支和 `ReadTiffMetadata` 的大图分支改走托管实现。
- **应用修正**：`MainWindowViewModel.OnZoomChanged` 在 `percent >= 30` 时不再启动代理升级。旧逻辑在区域模式打开的同一帧还会启动 8192 代理升级，导致“8192 代理 + 区域 tile”同时解码，100% 时工作集冲到 ~1.4 GB。修正后只走区域 tile。

## 验证

### 单元测试

新增 `tests/AirGlance.Core.Tests/TiffPreviewDecoderTests.cs`，8 个测试：

1. tiled TIFF 缩放到 80 × 50，四角颜色和 alpha 正确；
2. stripped TIFF 同上；
3. 单 strip TIFF 同上；
4. tiled 边缘 tile（300 × 190，64 tile）四角正确；
5. orientation tag 为 6 时仍返回原始存储方向；
6. RGBA alpha 在预览中保留；
7. 请求 20000 px 时目标被夹到源尺寸；
8. 元数据报告尺寸、位深、格式。

完整套件：Core **122 passed**，App **35 passed**。

### 直接预览基准（256 MP，`TiffPreviewDecoder.DecodePreview`）

| 文件 | 目标 | 耗时 | 峰值增量 |
| --- | --- | --- | --- |
| tiled 16000² | 1000² | 3.20 s | +9.0 MB |
| tiled 16000² | 4096² | 4.36 s | +64.4 MB |
| stripped 16000² | 1000² | 2.95 s | +4.0 MB |
| stripped 16000² | 4096² | 4.26 s | +64.0 MB |
| single strip 16000² | 1000² | 2.38 s | +8.5 MB |
| single strip 16000² | 4096² | 3.68 s | +68.9 MB |

### 应用 `--bench=startup`（NativeAOT win-x64）

| 文件 | 首帧 | decode_ms | 峰值 WS |
| --- | --- | --- | --- |
| tiled 16000² | 4573 ms（首跑窗口 1568 ms） | 3134 ms | **79.8 MB** |
| stripped 16000² | 3179 ms | 3001 ms | **81.0 MB** |
| single strip 16000² | 2543 ms | 2386 ms | **77.5 MB** |

旧 libvips 路径对照（同一批文件，上一轮）：tiled 首帧 2139 ms / 268 MB；stripped 2647 ms / 299 MB；single strip 回退 3663 ms / 312 MB。

### 100% 区域模式工作集

| 文件 | 托管预览 + 本次修正 | 旧 libvips 预览 |
| --- | --- | --- |
| tiled 16000² | **132 MB** | 563 MB |
| stripped 16000² | **336 MB** | 699 MB |

tiled 的 132 MB 包含区域 tile 缓存；stripped 的 336 MB 主要多出 96 MiB 的 strip 缓存和 LibTiff.NET 的条带解码缓冲。

## 已知限制

- **速度**：托管 LZW 解码约 3.0 s / 256 MP，libvips 约 2.1–2.6 s；40K × 21K 级别会更慢。下一步可以做并行 tile/strip 解码或 pyramid/subIFD 优先。
- **质量**：代理是 2× 过采样的最近邻，不是 Lanczos/box；细密网格和纹理可能比 libvips 硬。30% 以上由区域 tile 接管。
- **格式长尾**：16-bit、palette、CMYK、YCbCr、JPEG-in-TIFF 仍走 `ReadRGBA*`；单 strip 的 16-bit 等超出 64 MiB 条带上限时，Windows 回退 libvips，其他平台返回错误。
- **pyramid/parallel**：没有利用 TIFF 金字塔/subreduced IFD，也没有并行解码；这两项是下一步最大的性能空间。
- **ICC**：元数据会显示 ICC 描述，但预览像素没有做 ICC→sRGB 转换，和旧的大 TIFF 路径一致。
- **方向**：大 TIFF 仍按原始存储方向显示；代理和区域 tile 一致，但和普通 TIFF 的“应用 orientation”行为不同。

## 下一步

1. **并行解码**：按 tile/strip 行带分给多个 `Tiff` 句柄，目标按行带分区写入；预计 256 MP 可降到 1–1.5 s。
2. **pyramid/subIFD**：优先选接近目标尺寸的 reduced IFD；对有金字塔的 VIPS/Photoshop TIFF，预览可以接近瞬时。
3. **16-bit / 长尾快速路径**：`ReadScanline` + 16-bit 转换，覆盖天文/扫描类 TIFF 的单 strip。
4. **移除 libvips 回退**：等 16-bit 单 strip 和 JPEG-in-TIFF 覆盖率足够后，删掉 `NetVipsTiffDecoder`，大 TIFF 才真正跨平台。
5. 用真实 Cosmic（tiled）和 Trifid（single strip）验证预览和区域浏览。
