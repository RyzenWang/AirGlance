# 托管 TIFF 预览

> 分支：`spike/managed-tiff-preview`（基于 `spike/managed-tiff-region`）  
> 状态：验证完成，未合并。目标是把大 TIFF 的元数据和代理预览也从 libvips 换成托管 LibTiff.NET。

## 结论

可行。`16000 × 16000`（256 MP）的 tiled、stripped 和单 strip TIFF 都能用托管路径生成代理；并行 tile/strip 解码后，tiled 首帧约 **616 ms**、stripped 约 **585 ms**，峰值内存 **80–125 MB**。有金字塔时，fit 预览只解码一个 reduced IFD，decode 约 **50 ms**、首帧约 **331 ms**；8192 目标复用 8000 px level，约 **0.68–0.72 s**，而不是全图的 1.5 s。旧 libvips 路径约 **2.1–2.6 s / 270–300 MB**，托管路径现在同时更快、更省内存。单 strip 仍是顺序 `ReadScanline`，约 2.4 s。

托管预览让大 TIFF 不再依赖 libvips 的 `Thumbnail`，是让大 TIFF 在 macOS/Linux 端到端可用的关键一步。

## 实现

新增 `src/AirGlance.Core/Imaging/TiffPreviewDecoder.cs`：

- **元数据**：用 LibTiff.NET 读宽度、高度、`BITSPERSAMPLE`、`ICCPROFILE`；`FormatName = "TIFF"`，方向按原始存储方向报告。
- **tiled 预览**：
  - 8-bit/16-bit RGB/gray、8-bit CMYK、8-bit/16-bit RGBA、8-bit palette、JPEG（RGB、YCbCr、CMYK）、YCbCr（非 JPEG、subsampling=1×1，或未压缩/LZW/Deflate subsampled）、contig、无损压缩（None/LZW/Deflate/PackBits）走快速路径：`ReadEncodedTile` 取原始样本，只采样需要的像素；
  - RGBA 的 alpha 按 RGBA 路径同样预乘；palette 查 `COLORMAP`；YCbCr 按 BT.601 整数公式转 RGB；未压缩 subsampled YCbCr 从原始 TIFF strip 逐 block 解析；
  - YCbCr JPEG 在 raw 读取前设 `JPEGCOLORMODE=RGB`，LibTiff 返回 RGB；CMYK JPEG 返回 CMYK；LZW/Deflate 压缩的 subsampled YCbCr 仍走 RGBA 转换；
  - 其他情况（planar、float、16-bit palette/alpha）走 `ReadRGBATile` 的 RGBA 转换路径。
- **stripped 预览**：
  - 8-bit/16-bit RGB/gray、8-bit CMYK、8-bit/16-bit RGBA、8-bit palette、JPEG（RGB、YCbCr、CMYK）、YCbCr（非 JPEG、1×1，或未压缩/LZW/Deflate subsampled）走 `ReadScanline` 逐行快速路径：内存只有一行，**单 strip 大图也能预览**；
  - 16-bit sample 取高字节转 8-bit；CMYK 按 `255 - min(255, C + K)` 转 RGB；palette 查 `COLORMAP`；RGBA 预乘 alpha；YCbCr JPEG 设 `JPEGCOLORMODE=RGB`；非 JPEG YCbCr 用 BT.601 公式；未压缩 subsampled YCbCr 逐 block 解析；
  - 多 strip 的 16-bit 走 `ReadEncodedStrip` 的并行 raw 路径；
  - 其他情况走 `ReadRGBAStrip`，单条带光栅上限 64 MiB。
- **采样**：每个目标像素在每个轴上约 2 个采样点（`step ≈ 0.5 / scale`），最近邻写入；目标缓冲零初始化，所有目标像素都有采样覆盖。不是 Lanczos/box 平均，代理在细密纹理上会比 libvips 略硬。
- **方向**：在内存目录中把 orientation 改成 `TOPLEFT`，返回原始存储方向；和区域 tile、`DecodeFullRaw` 一致。源文件不重写。
- **回退**：Windows 上，单条带 16-bit/CMYK 等超出托管限制的情况回退到 `NetVipsTiffDecoder`；其他平台返回明确错误。
- **路由**：`ImageDecoder.DecodeTiff` 的大图分支和 `ReadTiffMetadata` 的大图分支改走托管实现。
- **并行解码**：大于 2 MP 的 tiled 和 multi-strip 图按源行带分给最多 8 个 worker，每个 worker 打开自己的 `Tiff` 句柄；目标像素用一次 32 位 `Volatile.Write` 写入，边界重复采样时 last-writer-wins，不会把像素拆成混合通道。worker 数还受 256 MiB 的每 worker 光栅预算限制。
- **pyramid/subIFD**：递归遍历顶层目录及其 `SUBIFD` 子目录（深度上限 8，offset 去重），跳过 `SUBFILETYPE` 里 page/mask 的目录。LibTiff.NET 不暴露 classic TIFF（IFD type 13）的 `SUBIFD` 标签，所以 `GetField` 为空时用原始 IFD 解析器按 tag 330 读 offset；BigTIFF 的 IFD8 标签仍走 LibTiff。选面积最小、宽高都在目标 95% 以上、宽高比与全图一致、面积小于全图的 reduced level，直接在这个 IFD 上解码。选中的 level 也走并行路径：worker 通过 `OpenAtLevel` 打开同一目录或 subIFD。level 比目标略小时（例如 8000 → 8192），用 32 位块写入做最近邻上采样，保证目标没有空像素。
- **应用修正**：`MainWindowViewModel.OnZoomChanged` 在 `percent >= 30` 时不再启动代理升级。旧逻辑在区域模式打开的同一帧还会启动 8192 代理升级，导致“8192 代理 + 区域 tile”同时解码，100% 时工作集冲到 ~1.4 GB。修正后只走区域 tile。

## 验证

### 单元测试

新增 `tests/AirGlance.Core.Tests/TiffPreviewDecoderTests.cs`，31 个测试：

1. tiled TIFF 缩放到 80 × 50，四角颜色和 alpha 正确；
2. stripped TIFF 同上；
3. 单 strip TIFF 同上；
4. tiled 边缘 tile（300 × 190，64 tile）四角正确；
5. orientation tag 为 6 时仍返回原始存储方向；
6. RGBA alpha 在预览中保留；
7. 请求 20000 px 时目标被夹到源尺寸；
8. 元数据报告尺寸、位深、格式；
9. 2048 × 1536 tiled 走并行路径后四角颜色正确；
10. 2048 × 1536 stripped 走并行路径后四角颜色正确；
11. 已取消的 token 抛出 `OperationCanceledException`；
12. 两层目录 TIFF 在目标较小时选中 reduced 目录（绿色），而不是全图目录（红色）；
13. 2048 × 1536 reduced 目录超过并行阈值时，worker 仍然打开该目录（绿色），不是 dir 0（红色）；
14. 2048 reduced 目录被放大到 2100 目标时没有空像素，且仍选中 reduced 目录；
15. 三层目录里 dir 1 是 page（绿色）、dir 2 是 reduced（蓝色），预览选蓝色而不是 page；
16. classic TIFF（IFD type 13）的 `subifd-levels.tif` fixture 选中 100 × 75 的蓝色 subIFD；
17. 4200 × 4000 的 16-bit 单 strip TIFF（RGBA 光栅 > 64 MiB）走 scanline，四角颜色正确；
18. 2048 × 1536 的 16-bit tiled TIFF 走 raw tile 并行路径；
19. 2048 × 1536 的 16-bit multi-strip TIFF 走 raw strip 并行路径；
20. CMYK LZW 四象限 TIFF 的四角颜色正确；
21. RGB JPEG-in-TIFF 四象限的四角颜色正确；
22. 4200 × 4000 的 CMYK 单 strip（raw strip 67.2 MB）走 scanline，四角颜色正确；
23. palette 四象限 TIFF 的四角颜色正确；
24. RGBA 四象限 TIFF 的四角颜色和 alpha（255/200）正确；
25. 2048 × 1536 YCbCr JPEG 四象限（128 行 strip）走 raw 并行路径，四角颜色正确；
26. 2048 × 1536 CMYK JPEG 四象限（128 行 strip）走 raw 并行路径，四角颜色正确；
27. 320 × 200 非 JPEG YCbCr（subsampling=1×1）四象限颜色正确；
28. 320 × 200 未压缩 YCbCr（subsampling=2×2）四象限颜色正确；
29. LZW 压缩的 2×2 YCbCr 四象限颜色正确；
30. Deflate 压缩的 2×2 YCbCr 四象限颜色正确；
31. 16-bit RGBA 四象限颜色和 alpha（255/156）正确。

完整套件：Core **161 passed**，App **35 passed**（含 24 个区域读取测试）。

### 直接预览基准（256 MP，`TiffPreviewDecoder.DecodePreview`）

8 worker、Xeon E5-2697 v4 上的实测：

| 文件 | 目标 | 耗时 | 峰值增量 |
| --- | --- | --- | --- |
| tiled 16000² | 1000² | 0.79 s | +16.9 MB |
| tiled 16000² | 4096² | 0.60 s | +65.3 MB |
| stripped 16000² | 1000² | 0.42 s | +48.7 MB |
| stripped 16000² | 4096² | 0.61 s | +113.3 MB |
| single strip 16000² | 1000² | 2.39 s | +8.8 MB |
| single strip 16000² | 4096² | 3.60 s | +40.3 MB |

带金字塔的 16000² 文件，fit 预览只解码 reduced level：subIFD 版 **55–68 ms**，顶层目录版 **55–62 ms**（无金字塔 654 ms）。4096² 目标会选中 8000 px level 并并行解码：subIFD **385 ms**、顶层目录 **363 ms**（无金字塔 640–677 ms）。8192² 目标允许 8000 level 做约 2.4% 的块上采样：subIFD **717 ms**、顶层目录 **675 ms**（无金字塔 1519 ms），目标没有空像素。

### 应用 `--bench=startup`（NativeAOT win-x64）

| 文件 | 首帧 | decode_ms | 峰值 WS |
| --- | --- | --- | --- |
| tiled 16000² | 616 ms（首跑冷启动 1898 ms） | 478 ms | **81.3 MB** |
| stripped 16000² | 585 ms | 447 ms | **125.2 MB** |
| single strip 16000² | 2553 ms | 2408 ms | **105.2 MB** |
| pyramid subIFD 16000² | **331 ms** | 53 ms | **82.7 MB** |
| pyramid dirs 16000² | **331 ms** | 49 ms | **82.4 MB** |

走 `ImageDecoder.Decode(..., 4096)` 的代理升级路径：pyramid subIFD **579 ms**、pyramid dirs **431 ms**（无金字塔约 640–677 ms）。

16-bit 16000²（单 strip / tiled / multi-strip）：

| 文件 | 首帧 | decode_ms | 峰值 WS |
| --- | --- | --- | --- |
| 16-bit single strip | 5126 ms | 4993 ms | 100 MB |
| 16-bit tiled | 1736 ms（冷启动 3034 ms） | 738–770 ms | 107–113 MB |
| 16-bit multi-strip（rowsPerStrip=128） | 1567–1629 ms | 698–745 ms | 287 MB |

CMYK 4200 × 4000 单 strip（raw strip 67.2 MB）：预览 **340 ms**；区域首次建缓存 **581 ms**，后续 512 tile **9 ms**。

旧 libvips 路径对照（同一批文件，上一轮）：tiled 首帧 2139 ms / 268 MB；stripped 2647 ms / 299 MB；single strip 回退 3663 ms / 312 MB。

### 100% 区域模式工作集

| 文件 | 托管预览 + 本次修正 | 旧 libvips 预览 |
| --- | --- | --- |
| tiled 16000² | **164–228 MB** | 563 MB |
| stripped 16000² | **336 MB** | 699 MB |
| 16-bit tiled 16000² | **479 MB** | — |
| 16-bit multi-strip 16000² | **671 MB** | — |
| 16-bit 单 strip 16000² | **474 MB**（首 tile 约 9.9 s） | — |
| 8-bit 单 strip 16000² | **372 MB**（首 tile 约 6.9 s） | — |

tiled 的 164–228 MB 包含区域 tile 缓存；stripped 的 336 MB 主要多出 96 MiB 的 strip 缓存和 LibTiff.NET 的条带解码缓冲。16-bit 区域路径同样使用 raw tile/strip，工作集更高，主要来自 16-bit 解码和 96 MiB strip 缓存。

## 已知限制

- **single strip**：单 strip 的 8-bit RGB/gray 仍走顺序 `ReadScanline`（256 MP 约 2.4 s），没有并行；LZW 单流无法按行带拆分。
- **并行输出的最后写入者**：行带边界可能有两个 worker 对同一目标像素采样，32 位原子写保证不会撕裂，但具体取哪个样本取决于调度；对代理无影响，但不是逐次完全一致。
- **并行内存**：stripped 4K 预览的峰值增量约 113 MB（8 个 worker 的条带光栅 + 目标缓冲）。
- **质量**：代理是 2× 过采样的最近邻，不是 Lanczos/box；细密网格和纹理可能比 libvips 硬。30% 以上由区域 tile 接管。
- **格式长尾**：planar separate、float、16-bit palette 仍走 `ReadRGBA*`；8-bit CMYK（含 CMYK JPEG）、RGBA（8/16-bit）、palette、RGB/YCbCr JPEG、非 JPEG 1×1 YCbCr、未压缩及 LZW/Deflate 压缩的 subsampled YCbCr 已走 raw，16-bit RGB/gray 也已 raw，但 float/非 uint 16-bit 仍可能回退 libvips（Windows）或报错。
- **16-bit multi-strip 内存**：8 个 worker 各有一份条带光栅；16000 宽、`rowsPerStrip=128` 时峰值约 287 MB。
- **16-bit 区域**：tiled 和 multi-strip 走 raw 区域路径，100% 可用；单 strip 的 raw RGB/gray 会建临时 BGRA 缓存（16-bit 首次约 9.9 s，8-bit 约 6.9 s，磁盘约 1 GB），其他格式仍抛 `TooLarge`。16-bit tiled/multi-strip 100% 工作集约 479/671 MB。
- **pyramid level 选择**：允许 level 比目标小最多 5%，用最近邻块上采样（8000 → 8192 约 2.4%）；细节会比全图略软。低于 95% 的 level 不选，8192 目标下仍可能回退全图。
- **pyramid 枚举范围**：递归 `SUBIFD` 树，但只跟随 SubIFD tag；不跟随 SubIFD 之间的 NextIFD 链，也不处理多页文档的页缩略图。`SUBFILETYPE` 的 page/mask 位会跳过；tag 缺失或为 0 时仍用尺寸和 1% 宽高比启发式，同比例的小页理论上可能被误选。
- **ICC**：元数据会显示 ICC 描述，但预览像素没有做 ICC→sRGB 转换，和旧的大 TIFF 路径一致。
- **方向**：大 TIFF 仍按原始存储方向显示；代理和区域 tile 一致，但和普通 TIFF 的“应用 orientation”行为不同。

## 下一步

1. **更多金字塔布局**：SubIFD NextIFD 链、更多 `SUBFILETYPE` 组合、多页文档的页缩略图。
2. **更多 16-bit / 长尾路径**：CMYK/YCbCr/JPEG 转换；之后可以移除 libvips 回退。
3. **单 strip 并行**：需要能按行带解码的 LZW 流式实现，或者先接受顺序路径。
4. 用真实 Cosmic（tiled）和 Trifid（single strip）验证预览和区域浏览。
