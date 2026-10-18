#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-0.1.0}"
ARCH="${DEB_ARCH:-amd64}"
RID="${DOTNET_RID:-linux-x64}"
PACKAGE_NAME="ocphlabremote-agent"
BUILD_DIR="$ROOT_DIR/artifacts/deb"
STAGE_DIR="$BUILD_DIR/${PACKAGE_NAME}_${VERSION}_${ARCH}"

command -v dotnet >/dev/null || { echo "dotnet SDK diperlukan" >&2; exit 1; }
command -v dpkg-deb >/dev/null || { echo "dpkg-deb diperlukan (jalankan di Debian/Ubuntu/WSL)" >&2; exit 1; }

rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR/DEBIAN" "$STAGE_DIR/opt/labremote-agent" "$STAGE_DIR/lib/systemd/system"

dotnet publish "$ROOT_DIR/LabRemote.Agent/LabRemote.Agent.csproj" \
  -c Release -r "$RID" --self-contained true \
  -o "$STAGE_DIR/opt/labremote-agent"

cp "$ROOT_DIR/packaging/debian-agent/appsettings.json" "$STAGE_DIR/opt/labremote-agent/appsettings.json"
cp "$ROOT_DIR/packaging/debian-agent/ocphlabremote-agent.service" "$STAGE_DIR/lib/systemd/system/ocphlabremote-agent.service"
cp "$ROOT_DIR/packaging/debian-agent/postinst" "$STAGE_DIR/DEBIAN/postinst"
cp "$ROOT_DIR/packaging/debian-agent/prerm" "$STAGE_DIR/DEBIAN/prerm"

sed "s/@VERSION@/$VERSION/g; s/@ARCH@/$ARCH/g" \
  "$ROOT_DIR/packaging/debian-agent/control" > "$STAGE_DIR/DEBIAN/control"

chmod 755 "$STAGE_DIR/DEBIAN/postinst" "$STAGE_DIR/DEBIAN/prerm"
chmod 755 "$STAGE_DIR/opt/labremote-agent/LabRemote.Agent"

dpkg-deb --build --root-owner-group "$STAGE_DIR" "$BUILD_DIR/${PACKAGE_NAME}_${VERSION}_${ARCH}.deb"
echo "Package dibuat: $BUILD_DIR/${PACKAGE_NAME}_${VERSION}_${ARCH}.deb"
