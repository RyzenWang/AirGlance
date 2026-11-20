#Requires -Version 5.1
<#
.SYNOPSIS
    Cross-builds the Windows ARM64 AVIF decoder from an x64 Windows host.

.DESCRIPTION
    Uses LLVM's clang/lld/llvm-ar with the Windows SDK ARM64 UCRT and import
    libraries. Produces native/avif/bin/win-arm64/airglance_avif.dll. This is a
    build-only verification path on an x64 host; run the resulting package on
    Windows ARM64 hardware before treating the runtime as verified.

    The shim is linked /NOENTRY with /NODEFAULTLIB. ucrt.c supplies the small
    memory routines that the UCRT import library does not expose on ARM64.

.PARAMETER LlvmDirectory
    Directory containing clang.exe, lld-link.exe and llvm-ar.exe. Defaults to
    PATH, then C:\Program Files\LLVM\bin.

.PARAMETER Clean
    Removes cached build and install directories first.

.PARAMETER SkipDownload
    Fails instead of downloading missing sources.

.PARAMETER LibavifSource
    An already extracted libavif 1.4.2 source directory.

.PARAMETER Dav1dSource
    An already extracted dav1d 1.5.5 source directory.
#>
[CmdletBinding()]
param(
    [string]$LlvmDirectory = "",

    [switch]$Clean,

    [switch]$SkipDownload,

    [string]$LibavifSource = "",

    [string]$Dav1dSource = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$cacheDirectory = Join-Path $scriptDirectory ".cache\win-arm64"
$buildDirectory = Join-Path $cacheDirectory "build"
$sourceDirectory = Join-Path $cacheDirectory "src"
$prefixDirectory = Join-Path $cacheDirectory "prefix"
$outputDirectory = Join-Path $scriptDirectory "bin\win-arm64"
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
        throw "vswhere.exe was not found. Install Visual Studio with the C++ tools workload."
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

    throw "$Name was not found."
}

function Test-LlvmDirectory([string]$Directory)
{
    if ([string]::IsNullOrWhiteSpace($Directory))
    {
        return $false
    }

    foreach ($name in @("clang.exe", "lld-link.exe", "llvm-ar.exe"))
    {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $name) -PathType Leaf))
        {
            return $false
        }
    }

    return $true
}

function Get-LlvmDirectory
{
    if (-not [string]::IsNullOrWhiteSpace($LlvmDirectory))
    {
        if (Test-LlvmDirectory $LlvmDirectory)
        {
            return [IO.Path]::GetFullPath($LlvmDirectory)
        }

        throw "clang.exe, lld-link.exe and llvm-ar.exe were not all found in '$LlvmDirectory'."
    }

    $clang = Get-Command clang.exe -ErrorAction SilentlyContinue
    if ($null -ne $clang)
    {
        $candidate = [IO.Path]::GetDirectoryName($clang.Source)
        if (Test-LlvmDirectory $candidate)
        {
            return $candidate
        }
    }

    $default = Join-Path ${env:ProgramFiles} "LLVM\bin"
    if (Test-LlvmDirectory $default)
    {
        return $default
    }

    throw "LLVM was not found. Install LLVM (clang, lld, llvm-ar) and pass -LlvmDirectory."
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
    Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing -TimeoutSec 600

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

Write-Step "Locating the cross toolchain"
$vsPath = Get-VisualStudioPath
$llvmDirectory = Get-LlvmDirectory
$clang = Join-Path $llvmDirectory "clang.exe"
$lldLink = Join-Path $llvmDirectory "lld-link.exe"
$llvmAr = Join-Path $llvmDirectory "llvm-ar.exe"

$vsRoot = Join-Path $vsPath "Common7\IDE\CommonExtensions\Microsoft\CMake"
$cmakePath = Get-ToolPath "cmake" (Join-Path $vsRoot "CMake\bin\cmake.exe")
$ninjaDirectory = [IO.Path]::GetDirectoryName((Get-ToolPath "ninja" (Join-Path $vsRoot "Ninja\ninja.exe")))
$python = Get-ToolPath "python" ""

& $python -m mesonbuild.mesonmain --version | Out-Null
if ($LASTEXITCODE -ne 0)
{
    throw "Meson is not installed for '$python'. Run: python -m pip install --user meson==1.7.2"
}

$dumpbin = Get-ChildItem -LiteralPath (Join-Path $vsPath "VC\Tools\MSVC") -Recurse -Filter dumpbin.exe -File |
    Where-Object { $_.FullName -match '\\Hostx64\\x64\\' } |
    Select-Object -First 1
if ($null -eq $dumpbin)
{
    throw "dumpbin.exe was not found in the Visual Studio installation."
}

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10"
$sdkVersion = Get-ChildItem -LiteralPath (Join-Path $sdkRoot "Lib") -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "ucrt\arm64\ucrt.lib") } |
    Sort-Object Name -Descending |
    Select-Object -First 1
if ($null -eq $sdkVersion)
{
    throw "A Windows SDK with ARM64 libraries was not found."
}

$sdkLib = $sdkVersion.FullName
$ucrtLib = Join-Path $sdkLib "ucrt\arm64\ucrt.lib"
$kernel32Lib = Join-Path $sdkLib "um\arm64\kernel32.lib"
$rcPath = Get-ChildItem -LiteralPath (Join-Path $sdkRoot "bin") -Recurse -Filter rc.exe -File |
    Where-Object { $_.FullName -match '\\arm64\\rc\.exe$' } |
    Select-Object -First 1
if ($null -eq $rcPath)
{
    throw "The ARM64 rc.exe was not found in the Windows SDK."
}

Write-Host "Visual Studio: $vsPath"
Write-Host "LLVM:          $llvmDirectory"
Write-Host "CMake:         $cmakePath"
Write-Host "Ninja:         $(Join-Path $ninjaDirectory 'ninja.exe')"
Write-Host "Windows SDK:   $sdkLib"
Write-Host "RC:            $($rcPath.FullName)"

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

$clangWrapper = Join-Path $cacheDirectory "clang-arm64.cmd"
Set-Content -LiteralPath $clangWrapper -Value @(
    "@echo off",
    "`"$clang`" --target=aarch64-pc-windows-msvc %*"
) -Encoding ASCII

$ranlibWrapper = Join-Path $cacheDirectory "llvm-ranlib.cmd"
Set-Content -LiteralPath $ranlibWrapper -Value @(
    "@echo off",
    "`"$llvmAr`" s %*"
) -Encoding ASCII

$forwardSlash = {
    param([string]$Value)
    return $Value.Replace("\", "/")
}

$dav1dBuild = Join-Path $buildDirectory "dav1d"
$libavifBuild = Join-Path $buildDirectory "libavif"
foreach ($directory in @($dav1dBuild, $libavifBuild))
{
    if (Test-Path -LiteralPath $directory -PathType Container)
    {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}

$crossFile = Join-Path $cacheDirectory "cross.ini"
Set-Content -LiteralPath $crossFile -Value @(
    "[binaries]",
    "c = '$(& $forwardSlash $clangWrapper)'",
    "ar = '$(& $forwardSlash $llvmAr)'",
    "",
    "[host_machine]",
    "system = 'windows'",
    "cpu_family = 'aarch64'",
    "cpu = 'arm64'",
    "endian = 'little'",
    "",
    "[properties]",
    "needs_exe_wrapper = true",
    "",
    "[built-in options]",
    "c_args = ['-fno-stack-protector', '-mstack-probe-size=1048576', '-D_CRT_SECURE_NO_WARNINGS']",
    "c_link_args = ['-nostdlib', '-Wl,/NOENTRY', '-Wl,/NODEFAULTLIB']"
) -Encoding ASCII

$toolchainFile = Join-Path $cacheDirectory "toolchain.cmake"
Set-Content -LiteralPath $toolchainFile -Value @(
    "set(CMAKE_SYSTEM_NAME Windows)",
    "set(CMAKE_SYSTEM_PROCESSOR ARM64)",
    "set(CMAKE_C_COMPILER `"$(& $forwardSlash $clangWrapper)`")",
    "set(CMAKE_C_COMPILER_TARGET aarch64-pc-windows-msvc)",
    "set(CMAKE_AR `"$(& $forwardSlash $llvmAr)`")",
    "set(CMAKE_RANLIB `"$(& $forwardSlash $ranlibWrapper)`")",
    "set(CMAKE_LINKER `"$(& $forwardSlash $lldLink)`")",
    "set(CMAKE_RC_COMPILER `"$(& $forwardSlash $rcPath.FullName)`")",
    "set(CMAKE_TRY_COMPILE_TARGET_TYPE STATIC_LIBRARY)",
    "set(CMAKE_C_FLAGS `"-fno-stack-protector -mstack-probe-size=1048576 -D_CRT_SECURE_NO_WARNINGS`")"
) -Encoding ASCII

Write-Step "Building dav1d $dav1dVersion for win-arm64 (no asm)"
$dav1dBatch = Join-Path $cacheDirectory "build-dav1d.cmd"
New-BuildBatch $dav1dBatch @(
    "python -m mesonbuild.mesonmain setup `"$dav1dBuild`" `"$Dav1dSource`" --cross-file=`"$crossFile`" --buildtype=release --default-library=static -Denable_tools=false -Denable_tests=false -Denable_asm=false --prefix=`"$prefixDirectory`"",
    "if errorlevel 1 exit /b 1",
    "python -m mesonbuild.mesonmain compile -C `"$dav1dBuild`"",
    "if errorlevel 1 exit /b 1",
    "python -m mesonbuild.mesonmain install -C `"$dav1dBuild`""
) $ninjaDirectory
Invoke-Batch $dav1dBatch

Write-Step "Building libavif $libavifVersion for win-arm64"
$libavifBatch = Join-Path $cacheDirectory "build-libavif.cmd"
New-BuildBatch $libavifBatch @(
    "`"$cmakePath`" -S `"$LibavifSource`" -B `"$libavifBuild`" -G Ninja -DCMAKE_TOOLCHAIN_FILE=`"$toolchainFile`" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DAVIF_CODEC_DAV1D=SYSTEM -DAVIF_LIBYUV=OFF -DAVIF_LIBXML2=OFF -DAVIF_BUILD_APPS=OFF -DAVIF_BUILD_TESTS=OFF -DDAV1D_INCLUDE_DIR=`"$prefixDirectory\include`" -DDAV1D_LIBRARY=`"$prefixDirectory\lib\libdav1d.a`" -DCMAKE_INSTALL_PREFIX=`"$prefixDirectory`"",
    "if errorlevel 1 exit /b 1",
    "`"$cmakePath`" --build `"$libavifBuild`"",
    "if errorlevel 1 exit /b 1",
    "`"$cmakePath`" --install `"$libavifBuild`""
) $ninjaDirectory
Invoke-Batch $libavifBatch

foreach ($required in @(
    (Join-Path $prefixDirectory "lib\avif.lib"),
    (Join-Path $prefixDirectory "lib\libdav1d.a"),
    (Join-Path $prefixDirectory "include\avif\avif.h")))
{
    if (-not (Test-Path -LiteralPath $required))
    {
        throw "Expected build output is missing: $required"
    }
}

Write-Step "Building airglance_avif.dll for win-arm64"
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container))
{
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$shimObject = Join-Path $cacheDirectory "airglance_avif.obj"
$ucrtObject = Join-Path $cacheDirectory "ucrt.obj"
$compileBatch = Join-Path $cacheDirectory "compile-shim.cmd"
New-BuildBatch $compileBatch @(
    "`"$clang`" --target=aarch64-pc-windows-msvc -O2 -fno-stack-protector -mstack-probe-size=1048576 -D_CRT_SECURE_NO_WARNINGS -I`"$prefixDirectory\include`" -I`"$scriptDirectory`" -c `"$scriptDirectory\airglance_avif.c`" -o `"$shimObject`"",
    "if errorlevel 1 exit /b 1",
    "`"$clang`" --target=aarch64-pc-windows-msvc -O2 -fno-stack-protector -mstack-probe-size=1048576 -fno-builtin -c `"$scriptDirectory\ucrt.c`" -o `"$ucrtObject`""
) $ninjaDirectory
Invoke-Batch $compileBatch

$linkBatch = Join-Path $cacheDirectory "link-shim.cmd"
New-BuildBatch $linkBatch @(
    "`"$lldLink`" /DLL /NOENTRY /MACHINE:ARM64 /OUT:`"$outputPath`" /NODEFAULTLIB `"$shimObject`" `"$ucrtObject`" `"$prefixDirectory\lib\avif.lib`" `"$prefixDirectory\lib\libdav1d.a`" `"$ucrtLib`" `"$kernel32Lib`""
) $ninjaDirectory
Invoke-Batch $linkBatch

$headers = & $dumpbin.FullName /headers $outputPath 2>&1 | Out-String
if ($headers -notmatch "AA64 machine") {
    throw "The produced file is not an ARM64 PE image: $outputPath"
}

$dll = Get-Item -LiteralPath $outputPath
$buildInfo = @(
    "libavif=$libavifVersion",
    "dav1d=$dav1dVersion",
    "libavif_sha256=$libavifHash",
    "dav1d_sha256=$dav1dHash",
    "architecture=arm64",
    "dav1d_asm=false",
    "llvm=$llvmDirectory",
    "dll_bytes=$($dll.Length)",
    "built_utc=$([DateTime]::UtcNow.ToString('o'))"
)
Set-Content -LiteralPath (Join-Path $outputDirectory "build-info.txt") -Value $buildInfo -Encoding ASCII

Write-Host ""
Write-Host "Built: $outputPath ($([math]::Round($dll.Length / 1MB, 2)) MiB, ARM64)" -ForegroundColor Green
