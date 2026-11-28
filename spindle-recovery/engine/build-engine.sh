#!/usr/bin/env bash
# Builds the TestDisk 7.2 / PhotoRec 7.2 recovery engines for 64-bit Windows
# (cross-compiled with MinGW-w64), with the Spindle Recovery GUI hooks applied.
#
# Requirements (Debian/Ubuntu): mingw-w64 autoconf automake libtool pkg-config git
#
# Source: TestDisk 7.2 by Christophe GRENIER (GPL-2.0-or-later).
#   - If TESTDISK_TARBALL points to testdisk-7.2.tar.bz2 (from
#     https://www.cgsecurity.org/testdisk-7.2.tar.bz2) it is used.
#   - Otherwise the official repository tag v7.2 is cloned from
#     https://github.com/cgsecurity/testdisk (same release).
#
# Output: engine/out/testdisk.exe, engine/out/photorec.exe
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="${WORK:-$HERE/.work}"
OUT="$HERE/out"
HOST="${HOST:-x86_64-w64-mingw32}"
JOBS="${JOBS:-$(nproc 2>/dev/null || echo 2)}"

mkdir -p "$WORK" "$OUT"
SRC="$WORK/testdisk-7.2"

if [ ! -d "$SRC" ]; then
  if [ -n "${TESTDISK_TARBALL:-}" ]; then
    echo ">> Extracting $TESTDISK_TARBALL"
    tar -xjf "$TESTDISK_TARBALL" -C "$WORK"
  else
    echo ">> Cloning TestDisk v7.2 from github.com/cgsecurity/testdisk"
    git -c advice.detachedHead=false clone --quiet --depth 1 --branch v7.2 \
      https://github.com/cgsecurity/testdisk "$SRC"
  fi
  echo ">> Applying Spindle Recovery GUI hooks"
  patch -d "$SRC" -p1 < "$HERE/patches/0001-spindle-recovery-gui-hooks.patch"
fi

if [ ! -x "$SRC/configure" ]; then
  echo ">> Generating configure"
  (cd "$SRC" && autoreconf -fi >/dev/null)
fi

BUILD="$WORK/build-$HOST"
mkdir -p "$BUILD"
cd "$BUILD"
if [ ! -f Makefile ]; then
  echo ">> Configuring for $HOST"
  "$SRC/configure" --host="$HOST" \
    --without-ncurses --disable-qt \
    --without-ntfs --without-ntfs3g --without-ewf --without-reiserfs \
    --without-iconv --without-jpeg \
    LDFLAGS="-static -static-libgcc" > configure.log
fi

echo ">> Compiling (make -j$JOBS)"
make -j"$JOBS" > make.log 2>&1 || { tail -40 make.log; exit 1; }

"$HOST-strip" -o "$OUT/testdisk.exe" src/testdisk.exe
"$HOST-strip" -o "$OUT/photorec.exe" src/photorec.exe
ls -la "$OUT"
echo ">> Engine build complete"
