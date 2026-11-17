#!/usr/bin/env bash
#
# Builds the macOS .app zip or the Linux tar.gz for AirGlance.
#
# NativeAOT emits native machine code and cannot be cross-compiled between
# operating-system families. Build a macOS archive on macOS and a Linux archive
# on Linux; the CI release workflow uses native runners for every target.
#
# Usage:
#   ./packaging/scripts/pack.sh [version] <rid>
#
#   version  defaults to 0.1.0
#   rid      osx-arm64 | osx-x64 | linux-x64 | linux-arm64
#
set -euo pipefail

VERSION="${1:-0.1.0}"
RID="${2:-}"

usage() {
    cat >&2 <<'EOF'
Usage: pack.sh [version] <rid>

  version  defaults to 0.1.0
  rid      osx-arm64, osx-x64, linux-x64, or linux-arm64

Examples:
  ./packaging/scripts/pack.sh 0.1.0 osx-arm64
  ./packaging/scripts/pack.sh 0.1.0 linux-x64
EOF
}

if [[ -z "$RID" ]]; then
    echo "error: a runtime identifier is required." >&2
    usage
    exit 2
fi

case "$RID" in
    osx-arm64 | osx-x64)
        PLATFORM="macos"
        ;;
    linux-x64 | linux-arm64)
        PLATFORM="linux"
        ;;
    *)
        echo "error: unsupported runtime identifier '$RID'." >&2
        usage
        exit 2
        ;;
esac

if [[ ! "$VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z.+-]*$ ]]; then
    echo "error: version '$VERSION' contains characters that are not safe for an artifact name." >&2
    exit 2
fi

HOST_OS="$(uname -s)"
if [[ "$PLATFORM" == "macos" && "$HOST_OS" != "Darwin" ]]; then
    echo "error: osx-* packages must be built on macOS because NativeAOT cannot cross-compile from $HOST_OS." >&2
    exit 2
fi

if [[ "$PLATFORM" == "linux" && "$HOST_OS" != "Linux" ]]; then
    echo "error: linux-* packages must be built on Linux because NativeAOT cannot cross-compile from $HOST_OS." >&2
    exit 2
fi

SCRIPT_DIR="$(cd -- "$(dirname -- "$0")" > /dev/null 2>&1 && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." > /dev/null 2>&1 && pwd -P)"
PROJECT="$REPO_ROOT/src/AirGlance.App/AirGlance.App.csproj"
ARTIFACTS_DIR="$REPO_ROOT/artifacts"
ARCH="${RID##*-}"

if [[ ! -f "$PROJECT" ]]; then
    echo "error: project file not found: $PROJECT" >&2
    exit 1
fi

DOTNET=""
if command -v dotnet >/dev/null 2>&1; then
    DOTNET="$(command -v dotnet)"
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
    DOTNET="$HOME/.dotnet/dotnet"
else
    echo "error: dotnet was not found. Install the .NET 10 SDK and make 'dotnet' available on PATH." >&2
    exit 1
fi

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/airglance-pack.XXXXXX")"
cleanup() {
    rm -rf -- "$TMP_ROOT"
}
trap cleanup EXIT

PUBLISH_DIR="$TMP_ROOT/publish"
mkdir -p "$PUBLISH_DIR" "$ARTIFACTS_DIR"

echo "AirGlance package"
echo "  Version: $VERSION"
echo "  RID:     $RID"
echo "  dotnet:  $DOTNET"
echo "  Output:  $ARTIFACTS_DIR"
echo ""

echo "Publishing with NativeAOT..."
"$DOTNET" publish "$PROJECT" \
    -c Release \
    -r "$RID" \
    -p:PublishAot=true \
    -p:StripSymbols=true \
    -p:Version="$VERSION" \
    -o "$PUBLISH_DIR"

if [[ ! -f "$PUBLISH_DIR/AirGlance" ]]; then
    echo "error: dotnet publish completed but did not produce '$PUBLISH_DIR/AirGlance'." >&2
    exit 1
fi

# Strip any symbol files that survived the publish. Release archives must not
# contain PDBs or their platform equivalents.
find "$PUBLISH_DIR" -type f \( -name '*.pdb' -o -name '*.dbg' -o -name '*.mdb' \) -delete

if [[ "$PLATFORM" == "macos" ]]; then
    APP_DIR="$TMP_ROOT/AirGlance.app"
    MACOS_DIR="$APP_DIR/Contents/MacOS"
    RESOURCES_DIR="$APP_DIR/Contents/Resources"
    mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

    cp -R "$PUBLISH_DIR"/. "$MACOS_DIR"/
    cp "$REPO_ROOT/packaging/macos/Info.plist" "$APP_DIR/Contents/Info.plist"

    ICON_SOURCE="$REPO_ROOT/packaging/macos/airglance.icns"
    if [[ ! -f "$ICON_SOURCE" ]]; then
        # The app icon is maintained with the application assets.
        ICON_SOURCE="$REPO_ROOT/src/AirGlance.App/Assets/airglance.icns"
    fi
    if [[ ! -f "$ICON_SOURCE" ]]; then
        echo "error: airglance.icns was not found in packaging/macos or src/AirGlance.App/Assets." >&2
        exit 1
    fi
    cp "$ICON_SOURCE" "$RESOURCES_DIR/airglance.icns"
    for notice in LICENSE THIRD-PARTY-NOTICES.md; do
        if [[ -f "$REPO_ROOT/$notice" ]]; then
            cp "$REPO_ROOT/$notice" "$RESOURCES_DIR/$notice"
        fi
    done

    if [[ ! -x /usr/libexec/PlistBuddy ]]; then
        echo "error: /usr/libexec/PlistBuddy is required to stamp the bundle version." >&2
        exit 1
    fi
    /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" \
        "$APP_DIR/Contents/Info.plist"

    chmod 0755 "$MACOS_DIR/AirGlance"
    if [[ ! -x "$MACOS_DIR/AirGlance" ]]; then
        echo "error: the .app binary is not executable." >&2
        exit 1
    fi

    OUTPUT="$ARTIFACTS_DIR/AirGlance-${VERSION}-osx-${ARCH}.zip"
    rm -f "$OUTPUT"
    # Keep AirGlance.app at the archive root and preserve executable modes.
    ditto -c -k --keepParent "$APP_DIR" "$OUTPUT"
else
    STAGE="$TMP_ROOT/linux"
    mkdir -p "$STAGE/icons"
    cp -R "$PUBLISH_DIR"/. "$STAGE"/
    cp "$REPO_ROOT/packaging/linux/airglance.desktop" "$STAGE/airglance.desktop"
    for notice in LICENSE THIRD-PARTY-NOTICES.md; do
        if [[ -f "$REPO_ROOT/$notice" ]]; then
            cp "$REPO_ROOT/$notice" "$STAGE/$notice"
        fi
    done

    shopt -s nullglob
    LINUX_ICONS=("$REPO_ROOT"/packaging/linux/icons/airglance-*.png)
    shopt -u nullglob
    if (( ${#LINUX_ICONS[@]} == 0 )); then
        echo "error: no Linux icons were found in packaging/linux/icons." >&2
        exit 1
    fi
    cp "${LINUX_ICONS[@]}" "$STAGE/icons/"

    chmod 0755 "$STAGE/AirGlance"
    if [[ ! -x "$STAGE/AirGlance" ]]; then
        echo "error: the Linux binary is not executable." >&2
        exit 1
    fi

    OUTPUT="$ARTIFACTS_DIR/AirGlance-${VERSION}-linux-${ARCH}.tar.gz"
    rm -f "$OUTPUT"
    # Include the published payload, desktop entry, and icons at the tar root.
    # Expand the glob after changing into the staging directory so tar receives
    # staging-relative names rather than names from the caller's working directory.
    (
        cd "$STAGE"
        tar -czf "$OUTPUT" -- *
    )
fi

if [[ ! -f "$OUTPUT" ]]; then
    echo "error: packaging completed but '$OUTPUT' was not created." >&2
    exit 1
fi

SIZE_BYTES="$(wc -c < "$OUTPUT" | tr -d '[:space:]')"
echo ""
echo "Artifact: $OUTPUT"
echo "Size: ${SIZE_BYTES} bytes"
if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$OUTPUT"
else
    shasum -a 256 "$OUTPUT"
fi
