# 去除 libvips spike

> 分支：`spike/no-libvips`（已合并）
>
> 后续：AVIF 已于 2026-11-20 通过 libavif + dav1d（方案 1）在 Windows x64/arm64、macOS
> 和 Linux 恢复，未重新引入 libvips。见 [avif-revival.md](avif-revival.md)。

## 做了什么

- 移除 `NetVips` / `NetVips.Native.win-x64` 包引用和 `libvips-42.dll`；
- 删除 `NetVipsImageDecoder`、`NetVipsTiffDecoder`、`EditedImageExporter`；
- 删除大 TIFF 的 libvips 回退；
- 编辑导出统一走 `ImageExporter`（SkiaSharp）；
- 从支持列表移除 AVIF，因为 SkiaSharp 4.150.1 无法解码 AVIF；
- 删除对应的 AVIF 测试和 EditedImageExporter 测试。

## 结果

- NativeAOT `win-x64` 包：**20.01 MB**（原 27.72 MB，减少 **7.71 MB / 27.8%**）；
- 包内已无 `libvips-42.dll`；
- Core **177 passed**，App **35 passed**（少 4 个：1 个 AVIF + 3 个 EditedImageExporter）；
- 真实 ESO Trifid 20 MP：
  - first_frame 1515.5 ms
  - decode 1104.6 ms
  - peak 263.6 MB
- 256 MP 真实内容文件：
  - first_frame 1906.2 ms
  - decode 1763.7 ms
  - peak 331.8 MB
- 这些数据与带 libvips 的包基本持平，部分更快。

## 代价

- **AVIF 不再支持**：SkiaSharp `SKCodec.Create` 对样例返回 null；没有其他托管 AVIF 解码器。
- **编辑导出丢失 EXIF/ICC/XMP 保留**：`ImageExporter` 只写像素；JPEG/PNG/WebP 仍可用。
- **12-bit JPEG 从回退变为失败**：当前本来也没有可用的 12-bit 解码器。
- TIFF 的 libvips 兜底（极端 photometric/bit-depth）被移除。

## 结论

技术上可以去掉 libvips 并缩小 7.7 MB，代价是 AVIF。若要保留 AVIF，需要：

1. 引入 libavif + dav1d 原生库（仍是非托管依赖，但比 libvips 小）；
2. 或接受 AVIF 不支持，合并本分支；
3. 或保留 libvips，仅用于 AVIF。

目前建议**不合并**，先决定 AVIF 去留。
