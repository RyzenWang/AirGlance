# AVIF 方案评估：libavif + dav1d

> 状态：评估完成，未实现。分支 `spike/avif-libavif`。

## 结论

**可行，推荐自建 libavif + dav1d**，不建议继续依赖 libvips。

POC 在 Windows x64 上实际编译并解码成功：

- libavif **1.4.2** + dav1d **1.5.3**，MSVC 19.44；
- `avif.dll` 206 KB + `dav1d.dll` 526 KB，压缩后约 **381 KB**；
- 1440 × 900 AVIF 解码约 **90 ms**（含进程启动）；
- alpha 通道完全保留（透明角落 0，中心 227,86,59,255）；
- 10-bit AVIF 正常解码到 8-bit RGBA；
- 许可证是 BSD-2-Clause，适合随应用分发。

对比：libvips 移除让包从 27.72 MB 降到 20.01 MB；恢复 AVIF 只需增加约 **0.4 MB**，净收益仍有约 7.3 MB。

## POC 环境

- libavif v1.4.2 源码；
- dav1d 1.5.3 源码；
- MSVC Build Tools 2022 + CMake + Ninja + Meson；
- 未安装 NASM，因此 POC 的 dav1d 关闭了 x86 SIMD（`-Denable_asm=false`）；
- libavif 关闭 libyuv（`-DAVIF_LIBYUV=OFF`），使用内置 RGB 转换。

构建结果：

```text
libavif/build2/avif.dll    206,848 B
prefix/bin/dav1d.dll        539,136 B
两个 DLL 的 zip             381 KB
```

解码验证：

```text
decoded 1440x900 depth=8
rgba first=241,231,219,255 last=212,206,194,255 center=137,139,138,255

decoded 48x32 depth=8
rgba first=0,0,0,0 last=0,0,0,0 center=227,86,59,255

decoded 48x32 depth=10
rgba first=0,0,0,0 last=0,0,0,0 center=227,86,59,255
```

## 可选依赖对比

| 方案 | 体积 | 平台 | 编码器 | 评价 |
| --- | --- | --- | --- | --- |
| 自建 libavif + dav1d | 约 0.4 MB 压缩 | 可做全平台 | dav1d | **推荐**，体积小、BSD、可控 |
| `Starward.Codec` | 29.5 MB nupkg | Windows x86/x64/ARM64 | libaom | 含 JPEG XL 等大量不需要的库 |
| `EasyImageSharp.Codecs` | 4.23 MB nupkg | Windows x64 | libaom | 比自建大得多，仍只 Windows |
| `LibavifSharp` | 19 KB 绑定 | 取决于原生库 | 取决于原生库 | 只提供 C# 绑定，仍要自己打包原生库 |
| 官方 `avifdec.exe` 子进程 | 12 MB exe | 官方只发 exe | 静态 | 每次解码起进程/临时文件，不适合预览 |
| 继续用 libvips | 约 7.7 MB | Windows | mozjpeg/AOM | 已移除，且带 LGPL 依赖 |

## 推荐接入方式

### 1. 原生层

把 libavif + dav1d 编译成每个 RID 的原生库：

- `win-x64` / `win-arm64`；
- `osx-x64` / `osx-arm64`；
- `linux-x64` / `linux-arm64`。

产物：

- `avif.dll` / `libavif.so` / `libavif.dylib`；
- `dav1d.dll` / `libdav1d.so` / `libdav1d.dylib`。

生产构建应尽量打开 NASM/NEON，POC 的 no-asm 数字只用于验证功能。

### 2. C shim

不要让 C# 直接 marhsal `avifDecoder` / `avifImage` 这些大结构体。加一个很小的 C shim：

```c
int airglance_avif_decode(
    const char* path,
    uint8_t** rgba,
    int* width,
    int* height,
    int* stride,
    int* has_alpha,
    char* error,
    int error_capacity);

void airglance_avif_free(void* pixels);
```

C shim 内部负责：

- `avifDecoderCreate` / `avifDecoderSetIOFile` / `avifDecoderParse`；
- 只取第一帧（`avifDecoderNextImage`）；
- `avifRGBImageSetDefaults` + `AVIF_RGB_FORMAT_RGBA` + depth 8；
- `avifImageYUVToRGB`；
- 错误字符串返回。

C# 侧只用 `LibraryImport` 导入这两个函数，NativeAOT 友好，ABI 稳定。

### 3. ImageDecoder

新增 `LibAvifDecoder`：

- 读取文件 → 得到 RGBA；
- 应用 AVIF 的 irot/imir 方向（libavif 的 `avifImage` 已带 transform，或读属性）；
- 按 `maxWidth/maxHeight` 用 Skia 缩放；
- 限制最大像素数，避免超大 AVIF 直接分配；
- alpha 保留，JPEG 导出时由现有逻辑压白。

### 4. 测试

至少覆盖：

- 普通 8-bit AVIF；
- alpha AVIF（透明角 + 中心颜色）；
- 10-bit / 12-bit AVIF 降到 8-bit；
- 4:2:0 / 4:4:4；
- 损坏文件、截断文件；
- 超大尺寸限制；
- 动画 AVIF 只取第一帧；
- Windows/macOS/Linux 的 DLL/dylib/so 加载。

## 风险与成本

| 项 | 评估 |
| --- | --- |
| 原生构建/CI | 每个 RID 一套 libavif+dav1d；CMake + Meson，工作量中等 |
| AOT P/Invoke | 简单 C shim 即可，风险低 |
| 包体 | +约 0.4 MB（no-asm）；开 SIMD 预计 +1 MB 内 |
| 性能 | dav1d 是主流 AV1 解码器；开 SIMD 后与 libvips 同量级 |
| 大图内存 | libavif 全图解码，需加像素上限；AVIF 通常远小于 TIFF |
| 色彩管理 | libavif 做 YUV→RGB；ICC/HDR/gain map 需要单独处理或先忽略 |
| 动画 | 只取第一帧，和之前行为一致 |
| 签名/公证 | macOS dylib 需随 app 签名；Windows 无额外要求 |
| 许可证 | libavif BSD-2、dav1d BSD-2，需加入 THIRD-PARTY-NOTICES |

## 工作量估算

- Windows x64 POC 接入（C shim + ImageDecoder + 测试）：**2–3 天**；
- 全平台原生构建 + CI + 签名：**1–2 周**；
- 若只先恢复 Windows AVIF，可控制在 **3–4 天**。

## 建议

1. 先做 **Windows x64** 的 libavif + dav1d 接入，恢复 AVIF 格式；
2. 同时把 C shim 和 native 构建脚本做成可复用；
3. 验证 alpha/10-bit/大图限制后，再扩到 macOS/Linux；
4. 不要回到 libvips。

## 附：POC 关键命令

```text
# dav1d
meson setup build --buildtype=release --default-library=shared \
  -Denable_asm=false -Denable_tools=false -Denable_tests=false --prefix=... 
meson compile -C build
meson install -C build

# libavif
cmake -S libavif -B build -G Ninja -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=ON -DAVIF_CODEC_DAV1D=SYSTEM -DAVIF_LIBYUV=OFF \
  -DAVIF_BUILD_APPS=OFF -DAVIF_BUILD_TESTS=OFF \
  -DDAV1D_INCLUDE_DIR=.../include -DDAV1D_LIBRARY=.../lib/dav1d.lib
cmake --build build
```
