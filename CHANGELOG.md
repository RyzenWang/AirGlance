# Changelog

All notable changes to AirGlance are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.0] - 2026-11-18

### Added

- Side-by-side dual-canvas mode in the same window. Press `D` or click the split-view
  button in the dock to enter or leave it. The left canvas keeps the current image,
  the right canvas opens the next image in the same folder, and each canvas keeps
  its own folder, image and navigation state.
- Independent A/B folders. Drop a folder or image on either canvas, or click its
  folder chip, to open a different folder on that side without moving the other one.
- Active-canvas model. Click a canvas or press `Tab`; the dock, arrow keys, wheel,
  information drawer and filmstrip target the active canvas. The active canvas has a
  2 px identity border and a badge ring; the inactive canvas is not dimmed and shows
  the original image. There is no top line or text label. `X` swaps the two canvases,
  and the divider can be dragged between 25% and 75% (double-click resets 50/50).
- A pane-specific right-click menu. Right-clicking a canvas activates that side first,
  then offers Open folder…, Open image…, Show in folder, Copy image/path, Swap A/B and
  Exit dual view; folder/image actions target only the clicked canvas.
- A fixed 50/50 split filmstrip: the left half lists A's folder, the right half lists
  B's folder, with independent scrolling and selection. An empty half shows only its
  A/B badge (no text, button or placeholder); the filmstrip is disabled when both
  sides are empty. The information drawer keeps its A/B tabs.
- Per-canvas previous/next capsules above each image. Each pair targets only its
  canvas; the inactive pair is at 60% opacity but remains clickable. The pair is
  hidden for an empty or single-image canvas.
- The right pane header reserves a fixed 46 px page-counter slot and caps folder
  names at 12 characters and file names at 14 characters with the extension kept.
  Zoom hides before dimensions, and the header stays clear of the window controls.
- The pane menu's primary item no longer keeps a permanent hover-like fill; hover
  is applied only while the pointer is over an item.
- Shared 30 × 30 A/B badges across the pane headers, dock, filmstrip halves and the
  pane menu; the filmstrip header uses a fixed 30 px row with vertically centred
  badge, folder name and count.
- Empty-canvas state with folder and image actions, pane-local loading and error
  states, drag/drop, per-canvas fit/zoom/pan, animated GIF playback and the existing
  decoder formats (including TIFF and SVG).
- `Esc` closes the pane menu, then the information drawer or split filmstrip, then
  exits fullscreen, then leaves dual view. Dual view keeps both canvases in fullscreen.
- Localized pane-menu items, filmstrip empty states and empty-canvas strings in English
  and Chinese.

### Changed

- The dual-canvas design at `docs/design/dual-view-v2.md` is implemented; the v1 and
  v2 interactive HTML prototypes remain available for comparison.
- The window-caption controls are now a root overlay so they remain available in
  dual view while the single-view chrome is hidden.

### Notes

- Dual view is a viewing mode. Per-canvas rotate/flip, save-copy and slideshow are
  deferred; copy (`Ctrl+C`) copies the active preview. Two 100 MP images at 100%
  simultaneously are not supported; each pane loads a bounded preview.
- Pair stepping and physical dual-monitor windows are not part of this release.

## [0.3.0] - 2026-11-18

### Added

- TIFF support through LibTiff.NET. The first page is decoded, the TIFF orientation
  tag is applied, alpha is preserved, and LZW, Deflate and JPEG-compressed TIFFs are
  covered by fixtures. 16-bit sources are reported as 16-bit and displayed at 8-bit.
- TIFF raw decoding feeds the existing tile cache so very large TIFFs can use the same
  512 px tile path as other large images.

### Changed

- `.tif` and `.tiff` are now supported extensions; TIFF is no longer in the
  unsupported matrix. CMYK, multi-page files, BigTIFF and exotic compression variants
  remain outside the first-page support contract.
- The portable package now carries BitMiracle.LibTiff.NET 2.4.660.

### Performance

- NativeAOT `win-x64`: 24 MP first window 222.0 ms median, first frame 424.7 ms;
  peak working set 84.5 MB.
- 100 MP fit-window first frame 327.0 ms; 100 MP at 100% peaks at 457.6 MB and
  settles at 88.2 MB working set.

## [0.2.0] - 2026-11-18

### Added

- Animated GIF playback. Every frame is composited with GIF disposal and blend
  rules, delays are honoured, and playback loops in the viewer. Frame memory is
  capped at 24 million pixels for long animations.
- SVG support through Svg.Skia. Static SVGs are rasterized at the requested display
  size, keep a transparent background, and appear in the folder list, open dialog and
  thumbnails like other images. CSS is rendered; JavaScript is not executed.
- The information panel reports the frame count for animated GIFs.

### Changed

- The supported format list now includes `.svg`; SVG is no longer in the unsupported
  matrix. Animated WebP and APNG remain still images.
- The portable package now carries Svg.Skia and HarfBuzzSharp 14.2.0; the Windows x64
  zip grows from 15.55 MB to 20.24 MB, still under the 30 MB budget.
- The overlay chrome now follows the measurements in the interaction prototype:
  18 px top-bar margins, 38 px chips, a 52 px dock with 38 × 40 buttons and 18 px
  icons, 46 px circular navigation at 72% resting opacity, a 570 px empty card and
  a 138 px filmstrip with its heading row.
- Icons moved to one 24 × 24 design grid and are scaled by a new `IconPath` control,
  so every glyph is centred in its box instead of drifting to the bottom-right and
  clipping when the box is smaller than the artwork.
- The window chip has four buttons again, including a light/dark toggle; the file
  chip is centred on the window; the fullscreen hint and canvas backdrop gradient
  are back; shadows sit under the chips, dock, filmstrip and info panel.
- The filmstrip gained its heading row, inset filename labels and dimmed non-current
  thumbnails. Below 900 px the folder chip hides and the file chip narrows.
- The wheel now zooms around the pointer; paging stays on the arrow keys,
  `PgUp` / `PgDn`, `Space` / `Backspace` and the edge buttons. `Shift + wheel`
  pans sideways.
- Fit window has its own frame-in-frame icon instead of sharing the fullscreen
  corner brackets, and the theme button swaps between a sun and a moon and names
  the theme it switches to.
- Dialogs share the shell's chrome: Settings, Save a copy, Shortcuts and message
  dialogs drop the native title bar for a draggable header and a close button.
- Primary, secondary and link buttons now centre their label vertically; previously
  the content presenter stretched the text and left it sitting high in the button.

### Documentation

- Every Markdown file under `docs/` now has a reciprocal language switch. Added
  Simplified Chinese translations for the architecture, format, performance and
  shipping docs, and an English version of the interaction design.
- Section 8 of the interaction design records the geometry pass and refreshed
  screenshots for the default, filmstrip, info, empty, dark and fullscreen states.

### Performance

- NativeAOT `win-x64`: 24 MP first window 227.1 ms median, first frame 404.9 ms;
  peak working set 82.4 MB.
- 100 MP fit-window first frame 358.6 ms; 100 MP at 100% peaks at 457.4 MB and
  settles at 87.9 MB working set.

## [0.1.3] - 2026-11-17

Interaction overhaul. The image now owns the whole window, and the controls float over
it instead of reserving fixed rows.

### Changed

- The window chrome is an overlay: top folder/file chips, a centered bottom dock with
  zoom, fit, 1:1, rotate, info, filmstrip, slideshow, fullscreen and settings, and
  left/right navigation buttons.
- Controls fade out after 2.5 seconds of idle and return when the pointer moves.
- The filmstrip is a bottom overlay drawer, collapsed by default; it no longer shrinks
  the image. The information panel is a right overlay drawer. The two panels are
  mutually exclusive.
- The empty/error state is a card with Open folder / Open image actions and keyboard
  hints.
- Mouse wheel browses previous/next; `Ctrl/Cmd + wheel` zooms at the pointer. The wheel
  accumulates small trackpad deltas so one gesture does not skip several images.
- `Left`/`Right` always browse; panning is drag or `Shift + wheel`. `Esc` closes an open
  panel or leaves fullscreen instead of closing the window. `F` toggles fullscreen in
  addition to `F11`.
- Fullscreen uses a chrome-less black canvas; moving the pointer reveals the dock.

### Performance

- NativeAOT `win-x64`: 24 MP first window 195.3 ms median, first frame 325.5 ms.
- 100 MP fit-window first frame 300.8 ms; 100 MP at 100% peaks at 454.8 MB and settles
  at 83.4 MB working set.

## [0.1.2] - 2026-11-17

Memory patch for very large images at high zoom. A 100 MP JPEG at 100 percent now keeps
only the visible tiles in memory instead of a full-resolution bitmap.

### Changed

- Images at or above 40 megapixels use a file-backed tile cache when the full-resolution
  view is requested. The decoded pixels are written once to a temporary tile file, the
  unmanaged full decode is freed, and the viewer draws 512 px tiles from the file.
- 100 MP at 100 percent: peak working set is 452.4 MB (was 842.0 MB), steady working set
  is 83.0 MB (was 836.1 MB), private bytes 54.5 MB. The temporary tile file is about
  400 MB while the image is open.
- Rotate and flip at 100 percent rebuild the tile cache from the original file instead
  of materialising a full bitmap. Peak memory stays at the tile-write level.
- PNG and other codecs that ignore a requested preview scale are downsampled in software
  for the fit-window preview, so a large PNG does not keep a full image just to show fit.
- Clipboard copy from a tiled image decodes a full-resolution bitmap on demand; saving a
  copy and encoding already used the original file.

### Fixed

- The status bar multiplication sign is written as U+00D7 instead of a mojibake
  character on systems where the source file was edited with a legacy code page.
- Abandoned tile files older than one day are removed at startup.

## [0.1.1] - 2026-11-17

Performance patch. The first frame is now inside the 0.1 budget on the reference machine.

### Changed

- MainWindow chrome is built in C# instead of XAML. The first XAML window load cost about 370 ms of the ~550 ms startup; the code-built window constructs the same layout in about 30 ms. Panel and context-menu XAML still load after the first frame.
- The window icon is decoded after the first frame instead of during window construction.
- Published 24 MP first frame is 299.4 ms median (was 665.0 ms); first window is 186.0 ms median (was 542.8 ms). See `docs/performance.md`.
- Version is 0.1.1.

### Fixed

- Status-bar separators rendered as the wrong character on some systems.
- Hidden files beginning with a dot are skipped on Unix (`FolderScanner`).

## [0.1.0] - 2026-11-17

First public release. AirGlance opens one folder at a time and shows one image at a time.

### Added

- Open an image from the command line, from drag and drop, or with `Ctrl+O`. Open a folder with `Ctrl+Shift+O`.
- Folder browsing with top-level scanning, natural file-name sorting, a filmstrip, previous/next navigation and wrap-around at both ends.
- Zoom modes: fit window (capped at 100 percent), fit width, actual size (100 percent) and manual zoom. Mouse wheel zoom is anchored at the pointer; Shift+wheel pans horizontally; pinch zoom and double-click toggling are supported.
- JPEG, PNG, WebP, BMP, GIF and ICO decoding. GIF displays its first frame only.
- EXIF orientation 1–8 is applied to JPEG images. Embedded ICC profiles in JPEG, PNG and WebP are converted to sRGB. The info panel reports the source profile and the conversion target.
- Display-only rotate clockwise/counter-clockwise and horizontal/vertical flip. Edits never modify the original file.
- Save a copy as JPEG, PNG or WebP with a size estimate. A new file is always created; existing files are never overwritten. JPEG copies flatten transparency onto white.
- Copy the displayed image or its file path to the clipboard.
- Fullscreen mode, a slideshow with 3, 5 or 10 second intervals, an information panel with EXIF/ICC/file data, and a keyboard shortcut reference.
- English and Simplified Chinese UI with a live language switch. No restart is required.
- Light, dark and system themes. The system high-contrast palette is layered on top, and the system accent is used for marks.
- Settings for language, theme, sort order, slideshow interval, checkerboard, remember-zoom, zoom mode, window size and window state. Settings apply immediately and are saved automatically.
- A 160 px thumbnail cache with a 30-day cleanup and a clear-cache command.
- `--bench=startup`, `--bench=decode` and `--bench=scan` benchmark modes, plus `--help`, `--version` and `--` option termination.
- `tools/bench.ps1` for repeated startup runs, median/p95 summaries and optional JSON output.
- Opt-in startup timing marks through `AIRGLANCE_STARTUP_TRACE=1`.

### Changed

- Images open with a screen-sized preview. The CLI target is preloaded while the window is being built. A full-resolution decode is requested only when the visible zoom exceeds the preview's scale by 15 percent; at 100 percent it is requested unless the preview is already at 99 percent scale or higher.
- Windows uses Avalonia's software compositor by default for faster startup; `AIRGLANCE_GPU_RENDER=1` opts into the GPU path.
- Fit window and fit width never enlarge an image past 100 percent.
- Display edits are non-destructive. Saving a copy re-decodes the full image, applies the current edit and writes a new file.
- No network access, telemetry, analytics or update check. Settings and thumbnails stay on the local machine.

### Known issues

- HEIC, AVIF, RAW, TIFF and video are not supported. Animated GIF is limited to its first frame; there is no animation playback.
- A 100 MP image at 100 percent requires a very large full-resolution decode: about 400 MB for the BGRA pixels and another 400 MB for the Avalonia bitmap, with additional copies during rotate/flip or export. Expect high peak memory and a long decode on slower machines.
- There is no batch processing, gallery/library view, tabbed window, tray icon or single-instance forwarding. Each launch is a separate process.
- There is no cropping and no lossless JPEG rotation. Rotate and flip affect the display and saved copies only; the original file is never rewritten.
- Windows portable 0.1 builds are unsigned. SmartScreen shows an unknown-publisher warning; verify `SHA256SUMS` and use **More info → Run anyway**.
- macOS 0.1 builds are unsigned and not notarized; ad-hoc signing is optional and not part of the release script. Gatekeeper may require right-click **Open** or **System Settings → Privacy & Security → Open Anyway**.
- 16-bit PNG files are reported as 16-bit but displayed and exported at 8-bit.
- PNG `eXIf` orientation is read for the information panel but is not applied to the displayed pixels. JPEG EXIF orientation is applied.
- Windows MSIX packages require a real publisher certificate. The manifest publisher placeholder `CN=AirWorks` must be replaced with the Partner Center publisher before Store submission.

[Unreleased]: https://github.com/RyzenWang/AirGlance/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/RyzenWang/AirGlance/releases/tag/v0.4.0
[0.3.0]: https://github.com/RyzenWang/AirGlance/releases/tag/v0.3.0
[0.2.0]: https://github.com/RyzenWang/AirGlance/releases/tag/v0.2.0
[0.1.3]: https://github.com/RyzenWang/AirGlance/releases/tag/v0.1.3
[0.1.2]: https://github.com/RyzenWang/AirGlance/releases/tag/v0.1.2
[0.1.1]: https://github.com/RyzenWang/AirGlance/releases/tag/v0.1.1
[0.1.0]: https://github.com/RyzenWang/AirGlance/releases/tag/v0.1.0
