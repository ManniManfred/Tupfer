#!/usr/bin/env bash
# Linux-Pakete für den Tupfer Designer: .deb (Debian, Ubuntu, Mint …) und .tar.gz (alle anderen).
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

# .tar.gz: zum Entpacken und Starten von Tupfer.Designer
mkdir -p "$work/tar/tupfer-designer"
cp -r "$source"/. "$work/tar/tupfer-designer/"
chmod 755 "$work/tar/tupfer-designer/Tupfer.Designer"
tar -czf "$output/Tupfer-Designer-Linux-x64.tar.gz" -C "$work/tar" tupfer-designer

rm -rf "$work"
