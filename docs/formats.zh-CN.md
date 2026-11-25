# 格式支持

[English](formats.md) · **简体中文**

AirGlance 在图像显示到屏幕之前会做两项检查：

1. 文件扩展名必须位于受支持列表中（`SupportedFormats`）。这决定了该文件是否会出现在文件夹列表中，以及命令行或拖放处理程序是否接受它。
2. 普通位图格式由 SkiaSharp 的 `SKCodec` 解码；TIFF 走 LibTiff，SVG 由 Svg.Skia 光栅化，AVIF 在 Windows x64/arm64、macOS 和 Linux 构建中由随包分发的 libavif + dav1d 解码。扩展名受支持但内容无法读取的文件会报告为损坏，而不是不受支持。

即使文件内容恰好是受支持的图像，扩展名不受支持的文件也会被拒绝。信息面板中显示的格式名称优先取自编解码器，无法获取时回退到扩展名。没有原生 AVIF 解码器时，`SupportedFormats` 会隐藏 `.avif`，不会宣称支持一个打不开的格式。

## 支持的格式

| 格式 | 扩展名 | 解码方式 | 帧数 | Alpha 通道 | EXIF 方向 | ICC 配置文件 | 测试证据 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| JPEG | `.jpg`、`.jpeg`、`.jfif` | 8 位 BGRA 输出；报告为 8 位 | 1 | 否；保存的 JPEG 副本会被压平为白色 | 已应用，EXIF 1–8 | 从 APP2 读取并转换为 sRGB | `orientation-1..8.jpg`、`display-p3.jpg`、`adobe-rgb.jpg`、`srgb.jpg`、`exif-full.jpg` |
| PNG | `.png` | BGRA 输出；源位深从 IHDR 报告（测试夹具中为 8 或 16） | 1 | 保留 | 为信息面板从 `eXIf` 读取；不应用到像素 | 从 `iCCP` 读取并转换为 sRGB | `alpha.png`、`display-p3.png`、`gradient-16bit.png` |
| WebP | `.webp` | 8 位 BGRA 输出 | 1 | 保留 | 为信息面板读取 EXIF；没有显示方向测试夹具 | 从 `ICCP` 读取并转换为 sRGB | `alpha.webp` |
| AVIF | `.avif` | libavif + dav1d 输出 8 位 BGRA；报告源位深 8/10/12 | 1（第一帧） | 保留，预乘 | 从 `irot`/`imir` 应用，缺失时回退到 EXIF 方向 | 为信息面板读取 AVIF ICC 项；像素暂不做 ICC 色彩管理 | `sample.avif`、`alpha.avif`、`irot.avif`、`icc-exif.avif`、`animated.avif`、`white-1x1.avif` |
| BMP | `.bmp` | 由 `SKCodec` 解码 | 1 | 未纳入测试覆盖 | 未读取 | 未读取 | 无测试夹具 |
| GIF | `.gif` | 8 位 BGRA 输出；按 GIF 处置规则合成每一帧 | 全部帧 | 通过合成输出保留；没有专门的透明度测试夹具 | 未读取 | 未读取 | `animated.gif` |
| ICO | `.ico` | 由编解码器选择的一幅图像 | 1 | 沿 BGRA 解码路径保留；未做测试夹具验证 | 未读取 | 未读取 | 无测试夹具 |
| TIFF | `.tif`、`.tiff` | 第一页，由 LibTiff.NET 解码为 RGBA；测试覆盖 LZW、Deflate 和 JPEG 压缩 | 1（第一页） | 保留 | 从 TIFF 方向标签读取并应用 | 为信息面板读取标签；像素不做 TIFF ICC 色彩管理 | `rgb-lzw.tif`、`alpha.tif`、`orientation-6.tif` |
| SVG | `.svg` | 由 Svg.Skia 按请求的显示尺寸光栅化 | 1 | 保留透明背景 | 不适用 | 未读取 | `vector.svg` |

“保留”指解码后的缓冲区是带预乘 Alpha 的 BGRA，并且 PNG/WebP 编码器会保留该 Alpha。GIF 帧会先合成到透明画布再交给查看器；SVG 会渲染到透明的 BGRA 表面。JPEG 副本会先绘制到白色背景上，因此透明像素会变成白色。参见 [Alpha 与位深](#alpha-与位深)。

### “转换为 sRGB”的含义

解码器向 `SKCodec` 提供 sRGB `SKImageInfo`，其中使用 `Bgra8888` 和 `Premul`。Skia 在生成这些像素时会把内嵌的 ICC 配置文件转换为 sRGB。应用本身不执行颜色变换，也不在像素缓冲区中保留源配置文件。

信息面板会单独从文件头读取配置文件：

- JPEG：APP2 `ICC_PROFILE` 分段，按序列顺序重新组装。
- PNG：`iCCP`，使用 zlib 解压缩。
- WebP：`ICCP` 块。

`IccProfile` 从 `desc`、`text` 或 `mluc` 标签中读取人类可读的描述，并将 `A98C`/`A98` 映射为 “Adobe RGB (1998)”，将 `sP3`/`P3` 映射为 “Display P3”。随后，`ImageMetadata.ColorSpaceDisplay` 在没有配置文件或描述中已提及 sRGB 时报告 `sRGB`，否则报告 `<profile> → sRGB`。

### AVIF

Windows x64/arm64、macOS 和 Linux 构建把 libavif 1.4.2 和 dav1d 1.5.5 打成 `airglance_avif.dll`、`libairglance_avif.dylib` 或 `libairglance_avif.so`。`LibAvifDecoder` 解析容器、解第一帧，并要求 libavif 输出 8 位预乘 BGRA；像素之后走与其他格式相同的方向和位图路径。

- `irot`/`imir` 由 libavif 的 transform 属性驱动；两者都没有时使用 EXIF 方向。映射使用 libavif 文档中的八种组合（irot 先于 imir）。
- `clap` clean aperture 在方向之前裁剪。
- 10/12 位源由 libavif 转成 8 位；信息面板报告源位深。
- Alpha 保留并预乘。
- AVIF 序列对查看器仍是静态图：只显示第一帧，`FrameCount` 报告 1。
- ICC 和 EXIF 会读取用于信息面板；与 JPEG/PNG/WebP 不同，AVIF 像素暂不从 ICC 转换到 sRGB，信息面板里的配置文件只作描述。
- `avifDecoder.imageSizeLimit` 和 `imageDimensionLimit` 限制解码像素量；超过应用上限的文件会被拒绝，而不是先分配巨量内存。
- 不支持 AVIF 输出。另存副本仍写 JPEG、PNG 或 WebP。

原生库由 `native/avif/build.ps1`（Windows x64）、`build-win-arm64.ps1`（Windows arm64，LLVM 交叉构建）或 `build.sh`（macOS/Linux）构建，不提交到 git。MSIX 清单为 x64 Windows 包声明 `.avif` 文件关联。

## 不支持的格式

| 格式 | v0.1 行为 |
| --- | --- |
| HEIC / HEIF | 不列出，也不打开 |
| RAW（CR2、CR3、NEF、ARW、DNG、RAF、ORF、RW2 等） | 不列出，也不打开 |
| 动画 AVIF（AVIF 序列） | 只把第 1 帧当作静态图显示；没有动画循环 |
| 动画 WebP / APNG | 只把第 1 帧当作静态图显示；没有动画循环 |
| 视频（MP4、MOV、WebM、MKV 等） | 不列出，也不打开 |

HEIC、HEIF、RAW 等格式是有意不支持的，而不是尚未完成的工作。HEIC 需要未随包分发的原生 HEVC 解码器；AVIF 使用 AV1，Windows x64/arm64、macOS 和 Linux 构建已经随包支持。动画 WebP 和 APNG 是受支持的容器，但查看器只使用它们的第一帧。把不受支持的文件重命名为受支持的扩展名没有用处：内容检查仍然会失败，文件会被报告为损坏。

## 确切行为

### EXIF 方向

JPEG 的方向会被应用。`ImageDecoder` 读取 `SKCodec.EncodedOrigin`，把方向 1–8 映射为像素重排，并对 5–8 交换宽度和高度。测试套件会解码全部八个测试夹具，并检查每个结果四个角的颜色。

PNG 的 `eXIf` 元数据会被读取，但其方向不会作为显示变换应用。解码只使用 `SKCodec.EncodedOrigin`；PNG 编解码器不会把来自 `eXIf` 的方向提供给这条路径。有一个细节源自元数据代码：当编解码器报告 TopLeft 时，`ImageDecoder.ReadMetadata` 会回退到文件头中的 EXIF 方向，因此对于像素未被旋转的 PNG，信息面板可能显示一个方向编号和交换后的显示尺寸。在 0.1 中，请将 PNG `eXIf` 的方向仅视为提示信息。

WebP 的 EXIF 也会为信息面板而读取。没有 WebP 方向测试夹具，因此 WebP 的显示旋转未经测量。不要假定 WebP 的 EXIF 方向会旋转图像。

BMP、GIF、ICO 和 SVG 没有 EXIF 块读取器。TIFF 方向从 TIFF 标签读取并应用到像素。

### ICC 转换

JPEG、PNG 和 WebP 的配置文件在解码过程中会转换为 sRGB。覆盖范围有所不同：

- JPEG 的 Display P3 经过像素级测试：与 `srgb.jpg` 中相同的数值颜色相比，解码后的 P3 红色通道更高、绿色通道更低，这正是把更宽色域转换为 sRGB 的预期结果。
- JPEG 中的 Adobe RGB 通过配置文件描述来测试，而不是通过像素值。
- PNG 的 Display P3 通过读取 `iCCP` 描述来测试，而不是比较像素。
- WebP 的 ICC 转换已实现，但没有测试夹具，也未经过测试验证。

缺少配置文件时会按 sRGB 处理。CMYK JPEG、特殊传递曲线和超大配置文件没有单独测试。超过 4 MiB 的 ICC 载荷和不完整的 JPEG APP2 分段集合会被元数据读取器忽略，因此信息面板没有配置文件描述；像素解码仍遵循 Skia 对该文件的处理。

### Alpha 与位深

PNG 和 WebP 的 Alpha 会被保留。测试夹具中有一个完全透明的角和一个完全不透明的中心；`alpha.png` 检查解码后的 Alpha 值，`alpha.webp` 检查 WebP 解码后的透明角。PNG 导出会保留透明度。JPEG 导出会压平到白色背景上，这一点会在一块透明 PNG 上检查。

ICO 使用相同的预乘 BGRA 路径，并且 `HasAlpha` 会为 ICO 容器设置，但没有 ICO 测试夹具。GIF 帧会在合成时处理透明度；没有专门的透明度测试夹具。TIFF 的 RGBA 透明度会保留（`alpha.tif`）。BMP 的 Alpha 既未记录也未测试。

16 位 PNG 会从 IHDR 报告为 16 位。它会被解码为 8 位 BGRA，并以 8 位显示和导出。测试断言的是所报告的位深，而不是结果像素的精度。没有 16 位导出路径。

### 动画

动画 GIF 会逐帧解码并由查看器自动播放。`ImageDecoder.DecodeAnimation` 按 GIF 的处置与混合规则合成每一帧，记录每帧延迟，并通过在长动画超出预算时降低光栅化尺寸，把总帧内存限制在 2400 万像素以内。查看器使用 Avalonia `DispatcherTimer` 循环播放这组帧。

`animated.gif` 有三帧、每帧延迟 100 ms；`ImageDecoderTests.Animated_gif_decodes_every_frame_and_delay` 检查三帧颜色和延迟，窗口测试检查显示的位图会随时间变化。旋转、另存和复制操作使用第 1 帧；动画按解码后的预览尺寸播放（较大的 GIF 在 100% 下可能偏软）。

动画 WebP 和 APNG 仍按静态图处理：这些容器不会被解码为帧集合，也没有测试。视频不支持。

### 元数据

`ExifReader` 读取信息面板使用的字段：

- 相机厂商和型号；
- 镜头型号；
- 曝光时间；
- 光圈值；
- ISO；
- 焦距；
- 曝光补偿；
- 原始日期/时间；
- 方向。

它读取 IFD0 和 Exif 子 IFD，其中子 IFD 优先。支持 RATIONAL（类型 5）和有符号 RATIONAL（类型 10）值，也支持以 SHORT（类型 3）或 SSHORT（类型 8）写成的 Pillow 风格数对。JPEG APP1 载荷上限为 1 MiB，PNG `eXIf` 载荷上限为 1 MiB，ICC 载荷上限为 4 MiB。格式错误的元数据会返回空详情，绝不会阻止图像打开。

XMP、IPTC、厂商注释以及 `eXIf` 之外的 PNG 文本块不会被读取。

## 测试夹具

所有路径都在 `tests/Assets` 下，并会被复制到测试二进制文件旁边。

| 测试夹具 | 尺寸 / 大小 | 使用方 | 验证内容 |
| --- | --- | --- | --- |
| `orientation-1.jpg` … `orientation-8.jpg` | 存储为 80 × 40；方向 5–8 显示为 40 × 80 | `ImageDecoderTests.Exif_orientations_are_applied_to_decoded_pixels` | 全部八种 EXIF 方向都会应用到像素和尺寸 |
| `orientation-6.jpg` | 存储为 80 × 40 | `ImageDecoderTests.Metadata_reports_orientation_adjusted_size` | 元数据分别报告原始尺寸和显示尺寸 |
| `display-p3.jpg` | 64 × 32 | `ImageDecoderTests.Display_p3_is_converted_to_srgb` | Display P3 JPEG 被正确命名，并转换为不同的 sRGB 像素值 |
| `display-p3.png` | 64 × 32 | `ImageDecoderTests.Png_icc_profile_is_read` | PNG 的 `iCCP` 配置文件被读取 |
| `adobe-rgb.jpg` | 64 × 32 | `ImageDecoderTests.Adobe_rgb_profile_is_named` | 紧凑的 `A98C` 描述变为 “Adobe RGB (1998)” |
| `srgb.jpg` | 64 × 32 | `ImageDecoderTests.Display_p3_is_converted_to_srgb` | 用于 P3 转换比较的 sRGB 参考图像 |
| `alpha.png` | 48 × 32 | `ImageDecoderTests.Png_alpha_is_preserved_and_premultiplied`、`ImageExporterTests.Jpeg_flattens_transparency_to_white`、`ImageExporterTests.Png_keeps_transparency` | PNG 的 Alpha 被解码、在 PNG 导出时保留，并在 JPEG 导出时被压平 |
| `alpha.webp` | 48 × 32 | `ImageDecoderTests.Webp_decodes` | WebP 能解码并保留透明角 |
| `rgb-lzw.tif` | 80 × 40 | `ImageDecoderTests.Tiff_decodes_rgb_and_lzw`、`Tiff_scaled_decode_respects_the_requested_box` | LZW RGB TIFF、颜色通道和缩放解码 |
| `alpha.tif` | 48 × 32 | `ImageDecoderTests.Tiff_alpha_is_preserved` | TIFF Alpha 保留 |
| `orientation-6.tif` | 存储 80 × 40；显示 40 × 80 | `ImageDecoderTests.Tiff_orientation_is_applied`、`Tiff_raw_decode_carries_the_orientation_for_the_tile_writer` | TIFF 方向应用并传给瓦片路径 |
| `animated.gif` | 40 × 40，3 帧 | `ImageDecoderTests.Animated_gif_decodes_every_frame_and_delay`、`MainWindowTests.Animated_gif_is_presented_as_an_animation` | 验证 GIF 的帧、延迟和播放 |
| `vector.svg` | 64 × 48 | `ImageDecoderTests.Svg_renders_at_its_intrinsic_size`、`Svg_scaled_decode_keeps_the_vector_aspect_ratio` | 验证 SVG 在固有尺寸和缩放尺寸下的光栅化 |
| `gradient-16bit.png` | 64 × 32 | `ImageDecoderTests.Sixteen_bit_png_reports_its_source_bit_depth` | 报告 PNG 源位深为 16 |
| `sample.avif` | 1440 × 900 | `ImageDecoderTests.Avif_decodes_a_still_image`、`Avif_preview_respects_the_requested_box` | AVIF 静态解码、中心颜色和预览缩放 |
| `alpha.avif` | 80 × 80 | `ImageDecoderTests.Avif_alpha_is_preserved` | AVIF Alpha 保留并预乘 |
| `irot.avif` | 存储 12 × 34；显示 34 × 12 | `ImageDecoderTests.Avif_irot_is_applied_and_bit_depth_is_reported` | `irot` 方向和 10 位源报告 |
| `icc-exif.avif` | 403 × 302 | `ImageDecoderTests.Avif_icc_and_exif_are_read` | AVIF ICC 描述和 EXIF 相机字段 |
| `animated.avif` | 150 × 150，5 帧 | `ImageDecoderTests.Avif_animation_uses_the_first_frame` | 动画 AVIF 只显示第一帧 |
| `white-1x1.avif` | 1 × 1 | `ImageDecoderTests.Avif_one_pixel_image_decodes` | 最小 AVIF 尺寸 |
| `corrupt.avif` | 100 字节（截断） | `ImageDecoderTests.Avif_corrupt_file_reports_corrupt` | 截断 AVIF 报告损坏 |
| `corrupt.jpg` | 18 字节 | `ImageDecoderTests.Corrupt_file_reports_corrupt` | 受支持的扩展名搭配无效内容时会报告图像损坏 |
| `exif-full.jpg` | 120 × 80 | `ImageDecoderTests.Exif_fields_are_read` | IFD0/子 IFD 字段、有理数值和 Pillow 风格数对 |
| `profiles/DisplayP3.icc` | 480 字节 | 测试资产生成以及 `display-p3.*` | Display P3 配置文件测试夹具 |
| `profiles/AdobeCompat.icc` | 374 字节 | 测试资产生成以及 `adobe-rgb.jpg` | 兼容 Adobe RGB 的配置文件测试夹具 |

这两个 ICC 文件来自 saucecontrol/Compact-ICC-Profiles，采用 CC0 许可。它们只是测试夹具，不随应用一起分发。参见 [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md)。

### TIFF 夹具

每种样本布局各一张 quadrants 图，由 `TiffPreviewDecoderTests` 和 `TiffRegionReaderTests` 共用。基线用的 `rgb-lzw.tif`、`alpha.tif` 和 `orientation-6.tif` 已在上表中。

| 夹具 | 证明的内容 |
| --- | --- |
| `cmyk-quadrants.tif`、`cmyk-quadrants-jpeg.tif`、`cmyk-large-single-strip.tif` | CMYK 转换、JPEG 压缩的 CMYK，以及单条带大于条带缓存的情况 |
| `planar-rgb.tif`、`planar-tiled-rgb.tif`、`planar-cmyk.tif`、`planar16-rgb.tif`、`planar16-cmyk.tif`、`planar16-rgba.tif` | 平面（planar）配置，条带与瓦片两种排布，8 位与 16 位，含与不含 Alpha |
| `palette-quadrants.tif`、`palette16-quadrants.tif` | 8 位与 16 位调色板颜色表 |
| `ycbcr-quadrants.tif`、`ycbcr-quadrants-jpeg.tif`、`ycbcr-subsampled.tif`、`ycbcr-subsampled-lzw.tif`、`ycbcr-subsampled-deflate.tif` | YCbCr 的三种形态：未压缩、JPEG 压缩、子采样并配合 LZW 或 Deflate |
| `int16-quadrants.tif`、`float16-quadrants.tif`、`float32-quadrants.tif` | 有符号 16 位样本，以及半精度/单精度浮点样本 |
| `rgba-quadrants.tif`、`rgba16-quadrants.tif` | 8 位与 16 位 RGBA |
| `subifd-levels.tif` | 优先使用降采样金字塔层，而不是全分辨率目录 |

## 覆盖缺口

该矩阵描述的是已测试的行为，并不声称每种受支持的格式都有测试夹具。以下各点未被当前测试套件测量：

- BMP 和 ICO 没有测试夹具，也没有测试。它们的支持仅依靠 `SKCodec` 路径和扩展名校验。
- AVIF 的 `clap` 已用静图夹具测试；`imir` 及 `irot`/`imir` 组合按 libavif 文档映射，但没有夹具测试。ICC 只为信息面板读取，AVIF 像素暂不转换到 sRGB。HDR/gain map 和 AVIF 编码不支持。
- AVIF 测试和夹具只在原生解码器存在的平台运行。Windows CI 构建 x64 和 arm64 解码器；macOS 和 Linux CI 为当前架构构建。Windows arm64 DLL 是交叉构建并做了 PE 校验，但没有记录 Windows arm64 硬件运行结果。
- PNG `eXIf` 的方向不会应用到像素，也没有 PNG 方向测试夹具来端到端地证明这一点。
- WebP 的 ICC 和 WebP 的 EXIF 没有测试夹具覆盖。
- ICO 的 Alpha、GIF 的透明度和 BMP 的 Alpha 没有专门的测试夹具覆盖。
- TIFF 的多页文件、BigTIFF 和 TIFF 内 12 位 JPEG 没有夹具覆盖。条带式、瓦片式、金字塔、平面、CMYK、调色板、YCbCr、浮点以及有符号/无符号 16 位路径都有覆盖，对应夹具见上方分组表。
- CMYK JPEG、渐进式 JPEG 和超大 JPEG 没有单独测试。
- 16 位 PNG 测试检查的是所报告的位深，而不是 8 位结果的精度。
- 动画 WebP 和 APNG 不会按动画解码。
- 格式测试在 `build.yml` 中的每个主机上运行（Windows、Linux 和 macOS），但没有按操作系统分开的测试夹具或预期结果矩阵。
- `tools/testdata/photo-100mp.jpg` 和 `photo-24mp.jpg` 是基准测试图像，不是格式测试夹具，并且被 gitignore 忽略。

## 运行格式测试

```text
dotnet test AirGlance.sln -c Release
```

只运行解码器测试：

```text
dotnet test tests/AirGlance.Core.Tests/AirGlance.Core.Tests.csproj -c Release --filter FullyQualifiedName~ImageDecoderTests
```

测试项目会把 `tests/Assets` 复制到各自的输出目录，因此测试夹具不需要单独的安装步骤。仓库没有捆绑 .NET SDK；请使用 `global.json` 中的 SDK 版本。

## 相关文档

- [架构](architecture.zh-CN.md)
- [发布 AirGlance 0.7.0](shipping.zh-CN.md)
- [更新日志](../CHANGELOG.md)
- [README](../README.zh-CN.md)
