#!/usr/bin/env bash
# Builds the dedicated game server as self-contained, single-file, cross-platform
# binaries (no .NET runtime needed to run them). Verified: each builds, and the
# linux binary runs and serves WebSockets. Output: ./dist/
#
# Usage: tools/publish-server.sh
set -euo pipefail

CSPROJ="server/Eliminated.Server/Eliminated.Server.csproj"
OUT="dist"
RIDS=(linux-x64 win-x64 osx-x64 osx-arm64 linux-arm64)

rm -rf "$OUT" && mkdir -p "$OUT"
for RID in "${RIDS[@]}"; do
  echo ">> publishing $RID"
  dotnet publish "$CSPROJ" -c Release -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "build/$RID"
  EXT=""; [[ "$RID" == win-* ]] && EXT=".exe"
  cp "build/$RID/Eliminated.Server$EXT" "$OUT/eliminated-server-$RID$EXT"
done

( cd "$OUT" && sha256sum eliminated-server-* > SHA256SUMS.txt )
echo "Built: $(ls "$OUT")"
echo "Run e.g.:  ./$OUT/eliminated-server-linux-x64 8080   # ws://0.0.0.0:8080/"
