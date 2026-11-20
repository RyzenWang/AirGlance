# 托管 TIFF 区域读取（spike）

> 分支：`spike/managed-tiff-preview`
> 状态：验证完成，未合并。目标是用托管 LibTiff.NET 替换 `TiffRegionReader` 里的 libvips 实现。

## 结论

可行。现有 `TiffRegionReader` 的公开接口（`Open`、`PixelWidth`、`PixelHeight`、`ReadTile`、`Dispose`）可以原样保留，内部改为：

- tiled TIFF：按 `TILEWIDTH` / `TILELENGTH` 用 `Tiff.ReadRGBATile` 或 `ReadEncodedTile` 逐块解码；
- stripped TIFF：按 `ROWSPERSTRIP` 用 `Tiff.ReadRGBAStrip` 或 `ReadEncodedStrip` 逐条带解码；
- 8-bit/16-bit RGB/gray 走 raw 路径，16-bit 取高字节转 8-bit；palette/CMYK/alpha 等仍走 RGBA 路径；
- 返回 BGRA、预乘 alpha 的 `byte[]`，与 `TiledImageSource` 现有约定一致。

整个路径只依赖 `BitMiracle.LibTiff.NET`（纯托管、BSD-3、AOT 可用），不再需要 libvips 读 TIFF 像素。

## 实现要点

- **不会整图解码**：`ReadTile` 只分配 `width * height * 4` 的输出和一个 tile/strip 的光栅；40K TIFF 不需要 3 GB 的缓冲区。
- **raw 路径**：8/16-bit RGB/gray、contig、uint、None/LZW/Deflate/PackBits 用原始 tile/strip 样本，避免 RGBA 转换；16-bit 取高字节。
- **strip 缓存**：stripped 图像按条带缓存已解码的光栅/字节（上限 96 MiB，LRU）。相邻 512 tile 落在同一组条带上时不会重复解码。
- **方向**：LibTiff 的 RGBA 读取会应用 TIFF orientation tag。区域路径需要“存储方向”的光栅，由 `ImageTransform` 统一应用一次方向，因此 `Open` 里把 orientation 在内存目录中改成 `TOPLEFT`。文件以只读方式打开，实测 SHA-256 不变。
- **行序**：RGBA 路径的光栅原点在左下角，复制时翻转；raw tile/strip 是 top-down，直接映射。
- **alpha**：RGBA 输出对 associated / unassociated alpha 都返回预乘颜色；raw 路径只支持无 alpha 的 RGB/gray。
- **内存上限**：单条带 RGBA/raw 光栅超过 64 MiB 时抛出 `ImageLoadException(TooLarge)`。单 strip 的 40K 图仍需要流式解码。
- **线程**：所有读取在 `lock` 内串行化；`Tiff` 句柄不是线程安全的。

## 验证

### 单元测试

新增 `tests/AirGlance.Core.Tests/TiffRegionReaderTests.cs`：

1. `rgb-lzw.tif` 返回 BGRA，左上角为红色；
2. `rgb-lzw.tif` 分块读取与 `ImageDecoder.DecodeFull` 逐像素一致；
3. `alpha.tif` 分块读取与完整解码一致；
4. 运行时生成的多 strip TIFF（`ROWSPERSTRIP = 10`，含不完整末条）与完整解码一致；
5. 运行时生成的 tiled TIFF（16 × 16 tile，含边缘 tile）与完整解码一致；
6. 边缘请求 `ReadTile(76, 36, 16, 16)` 裁剪为 4 × 4；
7. `orientation-6.tif` 的分块读取与 `DecodeFullRaw` 的原始光栅逐像素一致；
8. `Dispose` 后读取抛 `ObjectDisposedException`；
9. 16-bit tiled TIFF 的四角颜色正确；
10. 16-bit multi-strip TIFF 的四角颜色正确；
11. 16-bit 单 strip TIFF（384 KB raw strip）的四角颜色正确。

结果：Core 136 passed，App 35 passed。

### 大文件基准

`12000 × 12000` tiled BigTIFF（256 × 256 tile，LZW + predictor，2209 个 tile），同一台 Windows 10 / Xeon E5-2697 v4 机器：

```text
open: 53ms 12000x12000
first 512 tile: 44ms
20 random 512 tiles: 356ms
edge tile bytes=360000 (300x300x4)
peak WS delta=15.2MB alloc=57.5MB
```

对比现有 libvips 大 TIFF 路径的文档数据（40K × 21K：首个 tile 约 8.4 s，后续约 0.01 s，36 个视口 tile 约 0.25 s，峰值 295 MB），托管路径的内存更可控；速度取决于文件布局和磁盘缓存，量级相同。

`12000 × 12000` stripped TIFF（`ROWSPERSTRIP = 128`，LZW + predictor，6.1 MB）：

```text
open: 49ms
cold first 512 tile: 187ms
10 tiles across one 512-row band: 200ms
10 random 512 tiles: 1452ms
peak WS delta=253.7MB alloc=626.3MB
```

同一行带内的 tile 命中 strip 缓存，10 块只花约 0.2 s；随机跨条带读取仍要为每块解码 4 条带。96 MiB 缓存比 128 MiB 的峰值内存低约 100 MB，仍能装下 40K 宽、`ROWSPERSTRIP = 128` 的 4 条带（约 82 MB）。

16-bit `16000 × 16000` 大图在应用 100% 区域模式下的工作集：

| 文件 | 区域模式 | 工作集 |
| --- | --- | --- |
| 16-bit tiled | `区域 · 100%` | **479 MB** |
| 16-bit multi-strip（128 rows） | `区域 · 100%` | **671 MB** |
| 16-bit 单 strip | 不支持 | 只保留代理 |

tiled 的 479 MB 包含 16-bit raw tile 解码和区域 tile 缓存；multi-strip 的 671 MB 主要来自 96 MiB raw strip 缓存和 16-bit 条带解码缓冲。

### 已覆盖的格式细节

- tiled / stripped 两种组织；
- LZW + predictor；
- 8-bit RGB、RGBA alpha；
- 16-bit RGB/gray 的 raw tile/strip；
- orientation tag（按原始存储方向输出）；
- 边缘 tile / 不完整末条；
- BigTIFF 容器（LibTiff.NET 2.4.660 支持）。

早前的手工 POC 还验证了 JPEG-in-TIFF 的 tiled 读取（`ReadRGBATile` 返回解码后的 256 × 256 RGB）。

## 已知限制

- **单 strip 超大图**：`ROWSPERSTRIP = height` 且光栅超过 64 MiB 时返回 `TooLarge`。这类文件需要 `ReadScanline` 一遍顺序解码 + 临时 tile 缓存；早前 POC 已确认 `ReadScanline` 在 108 MB 单 strip LZW 上内存有界（托管分配 0.5 MB），实现是后续工作。16-bit 单 strip 同样受限。
- **stripped 随机访问成本**：一个 512 tile 跨 `ceil(512 / ROWSPERSTRIP)` 条带，每条带按整宽解码。同一行带内的 tile 命中缓存很快；跨行带随机跳转的缓存命中率低，单块仍可能几十到几百毫秒。96 MiB 缓存大小需要按目标图像宽度和 `ROWSPERSTRIP` 再调。
- **大 TIFF 预览/元数据仍走 libvips**：`NetVipsTiffDecoder.ReadMetadata` / `DecodePreview` 没有改，因此 Linux/macOS 的大 TIFF 端到端仍不可用。下一步是把预览也改成托管实现（按 tile 网格抽样降采样）或保留 libvips 仅做预览。
- **方向与坐标**：区域读取返回原始存储方向，代理图 `NetVipsTiffDecoder` 走 `Thumbnail(noRotate: false)`，两者对带 orientation 的大 TIFF 可能不一致；这在大 TIFF 路径里是既有问题，需要端到端决定“代理和 tile 都用显示方向”还是“都用原始方向”。
- **颜色**：TIFF ICC 仍未参与像素转换，与当前完整解码路径一致。

## 下一步

1. 用真实的 Cosmic（tiled）和 Trifid（单 strip）文件跑一遍区域浏览，记录首 tile、连续 tile、峰值内存。
2. 单 strip 流式缓存：顺序 `ReadScanline` 写入临时文件，`ReadTile` 从文件随机读。
3. 托管预览：对大 TIFF 按 tile/strip 抽样生成 1600/2048/4096/8192 px 代理，替换 `NetVipsTiffDecoder`，让大 TIFF 在非 Windows 平台可用。
4. 把 `ImageDecoder` 的大 TIFF 路由改成“托管预览 + 托管区域”，再决定是否移除 `NetVips.Tiff` 依赖。

## 复现

```bash
dotnet test tests/AirGlance.Core.Tests/AirGlance.Core.Tests.csproj -c Release \
  --filter FullyQualifiedName~TiffRegionReaderTests
```
