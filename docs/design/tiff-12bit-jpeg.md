# 12-bit JPEG-in-TIFF 调研

> 状态：调研完成，未实现。当前 12-bit JPEG 仍走 Windows libvips 回退。

## 当前依赖为什么不行

### LibTiff.NET

- `Tiff.Open` 能读标签，但读写 12-bit JPEG 时 libtiff 的 JPEG codec 返回
  `Compression algorithm does not support random access`，写出的文件为 0 字节。
- `BitMiracle.LibTiff.NET` 的 JPEG codec 只覆盖 8-bit 样本，不能把 12-bit 交给它解。

### NetVips.Native.win-x64 8.18.7

- 该包只有 `libvips-42.dll`，JPEG 是静态链接的 **mozjpeg 0826579**；
- `versions.json` / `THIRD-PARTY-NOTICES.md` 没有独立 libjpeg DLL；
- mozjpeg 这个构建是 8-bit，无法通过 P/Invoke 直接拿到 12-bit 解码器；
- 因此 libvips 回退本身对 12-bit JPEG 也不可靠。

## 上游可行性

libjpeg-turbo 3.x 的源码构建支持 8/12/16-bit：CMake 里包含
`jcapistd-12.c`、`jccolor-12.c`、`jcdctmgr-12.c` 等 wrapper，测试也覆盖
`precision 12`。但需要自己构建 Windows x64 DLL：

- CMake ≥ 3.15；
- MSVC 或 MinGW；
- NASM/YASM（x86-64 SIMD）；
- 生成 `jpeg{62,7,8}.dll`，再用 TurboJPEG/libjpeg API 解码。

参考：
- <https://raw.githubusercontent.com/libjpeg-turbo/libjpeg-turbo/main/CMakeLists.txt>
- <https://raw.githubusercontent.com/libjpeg-turbo/libjpeg-turbo/main/BUILDING.md>

## 接入成本

1. 从 TIFF 读取 JPEG strip：raw IFD 的 `StripOffsets` / `StripByteCounts`，
   compression=7；这部分已有 raw IFD parser。
2. P/Invoke `turbojpeg` 或 libjpeg API，解码 12-bit 到 16-bit 样本。
3. 处理 RGB / YCbCr / CMYK、subsampling、Adobe APP14、颜色转换。
4. 12→8-bit 缩放；preview/region/cache 都要接。
5. NativeAOT 下额外 DLL、平台打包、许可证（IJG/BSD/zlib）。
6. 测试文件需要 `cjpeg -precision 12` 生成，现有 LibTiff writer 生成不了。

## 建议

- 短期保留 libvips 回退；在非 Windows 上对 12-bit JPEG 返回明确错误。
- 若真实用户文件里 12-bit JPEG 占比可观，再单独立项：
  - 构建 libjpeg-turbo 12-bit DLL；
  - 先做 preview 的 strip 解码，再做 region；
  - 加一个 `-precision 12` fixture。
- 不建议把 12-bit 解码塞进 LibTiff.NET 的托管 JPEG codec；工作量大于收益。
