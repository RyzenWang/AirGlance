# Optimization backlog

A maintainer working document, not a user-facing promise list. It records what is
left to improve after 0.7.0, why each item matters, and how to tell when it is
done. It is deliberately single-language: identifiers, comments and every other
technical document in `docs/` are English.

The P0 items are fixed in the same change that added this file. Everything below
them is untriaged work, ordered by expected return rather than by difficulty.

## How this list was produced

A static read of the whole tree: `src/`, `tests/`, `packaging/`, `native/`,
`tools/`, `.github/` and the docs. No .NET SDK is installed on the authoring
machine (`C:\Program Files\dotnet\sdk` is empty; only the 10.0.14 runtime is
present), so **nothing here was compiled or run**. Where a claim depends on
runtime behaviour it is marked *(verify)*.

Line numbers are a search hint, not a contract: they were taken at `87d48a8`
plus the P0 fixes in the same change, and they drift with every edit. Search for
the quoted symbol or string when a number no longer matches.

## Priority legend

| Tier | Meaning |
| --- | --- |
| P0 | Wrong output or a silently dead feature. Fix before the next release. |
| P1 | Measured or clearly attributable cost on a path the product is about (startup, navigation, pan/zoom, memory). |
| P2 | Engineering: release risk, CI cost, build reproducibility, packaging, coverage. |
| P3 | Documentation that contradicts the code or itself. |
| P4 | Maintainability, accessibility, dead or duplicated code. |

---

## P0 — correctness

All six are fixed here. Two still need a runtime check before they can be called
closed.

| ID | Item | Status |
| --- | --- | --- |
| P0-1 | The window caption showed two garbled bytes (`U+95C2` + `?`) where the em dash belongs | **Fixed.** Two of the five caption sites had been damaged by GBK round trips: `MainWindowViewModel` line 1536 carried a correct `U+2014`, while the others carried `U+95C2` followed by `?`. All five now go through one `FormatWindowTitle()` that formats `L.WindowTitleFormat` — the key already existed and had no caller. The no-path error state no longer produces `AirGlance — AirGlance` either: it clears `TitleFileName` like the empty states do, so the caption is just the app name. A scan of `src/`, `tests/`, `packaging/`, `tools/` and the docs for `U+FFFD`/`U+95C2`/`U+9225` returns nothing. |
| P0-2 | AVIF files with a clean aperture (`clap`) could never decode | **Fixed in managed code.** `LibAvifDecoder.Decode`/`DecodeRaw` allocated `clapWidth × clapHeight × 4`, but the shim requires the whole coded frame (`airglance_avif.c:218-226`) and the code itself asserts the uncropped size (`decoded.Width == info.Width`). Every such file paid for a full AV1 decode and then reported "corrupt". The buffer is now sized from `info.Width`/`info.Height` and `CropManaged`/`CropUnmanaged` — previously dead — trim it. **Still open:** the capacity check in `airglance_avif.c` runs *after* `avifDecoderNextImage`, so a rejected file still pays for the decode. Moving it after `ag_decoder_create` needs a native rebuild for all six RIDs. **Also open:** no `clap` fixture exists (`docs/formats.md` already records this), so the fix is unverified — add one. |
| P0-3 | TIFF pixels were never colour-managed, but the info panel said they were | **Fixed.** `ImageMetadata.IccAppliedToPixels` defaults to `true`; neither TIFF metadata path set it, so the panel rendered `Adobe RGB (1998) → sRGB` although LibTiff returns stored samples. Both `ImageDecoder.ReadTiffMetadata` and `TiffPreviewDecoder.ReadMetadata` now set it `false`, and `ColorSpaceDisplay` therefore shows the profile name alone. The AVIF path already did this. **Open:** actually converting TIFF profiles would be the better fix, but it needs a colour-managed path through LibTiff's raster. |
| P0-4 | High-contrast mode was silently dead | **Fixed, needs a runtime check.** `HighContrast.axaml` was a bare `<ResourceDictionary>` with no `x:Class`, yet `HighContrastPalette` loads it with `AvaloniaXamlLoader.Load(this)`, which resolves compiled XAML *by type*. The lookup could not match, `ThemeService` wrapped the constructor in a `catch { }` that swallowed the failure, and `IsHighContrast` — five writers, zero readers — never became `true`. `x:Class` is now declared. Verify by enabling a Windows contrast theme and confirming the palette switches; if it still does not, the catch in `ThemeService.ApplyHighContrastPalette` needs to at least report. |
| P0-5 | Dual-view chrome timer never stopped | **Fixed.** `DualCanvasView` started a 2.5 s `DispatcherTimer` in its constructor and stopped it only in `Dispose`, so after using dual view once the timer fired for the rest of the session against a hidden control. `HideChrome()` now stops it; the runner-up path (an overlay is open) still keeps it alive. |
| P0-6 | `DecodedBitmap` buffer guard could overflow | **Fixed.** `pixels.Length < width * height * 4` was int arithmetic; above ~536 MP it wrapped and let an undersized buffer through. Now `(long)width * height * 4`. Not reachable through `ImageDecoder` (which guards first) but it is a public constructor. |

---

## P1 — performance and memory

The published budgets are met (250.5 ms first window, 415.7 ms 24 MP first frame,
88.1 MB peak working set, 21.84 MiB zip). These are the remaining costs inside
those numbers.

| ID | Item | Where | Why it costs | Fix | Effort |
| --- | --- | --- | --- | --- | --- |
| P1-1 | Every TIFF preview under 250 MP builds two full-resolution rasters before downscaling | `ImageDecoder.DecodeTiff` (`ImageDecoder.cs:729-769`), threshold at `ImageDecoder.cs:17` | `ReadTiffRgba` returns `int[w*h]` (4 B/px), it is copied into `byte[w*h*4]` (another 4 B/px), then oriented into a third buffer when needed, then point-sampled down. A 100 MP TIFF spends >1 GB to show an 8 MP preview. | Route every reduced-size TIFF preview through `TiffPreviewDecoder.DecodePreview` (`TiffPreviewDecoder.cs:79`), which already samples tiles/strips with bounded memory and prefers a pyramid IFD level; keep the full raster only for `maxWidth <= 0`. Apply orientation to the small result. | M |
| P1-2 | The 250 MP "large TIFF" threshold is ~4× above what the full-raster path can allocate | `ImageDecoder.cs:17`, guard at `:984-992` | The only guard before `new int[w*h]` is `pixelCount > int.MaxValue / 4`, so a 200 MP TIFF asks for 2 GB and then another 1 GB. `DecodeTiff` has a catch-all, so this surfaces as `ImageLoadFailure.Corrupt` — a wrong, unhelpful error. | Derive the threshold from a real budget (e.g. 32 MP) and report `TooLarge`, not `Corrupt`. | S |
| P1-3 | Codecs that ignore the requested scale decode at full resolution with no pixel budget | `ImageDecoder.cs:120`, `:145-155` | PNG/WebP/BMP decode `w*h*4` and only then call `Downscale`. 100 MP PNG = 400 MB for an 8 MP preview. The Skia path's catch list (`:172-206`) has no catch-all, so a large enough PNG throws OOM straight to the UI. | Add a preview pixel ceiling (e.g. 64 MP) that throws `TooLarge` before allocating, and add a catch-all that translates `OutOfMemoryException`. | S |
| P1-4 | `Downscale` is nearest-neighbour with per-pixel double math | `ImageDecoder.cs:1315` | It recomputes `(x + 0.5) * xRatio` for every target pixel even though the row mapping is invariant, and point sampling drops rows/columns — visible moiré on large PNG/AVIF reductions, unlike the JPEG path where Skia box-filters. | Precompute the column map (free 2×), then a box filter over source spans for quality. | M |
| P1-5 | `ImageAnalyzer` hashes every pixel, then scans the image a second time | `ImageAnalyzer.cs:37`, `:136-143`, `ComputeDetail`, palette k-means | `HashSet<int>` insert per pixel is the dominant cost of an otherwise ~5-op loop (24 MP = 24 M hash lookups); `ComputeDetail` is a second full pass; k-means runs 16 iterations over 65 536 samples for a 6-colour palette. | 2^24-bit presence map (2 MB, O(1)) with `BitOperations.PopCount`; fold the luma box filter into the main pass; subsample the palette input to ~4 096 before k-means. | M |
| P1-6 | The first image is decoded twice at most window sizes | `StartupImageLoader.cs:32-33`, `MainWindowViewModel.cs:1128`, `:145` | The preload is fixed at 1600 × 1000 but is stored in the LRU under the *viewport-derived* key, so the cache both mislabels it and misses it: `_resizeTimer` (220 ms) then calls `ReloadPreviewForViewportAsync` and decodes again. | Either size the preload from the persisted window client size, or store it under its own key and use it only while `preload.Width >= target`. | S |
| P1-7 | One 146-line `ApplyViewModelState()` pass runs per VM property change, and twice per zoom-animation frame | `MainWindow.axaml.cs:913-914` (body `:916-1061`) | It reacts to every observable property, including `ZoomPercent`/`ZoomText`, which `ImageViewer` raises on every 8 ms zoom-animation frame and on every crop drag. Each pass is ~60 UI writes plus string building and resource lookups. | Coalesce with a dirty flag + one `Post(..., DispatcherPriority.Render)`, and/or split into status/layout/edit slices; throttle `ZoomText` during animation. | M |
| P1-8 | Tile reads block inside `Render` | `ImageViewer.DrawTiles`, `TiledImageSource.cs:456`, `:475`, `:522` | A cache miss performs a synchronous 1 MB file read on the UI thread, up to 32 per frame, and allocates a fresh LOH buffer per tile. This is the hitch when panning a 100 MP image. | Return `null` on a miss, read on a worker (the region-tile machinery already exists), pre-read neighbours, and pool the buffers. | M |
| P1-9 | Thumbnails are decoded on the UI thread and never disposed | `ThumbnailService.cs:70`, `ImageItemViewModel` (no `IDisposable`) | 500-image folder = 500 UI-thread PNG decodes + 500 native bitmaps released only by finalization. The encoded cache format also means every thumbnail is encoded on write and decoded on read. | Create the `Bitmap` on the worker and only assign on the dispatcher; dispose thumbnails when the folder changes. | M |
| P1-10 | The full-resolution `WriteableBitmap` copy runs on the UI thread | `MainWindowViewModel.cs:1582`, `BitmapFactory.FromDecoded` | A 24 MP image allocates and copies 96 MB while the UI thread is blocked, and the source `DecodedBitmap` (another 96 MB) is alive at the same time — the main threat to the 250 MB target. | Create the bitmap in the background decode task and hand it over; keep an `IDisposable` handoff for cancellation. | M |
| P1-11 | A `Cursor` is allocated on every pointer move and old ones are never disposed | `ImageViewer.cs:1243-1251`, also `:557`, `:804`, `:856` | One `Cursor` plus platform impl per move event over the canvas, exactly during pan/hover — steady Gen0 and handle churn. | Three or five `static readonly` cursors, disposed at shutdown. | S |
| P1-12 | Dual panes decode a fixed 2400 × 1600 with no cache, and bypass the thumbnail pipeline | `DualCanvasView.cs:1187`, `:1562`, `:1619` | Each pane shows ~480 px but decodes 15 MB, re-decoding from disk on every `→`/`←`. The dual filmstrip re-decodes up to 48 thumbnails per navigation and ignores `ThumbnailService` and its disk cache. | Share `ImageLoadService` and size from the pane viewport; back the filmstrip with the same thumbnail service. | M |
| P1-13 | SVG is parsed twice and its rendered pixels copied twice | `ImageDecoder.DecodeSvg` / `SvgGeometry.Read` | `XDocument.Load` builds a DOM just to read three root attributes, then `SKSvg.Load` re-parses the same file, then `bitmap.Bytes` copies `w*h*4` out of the `SKBitmap` (`DecodeSvgRaw` copies again). No document-size cap either. | Derive the size from `picture.CullRect` first and fall back to `SvgGeometry`; read the root with `XmlReader`; render straight into the managed buffer via `InstallPixels`. | S |
| P1-14 | One AVIF open reads the file twice and parses the container five times | `LibAvifDecoder.ReadInfo`/`ReadPayload`, `airglance_avif.c` | `ReadInfo` calls the shim twice (once to learn the ICC/EXIF sizes, once to fetch them) and `Decode` parses a third time; the UI reads metadata and then decodes, and each call re-reads up to 512 MB into a managed array. | Extend the shim to return ICC/EXIF alongside the pixels in one call, or cache `(info, icc, exif)` per `(path, mtime, length)`. Needs a native rebuild. | M |
| P1-15 | The last ~150 ms before the first frame is unmeasured, and the measurement itself is biased | `StartupTrace` marks, `ImageViewer.cs:627-629` | The trace stops at `image-initialized` (266.0 ms in the documented run) while the first frame is at 415.7 ms, so ~150 ms is a blind spot. `FirstFramePresented` is posted at `DispatcherPriority.Background`, so `first_frame_ms` measures when the queue got to the callback, not when the pixels appeared. | Add a `first-frame` mark and marks around `OnImagePresented`/`SetImage`; raise the post to `DispatcherPriority.Render`. | S |

---

## P2 — engineering, CI and packaging

| ID | Item | Evidence | Fix | Effort |
| --- | --- | --- | --- | --- |
| P2-1 | The release gate runs a weaker test set than PR CI, and all AVIF tests pass silently there | `release.yml:16-44` has no meson/cmake and never builds the decoder (`build.yml:79-81` does); each AVIF test begins `if (!SupportedFormats.SupportsAvif) return;` | Reuse the `build.yml` matrix (or at least build the decoder on Linux) and convert the silent returns to `Assert.Skip` so skips are visible. | M |
| P2-2 | The native AVIF toolchain is rebuilt from source in every job, with only NuGet cached | `build.yml:36-42`, `release.yml:93-99`, `store.yml:32-45`; `build.ps1` deletes its build trees each run | Cache `native/avif/.cache` and `native/avif/bin`, keyed on `hashFiles('native/avif/build*')` plus OS/arch. A release-level event currently performs ~10 full libavif + dav1d builds. | S |
| P2-3 | The version has five sources of truth, and `pack.ps1 -Msix` produces an unsubmittable package | `Directory.Build.props:10`, `store.yml:15-16`, `AppxManifest.xml:11`, `pack.ps1:48` (default `0.1.0`), `Build-Msix.ps1:34` | Single source (tag or props), read by `store.yml`; have `pack.ps1 -Msix` refuse to build below the manifest's current version. Today the documented path emits `0.7.0.0` against a live `1.0.2.0`. | S |
| P2-4 | No `concurrency`, workflow-wide `contents: write`, actions not pinned by SHA | `release.yml:8-9` applies to all jobs; 16 `uses:` on mutable major tags; `build.yml:3-5` runs the full matrix on every branch push | Scope `contents: write` to the release job, add `persist-credentials: false`, add a `concurrency` group with `cancel-in-progress` on PRs, pin actions by SHA with the version in a comment. | S |
| P2-5 | No test artifacts, no coverage, no style gate | `build.yml:89-90` / `release.yml:43-44` are `dotnet test ... --logger console`; no `EnforceCodeStyleInBuild`; no `dotnet format` step | Add `--logger trx` + `--collect:"XPlat Code Coverage"` + `upload-artifact if: always()`; enable `EnforceCodeStyleInBuild` and add a one-OS `dotnet format --verify-no-changes` job. | S |
| P2-6 | No packaged artifact is ever launched, and the benchmark cannot fail a build | `docs/shipping.md:427`; `tools/bench.ps1:485` gates the target only when `-Runs` is exactly 10 | Add a post-package step that extracts each archive and runs `--bench=startup`, and a `-EnforceTargets` switch that exits non-zero on MISS. Add `-Warmup` (the first run is contaminated by cold page cache — the README says so itself). | M |
| P2-7 | The SDK is not actually pinned and the README says it is | `global.json` uses `rollForward: latestFeature`; `README.md:210` says it "pins the SDK to 10.0.100 with `rollForward: latestFeature`"; the measured binary used 10.0.403 | Pick `latestPatch` or `disable`, let `setup-dotnet` read `global.json`, and correct the README sentence. | S |
| P2-8 | No dependency or security scanning, and a published advisory becomes a repo-wide build failure | no `dependabot.yml`, no CodeQL; `TreatWarningsAsErrors=true` elevates NuGet audit NU1901–NU1904 to errors | Add Dependabot (nuget + actions) and CodeQL; add `WarningsNotAsErrors` for the audit IDs and let a scheduled job fail deliberately. | S |
| P2-9 | macOS `CFBundleVersion` is hardcoded to `1` | `packaging/macos/Info.plist:17-18`; `pack.sh:172-173` stamps only the marketing version | Stamp a monotonic build number (commit count or tag build metadata). | S |
| P2-10 | Platform metadata advertises a fraction of `SupportedFormats`, differently per OS | `packaging/linux/airglance.desktop:9` (6 MIME types, no tiff/avif/svg); `packaging/macos/Info.plist:41-47` (5 UTIs); MSIX declares all 12 | Generate the three lists from one source and add a CI check against `SupportedFormats`. | S |
| P2-11 | 60.7 MB of `docs/design` PNGs are tracked, plus a stale Store screenshot set | `docs/design` = 123 files; the repo is 71 MB tracked / 84 MB `.git`; `packaging/msix/screenshots/0*.png` (1920 × 1080) is referenced by nothing | Re-encode the UI screenshots as WebP. Measured on a 10-file sample of `docs/design/implemented-*.png`: 2 984 KB PNG → 2 066 KB lossless WebP (69 %) → **563 KB at q92 (19 %)**. Delete the superseded screenshot set. | S |
| P2-12 | Whole surfaces have no direct tests | `EditExportDialog` (0 references in `tests/`), `CommandLineOptions` (one incidental use), `LocalizationService` key parity, `ImageLoadService`, `TiledImageSource` (1 test), `ExifReader`, `ImageHeaderReader` | Start with three: a headless export-dialog test asserting format/quality/resize reach `ImageExporter`; a `[Theory]` over `CommandLineOptions.Parse`; a parity test that both localization halves have identical keys. | S–M |
| P2-13 | Fixture provenance and git attributes are incomplete | 25 committed TIFF fixtures have no generator in `tools/` and no notice entry; `.gitattributes` marks png/ico/icns/jpg/webp/gif binary but not `tif`/`avif` | Extend `tools/make-test-assets.py` to emit the TIFF set, add a notices line, and add `*.tif`, `*.tiff`, `*.avif`, `*.jfif` as binary. | M |

---

## P3 — documentation consistency

| ID | Contradiction | Fix |
| --- | --- | --- |
| P3-1 | `docs/shipping.md:428` says the archives do not include `LICENSE`/`THIRD-PARTY-NOTICES.md`, but `pack.ps1:246-252` and `pack.sh:162-166` both copy them, and the same document lists them at `:107-115`. Only the `README.md` half of that sentence is true. | Reword to "the archives ship `LICENSE` and `THIRD-PARTY-NOTICES.md` but not `README.md`". |
| P3-2 | `README.md:110` and `README.zh-CN.md:110` say Gatekeeper keeps blocking "until the app is notarised, which 0.5 is not"; every other line in both files says 0.7.0. | Replace `0.5` with `0.7.0`. |
| P3-3 | `docs/formats.md:168` says TIFF CMYK conversion and 16-bit colour paths "are not covered by fixtures", but `TiffPreviewDecoderTests.cs:432-658` parameterises `cmyk-*`, `planar*`, `palette*`, `float16/32`, `int16`, `rgba16`, `ycbcr-*` and `subifd-levels`; the fixture table at `:128-154` lists 3 of the ~28 TIFF fixtures `README.md:199` then describes correctly. | Narrow the gap sentence to multi-page / BigTIFF / 12-bit JPEG in TIFF, and regenerate the fixture table from the directory. |
| P3-4 | `docs/performance.md:8-12` calls the measured binary "the published 0.7.0 `win-x64` binary … version `0.7.0+c54dbec`", but `c54dbec` is four commits after tag `v0.7.0` (`dca66ab`) and the release workflow never built it. | Label it a post-tag Store build, or re-measure the actual `v0.7.0` zip. |
| P3-5 | `docs/shipping.md:19-20` and `:407` say `store.yml` builds x64 **and** arm64 MSIX, but `store.yml:55-57` builds x64 only. The WACK step cannot fail either: `continue-on-error: true` plus a hard `exit 0` (`:88`, `:146`). | Fix the doc, or add the arm64 leg now that the decoder exists; publish the WACK XML to the job summary and fail on non-signature findings. |
| P3-6 | The working tree still has the uncommitted 0.7.0 documentation pass (`CHANGELOG.md`, both READMEs, both architecture docs, both performance docs) and an untracked `docs/blog/`. | Commit or drop before the next change lands. |

---

## P4 — maintainability and accessibility

| ID | Item | Where | Note |
| --- | --- | --- | --- |
| P4-1 | Three god classes | `MainWindow` 2 815 lines / 65 `null!` control fields / 117 `new` in `BuildUi()`; `DualCanvasView` 2 117 (24-parameter `PaneSlot` ctor); `MainWindowViewModel` 2 241 | Extraction seams: `MainWindowChrome`, `CropController`, `ShortcutRouter`, `WindowLifecycle`; `DualPaneView`; `ImagePresentationController`, `EditSession`, `InfoCatalog`. Do this **after** P1-7/P1-8/P1-10 so the threading model is settled. |
| P4-2 | ~700 lines duplicated between the two TIFF paths, and already drifting | `TiffPreviewDecoder` vs `TiffRegionReader`: two copies of the format-detection ladder, the pixel decoder, the IFD reader, the planar/CMYK block (four times), and two `WriteBgra` implementations (one 32-bit store, one four byte stores) | Extract one `TiffRawFormat` + `TiffIfdReader`; it is also the prerequisite for P1-1 and P1-8 in the TIFF path. |
| P4-3 | 23 `async void` handlers, and crash information is discarded | `MainWindow.axaml.cs` has 19 of them; `App.axaml.cs:27-28` writes `UnhandledException` to `Console.Error` on a `WinExe`, which has no console | Route unhandled exceptions to a log file under the cache directory and to `MessageWindow`; wrap the `async void` handlers that do real work (`OnEditExportRequested` has a `finally` but no `catch`). |
| P4-4 | Accessibility gaps | `DualCanvasView.cs:283-285` captures `Tab` to switch panes (a WCAG 2.1.2 keyboard trap); `ImageViewer` is `Focusable = false` with no `AutomationProperties.Name`; `CoralSlider`/`HistogramChart`/`ToneBar`/`MeterBar` have no automation peers; only `Button` has a `:focus-visible` style; `MinWidth 720 / MinHeight 480` does not fit a 1366 × 768 display at 200 % scaling | Move pane switching to `Ctrl+Tab`, name the canvas, add `RangeValuePattern` or use a styled `Slider`, express minimum sizes so they still fit. |
| P4-5 | User-visible strings bypass the catalog | `"AirGlance — Compare"`, `"CROP SIZE"`, `"2400 × 1350"`, `"NON-DESTRUCTIVE"`, `"English"`/`"简体中文"`, `"… · cropped preview"`, the dual-view shortcut hints | They stay English in Chinese mode. Add keys and route through `Localization.T`/`Format`. |
| P4-6 | `ImageTransform.RotateClockwise(DecodedBitmap)` looks like dead code | `ImageTransform.cs:107-136` | It is **not** dead: `ImageTransformTests.cs:47-48` uses it to prove two rotations equal a half turn. It was mis-triaged as removable during this review. Either keep it (and say so in the summary comment) or delete it together with that test. |
| P4-7 | `ImageLoadService` caps entries, not bytes | `ImageLoadService.cs:11` | Four entries of up to 8192 × 8192 are four 256 MB buffers. `docs/architecture.md` already records this; a byte budget is the honest fix. |

---

## Verification harness

Everything above can be checked with what the repository already has:

```powershell
# first window, first frame, peak working set (NativeAOT publish only)
$env:AIRGLANCE_SETTINGS_DIR = "<empty dir>"
powershell -ExecutionPolicy Bypass -File tools/bench.ps1 -Runs 10

# 100 MP fit window and 100 % behaviour
powershell -ExecutionPolicy Bypass -File tools/bench.ps1 -Runs 5 -Image tools/testdata/photo-100mp.jpg

# where the startup time goes
$env:AIRGLANCE_STARTUP_TRACE = "1"
./AirGlance.exe path/to/photo.jpg
```

`dotnet test AirGlance.sln -c Release` covers the rest. P1-1, P1-6 and P1-10 should
move `peak_ws_mb` and `first_frame_ms`; P1-7, P1-8, P1-9 and P1-10 should be
judged by interaction feel plus a `dotnet-counters` Gen0/dependency check.

Sizes and counts in this document were measured on the tree described above:
tracked bytes 71.0 MB, `.git` 84.1 MB, `docs/design` 60.7 MB / 123 files,
213 `[Fact]`/`[Theory]`/`[AvaloniaFact]` attributes across 18 test files.

## Suggested sequence

1. **P0** — done here; run the app once to close P0-4 and add a `clap` fixture to close P0-2.
2. **P2-2, P2-1, P2-3** — cheapest CI/release risk reduction: cache the native build, make the release gate test what PR CI tests, collapse the version sources.
3. **P2-11** — repo slim-down, zero behavioural risk, largest visible number.
4. **P1-6, P1-7, P1-11, P1-9** — small diffs, immediate feel: one decode instead of two, one chrome pass instead of dozens, no cursor churn, thumbnails off the UI thread.
5. **P1-1, P1-2, P1-3, P1-4** — the decoder memory work; measure with `peak_ws_mb` before and after each.
6. **P1-8, P1-10, P1-12, P1-5** — remaining hot paths.
7. **P4-1/P4-2** — extraction, once the threading model has stopped moving.
8. **P3** — documentation pass, together with the next version bump.

## Notes on scope

Not in this backlog, by design: HEIC/HEIF, camera RAW, video, animation beyond
GIF, tabs, single-instance forwarding, a tray icon, auto-update, analytics,
code signing and notarization. Those are product decisions recorded in
`README.md` and `docs/shipping.md`, not unfinished optimizations.
