#!/usr/bin/env bash
# Builds SecureGateway.app (and a .dmg) for macOS. Run ON A MAC from the repository root:
#
#   ./scripts/build-mac.sh                 # native arch (Apple Silicon -> arm64, Intel -> x64)
#   ./scripts/build-mac.sh --arch x64      # Intel build
#   ./scripts/build-mac.sh --arch arm64    # Apple Silicon build
#   ./scripts/build-mac.sh --sign "Developer ID Application: Your Company (TEAMID)"
#
# Requirements: .NET 8 SDK, Xcode command line tools (iconutil, codesign, hdiutil), curl.
# v2ray-core for macOS is downloaded from GitHub (release pinned below to match the Windows bundle).
#
# Output: build/mac/SecureGateway.app and build/SecureGateway-<version>-macos-<arch>.dmg
#
# Without --sign the app is ad-hoc signed: it runs on your own Mac, but on other Macs Gatekeeper
# shows "cannot be opened because the developer cannot be verified" -> right-click > Open once,
# or: xattr -d com.apple.quarantine /Applications/SecureGateway.app
# For company-wide distribution use a Developer ID certificate and notarize (see README).

set -euo pipefail

V2RAY_VERSION="${V2RAY_VERSION:-v5.22.0}"
ARCH=""
SIGN_ID=""
while [ $# -gt 0 ]; do
    case "$1" in
        --arch) ARCH="$2"; shift 2 ;;
        --sign) SIGN_ID="$2"; shift 2 ;;
        *) echo "unknown option: $1" >&2; exit 1 ;;
    esac
done

if [ -z "$ARCH" ]; then
    case "$(uname -m)" in arm64) ARCH=arm64 ;; *) ARCH=x64 ;; esac
fi
case "$ARCH" in
    arm64) RID=osx-arm64; V2RAY_ZIP="v2ray-macos-arm64-v8a.zip" ;;
    x64)   RID=osx-x64;   V2RAY_ZIP="v2ray-macos-64.zip" ;;
    *) echo "--arch must be arm64 or x64" >&2; exit 1 ;;
esac

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/src/SecureGateway.Mac/SecureGateway.Mac.csproj"
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJ" | head -1); VERSION="${VERSION:-1.0.0}"
BUILD="$ROOT/build/mac"
PUBLISH="$BUILD/publish-$RID"
APP="$BUILD/SecureGateway.app"
BUNDLE_ID="com.fourthzodiac.securegateway"

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }

command -v dotnet >/dev/null || die ".NET SDK not found — https://dotnet.microsoft.com/download/dotnet/8.0"

# ---- 1. publish ---------------------------------------------------------------------------
log "Publishing for $RID"
rm -rf "$PUBLISH"
dotnet publish "$PROJ" -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=false -p:DebugType=none -o "$PUBLISH" --nologo -v:q
[ -x "$PUBLISH/SecureGateway" ] || die "publish did not produce SecureGateway executable"

# ---- 2. v2ray-core -------------------------------------------------------------------------
V2RAY_CACHE="$BUILD/v2ray-$V2RAY_VERSION-$ARCH"
if [ ! -x "$V2RAY_CACHE/v2ray" ]; then
    log "Downloading v2ray-core $V2RAY_VERSION ($V2RAY_ZIP)"
    mkdir -p "$V2RAY_CACHE"
    curl -fsSL -o "$V2RAY_CACHE/v2ray.zip" \
        "https://github.com/v2fly/v2ray-core/releases/download/$V2RAY_VERSION/$V2RAY_ZIP" \
        || die "download failed"
    (cd "$V2RAY_CACHE" && unzip -qo v2ray.zip && rm -f v2ray.zip)
    chmod +x "$V2RAY_CACHE/v2ray"
fi

# ---- 3. app bundle -------------------------------------------------------------------------
log "Assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
mkdir -p "$APP/Contents/MacOS/v2ray-core"
cp "$V2RAY_CACHE/v2ray" "$V2RAY_CACHE"/*.dat "$APP/Contents/MacOS/v2ray-core/"
chmod +x "$APP/Contents/MacOS/SecureGateway" "$APP/Contents/MacOS/v2ray-core/v2ray"

# Icon: build .icns from the 1024px PNG in the repo.
ICONSET="$BUILD/AppIcon.iconset"
rm -rf "$ICONSET"; mkdir -p "$ICONSET"
SRC_PNG="$ROOT/src/SecureGateway.Mac/Assets/icon_1024.png"
for s in 16 32 128 256 512; do
    sips -z $s $s "$SRC_PNG" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    d=$((s*2)); sips -z $d $d "$SRC_PNG" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>SecureGateway</string>
    <key>CFBundleDisplayName</key><string>SecureGateway</string>
    <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
    <key>CFBundleVersion</key><string>$VERSION</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleExecutable</key><string>SecureGateway</string>
    <key>CFBundleIconFile</key><string>AppIcon</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSHumanReadableCopyright</key><string>Fourth Zodiac</string>
    <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
</dict>
</plist>
PLIST

# ---- 4. sign ------------------------------------------------------------------------------
if [ -n "$SIGN_ID" ]; then
    log "Signing with '$SIGN_ID' (hardened runtime)"
    cat > "$BUILD/entitlements.plist" <<'ENT'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
    <key>com.apple.security.cs.allow-jit</key><true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
    <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
ENT
    # Sign nested binaries first, then the bundle.
    find "$APP/Contents/MacOS" -type f \( -perm -u+x -o -name '*.dylib' \) -print0 \
        | xargs -0 -n1 codesign --force --options runtime --timestamp --sign "$SIGN_ID" \
            --entitlements "$BUILD/entitlements.plist"
    codesign --force --options runtime --timestamp --sign "$SIGN_ID" \
        --entitlements "$BUILD/entitlements.plist" "$APP"
else
    log "Ad-hoc signing (required to launch on Apple Silicon; not trusted on other Macs)"
    codesign --force --deep --sign - "$APP"
fi
codesign --verify --deep --strict "$APP" && log "Signature verified"

# ---- 5. dmg ------------------------------------------------------------------------------
DMG="$ROOT/build/SecureGateway-$VERSION-macos-$ARCH.dmg"
log "Creating $DMG"
STAGE="$BUILD/dmg-stage"; rm -rf "$STAGE"; mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
rm -f "$DMG"
hdiutil create -volname "SecureGateway" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
[ -n "$SIGN_ID" ] && codesign --sign "$SIGN_ID" --timestamp "$DMG"

echo
log "Done."
echo "  App : $APP"
echo "  DMG : $DMG"
if [ -n "$SIGN_ID" ]; then
    echo
    echo "  To notarize (needed so other Macs open it without warnings):"
    echo "    xcrun notarytool submit \"$DMG\" --apple-id you@company.com --team-id TEAMID --password app-specific-pw --wait"
    echo "    xcrun stapler staple \"$DMG\""
fi
