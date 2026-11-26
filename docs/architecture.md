# Architecture

**English** · [简体中文](architecture.zh-CN.md)

AirGlance 0.7.0 is a desktop image viewer. It opens one folder at a time and displays one image at a time. The code is split into a UI-free core library and an Avalonia application.

## Stack

| Component | Version | Use |
| --- | --- | --- |
| .NET | 10 | Runtime and build target for both projects |
| Avalonia | 11.3.22 | App windows, input, rendering, dialogs |
| CommunityToolkit.Mvvm | 8.4.2 | Observable view models and commands |
| SkiaSharp | 4.150.1 | Image decoding and encoding |
| Svg.Skia | 5.2.3 | SVG parsing and rasterization |
| BitMiracle.LibTiff.NET | 2.4.660 | TIFF first-page decoding |
| libavif + dav1d | 1.4.2 / 1.5.5 | AVIF decoding on Windows x64/arm64, macOS and Linux through a bundled native shim |
| NativeAOT | .NET 10 | Release publishing on Windows, macOS and Linux |

The SkiaSharp 3.x line is superseded. 4.150.1 is the version that unifies with Avalonia 11.3.22's native asset graph on Windows, macOS and Linux. All projects use the versions from `Directory.Packages.props`; do not mix a 3.x SkiaSharp package into the graph.

`AirGlance.App` enables `AvaloniaUseCompiledBindingsByDefault`. Every data-bound XAML file declares `x:DataType`, so binding errors are build errors instead of runtime warnings. Settings use a source-generated `JsonSerializerContext`; there is no reflection-based JSON.

## Layers

```text
AirGlance.App (WinExe, Avalonia)
    references AirGlance.Core

AirGlance.Core (net10.0, SkiaSharp + Svg.Skia)
```

`AirGlance.Core` has no Avalonia reference and no UI-thread dependency. It contains navigation, decoding, transforms, export, metadata readers, settings and the thumbnail cache. It is marked `IsAotCompatible`.

`AirGlance.App` owns windows, input handling, the UI thread, localization, themes, clipboard access, shell integration and background services. It references `AirGlance.Core`; the dependency never points the other way.

The rule is deliberate:

- Core operations are synchronous and take a `CancellationToken` where the work can be aborted. Callers decide which thread runs them.
- App code does not reimplement decoding, sorting, navigation or settings logic.
- View models do not own windows or controls. `MainWindow` code-behind handles platform-level window and input work.
- Core tests run without a window. App tests use Avalonia's headless platform.

## Project map

### Core

`src/AirGlance.Core/Files`

- `SupportedFormats` is the extension gate: `.jpg`, `.jpeg`, `.jfif`, `.png`, `.webp`, `.avif`, `.bmp`, `.gif`, `.ico`, `.svg`, `.tif`, `.tiff`. Extension checks are case-insensitive. `Describe` provides a format label when the codec cannot be asked. `.avif` is only listed when the per-RID native decoder is present (`SupportsAvif`); a build without it hides the format instead of advertising an unopenable extension.
- `NaturalStringComparer` compares digit runs numerically, so `img2.jpg` comes before `img10.jpg`. Equal numeric runs with fewer leading zeros sort first; an ordinal comparison breaks remaining ties. `NaturalPathComparer` sorts full paths by file name.
- `FolderScanner.EnumerateImages` lists one folder only. It does not recurse. Hidden and system files are skipped, inaccessible entries are ignored, only supported extensions are kept, and the result is naturally sorted. Descending order reverses the list.
- `ImageSourceResolver` turns a path into a folder, a file list and an index. A file selects itself in its folder; a folder starts at its first image. A directly opened hidden file that the scan skipped still opens alone. A folder scan failure for a directly opened file falls back to that single file.

`src/AirGlance.Core/Navigation`

- `ImageNavigator` holds the file list and current index. `MoveNext` and `MovePrevious` wrap at the ends and report `Wrapped` so the UI can show a message. `GoTo` clamps to a valid index and does not wrap. `Reset` replaces the list.

`src/AirGlance.Core/Imaging`

- `ImageDecoder` is the raster decode path. It opens files with `FileShare.ReadWrite | FileShare.Delete`, creates an `SKCodec`, reads `SKCodec.EncodedOrigin`, and applies the reported EXIF orientation (1–8) to the decoded pixels. For a preview it asks `SKCodec.GetScaledDimensions` for a screen-sized decode, then steps the scale up once or more if the nearest codec step would look soft. Pixels are decoded into an 8-bit BGRA, premultiplied buffer with an sRGB `SKImageInfo`; Skia converts an embedded ICC profile to sRGB during that decode. The result is a `DecodedBitmap` with source dimensions, decode scale and alpha capability. AVIF bypasses `SKCodec` through `LibAvifDecoder`; the AVIF path decodes the first frame to premultiplied BGRA, applies `irot`/`imir` or EXIF orientation, and then reuses the same preview downscale as a PNG that ignored the requested scale.
- `DecodedBitmap` carries the pixel buffer, stride, source width/height after orientation, decode scale and `HasAlpha`. `IsFullResolution` is true at a scale of at least 0.999.
- `ImageDecoder.DecodeAnimation` decodes every GIF frame, applies frame disposal and blend rules into one canvas, and returns `DecodedAnimation` with per-frame delays. The viewer owns playback; the decoder caps frame memory at 24 million pixels.
- SVG files bypass `SKCodec`: `SvgGeometry` reads the intrinsic size, `SKSvg` parses the document, and the picture is drawn into a transparent BGRA surface at the requested scale. TIFF takes the first page through LibTiff.NET, converts its RGBA raster to BGRA, applies the TIFF orientation tag and passes the raw orientation to the tile writer.

- `ImageDecoder.DecodeFullRaw` is the tile-cache decode path. It decodes into unmanaged memory (`NativeMemory.Alloc`) instead of a managed `byte[]`, so a 100 MP buffer can be freed immediately after the tile file is written. It returns `DecodedImageBuffer` with raw dimensions, oriented source dimensions, EXIF orientation and alpha capability; it does not apply the orientation copy.
- `LibAvifDecoder` (Windows x64/arm64, macOS, Linux) is the AVIF bridge. It calls `airglance_avif.dll`, `libairglance_avif.dylib` or `libairglance_avif.so` through `LibraryImport`, parses metadata with libavif, decodes the first frame as premultiplied BGRA into caller-owned memory, applies `clap` cropping, maps `irot`/`imir` to the existing `ImageOrientation` values (falling back to EXIF orientation), and caps the decoded pixel count. The native shim statically links libavif and dav1d; on Windows it uses the static CRT. The library is copied next to the executable by `Directory.Build.targets` and is not committed. The Windows arm64 DLL is cross-built with LLVM against the ARM64 UCRT and PE-checked; no Windows arm64 hardware run is recorded.
- `TiledImageSource` (App) writes 512 px tiles to a temporary file-backed cache. Each tile is 512 × 512 BGRA padded to a fixed size; the last row/column is partial. Orientation and display edits are applied while copying into the tile, so no full oriented or edited intermediate buffer exists. The tile file is deleted on dispose; files older than one day are cleaned at startup. Only 32 tile bitmaps are cached, and the viewer draws the tiles intersecting the viewport. Two readers feed this path: a full raster for anything under the 32 MP budget, and `TiffRegionReader` for larger TIFFs, which streams tiles from the file without ever materialising the whole image. The tile file is therefore bounded by what the raster budget allows, not by the source size.
- For codecs that ignore a requested preview scale (notably PNG), `ImageDecoder.Decode` downsamples the decoded buffer in software so the fit preview does not keep the full image. The downscale averages the source block each target pixel covers, with the column map precomputed, and is only used when the codec returned its full dimensions.
- `ImageTransform` contains the EXIF orientation pixel map and the display edits. `EditTransform` stores one canonical state: mirror first, then a number of clockwise quarter turns. Any sequence of rotate and flip commands collapses to eight states. Rotating or flipping allocates a new pixel buffer and updates source dimensions when the axes swap.
- `ImageExporter` encodes JPEG, PNG or WebP; JPEG and WebP use quality 90 and PNG is lossless. JPEG is drawn onto a white bitmap first, so transparency becomes white. PNG and WebP keep alpha. A save always creates a new file. The default name is `<name>-copy.<ext>`; if that exists, the next candidates are `-copy-2`, `-copy-3`, and so on. `WriteNewFile` uses `FileMode.CreateNew` and retries with another suffix if another process creates the same name first.
- `ExifReader` reads a TIFF/EXIF payload and returns the fields the info panel shows. It handles IFD0 and the Exif sub-IFD, RATIONAL type 5, signed RATIONAL type 10, and Pillow-style pairs stored as SHORT or SSHORT. Malformed metadata returns empty details; it never blocks an image from opening.
- `ImageHeaderReader` reads metadata blocks without decoding pixels: JPEG APP1 EXIF and APP2 ICC chunks, PNG `eXIf` and `iCCP`, WebP `EXIF` and `ICCP`, and the PNG IHDR bit depth. It caps EXIF at 1 MiB and ICC at 4 MiB.
- `IccProfile` reads a description from `desc`, `text` or `mluc` tags. It maps `A98C`/`A98` to "Adobe RGB (1998)" and `sP3`/`P3` to "Display P3".
- `ImageMetadata` joins file facts, display dimensions after orientation, format name, bit depth, ICC description and EXIF fields. `ColorSpaceDisplay` reports `sRGB` or `<profile> → sRGB`.

`src/AirGlance.Core/Storage`

- `AppPaths` resolves settings and cache folders once per process. `AIRGLANCE_SETTINGS_DIR` and `AIRGLANCE_CACHE_DIR` override the platform defaults; the test suite uses constructor injection instead of environment variables for most cases.
- `AppSettings` holds language, theme, sort order, slideshow interval, remember-zoom, zoom mode/factor, checkerboard, window size and maximized state. `Normalize` repairs invalid values.
- `SettingsService` loads `settings.json`, falls back to `settings.json.bak`, then defaults. Saves write a temporary file and move it over the primary file, keeping the previous file as `.bak`. If a move is refused, as can happen with MSIX redirected app data, it writes in place. Read and write failures are swallowed; preferences never stop the viewer from opening.
- `ThumbnailCache` stores 160 px PNG entries. The entry name is a SHA-256 of the full image path, the last-write ticks, the file length and the thumbnail size, so a changed file gets a new entry. It provides read, write, cleanup by age and clear. Startup removes entries older than 30 days on a background task without waiting for it.

### App

`Program` builds the Avalonia app with platform detection and the Inter font. On Windows it selects Avalonia's software compositor by default; `AIRGLANCE_GPU_RENDER=1` opts back into the GPU path. macOS and Linux keep the platform defaults.

`App.OnFrameworkInitializationCompleted` parses the command line, creates process-wide `AppServices`, handles `--bench=decode` and `--bench=scan` without a window, and otherwise creates one `MainWindow`. Before the window is constructed it starts `StartupImageLoader.TryLoad` on the thread pool for the target path. The loader reads metadata and decodes a 1600 × 1000 preview while the window is being built. `MainWindowViewModel` consumes the result once if the path still matches, so the first frame usually does not wait for the decode. Preload failures are ignored and the normal load path reports the error.

`MainWindow` is intentionally thin. It handles the keyboard map, drag and drop, file and folder pickers, the context menu, fullscreen, clipboard writes, settings and help dialogs, and window/zoom persistence. It creates one `MainWindowViewModel`, which owns the folder and current image.

The window is an image-first overlay: the `ImageViewer` fills the canvas, while the top folder/file chips, bottom dock, side navigation, filmstrip and information drawer are separate overlay controls. The dock and side buttons fade after 2.5 seconds of idle and return on pointer movement. The filmstrip and information panel are mutually exclusive drawers, so opening one does not shrink or cover the other. The window subscribes to `MainWindowViewModel.PropertyChanged` and applies visibility, text and fullscreen state imperatively; static labels are refreshed from `LocalizationService.LanguageChanged`.

The earlier XAML chrome cost about 370 ms of the first-window time; the overlay tree built in code is about 46 ms at 0.7.0 (about 14 ms at 0.3.0). The filmstrip and information panel remain XAML and are loaded after the first frame. The context menu is built on the first right-click instead of during window construction.

Mouse input is split by modifier: the wheel zooms at the pointer (no accumulated page jumps), and `Shift + wheel` pans. Left/right keys always browse; after the redesign they no longer become pan commands when the image is wider than the window.

The filmstrip and information panel are separate views (`FilmstripView`, `InfoPanelView`) loaded one frame after the first image is painted. The window therefore appears and shows pixels before those panels exist; this matches the "window first, panels later" startup rule and keeps their XAML out of the first-frame path. The context menu is also built on the first right-click instead of during window construction.

The analysis drawer (`AnalysisPanelView`) follows the same lazy rule: it is created the first time an analysis tab is opened, only in single-image view. `ImageAnalyzer` runs on a background task over the already-decoded preview and returns a result that never retains a pixel buffer; `AnalysisViewModel` caches the last few results per file and formats them for the drawer. The drawer is read-only, closes when dual view or edit mode starts, and is hidden (state kept) in fullscreen.

`MainWindowViewModel` holds:

- the `ImageNavigator` and the observable `Images` filmstrip;
- the current path, bitmap, metadata and `EditTransform`;
- load version, load/prefetch/full-resolution cancellation tokens;
- info-panel sections, transient messages, slideshow and empty/error overlays.

Dual view is a separate view (`DualCanvasView`) owned by `MainWindow`. It creates two `PaneSlot` sessions, each with its own folder list, file index, metadata, preview bitmap/animation and `ImageViewer`. Both decode through the same `ImageDecoder` with a bounded 2400 x 1600 preview, so entering dual view does not require a second full-resolution source. The view owns the active-pane state, divider, dock, fixed 50/50 split filmstrip, information drawer, a pane context menu and a top-centre previous/next capsule for each canvas. The paging capsule targets only its own canvas; it is hidden for an empty or single-image canvas. The active pane uses a 2 px identity frame and a 30 x 30 badge ring; the inactive image is not dimmed. `MainWindow` hides the single-view canvas/chrome and keeps the window caption buttons in a root overlay. `D` enters or leaves; the active pane's path is handed back to `MainWindowViewModel` on exit.

`ImageViewer` is a custom `Control` that owns zoom and pan. `MainWindow` forwards the presented bitmap and source dimensions to it and forwards its zoom and viewport events back to the view model. When a `TiledImageSource` is attached, the viewer draws the intersecting tiles instead of the preview bitmap; otherwise it draws the preview bitmap scaled to the image rectangle.

### App services

- `AppServices` is created once per process. It loads settings, applies the saved language and theme, opens the thumbnail cache and starts the 30-day cleanup on the thread pool. It saves settings when a setting changes and again on exit. It also exposes the assembly version and a shell helper for the project links. It has no network client.
- `LocalizationService` keeps a catalog of roughly 150 English/Chinese key pairs (153 at 0.3.0). XAML text uses `{DynamicResource L.*}`; text built in code uses `T` and `Format`. Applying a language builds a merged `ResourceDictionary`, removes the previous language dictionary and adds the new one. `LanguageChanged` refreshes view models and windows, so switching English/Chinese does not restart the app. The default follows the OS UI culture: a `zh*` culture selects Simplified Chinese, everything else English.
- `ThemeService` normalizes `System`, `Light` or `Dark` and sets `Application.RequestedThemeVariant`. System mode follows `PlatformSettings.ColorValuesChanged`; an explicit light or dark choice ignores the OS. There is no high-contrast palette: the system contrast preference is not read, because the palette never loaded (see the optimization backlog) and the theme was removed rather than left silently inert.
- `BenchmarkRecorder` measures from the operating system's process start time. Startup mode records first window, first frame, decode time, image megapixels and peak working set, then exits after the first frame. Decode mode exits after a 1600 × 1000 decode. Scan mode times folder enumeration and the first eight 160 px thumbnails, and reports `scan_ms`, `thumbnail_ms`, `total_ms`, `files` and `peak_ws_mb`. All modes write `key=value` lines to stdout and exit 1 with `error=<message>` on failure.
- `StartupTrace` reads `AIRGLANCE_STARTUP_TRACE` once. When it is `1`, it writes marks to stderr relative to the process start time. Normal runs only pay for the environment read.
- `CommandLineOptions` parses the arguments before a window exists. It records help, version, benchmark mode, the first displayable target and ignored paths/options. Details are in [Command line](#command-line).

## Open and load flow

```text
CLI target (optional)
  -> StartupImageLoader.TryLoad (thread pool, before the window)
       -> metadata + 1600 × 1000 preview

CLI path, drop, or file picker
  -> ImageSourceResolver.Resolve (thread pool)
  -> ApplyFolder: navigator, filmstrip, thumbnail queue
  -> LoadCurrentAsync
       -> consume startup preload if it matches, otherwise ReadMetadata (thread pool, cached)
       -> preview target from the physical viewport
       -> ImageLoadService cache or decode (thread pool)
       -> version/path check
       -> BitmapFactory.FromDecoded (UI thread)
       -> ImageViewer.SetImage (UI thread)
       -> prefetch previous/next neighbours (thread pool)
```

`ImageLoadService` is a four-entry LRU keyed by path and requested size. A prefetched neighbour is a screen-sized decode, not a full-resolution one. Navigation cancels the previous prefetch, so a fast sequence of arrow presses does not keep decoding abandoned neighbours.

A full-resolution decode is requested when the visible zoom exceeds the current preview's scale by 15 percent:

```text
percent / 100 > preview.Scale * 1.15
```

At 100 percent zoom this condition is true unless the preview is already at 99 percent scale or higher (`preview.Scale >= 0.99`). The full decode runs off the UI thread, replaces the preview without resetting the zoom, and re-applies the current display edit if one is active. If it fails, the preview stays usable. This threshold is relative to the preview, not an absolute "only at 100 percent" rule: a very large image with a small preview may request the full decode below 100 percent.

The info panel is built from `ImageDecoder.ReadMetadata`, which reads headers and EXIF/ICC blocks but does not decode the full image.

## Viewer control

`ImageViewer` owns the camera, not the view model:

- **Fit window** uses `min(1, viewport / source)`, so it never enlarges an image past 100 percent. **Fit width** uses `min(1, viewport width / source width)`. **Actual size** is exactly 100 percent. **Manual** is clamped to 1–3200 percent.
- The wheel uses a factor of 1.25 per step, anchored at the pointer. Shift+wheel pans horizontally by 80 px per step. Pinch gestures zoom at the gesture origin.
- A double-click switches between Actual size and Fit window, keeping the clicked image point under the pointer. `+` and `-` zoom at the center by 1.25. `0`, `1` and `2` select fit window, actual size and fit width.
- Pan is clamped so at least 48 px of the image remains in view. Fit window cannot be panned; fit width can pan vertically only.
- The checkerboard is a 16 × 16 DIP tile made of 8 × 8 DIP squares, drawn only behind the image rectangle. It is clipped to the viewport and follows the checkerboard setting.
- Switching images may fade the new bitmap in over 80 ms. The fade is not used for the first image, for a full-resolution replacement, or for a rotate/flip preview.
- Bitmap interpolation is high quality below 100 percent and nearest at or above 100 percent.
- Coordinates are in DIPs. The viewport event reports physical pixels by multiplying by `RenderScaling`; the decoder uses those pixels for the preview target.

## Threading rules

| Work | Thread |
| --- | --- |
| Directory enumeration and sorting | Thread pool |
| Preview and full-resolution decode | Thread pool |
| EXIF, ICC and header reads | Thread pool |
| Thumbnail decode, encode and disk cache | One serial background queue |
| Save-copy encode and file write | Thread pool |
| Clipboard source-file read | Thread pool |
| `WriteableBitmap` creation and pixel copy | UI thread |
| Observable property updates and viewer calls | UI thread |

Decode, directory enumeration, metadata reads, thumbnail generation and export all run on the thread pool. Only the final `WriteableBitmap` copy and observable property updates happen on the UI thread.

Every navigation increments `_loadVersion` and cancels the previous load token. Async continuations check both the captured version and the current path before presenting pixels. A stale decode is discarded, never shown. Prefetch and full-resolution tokens are cancelled on navigation as well.

`BitmapFactory.FromDecoded` creates a `WriteableBitmap` with `Bgra8888`/`Premul` and makes one bulk copy per row. The decoded buffer and Avalonia bitmap use the same layout, so there is no PNG round trip.

Thumbnails are generated by `ThumbnailService` on one serial background queue, in folder order. The disk cache is consulted first; failures are not retried. Each completed PNG is turned into an Avalonia `Bitmap` on the UI thread.

## Memory behavior

A preview is sized for the window, not for the file. The target is the physical viewport width and height, with a floor of 640 × 480 and a ceiling of 8192 × 8192, then reduced by `SKCodec.GetScaledDimensions`. A 100 MP photo in a 1600 × 1000 window therefore decodes roughly a 1.6 MP preview, about 6.4 MB of BGRA pixels.

The preview cache holds at most four entries. That is an entry count, not a byte budget: four 8K-sized previews would be much larger than four window-sized ones. The cache is cleared when the folder changes.

A full-resolution decode allocates the complete image. At 100 megapixels:

- one BGRA buffer is 100,000,000 × 4 = 400 MB (decimal);
- the Avalonia `WriteableBitmap` is another 400 MB;
- a rotate or flip allocates another 400 MB before the old buffer can be released;
- JPEG export adds a white-flattened bitmap and the encoded output.

So the 100 MP case can reach roughly 800 MB for preview replacement, and more than 1.2 GB transiently during an edit or export. Those are arithmetic from the pixel format, not measured peak working sets. The full-resolution bitmap replaces the preview and the previous Avalonia bitmap is disposed. The decoded preview object may remain in the LRU until it is evicted.

There is no tiling, streaming or mip pyramid in 0.1. A 100 MP image at 100 percent needs a very large full-resolution decode and enough free memory; that is the deliberate tradeoff for a simple viewer. The decoder rejects images whose pixel count would exceed `int.MaxValue / 4` (about 536 megapixels) and reports them as too large.

## Settings and local data

| Data | Windows | macOS | Linux |
| --- | --- | --- | --- |
| Settings | `%APPDATA%\AirGlance\settings.json` | `~/Library/Application Support/AirGlance/settings.json` | `${XDG_CONFIG_HOME:-~/.config}/airglance/settings.json` |
| Cache | `%LOCALAPPDATA%\AirGlance\cache` | `~/Library/Caches/AirGlance` | `${XDG_CACHE_HOME:-~/.cache}/airglance` |
| Thumbnails | `%LOCALAPPDATA%\AirGlance\cache\thumbs` | `~/Library/Caches/AirGlance/thumbs` | `${XDG_CACHE_HOME:-~/.cache}/airglance/thumbs` |

`AIRGLANCE_SETTINGS_DIR` and `AIRGLANCE_CACHE_DIR` replace the platform roots for tests and portable runs. They are full paths, not folder names.

Settings JSON is indented, camelCase, UTF-8 without a BOM. A missing or corrupt primary file falls back to the `.bak` file, then to normalized defaults. Saving over an existing file keeps the previous file as `settings.json.bak`, writes `settings.json.tmp`, and moves it over the primary file. An `IOException` during the move triggers an in-place overwrite, which keeps MSIX-redirected app data to one file.

The thumbnail cache expires entries after 30 days. Cleanup starts in the background during `AppServices` construction. The settings dialog can report cache size and clear the folder.

## Command line

```text
AirGlance [options] [file or folder]
```

| Option | Behavior |
| --- | --- |
| `-h`, `--help`, `-?` | Show the help text in a message window |
| `-v`, `--version` | Show the version in a message window |
| `--bench=startup` | Open the target, measure the first frame, then exit |
| `--bench=decode` | Decode the target at 1600 × 1000, print numbers, then exit |
| `--bench=scan` | Enumerate the target folder (or the folder containing the target file) and decode the first eight thumbnails, then exit |
| `--` | Treat every later argument as a path |

The first displayable file or folder wins. Later paths are ignored. A directory always counts as a target, even if it contains no images. A file normally counts only if it exists and has a supported extension. If no displayable path exists but an unsupported file was named, that file becomes the target so the window can explain why it cannot be opened. Unknown options are ignored.

Help and version are dialogs because the Windows build is a GUI executable; they are not console output.

`--bench=startup` requires a target and measures from the operating system's process start time. It prints:

```text
first_window_ms=<ms>
first_frame_ms=<ms>
decode_ms=<ms>
image_mp=<megapixels>
peak_ws_mb=<megabytes>
```

`--bench=decode` skips the window, decodes the first image of a folder (or the given file) at 1600 × 1000 and prints the same keys, with the window and first-frame values at zero.

`--bench=scan` also skips the window. It enumerates the folder that contains the target and decodes the first eight images at 160 × 160, then prints:

```text
scan_ms=<ms>
thumbnail_ms=<ms>
total_ms=<ms>
files=<count>
peak_ws_mb=<megabytes>
```

Benchmark failures print `error=<message>` and exit with code 1. `tools/bench.ps1` runs repeated `--bench=startup` processes, reports median and p95, and can write the raw runs as JSON. The application relies on stdout redirection because the Windows build has no console.

`AIRGLANCE_STARTUP_TRACE=1` prints opt-in startup marks to stderr, including `main`, `app-init`, `xaml-loaded`, `framework-init`, `services-ready`, `window-created`, `window-opened` and `image-initialized`.

`SingleInstance` claims a named mutex and listens on a named pipe, both keyed by a hash of the settings directory. A launch that cannot claim the mutex writes its path to the pipe and exits; the running instance opens it on the UI thread. The scope is the settings directory so an isolated test, benchmark or portable copy never forwards into a normal session. There is no tray icon.

## NativeAOT and trimming

Release builds are published with NativeAOT on Windows, macOS and Linux. NativeAOT does not cross-compile between operating-system families; Windows, macOS and Linux each build on their own runner. Native SkiaSharp, HarfBuzzSharp and Avalonia libraries are copied per runtime identifier.

AOT constraints are enforced by design:

- Settings JSON uses `SettingsJsonContext`, a source-generated `JsonSerializerContext`. There is no reflection serializer.
- All data-bound XAML (panels and dialogs) uses compiled bindings and `x:DataType`.
- XAML is compiled. The main window is built in C#; panels and dialogs are constructed in code from compiled XAML. No window is instantiated by the runtime XAML loader.
- There is no `Assembly.Load`, no `Activator.CreateInstance`, no `dynamic` and no runtime code generation.
- `AppServices.Version` reads `AssemblyInformationalVersionAttribute`; that is metadata reflection, not dynamic code.

`AirGlance.App.csproj` suppresses analyzer warning `AVLN3001`. The warning expects a public parameterless constructor for windows instantiated through the runtime XAML loader. AirGlance constructs every window directly and passes services or view models, so the constructor shape is intentional. If a window is ever loaded through the runtime XAML loader, remove the suppression and add a parameterless constructor instead.

The AOT publish configuration also sets `IlcOptimizationPreference=Speed`, disables stack trace generation, EventSource and metadata updates, and removes PDBs from the publish directory.

## Tests

- `tests/AirGlance.Core.Tests` uses xUnit for sorting, folder scanning, navigation, orientation and edits, decoding, metadata, export, settings and the thumbnail cache.
- `tests/AirGlance.App.Tests` uses Avalonia.Headless and xUnit for window-level keyboard, navigation, edit, fullscreen, language switching, info rows and empty/error states.
- Test images and ICC profiles are copied from `tests/Assets` into the test output. The fixtures and what each one proves are listed in the [format support matrix](formats.md).

Run the suite with:

```text
dotnet test AirGlance.sln -c Release
```

`.github/workflows/build.yml` restores, builds and tests the solution on Windows, Linux and macOS. `release.yml` packages the six portable archives and the checksum file on a version tag. `store.yml` builds the unsigned MSIX packages and runs an informational WACK preflight. See [shipping](shipping.md) for the release process and its current limits, and [performance notes](performance.md) for startup, decode and memory measurements.

## Related documents

- [Format support matrix](formats.md)
- [Shipping AirGlance 0.7.0](shipping.md)
- [Third-party notices](../THIRD-PARTY-NOTICES.md)
- [Changelog](../CHANGELOG.md)
- [README](../README.md)
