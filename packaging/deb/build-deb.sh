#!/bin/bash
# Builds elyra_<version>_amd64.deb from src/Elyra.Desktop.
#
# Usage: packaging/deb/build-deb.sh [version]
# Output: packaging/deb/dist/elyra_<version>_amd64.deb
set -euo pipefail

VERSION="${1:-1.0.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
STAGING="$SCRIPT_DIR/staging"
DIST="$SCRIPT_DIR/dist"

rm -rf "$STAGING" "$DIST"
mkdir -p "$STAGING/DEBIAN" \
         "$STAGING/opt/elyra" \
         "$STAGING/usr/bin" \
         "$STAGING/usr/share/applications" \
         "$DIST"

echo "==> Publishing self-contained linux-x64 build"
dotnet publish "$REPO_ROOT/src/Elyra.Desktop/Elyra.Desktop.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=false \
    -o "$STAGING/opt/elyra"

echo "==> Writing DEBIAN/control"
sed "s/__VERSION__/$VERSION/" "$SCRIPT_DIR/control.template" > "$STAGING/DEBIAN/control"

echo "==> Installing launcher, desktop entry, icons"
install -m 755 "$SCRIPT_DIR/elyra-launcher.sh" "$STAGING/usr/bin/elyra"
install -m 644 "$SCRIPT_DIR/elyra.desktop" "$STAGING/usr/share/applications/elyra.desktop"

for size in 16 24 32 48 64 128 256 512; do
    dir="$STAGING/usr/share/icons/hicolor/${size}x${size}/apps"
    mkdir -p "$dir"
    install -m 644 "$SCRIPT_DIR/icons/elyra-icon-${size}.png" "$dir/elyra.png"
done

# The self-contained publish output includes a native launcher executable
# named after AssemblyName (Elyra.Desktop); make sure it's executable.
chmod 755 "$STAGING/opt/elyra/Elyra.Desktop"

echo "==> Building .deb"
dpkg-deb --root-owner-group --build "$STAGING" "$DIST/elyra_${VERSION}_amd64.deb"

echo "==> Done: $DIST/elyra_${VERSION}_amd64.deb"
