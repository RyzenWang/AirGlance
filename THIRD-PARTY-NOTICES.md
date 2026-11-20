# Third-party notices

AirGlance is licensed under the MIT License; see [LICENSE](LICENSE).

AirGlance uses the components below. Full license texts are available at the linked project pages. No third-party source is modified by this repository.

## Runtime and UI dependencies

| Component | Version | License | Project and license |
| --- | --- | --- | --- |
| Avalonia | 11.3.22 | MIT | [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia), [licence.md](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) |
| Avalonia.Fonts.Inter (Inter font) | 11.3.22 | SIL Open Font License 1.1 | [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia), [rsms/inter](https://github.com/rsms/inter), [OFL.txt](https://github.com/rsms/inter/blob/master/LICENSE.txt) |
| SkiaSharp | 4.150.1 | MIT | [mono/SkiaSharp](https://github.com/mono/SkiaSharp), [LICENSE.md](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md) |
| Svg.Skia (and Svg.Model, Svg.Custom, Svg.Animation, Svg.SceneGraph) | 5.2.3 | MIT | [wieslawsoltes/Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia), [LICENSE](https://github.com/wieslawsoltes/Svg.Skia/blob/main/LICENSE) |
| ShimSkiaSharp | 5.2.3 | MIT | [wieslawsoltes/Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia), [LICENSE](https://github.com/wieslawsoltes/Svg.Skia/blob/main/LICENSE) |
| BitMiracle.LibTiff.NET | 2.4.660 | BSD-3-Clause | [BitMiracle/libtiff.net](https://github.com/BitMiracle/libtiff.net), [LICENSE](https://bitmiracle.github.io/libtiff.net/help/articles/license.html) |
| libavif | 1.4.2 | BSD-2-Clause | [AOMediaCodec/libavif](https://github.com/AOMediaCodec/libavif), [LICENSE](https://github.com/AOMediaCodec/libavif/blob/main/LICENSE) |
| dav1d | 1.5.5 | BSD-2-Clause | [videolan/dav1d](https://code.videolan.org/videolan/dav1d), [COPYING](https://code.videolan.org/videolan/dav1d/-/blob/master/COPYING) |
| HarfBuzzSharp | 14.2.0 | MIT | [mono/SkiaSharp](https://github.com/mono/SkiaSharp), [LICENSE.md](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md) |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | [CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet), [License.md](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) |
| .NET runtime and libraries | 10 | MIT | [dotnet/runtime](https://github.com/dotnet/runtime), [LICENSE.TXT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |

HarfBuzzSharp is a transitive dependency of SkiaSharp. Its native HarfBuzz library is MIT licensed; see [harfbuzz/harfbuzz](https://github.com/harfbuzz/harfbuzz).

### Native libraries

SkiaSharp and HarfBuzzSharp ship native binaries for each supported platform. Those binaries include Skia (BSD-3-Clause, [google/skia](https://github.com/google/skia)) and HarfBuzz (MIT, [harfbuzz/harfbuzz](https://github.com/harfbuzz/harfbuzz)). The .NET runtime and Avalonia native assets carry their own notices from their projects.

The Windows x64, macOS and Linux builds also bundle a native AVIF decoder (`airglance_avif.dll`, `libairglance_avif.dylib` or `libairglance_avif.so`), built from libavif 1.4.2 and dav1d 1.5.5. Both libraries are BSD-2-Clause. The required notice follows; it applies to libavif (Copyright 2019 Joe Drago) and dav1d (Copyright (c) 2018-2025 VideoLAN and dav1d authors):

```text
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

The AVIF test fixtures under `tests/Assets` (`sample.avif`, `alpha.avif`, `irot.avif`, `icc-exif.avif`, `animated.avif`, `white-1x1.avif`) are derived from the libavif test data and are distributed under the same BSD-2-Clause licence. They are test-only and are not shipped with the application.

## Test-only dependencies

These packages are used by the test projects and are not part of the application binaries.

| Component | Version | License | Project and license |
| --- | --- | --- | --- |
| xUnit | 2.9.3 | Apache-2.0 | [xunit/xunit](https://github.com/xunit/xunit), [LICENSE](https://github.com/xunit/xunit/blob/main/LICENSE) |
| Avalonia.Headless | 11.3.22 | MIT | [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia), [licence.md](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) |

The test projects also use `xunit.runner.visualstudio` and the Microsoft test SDK. They are test harness packages and are not distributed with AirGlance.

## Test ICC profiles

`tests/Assets/profiles/DisplayP3.icc` and `tests/Assets/profiles/AdobeCompat.icc` are from [saucecontrol/Compact-ICC-Profiles](https://github.com/saucecontrol/Compact-ICC-Profiles), released under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/).

They are test fixtures only. They are not compiled into AirGlance, are not shipped in the application, and are not runtime dependencies. The generated test images in `tests/Assets` use these profiles to exercise ICC reading and conversion. `tools/make-test-assets.py` records the source filenames.

## Application assets

The AirGlance icon, test images and other files under `src/AirGlance.App/Assets`, `packaging/` and `tests/Assets` are part of this repository. The test images and profiles are not shipped with the application.
