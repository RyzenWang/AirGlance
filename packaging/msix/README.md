# AirGlance MSIX packaging

This directory contains the Desktop Bridge (full-trust) MSIX manifest and the
`Build-Msix.ps1` helper for AirGlance 0.6.0. The package uses the Windows desktop
application model: it executes the normal `AirGlance.exe` with the
`Windows.FullTrustApplication` entry point and declares the `runFullTrust`
restricted capability.

## Layout

| File | Purpose |
| --- | --- |
| `AppxManifest.xml` | Package identity, visual assets, full-trust application, and image file associations. |
| `Assets/` | PNG package and tile logos referenced by the manifest. |
| `Build-Msix.ps1` | Stages a publish directory and calls `MakeAppx.exe`. |

## Building a package

Prerequisites:

- The .NET 10 SDK.
- A Windows 10/11 SDK that includes `MakeAppx.exe` (normally installed by the
  "Windows SDK for Desktop apps" workload).
- A published, self-contained AirGlance build for the target architecture.
  The AOT publish is recommended because it is what the release packages use:

  ```powershell
  dotnet publish src/AirGlance.App/AirGlance.App.csproj `
    -c Release -r win-x64 `
    -p:PublishAot=true -p:StripSymbols=true -p:Version=0.6.0 `
    -o publish/win-x64
  ```

Then pack:

```powershell
pwsh -File ./packaging/msix/Build-Msix.ps1 `
  -Version 1.0.0 `
  -Architecture x64 `
  -PublishDir publish/win-x64 `
  -OutputDir artifacts/msix
```

`Build-Msix.ps1`:

1. Searches `%ProgramFiles(x86)%\Windows Kits\10\bin\<sdk>\x64\makeappx.exe`
   and selects the newest SDK version. It stops with an actionable error if the
   SDK or `MakeAppx.exe` is absent.
2. Copies `AirGlance.exe`, the rest of the publish output, `AppxManifest.xml`,
   and `Assets/` into a temporary staging directory. PDB and other symbol files
   are removed.
3. Rewrites the staged manifest's `Identity/@Version` to the four-part MSIX
   form (`0.3.0` becomes `0.3.0.0`) and sets
   `Identity/@ProcessorArchitecture` to the requested architecture. The checked
   in manifest defaults to `x64`; the build output is architecture-specific.
4. Calls `MakeAppx.exe pack /d <staging> /p <package> /o`.

The output is named `AirGlance_<four-part-version>_<architecture>.msix`, for
example `artifacts/msix/AirGlance_1.0.1.0_x64.msix`. The Store submission is x64-only for this update; the arm64 MSIX can be built and now includes the ARM64 AVIF decoder, but it is not part of this upload.

`Build-Msix.ps1` does not create a certificate and does not sign the package.
The output is an unsigned container.

## Manifest notes

- `Publisher="CN=D87D6846-5A2B-4D82-BDB7-8E739EB3F35B"` is the exact Partner
  Center publisher identity. The package is uploaded unsigned; Store
  certification re-signs it with Microsoft's certificate.
- `Name="RyzenWang.AirGlance"` and `PublisherDisplayName="RyzenWang"` are the
  reserved Store identity values.
- The package logo (`StoreLogo.png`) is declared as `Properties/Logo`. The Appx
  schema has no `StoreLogo` attribute on `uap:VisualElements`; that element uses
  `Square150x150Logo` and `Square44x44Logo`, which are also declared.
- The manifest registers `.jpg`, `.jpeg`, `.jfif`, `.png`, `.webp`, `.bmp`,
  `.gif`, `.ico`, `.tif`, `.tiff`, `.avif`, and `.svg` through
  `windows.fileTypeAssociation`. Registration makes
  AirGlance available in "Open with"; Windows does not make it the default
  silently. The user remains in control of default apps.
- The minimum tested target is Windows 10 version 2004 (build 19041).
- The only capability requested is `runFullTrust`, which is required for a
  Desktop Bridge application.

## WACK self-test

The Windows App Certification Kit (WACK) is a local pre-submission check, not a
replacement for Partner Center certification. It must be run from an elevated
command prompt on a clean Windows VM. The public workflow treats WACK as an
informational check because the build it produces is unsigned; signature tests
cannot pass until a trusted signing certificate is installed.

A meaningful self-test therefore needs a signed package:

1. Sign a copy of `AirGlance_1.0.1.0_x64.msix` with a local test certificate
   certificate. The certificate subject must match the manifest `Publisher`.
   Do not use an untrusted certificate for a Store submission.
2. On a clean Windows 10/11 test VM, trust that certificate and install the
   whose subject exactly matches the manifest Publisher, then install the copy.
3. Run WACK as administrator:

   ```powershell
   $appcert = Get-ChildItem `
     'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe' `
     -ErrorAction Stop

   & $appcert.FullName reset

   & $appcert.FullName test `
     -appxpackagepath (Resolve-Path '.\AirGlance_0.3.0.0_x64.msix').Path `
     -reportoutputpath (Join-Path (Get-Location) 'wack-x64.xml')
   ```

4. Review `wack-x64.xml` (and the WACK logs if a test fails) before submitting.


WACK can inspect a package path, but a full test depends on a trusted signature
and a working installed app. The Store workflow in `.github/workflows/store.yml`
runs `appcert.exe` when the kit is present and uploads the reports for manual
review; it deliberately does not make an unsigned package's WACK result a gate.

## Partner Center submission

- Reserve or confirm the product name in Partner Center and copy the exact
  package identity values into `AppxManifest.xml`. In particular, the publisher
  in the manifest, the publisher in the signing certificate, and the publisher
  in the Partner Center account must match.
- Build the x64 package, bump the four-part package version for every submission,
  and upload the unsigned `.msix`. Microsoft Store re-signs approved packages.
  ARM64 is not part of this Store upload; the portable ARM64 build remains on GitHub releases.
- A full-trust package uses the restricted `runFullTrust` capability. Be ready
  to explain the functionality that requires full trust if the Store asks.
- The release workflow publishes portable archives only. It intentionally never
  publishes an MSIX and never submits anything to Partner Center. Store upload remains a manual action after a Store-ready build.

## Why the portable Windows build is unsigned in 0.1

The 0.1 portable Windows artifact is a zip of a NativeAOT build, not an
installer. Adding a trusted signature would require a code-signing certificate,
secure key handling, timestamping infrastructure, and a release identity that
the project has not established yet. The project therefore ships the portable
build unsigned and documents the consequence: Windows SmartScreen may warn on
first launch. The published SHA256SUMS file can confirm that a download matches
the released bytes, but a hash is not a publisher signature.

MSIX builds are uploaded unsigned for Store distribution. A self-signed
development certificate is needed only for local install/WACK testing; the
packaging scripts never fabricate a publisher certificate.
