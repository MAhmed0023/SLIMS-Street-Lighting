#!/bin/bash
# Builds PERA-Setup: a portable Windows copy of PERA (self-contained API + standalone web app + node.exe).
# Run from a Linux/macOS build machine with the .NET SDK and Node.js; copy the zip to the Windows PC.
set -e
ROOT=$(cd "$(dirname "$0")" && pwd)
OUT=${1:-$ROOT/PERA-Setup}
NODE_VER=v22.22.0
rm -rf "$OUT"; mkdir -p "$OUT"
echo "[1/4] API (win-x64, self-contained)"
dotnet publish "$ROOT/backend/src/Pera.Api" -c Release -r win-x64 --self-contained true -o "$OUT/api" --nologo
rm -f "$OUT/api/appsettings.Development.json" "$OUT/api/appsettings.Production.json"; rm -rf "$OUT/api/uploads"
echo "[2/4] Web app (standalone)"
(cd "$ROOT/frontend" && npm run build)
mkdir -p "$OUT/web"; cp -r "$ROOT/frontend/.next/standalone/." "$OUT/web/"
# image optimisation is unused; drop the Linux-only native library the build host bundled
rm -rf "$OUT/web/node_modules/@img"
echo "[3/4] Node.js runtime for Windows"
curl -fsSL "https://nodejs.org/dist/$NODE_VER/node-$NODE_VER-win-x64.zip" -o /tmp/node-win.zip
unzip -q -j -o /tmp/node-win.zip "node-$NODE_VER-win-x64/node.exe" -d "$OUT/web"
echo "[4/4] Start files"
cp "$ROOT/deploy/"*.bat "$ROOT/deploy/prepare-settings.ps1" "$ROOT/deploy/README-FIRST.txt" "$OUT/"
mkdir -p "$OUT/uploads"
# Windows line endings for the scripts and the note
for f in "$OUT"/*.bat "$OUT"/*.ps1 "$OUT"/*.txt; do sed -i 's/\r\?$/\r/' "$f"; done
echo "Done: $OUT"
