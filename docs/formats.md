# Format support

**English** · [简体中文](formats.zh-CN.md)

AirGlance uses two checks before an image reaches the screen:

1. The file extension must be on the supported list (`SupportedFormats`). This decides whether the file appears in a folder listing and whether the command line or drop handler accepts it.
2. Ordinary raster formats decode with SkiaSharp's `SKCodec`; TIFF uses LibTiff, SVG uses Svg.Skia, and AVIF uses a bundled libavif + dav1d decoder on Windows x64, macOS and Linux. A supported extension with unreadable content is reported as corrupt, not unsupported.

A file with an unsupported extension is refused even if its content happens to be a supported image. Format names shown in the info panel come from the codec when available and fall back to the extension. `SupportedFormats` hides `.avif` when the native AVIF decoder is not present, so a build without it never advertises a format it cannot open.

## Supported formats

| Format | Extensions | Decoded as | Frames | Alpha | EXIF orientation | ICC profile | Test evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| JPEG | `.jpg`, `.jpeg`, `.jfif` | 8-bit BGRA output; 8-bit reported | 1 | No; saved JPEG copies are flattened to white | Applied, EXIF 1–8 | Read from APP2 and converted to sRGB | `orientation-1..8.jpg`, `display-p3.jpg`, `adobe-rgb.jpg`, `srgb.jpg`, `exif-full.jpg` |
| PNG | `.png` | BGRA output; source bit depth reported from IHDR (8 or 16 in the fixtures) | 1 | Preserved | Read from `eXIf` for the info panel; not applied to pixels | Read from `iCCP` and converted to sRGB | `alpha.png`, `display-p3.png`, `gradient-16bit.png` |
| WebP | `.webp` | 8-bit BGRA output | 1 | Preserved | EXIF read for the info panel; no display-orientation fixture | Read from `ICCP` and converted to sRGB | `alpha.webp` |
| AVIF | `.avif` | 8-bit BGRA output from libavif + dav1d; source depth 8/10/12 reported | 1 (first frame) | Preserved, premultiplied | Applied from `irot`/`imir`, falling back to EXIF orientation | Read from the AVIF ICC item for the info panel; pixels are not colour-managed from ICC yet | `sample.avif`, `alpha.avif`, `irot.avif`, `icc-exif.avif`, `animated.avif`, `white-1x1.avif` |
| BMP | `.bmp` | Decoded by `SKCodec` | 1 | Not test-covered | Not read | Not read | No fixture |
| GIF | `.gif` | 8-bit BGRA output; every frame composited with GIF disposal rules | All frames | Carried through the composited output; no dedicated transparency fixture | Not read | Not read | `animated.gif` |
| ICO | `.ico` | One image selected by the codec | 1 | Carried through the BGRA decode path; not fixture-tested | Not read | Not read | No fixture |
| TIFF | `.tif`, `.tiff` | First page, decoded to RGBA by LibTiff.NET; LZW, Deflate and JPEG-compressed fixtures | 1 (first page) | Preserved | Applied from the TIFF orientation tag | Tag read for the info panel; pixels are not colour-managed from TIFF ICC | `rgb-lzw.tif`, `alpha.tif`, `orientation-6.tif` |
| SVG | `.svg` | Rasterized by Svg.Skia at the requested display size | 1 | Transparent background preserved | Not applicable | Not read | `vector.svg` |

"Preserved" means the decoded buffer is BGRA with premultiplied alpha and the PNG/WebP encoders keep that alpha. GIF frames are composited into a transparent canvas before they reach the viewer; SVG is rendered to a transparent BGRA surface. A JPEG copy is first drawn onto white, so transparent pixels become white. See [alpha and bit depth](#alpha-and-bit-depth).

### What "converted to sRGB" means

The decoder gives `SKCodec` an sRGB `SKImageInfo` with `Bgra8888` and `Premul`. Skia converts an embedded ICC profile to sRGB while producing those pixels. The app does not run its own colour transform and does not keep the source profile in the pixel buffer.

The info panel reads the profile separately from the file header:

- JPEG: APP2 `ICC_PROFILE` segments, reassembled in sequence order.
- PNG: `iCCP`, decompressed with zlib.
- WebP: `ICCP` chunk.

`IccProfile` reads a human-readable description from `desc`, `text` or `mluc` tags and maps `A98C`/`A98` to "Adobe RGB (1998)" and `sP3`/`P3` to "Display P3". `ImageMetadata.ColorSpaceDisplay` then reports `sRGB` when there is no profile or the description already mentions sRGB, and `<profile> → sRGB` otherwise.

### AVIF

The Windows x64, macOS and Linux builds bundle libavif 1.4.2 and dav1d 1.5.5 as `airglance_avif.dll`, `libairglance_avif.dylib` or `libairglance_avif.so`. `LibAvifDecoder` parses the container, decodes the first frame, and asks libavif for 8-bit premultiplied BGRA. The pixels then go through the same orientation and bitmap path as the other formats.

- `irot` and `imir` are applied from libavif's transform properties; if neither is present, the EXIF orientation is used. The mapping is the same eight-way composition libavif documents (irot before imir).
- A `clap` clean aperture is cropped before orientation.
- 10-bit and 12-bit sources are converted to 8-bit by libavif; the source depth is reported in the info panel.
- Alpha is preserved and premultiplied.
- AVIF sequences are still images to the viewer: the first frame is shown and `FrameCount` is reported as 1.
- ICC and EXIF payloads are read for the info panel. Unlike JPEG/PNG/WebP, AVIF pixels are not colour-managed from ICC to sRGB yet; treat the info panel profile as descriptive.
- `avifDecoder.imageSizeLimit` and `imageDimensionLimit` bound the decoded pixel count. The app rejects files above its own limit instead of attempting a huge allocation.
- AVIF output is not supported. Save copy still writes JPEG, PNG or WebP.

The native library is built by `native/avif/build.ps1` (Windows) or `native/avif/build.sh` (macOS/Linux) and is not committed. Windows arm64 does not contain it yet, so `SupportedFormats` hides `.avif` there. The MSIX manifest declares the `.avif` file association for the x64 Windows package.

## Not supported

| Format | v0.1 behavior |
| --- | --- |
| HEIC / HEIF | Not listed and not opened |
| RAW (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2 and others) | Not listed and not opened |
| Animated AVIF (AVIF sequences) | First frame is shown as a still image; no animation loop |
| Animated WebP / APNG | First frame is shown as a still image; no animation loop |
| Video (MP4, MOV, WebM, MKV and others) | Not listed and not opened |

HEIC, HEIF and RAW are deliberately absent, not pending work. HEIC needs a native HEVC decoder that is not bundled; AVIF uses AV1 and is now bundled for Windows x64, macOS and Linux. Animated WebP and APNG are supported containers, but the viewer only uses their first frame. Renaming an unsupported file to a supported extension does not help: the content check still fails and the file is reported as corrupt.

## Exact behavior

### EXIF orientation

JPEG orientation is applied. `ImageDecoder` reads `SKCodec.EncodedOrigin`, maps orientations 1–8 to a pixel remap, and swaps width and height for 5–8. The test suite decodes all eight fixtures and checks the four corner colours of each result.

PNG `eXIf` metadata is read but its orientation is not applied as a display transform. Decoding only uses `SKCodec.EncodedOrigin`; the PNG codec does not provide an orientation from `eXIf` to this path. One detail follows from the metadata code: `ImageDecoder.ReadMetadata` falls back to the header's EXIF orientation when the codec reports TopLeft, so the info panel can show an orientation number and swapped display dimensions for a PNG whose pixels were not rotated. Treat PNG `eXIf` orientation as informational only in 0.1.

WebP EXIF is also read for the info panel. There is no WebP orientation fixture, so WebP display rotation is not measured. Do not assume that a WebP EXIF orientation rotates the image.

BMP, GIF, ICO and SVG have no EXIF block reader. TIFF orientation is read from the TIFF tag and applied to pixels.

### ICC conversion

JPEG, PNG and WebP profiles are converted to sRGB during decode. Coverage differs:

- JPEG Display P3 is pixel-tested: the decoded P3 red channel is higher and the green channel is lower than the same numeric colour in `srgb.jpg`, which is the expected result of converting the wider space to sRGB.
- Adobe RGB in JPEG is tested by profile description, not by pixel values.
- PNG Display P3 is tested by reading the `iCCP` description, not by comparing pixels.
- WebP ICC conversion is implemented but has no fixture and is not test-verified.

A missing profile is treated as sRGB. CMYK JPEG, exotic transfer curves and very large profiles are not separately tested. ICC payloads over 4 MiB and incomplete JPEG APP2 segment sets are ignored by the metadata reader, so the info panel has no profile description; pixel decoding still follows Skia's handling of the file.

### Alpha and bit depth

PNG and WebP alpha are preserved. The test fixtures have a fully transparent corner and a fully opaque centre; `alpha.png` checks the decoded alpha values, and `alpha.webp` checks the transparent corner after WebP decoding. PNG export keeps transparency. JPEG export flattens onto white, which is checked on a transparent PNG.

ICO uses the same premultiplied BGRA path and `HasAlpha` is set for the ICO container, but there is no ICO fixture. GIF frames are composited with transparency; a dedicated transparency fixture is not included. TIFF RGBA alpha is preserved (`alpha.tif`). BMP alpha is not documented or tested.

A 16-bit PNG is reported as 16-bit from its IHDR. It is decoded into 8-bit BGRA and displayed and exported as 8-bit. The test asserts the reported bit depth, not the precision of the resulting pixels. There is no 16-bit export path.

### Animation

Animated GIFs are decoded frame by frame and played automatically by the viewer. `ImageDecoder.DecodeAnimation` composites every frame using the GIF disposal and blend rules, records each frame's delay, and caps the total frame memory at 24 million pixels by reducing the rasterization size when a long animation would exceed that budget. The viewer loops the frame set with an Avalonia `DispatcherTimer`.

`animated.gif` has three frames with 100 ms delays; `ImageDecoderTests.Animated_gif_decodes_every_frame_and_delay` checks all three frame colours and delays, and the window test checks that the displayed bitmap changes over time. Rotation and save/copy operate on the first frame; animated playback is at the decoded preview size (a large GIF may be softer at 100%).

Animated WebP and APNG remain still images: those containers are not decoded as frame sets and are not tested. Video is not supported.

### Metadata

`ExifReader` reads the fields used by the info panel:

- camera make and model;
- lens model;
- exposure time;
- f-number;
- ISO;
- focal length;
- exposure bias;
- original date/time;
- orientation.

It reads IFD0 and the Exif sub-IFD, with the sub-IFD taking priority. RATIONAL (type 5) and signed RATIONAL (type 10) values are supported, as are Pillow-style pairs written as SHORT (type 3) or SSHORT (type 8). The JPEG APP1 payload is capped at 1 MiB, the PNG `eXIf` payload at 1 MiB, and ICC payloads at 4 MiB. Malformed metadata returns empty details and never prevents the image from opening.

XMP, IPTC, maker notes and PNG text chunks other than `eXIf` are not read.

## Test fixtures

All paths are under `tests/Assets` and are copied next to the test binaries.

| Fixture | Dimensions / size | Used by | What it proves |
| --- | --- | --- | --- |
| `orientation-1.jpg` … `orientation-8.jpg` | 80 × 40 stored; 40 × 80 displayed for 5–8 | `ImageDecoderTests.Exif_orientations_are_applied_to_decoded_pixels` | All eight EXIF orientations are applied to pixels and dimensions |
| `orientation-6.jpg` | 80 × 40 stored | `ImageDecoderTests.Metadata_reports_orientation_adjusted_size` | Metadata reports raw and display dimensions separately |
| `display-p3.jpg` | 64 × 32 | `ImageDecoderTests.Display_p3_is_converted_to_srgb` | Display P3 JPEG is named and converted to different sRGB pixel values |
| `display-p3.png` | 64 × 32 | `ImageDecoderTests.Png_icc_profile_is_read` | PNG `iCCP` profile is read |
| `adobe-rgb.jpg` | 64 × 32 | `ImageDecoderTests.Adobe_rgb_profile_is_named` | The compact `A98C` description becomes "Adobe RGB (1998)" |
| `srgb.jpg` | 64 × 32 | `ImageDecoderTests.Display_p3_is_converted_to_srgb` | sRGB reference image for the P3 conversion comparison |
| `alpha.png` | 48 × 32 | `ImageDecoderTests.Png_alpha_is_preserved_and_premultiplied`, `ImageExporterTests.Jpeg_flattens_transparency_to_white`, `ImageExporterTests.Png_keeps_transparency` | PNG alpha is decoded, preserved on PNG export and flattened on JPEG export |
| `alpha.webp` | 48 × 32 | `ImageDecoderTests.Webp_decodes` | WebP decodes and keeps a transparent corner |
| `rgb-lzw.tif` | 80 × 40 | `ImageDecoderTests.Tiff_decodes_rgb_and_lzw`, `Tiff_scaled_decode_respects_the_requested_box` | LZW RGB TIFF, colour channels and scaled decode |
| `alpha.tif` | 48 × 32 | `ImageDecoderTests.Tiff_alpha_is_preserved` | TIFF alpha is preserved |
| `orientation-6.tif` | 80 × 40 stored; 40 × 80 displayed | `ImageDecoderTests.Tiff_orientation_is_applied`, `Tiff_raw_decode_carries_the_orientation_for_the_tile_writer` | TIFF orientation is applied and passed to the tile path |
| `animated.gif` | 40 × 40, 3 frames | `ImageDecoderTests.Animated_gif_decodes_every_frame_and_delay`, `MainWindowTests.Animated_gif_is_presented_as_an_animation` | GIF frames, delays and playback are exercised |
| `vector.svg` | 64 × 48 | `ImageDecoderTests.Svg_renders_at_its_intrinsic_size`, `Svg_scaled_decode_keeps_the_vector_aspect_ratio` | SVG is rasterized at intrinsic and scaled sizes |
| `gradient-16bit.png` | 64 × 32 | `ImageDecoderTests.Sixteen_bit_png_reports_its_source_bit_depth` | PNG source bit depth 16 is reported |
| `sample.avif` | 1440 × 900 | `ImageDecoderTests.Avif_decodes_a_still_image`, `Avif_preview_respects_the_requested_box` | AVIF still decode, centre colour and preview scaling |
| `alpha.avif` | 80 × 80 | `ImageDecoderTests.Avif_alpha_is_preserved` | AVIF alpha is preserved and premultiplied |
| `irot.avif` | 12 × 34 stored; 34 × 12 displayed | `ImageDecoderTests.Avif_irot_is_applied_and_bit_depth_is_reported` | `irot` orientation and 10-bit source reporting |
| `icc-exif.avif` | 403 × 302 | `ImageDecoderTests.Avif_icc_and_exif_are_read` | AVIF ICC description and EXIF camera fields |
| `animated.avif` | 150 × 150, 5 frames | `ImageDecoderTests.Avif_animation_uses_the_first_frame` | Animated AVIF is presented as one still frame |
| `white-1x1.avif` | 1 × 1 | `ImageDecoderTests.Avif_one_pixel_image_decodes` | Smallest AVIF dimensions |
| `corrupt.avif` | 100 bytes (truncated) | `ImageDecoderTests.Avif_corrupt_file_reports_corrupt` | Truncated AVIF reports corrupt |
| `corrupt.jpg` | 18 bytes | `ImageDecoderTests.Corrupt_file_reports_corrupt` | A supported extension with invalid content reports a corrupt image |
| `exif-full.jpg` | 120 × 80 | `ImageDecoderTests.Exif_fields_are_read` | IFD0/sub-IFD fields, rational values and Pillow-style pairs |
| `profiles/DisplayP3.icc` | 480 bytes | Test asset generation and `display-p3.*` | Display P3 profile fixture |
| `profiles/AdobeCompat.icc` | 374 bytes | Test asset generation and `adobe-rgb.jpg` | Adobe RGB compatible profile fixture |

The two ICC files come from saucecontrol/Compact-ICC-Profiles and are CC0. They are test fixtures only and are not shipped with the application. See [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md).

## Coverage gaps

The matrix is a description of tested behavior, not a claim that every supported format has a fixture. These points are not measured by the current suite:

- BMP and ICO have no fixture and no test. Their support is the `SKCodec` path and the extension gate.
- AVIF has no valid clean-aperture-only fixture; the `clap` crop path is implemented but not exercised end to end. `irot` is fixture-tested; `imir` and combined `irot`/`imir` follow libavif's documented mapping but are not fixture-tested. ICC is read for the info panel but not converted to sRGB for AVIF pixels. HDR/gain maps and AVIF encoding are not supported.
- AVIF tests and fixtures only run where the native decoder is present; Windows arm64 hides `.avif` until its toolchain is added. macOS and Linux CI builds the decoder for the matching architecture.
- PNG `eXIf` orientation is not applied to pixels, and there is no PNG orientation fixture to demonstrate that end to end.
- WebP ICC and WebP EXIF are not covered by fixtures.
- ICO alpha, GIF transparency and BMP alpha are not covered by dedicated fixtures.
- TIFF CMYK conversion, multi-page files, BigTIFF and exotic compression/16-bit colour paths are not covered by fixtures.
- CMYK JPEG, progressive JPEG and very large JPEGs are not separately tested.
- The 16-bit PNG test checks the reported depth, not the precision of the 8-bit result.
- Animated WebP and APNG are not decoded as animations.
- The format tests run on each host in `build.yml` (Windows, Linux and macOS), but there is no separate per-OS fixture or expected-result matrix.
- `tools/testdata/photo-100mp.jpg` and `photo-24mp.jpg` are benchmark images, not format fixtures, and are gitignored.

## Running the format tests

```text
dotnet test AirGlance.sln -c Release
```

To run only the decoder tests:

```text
dotnet test tests/AirGlance.Core.Tests/AirGlance.Core.Tests.csproj -c Release --filter FullyQualifiedName~ImageDecoderTests
```

The test projects copy `tests/Assets` into their output directories, so the fixtures do not need a separate install step. The test suite has no .NET SDK bundled with the repository; use the SDK version from `global.json`.

## Related documents

- [Architecture](architecture.md)
- [Shipping AirGlance 0.5.0](shipping.md)
- [Changelog](../CHANGELOG.md)
- [README](../README.md)
