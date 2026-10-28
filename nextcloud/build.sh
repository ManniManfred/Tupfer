#!/usr/bin/env bash
# Baut die Nextcloud-App: veröffentlicht die Web-Version, legt sie als editor/ in die App und packt
# Tupfer-Designer-Nextcloud.tar.gz (Version der App aus Directory.Build.props).
# Aufruf aus dem Hauptverzeichnis: nextcloud/build.sh [Zielordner, Standard nextcloud/dist]
# Braucht das .NET-SDK mit „dotnet workload install wasm-tools“.
set -euo pipefail
cd "$(dirname "$0")/.."
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
out=$(realpath -m "${1:-nextcloud/dist}")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

dotnet publish Tupfer.Designer.Browser -c Release -o "$work/publish"
cp -r nextcloud/tupfer "$work/tupfer"
rm -rf "$work/tupfer/editor"
cp -r "$work/publish/wwwroot" "$work/tupfer/editor"
sed -i "s:<version>.*</version>:<version>$version</version>:" "$work/tupfer/appinfo/info.xml"

mkdir -p "$out"
tar -czf "$out/Tupfer-Designer-Nextcloud.tar.gz" -C "$work" tupfer
echo "Nextcloud-App $version: $out/Tupfer-Designer-Nextcloud.tar.gz"
