#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the portable Windows zip for AirGlance and optionally an MSIX.

.DESCRIPTION
    Publishes AirGlance with NativeAOT by default and puts the flat contents of the
    publish directory into AirGlance-<version>-<runtime>.zip. PDBs are removed before
    zipping. With -Msix, the same publish directory is handed to
    packaging/msix/Build-Msix.ps1 for the MSIX architecture matching -Runtime. The
    MSIX is unsigned; see packaging/msix/README.md.

    Relative -OutputDir paths resolve from the repository root.

.PARAMETER Version
    Application version used for MSBuild and the zip file name. Defaults to 0.1.0.

.PARAMETER Runtime
    .NET runtime identifier to publish: win-x64 or win-arm64.

.PARAMETER Aot
    NativeAOT publish switch. Defaults to true. Disable it only for diagnostics with
    -Aot:$false; such archives are not comparable to the startup benchmark targets.

.PARAMETER OutputDir
    Artifact directory. Defaults to artifacts under the repository root.

.PARAMETER Msix
    Also build an unsigned MSIX for the architecture corresponding to -Runtime.

.EXAMPLE
    ./packaging/scripts/pack.ps1 -Version 0.1.0 -Runtime win-x64 -Aot

.EXAMPLE
    ./packaging/scripts/pack.ps1 -Version 0.1.0 -Runtime win-arm64 -Msix
#>
[CmdletBinding()]
param(
    [string]$Version = "0.1.0",

    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [switch]$Aot = $true,

    [string]$OutputDir = "artifacts",

    [switch]$Msix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$projectPath = Join-Path $repoRoot "src\AirGlance.App\AirGlance.App.csproj"

function Resolve-OutputPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "OutputDir cannot be empty."
    }

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Get-DotnetExecutable {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        $candidate = Join-Path $env:DOTNET_ROOT "dotnet.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw "dotnet was not found on PATH or in DOTNET_ROOT. Install the .NET 10 SDK, then reopen the terminal or set DOTNET_ROOT."
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

function Format-Megabytes {
    param(
        [Parameter(Mandatory = $true)]
        [long]$Bytes
    )

    return (($Bytes / 1MB).ToString("0.00", [Globalization.CultureInfo]::InvariantCulture) + " MB")
}

$Version = $Version.Trim()
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version cannot be empty."
}

if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z.+-]*$') {
    throw "Version '$Version' contains characters that are not safe for an artifact name. Use letters, digits, dots, plus signs, or hyphens."
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Project file not found: '$projectPath'."
}

$dotnetPath = Get-DotnetExecutable
$outputPath = Resolve-OutputPath -Path $OutputDir
$zipPath = Join-Path $outputPath ("AirGlance-{0}-{1}.zip" -f $Version, $Runtime)

$msixArchitecture = $null
$msixPath = $null
if ($Msix) {
    # Only the MSIX has a numeric four-part version requirement. Portable tags may
    # carry a prerelease suffix.
    $packageVersion = ConvertTo-PackageVersion -Value $Version

    if ($Runtime -eq "win-x64") {
        $msixArchitecture = "x64"
    }
    else {
        $msixArchitecture = "arm64"
    }

    $msixPath = Join-Path $outputPath ("AirGlance_{0}_{1}.msix" -f $packageVersion, $msixArchitecture)
}

$aotValue = "false"
if ($Aot.IsPresent) {
    $aotValue = "true"
}

if ($aotValue -eq "false") {
    Write-Warning "PublishAot is disabled. The resulting archive is not NativeAOT and its startup numbers are not comparable to the published targets."
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("airglance-pack-" + [guid]::NewGuid().ToString("N"))
$publishPath = Join-Path $tempRoot "publish"
$artifactPaths = @()

Write-Host "AirGlance package"
Write-Host "  Version:   $Version"
Write-Host "  Runtime:   $Runtime"
Write-Host "  AOT:       $aotValue"
Write-Host "  Output:    $outputPath"
Write-Host "  dotnet:    $dotnetPath"
Write-Host ""

try {
    New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

    Write-Host "Publishing..."
    & $dotnetPath publish $projectPath -c Release -r $Runtime -p:PublishAot=$aotValue -p:StripSymbols=true -p:Version=$Version -o $publishPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $publishedExe = Join-Path $publishPath "AirGlance.exe"
    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
        throw "dotnet publish completed but did not produce '$publishedExe'."
    }

    # The MIT licence and the third-party notices travel with every portable copy.
    foreach ($noticeName in @("LICENSE", "THIRD-PARTY-NOTICES.md")) {
        $noticeSource = Join-Path $repoRoot $noticeName
        if (Test-Path -LiteralPath $noticeSource -PathType Leaf) {
            Copy-Item -LiteralPath $noticeSource -Destination (Join-Path $publishPath $noticeName) -Force
        }
    }

    # The release archives must never contain symbols.
    $pdbFiles = Get-ChildItem -LiteralPath $publishPath -Recurse -File | Where-Object {
        $_.Extension -ieq ".pdb"
    }
    foreach ($pdbFile in $pdbFiles) {
        Remove-Item -LiteralPath $pdbFile.FullName -Force
    }

    $remainingPdbs = Get-ChildItem -LiteralPath $publishPath -Recurse -File | Where-Object {
        $_.Extension -ieq ".pdb"
    }
    if (@($remainingPdbs).Count -gt 0) {
        throw "PDB files could not be removed from the publish directory."
    }

    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Write-Host "Creating portable archive..."
    Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $zipPath -CompressionLevel Optimal

    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        throw "The portable zip was not created at '$zipPath'."
    }

    $artifactPaths += $zipPath

    if ($Msix) {
        $msixScript = Join-Path $repoRoot "packaging\msix\Build-Msix.ps1"
        if (-not (Test-Path -LiteralPath $msixScript -PathType Leaf)) {
            throw "MSIX build script not found: '$msixScript'."
        }

        Write-Host ""
        Write-Host "Building $msixArchitecture MSIX..."
        & $msixScript -Version $Version -Architecture $msixArchitecture -PublishDir $publishPath -OutputDir $outputPath
        if (-not (Test-Path -LiteralPath $msixPath -PathType Leaf)) {
            throw "MSIX packaging completed but '$msixPath' was not created."
        }

        $artifactPaths += $msixPath
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "The portable zip is missing: '$zipPath'."
}

foreach ($artifactPath in $artifactPaths) {
    if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
        throw "Expected artifact is missing: '$artifactPath'."
    }
}

Write-Host ""
Write-Host "Artifacts:"
$zipSizeMb = 0.0
$zipOverLimit = $false
$sizeLimitBytes = [long](30 * 1MB)

foreach ($artifactPath in $artifactPaths) {
    $artifact = Get-Item -LiteralPath $artifactPath
    $hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ("  {0}" -f $artifact.FullName)
    Write-Host ("    {0}   SHA256 {1}" -f (Format-Megabytes -Bytes $artifact.Length), $hash)

    if ($artifactPath -eq $zipPath) {
        $zipSizeMb = $artifact.Length / 1MB
        if ($artifact.Length -gt $sizeLimitBytes) {
            $zipOverLimit = $true
        }
    }
}

Write-Host ""
if ($Aot.IsPresent) {
    Write-Host "The portable zip contains the NativeAOT payload without PDB files."
}
else {
    Write-Host "The portable zip contains a framework-dependent build without PDB files."
}
if ($Msix) {
    Write-Host "The MSIX is unsigned; sign it with a trusted certificate before installing it."
}

if ($zipOverLimit) {
    Write-Warning ("The portable zip is {0:0.00} MB, above the 30 MB budget." -f $zipSizeMb)
    throw ("Portable zip size check failed: '{0}' is {1} MB (limit: 30 MB). The oversized artifact was left on disk for inspection." -f $zipPath, $zipSizeMb.ToString("0.00", [Globalization.CultureInfo]::InvariantCulture))
}
