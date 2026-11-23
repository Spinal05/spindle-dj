#!/usr/bin/env bash
# Compiles the engine-output parsers with the tests and runs them under Mono.
# Requirements: mono-complete (mcs + mono)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
mcs -nologo -out:"$OUT/ParserTests.exe" -r:System.Management -r:System.Runtime.Serialization \
  "$HERE/ParserTests.cs" \
  "$HERE/../gui/Engine/Models.cs" "$HERE/../gui/Engine/FileTypes.cs" \
  "$HERE/../gui/Engine/TestDiskParser.cs" "$HERE/../gui/Engine/RecoveryService.cs" \
  "$HERE/../gui/Engine/EngineHost.cs" "$HERE/../gui/Engine/WindowsDevices.cs" \
  "$HERE/../gui/Engine/NtfsReader.cs" "$HERE/../gui/Engine/DiskImager.cs" "$HERE/../gui/Engine/ScanStore.cs"
mono "$OUT/ParserTests.exe" "$HERE/fixtures"
