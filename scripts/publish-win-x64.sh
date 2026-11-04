#!/usr/bin/env bash
# Repeatable self-contained win-x64 publish. No admin rights required to run the output.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/artifacts/publish/win-x64}"
mkdir -p "$OUT"
dotnet publish "$ROOT/src/TaskPane.App/TaskPane.App.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishReadyToRun=true \
  -p:PublishSingleFile=false \
  -o "$OUT"
echo "Published to $OUT"
echo "Run TaskPane.exe from that folder on Windows 11 x64 (no installer/signing in v1)."
