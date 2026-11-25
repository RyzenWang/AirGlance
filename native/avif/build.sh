#!/usr/bin/env bash
#
# Builds libairglance_avif.dylib / .so for macOS and Linux.
#
# Usage:
#   ./native/avif/build.sh [--clean] [rid]
#
#   rid      osx-x64 | osx-arm64 | linux-x64 | linux-arm64
#            Defaults to the host OS/architecture.
#   --clean  Discard the prefix and rebuild everything, ignoring the marker.
#
# The script downloads pinned libavif and dav1d sources into native/avif/.cache,
# verifies their SHA-256, builds both as static libraries with PIC, and links a
# single shared library that the .NET decoder loads.
#
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." >/dev/null 2>&1 && pwd -P)"
CACHE_DIR="$SCRIPT_DIR/.cache"
BUILD_DIR="$CACHE_DIR/build"
SOURCE_DIR="$CACHE_DIR/src"
PREFIX_DIR="$CACHE_DIR/prefix"

LIBAVIF_VERSION="1.4.2"
DAV1D_VERSION="1.5.5"
LIBAVIF_HASH="2B645287340BA5A631D268B551DC2D72BD73AC33335962DD36DCDB6D8366921D"
DAV1D_HASH="76AD07AB121D68407B64A6F50F540C90CA0B066D178BDC9ADBEA14ACF4CE25A9"

HOST_OS=""
HOST_ARCH=""
case "$(uname -s)" in
    Darwin) HOST_OS="osx" ;;
    Linux) HOST_OS="linux" ;;
    *)
        echo "error: this script supports macOS and Linux only." >&2
        exit 2
        ;;
esac

case "$(uname -m)" in
    x86_64 | amd64) HOST_ARCH="x64" ;;
    arm64 | aarch64) HOST_ARCH="arm64" ;;
    *)
        echo "error: unsupported host architecture: $(uname -m)" >&2
        exit 2
        ;;
esac

HOST_RID="$HOST_OS-$HOST_ARCH"

CLEAN=0
RID=""
for arg in "$@"; do
    case "$arg" in
        --clean | -c)
            CLEAN=1
            ;;
        -h | --help)
            echo "usage: ./native/avif/build.sh [--clean] [rid]"
            echo "  rid: osx-x64 | osx-arm64 | linux-x64 | linux-arm64 (default: the host)"
            echo "  --clean: discard the prefix and rebuild everything"
            exit 0
            ;;
        *)
            if [[ -n "$RID" ]]; then
                echo "error: unexpected argument '$arg'." >&2
                exit 2
            fi
            RID="$arg"
            ;;
    esac
done
RID="${RID:-$HOST_RID}"
if [[ "$RID" != "$HOST_OS-x64" && "$RID" != "$HOST_OS-arm64" ]]; then
    echo "error: RID '$RID' does not match host '$HOST_RID'." >&2
    echo "Build macOS packages on macOS and Linux packages on Linux." >&2
    exit 2
fi
if [[ "$RID" != "$HOST_RID" ]]; then
    echo "error: cross-architecture native builds are not supported: host=$HOST_RID rid=$RID." >&2
    exit 2
fi

ARCH="${RID##*-}"
PLATFORM="$HOST_OS"

LIBAVIF_ARCHIVE="$SOURCE_DIR/libavif-$LIBAVIF_VERSION.tar.gz"
DAV1D_ARCHIVE="$SOURCE_DIR/dav1d-$DAV1D_VERSION.tar.gz"

LIBAVIF_SOURCE="${AIRGLANCE_LIBAVIF_SOURCE:-}"
DAV1D_SOURCE="${AIRGLANCE_DAV1D_SOURCE:-}"

OUTPUT_DIR="$SCRIPT_DIR/bin/$RID"
if [[ "$PLATFORM" == "osx" ]]; then
    OUTPUT_NAME="libairglance_avif.dylib"
else
    OUTPUT_NAME="libairglance_avif.so"
fi
OUTPUT_PATH="$OUTPUT_DIR/$OUTPUT_NAME"

die() {
    echo "error: $*" >&2
    exit 1
}

require_tool() {
    if ! command -v "$1" >/dev/null 2>&1; then
        die "$1 was not found. Install it before building the AVIF decoder."
    fi
}

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{ print toupper($1) }'
    else
        shasum -a 256 "$1" | awk '{ print toupper($1) }'
    fi
}

download() {
    local url="$1"
    local destination="$2"
    local expected="$3"

    if [[ -f "$destination" ]]; then
        if [[ "$(sha256_of "$destination")" == "$expected" ]]; then
            return
        fi
        echo "warning: checksum mismatch for '$destination'; downloading again." >&2
        rm -f "$destination"
    fi

    if [[ "${AIRGLANCE_SKIP_DOWNLOAD:-0}" == "1" ]]; then
        die "missing source archive: $destination"
    fi

    echo "Downloading $url"
    if command -v curl >/dev/null 2>&1; then
        curl --fail --location --retry 3 --silent --show-error --output "$destination" "$url"
    elif command -v wget >/dev/null 2>&1; then
        wget --quiet --output-document="$destination" "$url"
    else
        die "curl or wget is required to download the AVIF sources."
    fi

    if [[ "$(sha256_of "$destination")" != "$expected" ]]; then
        rm -f "$destination"
        die "checksum mismatch for '$destination'."
    fi
}

extract() {
    local archive="$1"
    local destination="$2"
    mkdir -p "$destination"
    tar -xzf "$archive" -C "$destination"
}

if ! command -v cmake >/dev/null 2>&1; then
    die "cmake was not found."
fi
if ! command -v ninja >/dev/null 2>&1; then
    die "ninja was not found."
fi

if command -v meson >/dev/null 2>&1; then
    MESON=(meson)
else
    require_tool python3
    if ! python3 -m mesonbuild.mesonmain --version >/dev/null 2>&1; then
        die "Meson was not found. Install it with: python3 -m pip install --user meson==1.7.2"
    fi
    MESON=(python3 -m mesonbuild.mesonmain)
fi

CC="${CC:-cc}"
if ! command -v "$CC" >/dev/null 2>&1; then
    die "C compiler '$CC' was not found."
fi

ASM="true"
if [[ "$ARCH" == "x64" ]] && ! command -v nasm >/dev/null 2>&1; then
    ASM="false"
    echo "warning: NASM was not found; dav1d will be built without x86 SIMD." >&2
fi

# Both shim sources are part of the marker, so editing either one rebuilds.
SHIM_HASH="$(sha256_of "$SCRIPT_DIR/airglance_avif.c"):$(sha256_of "$SCRIPT_DIR/airglance_avif.h")"

is_up_to_date() {
    local info="$OUTPUT_DIR/build-info.txt"
    [[ -f "$OUTPUT_PATH" && -f "$info" ]] || return 1
    grep -qx "libavif=$LIBAVIF_VERSION" "$info" || return 1
    grep -qx "dav1d=$DAV1D_VERSION" "$info" || return 1
    grep -qx "libavif_sha256=$LIBAVIF_HASH" "$info" || return 1
    grep -qx "dav1d_sha256=$DAV1D_HASH" "$info" || return 1
    grep -qx "dav1d_asm=$ASM" "$info" || return 1
    grep -qx "shim_sha256=$SHIM_HASH" "$info" || return 1
    return 0
}

# A restored cache makes the whole download and compile a no-op.
if [[ "$CLEAN" -eq 0 ]] && is_up_to_date; then
    echo "Up to date: $OUTPUT_PATH"
    echo "  libavif $LIBAVIF_VERSION, dav1d $DAV1D_VERSION, dav1d asm=$ASM"
    echo "  Use --clean to force a rebuild."
    exit 0
fi

if [[ "$CLEAN" -eq 1 ]]; then
    rm -rf "$PREFIX_DIR"
fi

mkdir -p "$SOURCE_DIR" "$BUILD_DIR" "$CACHE_DIR"

echo "AirGlance AVIF native build"
echo "  RID:       $RID"
echo "  Compiler:  $("$CC" --version 2>/dev/null | head -n 1)"
echo "  libavif:   $LIBAVIF_VERSION"
echo "  dav1d:     $DAV1D_VERSION"
echo "  dav1d asm: $ASM"
echo ""

if [[ -z "$LIBAVIF_SOURCE" ]]; then
    download "https://github.com/AOMediaCodec/libavif/archive/refs/tags/v$LIBAVIF_VERSION.tar.gz" "$LIBAVIF_ARCHIVE" "$LIBAVIF_HASH"
    extract "$LIBAVIF_ARCHIVE" "$SOURCE_DIR"
    LIBAVIF_SOURCE="$SOURCE_DIR/libavif-$LIBAVIF_VERSION"
fi

if [[ -z "$DAV1D_SOURCE" ]]; then
    download "https://code.videolan.org/videolan/dav1d/-/archive/$DAV1D_VERSION/dav1d-$DAV1D_VERSION.tar.gz" "$DAV1D_ARCHIVE" "$DAV1D_HASH"
    extract "$DAV1D_ARCHIVE" "$SOURCE_DIR"
    DAV1D_SOURCE="$SOURCE_DIR/dav1d-$DAV1D_VERSION"
fi

[[ -f "$LIBAVIF_SOURCE/include/avif/avif.h" ]] || die "libavif source not found at '$LIBAVIF_SOURCE'."
[[ -f "$DAV1D_SOURCE/include/dav1d/dav1d.h" ]] || die "dav1d source not found at '$DAV1D_SOURCE'."

DAV1D_BUILD="$BUILD_DIR/dav1d"
LIBAVIF_BUILD="$BUILD_DIR/libavif"
rm -rf "$DAV1D_BUILD" "$LIBAVIF_BUILD"

echo "Building dav1d (static, PIC)..."
"${MESON[@]}" setup "$DAV1D_BUILD" "$DAV1D_SOURCE" \
    --buildtype=release \
    --default-library=static \
    -Denable_tools=false \
    -Denable_tests=false \
    -Denable_asm="$ASM" \
    -Db_staticpic=true \
    --prefix="$PREFIX_DIR"
"${MESON[@]}" compile -C "$DAV1D_BUILD"
"${MESON[@]}" install -C "$DAV1D_BUILD"

# Debian/Ubuntu Meson installs into lib/<multiarch> when the prefix is
# configured that way. Find the archive instead of assuming lib/.
DAV1D_LIBRARY="$(find "$PREFIX_DIR" -name libdav1d.a -print -quit)"
[[ -n "$DAV1D_LIBRARY" ]] || die "libdav1d.a was not installed under '$PREFIX_DIR'."

echo "Building libavif (static, PIC)..."
cmake -S "$LIBAVIF_SOURCE" -B "$LIBAVIF_BUILD" -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_C_COMPILER="$CC" \
    -DBUILD_SHARED_LIBS=OFF \
    -DAVIF_CODEC_DAV1D=SYSTEM \
    -DAVIF_LIBYUV=OFF \
    -DAVIF_LIBXML2=OFF \
    -DAVIF_BUILD_APPS=OFF \
    -DAVIF_BUILD_TESTS=OFF \
    -DDAV1D_INCLUDE_DIR="$PREFIX_DIR/include" \
    -DDAV1D_LIBRARY="$DAV1D_LIBRARY" \
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
    -DCMAKE_INSTALL_PREFIX="$PREFIX_DIR"
cmake --build "$LIBAVIF_BUILD"
cmake --install "$LIBAVIF_BUILD"

LIBAVIF_LIBRARY="$(find "$PREFIX_DIR" -name libavif.a -print -quit)"
[[ -n "$LIBAVIF_LIBRARY" ]] || die "libavif.a was not installed under '$PREFIX_DIR'."

for required in \
    "$PREFIX_DIR/include/avif/avif.h" \
    "$LIBAVIF_LIBRARY" \
    "$DAV1D_LIBRARY"; do
    [[ -e "$required" ]] || die "expected build output is missing: $required"
done

echo "Building $OUTPUT_NAME..."
mkdir -p "$OUTPUT_DIR"

SHIM_FLAGS=(-O2 -fPIC)
LINK_FLAGS=(-lm)
if [[ "$PLATFORM" == "osx" ]]; then
    SHIM_FLAGS+=(-dynamiclib -Wl,-install_name,@rpath/libairglance_avif.dylib)
else
    SHIM_FLAGS+=(-shared -Wl,-soname,libairglance_avif.so)
    LINK_FLAGS+=(-lpthread)
fi

"$CC" "${SHIM_FLAGS[@]}" \
    -I"$PREFIX_DIR/include" \
    -I"$SCRIPT_DIR" \
    "$SCRIPT_DIR/airglance_avif.c" \
    "$LIBAVIF_LIBRARY" \
    "$DAV1D_LIBRARY" \
    "${LINK_FLAGS[@]}" \
    -o "$OUTPUT_PATH"

if command -v strip >/dev/null 2>&1; then
    if [[ "$PLATFORM" == "osx" ]]; then
        strip -x "$OUTPUT_PATH" 2>/dev/null || true
    else
        strip --strip-unneeded "$OUTPUT_PATH" 2>/dev/null || true
    fi
fi

cat > "$OUTPUT_DIR/build-info.txt" <<EOF
libavif=$LIBAVIF_VERSION
dav1d=$DAV1D_VERSION
libavif_sha256=$LIBAVIF_HASH
dav1d_sha256=$DAV1D_HASH
dav1d_asm=$ASM
shim_sha256=$SHIM_HASH
dll_bytes=$(wc -c < "$OUTPUT_PATH" | tr -d '[:space:]')
built_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF

SIZE_BYTES="$(wc -c < "$OUTPUT_PATH" | tr -d '[:space:]')"
echo ""
echo "Built: $OUTPUT_PATH ($SIZE_BYTES bytes, asm=$ASM)"
