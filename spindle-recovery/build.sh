#!/usr/bin/env bash
# Builds Spindle Recovery for Windows (x64) from Linux:
#   1. cross-compiles the TestDisk/PhotoRec 7.2 engines with MinGW-w64
#   2. builds the .NET Framework 4.8 GUI with the engines embedded
#   3. writes bin/SpindleRecovery.exe and bin/SHA256SUMS
#
# Requirements (Ubuntu 24.04):
#   sudo apt-get install mingw-w64 autoconf automake libtool pkg-config git dotnet-sdk-8.0
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "== 1/3 Recovery engines"
"$HERE/engine/build-engine.sh"

echo "== 2/3 Graphical interface"
OUT="$HERE/gui/bin/publish"
rm -rf "$OUT"
dotnet build "$HERE/gui/SpindleRecovery.csproj" -c Release -o "$OUT" -p:EngineDir="$HERE/engine/out/"

echo "== 3/3 Package"
mkdir -p "$HERE/bin"
cp "$OUT/SpindleRecovery.exe" "$HERE/bin/SpindleRecovery.exe"
(cd "$HERE/bin" && sha256sum SpindleRecovery.exe > SHA256SUMS)
ls -la "$HERE/bin"
cat "$HERE/bin/SHA256SUMS"

if command -v mono >/dev/null && command -v mcs >/dev/null; then
  echo "== Tests"
  "$HERE/tests/run-tests.sh"
fi
echo "Done: $HERE/bin/SpindleRecovery.exe"
