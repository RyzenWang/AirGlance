# Shipping AirGlance 0.1.1

This is the release procedure for AirGlance 0.1.1 (2026-11-17). It covers the packaging scripts, NativeAOT builds, signing limits, the MSIX packages used only for Partner Center, WACK and the Store listing.

The 0.1 release is a portable, unsigned desktop release. It has no installer, updater, tray icon or single-instance forwarding. The packaging scripts and CI workflows exist in the tree, but no signing certificate or Store submission is part of 0.1.

## Release matrix

| Target | Public artifact | Signature | Built by | Distribution |
| --- | --- | --- | --- | --- |
| Windows x64 | `AirGlance-0.1.1-win-x64.zip` | None | `packaging/scripts/pack.ps1` | GitHub release |
| Windows arm64 | `AirGlance-0.1.1-win-arm64.zip` | None | `packaging/scripts/pack.ps1` on `windows-latest` | GitHub release |
| macOS arm64 | `AirGlance-0.1.1-osx-arm64.zip` (contains `AirGlance.app`) | None | `packaging/scripts/pack.sh` on `macos-15` | GitHub release |
| macOS x64 | `AirGlance-0.1.1-osx-x64.zip` (contains `AirGlance.app`) | None | `packaging/scripts/pack.sh` on `macos-15-intel` | GitHub release |
| Linux x64 | `AirGlance-0.1.1-linux-x64.tar.gz` | None | `packaging/scripts/pack.sh` on `ubuntu-22.04` | GitHub release |
| Linux arm64 | `AirGlance-0.1.1-linux-arm64.tar.gz` | None | `packaging/scripts/pack.sh` on `ubuntu-24.04-arm` | GitHub release |
| Windows x64 MSIX | `AirGlance_0.1.1.0_x64.msix` | None in the repository | `packaging/msix/Build-Msix.ps1` / `store.yml` | Partner Center only |
| Windows arm64 MSIX | `AirGlance_0.1.1.0_arm64.msix` | None in the repository | `packaging/msix/Build-Msix.ps1` / `store.yml` | Partner Center only |

Archive names use the three-part product version `0.1.1`. MSIX names use the four-part package version `0.1.1.0`. `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64` and `linux-arm64` are .NET runtime identifiers.

The release workflow publishes exactly six portable archives: Windows x64/arm64 zip, macOS x64/arm64 zip and Linux x64/arm64 tar.gz. It rejects MSIX files, so the Store packages never appear on a public GitHub release. The MSIX files are built by `store.yml` for review and Partner Center.

## Build prerequisites

- .NET SDK 10.0.100. `global.json` uses `rollForward: latestFeature` with `allowPrerelease: false`.
- NativeAOT native toolchain on each build host:
  - Windows: Visual Studio 2022 C++ desktop workload and a Windows SDK.
  - macOS: Xcode command line tools. `pack.sh` also uses `/usr/libexec/PlistBuddy` and `ditto`.
  - Linux: clang, zlib and the native dependencies required by .NET NativeAOT on the target distribution. ICU is required unless globalization is made invariant; AirGlance has not been changed to invariant globalization.
- The package versions in `Directory.Packages.props`. SkiaSharp 4.150.1 and Avalonia 11.3.22 share one native asset graph; do not replace either with a 3.x SkiaSharp package.
- For Store packaging: a Windows 10/11 SDK with `MakeAppx.exe`, `signtool.exe` and the Windows App Certification Kit.
- A display for manual smoke tests. The build and packaging steps are headless, but launching the app is not. Linux smoke tests need X11/Wayland or Xvfb.
- A code-signing identity is not in the repository. No `.pfx`, Developer ID identity, notarization credential or Store certificate is checked in.

NativeAOT cannot cross-compile between operating-system families. `pack.sh` refuses macOS packages on a non-Darwin host and Linux packages on a non-Linux host. The release workflow uses native macOS and Linux runners. The current Windows job builds both `win-x64` and `win-arm64` on `windows-latest`; no Windows arm64 hardware smoke test is recorded.

## Packaging scripts

| File | Purpose |
| --- | --- |
| `packaging/scripts/pack.ps1` | Publish an AOT Windows build, remove symbols, create a flat zip, optionally build an unsigned MSIX |
| `packaging/scripts/pack.sh` | Publish an AOT macOS or Linux build and create the platform archive |
| `packaging/msix/Build-Msix.ps1` | Stage a Windows publish directory and pack an unsigned MSIX with `MakeAppx.exe` |
| `packaging/msix/AppxManifest.xml` | Package identity, full-trust application, logos and image file associations |
| `packaging/macos/Info.plist` | macOS app bundle metadata |
| `packaging/linux/airglance.desktop` | Desktop entry included in the Linux tar.gz |
| `packaging/linux/icons` | PNG icons included in the Linux tar.gz |

Windows x64 or arm64:

```powershell
./packaging/scripts/pack.ps1 -Version 0.1.1 -Runtime win-x64 -Aot
./packaging/scripts/pack.ps1 -Version 0.1.1 -Runtime win-x64 -Aot -Msix
```

macOS or Linux, on the matching host:

```sh
./packaging/scripts/pack.sh 0.1.1 osx-arm64
./packaging/scripts/pack.sh 0.1.1 osx-x64
./packaging/scripts/pack.sh 0.1.1 linux-x64
./packaging/scripts/pack.sh 0.1.1 linux-arm64
```

The scripts call `dotnet publish -c Release -r <rid> -p:PublishAot=true -p:StripSymbols=true -p:Version=<version>`. `pack.ps1` also removes any remaining PDBs and enforces a 30 MB zip budget. `pack.sh` removes PDB, DBG and MDB files before packaging.

### Windows zip contents

`pack.ps1` zips the flat publish payload, not a wrapper folder:

```text
AirGlance.exe
av_libglesv2.dll
libHarfBuzzSharp.dll
libSkiaSharp.dll
LICENSE
THIRD-PARTY-NOTICES.md
```

The archive has `AirGlance.exe` at its root. It does not include `README.md`, but the MIT
license and third-party notices travel with the binary. The macOS bundle carries them in
`Contents/Resources`; the Linux tar.gz places them at the archive root.

### macOS app bundle

`pack.sh` builds `AirGlance.app` from the publish output, `packaging/macos/Info.plist` and `airglance.icns`:

```text
AirGlance.app/Contents/
  Info.plist
  MacOS/                 (publish output, including AirGlance and native libraries)
  Resources/airglance.icns
```

The current `Info.plist` declares:

- `CFBundleExecutable` = `AirGlance`;
- `CFBundleIdentifier` = `dev.airworks.AirGlance`;
- `CFBundleName` and `CFBundleDisplayName` = `AirGlance`;
- `CFBundleShortVersionString` = `0.1.1`, stamped by `pack.sh` from the version argument;
- `CFBundleVersion` = `1`, not stamped per build;
- `CFBundleIconFile` = `airglance.icns`;
- `LSMinimumSystemVersion` = `12.0`;
- document types for JPEG, PNG, WebP, BMP and GIF, with `LSHandlerRank` = `Alternate`.

The archive is created with `ditto -c -k --keepParent`, so `AirGlance.app` is at the zip root and executable modes are preserved. The script does not call `codesign`. The released macOS app is unsigned, not ad-hoc signed, not hardened and not notarized.

### Linux tar.gz contents

`pack.sh` creates a flat tar.gz with the publish payload, `airglance.desktop` and the icon set:

```text
AirGlance
airglance.desktop
icons/airglance-16.png
...
icons/airglance-512.png
```

The archive has no wrapper directory. The desktop entry uses `Exec=AirGlance %f`, so the binary must be on `PATH` or the `Exec` line must be edited after extraction.

### MSIX build

`Build-Msix.ps1` stages a Windows publish directory, copies the manifest and `Assets/`, removes symbol files, rewrites `Identity/@Version` and `Identity/@ProcessorArchitecture`, and calls `MakeAppx.exe pack`. It does not create or install a certificate.

```powershell
./packaging/msix/Build-Msix.ps1 `
  -Version 0.1.1 `
  -Architecture x64 `
  -PublishDir publish/win-x64 `
  -OutputDir artifacts/msix
```

The output is `AirGlance_0.1.1.0_x64.msix`, or `AirGlance_0.1.1.0_arm64.msix` for `-Architecture arm64`. `store.yml` runs this for both architectures. The packages are unsigned.

## Smoke test

Packaging is not a launch test. Run the benchmark from each extracted artifact before publishing:

```powershell
./AirGlance.exe --bench=startup tests/Assets/orientation-1.jpg
```

```sh
./AirGlance --bench=startup tests/Assets/orientation-1.jpg
```

A successful run prints `first_window_ms`, `first_frame_ms`, `decode_ms`, `image_mp` and `peak_ws_mb`, then exits 0. A failure prints `error=...` and exits 1. `--version` opens a window rather than writing to the console, so it is not a headless smoke test.

Also open a folder, navigate with the arrow keys, toggle fullscreen, switch language and save a copy on each OS before publishing. These checks are manual. The CI workflows run the test suite and package the binaries, but they do not launch the packaged app or run `--bench`; no packaged-artifact smoke result is recorded in the repository.

## Windows 0.1: unsigned portable build

The Windows zip is not Authenticode-signed. Windows shows "Windows protected your PC" / "Unknown publisher" for a new unsigned binary.

To continue after verifying the download hash:

1. Check the file against the `SHA256SUMS.txt` release asset.
2. Extract the zip. `AirGlance.exe` is at the archive root.
3. If SmartScreen appears, choose **More info**, then **Run anyway**.
4. If the extracted file still carries the Mark-of-the-Web, `Unblock-File .\AirGlance.exe` can clear it.

Do not disable SmartScreen or Windows Defender to run AirGlance. Enterprise policy may block an unsigned binary; that is outside the app's control.

There is no Windows installer. The zip does not register file associations, add an uninstall entry or create a Start menu shortcut. The MSIX is a Store package, not a workaround for unsigned public distribution.

A real Authenticode certificate and timestamp would remove the unknown-publisher warning. No certificate is used for 0.1.

## macOS 0.1: unsigned build

The macOS release archive contains an unsigned `AirGlance.app`. It is not ad-hoc signed, not hardened and not notarized. Gatekeeper may show "AirGlance cannot be opened because the developer cannot be verified."

To open it:

1. Verify the archive against `SHA256SUMS.txt`.
2. Extract it.
3. Right-click `AirGlance.app`, choose **Open**, then confirm **Open**.
4. If macOS still blocks it, open **System Settings → Privacy & Security** and use **Open Anyway** for AirGlance.

Do not disable Gatekeeper globally. On Apple silicon, an x64 build runs only with Rosetta installed. An arm64 build is native.

An ad-hoc signature is optional for local testing, but it is not part of `pack.sh` or the release workflow:

```sh
codesign --force --deep --sign - AirGlance.app
codesign --verify --deep --strict --verbose=2 AirGlance.app
```

An ad-hoc signature does not satisfy Gatekeeper or notarization requirements. There is no Developer ID certificate, hardened runtime, notarization or stapled ticket in 0.1. macOS x64 and arm64 are separate builds; no universal binary is produced.

## Linux 0.1: tar.gz install

Extract the archive and run the binary:

```sh
tar -xzf AirGlance-0.1.1-linux-x64.tar.gz
./AirGlance
```

A per-user install, matching `packaging/linux/README.md`:

```sh
mkdir -p "$HOME/.local/bin" "$HOME/.local/share/applications"
install -m 0755 AirGlance "$HOME/.local/bin/AirGlance"
install -m 0644 airglance.desktop "$HOME/.local/share/applications/airglance.desktop"

for icon in icons/airglance-*.png; do
    size="${icon##*/airglance-}"
    size="${size%.png}"
    install -Dm0644 "$icon" \
        "$HOME/.local/share/icons/hicolor/${size}x${size}/apps/airglance.png"
done
```

Make sure `$HOME/.local/bin` is on `PATH`; the desktop entry uses `Exec=AirGlance %f`. Refresh desktop and icon caches if needed:

```sh
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
gtk-update-icon-cache -f -t "$HOME/.local/share/icons/hicolor" 2>/dev/null || true
```

For a system-wide install, use `/usr/local/bin/AirGlance`, `/usr/share/applications/airglance.desktop` and `/usr/share/icons/hicolor`. The desktop entry only advertises the supported MIME types; it does not make AirGlance the default viewer. There is no `.deb`, `.rpm`, Flatpak or AppImage for 0.1. The Linux artifacts are built on Ubuntu 22.04/24.04 runners for the matching architecture; musl/Alpine is not tested.

## MSIX for Partner Center

The MSIX packages are only for the Microsoft Store submission:

- `AirGlance_0.1.1.0_x64.msix`
- `AirGlance_0.1.1.0_arm64.msix`

They are not public release assets. `store.yml` builds them on demand and uploads them as workflow artifacts.

### Package identity and publisher

`packaging/msix/AppxManifest.xml` is checked in. Its identity is:

```xml
<Identity
  Name="AirWorks.AirGlance"
  Publisher="CN=AirWorks"
  Version="0.1.1.0"
  ProcessorArchitecture="x64" />
```

The `Publisher` value `CN=AirWorks` is a placeholder and must be replaced. Use the exact publisher subject shown in Partner Center at **Account settings → Publisher profile**. The package publisher, the signing certificate subject and the Partner Center account must agree. If they do not, installation, WACK or Store ingestion fails.

`Name` is a placeholder identity too. Reserve the AirGlance name in Partner Center and use the value Partner Center assigns. `DisplayName` remains `AirGlance` and `PublisherDisplayName` is `AirWorks`.

The manifest also declares:

- `TargetDeviceFamily` `Windows.Desktop` with `MinVersion="10.0.19041.0"` and `MaxVersionTested="10.0.26100.0"`;
- `Application` with `Executable="AirGlance.exe"` and `EntryPoint="Windows.FullTrustApplication"`;
- the `runFullTrust` restricted capability;
- file associations for `.jpg`, `.jpeg`, `.jfif`, `.png`, `.webp`, `.bmp`, `.gif` and `.ico`;
- the package, tile and Store logos from `packaging/msix/Assets`.

The manifest does not request network capabilities. It also has no telemetry component.

The minimum Windows version is set in the manifest to Windows 10 version 2004 (build 19041). That value is a manifest declaration, not a measured compatibility matrix; Windows 10/11 x64 and arm64 are the intended targets.

### Signing

A Store MSIX needs a code-signing certificate whose subject matches the manifest publisher. A self-signed certificate can be used on a test machine after installing it in the trusted store, but it is not a publisher certificate for the Store. Replace the placeholder and use a real certificate before submission.

Pack with the Windows SDK:

```powershell
makeappx.exe pack `
  /d artifacts/msix/x64 `
  /p AirGlance_0.1.1.0_x64.msix `
  /o
```

Sign and verify:

```powershell
signtool.exe sign /fd SHA256 /a `
  /f publisher-certificate.pfx `
  /p "<password>" `
  AirGlance_0.1.1.0_x64.msix

signtool.exe verify /pa /v AirGlance_0.1.1.0_x64.msix
```

Use a timestamp server in a production signature. Do not commit the `.pfx` or its password. Build and sign the arm64 package separately for its architecture.

### WACK

`packaging/msix/README.md` and `.github/workflows/store.yml` use the WACK command-line form. Run it as administrator on a clean Windows 10/11 VM:

1. Install the signed MSIX on the test VM after trusting its certificate, or use your normal signed-package process.
2. Launch AirGlance once and close it.
3. Reset and run WACK:

```powershell
$appcert = Get-ChildItem `
  'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe' `
  -ErrorAction Stop

& $appcert.FullName reset

& $appcert.FullName test `
  -appxpackagepath (Resolve-Path '.\AirGlance_0.1.1.0_x64.msix').Path `
  -reportoutputpath (Join-Path (Get-Location) 'wack-x64.xml')
```

4. Review `wack-x64.xml` and the WACK logs. Fix every failure or warning that blocks Store certification.
5. Repeat for the arm64 package on arm64 Windows.

`store.yml` runs WACK against the unsigned packages as an informational preflight and uploads the reports. Signature-related failures are expected for unsigned packages. A meaningful WACK pass requires a trusted signed package, and no signed package or passing WACK report is recorded for 0.1.1.

## Store listing

Submit through Partner Center as an MSIX desktop package.

| Listing field | Value |
| --- | --- |
| Product name | AirGlance |
| Publisher | AirWorks |
| Category | 照片与视频 (Photos & video) |
| Price | Free |
| Trial / in-app purchases / ads | None |
| Languages | English, Simplified Chinese (`zh-CN`) |
| Network use | None |
| Telemetry | None |

The listing needs:

- a public privacy policy URL. Even though the app collects nothing, the Store requires a policy. State that settings and thumbnails stay on the device and that no personal data is collected or transmitted;
- at least one desktop screenshot, 1366 × 768 or larger, showing the viewer, filmstrip, info panel and settings;
- the package, tile and Store logos already under `packaging/msix/Assets`;
- system requirements: Windows 10/11, x64 or arm64. Memory depends on the image; 100 MP at 100 percent needs a large full-resolution decode;
- an age rating questionnaire. AirGlance has no user-generated content, accounts, ads or network features;
- certification notes: local image viewer, full-trust desktop package, no network calls, no telemetry, supported formats and known limitations.

The category is selected in Partner Center. Use **照片与视频** for the Chinese listing; the English Store UI shows the corresponding Photos & video category.

The listing, screenshots, privacy policy and submission are not complete for 0.1.1.

## No network and no telemetry

AirGlance contains no HTTP client, socket, analytics SDK, crash upload or update check. Settings and thumbnail data are local files. The only external navigation is an explicit click on the GitHub project link or the third-party notices link, which opens the default browser through the shell. That is a user action, not telemetry.

This claim was checked against the source: there are no `HttpClient`, `WebRequest`, `Socket` or telemetry calls in `src/`. Keep it true when adding features. A Store privacy policy still needs to exist even when no data is collected.

## SHA256SUMS.txt

The release workflow writes a checksum file named `SHA256SUMS.txt`. It contains all six public archives, sorted by file name, in standard `sha256sum` format. It deliberately excludes MSIX packages.

```text
<64 lowercase hex characters>  AirGlance-0.1.1-linux-arm64.tar.gz
<64 lowercase hex characters>  AirGlance-0.1.1-linux-x64.tar.gz
<64 lowercase hex characters>  AirGlance-0.1.1-osx-arm64.zip
<64 lowercase hex characters>  AirGlance-0.1.1-osx-x64.zip
<64 lowercase hex characters>  AirGlance-0.1.1-win-arm64.zip
<64 lowercase hex characters>  AirGlance-0.1.1-win-x64.zip
```

The checked-in workflow uses the `.txt` suffix; it is the SHA256SUMS file for the release. The conventional unsuffixed `SHA256SUMS` name is not produced by the current workflow.

`SHA256SUMS.txt` is generated by `.github/workflows/release.yml` after downloading the six archives. The packaging scripts print individual hashes but do not write the checksum file themselves.

Verify a download:

```powershell
Get-FileHash .\AirGlance-0.1.1-win-x64.zip -Algorithm SHA256
```

```sh
shasum -a 256 AirGlance-0.1.1-osx-arm64.zip
sha256sum -c SHA256SUMS.txt
```

If an MSIX is shared outside Partner Center, hash it separately and state which certificate signed it. The public release does not include MSIX files.

## Release workflow overview

Three workflows are checked in:

| Workflow | Trigger | What it does |
| --- | --- | --- |
| `.github/workflows/build.yml` | Push or pull request | Restore, build and test on Windows, Linux and macOS |
| `.github/workflows/release.yml` | Tag `v*` | Test, package six archives, create `SHA256SUMS.txt`, publish a GitHub release |
| `.github/workflows/store.yml` | Manual dispatch | Build unsigned x64 and arm64 MSIX packages, run informational WACK, upload artifacts |

`release.yml`:

1. Runs the test suite on Ubuntu.
2. Packages `win-x64` and `win-arm64` on `windows-latest`, `osx-arm64` on `macos-15`, `osx-x64` on `macos-15-intel`, `linux-x64` on `ubuntu-22.04` and `linux-arm64` on `ubuntu-24.04-arm`.
3. Downloads exactly six archives and rejects any MSIX in the release set.
4. Sorts the archive names and writes `SHA256SUMS.txt`.
5. Builds release notes from `CHANGELOG.md` for the tag version, falling back to the tag message.
6. Creates or updates the GitHub release `v0.1.1` with the six archives and `SHA256SUMS.txt`.

`store.yml` does not submit anything to Partner Center. It builds the MSIX files, runs WACK when the kit is present, and uploads the packages and reports for manual review. Partner Center submission remains a manual action after the publisher is replaced and the packages are signed.

## What is not done yet

- Windows portable archives, macOS app bundles and MSIX packages are unsigned. There is no Authenticode certificate, Developer ID identity, notarization, hardened runtime, MSIX publisher certificate or timestamp.
- macOS release archives are not ad-hoc signed either. `pack.sh` does not call `codesign`. The optional ad-hoc signing command is documented for local testing only.
- The MSIX publisher is still the placeholder `CN=AirWorks`, and the package identity `AirWorks.AirGlance` is a placeholder. Both must be replaced before Partner Center submission.
- No signed MSIX has been installed, no WACK pass has been recorded and no Store submission or listing approval has been completed for 0.1.1.
- Windows arm64 is built on the x64 `windows-latest` runner. No Windows arm64 hardware launch test is recorded. Linux arm64 and macOS x64 are built on native runners, but no packaged-artifact smoke test is recorded in CI.
- No packaged-artifact launch test or `--bench` run is part of the workflows. The release workflow tests the source tree, not the extracted zip, tar.gz or app bundle.
- The Windows zip, macOS app and Linux tar.gz do not include `LICENSE`, `THIRD-PARTY-NOTICES.md` or `README.md`. The repository contains these files, but the packaging scripts do not copy them into the archives.
- The macOS `CFBundleVersion` is fixed at `1`; `pack.sh` stamps only `CFBundleShortVersionString`. There is no monotonic build number.
- There is no Windows installer, Linux package format beyond tar.gz, auto-update, crash reporting or telemetry. The absence of telemetry is intentional.
- There is no single-instance forwarding and no tray icon.
- The release checksum file is named `SHA256SUMS.txt`, not `SHA256SUMS`. The contents are standard `sha256sum` output.
- The Store listing, screenshots and privacy policy are not final. The manifest `MinVersion` (Windows 10 build 19041) and macOS `LSMinimumSystemVersion` (12.0) are declarations, not a measured compatibility matrix.
- There is no universal macOS binary. macOS x64 and arm64 are separate downloads.
- No workflow contains a signing step, and no signing certificate is stored in the repository.

## Related documents

- [Architecture](architecture.md)
- [Format support matrix](formats.md)
- [Changelog](../CHANGELOG.md)
- [Third-party notices](../THIRD-PARTY-NOTICES.md)
- [Linux packaging notes](../packaging/linux/README.md)
- [MSIX packaging notes](../packaging/msix/README.md)
- [README](../README.md)
