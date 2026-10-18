#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-latest}"
OWNER="Ocph23"
REPOSITORY="OcphLabRemote"
ASSET="ocphlabremote-controller-linux-x64.tar.gz"
INSTALL_DIR="/opt/ocphlabremote/controller"

if [[ "$VERSION" == "latest" ]]; then
  VERSION="$(curl -fsSL "https://api.github.com/repos/$OWNER/$REPOSITORY/releases/latest" \
    | sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)"
fi

if [[ -z "$VERSION" ]]; then
  echo "Gagal menentukan versi release." >&2
  exit 1
fi

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT
curl -fL "https://github.com/$OWNER/$REPOSITORY/releases/download/$VERSION/$ASSET" \
  -o "$tmp_dir/$ASSET"

sudo mkdir -p "$INSTALL_DIR"
sudo tar -xzf "$tmp_dir/$ASSET" -C "$INSTALL_DIR"
sudo chmod +x "$INSTALL_DIR/LabRemote"
sudo ln -sfn "$INSTALL_DIR/LabRemote" /usr/local/bin/ocphlabremote

echo "Controller terpasang di $INSTALL_DIR"
echo "Jalankan dengan: ocphlabremote"
echo "Set token sebelum menjalankan: export LAB_REMOTE_AGENT_TOKEN='TOKEN_YANG_SAMA'"
