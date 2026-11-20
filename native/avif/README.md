# AVIF decoder (Windows x64)

This directory builds `airglance_avif.dll`, the native bridge used by
`LibAvifDecoder`. It statically links [libavif](https://github.com/AOMediaCodec/libavif)
1.4.2 and [dav1d](https://code.videolan.org/videolan/dav1d) 1.5.5 with the
static MSVC CRT, so the portable zip and MSIX do not need a VC++ redistributable.

The shim exposes a tiny C ABI:

- `airglance_avif_probe()` / `airglance_avif_version()`;
- `airglance_avif_info()` parses the container and copies ICC/EXIF sizes;
- `airglance_avif_decode()` decodes the first frame into caller-owned 8-bit
  premultiplied BGRA.

`avifImage`/`avifDecoder` are never marshalled across the P/Invoke boundary.

## Build

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
verifies their SHA-256, builds dav1d and libavif as static libraries, and writes:

```text
native/avif/bin/win-x64/airglance_avif.dll
native/avif/bin/win-x64/build-info.txt
```

`Directory.Build.targets` copies the DLL next to `AirGlance.exe`, the test
executables, and the publish output. `packaging/scripts/pack.ps1` builds it
automatically for `win-x64`. `dotnet build` or `dotnet test` without a native
build still compile, but `.avif` stays hidden and the Windows AVIF test fails
with a build instruction.

To rebuild from local checkouts:

```powershell
powershell -ExecutionPolicy Bypass -File native/avif/build.ps1 `
  -LibavifSource F:\src\libavif-1.4.2 `
  -Dav1dSource F:\src\dav1d-1.5.5 `
  -NasmDirectory "C:\Program Files\NASM"
```

## Scope

- Windows x64 only. `win-arm64`, macOS and Linux builds do not ship the DLL yet,
  so `SupportedFormats` hides `.avif` there.
- Still images and the first frame of an AVIF sequence. No animation playback.
- No AVIF encoding.
- ICC/EXIF are exposed as metadata; ICC-to-sRGB pixel conversion is not applied.
- HDR and gain maps are out of scope.

## Licence

libavif and dav1d are BSD-2-Clause. The full notice and copyrights are in
[THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md).
