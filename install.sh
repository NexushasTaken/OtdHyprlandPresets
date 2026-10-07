#!/usr/bin/env bash
# Builds the plugin and installs it into the OpenTabletDriver plugin directory.
#   ./install.sh            build + install, then restart the daemon (asks first)
#   ./install.sh --zip      only build dist/OtdHyprlandPresets.zip (for "Plugins > Install plugin..." in the OTD UI)
set -euo pipefail

cd "$(dirname "$0")"
NAME=OtdHyprlandPresets
OUT=out
PLUGIN_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/OpenTabletDriver/Plugins/$NAME"

rm -rf "$OUT"
dotnet build "src/$NAME" -c Release -o "$OUT" --nologo -v quiet

if [[ "${1:-}" == "--zip" ]]; then
    mkdir -p dist
    rm -f "dist/$NAME.zip"
    (cd "$OUT" && zip -q "../dist/$NAME.zip" "$NAME.dll")
    echo "Created dist/$NAME.zip"
    exit 0
fi

mkdir -p "$PLUGIN_DIR"
cp "$OUT/$NAME.dll" "$PLUGIN_DIR/"
echo "Installed to $PLUGIN_DIR"

if systemctl --user is-active --quiet opentabletdriver.service; then
    read -rp "Restart opentabletdriver.service now to load it? [y/N] " answer
    if [[ "$answer" =~ ^[Yy]$ ]]; then
        systemctl --user restart opentabletdriver.service
        echo "Daemon restarted."
    fi
else
    echo "Restart otd-daemon to load the plugin."
fi
