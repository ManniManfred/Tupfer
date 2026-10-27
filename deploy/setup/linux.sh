#!/usr/bin/env bash
# Linux-Pakete für den Tupfer Designer: .deb (Debian, Ubuntu, Mint …) und AppImage (alle anderen).
# Das AppImage baut appimagetool (Umgebungsvariable APPIMAGETOOL, aus PATH oder es wird geladen).
# Aufruf aus dem Hauptverzeichnis nach „dotnet publish … -r linux-x64 -o publish/linux-x64“:
#   deploy/setup/linux.sh 1.2.3 publish/linux-x64 setups
set -euo pipefail

version=$1
source=$2
output=$3
mkdir -p "$output"
work=$(mktemp -d)

# .deb: Programm unter /opt, Starter unter /usr/bin, Eintrag im Anwendungsmenü
pkg=$work/deb
mkdir -p "$pkg/DEBIAN" "$pkg/opt/tupfer-designer" "$pkg/usr/bin" \
         "$pkg/usr/share/applications" "$pkg/usr/share/icons/hicolor/256x256/apps"
cp -r "$source"/. "$pkg/opt/tupfer-designer/"
chmod 755 "$pkg/opt/tupfer-designer/Tupfer.Designer"
ln -s /opt/tupfer-designer/Tupfer.Designer "$pkg/usr/bin/tupfer-designer"
cp Tupfer.Designer/Assets/Tupfer.png "$pkg/usr/share/icons/hicolor/256x256/apps/tupfer-designer.png"
cat > "$pkg/usr/share/applications/tupfer-designer.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Tupfer Designer
Comment=Eigene Seiten für den tiptoi-Stift gestalten
Exec=/opt/tupfer-designer/Tupfer.Designer %f
Icon=tupfer-designer
Terminal=false
Categories=Graphics;Education;
DESKTOP
cat > "$pkg/DEBIAN/control" <<CONTROL
Package: tupfer-designer
Version: $version
Section: graphics
Priority: optional
Architecture: amd64
Depends: libc6, libfontconfig1, libx11-6, libice6, libsm6
Maintainer: Manfred Roßkamp <ManniManfred@users.noreply.github.com>
Homepage: https://github.com/ManniManfred/Tupfer
Description: Eigene Seiten für den tiptoi-Stift gestalten
 Bilder und Texte setzen, OID-Flächen aufziehen, Skripte und Sprachausgaben
 festlegen, die GME-Datei für den Stift bauen und die Seiten drucken.
CONTROL
dpkg-deb --root-owner-group --build "$pkg" "$output/Tupfer-Designer-Linux-x64.deb"

# AppImage: eine Datei, die ohne Installation auf fast allen Distributionen läuft
appdir=$work/AppDir
mkdir -p "$appdir/usr/lib/tupfer-designer"
cp -r "$source"/. "$appdir/usr/lib/tupfer-designer/"
chmod 755 "$appdir/usr/lib/tupfer-designer/Tupfer.Designer"
cat > "$appdir/AppRun" <<'APPRUN'
#!/bin/sh
here="$(dirname "$(readlink -f "$0")")"
exec "$here/usr/lib/tupfer-designer/Tupfer.Designer" "$@"
APPRUN
chmod 755 "$appdir/AppRun"
cp Tupfer.Designer/Assets/Tupfer.png "$appdir/tupfer-designer.png"
sed 's|^Exec=.*|Exec=Tupfer.Designer %f|' "$pkg/usr/share/applications/tupfer-designer.desktop" > "$appdir/tupfer-designer.desktop"

tool=${APPIMAGETOOL:-$(command -v appimagetool || true)}
if [ -z "$tool" ]; then
    tool=$work/appimagetool
    curl -sSfL -o "$tool" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
    chmod 755 "$tool"
fi
# Ohne FUSE (z. B. in CI oder Containern) entpackt sich appimagetool selbst
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$tool" --no-appstream "$appdir" "$output/Tupfer-Designer-Linux-x64.AppImage"

rm -rf "$work"
