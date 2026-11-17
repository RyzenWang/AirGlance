# AirGlance

**Whole folders, one glance.** · 「一个文件夹，一眼看完。」

AirGlance is a desktop image viewer for Windows, macOS and Linux. It opens a folder, shows one image, and lets you step through the rest without tabs, libraries or a workspace. Version 0.1.1 is the current build; 0.1.0 was the first public build. It is deliberately small.

- [Download](#install) · [Keyboard](#keyboard) · [Performance](#performance) · [Known limitations](#known-limitations) · [Build from source](#build-from-source)

## What it does

- Opens from the command line, a drag-and-drop, or the open dialog. The window appears first; the image follows as soon as it is decoded.
- Browses one folder in natural order (`img2` before `img10`), with a 160 px filmstrip and wrap-around previous / next.
- Fit window, fit width and 100% views; the wheel zooms around the pointer, Shift + wheel pans sideways, dragging pans, double-click toggles 100% and fit.
- Applies the EXIF orientation before the first pixel is shown, and converts embedded ICC profiles (Display P3, Adobe RGB) to sRGB for display.
- Rotates and flips for display only. Save a copy as JPEG, PNG or WebP, or copy the image; the original file is never written.
- Fullscreen, a 3 / 5 / 10 second slideshow, and an information panel with file, image, ICC and EXIF fields.
- English and Chinese switch at runtime. Light, dark and follow-system themes; the system high-contrast setting is honoured.
- Settings and thumbnails stay on your machine. There is no account, no telemetry, and no network code.

## Install

0.1.1 is not on the product site yet. Download it from the [v0.1.1 GitHub release](https://github.com/RyzenWang/AirGlance/releases/tag/v0.1.1).

### Windows

1. Download `AirGlance-0.1.1-win-x64.zip` (or `win-arm64`).
2. Unzip it anywhere and run `AirGlance.exe`.

The 0.1 portable build is not code-signed, so SmartScreen warns the first time. Choose **More info → Run anyway**. The executable is a NativeAOT build; it does not need a .NET runtime installed.

### macOS

1. Download `AirGlance-0.1.1-osx-arm64.zip` or `-osx-x64.zip`.
2. Unzip `AirGlance.app`, then right-click the app and choose **Open** the first time. Gatekeeper will keep blocking a normal double-click until the app is notarised, which 0.1 is not.

### Linux

```bash
tar -xzf AirGlance-0.1.1-linux-x64.tar.gz
./AirGlance
```

The archive also contains `airglance.desktop` and PNG icons. Copy them into `~/.local/share/applications/` and `~/.local/share/icons/hicolor/` for a menu entry. The portable build registers no default handler.

### Platform status

The release publishes six archives. Runtime verification is narrower than that:

| Platform | Archive | NativeAOT binary actually run |
| --- | --- | --- |
| Windows x64 | `win-x64.zip` | **Verified** on the reference machine (0.1.1) |
| Linux x64 | `linux-x64.tar.gz` | **Verified** by the maintainer |
| Windows arm64 | `win-arm64.zip` | Unverified |
| macOS arm64 | `osx-arm64.zip` | Unverified |
| macOS x64 | `osx-x64.zip` | Unverified |
| Linux arm64 | `linux-arm64.tar.gz` | Unverified |

"Verified" means the AOT binary was launched and displayed an image. The other archives are produced by CI but have not been run on their platform. Clipboard paste into other applications and Windows display scaling above 100% are also unverified; see [shipping](docs/shipping.md).

## Keyboard

| Key | Action |
| --- | --- |
| `Ctrl+O` / `Ctrl+Shift+O` | Open an image / a folder |
| `←` `→` | Previous / next image; pan when the image is wider than the window |
| `PgUp` `PgDn` | Previous / next image, always |
| `Space` / `Backspace` | Next / previous image |
| `+` `-` | Zoom in / out around the pointer |
| `0` `1` `2` | Fit window / 100% / fit width |
| `R` / `Shift+R` | Rotate clockwise / counter-clockwise |
| `H` / `V` | Flip horizontally / vertically |
| `F11` / `Esc` | Fullscreen; exit fullscreen or close the window |
| `S` | Start or stop the slideshow; any key stops it |
| `I` / `C` | Information panel / filmstrip |
| `Ctrl+C` / `Ctrl+Shift+C` | Copy the image / copy the path |
| `Ctrl+Shift+S` | Save a copy |
| `Ctrl+Shift+L` | Switch English / Chinese |
| `F1` | The full shortcut list |

Right-click the image for open, copy, show-in-folder, open-with and settings.

## Performance

Measured on Windows 10 Pro 22H2 (19045), Intel Xeon E5-2697 v4, 32 GB RAM, .NET 10.0.14, NativeAOT `win-x64`. Ten fresh processes per row, median from `tools/bench.ps1`; the full method and raw values are in [docs/performance.md](docs/performance.md).

| Metric | Target | 0.1.1 |
| --- | --- | --- |
| Cold start to first window | ≤ 500 ms | 186.0 ms |
| 24 MP JPEG first frame | ≤ 500 ms | 299.4 ms |
| 100 MP JPEG first frame (fit window) | ≤ 2.0 s | 303.9 ms |
| 24 MP fit-window peak working set | ≤ 250 MB | 76.5 MB |
| Windows x64 portable zip | ≤ 30 MB | 15.51 MB |

The 0.1.1 build meets the startup and first-frame targets on the reference machine: the window is up in about 186 ms and a 24 MP image is on screen in about 299 ms. The window chrome is built in C# instead of XAML, which removed the largest first-frame cost; decoding overlaps window construction. A cold first run can be slower while the operating system scans the executable, so the median is the number to compare with the budget. The numbers are measured, not projections. See [docs/performance.md](docs/performance.md) for the raw runs.

## Known limitations

These are limitations, not a promise list.

- No HEIC, AVIF, RAW or TIFF. SkiaSharp cannot decode them and AirGlance does not ship platform codecs.
- GIF shows the first frame only. There is no animation and no video.
- No gallery, database, tags, ratings, search, batch conversion or rename, filters, layers, text, stitching, cloud sync or accounts.
- No tabs. The viewer is one folder per window. No tray icon and no single-instance forwarding.
- No cropping and no lossless JPEG rotation. Rotate and flip only change what is on screen.
- 16-bit PNGs are reported as 16-bit but displayed and re-saved as 8-bit.
- 100 MP images can be viewed at fit-window size comfortably, but zooming to 100% decodes the full image. Measured peak working set for a 10000 × 10000 JPEG at 100% is about **842 MB** on Windows x64; it did not crash on the 32 GB reference machine, but it is heavy on smaller machines.
- The 0.1 Windows portable build is unsigned and the macOS build is not notarised. The MSIX package is not published yet.

## Build from source

You need the .NET 10 SDK. The solution targets `net10.0` and pins the SDK to 10.0.100 with `rollForward: latestFeature`.

```bash
git clone https://github.com/RyzenWang/AirGlance.git
cd AirGlance
dotnet restore AirGlance.sln
dotnet build AirGlance.sln -c Release
dotnet test AirGlance.sln -c Release
```

Run it:

```bash
dotnet run --project src/AirGlance.App -- path/to/photo.jpg
```

NativeAOT publish for this machine:

```bash
dotnet publish src/AirGlance.App/AirGlance.App.csproj \
  -c Release -r win-x64 -p:PublishAot=true -p:StripSymbols=true
```

`packaging/scripts/pack.ps1` and `pack.sh` wrap the per-platform packaging. AOT builds cannot be cross-compiled, so each platform is built on its own machine.

Tests cover EXIF orientations 1–8, ICC conversion, scaled decoding, the natural sort, folder enumeration, settings recovery, thumbnail caching, export and filename safety, and the view model plus real window keyboard flow. See `tests/`.

## Command line

```text
AirGlance [options] [file or folder]

  -h, --help            Show help
      --version         Show the version
      --bench=startup   Measure first window, first frame and memory, then exit
      --bench=decode    Measure a fit-window decode, then exit
      --bench=scan      Measure folder enumeration and first thumbnails, then exit
  --                    Treat every following argument as a path
```

A folder opens its first supported image. The first displayable path wins; other arguments are ignored. `--bench=startup` prints `first_window_ms`, `first_frame_ms`, `decode_ms`, `image_mp` and `peak_ws_mb` to standard output.

## Privacy

AirGlance contains no `HttpClient`, no sockets, no telemetry, no crash upload and no update check. The only outbound action is a link you click yourself: the GitHub entry in the status bar opens your browser. Settings live in the per-user settings folder; thumbnails live in the per-user cache and are removed after 30 days.

## License

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

More detail: [architecture](docs/architecture.md) · [format matrix](docs/formats.md) · [performance method](docs/performance.md) · [shipping](docs/shipping.md) · [changelog](CHANGELOG.md).
