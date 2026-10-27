#!/usr/bin/env bash
# macOS-Abbild (.dmg) mit „Tupfer Designer.app“; läuft nur auf macOS (sips, iconutil, hdiutil).
# Aufruf aus dem Hauptverzeichnis nach „dotnet publish … -r osx-arm64 -o publish/osx-arm64“:
#   deploy/setup/macos.sh 1.2.3 publish/osx-arm64 setups/Tupfer-Designer-macOS-arm64.dmg
set -euo pipefail

version=$1
source=$2
dmg=$3
mkdir -p "$(dirname "$dmg")"
work=$(mktemp -d)
app="$work/dmg/Tupfer Designer.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$source"/. "$app/Contents/MacOS/"
chmod 755 "$app/Contents/MacOS/Tupfer.Designer"

# Programmsymbol aus Tupfer.png
iconset="$work/Tupfer.iconset"
mkdir -p "$iconset"
for size in 16 32 64 128 256; do
    sips -z $size $size Tupfer.Designer/Assets/Tupfer.png --out "$iconset/icon_${size}x${size}.png" > /dev/null
done
for size in 16 32 64 128; do
    cp "$iconset/icon_$((size * 2))x$((size * 2)).png" "$iconset/icon_${size}x${size}@2x.png"
done
rm "$iconset/icon_64x64.png"
iconutil -c icns "$iconset" -o "$app/Contents/Resources/Tupfer.icns"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Tupfer Designer</string>
  <key>CFBundleDisplayName</key><string>Tupfer Designer</string>
  <key>CFBundleIdentifier</key><string>de.tupfer.designer</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>Tupfer.Designer</string>
  <key>CFBundleIconFile</key><string>Tupfer.icns</string>
  <key>CFBundleDevelopmentRegion</key><string>de</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSMicrophoneUsageDescription</key><string>Für eigene Sprachaufnahmen.</string>
</dict>
</plist>
PLIST

# Ohne Apple-Zertifikat nur ad hoc signiert (auf Apple Silicon Pflicht);
# beim ersten Start daher per Rechtsklick → „Öffnen“ bestätigen
codesign --force --deep --sign - "$app"

ln -s /Applications "$work/dmg/Programme"
rm -f "$dmg"
hdiutil create -volname "Tupfer Designer" -srcfolder "$work/dmg" -ov -format UDZO "$dmg"
rm -rf "$work"
