#!/bin/zsh
# Builds "AGS Editor.app" (arm64, self-contained .NET, no installation needed) into build/.
# The bundle contains the editor, libagsnative, the AGS command-line tools and the native engine,
# so Build and Run work on any Apple Silicon Mac.
set -euo pipefail

ROOT=${0:A:h:h}
BUILD=$ROOT/build
APP="$BUILD/AGS Editor.app"
PUBLISH=$BUILD/publish/editor
VERSION=$(python3 -c "import json; print(json.load(open('$ROOT/ags/version.json'))['versionFriendly'])")

[[ -x "$BUILD/agstools/agsbuild" ]] || "$ROOT/scripts/build-upstream-tools.sh"
[[ -f "$BUILD/native/libagsnative.dylib" ]] || "$ROOT/scripts/build-native.sh"

echo "Publishing the editor..."
rm -rf "$PUBLISH"
dotnet publish "$ROOT/src/AGS.Editor.Avalonia" -c Release -r osx-arm64 --self-contained true -o "$PUBLISH" -v q

echo "Assembling $APP ..."
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/tools"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
# Tools are symlinks in build/agstools; copy the real files.
cp -L "$BUILD/agstools/"* "$APP/Contents/Resources/tools/"

# Icon from the upstream editor's icon.
ICONSET=$(mktemp -d)/AGSEditor.iconset
mkdir -p "$ICONSET"
sips -s format png "$ROOT/ags/Editor/AGS.Editor/agsico-1.ico" --out "$ICONSET/source.png" >/dev/null
for size in 16 32 128 256 512; do
    sips -z $size $size "$ICONSET/source.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z $double $double "$ICONSET/source.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
rm "$ICONSET/source.png"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AGSEditor.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>AGS Editor</string>
  <key>CFBundleDisplayName</key><string>AGS Editor</string>
  <key>CFBundleIdentifier</key><string>com.ags4mac.editor</string>
  <key>CFBundleExecutable</key><string>AGSEditor</string>
  <key>CFBundleIconFile</key><string>AGSEditor</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSArchitecturePriority</key><array><string>arm64</string></array>
  <key>NSHighResolutionCapable</key><true/>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>AGS Game Project</string>
      <key>CFBundleTypeRole</key><string>Editor</string>
      <key>LSHandlerRank</key><string>Owner</string>
      <key>CFBundleTypeIconFile</key><string>AGSEditor</string>
      <key>LSItemContentTypes</key><array><string>com.adventuregamestudio.agf</string></array>
      <key>CFBundleTypeExtensions</key><array><string>agf</string></array>
    </dict>
  </array>
  <key>UTExportedTypeDeclarations</key>
  <array>
    <dict>
      <key>UTTypeIdentifier</key><string>com.adventuregamestudio.agf</string>
      <key>UTTypeDescription</key><string>AGS Game Project</string>
      <key>UTTypeConformsTo</key><array><string>public.xml</string><string>public.data</string></array>
      <key>UTTypeIconFile</key><string>AGSEditor</string>
      <key>UTTypeTagSpecification</key>
      <dict>
        <key>public.filename-extension</key><array><string>agf</string></array>
      </dict>
    </dict>
  </array>
  <key>NSHumanReadableCopyright</key><string>Adventure Game Studio © Chris Jones and others; macOS port ags4mac</string>
</dict>
</plist>
PLIST

# Ad-hoc signature (required for arm64 code; not notarized).
codesign --force --deep --sign - "$APP" 2>/dev/null
echo "Built $APP ($(du -sh "$APP" | cut -f1))"
