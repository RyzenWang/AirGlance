#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Windows x64 AVIF decoder (libavif + dav1d + shim).

.DESCRIPTION
    Produces native/avif/bin/<rid>/airglance_avif.dll. Sources are downloaded into
    native/avif/.cache unless -LibavifSource and -Dav1dSource are supplied. The
    build needs Visual Studio's C++ tools, CMake, Ninja, Python + Meson, and NASM.
    Without NASM, dav1d is built without x86 SIMD (slower, smaller).

.PARAMETER Rid
    Only win-x64 is supported for now.

.PARAMETER Clean
    Removes the cached build and install directories before building.

.PARAMETER SkipDownload
    Fails instead of downloading missing source archives.

.PARAMETER LibavifSource
    An already extracted libavif 1.4.2 source directory.

.PARAMETER Dav1dSource
    An already extracted dav1d 1.5.5 source directory.

.PARAMETER NasmDirectory
    Directory containing nasm.exe. Falls back to PATH and native/avif/tools.
#>
[CmdletBinding()]
param(
    [ValidateSet("win-x64")]
    [string]$Rid = "win-x64",

    [switch]$Clean,

    [switch]$SkipDownload,

    [string]$LibavifSource = "",

    [string]$Dav1dSource = "",

    [string]$NasmDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$cacheDirectory = Join-Path $scriptDirectory ".cache"
$buildDirectory = Join-Path $cacheDirectory "build"
$sourceDirectory = Join-Path $cacheDirectory "src"
$prefixDirectory = Join-Path $cacheDirectory "prefix"
$outputDirectory = Join-Path $scriptDirectory ("bin\" + $Rid)
$outputPath = Join-Path $outputDirectory "airglance_avif.dll"

$libavifVersion = "1.4.2"
$dav1dVersion = "1.5.5"
$libavifArchive = Join-Path $sourceDirectory "libavif-$libavifVersion.tar.gz"
$dav1dArchive = Join-Path $sourceDirectory "dav1d-$dav1dVersion.tar.gz"
$libavifHash = "2B645287340BA5A631D268B551DC2D72BD73AC33335962DD36DCDB6D8366921D"
$dav1dHash = "76AD07AB121D68407B64A6F50F540C90CA0B066D178BDC9ADBEA14ACF4CE25A9"

function Write-Step([string]$Message)
{
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-VisualStudioPath
{
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf))
    {
        throw "vswhere.exe was not found. Install Visual Studio 2022/2026 with the C++ desktop workload."
    }

    $installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installPath))
    {
        throw "No Visual Studio installation with the C++ tools workload was found."
    }

    return $installPath.Trim()
}

function Get-ToolPath([string]$Name, [string]$Fallback)
{
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command)
    {
        return $command.Source
    }

    if (-not [string]::IsNullOrWhiteSpace($Fallback) -and (Test-Path -LiteralPath $Fallback -PathType Leaf))
    {
        return $Fallback
    }

    throw "$Name was not found. Add it to PATH or install it with the Visual Studio C++ workload."
}

function Get-NasmDirectory
{
    if (-not [string]::IsNullOrWhiteSpace($NasmDirectory))
    {
        $candidate = Join-Path $NasmDirectory "nasm.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf)
        {
            return [IO.Path]::GetFullPath($NasmDirectory)
        }

        throw "nasm.exe was not found in '$NasmDirectory'."
    }

    $command = Get-Command nasm -ErrorAction SilentlyContinue
    if ($null -ne $command)
    {
        return [IO.Path]::GetDirectoryName($command.Source)
    }

    $programFilesNasm = Join-Path ${env:ProgramFiles} "NASM"
    if (Test-Path -LiteralPath (Join-Path $programFilesNasm "nasm.exe") -PathType Leaf)
    {
        return $programFilesNasm
    }

    $toolsDirectory = Join-Path $scriptDirectory "tools"
    if (Test-Path -LiteralPath $toolsDirectory -PathType Container)
    {
        $candidate = Get-ChildItem -LiteralPath $toolsDirectory -Recurse -Filter nasm.exe -File |
            Select-Object -First 1
        if ($null -ne $candidate)
        {
            return $candidate.DirectoryName
        }
    }

    Write-Warning "NASM was not found. dav1d will be built without x86 SIMD."
    return ""
}

function Get-Sha256([string]$Path)
{
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-SourceArchive([string]$Url, [string]$Destination, [string]$ExpectedHash)
{
    if (Test-Path -LiteralPath $Destination -PathType Leaf)
    {
        if ((Get-Sha256 $Destination) -eq $ExpectedHash)
        {
            return
        }

        Write-Warning "Checksum mismatch for '$Destination'; downloading it again."
        Remove-Item -LiteralPath $Destination -Force
    }

    if ($SkipDownload)
    {
        throw "Missing source archive: $Destination. Download it manually or omit -SkipDownload."
    }

    Write-Host "Downloading $Url"
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing -TimeoutSec 300

    $actualHash = Get-Sha256 $Destination
    if ($actualHash -ne $ExpectedHash)
    {
        Remove-Item -LiteralPath $Destination -Force
        throw "Checksum mismatch for '$Destination': expected $ExpectedHash, got $actualHash."
    }
}

function Expand-SourceArchive([string]$Archive, [string]$Destination)
{
    if (-not (Test-Path -LiteralPath $Destination -PathType Container))
    {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }

    & tar.exe -xzf $Archive -C $Destination
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not extract '$Archive'."
    }
}

function New-BuildBatch([string]$Path, [string[]]$Lines, [string]$PathPrefix)
{
    $content = @(
        "@echo off",
        "setlocal",
        "call `"$vsPath\VC\Auxiliary\Build\vcvars64.bat`" >nul",
        "set `"PATH=$PathPrefix;%PATH%`""
    ) + $Lines + @("exit /b %errorlevel%")

    Set-Content -LiteralPath $Path -Value $content -Encoding ASCII
}

function Invoke-Batch([string]$Path)
{
    & cmd.exe /c $Path
    if ($LASTEXITCODE -ne 0)
    {
        throw "Command failed with exit code ${LASTEXITCODE}: $Path"
    }
}

if (-not (Test-Path -LiteralPath $cacheDirectory -PathType Container))
{
    New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
}

if ($Clean)
{
    foreach ($directory in @($buildDirectory, $prefixDirectory))
    {
        if (Test-Path -LiteralPath $directory -PathType Container)
        {
            Remove-Item -LiteralPath $directory -Recurse -Force
        }
    }
}

Write-Step "Locating the build toolchain"
$vsPath = Get-VisualStudioPath
$vsRoot = Join-Path $vsPath "Common7\IDE\CommonExtensions\Microsoft\CMake"
$cmakePath = Get-ToolPath "cmake" (Join-Path $vsRoot "CMake\bin\cmake.exe")
$ninjaDirectory = [IO.Path]::GetDirectoryName((Get-ToolPath "ninja" (Join-Path $vsRoot "Ninja\ninja.exe")))
$nasmDirectory = Get-NasmDirectory
$python = Get-ToolPath "python" ""

& $python -m mesonbuild.mesonmain --version | Out-Null
if ($LASTEXITCODE -ne 0)
{
    throw "Meson is not installed for '$python'. Run: python -m pip install --user meson==1.7.2"
}

$pathPrefix = $ninjaDirectory
if (-not [string]::IsNullOrWhiteSpace($nasmDirectory))
{
    $pathPrefix = "$nasmDirectory;$pathPrefix"
}

Write-Host "Visual Studio: $vsPath"
Write-Host "CMake:         $cmakePath"
Write-Host "Ninja:         $(Join-Path $ninjaDirectory 'ninja.exe')"
Write-Host "NASM:          $(if ($nasmDirectory) { $nasmDirectory } else { '<not found>' })"

Write-Step "Preparing sources"
if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container))
{
    New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
}

if ([string]::IsNullOrWhiteSpace($LibavifSource))
{
    Get-SourceArchive "https://github.com/AOMediaCodec/libavif/archive/refs/tags/v$libavifVersion.tar.gz" $libavifArchive $libavifHash
    Expand-SourceArchive $libavifArchive $sourceDirectory
    $LibavifSource = Join-Path $sourceDirectory "libavif-$libavifVersion"
}

if ([string]::IsNullOrWhiteSpace($Dav1dSource))
{
    Get-SourceArchive "https://code.videolan.org/videolan/dav1d/-/archive/$dav1dVersion/dav1d-$dav1dVersion.tar.gz" $dav1dArchive $dav1dHash
    Expand-SourceArchive $dav1dArchive $sourceDirectory
    $Dav1dSource = Join-Path $sourceDirectory "dav1d-$dav1dVersion"
}

if (-not (Test-Path -LiteralPath (Join-Path $LibavifSource "include\avif\avif.h") -PathType Leaf))
{
    throw "libavif source not found at '$LibavifSource'."
}

if (-not (Test-Path -LiteralPath (Join-Path $Dav1dSource "include\dav1d\dav1d.h") -PathType Leaf))
{
    throw "dav1d source not found at '$Dav1dSource'."
}

$asmEnabled = -not [string]::IsNullOrWhiteSpace($nasmDirectory)
$dav1dBuild = Join-Path $buildDirectory "dav1d"
$libavifBuild = Join-Path $buildDirectory "libavif"
$prefixLib = Join-Path $prefixDirectory "lib"
$prefixInclude = Join-Path $prefixDirectory "include"
$dav1dInclude = Join-Path $prefixInclude "dav1d"
$libavifHeader = Join-Path $prefixInclude "avif\avif.h"

if ($Clean)
{
    foreach ($directory in @($dav1dBuild, $libavifBuild))
    {
        if (Test-Path -LiteralPath $directory -PathType Container)
        {
            Remove-Item -LiteralPath $directory -Recurse -Force
        }
    }
}

# Meson and CMake refuse to reuse a configured directory after a version or
# option change. A fresh configure is cheaper than debugging stale state.
foreach ($directory in @($dav1dBuild, $libavifBuild))
{
    if (Test-Path -LiteralPath $directory -PathType Container)
    {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}

Write-Step "Building dav1d $dav1dVersion (asm=$asmEnabled, static CRT)"
$dav1dBatch = Join-Path $cacheDirectory "build-dav1d.cmd"
New-BuildBatch $dav1dBatch @(
    "python -m mesonbuild.mesonmain setup `"$dav1dBuild`" `"$Dav1dSource`" --buildtype=release --default-library=static -Denable_tools=false -Denable_tests=false -Denable_asm=$($asmEnabled.ToString().ToLowerInvariant()) -Db_vscrt=mt --prefix=`"$prefixDirectory`"",
    "if errorlevel 1 exit /b 1",
    "python -m mesonbuild.mesonmain compile -C `"$dav1dBuild`"",
    "if errorlevel 1 exit /b 1",
    "python -m mesonbuild.mesonmain install -C `"$dav1dBuild`""
) $pathPrefix
Invoke-Batch $dav1dBatch

Write-Step "Building libavif $libavifVersion (static)"
$libavifBatch = Join-Path $cacheDirectory "build-libavif.cmd"
New-BuildBatch $libavifBatch @(
    "`"$cmakePath`" -S `"$LibavifSource`" -B `"$libavifBuild`" -G Ninja -DCMAKE_C_COMPILER=cl -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DAVIF_CODEC_DAV1D=SYSTEM -DAVIF_LIBYUV=OFF -DAVIF_LIBXML2=OFF -DAVIF_BUILD_APPS=OFF -DAVIF_BUILD_TESTS=OFF -DDAV1D_INCLUDE_DIR=`"$prefixInclude`" -DDAV1D_LIBRARY=`"$prefixLib\libdav1d.a`" -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DCMAKE_INSTALL_PREFIX=`"$prefixDirectory`"",
    "if errorlevel 1 exit /b 1",
    "`"$cmakePath`" --build `"$libavifBuild`" --config Release",
    "if errorlevel 1 exit /b 1",
    "`"$cmakePath`" --install `"$libavifBuild`" --config Release"
) $pathPrefix
Invoke-Batch $libavifBatch

foreach ($required in @($dav1dInclude, $libavifHeader, (Join-Path $prefixLib "avif.lib"), (Join-Path $prefixLib "libdav1d.a")))
{
    if (-not (Test-Path -LiteralPath $required))
    {
        throw "Expected build output is missing: $required"
    }
}

Write-Step "Building airglance_avif.dll"
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container))
{
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$shimBatch = Join-Path $cacheDirectory "build-shim.cmd"
New-BuildBatch $shimBatch @(
    "cl /nologo /O2 /MT /LD /D_CRT_SECURE_NO_WARNINGS /I `"$prefixInclude`" /I `"$scriptDirectory`" `"$scriptDirectory\airglance_avif.c`" /Fe:`"$outputPath`" /link `"$prefixLib\avif.lib`" `"$prefixLib\libdav1d.a`""
) $pathPrefix
Invoke-Batch $shimBatch

$dll = Get-Item -LiteralPath $outputPath
$buildInfo = @(
    "libavif=$libavifVersion",
    "dav1d=$dav1dVersion",
    "libavif_sha256=$libavifHash",
    "dav1d_sha256=$dav1dHash",
    "dav1d_asm=$($asmEnabled.ToString().ToLowerInvariant())",
    "dll_bytes=$($dll.Length)",
    "built_utc=$([DateTime]::UtcNow.ToString('o'))"
)
Set-Content -LiteralPath (Join-Path $outputDirectory "build-info.txt") -Value $buildInfo -Encoding ASCII

Write-Host ""
Write-Host "Built: $outputPath ($([math]::Round($dll.Length / 1MB, 2)) MiB, asm=$asmEnabled)" -ForegroundColor Green
