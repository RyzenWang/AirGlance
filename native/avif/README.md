# AVIF decoder

This directory builds the native bridge used by `LibAvifDecoder`:

| RID | Output |
| --- | --- |
| `win-x64` | `airglance_avif.dll` |
| `osx-x64`, `osx-arm64` | `libairglance_avif.dylib` |
| `linux-x64`, `linux-arm64` | `libairglance_avif.so` |

It statically links [libavif](https://github.com/AOMediaCodec/libavif) 1.4.2 and
[dav1d](https://code.videolan.org/videolan/dav1d) 1.5.5. On Windows the static
MSVC CRT is used, so the portable zip and MSIX do not need a VC++ redistributable.
On macOS and Linux the static libraries are built with PIC and linked into one
shared library.

The shim exposes a tiny C ABI:

- `airglance_avif_probe()` / `airglance_avif_version()`;
- `airglance_avif_info()` parses the container and copies ICC/EXIF sizes;
- `airglance_avif_decode()` decodes the first frame into caller-owned 8-bit
  premultiplied BGRA.

`avifImage`/`avifDecoder` are never marshalled across the P/Invoke boundary.

## Windows x64

Prerequisites:

- Visual Studio 2022 or 2026 with the C++ desktop workload, Windows SDK;
- CMake and Ninja from Visual Studio (or on `PATH`);
- Python 3 and Meson 1.7.2:
  `python -m pip install --user meson==1.7.2`;
- NASM 2.16+ (optional; without it dav1d is built without x86 SIMD).

```powershell
powershell -ExecutionPolicy Bypass -File native/avif/build.ps1
```

The script downloads the pinned source archives into `native/avif/.cache`,
verifies their SHA-256, builds dav1d and libavif as static libraries, and writes
`native/avif/bin/win-x64/airglance_avif.dll` plus `build-info.txt`.

## macOS and Linux

Prerequisites:

- CMake 3.22+, Ninja, a C compiler (Xcode clang or GCC/Clang);
- Meson 1.7.2 (`python3 -m pip install --user meson==1.7.2`) or a distribution
  package;
- NASM on x64 for dav1d's x86 assembly (arm64 uses the compiler's assembler).

```bash
bash native/avif/build.sh
```

The script infers `osx-arm64`, `osx-x64`, `linux-arm64` or `linux-x64` from the
host and writes `native/avif/bin/<rid>/libairglance_avif.dylib` or
`libairglance_avif.so`. It verifies the host OS and architecture; cross-building
between OS families or architectures is not supported. Pass a RID to check it
explicitly:

```bash
bash native/avif/build.sh linux-x64
```

## Packaging and tests

`Directory.Build.targets` copies the matching library next to `AirGlance.exe`,
the test executables and the publish output. `packaging/scripts/pack.ps1`
builds it for Windows; `packaging/scripts/pack.sh` does the same for macOS and
Linux. A build without the native library still compiles, but `.avif` stays
hidden and the Windows AVIF test fails with a build instruction. When testing a
non-default host architecture, set `AIRGLANCE_AVIF_RID` (for example
`osx-x64`) so the test output receives the matching library.

## Scope

- Windows x64, macOS x64/arm64 and Linux x64/arm64. Windows arm64 has no ARM64
  native toolchain in this repository yet and hides `.avif`.
- Still images and the first frame of an AVIF sequence. No animation playback.
- No AVIF encoding.
- ICC/EXIF are exposed as metadata; ICC-to-sRGB pixel conversion is not applied.
- HDR and gain maps are out of scope.

## Licence

libavif and dav1d are BSD-2-Clause. The full notice and copyrights are in
[THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md).
