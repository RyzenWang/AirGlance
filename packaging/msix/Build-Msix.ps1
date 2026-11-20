#Requires -Version 5.1
<#
.SYNOPSIS
    Stages a published AirGlance build and packs an unsigned MSIX with MakeAppx.exe.

.DESCRIPTION
    The package is deliberately not signed. Microsoft Store accepts an unsigned
    upload and re-signs the approved package. A certificate whose subject matches
    the manifest Publisher is only needed for local install/WACK testing. This
    script never creates or invents a certificate or signature.

.PARAMETER Version
    Application version. It is normalized to the four-part MSIX version, for example
    0.1.0 becomes 0.1.0.0.

.PARAMETER Architecture
    Package architecture: x64 or arm64. The staged manifest Identity is updated to
    match this value.

.PARAMETER PublishDir
    Directory produced by dotnet publish for the matching runtime. Defaults to
    publish/win-<Architecture> under the repository root.

.PARAMETER OutputDir
    Directory for the resulting .msix. Defaults to artifacts/msix under the
    repository root.

.EXAMPLE
    ./packaging/msix/Build-Msix.ps1 -Version 0.1.0 -Architecture x64 `
        -PublishDir publish/win-x64 -OutputDir artifacts/msix
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",

    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64",

    [string]$PublishDir,

    [string]$OutputDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$repoRoot = [IO.Path]::GetFullPath((Join-Path $scriptDir "..\.."))

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$BasePath
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "A path cannot be empty."
    }

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function ConvertTo-PackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $parsed = $null
    if (-not [version]::TryParse($Value.Trim(), [ref]$parsed)) {
        throw "Version '$Value' is not a numeric MSIX version. Use one to four numeric parts, for example 0.1.0 or 0.1.0.0."
    }

    $build = 0
    if ($parsed.Build -ge 0) {
        $build = $parsed.Build
    }

    $revision = 0
    if ($parsed.Revision -ge 0) {
        $revision = $parsed.Revision
    }

    foreach ($component in @($parsed.Major, $parsed.Minor, $build, $revision)) {
        if ($component -lt 0 -or $component -gt 65535) {
            throw "MSIX version components must be between 0 and 65535. Received '$Value'."
        }
    }

    return ("{0}.{1}.{2}.{3}" -f $parsed.Major, $parsed.Minor, $build, $revision)
}

function Find-MakeAppx {
    $programFilesX86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
    if ([string]::IsNullOrWhiteSpace($programFilesX86)) {
        $programFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
    }

    if ([string]::IsNullOrWhiteSpace($programFilesX86)) {
        throw "Could not locate the Program Files (x86) directory, so MakeAppx.exe could not be found. Install the Windows 10/11 SDK and rerun."
    }

    $sdkBinRoot = Join-Path $programFilesX86 "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $sdkBinRoot -PathType Container)) {
        throw "MakeAppx.exe was not found. Expected the Windows SDK at '$sdkBinRoot'. Install the Windows 10/11 SDK (including the Windows SDK for Desktop apps) and rerun. No MSIX was created."
    }

    $candidates = @()
    foreach ($versionDirectory in Get-ChildItem -LiteralPath $sdkBinRoot -Directory -ErrorAction SilentlyContinue) {
        $candidate = Join-Path (Join-Path $versionDirectory.FullName "x64") "makeappx.exe"
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        $sortVersion = [version]"0.0.0.0"
        $parsedVersion = $null
        if ([version]::TryParse($versionDirectory.Name, [ref]$parsedVersion)) {
            $sortVersion = $parsedVersion
        }

        $candidates += [pscustomobject]@{
            Path       = $candidate
            SdkVersion = $versionDirectory.Name
            Version    = $sortVersion
        }
    }

    if ($candidates.Count -eq 0) {
        throw "MakeAppx.exe was not found under '$sdkBinRoot\<version>\x64\'. Install the x64 Windows 10/11 SDK and rerun. No MSIX was created."
    }

    return ($candidates | Sort-Object -Property Version -Descending | Select-Object -First 1)
}

$packageVersion = ConvertTo-PackageVersion -Value $Version

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path (Join-Path $repoRoot "publish") ("win-" + $Architecture)
}
else {
    $PublishDir = Resolve-FullPath -Path $PublishDir -BasePath (Get-Location).Path
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path (Join-Path $repoRoot "artifacts") "msix"
}
else {
    $OutputDir = Resolve-FullPath -Path $OutputDir -BasePath (Get-Location).Path
}

$manifestSource = Join-Path $scriptDir "AppxManifest.xml"
$assetsSource = Join-Path $scriptDir "Assets"

if (-not (Test-Path -LiteralPath $manifestSource -PathType Leaf)) {
    throw "The package manifest is missing: '$manifestSource'."
}

if (-not (Test-Path -LiteralPath $assetsSource -PathType Container)) {
    throw "The MSIX assets directory is missing: '$assetsSource'."
}

if (-not (Test-Path -LiteralPath $PublishDir -PathType Container)) {
    throw "Publish directory '$PublishDir' does not exist. Publish AirGlance for the $Architecture runtime first, for example: dotnet publish src/AirGlance.App/AirGlance.App.csproj -c Release -r win-$Architecture -p:PublishAot=true -o '$PublishDir'."
}

$executableSource = Join-Path $PublishDir "AirGlance.exe"
if (-not (Test-Path -LiteralPath $executableSource -PathType Leaf)) {
    throw "AirGlance.exe was not found in '$PublishDir'. Publish AirGlance for win-$Architecture before packing the MSIX."
}

foreach ($requiredAsset in @("Square150x150Logo.png", "Square44x44Logo.png", "StoreLogo.png")) {
    if (-not (Test-Path -LiteralPath (Join-Path $assetsSource $requiredAsset) -PathType Leaf)) {
        throw "Required MSIX asset '$requiredAsset' is missing from '$assetsSource'."
    }
}

$makeAppx = Find-MakeAppx
Write-Host "Using MakeAppx: $($makeAppx.Path) (SDK $($makeAppx.SdkVersion))"

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$packagePath = Join-Path $OutputDir ("AirGlance_{0}_{1}.msix" -f $packageVersion, $Architecture)

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("airglance-msix-" + [guid]::NewGuid().ToString("N"))
$staging = Join-Path $tempRoot "package"
$stagingAssets = Join-Path $staging "Assets"
$stagedManifest = Join-Path $staging "AppxManifest.xml"

try {
    New-Item -ItemType Directory -Path $stagingAssets -Force | Out-Null

    # Copy the published payload first so manifest and asset files win on collisions.
    foreach ($item in Get-ChildItem -LiteralPath $PublishDir -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $staging -Recurse -Force
    }

    $symbolExtensions = @(".pdb", ".mdb", ".dbg")
    $symbolFiles = Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object {
        $symbolExtensions -contains $_.Extension.ToLowerInvariant()
    }
    foreach ($symbolFile in $symbolFiles) {
        Remove-Item -LiteralPath $symbolFile.FullName -Force
    }

    foreach ($asset in Get-ChildItem -LiteralPath $assetsSource -Force) {
        Copy-Item -LiteralPath $asset.FullName -Destination $stagingAssets -Recurse -Force
    }

    # Keep the licence and up-to-date third-party notices in the package
    # even when the publish directory was produced before the notices changed.
    foreach ($noticeName in @("LICENSE", "THIRD-PARTY-NOTICES.md")) {
        $noticeSource = Join-Path $repoRoot $noticeName
        if (Test-Path -LiteralPath $noticeSource -PathType Leaf) {
            Copy-Item -LiteralPath $noticeSource -Destination (Join-Path $staging $noticeName) -Force
        }
    }

    Copy-Item -LiteralPath $manifestSource -Destination $stagedManifest -Force

    [xml]$manifest = Get-Content -LiteralPath $stagedManifest -Raw
    $identity = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
    if ($null -eq $identity) {
        throw "AppxManifest.xml does not contain a Package/Identity element."
    }

    [void]$identity.SetAttribute("Version", $packageVersion)
    [void]$identity.SetAttribute("ProcessorArchitecture", $Architecture)
    $manifest.Save($stagedManifest)

    if (-not (Test-Path -LiteralPath (Join-Path $staging "AirGlance.exe") -PathType Leaf)) {
        throw "AirGlance.exe was not copied into the staging directory."
    }

    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }

    Write-Host "Packing $Architecture MSIX version $packageVersion..."
    & $makeAppx.Path pack /d $staging /p $packagePath /o
    if ($LASTEXITCODE -ne 0) {
        throw "MakeAppx.exe failed with exit code $LASTEXITCODE. No MSIX was produced."
    }

    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "MakeAppx.exe reported success but did not create '$packagePath'."
    }
}
catch {
    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force -ErrorAction SilentlyContinue
    }

    throw
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$package = Get-Item -LiteralPath $packagePath
$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$sizeMb = ($package.Length / 1MB).ToString("0.00", [Globalization.CultureInfo]::InvariantCulture)

Write-Host ""
Write-Host "MSIX created (unsigned): $packagePath"
Write-Host "Size: $sizeMb MB"
Write-Host "SHA256: $hash"
Write-Host "Upload this unsigned package to Partner Center; Store certification signs it. Use a self-signed test certificate only for local install/WACK."
